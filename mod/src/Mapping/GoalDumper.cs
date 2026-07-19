using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Enumerates the overworld's OverworldGoal objects (the "flags" you golf into
/// to enter a level) and records how they map to levels, hub sections, and lock
/// state. This is the structure that gating hooks into: each goal has a
/// levelData (scene), a ParentHubSection (area/chamber), a state
/// (Hidden/Unplayed/Won/Crown), IsUnlocked(), and a requireGoalToUnlock chain.
///
/// Read-only. Runs periodically from Mod.OnUpdate; goals only exist while in the
/// overworld, so walk the lab to capture them. Writes wtg_goals.json.
/// </summary>
public static class GoalDumper
{
    private static readonly Dictionary<string, string> Seen = new();
    private static int _runs;

    private static string OutPath =>
        Path.Combine(MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_goals.json");

    public static void Dump()
    {
        try
        {
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldGoal>();
            int added = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var g = all[i];
                if (g == null) continue;

                var ld = g.levelData;
                string scene = ld != null ? ld.SceneName : null;
                string key = scene ?? ("goal#" + i);
                if (Seen.ContainsKey(key)) continue;

                var section = g.ParentHubSection;
                string sectionName = section != null ? section.name : null;
                var req = g.requireGoalToUnlock;
                string requires = (req != null && req.levelData != null) ? req.levelData.SceneName : null;

                Seen[key] = "{" +
                    $"\"scene\":{J(scene)}," +
                    $"\"section\":{J(sectionName)}," +
                    $"\"state\":{(int)g.state}," +
                    $"\"unlocked\":{(g.IsUnlocked() ? "true" : "false")}," +
                    $"\"requires\":{J(requires)}" +
                    "}";
                added++;
            }

            if (added > 0)
            {
                Write();
                Plugin.Log.LogInfo($"[GOALS] +{added} goals (total {Seen.Count}) -> {OutPath}");
            }
            else if (++_runs % 4 == 0)
            {
                Plugin.Log.LogInfo($"[GOALS] heartbeat: {all.Length} goals loaded, {Seen.Count} captured");
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"GoalDumper: {e}"); }
    }

    private static void Write()
    {
        var sb = new StringBuilder("[\n");
        int i = 0;
        foreach (var line in Seen.Values)
            sb.Append("  ").Append(line).Append(++i < Seen.Count ? ",\n" : "\n");
        sb.Append("]\n");
        File.WriteAllText(OutPath, sb.ToString());
    }

    private static string J(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < 0x20) sb.Append(' ');
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }
}
