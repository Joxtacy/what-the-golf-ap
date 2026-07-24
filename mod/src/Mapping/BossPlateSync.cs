using System;
using System.Collections.Generic;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Native computer-door plate self-heal, ACTIVE ONLY WHEN boss_keys IS OFF.
///
/// With boss keys off the mod doesn't manage computer doors at all -- they rely on
/// the game's native "area complete -> plate lights (isOn=true)" behaviour, and the
/// door opens once all its plates are on. But that lighting is event-driven AND the
/// plate's isOn is PERSISTED (the door has a saveFormatUnlockedPlate save key). In
/// this randomizer you complete a chamber by teleporting into its holes, so the area
/// can hit LEVELS_COMLETE/CHALLENGES_COMPLETE while the door object isn't loaded /
/// subscribed -> the plate never lights, never gets written "on", and on the next
/// load the game does NOT re-derive isOn from the current areaState. Result: a fully
/// finished chamber's computer stays dark and unfightable forever.
///
/// Live-diagnosed 2026-07-24 (Western / Computer 8, via BossDoorProbe): all four
/// WETERN_01 plates read areaState=CHALLENGES_COMPLETE with every goal won+crowned,
/// yet isOn=false and door state=Closed (no stale "completed"/main-door flag).
///
/// Heal: each tick, light any plate whose area is natively complete (areaState >=
/// LEVELS_COMLETE) but still dark. This only lights plates the player genuinely
/// earned -- it never forces an incomplete area on -- so it can't make a boss
/// fightable out of logic; it just restores the native activation the reload dropped.
/// No-op when boss_keys is ON (BossGate + the CanBeOpened override own the doors then)
/// and self-heals across the door reloads that happen on teleport (like BossGate).
/// </summary>
public static class BossPlateSync
{
    private static readonly HashSet<string> _loggedLit = new();

    public static void Tick()
    {
        if (BossGate.Enabled) return;   // boss_keys on -> BossGate owns the doors
        try
        {
            var doors = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldMainDoorRobot>();
            for (int i = 0; i < doors.Length; i++)
            {
                var d = doors[i];
                if (d == null) continue;
                var plates = d.plates;
                if (plates == null) continue;

                for (int j = 0; j < plates.Count; j++)
                {
                    var p = plates[j];
                    if (p == null || p.isOn) continue;          // already lit or missing

                    var pi = p.plateInfo;
                    if (pi == null) continue;

                    // areaState >= LEVELS_COMLETE(2) means the game itself counts the
                    // area done (all levels, and CHALLENGES_COMPLETE(3) = all crowns).
                    int state;
                    try { state = (int)pi.areaState; } catch { continue; }
                    if (state < 2) continue;                    // area not complete -> leave dark

                    try
                    {
                        p.SetState(true, false);                // the validated lever
                        string bid = d.bossLevelID;
                        if (!string.IsNullOrEmpty(bid) && _loggedLit.Add(bid + ":" + j))
                            Plugin.Log.LogInfo(
                                $"[PLATESYNC] lit plate {j} of door {bid} (area complete but was dark)");
                    }
                    catch { }
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"BossPlateSync.Tick: {e}"); }
    }
}
