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
/// Output: wtg_sections.json = [ { name, hasBoss, saveSpotId, unlockTriggerId,
/// levels:[scene,...] }, ... ] (order preserved = overworld progression order).
/// Read-only; runs periodically from Mod.OnUpdate.
/// </summary>
public static class SectionDumper
{
    private class SectionRec
    {
        public string name;
        public bool hasBoss;
        public string saveSpotId;
        public string unlockTriggerId;
        public List<string> levels = new();
    }

    // section name -> record (accumulate best = most levels; order tracked separately)
    private static readonly Dictionary<string, SectionRec> Sections = new();
    private static readonly List<string> Order = new();
    private static int _runs;

    private static string OutPath =>
        Path.Combine(MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_sections.json");

    public static void Dump()
    {
        try
        {
            var datas = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldLevelData>();
            bool changed = false;

            for (int d = 0; d < datas.Length; d++)
            {
                var old = datas[d];
                var sections = old != null ? old.Sections : null;
                if (sections == null) continue;

                for (int s = 0; s < sections.Count; s++)
                {
                    var sec = sections[s];
                    if (sec == null) continue;
                    string name = sec.name;
                    if (string.IsNullOrEmpty(name)) name = $"section#{d}.{s}";

                    if (!Sections.TryGetValue(name, out var rec))
                    {
                        rec = new SectionRec { name = name };
                        Sections[name] = rec;
                        Order.Add(name);
                        changed = true;
                    }
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
                Plugin.Log.LogInfo($"[SECTIONS] {Sections.Count} sections ({bosses} w/boss), {total} holes -> {OutPath}");
            }
            else if (++_runs % 4 == 0)
            {
                Plugin.Log.LogInfo($"[SECTIONS] heartbeat: {datas.Length} OverworldLevelData, {Sections.Count} sections captured");
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"SectionDumper: {e}"); }
    }

    private static void Write()
    {
        var list = Order.Select(n => Sections[n]).ToList();
        File.WriteAllText(OutPath, JsonConvert.SerializeObject(list, Formatting.Indented));
    }
}
