using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Harvests the overworld CHEST topology into wtg_chests.json — the data behind the
/// "crowns" option (chests as AP locations; crown-door-gated chests also get a key).
///
/// Three collections, all accumulated/deduped across passes (chests + crown doors
/// stream in as you walk near them, so several partial walks add up):
///   chests[]      — every <c>Chest</c> / <c>OverworldIslandChest</c>: its
///                   OverworldID.ID (stable authored key = the AP location id),
///                   unlocked state at capture, world pos, parent chain (room hint).
///   crown_doors[] — every <c>OverworldButton2D</c> whose object name contains
///                   "Crown" (the "Main Crown Door" / "Crowns Only" variants): its
///                   OverworldID.ID (= the key/trigger the mod will block via
///                   canOpen=false), name, canOpen at capture, world pos. This is
///                   how we CLASSIFY a chest as gated: a chest sitting behind one of
///                   these doors is keyed; a chest with no crown door in front is
///                   free (location only, no key).
///   areas[]       — <c>PlateInfoManager</c> Name(AreaIDEnum) -> chestID: the
///                   authoritative area<->ChestID mapping (e.g. CARS_03B ->
///                   CHEST_CARS), so build_levels.py can attach each chest to a
///                   chamber without guessing from enum names.
///
/// Read-only; runs periodically from Mod.OnUpdate (behind DumpersEnabled).
/// Classification of gated-vs-free is done OFFLINE from this data (proximity of a
/// chest to a crown door + the areas map), the project's usual dump-then-classify.
/// </summary>
public static class ChestDumper
{
    private class ChestRec
    {
        public string campaign;           // episode tag (Main/Olympics/Snow/...)
        public string id;                 // OverworldID.ID (AP location key)
        public string type;               // "Chest" | "OverworldIslandChest"
        public bool unlocked;             // IsUnlocked() at capture time
        public float[] pos;               // world position
        public List<string> parents = new(); // up to 3 parent object names (room hint)
    }

    private class DoorRec
    {
        public string campaign;           // episode tag (Main/Olympics/Snow/...)
        public string id;                 // OverworldID.ID (the block/key trigger)
        public string name;               // GameObject name (which crown-door variant)
        public bool can_open;             // canOpen at capture time
        public float[] pos;
    }

    private class AreaRec
    {
        public string campaign;           // episode tag (Main/Olympics/Snow/...)
        public string area;               // PlateInfoManager.Name (AreaIDEnum)
        public string chest;              // PlateInfoManager.chestID (ChestID)
    }

    private class ChestsFile
    {
        public Dictionary<string, ChestRec> chests = new();
        public Dictionary<string, DoorRec> crown_doors = new();
        public Dictionary<string, AreaRec> areas = new();
    }

    private static readonly Dictionary<string, ChestRec> Chests = new();
    private static readonly Dictionary<string, DoorRec> Doors = new();
    private static readonly Dictionary<string, AreaRec> Areas = new();
    private static bool _loaded;
    private static int _runs;

