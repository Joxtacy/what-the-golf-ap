using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Enumerates the overworld's OverworldGoal objects (the "flags" you golf into to
/// enter a level) and records how they map to levels, hub sections, lock state and
/// WORLD POSITION. This is the structure gating hooks into: each goal has a
/// levelData (scene), a ParentHubSection (area/chamber), a state
/// (Hidden/Unplayed/Won/Crown), IsUnlocked(), and a requireGoalToUnlock chain.
///
/// The positions are what lets tools/build_poptracker_maps.py place a marker for
/// every hole at its true overworld coordinate instead of on a fallback grid.
///
/// Read-only. Runs periodically from Mod.OnUpdate (behind DumpersEnabled). Goals
/// only exist while their overworld is loaded, so walk the lab to capture them --
/// and each episode is a SEPARATE campaign/overworld, hence the campaign tag and
/// the cross-session merge below. Writes wtg_goals.json (a JSON array;
/// tools/build_levels.py reads it as a list, so that shape is load-bearing).
/// </summary>
public static class GoalDumper
{
    private class GoalRec
    {
        public string campaign;      // episode tag (Main/Olympics/Snow/...)
        public string scene;         // levelData.SceneName (join key)
        public string section;       // ParentHubSection name ("Hub Section - Cars")
        public int state;            // Hidden=0 / Unplayed=1 / Won=2 / Crown=3
        public bool unlocked;        // IsUnlocked() at capture
        public string requires;      // requireGoalToUnlock's scene, if any
        public float[] pos;          // world position of the goal flag
        public bool in_scene;        // pos came from a LIVE instance, not an asset
    }

    private static readonly Dictionary<string, GoalRec> Seen = new();
    private static readonly List<string> Order = new();
    private static bool _loaded;
    private static int _runs;

    private static string OutPath =>
        Path.Combine(MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_goals.json");

    public static void Dump()
    {
        try
        {
            LoadOnce();
            string campaign = CampaignInfo.Current();
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldGoal>();
            bool changed = false;

            for (int i = 0; i < all.Length; i++)
            {
                var g = all[i];
                if (g == null) continue;

                var ld = g.levelData;
                string scene = ld != null ? ld.SceneName : null;
                // Key by campaign too, so episodes that reuse scene names don't drop goals.
                string key = campaign + "::" + (scene ?? ("goal#" + i));

                if (!Seen.TryGetValue(key, out var rec))
                {
                    rec = new GoalRec { campaign = campaign, scene = scene };
                    Seen[key] = rec;
                    Order.Add(key);
                    changed = true;
                }

                var section = g.ParentHubSection;
                string sectionName = section != null ? section.name : null;
                var req = g.requireGoalToUnlock;
                string requires = (req != null && req.levelData != null)
                    ? req.levelData.SceneName : null;
                int state = (int)g.state;
                bool unlocked = false;
                try { unlocked = g.IsUnlocked(); } catch { }

                if (rec.section != sectionName) { rec.section = sectionName; changed = true; }
                if (rec.requires != requires) { rec.requires = requires; changed = true; }
                if (rec.state != state) { rec.state = state; changed = true; }
                if (rec.unlocked != unlocked) { rec.unlocked = unlocked; changed = true; }

                // FindObjectsOfTypeAll also returns PREFAB/asset copies, whose
                // transform holds authoring-local coordinates -- ChestGate documents
                // the same trap for canOpen. So only trust an instance that lives in
                // a loaded scene, and UPGRADE a previously recorded asset position
                // the first time a real scene instance shows up.
                bool live = false;
                try { live = g.gameObject.scene.IsValid(); } catch { }
                if (rec.pos == null || (live && !rec.in_scene))
                {
                    var p = Pos(g);
                    if (p != null)
                    {
                        rec.pos = p;
                        rec.in_scene = live;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                Write();
                int live = Seen.Values.Count(r => r.pos != null && r.in_scene);
                Plugin.Log.LogInfo($"[GOALS] {Seen.Count} goals, {live} with live pos "
                                   + $"(active={campaign}) -> {OutPath}");
            }
            else if (++_runs % 4 == 0)
            {
                Plugin.Log.LogInfo($"[GOALS] heartbeat: {all.Length} goals loaded, "
                                   + $"{Seen.Count} captured");
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"GoalDumper: {e}"); }
    }

    /// <summary>Merge what a previous session already captured, so the dump can be
    /// built up over several passes -- it takes Main plus five separate episode
    /// overworlds, which is a lot to walk in one sitting. Records with no campaign
    /// tag predate the campaign-aware dumpers and are Main.</summary>
    private static void LoadOnce()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(OutPath)) return;
            var list = JsonConvert.DeserializeObject<List<GoalRec>>(File.ReadAllText(OutPath));
            if (list == null) return;
            foreach (var rec in list)
            {
                if (rec == null) continue;
                if (string.IsNullOrEmpty(rec.campaign)) rec.campaign = "Main";
                string key = rec.campaign + "::"
                             + (rec.scene ?? ("goal#" + Guid.NewGuid().ToString("N")));
                if (!Seen.ContainsKey(key))
                {
                    Seen[key] = rec;
                    Order.Add(key);
                }
            }
            Plugin.Log.LogInfo($"[GOALS] loaded {Seen.Count} existing goals from {OutPath}");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"GoalDumper.LoadOnce: {e}"); }
    }

    private static float[] Pos(UnityEngine.Component c)
    {
        try
        {
            var p = c.transform.position;
            return new[] { p.x, p.y, p.z };
        }
        catch { return null; }
    }

    private static void Write() =>
        File.WriteAllText(OutPath, JsonConvert.SerializeObject(
            Order.Select(k => Seen[k]).ToList(), Formatting.Indented));
}
