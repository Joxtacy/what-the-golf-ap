using System;
using UnityEngine;
using WtgArchipelago.Patches;   // GameState (IsInLevel / RestartLevel)

namespace WtgArchipelago.Mapping;

/// <summary>
/// Trap items (the apworld "traps" option). A received trap fires a short,
/// disruptive/funny effect. Routing is purely by item NAME, so the constants
/// below MUST match what_the_golf/data.py TRAP_ITEMS exactly. Every effect is
/// wrapped in try/catch and no-ops safely if its game lever isn't reachable
/// (e.g. a Mulligan received in the overworld has no hole to restart) — a trap
/// can never softlock or crash the game.
///
/// Levers (from the IL2CPP dump + in-game validation):
///  * Mulligan       -> Il2CppCore.Level.Instance.Restart()  (same lever DeathLink uses)
///  * Slow-Mo / Fast -> the in-level ball is driven by the Chronos time library
///                      (NOT UnityEngine.Time — timeScale=0 freezes the menu ball but
///                      not the in-hole ball). So we scale every Chronos GLOBAL clock's
///                      localTimeScale via Timekeeper.clocks (the game's per-aim
///                      TimeControl only drives its own LOCAL clock, so it won't fight
///                      us). Also nudge Time.timeScale for the non-Chronos bits. Reverted
///                      after WarpSeconds real seconds. (FindObjectsOfTypeAll<GlobalClock>
///                      found 0 in-game, hence going through Timekeeper.)
///  * Transmogrify   -> Il2Cpp.OverworldBallManager.Instance.Load(BALLSHAPES) (hub only,
///                      cosmetic; validated in-game). Rolls a shape != the current one.
/// </summary>
public static class TrapManager
{
    // Trap item names — keep in sync with data.py TRAP_ITEMS.
    public const string Mulligan = "Mulligan Trap";
    public const string SlowMo = "Slow-Mo Trap";
    public const string FastForward = "Fast-Forward Trap";
    public const string Transmogrify = "Transmogrify Trap";

    private static readonly string[] Names = { Mulligan, SlowMo, FastForward, Transmogrify };

    // Time-warp tuning.
    private const float SlowFactor = 0.35f;   // slow-mo speed multiplier
    private const float FastFactor = 2.2f;    // fast-forward speed multiplier
    private const float WarpSeconds = 10f;    // how long a warp lasts (real seconds)

    // Overworld ball shapes that actually have an overworld prefab, so Load() shows
    // a distinct, non-broken ball. FULL 15-shape sweep done in-game via BallShapeProbe
    // (2026-07-23): only ball(0)/bread(2)/speedboat(4)/saw(5)/companioncube(6)/
    // endball(7) render as a distinct skin. fish + islandBall/waterBall/puckBall/
    // snowball/pizza/turkey/goo (8..14) show NOTHING (no overworld asset); boat(3)
    // renders as the plain ball + a grey dot/wake (not visually distinct); speedboat
    // reverts to the plain ball when "docked". For the Transmogrify TRAP we use the
    // distinct, self-contained ones (skip the default ball, boat's non-distinctness,
    // and speedboat's docking revert).
    private static readonly Il2Cpp.Transmogrif.BALLSHAPES[] SafeShapes =
    {
        Il2Cpp.Transmogrif.BALLSHAPES.bread,
        Il2Cpp.Transmogrif.BALLSHAPES.saw,
        Il2Cpp.Transmogrif.BALLSHAPES.companioncube,
        Il2Cpp.Transmogrif.BALLSHAPES.endball,
    };

    private static readonly System.Random Rng = new();

    // Active time-warp bookkeeping. Timed with DateTime.UtcNow (real wall-clock) —
    // BOTH Time.unscaledTime and accumulating Time.unscaledDeltaTime read back wrong
    // through the interop bridge in-game (reverted the warp in a fraction of a second).
    // DateTime is pure .NET, immune to any Unity/interop time weirdness.
    private static bool _warpActive;
    private static DateTime _warpEndUtc;
    private static float _warpFactor = 1f;   // re-asserted every frame while active
    // Clocks captured ONCE at warp start (via one FindObjectsOfTypeAll), then re-set
    // each frame without rescanning -- a per-frame scan tanked FPS (144 -> ~80).
    private static readonly System.Collections.Generic.List<Il2CppChronos.Clock> _warpClocks = new();

    /// <summary>Is this received item name one of our traps?</summary>
    public static bool Handles(string name) => Array.IndexOf(Names, name) >= 0;

    /// <summary>Apply a trap effect by name. Runs on the main thread (queued by
    /// ItemApplier). Safe to call for any string; unknown names no-op.</summary>
    public static void Apply(string name)
    {
        try
        {
            switch (name)
            {
                case Mulligan: DoMulligan(); break;
                case SlowMo: DoTimeWarp(SlowFactor, "Slow-Mo"); break;
                case FastForward: DoTimeWarp(FastFactor, "Fast-Forward"); break;
                case Transmogrify: DoTransmogrify(); break;
                default: return;
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"TrapManager.Apply({name}): {e}"); }
    }

