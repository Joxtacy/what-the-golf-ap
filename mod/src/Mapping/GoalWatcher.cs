using System;
using System.Collections.Generic;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Sends a hole's "- Clear" / "- Crown" checks from the per-NODE completion state,
/// reading the overworld flag (Il2Cpp.OverworldGoal) rather than the per-sub-level
/// GameAnalytics.OnLevelComplete event.
///
/// WHY: a single overworld flag can bundle several "smaller levels" -- its
/// OverworldGoal has a primary <c>levelData</c> PLUS <c>AdditionalLevelData</c>
/// (e.g. "2DBall mario 1"). OnLevelComplete fires once per sub-level, so sending
/// the check from there fires the Clear the moment the FIRST sub-level (the one
/// whose scene is the AP location) finishes -- before the whole node is done -- and
/// flips the Crown as soon as that sub-level's own challenges are met (not the whole
/// node's). It can also MISS a crown that only completes across the additional
/// levels. The game's own per-node truth is <c>OverworldGoal.state</c>:
///   Hidden=0, Unplayed=1, Won=2 (node cleared), Crown=3 (node fully crowned).
/// The game sets Won/Crown only when the ENTIRE node + all its challenges are done,
/// so reading it is correct for compound and single-level nodes alike.
///
/// Bosses are NOT overworld goals (they're triggered by the computer doors), so
/// they never appear here -- their Clear/Crown stays on the OnLevelComplete path in
/// GamePatches (see LevelCompletePostfix).
///
/// Scanned from Mod.OnUpdate while connected and in the overworld (goals only exist
/// there). Idempotent: a per-scene sent-set here plus ArchipelagoClient.SendCheck's
/// own dedup mean re-scanning every tick costs nothing after the first send. On a
/// fresh process the first scan re-sends every already-completed node's checks
/// (the server dedups them), which reconciles progress made before this launch.
/// </summary>
public static class GoalWatcher
{
    private const int Won = 2;
    private const int Crown = 3;

    private static readonly HashSet<string> _clearSent = new();
    private static readonly HashSet<string> _crownSent = new();

    /// <summary>Forget what we've sent (so the next scan reconciles from scratch).</summary>
    public static void Reset()
    {
        _clearSent.Clear();
        _crownSent.Clear();
    }

    /// <summary>Scan every loaded overworld flag and send the Clear/Crown for any
    /// node the game now reports as Won/Crown. Cheap after the first pass.</summary>
    public static void Scan()
    {
        try
        {
            var goals = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldGoal>();
            if (goals == null) return;

            for (int i = 0; i < goals.Length; i++)
            {
                var g = goals[i];
                if (g == null) continue;

                var ld = g.levelData;
                string scene = ld != null ? ld.SceneName : null;
                if (string.IsNullOrEmpty(scene)) continue;

                int state = (int)g.state;
                if (state < Won) continue;   // Hidden / Unplayed -> nothing earned yet

                // Cleared: send once. Only for scenes that map to an AP location.
                if (!_clearSent.Contains(scene) && LocationMap.ClearId(scene) >= 0)
                {
                    _clearSent.Add(scene);
                    Plugin.Client?.SendClear(scene);
                    Plugin.Log.LogInfo($"[GOAL] node cleared: '{scene}' (state={state})");
                }

                // Fully crowned: send once, and only if this node actually has a
                // crown location (challenges > 0 -> CrownId resolves).
                if (state >= Crown && !_crownSent.Contains(scene)
                    && LocationMap.CrownId(scene) >= 0)
                {
                    _crownSent.Add(scene);
                    Plugin.Client?.SendCrown(scene);
                    Plugin.Log.LogInfo($"[GOAL] node crowned: '{scene}'");
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"GoalWatcher.Scan: {e}"); }
    }
}
