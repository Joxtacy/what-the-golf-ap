using System;
using System.Collections.Generic;

namespace WtgArchipelago.Mapping;

/// <summary>
/// QoL: keep the post-intro (chamber-10) hub portal open from the start, so you can
/// warp back to the hub — e.g. to check the flag (%) doors — without first beating a
/// chamber's computer boss.
///
/// The game gates each shortcut portal with a <c>ShortCutPortalBoxEnableOnComputer</c>
/// (a <c>computer</c> boss door + a <c>toEnableOnComputerOpen</c> <c>SimplePortal</c>);
/// the box only enables the portal once its computer opens. The box has no per-frame
/// Update — it sets the portal's initial (disabled) state at Init and flips it on via
/// Computer_OnOpen — so we just force the targeted portal active ourselves and re-assert
/// it each tick (self-healing across the door/overworld reloads that happen on teleport,
/// same pattern as BossGate/ChamberUnlock).
///
/// TARGETING: we match a box's portal by identifier (its OverworldID, its GameObject
/// name, or the gating computer's bossLevelID) against <see cref="TargetKeys"/>. These
/// come from the ShortcutPortalDumper capture (wtg_portals.json) — see notes below.
///
/// Purely client-side QoL: it only opens a shortcut BACK toward the hub, which unlocks
/// no gated content, so this is a local preference (Preferences.OpenHubPortal), not an
/// apworld option — no generation/logic impact.
/// </summary>
public static class PortalGate
{
    // The shortcut portal(s) to keep open. A box matches if ANY of these strings
    // equals its portal's OverworldID, is contained in the portal's GameObject name,
    // or equals the gating computer's bossLevelID.
    //
    // Identified from the ShortcutPortalDumper capture (wtg_portals.json):
    // "Shortcut portal back to lab" in "Hub Section - archive intro" (the intro /
    // chamber-10 zone). Its portal is OverworldID "MYK31", it has NO computer gate,
    // and it links to the central "Winner area" hub (where the % / flag doors are) —
    // so opening it lets you warp to the hub from the very start.
    public static readonly string[] TargetKeys = new string[]
    {
        "MYK31",
    };

    private static readonly HashSet<string> _loggedOpen = new();

    /// <summary>Force the targeted hub portal(s) open. Call periodically from OnUpdate
    /// (while connected). No-ops when the preference is off or no target is set.</summary>
    public static void Tick()
    {
        if (!Preferences.OpenHubPortal.Value) return;
        if (TargetKeys.Length == 0) return;
        try
        {
            var boxes = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.ShortCutPortalBoxEnableOnComputer>();
            for (int i = 0; i < boxes.Length; i++)
            {
                var box = boxes[i];
                if (box == null) continue;

                Il2Cpp.SimplePortal portal;
                try { portal = box.toEnableOnComputerOpen; } catch { continue; }
                if (portal == null) continue;

                if (!Matches(box, portal)) continue;

                Open(portal);
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"PortalGate.Tick: {e}"); }
    }

    private static bool Matches(Il2Cpp.ShortCutPortalBoxEnableOnComputer box, Il2Cpp.SimplePortal portal)
    {
        // Portal OverworldID (most stable key).
        string pid = null;
        try { var oid = portal.gameObject.GetComponent<Il2Cpp.OverworldID>(); if (oid != null) pid = oid.ID; }
        catch { }

        // Portal object name.
        string pname = null;
        try { pname = portal.gameObject.name; } catch { }

        // Gating computer's boss id.
        string cboss = null;
        try { var c = box.computer; if (c != null) cboss = c.bossLevelID; } catch { }

        for (int i = 0; i < TargetKeys.Length; i++)
        {
            string k = TargetKeys[i];
            if (string.IsNullOrEmpty(k)) continue;
            if (!string.IsNullOrEmpty(pid) && pid == k) return true;
            if (!string.IsNullOrEmpty(cboss) && cboss == k) return true;
            if (!string.IsNullOrEmpty(pname) && pname.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private static void Open(Il2Cpp.SimplePortal portal)
    {
        string id;
        try { var oid = portal.gameObject.GetComponent<Il2Cpp.OverworldID>(); id = oid != null ? oid.ID : portal.gameObject.name; }
        catch { id = "?"; }

        bool acted = false;

        // 1) Make sure the object + component are live.
        try { if (!portal.gameObject.activeSelf) { portal.gameObject.SetActive(true); acted = true; } } catch { }
        try { if (!portal.enabled) { portal.enabled = true; acted = true; } } catch { }

        // 2) If it's a "hidden until entered from the far side" portal, reveal this end
        //    so it's visible + usable (the game exposes this as a public call — it's
        //    what the linked portal invokes when opened from the other side).
        try { if (portal.hideUntilUsedFromOtherSide) portal.OpenPortalFromOtherSide(); } catch { }

        if (acted && _loggedOpen.Add(id))
            Plugin.Log.LogInfo($"[PORTAL] forced hub portal '{id}' open (QoL)");
    }
}
