using System;
using UnityEngine;

namespace WtgArchipelago.Mapping;

/// <summary>
/// DEV/TEST probe (read-only-ish) to answer ONE design question before deciding
/// whether "ball shapes as a cosmetic collectible" is worth building: how many of
/// the 15 <see cref="Il2Cpp.Transmogrif.BALLSHAPES"/> actually RENDER in the
/// overworld when set via <c>OverworldBallManager.Load</c>?
///
/// Background (from the traps work): the in-LEVEL ball shape is level-scripted
/// (Transmogrif trigger zones) and can't be safely force-set — so ball shape can't
/// be progression. The OVERWORLD ball shape IS globally settable (cosmetic), but
/// only shapes that have an overworld prefab show anything. Traps confirmed 3 work
/// (bread/saw/companioncube) and 4 don't (goo/turkey/snowball/pizza); the other 8
/// were never tested. A cosmetic collectible is only worth it if enough render.
///
/// There's no cheap static answer (the prefab loads async via addressables), so
/// this is EMPIRICAL: each keypress advances to the next shape and calls Load(), so
/// you can watch which ones actually appear. It also logs a best-effort renderer
/// check as a hint (may lag, since Load animates the swap over a frame or two — the
/// VISUAL is the ground truth). This only ever calls Load() (the same lever the
/// Transmogrify trap uses); it writes no game/save state.
///
/// Gated by <see cref="Mod.BallShapeProbeEnabled"/> (OFF by default) + fired from a
/// hotkey. Stand in the overworld and press the key to step through the shapes.
/// Remove once the collectible decision is made.
/// </summary>
public static class BallShapeProbe
{
    // All 15 shapes, in enum order (Transmogrif.BALLSHAPES).
    private static readonly Il2Cpp.Transmogrif.BALLSHAPES[] All =
    {
        Il2Cpp.Transmogrif.BALLSHAPES.ball,          // 0
        Il2Cpp.Transmogrif.BALLSHAPES.fish,          // 1
        Il2Cpp.Transmogrif.BALLSHAPES.bread,         // 2  (traps: renders)
        Il2Cpp.Transmogrif.BALLSHAPES.boat,          // 3
        Il2Cpp.Transmogrif.BALLSHAPES.speedboat,     // 4
        Il2Cpp.Transmogrif.BALLSHAPES.saw,           // 5  (traps: renders)
        Il2Cpp.Transmogrif.BALLSHAPES.companioncube, // 6  (traps: renders)
        Il2Cpp.Transmogrif.BALLSHAPES.endball,       // 7
        Il2Cpp.Transmogrif.BALLSHAPES.islandBall,    // 8
        Il2Cpp.Transmogrif.BALLSHAPES.waterBall,     // 9
        Il2Cpp.Transmogrif.BALLSHAPES.puckBall,      // 10
        Il2Cpp.Transmogrif.BALLSHAPES.snowball,      // 11 (traps: nothing)
        Il2Cpp.Transmogrif.BALLSHAPES.pizza,         // 12 (traps: nothing)
        Il2Cpp.Transmogrif.BALLSHAPES.turkey,        // 13 (traps: nothing)
        Il2Cpp.Transmogrif.BALLSHAPES.goo,           // 14 (traps: nothing)
    };

    private static int _idx = -1;

    /// <summary>Advance to the next shape and Load() it. Call from a hotkey while
    /// standing in the overworld. Logs the shape + a best-effort render hint.</summary>
    public static void CycleNext()
    {
        try
        {
            if (!Il2Cpp.OverworldBallManager.HasInstance)
            {
                Plugin.Log.LogInfo("[BALLPROBE] not in the overworld -> stand in the hub and try again");
                return;
            }
            var mgr = Il2Cpp.OverworldBallManager.Instance;
            if (mgr == null) return;

            _idx = (_idx + 1) % All.Length;
            var shape = All[_idx];

            mgr.Load(shape);

            int readback = -1;
            try { readback = mgr.GetCurrentShapeAsInt(); } catch { }
            Plugin.Log.LogInfo(
                $"[BALLPROBE] {_idx + 1}/{All.Length} Load('{shape}' = {(int)shape}) " +
                $"-> readback int {readback}. {InspectBall(mgr)}  " +
                "(LOOK at the ball: does it render? note it down)");
        }
        catch (Exception e) { Plugin.Log.LogError($"BallShapeProbe.CycleNext: {e}"); }
    }

    /// <summary>Best-effort renderer hint for the current overworld ball. NOT
    /// authoritative — Load() swaps the ball over a frame or two, so a shape that
    /// DOES render can still read 0 sprites the instant after Load. Use the eyes.</summary>
    private static string InspectBall(Il2Cpp.OverworldBallManager mgr)
    {
        try
        {
            var ball = mgr.Ball;
            if (ball == null) return "ball=null";
            var srs = ball.GetComponentsInChildren<SpriteRenderer>(true);
            int total = srs != null ? srs.Length : 0;
            int withSprite = 0;
            for (int i = 0; i < total; i++)
            {
                var sr = srs[i];
                if (sr != null && sr.sprite != null && sr.enabled) withSprite++;
            }
            return $"renderers: {withSprite}/{total} active w/ sprite";
        }
        catch (Exception e) { return $"inspect-failed ({e.GetType().Name})"; }
    }
}
