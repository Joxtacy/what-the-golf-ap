using System;
using System.Collections.Generic;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Enforces AP chamber gating using the computer doors (the validated lever).
///
/// The campaign is a linear chain of chambers 10 -> 00 (10 = free start). A
/// "computer" door (OverworldMainDoorRobot) sits at a chamber's EXIT: its plates
/// belong to chamber C and, once lit, let you fight the boss and advance to C-1.
/// So the door in chamber C gates progression into chamber C-1.
///
/// AP rule: chamber C-1 is reachable only with the "Chamber (C-1) Access" item.
/// Therefore, until that item arrives, we hold chamber C's exit door SHUT by
/// forcing any lit plate back off (OverworldMainDoorPlate.SetState(false)). Once
/// unlocked, we stop suppressing and the door behaves natively (light plates by
/// playing the chamber, beat the boss, advance).
///
/// Chambers whose exit has no computer door (e.g. 10->09) aren't gated this way —
/// a known first-version gap; refine after live testing on a fresh save.
/// </summary>
public static class ChamberGate
{
    // Chambers the player is allowed to be in. Chamber 10 (start) is always open.
    private static readonly HashSet<int> Unlocked = new() { 10 };

    /// <summary>Unlock a chamber from an AP "Chamber NN Access" item.</summary>
    public static void Unlock(int chamber)
    {
        if (Unlocked.Add(chamber))
            Plugin.Log.LogInfo($"[GATE] chamber {chamber:D2} unlocked");
    }

    public static bool IsUnlocked(int chamber) => Unlocked.Contains(chamber);

    /// <summary>Parse the chamber number out of an area name like "Chamber 07".</summary>
    public static bool TryParseChamber(string areaName, out int chamber)
    {
        chamber = -1;
        if (string.IsNullOrEmpty(areaName)) return false;
        int i = 0;
        while (i < areaName.Length && !char.IsDigit(areaName[i])) i++;
        int start = i;
        while (i < areaName.Length && char.IsDigit(areaName[i])) i++;
        return start < i && int.TryParse(areaName.Substring(start, i - start), out chamber);
    }

    /// <summary>Run each frame: hold locked chambers' exit doors shut.</summary>
    public static void Tick()
    {
        try
        {
            var doors = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldMainDoorRobot>();
            for (int i = 0; i < doors.Length; i++)
            {
                var d = doors[i];
                var plates = d != null ? d.plates : null;
                if (plates == null || plates.Count == 0) continue;

                int doorChamber = DoorChamber(d);
                if (doorChamber < 0) continue;

                // This door leads into chamber (doorChamber - 1).
                if (IsUnlocked(doorChamber - 1)) continue;   // allowed through: native behaviour

                // Locked: force any lit plate back off (only when needed, to avoid SFX spam).
                for (int j = 0; j < plates.Count; j++)
                {
                    var p = plates[j];
                    if (p != null && p.isOn) p.SetState(false, false);
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"ChamberGate.Tick: {e}"); }
    }

    // A door's plates all belong to one chamber; read it from the plate area id.
    private static int DoorChamber(Il2Cpp.OverworldMainDoorRobot d)
    {
        var plates = d.plates;
        if (plates == null) return -1;
        for (int j = 0; j < plates.Count; j++)
        {
            var p = plates[j];
            var info = p != null ? p.plateInfo : null;
            if (info == null) continue;
            string area;
            try { area = info.Name.ToString(); } catch { continue; }
            if (TryParseChamber(area, out int c)) return c;
        }
        return -1;
    }
}
