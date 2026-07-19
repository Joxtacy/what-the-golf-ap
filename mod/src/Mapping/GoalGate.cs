using System;
using System.Collections.Generic;

namespace WtgArchipelago.Mapping;

/// <summary>
/// The gating: hides overworld goals whose area isn't unlocked yet, and reveals
/// them once their Access item arrives. Setting OverworldGoal.state + calling
/// RefreshVisuals() drives the game's own flag visuals/interaction. state is
/// derived from the save (private RefreshState), so our override affects
/// display/interaction only and is NOT persisted -- safe, but we must re-apply
/// periodically since the game may recompute it.
///
/// We only restore goals WE hid (tracked in ForcedHidden), so naturally-hidden
/// goals (secret/DLC) are left alone.
/// </summary>
public static class GoalGate
{
    private static readonly HashSet<string> ForcedHidden = new();

    public static void Apply()
    {
        try
        {
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldGoal>();
            for (int i = 0; i < all.Length; i++)
            {
                var g = all[i];
                if (g == null) continue;
                var ld = g.levelData;
                string scene = ld != null ? ld.SceneName : null;
                if (scene == null) continue;

                bool unlocked = AreaState.IsSceneUnlocked(scene);

                if (!unlocked)
                {
                    if (g.state != Il2Cpp.OverworldGoal.States.Hidden)
                    {
                        g.state = Il2Cpp.OverworldGoal.States.Hidden;
                        g.RefreshVisuals();
                    }
                    ForcedHidden.Add(scene);
                }
                else if (ForcedHidden.Contains(scene))
                {
                    // Area just unlocked -> reveal the goal we had hidden.
                    g.state = Il2Cpp.OverworldGoal.States.Unplayed;
                    g.RefreshVisuals();
                    ForcedHidden.Remove(scene);
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"GoalGate: {e}"); }
    }
}
