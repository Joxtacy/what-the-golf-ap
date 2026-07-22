using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Captures the REAL chamber topology into wtg_doors.json.
///
/// The game gates progress with "computer" doors (OverworldMainDoorRobot); each
/// door has plates (OverworldMainDoorPlate). Every plate's PlateInfoManager holds
/// an AreaIDEnum (e.g. PLATFORMERS_08A -> chamber 08, sub-area A) and the
/// List&lt;LevelData&gt; of the holes behind it. NOTE: several physical plates can
/// share one sub-area (Western = 4 plates, all WETERN_01), so we accumulate levels
/// per SUB-AREA (union), not per plate.
///
/// We enumerate the PLATES directly (FindObjectsOfTypeAll) rather than the doors,
/// because plates/levels stream in as you approach a region — enumerating the leaf
/// objects catches more in one pass. We also ACCUMULATE across sessions (load the
/// existing file on first run) so several partial overworld walks add up.
///
/// Output: { "areas": { "EASY2D_09A": {area_id, chamber, theme, boss_name,
/// levels[...] }, ... } }. Read-only; runs periodically from Mod.OnUpdate.
/// </summary>
public static class DoorDumper
{
    private class AreaRec
    {
        public string campaign;                   // episode tag (Main/Olympics/Snow/...)
        public string area;                       // bare PlateInfoManager enum name (key is campaign-prefixed)
        public int area_id;
        public int chamber = -1;
        public string theme;
        public string boss_name;                 // the door this sub-area belongs to
        public SortedSet<string> levels = new();  // union of hole scenes (dedup, stable order)
    }

    // Per computer-door (OverworldMainDoorRobot). Keyed by bossLevelName so it
    // accumulates/dedups across passes. bossLevelID is the REAL identifier the
    // door loads (map it to a scene via wtg_levels.json id->scene) — the missing
    // link between a computer door and the boss hole it actually gates.
    private class DoorRec
    {
        public string campaign;           // episode tag (Main/Olympics/Snow/...)
        public string boss_level_id;      // OverworldMainDoorRobot.bossLevelID
        public string boss_level_name;    // OverworldMainDoorRobot.bossLevelName
        public int chamber = -1;          // from the door's plates' sub-areas
        public SortedSet<string> plate_areas = new();  // sub-area enum names on this door
    }

    private class DoorsFile
    {
        public Dictionary<string, AreaRec> areas = new();
        public Dictionary<string, DoorRec> doors = new();
    }

    private static readonly Dictionary<string, AreaRec> Areas = new();
    private static readonly Dictionary<string, DoorRec> Doors = new();
    private static bool _loaded;
    private static int _runs;

