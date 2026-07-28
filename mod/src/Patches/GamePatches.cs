using System;
using System.Collections.Generic;
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
    // OIDs of locked crown/section doors we've already logged blocking (dedup so a
    // player bumping a locked door repeatedly doesn't spam the log). Separate sets per
    // open-path so both paths get a log line for the same OID.
    private static readonly HashSet<string> _loggedGateBlock = new();
    private static readonly HashSet<string> _loggedInstantBlock = new();

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

        // Crown-chest + section HARD gate (crowns / hard_sections options) -- the
        // race-free lever. A door's natural ball-contact open (OverworldButton2D.
        // OnCollisionEnter2D) routes through CheckOpen() to actually open; our own
        // force-open (ChestGate/SectionGate) uses InstantOpenDoor(), which bypasses
        // CheckOpen. So a prefix on CheckOpen that returns false for a still-locked
        // door OID blocks the natural open regardless of the per-tick canOpen poll --
        // closing the ~3s "open a check early" window that the poll leaves open right
        // after a teleport (teleporting skips the overworld poll burst). Filtered to
        // our locked OIDs; every other door (keyed, non-gated, the % goal door) defers
        // to the game. CheckOpen is inherited unchanged by the percentage subclass, so
        // one patch covers both.
        TryPatchPrefix(harmony, "OverworldButton2D:CheckOpen",
                       nameof(ButtonCheckOpenPrefix));

        // Crown-chest + section HARD gate, SECOND open-path. Besides the ball-contact
        // CheckOpen path, a door is also force-opened by GeneralCampaignStarter.CheckDoors
        // when the player navigates toward a level sitting behind it: it calls the door's
        // InstantOpenDoor() directly, which BYPASSES both canOpen and CheckOpen (so neither
        // the SectionGate/ChestGate canOpen poll nor the CheckOpen prefix above catches it).
        // A prefix on InstantOpenDoor that returns false for a still-locked door OID closes
        // that path; InstantOpenDoor also persists via SetDoorOpen, so skipping the whole
        // method keeps it out of OPEN_DOORS too. Guards genuine walk-connectors and crown-
        // chest doors (SectionGate/ChestGate IsLocked). NOTE: the "Main Crown Door(s)"
        // CROWN_MAIN1/2 (07B/03B) are deliberately NOT gated -- those sub-areas are
        // teleport-only, so SectionGate excludes them (see SectionGate.WalkConnectorTriggers).
        // Our own force-open of KEYED crown doors (ChestGate.Tick) calls InstantOpenDoor on
        // UNLOCKED doors, so IsLocked is false there -> unaffected.
        TryPatchPrefix(harmony, "OverworldButton2D:InstantOpenDoor",
                       nameof(ButtonInstantOpenPrefix));

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

        // Episode gate (the "episodes" option) -- the enforcement lever. Starting any
        // campaign/episode goes through a BasePackStarter.StartPack(ContentPack, object[])
        // override: a SYNCHRONOUS bool method that runs BEFORE any async scene load or
        // "transition" tunnel. For a LOCKED episode (seed-enabled, its "<name> Episode
        // Access" not yet received) we return false to skip it -- nothing starts, the
        // player stays in the hub. Skipping this sync entry (vs the async loader) can't
        // corrupt the loader state. Episodes use GeneralCampaignStarter; we also gate
        // CampaignStarter for safety. The IsBlocked check only fires for locked episode
        // packs, so gating both starters is harmless for every other pack.
        TryPatchStartPack(harmony, "GeneralCampaignStarter", nameof(StartPackGatePrefix));
        TryPatchStartPack(harmony, "CampaignStarter", nameof(StartPackGatePrefix));

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

    // Patch a BasePackStarter subclass's StartPack(ContentPack, object[]) override.
    // The class also has an unrelated private StartPack(ContentPack, string, BALLSHAPES)
    // iterator, so a name-only AccessTools lookup is ambiguous -- resolve by reflection
    // to the 2-parameter override whose first parameter is a ContentPack.
    private static void TryPatchStartPack(HarmonyLib.Harmony harmony, string className, string prefix)
    {
        try
        {
            var t = AccessTools.TypeByName(className);
            if (t == null) { Plugin.Log.LogWarning($"StartPack gate: type not found: {className}"); return; }
            System.Reflection.MethodInfo target = null;
            foreach (var m in AccessTools.GetDeclaredMethods(t))
            {
                if (m.Name != "StartPack") continue;
                var ps = m.GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType.Name == "ContentPack") { target = m; break; }
            }
            if (target == null) { Plugin.Log.LogWarning($"StartPack gate: no StartPack(ContentPack, object[]) on {className}"); return; }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(GamePatches), prefix));
            Plugin.Log.LogInfo($"patched (prefix): {className}:StartPack");
        }
        catch (Exception e) { Plugin.Log.LogError($"failed to patch {className}:StartPack: {e.Message}"); }
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

    // PREFIX on OverworldButton2D.CheckOpen() -- the choke the natural ball-contact
    // open routes through. For a LOCKED crown-chest door (crowns option) or a LOCKED
    // within-chamber section connector (hard_sections option), skip the original so
    // the door never opens on contact -- a race-free hard gate that doesn't depend on
    // the per-tick canOpen=false poll winning the frame (which it can miss for ~3s
    // after a teleport). Our own force-open uses InstantOpenDoor(), which bypasses
    // CheckOpen, so keyed doors still open. Every other door defers to the game.
    private static bool ButtonCheckOpenPrefix(Il2Cpp.OverworldButton2D __instance)
    {
        try
        {
            if (!IsLockedGatedDoor(__instance, out string oid)) return true;
            // Dedup: a player can bump a locked door repeatedly -> log once per OID.
            if (_loggedGateBlock.Add(oid))
                Plugin.Log.LogInfo($"[GATE] blocked opening locked door '{oid}' (need its key)");
            return false;   // skip original -> door stays shut
        }
        catch (Exception e) { Plugin.Log.LogError($"ButtonCheckOpenPrefix: {e}"); }
        return true;   // not gated / unlocked -> open as normal
    }

    // PREFIX on OverworldButton2D.InstantOpenDoor() -- the SECOND open-path (see Apply).
    // GeneralCampaignStarter.CheckDoors force-opens a level's preceding door via this
    // method, bypassing canOpen and CheckOpen. For a still-locked gated door we skip it so
    // the door stays shut (InstantOpenDoor also SetDoorOpen()s, so skipping keeps it out of
    // OPEN_DOORS). Filtered to our locked OIDs; keyed / non-gated doors defer to the game --
    // including ChestGate's own InstantOpenDoor on already-unlocked crown doors.
    private static bool ButtonInstantOpenPrefix(Il2Cpp.OverworldButton2D __instance)
    {
        try
        {
            if (!IsLockedGatedDoor(__instance, out string oid)) return true;
            if (_loggedInstantBlock.Add(oid))
                Plugin.Log.LogInfo($"[GATE] blocked InstantOpenDoor on locked door '{oid}' (need its key)");
            return false;   // skip original -> door stays shut, not persisted
        }
        catch (Exception e) { Plugin.Log.LogError($"ButtonInstantOpenPrefix: {e}"); }
        return true;   // not gated / unlocked -> open as normal
    }

    // Shared filter for the two door-open prefixes: read a button's OverworldID and report
    // whether it's a still-locked crown-chest door (crowns) or section connector
    // (hard_sections). Both gates return false when their option is off, so a plain install
    // never restricts anything.
    private static bool IsLockedGatedDoor(Il2Cpp.OverworldButton2D button, out string oid)
    {
        oid = null;
        if (button == null) return false;
        try
        {
            var o = button.gameObject.GetComponent<Il2Cpp.OverworldID>();
            if (o != null) oid = o.ID;
        }
        catch { }
        if (string.IsNullOrEmpty(oid)) return false;
        return Mapping.ChestGate.IsLocked(oid) || Mapping.SectionGate.IsLocked(oid);
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

    // PREFIX on a BasePackStarter.StartPack(ContentPack, object[]) override. Episode
    // gate: when the pack is a locked episode, set __result=false and skip the original
    // so nothing starts (the player stays in the hub). Otherwise runs normally. __0 =
    // the ContentPack param (by index; robust vs interop not preserving names).
    private static bool StartPackGatePrefix(Il2Cpp.ContentPack __0, ref bool __result)
    {
        try
        {
            string id = null;
            try { id = __0 != null ? __0.contentPackID : null; } catch { }

            // DEV log of the entry path (only while probing).
            if (Mod.EpisodeProbeEnabled)
            {
                int campaign = 0;
                try { campaign = __0 != null ? (int)__0.campaignType : 0; } catch { }
                Plugin.Log.LogInfo($"[EPISODES] StartPack pack='{id}' campaign={campaign} ({Mapping.CampaignInfo.NameOf(campaign)})");
            }

            // Passive until connected: never touch the game outside an AP session.
            var client = Plugin.Client;
            if (client == null || !client.Connected) return true;

            if (Mapping.EpisodeGate.IsBlocked(id))
            {
                string name = Mapping.EpisodeGate.NameOf(id);
                if (Mapping.EpisodeGate.ShouldLogBlock(id))
                    Plugin.Log.LogInfo($"[EPISODE] blocked locked '{name}' ({id}) — need its Episode Access");
                MessageFeed.PushLocal($"{name} is locked — need its Episode Access");
                __result = false;   // report "didn't start"
                return false;       // skip original -> no load, no transition, no corruption
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"StartPackGatePrefix: {e}"); }
        return true;   // not gated / unlocked -> start as normal
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
