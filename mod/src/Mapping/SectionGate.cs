using System;
using System.Collections.Generic;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Within-chamber hard-lock (the "hard_sections" option). Sub-areas inside one
/// chamber share an open overworld room, connected by <c>OverworldButton2D</c>
/// connectors that open on ball-contact while <c>canOpen == true</c>. Each
/// connector's <c>OverworldID.ID</c> equals its section's <c>unlockTriggerId</c>
/// (door_space_00, VJ69W, ...). So with plain section access you can physically
/// walk into a not-yet-keyed sibling once any sibling of the chamber is reachable.
///
/// When enabled we close that leak with a two-layer gate (mirrors BossGate/ChestGate):
///  * HARD (correctness): a Harmony prefix on OverworldButton2D.CheckOpen (see
///    GamePatches.ButtonCheckOpenPrefix) returns false for a still-locked connector
///    OID, so it can never open on ball contact -- race-free, and unaffected by a
///    teleport (which skips the overworld poll burst). Driven by IsLocked().
///  * SOFT (visual): <see cref="Tick"/> still polls -- forcing <c>canOpen = false</c>
///    on a locked connector so it SHOWS locked, and restoring <c>canOpen = true</c>
///    once its key arrives.
///
/// Notes:
/// - VALIDATED in-game on a FRESH save (a progressed save re-derives door state
///   from the persistent OPEN_DOORS flag each frame and overwrites the poke).
/// - Softlock-safe: the teleporter lists every keyed section directly (a section is
///   teleport-reachable iff its door is open — see ChamberUnlock), so a locked
///   connector never traps you; locked siblings are out of logic anyway.
/// - Auto-no-op under 'chamber' granularity: a chamber's sub-areas share triggers
///   that unlock together, so IsTriggerUnlocked is true for all of them at once.
/// - Only active when the seed enabled hard_sections (slot data); otherwise the
///   installed mod must not restrict movement at all.
/// </summary>
public static class SectionGate
{
    private static bool _enabled;
    private static HashSet<string> _triggers;
    private static readonly HashSet<string> _loggedBlock = new();
    private static readonly HashSet<string> _loggedOpen = new();

    /// <summary>Enable gating for this seed (from slot data hard_sections).</summary>
    public static void SetEnabled(bool on)
    {
        _enabled = on;
        Plugin.Log.LogInfo($"SectionGate: {(on ? "ENABLED" : "disabled")}.");
    }

    /// <summary>Is this connector OID a section trigger the seed has NOT unlocked?
    /// Used by the event-driven hard gate (the CheckOpen prefix in GamePatches) to
    /// block the natural ball-contact open regardless of the per-tick canOpen poll.
    /// False when hard_sections is off (nothing is gated then).</summary>
    public static bool IsLocked(string oid)
    {
        if (!_enabled || string.IsNullOrEmpty(oid)) return false;
        _triggers ??= ChamberUnlock.AllTriggers();
        return _triggers.Contains(oid) && !ChamberUnlock.IsTriggerUnlocked(oid);
    }

    /// <summary>Hold every locked section's connector shut; reopen keyed ones.
    /// Call periodically from OnUpdate.</summary>
    public static void Tick()
    {
        if (!_enabled) return;
        try
        {
            _triggers ??= ChamberUnlock.AllTriggers();
            if (_triggers.Count == 0) return;

            var btns = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldButton2D>();
            for (int i = 0; i < btns.Length; i++)
            {
                var b = btns[i];
                if (b == null) continue;

                string id = null;
                try { var oid = b.gameObject.GetComponent<Il2Cpp.OverworldID>(); if (oid != null) id = oid.ID; }
                catch { }
                if (string.IsNullOrEmpty(id) || !_triggers.Contains(id)) continue;   // not a section connector

                if (ChamberUnlock.IsTriggerUnlocked(id))
                {
                    // Keyed section: ensure its connector can open (restore if we shut it earlier).
                    if (!b.canOpen)
                    {
                        b.canOpen = true;
                        if (_loggedOpen.Add(id))
                            Plugin.Log.LogInfo($"[SECTIONGATE] opened connector '{id}' (section unlocked)");
                    }
                }
                else if (b.canOpen)
                {
                    // Locked section: hold its connector shut.
                    b.canOpen = false;
                    if (_loggedBlock.Add(id))
                        Plugin.Log.LogInfo($"[SECTIONGATE] holding connector '{id}' shut (section locked)");
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"SectionGate.Tick: {e}"); }
    }
}
