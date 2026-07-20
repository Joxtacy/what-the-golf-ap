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
    // Trigger ids requested (union across all received Access items) and applied.
    private static readonly HashSet<string> Requested = new();
    private static readonly HashSet<string> Applied = new();

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

    /// <summary>Has this section trigger been unlocked by an AP key this session?</summary>
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

    /// <summary>Apply any requested-but-not-yet-applied triggers if we're in an
    /// overworld. Cheap no-op otherwise. Call periodically from OnUpdate.</summary>
    public static void TryApply()
    {
        try
        {
            if (Requested.Count == Applied.Count) return;

            // Need an overworld loaded for RefreshDoorsAndGoals to matter.
            var mgrs = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldManager2d>();
            if (mgrs.Length == 0) return;

            int newlyApplied = 0;
            foreach (var trig in Requested)
            {
                if (Applied.Contains(trig)) continue;
                try { Il2Cpp.SaveGame.SetDoorOpen(trig); } catch { }
                try { Il2Cpp.SaveGame.SetMainDoorOpen(trig); } catch { }
                Applied.Add(trig);
                newlyApplied++;
            }

            if (newlyApplied > 0)
            {
                Plugin.Log.LogInfo($"[UNLOCK] opened {newlyApplied} door trigger(s)");
                for (int m = 0; m < mgrs.Length; m++)
                    if (mgrs[m] != null) { try { mgrs[m].RefreshDoorsAndGoals(); } catch { } }
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"ChamberUnlock.TryApply: {e}"); }
    }
}
