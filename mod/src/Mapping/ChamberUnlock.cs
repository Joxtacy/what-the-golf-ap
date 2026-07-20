using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Non-linear gate unlocking via the game's own save-trigger system.
///
/// An AP "&lt;Something&gt; Access" item opens one or more in-game doors. The apworld
/// tells us exactly which, via the exported map (tools/export_ids.py -&gt;
/// unlocks_by_item in wtg_ids.json): every Access item name -&gt; the section
/// unlockTriggerId(s) it opens. This works identically for both granularities:
///   * chamber granularity: "Chamber 08 Access" -&gt; all four of ch08's triggers.
///   * section granularity:  "Explosion Access"  -&gt; just ["VJ69W"].
///
/// The save records opened doors in two sets:
///   OPEN_DOORS      -> regular section triggers (e.g. "door_platformer_00", "Z4UZC")
///   OPEN_MAIN_DOORS -> computer-door triggers   (e.g. "YX3NO", "9DSBG", "OS8GA")
/// We don't know which set a given trigger belongs to, so we register it in both
/// (harmless) via SaveGame.SetDoorOpen + SetMainDoorOpen, then
/// OverworldManager2d.RefreshDoorsAndGoals() so the overworld/teleport updates.
/// Validated in-game: opening a chamber's triggers made its sub-areas
/// teleport-reachable on a fresh save.
///
/// AP items can arrive before the overworld scene is loaded (e.g. at connect, when
/// all prior items are resent), so we remember requested triggers and (re)apply
/// them from Mod.OnUpdate once an overworld is present.
/// </summary>
public static class ChamberUnlock
{
    // AP Access-item name -> in-game trigger ids it opens (from wtg_ids.json).
    private static Dictionary<string, List<string>> _unlocksByItem = new();
    // Trigger ids requested (union across all received Access items). We do NOT keep a
    // permanent "applied" set: the game reloads the save from disk when the campaign
    // overworld loads (e.g. after the intro), which discards any door we opened too
    // early. So every tick we VERIFY each requested trigger against the live save
    // (GetIsDoorOpen/GetIsMainDoorOpen) and re-open any that isn't set — self-healing.
    private static readonly HashSet<string> Requested = new();
    private static readonly HashSet<string> Available = new();  // sections we've logged as available (once)

    private class IdsFile
    {
        public Dictionary<string, List<string>> unlocks_by_item;
    }

    /// <summary>Every section trigger known to the seed (all door ids we manage).</summary>
    public static HashSet<string> AllTriggers()
    {
        var set = new HashSet<string>();
        foreach (var trigs in _unlocksByItem.Values)
            if (trigs != null)
                foreach (var t in trigs)
                    if (!string.IsNullOrEmpty(t)) set.Add(t);
        return set;
    }

    /// <summary>Has this section trigger been requested by an AP key this session?</summary>
    public static bool IsTriggerUnlocked(string trigger)
        => !string.IsNullOrEmpty(trigger) && Requested.Contains(trigger);

