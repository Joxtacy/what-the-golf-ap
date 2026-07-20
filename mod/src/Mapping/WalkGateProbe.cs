using System;
using System.Collections.Generic;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Read-only diagnostic for the within-chamber hard-lock spike (Approach A).
///
/// Prints a snapshot of the overworld's gating state on a hotkey (F8):
///   * each OverworldLevelSection: unlockTriggerId, isAvailable (drives teleport),
///     and SaveGame.GetIsDoorOpen(trigger) -- so we see if teleport-availability
///     tracks the save door flag.
///   * each OverworldButton2D (the ball-in connectors between rooms): its
///     OverworldID, canOpen, IsOpenOrOpening (is that walk-connector open?), and
///     the sections its requireGoals reference.
///
/// Capture on a FRESH save: (1) before unlocking anything, (2) after unlocking one
/// section. If unlocking one section flips sibling connectors to IsOpenOrOpening,
/// the sub-areas share connectors we can hold shut; if they were already open on a
/// fresh save, Approach A won't help and we need Approach B. Nothing is mutated.
/// </summary>
public static class WalkGateProbe
{
    public static void Snapshot()
    {
        try
        {
            Plugin.Log.LogInfo("===== [WALKPROBE] overworld gating snapshot =====");

            var datas = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldLevelData>();
            for (int a = 0; a < datas.Length; a++)
            {
                var secs = datas[a] != null ? datas[a].Sections : null;
                if (secs == null) continue;
                for (int s = 0; s < secs.Count; s++)
                {
                    var sec = secs[s];
                    if (sec == null) continue;
                    string trig = sec.unlockTriggerId ?? "";
                    bool doorFlag = false;
                    try { if (!string.IsNullOrEmpty(trig)) doorFlag = Il2Cpp.SaveGame.GetIsDoorOpen(trig); } catch { }
                    Plugin.Log.LogInfo(
                        $"[WALKPROBE] section {sec.name,-5} trig={trig,-18} isAvailable={sec.isAvailable,-5} doorFlagOpen={doorFlag}");
                }
            }

            var btns = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldButton2D>();
            int shown = 0;
            for (int i = 0; i < btns.Length; i++)
            {
                var b = btns[i];
                if (b == null) continue;
                string id = "";
                try { var oid = b.gameObject.GetComponent<Il2Cpp.OverworldID>(); if (oid != null) id = oid.ID; } catch { }
                bool open = false, canOpen = false;
                try { open = b.IsOpenOrOpening; } catch { }
                try { canOpen = b.canOpen; } catch { }
                string goals = GoalSections(b.requireGoals);
                Plugin.Log.LogInfo(
                    $"[WALKPROBE] button id={id,-8} name={b.name,-34} canOpen={canOpen,-5} open={open,-5} requireSections={goals}");
                shown++;
            }
            Plugin.Log.LogInfo($"===== [WALKPROBE] end ({shown} buttons) =====");
        }
        catch (Exception e) { Plugin.Log.LogError($"WalkGateProbe: {e}"); }
    }

    private static string GoalSections(Il2CppSystem.Collections.Generic.List<Il2Cpp.OverworldGoal> goals)
    {
        if (goals == null) return "[]";
        var parts = new List<string>();
        for (int j = 0; j < goals.Count; j++)
        {
            var g = goals[j];
            if (g == null) continue;
            var sec = g.ParentHubSection;
            if (sec != null && !string.IsNullOrEmpty(sec.name)) parts.Add(sec.name);
        }
        return "[" + string.Join(",", parts) + "]";
    }
}
