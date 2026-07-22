using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Dumps the AUTHORITATIVE campaign structure from the OverworldLevelData
/// ScriptableObject(s). Unlike the streamed overworld door/goal objects (which
/// only load as you walk near them), a ScriptableObject asset is fully loaded, so
/// one pass captures EVERY section with its complete level list — no gaps.
///
/// OverworldLevelData.Sections : List&lt;OverworldLevelSection&gt;, each with:
///   name, hasBoss, saveSpotId, unlockTriggerId, List&lt;LevelData&gt; levels.
/// This is the real chamber/sub-area -> levels membership the apworld needs.
///
/// EPISODE-AWARE: every record is tagged with its <c>campaign</c> (Main, Olympics,
/// Snow, ...; see <see cref="CampaignInfo"/>) and <c>source</c> (the OverworldLevelData
/// asset name), and records are keyed by "&lt;campaign&gt;::&lt;section name&gt;" so
/// episodes that reuse section codes (each renumbers 01.., 08A..) don't overwrite
/// each other as several overworld walks accumulate. Legacy records with no
/// campaign field (the original base-game dump) are migrated to "Main" on load.
///
/// Output: wtg_sections.json = [ { campaign, source, name, hasBoss, saveSpotId,
/// unlockTriggerId, levels:[scene,...] }, ... ] (order preserved = capture order).
/// Read-only; runs periodically from Mod.OnUpdate. Accumulates across sessions.
/// </summary>
public static class SectionDumper
{
    private class SectionRec
    {
        public string campaign;          // episode tag (Main/Olympics/Snow/Hotdog/Alive/Amongus)
        public string source;            // OverworldLevelData asset name (per-episode asset)
        public string name;
        public bool hasBoss;
        public string saveSpotId;
        public string unlockTriggerId;
        public List<string> levels = new();
    }

    // key = "<campaign>::<section name>" (see class summary).
    private static readonly Dictionary<string, SectionRec> Sections = new();
    private static readonly List<string> Order = new();
    private static bool _loaded;
    private static int _runs;

    private static string OutPath =>
        Path.Combine(MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_sections.json");

    public static void Dump()
    {
        try
        {
            LoadOnce();
            string campaign = CampaignInfo.Current();

            var datas = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldLevelData>();
            bool changed = false;

            for (int d = 0; d < datas.Length; d++)
            {
                var old = datas[d];
                var sections = old != null ? old.Sections : null;
                if (sections == null) continue;
                string source = null;
                try { source = old.name; } catch { }

                for (int s = 0; s < sections.Count; s++)
                {
                    var sec = sections[s];
                    if (sec == null) continue;
                    string name = sec.name;
                    if (string.IsNullOrEmpty(name)) name = $"section#{d}.{s}";
                    string key = campaign + "::" + name;

                    if (!Sections.TryGetValue(key, out var rec))
                    {
                        rec = new SectionRec { name = name, campaign = campaign, source = source };
                        Sections[key] = rec;
                        Order.Add(key);
                        changed = true;
                    }
                    if (string.IsNullOrEmpty(rec.source) && !string.IsNullOrEmpty(source)) { rec.source = source; changed = true; }
                    rec.hasBoss = sec.hasBoss;
                    rec.saveSpotId = sec.saveSpotId;
                    rec.unlockTriggerId = sec.unlockTriggerId;

                    var levels = sec.levels;
                    if (levels != null)
                        for (int k = 0; k < levels.Count; k++)
                        {
                            var ld = levels[k];
                            string scene = ld != null ? ld.SceneName : null;
                            if (!string.IsNullOrEmpty(scene) && !rec.levels.Contains(scene))
                            {
                                rec.levels.Add(scene);
                                changed = true;
                            }
                        }
                }
            }

            if (changed)
            {
                Write();
                int total = Sections.Values.SelectMany(r => r.levels).Distinct().Count();
                int bosses = Sections.Values.Count(r => r.hasBoss);
                var campaigns = string.Join(",", Sections.Values.Select(r => r.campaign).Distinct());
                Plugin.Log.LogInfo($"[SECTIONS] {Sections.Count} sections ({bosses} w/boss), {total} holes; campaigns=[{campaigns}] (active={campaign}) -> {OutPath}");
            }
            else if (++_runs % 4 == 0)
            {
                Plugin.Log.LogInfo($"[SECTIONS] heartbeat: {datas.Length} OverworldLevelData, {Sections.Count} sections captured (active campaign={campaign})");
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"SectionDumper: {e}"); }
    }

    private static void LoadOnce()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(OutPath)) return;
            var list = JsonConvert.DeserializeObject<List<SectionRec>>(File.ReadAllText(OutPath));
            if (list == null) return;
            foreach (var rec in list)
            {
                if (rec == null || string.IsNullOrEmpty(rec.name)) continue;
                if (string.IsNullOrEmpty(rec.campaign)) rec.campaign = "Main"; // legacy dump = base game
                rec.levels ??= new List<string>();
                string key = rec.campaign + "::" + rec.name;
                if (!Sections.ContainsKey(key)) { Sections[key] = rec; Order.Add(key); }
            }
            var campaigns = string.Join(",", Sections.Values.Select(r => r.campaign).Distinct());
            Plugin.Log.LogInfo($"[SECTIONS] loaded {Sections.Count} sections (campaigns=[{campaigns}]) from existing {OutPath}");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"SectionDumper.LoadOnce: {e}"); }
    }

    private static void Write()
    {
        var list = Order.Select(n => Sections[n]).ToList();
        File.WriteAllText(OutPath, JsonConvert.SerializeObject(list, Formatting.Indented));
    }
}
