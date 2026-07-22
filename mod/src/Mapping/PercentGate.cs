using System;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Goal gating for the door_50/75/100 goals. The overworld has three real completion
/// doors, each an <c>OverworldButton2DPercentage</c> (field <c>PercentageThreshold</c>
/// 0.5/0.75/1.0) that natively opens once you've collected that fraction of the game's
/// flags. Behind each open door sits an <c>OverworldMainButton</c> ("Speedrun N%",
/// linked back via <c>PreviousDoor</c>) that the player presses.
///
/// In the randomizer that press IS the win. For the seed's goal tier we:
///   (1) HARD-LOCK the % door on the AP Flag count, not the game's native flag count.
///       The door opens through the game's own CheckOpen when <c>GetFlagsLeft() &lt;= 0</c>,
///       so a Harmony prefix (see GamePatches) makes GetFlagsLeft return 0 once the AP
///       target is reached, else the remaining count -- keeping it shut otherwise.
///       (CanDoorBeOpened, despite the name, does NOT gate the open path -- verified
///       in-game: the door stayed shut with it true and GetFlagsLeft &gt; 0.)
///   (2) fire Victory when the button INSIDE is pressed (its OnCollisionEnter2D),
///       matched to our tier via PreviousDoor identity.
///
/// Armed only for a door_* seed (Data.Goal in 50/75/100). Matching is by
/// PercentageThreshold + PreviousDoor object identity -- no OverworldID map needed.
/// </summary>
public static class PercentGate
{
    private static bool _enabled;
    private static float _target;          // 0.5 / 0.75 / 1.0 (the goal's door tier)
    private static bool _won;              // victory sent this session (fire once)
    private static bool _loggedDoor;       // one-time info log for our tier's door
    private static bool _loggedButton;     // one-time info log for the inside button

    /// <summary>Arm from slot data. goal: 1=door_50, 2=door_75, 3=door_100 (see
    /// ArchipelagoData); anything else disarms (campaign/all_bosses don't use this).</summary>
    public static void SetGoal(int goal)
    {
        switch (goal)
        {
            case ArchipelagoData.GoalDoor50: _target = 0.5f; _enabled = true; break;
            case ArchipelagoData.GoalDoor75: _target = 0.75f; _enabled = true; break;
            case ArchipelagoData.GoalDoor100: _target = 1.0f; _enabled = true; break;
            default: _enabled = false; _target = 0f; break;
        }
        Plugin.Log.LogInfo(_enabled
            ? $"PercentGate: ARMED for {_target * 100f:0}% door goal."
            : "PercentGate: disabled (not a door goal).");
    }

    /// <summary>Forget victory/log state so a reconnect re-reconciles (matches
    /// GoalWatcher.Reset()).</summary>
    public static void Reset()
    {
        _won = false;
        _loggedDoor = _loggedButton = false;
    }

    // --- Flag-count state (source of truth = received AP Flag items) -------------

    /// <summary>Have enough AP Flags been collected to satisfy the goal?</summary>
    private static bool FlagsReached()
    {
        var d = Plugin.Client?.Data;
        return d != null && d.FlagGoal > 0 && d.FlagsCollected >= d.FlagGoal;
    }

    /// <summary>Normalise a PercentageThreshold to a 0..1 fraction (authored as 0.5 in
    /// the data, but tolerate a 0..100 authoring too).</summary>
    private static float Frac(float threshold) => threshold > 1.5f ? threshold / 100f : threshold;

    /// <summary>Is this percentage button the one matching the seed's goal tier?</summary>
    private static bool IsGoalTier(Il2Cpp.OverworldButton2DPercentage b)
    {
        if (b == null) return false;
        try { return Math.Abs(Frac(b.PercentageThreshold) - _target) < 0.01f; }
        catch { return false; }
    }

    // --- Lock (called from the GetFlagsLeft prefix) ------------------------------
    // The % door opens through the game's own CheckOpen when GetFlagsLeft() <= 0. So for
    // OUR goal-tier door we report flags-left off the AP Flag count: 0 once the target is
    // reached (door opens naturally), else the positive remaining count (door stays
    // shut) -- which the door's own "flags left" label then shows, in AP terms.