    private static string OutPath =>
        Path.Combine(MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_chests.json");

    public static void Dump()
    {
        try
        {
            LoadOnce();
            string campaign = CampaignInfo.Current();
            bool changed = false;

            // --- Chests (Chest + island chests) -------------------------------
            var chests = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.Chest>();
            for (int i = 0; i < chests.Length; i++)
            {
                var c = chests[i];
                if (c == null) continue;

                string id = ReadId(c.id);
                if (string.IsNullOrEmpty(id)) id = SafeName(c) + "#noid";
                string chestKey = campaign + "::" + id;

                if (!Chests.TryGetValue(chestKey, out var rec))
                {
                    rec = new ChestRec { id = id, campaign = campaign, type = "Chest" };
                    Chests[chestKey] = rec;
                    changed = true;
                }
                bool unlocked = false;
                try { unlocked = c.IsUnlocked(); } catch { }
                if (rec.unlocked != unlocked) { rec.unlocked = unlocked; changed = true; }
                if (rec.pos == null) { rec.pos = Pos(c); rec.parents = Parents(c); changed = true; }
            }

            var islands = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldIslandChest>();
            for (int i = 0; i < islands.Length; i++)
            {
                var ic = islands[i];
                var c = ic != null ? ic.ChestOnIsland : null;
                if (c == null) continue;
                string id = ReadId(c.id);
                if (string.IsNullOrEmpty(id)) continue;
                string chestKey = campaign + "::" + id;
                if (Chests.TryGetValue(chestKey, out var rec)) { if (rec.type != "OverworldIslandChest") { rec.type = "OverworldIslandChest"; changed = true; } }
                else { Chests[chestKey] = new ChestRec { id = id, campaign = campaign, type = "OverworldIslandChest", pos = Pos(c), parents = Parents(c) }; changed = true; }
            }

            // --- Crown doors (OverworldButton2D named *Crown*) ----------------
            var btns = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldButton2D>();
            for (int i = 0; i < btns.Length; i++)
            {
                var b = btns[i];
                if (b == null) continue;
                string name = SafeName(b);
                if (name == null || name.IndexOf("Crown", StringComparison.OrdinalIgnoreCase) < 0) continue;

                string id = null;
                try { var oid = b.gameObject.GetComponent<Il2Cpp.OverworldID>(); if (oid != null) id = oid.ID; }
                catch { }
                string key = campaign + "::" + (!string.IsNullOrEmpty(id) ? id : name);

                if (!Doors.TryGetValue(key, out var dr))
                {
                    dr = new DoorRec { id = id, name = name, campaign = campaign, pos = Pos(b) };
                    Doors[key] = dr;
                    changed = true;
                }
                try { if (dr.can_open != b.canOpen) { dr.can_open = b.canOpen; changed = true; } } catch { }
            }

            // --- PlateInfoManager area -> ChestID -----------------------------
            var plates = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.PlateInfoManager>();
            for (int i = 0; i < plates.Length; i++)
            {
                var p = plates[i];
                if (p == null) continue;
                string area, chest;
                try { area = p.Name.ToString(); chest = p.chestID.ToString(); }
                catch { continue; }
                if (string.IsNullOrEmpty(area)) continue;
                string areaKey = campaign + "::" + area;
                if (!Areas.ContainsKey(areaKey)) { Areas[areaKey] = new AreaRec { area = area, chest = chest, campaign = campaign }; changed = true; }
            }

            if (changed)
            {
                Write();
                Plugin.Log.LogInfo($"[CHESTS] {Chests.Count} chests, {Doors.Count} crown doors, {Areas.Count} area->chest (active={campaign}) -> {OutPath}");
            }
            else if (++_runs % 4 == 0)
            {
                Plugin.Log.LogInfo($"[CHESTS] heartbeat: {chests.Length} chests loaded, {Chests.Count} captured, {Doors.Count} crown doors (active campaign={campaign})");
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"ChestDumper: {e}"); }
    }

    private static string ReadId(Il2Cpp.OverworldID oid)
    { try { return oid != null ? oid.ID : null; } catch { return null; } }

    private static string SafeName(UnityEngine.Component c)
    { try { return c != null ? c.gameObject.name : null; } catch { return null; } }

    private static float[] Pos(UnityEngine.Component c)
    {
        try { var p = c.transform.position; return new[] { p.x, p.y, p.z }; }
        catch { return null; }
    }

    private static List<string> Parents(UnityEngine.Component c)
    {
        var list = new List<string>();
        try
        {
            var t = c.transform != null ? c.transform.parent : null;
            for (int i = 0; i < 3 && t != null; i++) { list.Add(t.name); t = t.parent; }
        }
        catch { }
        return list;
    }

    private static void LoadOnce()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(OutPath)) return;
            var root = JsonConvert.DeserializeObject<ChestsFile>(File.ReadAllText(OutPath));
            // Migrate legacy (un-tagged) records to Main, re-keying to campaign::<id/area>.
            if (root?.chests != null)
                foreach (var kv in root.chests)
                {
                    var rec = kv.Value;
                    if (string.IsNullOrEmpty(rec.campaign)) rec.campaign = "Main";
                    string key = kv.Key.Contains("::") ? kv.Key : rec.campaign + "::" + (rec.id ?? kv.Key);
                    Chests[key] = rec;
                }
            if (root?.crown_doors != null)
                foreach (var kv in root.crown_doors)
                {
                    var rec = kv.Value;
                    if (string.IsNullOrEmpty(rec.campaign)) rec.campaign = "Main";
                    string bare = !string.IsNullOrEmpty(rec.id) ? rec.id : rec.name;
                    string key = kv.Key.Contains("::") ? kv.Key : rec.campaign + "::" + (bare ?? kv.Key);
                    Doors[key] = rec;
                }
            if (root?.areas != null)
                foreach (var kv in root.areas)
                {
                    var rec = kv.Value;
                    if (string.IsNullOrEmpty(rec.campaign)) rec.campaign = "Main";
                    string key = kv.Key.Contains("::") ? kv.Key : rec.campaign + "::" + (rec.area ?? kv.Key);
                    Areas[key] = rec;
                }
            Plugin.Log.LogInfo($"[CHESTS] loaded {Chests.Count} chests, {Doors.Count} crown doors from existing {OutPath}");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"ChestDumper.LoadOnce: {e}"); }
    }

    private static void Write()
    {
        var file = new ChestsFile { chests = Chests, crown_doors = Doors, areas = Areas };
        File.WriteAllText(OutPath, JsonConvert.SerializeObject(file, Formatting.Indented));
    }
}