    private static string OutPath =>
        Path.Combine(MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_doors.json");

    public static void Dump()
    {
        try
        {
            LoadOnce();
            string campaign = CampaignInfo.Current();

            var plates = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldMainDoorPlate>();
            bool changed = false;

            for (int i = 0; i < plates.Length; i++)
            {
                var p = plates[i];
                if (p == null) continue;

                var info = p.plateInfo;
                if (info == null) continue;

                string areaName;
                int areaId;
                try { areaId = (int)info.Name; areaName = info.Name.ToString(); }
                catch { continue; }
                if (string.IsNullOrEmpty(areaName)) continue;

                string areaKey = campaign + "::" + areaName;
                if (!Areas.TryGetValue(areaKey, out var rec))
                {
                    rec = new AreaRec { area_id = areaId, campaign = campaign, area = areaName };
                    ParseName(areaName, out rec.chamber, out rec.theme);
                    Areas[areaKey] = rec;
                    changed = true;
                }

                // door (computer) this plate belongs to
                try
                {
                    var comp = p.computer;
                    string boss = comp != null ? comp.bossLevelName : null;
                    if (!string.IsNullOrEmpty(boss) && rec.boss_name != boss) { rec.boss_name = boss; changed = true; }
                }
                catch { }

                // union in the sub-area's hole scenes (populated once LevelData load)
                try
                {
                    var levels = info.levels;
                    if (levels != null)
                        for (int k = 0; k < levels.Count; k++)
                        {
                            var ld = levels[k];
                            string scene = ld != null ? ld.SceneName : null;
                            if (!string.IsNullOrEmpty(scene) && rec.levels.Add(scene)) changed = true;
                        }
                }
                catch { }
            }

            // Per computer-door records (bossLevelID is the key new datum).
            var robots = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldMainDoorRobot>();
            for (int i = 0; i < robots.Length; i++)
            {
                var r = robots[i];
                if (r == null) continue;
                string bid, bname;
                try { bid = r.bossLevelID; bname = r.bossLevelName; }
                catch { continue; }
                string bareKey = !string.IsNullOrEmpty(bname) ? bname
                           : (!string.IsNullOrEmpty(bid) ? bid : null);
                if (bareKey == null) continue;
                string key = campaign + "::" + bareKey;

                if (!Doors.TryGetValue(key, out var dr))
                {
                    dr = new DoorRec { boss_level_id = bid, boss_level_name = bname, campaign = campaign };
                    Doors[key] = dr;
                    changed = true;
                }
                if (dr.boss_level_id != bid && !string.IsNullOrEmpty(bid)) { dr.boss_level_id = bid; changed = true; }

                try
                {
                    var dplates = r.plates;
                    if (dplates != null)
                        for (int j = 0; j < dplates.Count; j++)
                        {
                            var info = dplates[j] != null ? dplates[j].plateInfo : null;
                            if (info == null) continue;
                            string an;
                            try { an = info.Name.ToString(); } catch { continue; }
                            if (string.IsNullOrEmpty(an)) continue;
                            if (dr.plate_areas.Add(an)) changed = true;
                            ParseName(an, out int c, out _);
                            if (c >= 0 && dr.chamber != c) { dr.chamber = c; changed = true; }
                        }
                }
                catch { }
            }

            if (changed)
            {
                Write();
                int totalLevels = Areas.Values.SelectMany(a => a.levels).Distinct().Count();
                int withLevels = Areas.Values.Count(a => a.levels.Count > 0);
                Plugin.Log.LogInfo($"[DOORS] {Areas.Count} sub-areas ({withLevels} w/levels), {totalLevels} holes, {Doors.Count} doors (active={campaign}) -> {OutPath}");
            }
            else if (++_runs % 4 == 0)
            {
                Plugin.Log.LogInfo($"[DOORS] heartbeat: {plates.Length} plates loaded, {Areas.Count} sub-areas captured (active campaign={campaign})");
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"DoorDumper: {e}"); }
    }

    private static void LoadOnce()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(OutPath)) return;
            var root = JsonConvert.DeserializeObject<DoorsFile>(File.ReadAllText(OutPath));
            if (root?.areas != null)
                foreach (var kv in root.areas)
                {
                    var rec = kv.Value;
                    rec.levels ??= new SortedSet<string>();
                    // Migrate legacy (un-tagged) records to Main, re-keying to campaign::area.
                    if (string.IsNullOrEmpty(rec.campaign)) rec.campaign = "Main";
                    if (string.IsNullOrEmpty(rec.area)) rec.area = kv.Key;
                    string key = kv.Key.Contains("::") ? kv.Key : rec.campaign + "::" + rec.area;
                    Areas[key] = rec;
                }
            if (root?.doors != null)
                foreach (var kv in root.doors)
                {
                    var rec = kv.Value;
                    rec.plate_areas ??= new SortedSet<string>();
                    if (string.IsNullOrEmpty(rec.campaign)) rec.campaign = "Main";
                    string bare = !string.IsNullOrEmpty(rec.boss_level_name) ? rec.boss_level_name : rec.boss_level_id;
                    string key = kv.Key.Contains("::") ? kv.Key : rec.campaign + "::" + (bare ?? kv.Key);
                    Doors[key] = rec;
                }
            Plugin.Log.LogInfo($"[DOORS] loaded {Areas.Count} sub-areas, {Doors.Count} doors from existing {OutPath}");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"DoorDumper.LoadOnce: {e}"); }
    }

    // "PLATFORMERS_08A" -> chamber 8, theme "PLATFORMERS"; "WATER_02" -> 2, "WATER".
    private static void ParseName(string name, out int chamber, out string theme)
    {
        chamber = -1; theme = name;
        int us = name.LastIndexOf('_');
        if (us < 0) return;
        theme = name.Substring(0, us);
        string tail = name.Substring(us + 1);
        string digits = new string(tail.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out int c)) chamber = c;
    }

    private static void Write()
    {
        var file = new DoorsFile { areas = Areas, doors = Doors };
        File.WriteAllText(OutPath, JsonConvert.SerializeObject(file, Formatting.Indented));
    }
}
