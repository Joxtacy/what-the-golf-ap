using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Enumerates every loaded LevelData and accumulates a de-duplicated JSON list
/// of the game's levels (id, scene, pun, boss, par, challenge count). Called
/// periodically from Mod.OnUpdate; because levels load on-demand, walking the
/// overworld / entering chambers grows the set. Writes wtg_levels.json to the
/// game root. This is the authoritative source for rebuilding data.py + LocationMap.
/// </summary>
public static class LevelDumper
{
    // id -> json object line (accumulates across dumps this session)
    private static readonly Dictionary<string, string> Seen = new();
    private static int _runs;

    private static string OutPath =>
        Path.Combine(MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_levels.json");

    /// <summary>Scan loaded LevelData; write the file if anything new appeared.</summary>
    public static void Dump()
    {
        try
        {
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<Il2CppCore.LevelData>();
            int added = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var ld = all[i];
                if (ld == null) continue;
                string id = ld.ID;
                if (string.IsNullOrEmpty(id) || Seen.ContainsKey(id)) continue;

                int challenges = ld.levelChallenges != null ? ld.levelChallenges.Count : 0;
                Seen[id] = "{" +
                    $"\"id\":{J(id)}," +
                    $"\"scene\":{J(ld.SceneName)}," +
                    $"\"pun\":{J(ld.Pun)}," +
                    $"\"boss\":{(ld.isBossBattle ? "true" : "false")}," +
                    $"\"par\":{ld.StrokesToPar}," +
                    $"\"challenges\":{challenges}" +
                    "}";
                added++;
            }

            if (added > 0)
            {
                Write();
                Plugin.Log.LogInfo($"[DUMP] +{added} new levels (total {Seen.Count}) -> {OutPath}");
            }
            else if (++_runs % 4 == 0)   // heartbeat ~every 20s: prove liveness
            {
                Plugin.Log.LogInfo($"[DUMP] heartbeat: {all.Length} LevelData loaded now, {Seen.Count} captured total");
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"LevelDumper: {e}"); }
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
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < 0x20) sb.Append("?");
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }
}