    /// <summary>Drive the active time-warp. Call every frame from Mod.OnLateUpdate
    /// (AFTER the game's own Update, so we win the race against TimeControl, which
    /// re-writes the clock scale each frame). Near-free when no warp is active.</summary>
    public static void Tick()
    {
        if (!_warpActive) return;
        if (DateTime.UtcNow >= _warpEndUtc)
        {
            _warpActive = false;
            ApplyToCached(1f);          // restore normal speed
            _warpClocks.Clear();
            Plugin.Log.LogInfo("[TRAP] time warp ended -> normal speed");
            return;
        }
        // Re-assert the scale every frame -- the game's TimeControl resets the clock
        // to 1.0 each frame for its aim slow-mo, so a one-shot set gets wiped. Cheap:
        // just re-sets the cached clocks, no per-frame object scan.
        ApplyToCached(_warpFactor);
    }

    // --- effects -------------------------------------------------------------

    private static void DoMulligan()
    {
        if (GameState.IsInLevel())
        {
            bool ok = GameState.RestartLevel();
            Plugin.Log.LogInfo($"[TRAP] Mulligan -> hole restarted (ok={ok})");
        }
        else
        {
            Plugin.Log.LogInfo("[TRAP] Mulligan received in overworld -> nothing to restart (dropped)");
        }
    }

    private static void DoTimeWarp(float factor, string label)
    {
        _warpFactor = factor;
        _warpActive = true;
        _warpEndUtc = DateTime.UtcNow.AddSeconds(WarpSeconds);
        CacheWarpClocks();                       // one scan; Tick() re-uses the cache
        int clocks = ApplyToCached(factor);
        Plugin.Log.LogInfo($"[TRAP] {label} x{factor} for {WarpSeconds}s ({clocks} clock(s))");
    }

    /// <summary>Capture the Chronos clocks to bend, ONCE per warp. Chronos GlobalClocks
    /// aren't Unity components here (FindObjectsOfTypeAll finds 0), so we reach them
    /// through the Timeline components that ARE on GameObjects (the ball, camera, ...):
    /// each Timeline.clock hands back its actual Clock object, whose localTimeScale is
    /// publicly settable. Cached so Tick() can re-assert without rescanning the object
    /// graph every frame (that scan is what dropped the framerate).</summary>
    private static void CacheWarpClocks()
    {
        _warpClocks.Clear();
        var timelines = Resources.FindObjectsOfTypeAll<Il2CppChronos.Timeline>();
        for (int i = 0; i < timelines.Length; i++)
        {
            var t = timelines[i];
            if (t == null) continue;
            Il2CppChronos.Clock clk = null;
            try { clk = t.clock; } catch { }
            if (clk != null) _warpClocks.Add(clk);
        }
    }

    /// <summary>Set the cached clocks' localTimeScale (+ Time.timeScale for the
    /// non-Chronos bits). Cheap -- no object scan. Returns the count set.</summary>
    private static int ApplyToCached(float factor)
    {
        Time.timeScale = factor;   // standard physics / camera / UI (non-Chronos bits)
        int n = 0;
        for (int i = 0; i < _warpClocks.Count; i++)
        {
            try { _warpClocks[i].localTimeScale = factor; n++; } catch { }
        }
        return n;
    }

    private static void DoTransmogrify()
    {
        // Only touch the ball in the overworld. OverworldBallManager is a persistent
        // singleton, so HasInstance stays true DURING a level too -- calling Load() on
        // the ball mid-hole corrupts level state and freezes the win-screen transition
        // (live-observed 2026-07-24). Gate on IsInLevel() like DoMulligan does.
        if (GameState.IsInLevel())
        {
            Plugin.Log.LogInfo("[TRAP] Transmogrify received in a level -> dropped (would freeze the win screen)");
            return;
        }
        if (!Il2Cpp.OverworldBallManager.HasInstance)
        {
            Plugin.Log.LogInfo("[TRAP] Transmogrify received but not in the overworld -> dropped");
            return;
        }
        var mgr = Il2Cpp.OverworldBallManager.Instance;
        if (mgr == null) return;

        // Roll a land-safe shape that isn't the one we're already wearing.
        int current = -1; try { current = mgr.GetCurrentShapeAsInt(); } catch { }
        Il2Cpp.Transmogrif.BALLSHAPES shape;
        int guard = 0;
        do { shape = SafeShapes[Rng.Next(SafeShapes.Length)]; }
        while ((int)shape == current && ++guard < 16);

        mgr.Load(shape);
        Plugin.Log.LogInfo($"[TRAP] Transmogrify -> overworld ball is now '{shape}' (was int {current})");
    }
}
