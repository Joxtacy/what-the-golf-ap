using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Dumps ALL overworld gate/fence types so we can see which mechanism actually
/// walls off areas: Bridge (requireGoals/isUnlocked/Unlock), LevelGate
/// (IsOpen/TurnOn), OverworldOLDoor, and SetActiveBasedOnFlag (toggles a barrier
/// based on goal completion). Read-only. Writes wtg_gates.json + logs counts.
/// </summary>
public static class BridgeDumper
{
    private static readonly Dictionary<string, string> Seen = new();
    private static int _runs;

    private static string OutPath =>
        Path.Combine(MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_gates.json");

    private static readonly string[] Keywords =
        { "door", "gate", "plate", "switch", "bridge", "lock", "barrier", "robot", "button", "fence" };

    public static void Dump()
    {
        try
        {
            int added = 0;

            // Name-based scan: find gate-ish GameObjects and report their real
            // component types, so we learn what class actually fences areas here.
            var gos = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.GameObject>();
            for (int i = 0; i < gos.Length; i++)
            {
                var go = gos[i];
                if (go == null) continue;
                string n = go.name;
                if (string.IsNullOrEmpty(n)) continue;
                string nl = n.ToLowerInvariant();
                bool match = false;
                foreach (var k in Keywords) { if (nl.Contains(k)) { match = true; break; } }
                if (!match || Seen.ContainsKey("go:" + n)) continue;

                var comps = go.GetComponents<UnityEngine.MonoBehaviour>();
                var types = new List<string>();
                for (int c = 0; c < comps.Length; c++)
                {
                    var comp = comps[c];
                    if (comp != null) types.Add(comp.GetIl2CppType().Name);
                }
                if (Add("go:" + n, $"\"type\":\"GameObject\",\"name\":{J(n)},\"components\":[{string.Join(",", types.ConvertAll(J))}]"))
                    added++;
            }

            int nBridge = 0, nGate = 0, nSaf = 0, nDoor = 0;

            var bridges = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.Bridge>();
            nBridge = bridges.Length;
            for (int i = 0; i < bridges.Length; i++)
            {
                var b = bridges[i]; if (b == null) continue;
                if (Add($"Bridge:{b.name}#{i}",
                        $"\"type\":\"Bridge\",\"name\":{J(b.name)},\"unlocked\":{Bool(b.isUnlocked)}," +
                        $"\"requireCount\":{b.requireCount},\"goals\":{GoalList(b.requireGoals)}")) added++;
            }

            var gates = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.LevelGate>();
            nGate = gates.Length;
            for (int i = 0; i < gates.Length; i++)
            {
                var g = gates[i]; if (g == null) continue;
                if (Add($"LevelGate:{g.name}#{i}",
                        $"\"type\":\"LevelGate\",\"name\":{J(g.name)},\"isOpen\":{Bool(g.IsOpen)},\"requiredTurnOns\":{g.RequriedTurnOns}")) added++;
            }

            var safs = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.SetActiveBasedOnFlag>();
            nSaf = safs.Length;
            for (int i = 0; i < safs.Length; i++)
            {
                var s = safs[i]; if (s == null) continue;
                if (Add($"SAF:{s.name}#{i}",
                        $"\"type\":\"SetActiveBasedOnFlag\",\"name\":{J(s.name)}," +
                        $"\"onComplete\":{Bool(s.stateOnComplete)},\"onIncomplete\":{Bool(s.stateOnIncomplete)}," +
                        $"\"active\":{Bool(s.gameObject.activeSelf)},\"goals\":{GoalList(s.goals)}")) added++;
            }

            var doors = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldOLDoor>();
            nDoor = doors.Length;
            for (int i = 0; i < doors.Length; i++)
            {
                var d = doors[i]; if (d == null) continue;
                if (Add($"OLDoor:{d.name}#{i}", $"\"type\":\"OverworldOLDoor\",\"name\":{J(d.name)}")) added++;
            }

            if (added > 0)
            {
                Write();
                Plugin.Log.LogInfo($"[GATES] +{added} (Bridge={nBridge} LevelGate={nGate} SAF={nSaf} OLDoor={nDoor}, total {Seen.Count}) -> {OutPath}");
            }
            else if (++_runs % 4 == 0)
            {
                Plugin.Log.LogInfo($"[GATES] heartbeat: Bridge={nBridge} LevelGate={nGate} SAF={nSaf} OLDoor={nDoor}, {Seen.Count} captured");
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"BridgeDumper: {e}"); }
    }

    private static bool Add(string key, string body)
    {
        if (Seen.ContainsKey(key)) return false;
        Seen[key] = "{" + body + "}";
        return true;
    }

    private static string GoalList(Il2CppSystem.Collections.Generic.List<Il2Cpp.OverworldGoal> goals)
    {
        if (goals == null) return "[]";
        var parts = new List<string>();
        for (int j = 0; j < goals.Count; j++)
        {
            var g = goals[j];
            if (g == null) continue;
            var ld = g.levelData;
            var sec = g.ParentHubSection;
            parts.Add("{" + $"\"scene\":{J(ld != null ? ld.SceneName : null)},\"section\":{J(sec != null ? sec.name : null)}" + "}");
        }
        return "[" + string.Join(",", parts) + "]";
    }

    private static string Bool(bool b) => b ? "true" : "false";

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
