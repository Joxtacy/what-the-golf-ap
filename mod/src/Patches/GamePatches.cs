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

        // Hard boss gate (boss_keys option) -- the AUTHORITATIVE lever. A computer
        // door's button only becomes hittable when CanBeOpened() (all plates on) is
        // true, and the game recomputes that from natural plate state every refresh.
        // BossGate's ~6x/sec SetState can't win that per-frame race in EITHER
        // direction (it failed to hold Computer 8 shut once Western was fully cleared,
        // and can't reliably hold a keyed boss's plates ON when its sub-areas aren't
        // cleared). So we override CanBeOpened directly, keyed purely off the AP key:
        // locked boss -> false (button never activates), keyed boss -> true (openable
        // regardless of plate state). Race-free; decoupled from sub-area completion.
        TryPatchPrefix(harmony, "OverworldMainDoorRobot:CanBeOpened",
                       nameof(MainDoorCanOpenPrefix));

        // Belt-and-suspenders for the locked case: also swallow the ball-hit that
        // would open a locked door, in case CanBeOpened isn't the only path to the
        // button firing. Harmless for keyed/non-gated doors (returns true).
        TryPatchPrefix(harmony, "OverworldMainDoorRobot:OnHitActiveButton",
                       nameof(MainDoorHitPrefix));

        // Door goal (door_50/75/100) HARD-LOCK: the overworld % completion door opens
        // through the game's own CheckOpen once GetFlagsLeft() <= 0. We override
        // GetFlagsLeft for the seed's goal-tier door off the AP Flag count (0 when the
        // target is reached -> opens; positive otherwise -> stays shut), so the door is
        // decoupled from the game's native flag count. Off-tier % doors defer to the
        // game. (CanDoorBeOpened turned out NOT to gate the open path.)
        TryPatchPrefix(harmony, "OverworldButton2DPercentage:GetFlagsLeft",
                       nameof(PercentFlagsLeftPrefix));

        // Door goal LABEL: the % door's requirement text natively shows the game's
        // completion % ("4/50%"); retarget the goal-tier door's label to AP progress
        // ("<collected>/<needed>") right after the game's UpdateStatus writes it.
        TryPatch(harmony, "OverworldButton2DPercentage:UpdateStatus",
                 nameof(PercentUpdateStatusPostfix));

        // Door goal VICTORY: pressing the OverworldMainButton INSIDE our % door (not
        // merely opening the door) reports the goal. Its collision handler fires
        // OnPressed when the ball hits it; we postfix it and, for the button linked to
        // our tier's door, send Victory (once, Flag-count-guarded).
        TryPatch(harmony, "OverworldMainButton:OnCollisionEnter2D",
                 nameof(PercentButtonPressedPostfix));

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

    // As TryPatch, but installs a PREFIX (which can veto the original by returning
    // false). Used for the hard boss gate.
    private static void TryPatchPrefix(HarmonyLib.Harmony harmony, string target, string prefix)
    {
        try
        {
            var method = AccessTools.Method(target);
            if (method == null)
            {
                Plugin.Log.LogWarning($"patch target not found (check the dump): {target}");
                return;
            }
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(GamePatches), prefix));
            Plugin.Log.LogInfo($"patched (prefix): {target}");
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
                // Bosses are computer-door encounters, NOT overworld flags, so
                // GoalWatcher never sees them -- send their Clear/Crown here, on the
                // per-level event. They're always single-level, so this fires at the
                // right time. Normal (non-boss) holes are OverworldGoals -- possibly
                // COMPOUND (a primary level + AdditionalLevelData) -- so their
                // Clear/Crown is driven by the per-node OverworldGoal.state in
                // GoalWatcher instead. Sending them here would fire the moment the
                // first sub-level finished, i.e. before the whole node is done.
                if (GameState.CurrentLevelIsBoss())
                {
                    Plugin.Client?.SendClear(scene);
                    if (GameState.CurrentLevelCrowned())
                        Plugin.Client?.SendCrown(scene);
                }

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

    // PREFIX on OverworldMainDoorRobot.CanBeOpened(). Overrides the return value for
    // OUR gated computer doors, using only the AP key state (not natural plate
    // state): keyed -> can open; not-yet-keyed -> cannot. For non-gated doors (or
    // when boss keys are off) it defers to the game. This is what actually makes a
    // keyed boss fightable without clearing its sub-areas, and keeps an unkeyed boss
    // shut even after its chamber is fully cleared.
    private static bool MainDoorCanOpenPrefix(Il2Cpp.OverworldMainDoorRobot __instance, ref bool __result)
    {
        try
        {
            string bid = __instance != null ? __instance.bossLevelID : null;
            if (!Mapping.BossGate.IsGatedBoss(bid)) return true;   // not ours -> game decides
            __result = Mapping.BossGate.IsUnlocked(bid);
            return false;   // skip original -> our decision stands
        }
        catch (Exception e) { Plugin.Log.LogError($"MainDoorCanOpenPrefix: {e}"); return true; }
    }

    // PREFIX on OverworldMainDoorRobot.OnHitActiveButton. Return false to swallow
    // the ball-hit that would open a LOCKED computer door (a gated boss whose
    // Computer N key hasn't arrived). Returns true (run normally) for unlocked or
    // non-gated doors, and whenever boss keys are off. This is the reliable gate:
    // completing a chamber's holes lights the door's plates natively, which the
    // polling BossGate can't suppress fast enough.
    private static bool MainDoorHitPrefix(Il2Cpp.OverworldMainDoorRobot __instance)
    {
        try
        {
            string bid = __instance != null ? __instance.bossLevelID : null;
            if (Mapping.BossGate.IsLocked(bid))
            {
                Plugin.Log.LogInfo($"[BOSS] blocked opening locked door {bid} (need its key)");
                return false;   // skip original -> door stays shut
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"MainDoorHitPrefix: {e}"); }
        return true;            // not gated / already unlocked -> open as normal
    }

    // PREFIX on OverworldButton2D.CanDoorBeOpened(). For the seed's goal-tier %
    // completion door we decide purely from the AP Flag count (PercentGate); every
    // other door (section doors, off-tier % doors) defers to the game. Mirrors the
    // boss-door CanBeOpened override.
    private static bool PercentFlagsLeftPrefix(Il2Cpp.OverworldButton2DPercentage __instance, ref int __result)
    {
        try
        {
            if (!Mapping.PercentGate.TryFlagsLeft(__instance, out int left)) return true;  // not ours
            __result = left;
            return false;   // skip original -> our AP-based flags-left stands
        }
        catch (Exception e) { Plugin.Log.LogError($"PercentFlagsLeftPrefix: {e}"); return true; }
    }

    // POSTFIX on OverworldMainButton.OnCollisionEnter2D (the ball-hit that presses the
    // button). For the enabled button sitting inside our goal-tier % door, report the
    // goal. IsInsideButton filters to exactly that button (via PreviousDoor identity +
    // isEnabled), so other main buttons (computer consoles, off-tier speedrun buttons)
    // are ignored.
    // POSTFIX on OverworldButton2DPercentage.UpdateStatus: overwrite the goal-tier
    // door's requirement label with AP progress (the game just set it to native %).
    private static void PercentUpdateStatusPostfix(Il2Cpp.OverworldButton2DPercentage __instance)
    {
        try { Mapping.PercentGate.ApplyDoorText(__instance); }
        catch (Exception e) { Plugin.Log.LogError($"PercentUpdateStatusPostfix: {e}"); }
    }

    private static void PercentButtonPressedPostfix(Il2Cpp.OverworldMainButton __instance)
    {
        try
        {
            if (Mapping.PercentGate.IsInsideButton(__instance))
                Mapping.PercentGate.OnInsideButtonPressed();
        }
        catch (Exception e) { Plugin.Log.LogError($"PercentButtonPressedPostfix: {e}"); }
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
