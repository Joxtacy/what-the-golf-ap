using System;
using HarmonyLib;

namespace WtgArchipelago.Patches;

/// <summary>
/// Harmony hooks into WHAT THE GOLF?. Method names are REAL, discovered from an
/// Il2CppDumper dump (see mod/REVERSE_ENGINEERING.md). Patches are applied by
/// string via AccessTools, so this compiles without the generated interop
/// assemblies. The postfixes read the current level via GameState (Il2CppCore
/// types) and send the matching AP checks.
/// </summary>
public static class GamePatches
{
    public static void Apply(HarmonyLib.Harmony harmony)
    {
        // Level cleared -> read the current level and send the AP Clear/Crown.
        //
        // IMPORTANT: we DON'T patch Core.Level.Complete. Its signature has a
        // Nullable<float> and a by-value struct param, which Il2CppInterop can't
        // marshal through Harmony's native->managed trampoline -- patching it
        // throws NullReferenceException on every call and breaks/crashes the game.
        // GameAnalytics.OnLevelComplete is the analytics reporter for the same
        // event, is static, and takes only a reference-type param (safe). We read
        // the level via GameState (reading fields OUT is the safe direction).
        TryPatch(harmony, "GameAnalytics:OnLevelComplete", nameof(LevelCompletePostfix));

        // Campaign goal: final Computer defeated.
        TryPatch(harmony, "GameAnalytics:OnFinalBossCompleted", nameof(FinalBossPostfix));

        // Crown chest opened (crowns option) -> send that chest's AP check. Fires
        // once per chest on first open; the Chest arg (reference type -> safe to
        // inject) gives us its OverworldID.ID (CHEST_*).
        TryPatch(harmony, "ChestManager:Chest_OnPostChestOpenFirst", nameof(ChestOpenedPostfix));

        // DeathLink (outgoing): a level FAILURE -> maybe broadcast a death. We hook
        // the static, no-arg GameAnalytics.OnLevelReset (the AUTOMATIC reset the game
        // fires on out-of-bounds/water/lost-ball) -- safe like the other statics, and
        // it excludes OnLevelManualReset (player pressed restart) and OnLevelAbort
        // (quit to overworld), which must NOT count as deaths. We avoid Level.Fail:
        // its Nullable/by-value signature crashes the interop trampoline.
        TryPatch(harmony, "GameAnalytics:OnLevelReset", nameof(LevelResetPostfix));

        // The main-thread pump lives in Mod.OnUpdate, which runs every frame.
    }

    private static void TryPatch(HarmonyLib.Harmony harmony, string target, string postfix)
    {
        try
        {
            var method = AccessTools.Method(target);
            if (method == null)
            {
                Plugin.Log.LogWarning($"patch target not found (check the dump): {target}");
                return;
            }
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(GamePatches), postfix));
            Plugin.Log.LogInfo($"patched: {target}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"failed to patch {target}: {e.Message}");
        }
    }

    // --- Postfixes -----------------------------------------------------------

    private static void LevelCompletePostfix()
    {
        // Never let an exception escape a postfix into the game.
        try
        {
            Plugin.Log.LogInfo($"[LEVEL] {GameState.CurrentLevelInfo()}");

            string scene = GameState.CurrentLevelScene();
            if (scene != null)
            {
                Plugin.Client?.SendClear(scene);
                if (GameState.CurrentLevelCrowned())
                    Plugin.Client?.SendCrown(scene);

                // all_bosses goal: count this clear if it's a boss (no-op otherwise).
                Mapping.BossGoal.RegisterDefeat(scene);
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"LevelCompletePostfix: {e}"); }
    }

    // Automatic level reset = a failure (out of bounds / water / lost ball). Feed
    // it to the DeathLink throttle, which broadcasts only every Nth wipe and ignores
    // resets it induced itself. Manual restart / quit route through different
    // GameAnalytics methods, so they never reach here.
    private static void LevelResetPostfix()
    {
        try { Plugin.Client?.DeathLink?.OnLocalWipe(); }
        catch (Exception e) { Plugin.Log.LogError($"LevelResetPostfix: {e}"); }
    }

    // __1 = the second parameter of Chest_OnPostChestOpenFirst(ChestTrophee, Chest)
    // -- i.e. the opened Chest. Index-based injection avoids relying on interop
    // preserving the original parameter name.
    private static void ChestOpenedPostfix(Il2Cpp.Chest __1)
    {
        try
        {
            string oid = null;
            try { oid = (__1 != null && __1.id != null) ? __1.id.ID : null; } catch { }
            Mapping.ChestGate.ReportOpened(oid);
        }
        catch (Exception e) { Plugin.Log.LogError($"ChestOpenedPostfix: {e}"); }
    }

    private static void FinalBossPostfix()
    {
        try
        {
            int goal = Plugin.Client?.Data?.Goal ?? WtgArchipelago.ArchipelagoData.GoalCampaign;
            if (goal == WtgArchipelago.ArchipelagoData.GoalAllBosses)
            {
                // all_bosses: the final boss is just one of the required bosses.
                Plugin.Log.LogInfo("OnFinalBossCompleted -> final boss down (all_bosses goal)");
                Mapping.BossGoal.RegisterFinalBoss();
            }
            else if (goal == WtgArchipelago.ArchipelagoData.GoalCampaign)
            {
                Plugin.Log.LogInfo("OnFinalBossCompleted -> campaign goal reached");
                Plugin.Client?.SendVictory();
            }
            else
            {
                // door_% goals complete via Flag count, not the final boss.
                Plugin.Log.LogInfo("OnFinalBossCompleted (door goal -> no victory here)");
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"FinalBossPostfix: {e}"); }
    }
}