    /// <summary>If this percentage button is our goal tier, compute the AP-based
    /// flags-left and return true (caller overrides GetFlagsLeft with it). Off-tier or
    /// disarmed -> false (the game's native count stands).</summary>
    public static bool TryFlagsLeft(Il2Cpp.OverworldButton2DPercentage b, out int flagsLeft)
    {
        flagsLeft = 0;
        if (!_enabled || !IsGoalTier(b)) return false;
        var d = Plugin.Client?.Data;
        if (FlagsReached())
        {
            flagsLeft = 0;
        }
        else
        {
            int remaining = (d?.FlagGoal ?? 0) - (d?.FlagsCollected ?? 0);
            flagsLeft = remaining > 0 ? remaining : 1;   // never 0 while still locked
        }
        if (!_loggedDoor)
        {
            _loggedDoor = true;
            Plugin.Log.LogInfo($"[PCT] gating {_target * 100f:0}% door "
                + $"(flags {d?.FlagsCollected}/{d?.FlagGoal}, flagsLeft={flagsLeft}).");
        }
        return true;
    }

    // --- Door label (called from the UpdateStatus postfix) -----------------------
    // The % door's requirement label (missingFlagsText, aka the "RequirementText" TMP)
    // natively shows "<nativePct>/<threshold>%" from the game's own completion calc --
    // NOT the AP Flag count. For our goal-tier door we overwrite it with AP progress
    // "<collected>/<needed>", right after the game's UpdateStatus sets it.

    /// <summary>Retarget the goal-tier % door's requirement label to AP progress
    /// (collected/needed) instead of the game's native completion %. No-op for off-tier
    /// doors (they keep the native label) and when disarmed.</summary>
    public static void ApplyDoorText(Il2Cpp.OverworldButton2DPercentage b)
    {
        if (!_enabled || b == null || !IsGoalTier(b)) return;
        try
        {
            var mt = b.missingFlagsText;
            if (mt == null) return;
            var d = Plugin.Client?.Data;
            string s = $"{d?.FlagsCollected ?? 0}/{d?.FlagGoal ?? 0}";
            if (mt.text != s) mt.text = s;
        }
        catch { }
    }

    // --- Victory (called from the inside button's collision postfix) -------------

    /// <summary>Is this main button the one INSIDE our goal-tier door (linked via
    /// PreviousDoor) and currently enabled (a real, valid press)?</summary>
    public static bool IsInsideButton(Il2Cpp.OverworldMainButton button)
    {
        if (!_enabled || button == null) return false;
        try
        {
            if (!button.isEnabled) return false;                 // only a live press counts
            var prev = button.PreviousDoor;                      // the door it sits behind
            if (prev == null) return false;
            bool ours = IsGoalTier(prev.TryCast<Il2Cpp.OverworldButton2DPercentage>());
            if (ours && !_loggedButton)
            {
                _loggedButton = true;
                Plugin.Log.LogInfo($"[PCT] inside button armed behind the {_target * 100f:0}% door.");
            }
            return ours;
        }
        catch { return false; }
    }

    /// <summary>The inside button was pressed -> the goal is met. Fire Victory once.
    /// Guarded on the Flag count too (defence in depth: the door can't have opened
    /// without it, but never report a goal we haven't actually earned).</summary>
    public static void OnInsideButtonPressed()
    {
        if (!_enabled || _won) return;
        if (!FlagsReached())
        {
            Plugin.Log.LogWarning("[PCT] inside button pressed but flag count not reached — ignoring.");
            return;
        }
        _won = true;
        Plugin.Log.LogInfo($"[PCT] {_target * 100f:0}% door button pressed -> goal reached!");
        MessageFeed.PushLocal($"{_target * 100f:0}% goal complete!");
        Plugin.Client?.SendVictory();
    }
}
