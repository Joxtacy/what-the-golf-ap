using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace WtgArchipelago.Mapping;

/// <summary>
/// DEV PROBE (behind <c>Mod.PortalProbeEnabled</c>) for the "open the hub portal
/// early" QoL feature. Captures the overworld's SHORTCUT-PORTAL topology so we can
/// pick exactly which portal to keep open.
///
/// The game wires each shortcut portal with a <c>ShortCutPortalBoxEnableOnComputer</c>:
/// a box holding a <c>computer</c> (an <c>OverworldMainDoorRobot</c> boss door) and a
/// <c>toEnableOnComputerOpen</c> (<c>SimplePortal</c>). The portal only turns on once
/// that computer opens — which is why you can't warp back to the hub until you've
/// cleared a chamber. This probe dumps every such box (its portal + which computer
/// gates it) PLUS every standalone <c>SimplePortal</c>, with world positions + parent
/// chains, so the chamber-10 (post-intro) portal can be identified by location.
///
/// Read-only; write goes to wtg_portals.json. Runs periodically only while the dev
/// toggle is on (portals stream in as you walk near them, so several passes accumulate
/// — same dump-then-classify pattern as ChestDumper). Flip OFF before committing.
/// </summary>
public static class ShortcutPortalDumper
{
    private class BoxRec
    {
        public string box;                       // box GameObject name
        public float[] pos;                      // box world position
        public List<string> parents = new();     // parent chain (room hint)

        public string computer;                  // OverworldMainDoorRobot name
        public string computer_boss_id;          // its bossLevelID
        public string computer_boss_name;        // its bossLevelName
        public float[] computer_pos;

        public string portal;                    // toEnableOnComputerOpen name
        public string portal_id;                 // portal's OverworldID.ID (best key)
        public string portal_linked;             // LinkedPortal name (where it leads)
        public float[] portal_pos;
        public bool portal_active;               // activeInHierarchy at capture
        public bool portal_enabled;              // MonoBehaviour enabled
        public bool portal_hide_until_used;      // hideUntilUsedFromOtherSide
    }

    private class PortalRec
    {
        public string name;                      // SimplePortal GameObject name
        public string id;                        // OverworldID.ID if any
        public string linked;                    // LinkedPortal name
        public float[] pos;
        public List<string> parents = new();
        public bool active;
        public bool enabled;
        public bool hide_until_used;
    }

    private class PortalsFile
    {
        public Dictionary<string, BoxRec> boxes = new();
        public Dictionary<string, PortalRec> portals = new();
    }

    private static readonly Dictionary<string, BoxRec> Boxes = new();
    private static readonly Dictionary<string, PortalRec> Portals = new();
    private static bool _loaded;
    private static int _runs;

    private static string OutPath =>
        Path.Combine(MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_portals.json");

    public static void Dump()
    {
        try
        {
            LoadOnce();
            bool changed = false;

            // --- Shortcut-portal boxes (the gated hub portals) ------------------
            var boxes = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.ShortCutPortalBoxEnableOnComputer>();
            for (int i = 0; i < boxes.Length; i++)
            {
                var box = boxes[i];
                if (box == null) continue;

                string key = SafeName(box) + "@" + PosKey(box);
                if (Boxes.ContainsKey(key)) continue;   // one capture per box instance

                var rec = new BoxRec { box = SafeName(box), pos = Pos(box), parents = Parents(box) };

                try
                {
                    var comp = box.computer;
                    if (comp != null)
                    {
                        rec.computer = SafeName(comp);
                        try { rec.computer_boss_id = comp.bossLevelID; } catch { }
                        try { rec.computer_boss_name = comp.bossLevelName; } catch { }
                        rec.computer_pos = Pos(comp);
                    }
                }
                catch { }

                try
                {
                    var p = box.toEnableOnComputerOpen;
                    if (p != null)
                    {
                        rec.portal = SafeName(p);
                        rec.portal_id = ReadId(p);
                        rec.portal_pos = Pos(p);
                        try { rec.portal_linked = p.LinkedPortal != null ? SafeName(p.LinkedPortal) : null; } catch { }
                        try { rec.portal_active = p.gameObject.activeInHierarchy; } catch { }
                        try { rec.portal_enabled = p.enabled; } catch { }
                        try { rec.portal_hide_until_used = p.hideUntilUsedFromOtherSide; } catch { }
                    }
                }
                catch { }

                Boxes[key] = rec;
                changed = true;
            }

            // --- Every SimplePortal (in case the target isn't box-gated) --------
            var portals = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.SimplePortal>();
            for (int i = 0; i < portals.Length; i++)
            {
                var p = portals[i];
                if (p == null) continue;

                string key = SafeName(p) + "@" + PosKey(p);
                if (Portals.ContainsKey(key)) continue;

                var rec = new PortalRec
                {
                    name = SafeName(p),
                    id = ReadId(p),
                    pos = Pos(p),
                    parents = Parents(p),
                };
                try { rec.linked = p.LinkedPortal != null ? SafeName(p.LinkedPortal) : null; } catch { }
                try { rec.active = p.gameObject.activeInHierarchy; } catch { }
                try { rec.enabled = p.enabled; } catch { }
                try { rec.hide_until_used = p.hideUntilUsedFromOtherSide; } catch { }
                Portals[key] = rec;
                changed = true;
            }

            if (changed)
            {
                Write();
                Plugin.Log.LogInfo($"[PORTALS] {Boxes.Count} shortcut boxes, {Portals.Count} portals -> {OutPath}");
            }
            else if (++_runs % 4 == 0)
            {
                Plugin.Log.LogInfo($"[PORTALS] heartbeat: {boxes.Length} boxes loaded, {Boxes.Count} captured, {Portals.Count} portals");
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"ShortcutPortalDumper: {e}"); }
    }

    private static string ReadId(Il2Cpp.SimplePortal p)
    {
        try { var oid = p.gameObject.GetComponent<Il2Cpp.OverworldID>(); return oid != null ? oid.ID : null; }
        catch { return null; }
    }

    private static string SafeName(UnityEngine.Component c)
    { try { return c != null ? c.gameObject.name : null; } catch { return null; } }

    private static float[] Pos(UnityEngine.Component c)
    {
        try { var p = c.transform.position; return new[] { p.x, p.y, p.z }; }
        catch { return null; }
    }

    private static string PosKey(UnityEngine.Component c)
    {
        var p = Pos(c);
        return p == null ? "?" : $"{p[0]:F1},{p[1]:F1},{p[2]:F1}";
    }

    private static List<string> Parents(UnityEngine.Component c)
    {
        var list = new List<string>();
        try
        {
            var t = c.transform != null ? c.transform.parent : null;
            for (int i = 0; i < 4 && t != null; i++) { list.Add(t.name); t = t.parent; }
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
            var root = JsonConvert.DeserializeObject<PortalsFile>(File.ReadAllText(OutPath));
            if (root?.boxes != null) foreach (var kv in root.boxes) Boxes[kv.Key] = kv.Value;
            if (root?.portals != null) foreach (var kv in root.portals) Portals[kv.Key] = kv.Value;
            Plugin.Log.LogInfo($"[PORTALS] loaded {Boxes.Count} boxes, {Portals.Count} portals from existing {OutPath}");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"ShortcutPortalDumper.LoadOnce: {e}"); }
    }

    private static void Write()
    {
        var file = new PortalsFile { boxes = Boxes, portals = Portals };
        File.WriteAllText(OutPath, JsonConvert.SerializeObject(file, Formatting.Indented));
    }
}
