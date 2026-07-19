using System;
using System.Collections.Generic;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Non-linear chamber unlocking via the game's own save-trigger system.
///
/// The save records opened doors in two sets (probed on a 100% save):
///   OPEN_DOORS      -> section unlockTriggerIds (e.g. "DOOR_easy2d_00", "Z4UZC")
///   OPEN_MAIN_DOORS -> computer-door triggers   (e.g. "YX3NO", "9DSBG", "OS8GA")
/// Each OverworldLevelSection carries its unlockTriggerId; a section becomes
/// available (and teleport-reachable) once that trigger is registered as open.
///
/// To unlock chamber N we register every section-of-N's unlockTriggerId via
/// SaveGame.SetDoorOpen + SetMainDoorOpen, then OverworldManager2d
/// .RefreshDoorsAndGoals() so the overworld/teleport updates. Validated in-game:
/// unlocking chamber 08 made all four of its sub-areas teleport-reachable on a
/// fresh save. This matches the game's teleport/portal travel — AP unlocks any
/// chamber independently, no forced linear walking.
///
/// AP items can arrive before the overworld scene is loaded (e.g. at connect, when
/// all prior items are resent), so we remember requested chambers and (re)apply
/// them from Mod.OnUpdate once an overworld is present.
/// </summary>
public static class ChamberUnlock
{
    private static readonly HashSet<int> Requested = new();
    private static readonly HashSet<int> Applied = new();

    /// <summary>Request chamber N unlocked (from an AP "Chamber NN Access" item).</summary>
    public static void Request(int chamber)
    {
        if (Requested.Add(chamber))
            Plugin.Log.LogInfo($"[UNLOCK] chamber {chamber:D2} requested");
        TryApply();
    }

    /// <summary>Apply any requested-but-not-yet-applied chambers if we're in an
    /// overworld. Cheap no-op otherwise. Call periodically from OnUpdate.</summary>
    public static void TryApply()
    {
        try
        {
            if (Requested.Count == Applied.Count) return;

            var datas = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldLevelData>();
            if (datas.Length == 0) return;                       // no overworld yet
            var mgrs = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldManager2d>();
            if (mgrs.Length == 0) return;

            int newlyApplied = 0;
            foreach (var chamber in Requested)
            {
                if (Applied.Contains(chamber)) continue;
                int hits = 0;
                for (int a = 0; a < datas.Length; a++)
                {
                    var secs = datas[a] != null ? datas[a].Sections : null;
                    if (secs == null) continue;
                    for (int s = 0; s < secs.Count; s++)
                    {
                        var sec = secs[s];
                        if (sec == null || ChamberOf(sec.name) != chamber) continue;
                        string trig = sec.unlockTriggerId;
                        if (string.IsNullOrEmpty(trig)) continue;
                        try { Il2Cpp.SaveGame.SetDoorOpen(trig); } catch { }
                        try { Il2Cpp.SaveGame.SetMainDoorOpen(trig); } catch { }
                        hits++;
                    }
                }
                Applied.Add(chamber);
                newlyApplied++;
                Plugin.Log.LogInfo($"[UNLOCK] chamber {chamber:D2}: opened {hits} section trigger(s)");
            }

            if (newlyApplied > 0)
            {
                for (int m = 0; m < mgrs.Length; m++)
                    if (mgrs[m] != null) { try { mgrs[m].RefreshDoorsAndGoals(); } catch { } }
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"ChamberUnlock.TryApply: {e}"); }
    }

    // "08A" -> 8, "02" -> 2, "10" -> 10.
    private static int ChamberOf(string sectionName)
    {
        if (string.IsNullOrEmpty(sectionName)) return -1;
        int i = 0, n = 0; bool any = false;
        while (i < sectionName.Length && char.IsDigit(sectionName[i])) { n = n * 10 + (sectionName[i] - '0'); i++; any = true; }
        return any ? n : -1;
    }
}