    public static void Load()
    {
        try
        {
            string path = Path.Combine(
                MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_ids.json");
            if (!File.Exists(path)) { Plugin.Log.LogWarning("ChamberUnlock: wtg_ids.json missing"); return; }
            var root = JsonConvert.DeserializeObject<IdsFile>(File.ReadAllText(path));
            _unlocksByItem = root?.unlocks_by_item ?? new Dictionary<string, List<string>>();
            Plugin.Log.LogInfo($"ChamberUnlock: {_unlocksByItem.Count} access->door maps loaded.");
        }
        catch (Exception e) { Plugin.Log.LogError($"ChamberUnlock.Load: {e}"); }
    }

    /// <summary>DEV/TEST: force one raw trigger id open, bypassing the AP item map.
    /// Used to cleanly test teleport-reachability of a specific unreached section on a
    /// fresh save (e.g. "door_platformer_00" for 08A Platformers) without needing the
    /// server to send that section's Access item. Off unless Mod.ForceUnlockTrigger set.</summary>
    public static void ForceTrigger(string trigger)
    {
        if (string.IsNullOrEmpty(trigger)) return;
        if (Requested.Add(trigger))
            Plugin.Log.LogInfo($"[UNLOCK] FORCE trigger '{trigger}' (dev test)");
        TryApply();
    }

    /// <summary>Request the doors for a received "&lt;X&gt; Access" item to open.</summary>
    public static void RequestItem(string accessItemName)
    {
        if (!_unlocksByItem.TryGetValue(accessItemName, out var triggers) || triggers == null)
        {
            Plugin.Log.LogWarning($"[UNLOCK] no door map for item '{accessItemName}'");
            return;
        }
        int added = 0;
        foreach (var t in triggers)
            if (!string.IsNullOrEmpty(t) && Requested.Add(t)) added++;
        if (added > 0)
            Plugin.Log.LogInfo($"[UNLOCK] '{accessItemName}' -> {string.Join(",", triggers)}");
        TryApply();
    }

    /// <summary>Ensure every requested trigger is registered as an open door in the
    /// LIVE save, re-applying any the game has dropped (e.g. after a save reload on
    /// overworld load). Cheap no-op when everything is already open. Call periodically
    /// from OnUpdate.
    ///
    /// GATING MECHANISM (confirmed by disassembling the game, 2026-07-20):
    /// OverworldLevelSection.Refresh() sets isAvailable = IsNullOrWhiteSpace(trigger)
    /// || SaveGame.GetIsDoorOpen(trigger) || SaveGame.GetIsMainDoorOpen(trigger). The
    /// pause-menu teleporter (PopulateMainCampaignSections) shows a button for EVERY
    /// section and only sets isLocked = !isAvailable -- there is NO saveSpotId /
    /// TELEPORT_ filtering. So a section is teleport-reachable iff its trigger is an
    /// open door. Nothing else to do.</summary>
    public static void TryApply()
    {
        try
        {
            if (Requested.Count == 0) return;

            // Need an overworld loaded for the door state / RefreshDoorsAndGoals to matter.
            var mgrs = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldManager2d>();
            if (mgrs.Length == 0) return;

            int opened = 0;
            foreach (var trig in Requested)
            {
                bool isOpen = false;
                try { isOpen = Il2Cpp.SaveGame.GetIsDoorOpen(trig) || Il2Cpp.SaveGame.GetIsMainDoorOpen(trig); }
                catch { }
                if (isOpen) continue;   // already registered (or persisted from a prior write)
                try { Il2Cpp.SaveGame.SetDoorOpen(trig); } catch { }
                try { Il2Cpp.SaveGame.SetMainDoorOpen(trig); } catch { }
                opened++;
            }

            if (opened > 0)
            {
                Plugin.Log.LogInfo($"[UNLOCK] (re)opened {opened} door trigger(s) in live save");
                for (int m = 0; m < mgrs.Length; m++)
                    if (mgrs[m] != null) { try { mgrs[m].RefreshDoorsAndGoals(); } catch { } }
                RefreshUnlockedSections();
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"ChamberUnlock.TryApply: {e}"); }
    }

    /// <summary>Recompute isAvailable for each section whose trigger we've opened, so
    /// the overworld/teleporter reflects it immediately (the menu re-derives it too,
    /// but this updates goal visuals now). isAvailable is purely door-derived, so a
    /// plain Refresh() is sufficient -- no forcing.</summary>
    private static void RefreshUnlockedSections()
    {
        try
        {
            var datas = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldLevelData>();
            for (int a = 0; a < datas.Length; a++)
            {
                var secs = datas[a] != null ? datas[a].Sections : null;
                if (secs == null) continue;
                for (int s = 0; s < secs.Count; s++)
                {
                    var sec = secs[s];
                    if (sec == null || string.IsNullOrEmpty(sec.unlockTriggerId)) continue;
                    if (!Requested.Contains(sec.unlockTriggerId)) continue;
                    try { sec.Refresh(); } catch { }
                    if (sec.isAvailable && Available.Add(sec.unlockTriggerId))
                        Plugin.Log.LogInfo($"[UNLOCK] section '{sec.name}' ({sec.unlockTriggerId}) now teleport-available");
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"ChamberUnlock.RefreshUnlockedSections: {e}"); }
    }
}
