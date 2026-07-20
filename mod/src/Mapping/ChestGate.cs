using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Crown-chest gating + detection (the "crowns" option).
///
/// Every overworld crown chest is an AP location. Most sit behind a crown door
/// (an <c>OverworldButton2D</c> whose <c>OverworldID.ID</c> is a CROWN_* id); in
/// vanilla that door opens once you own enough crowns. Here we OVERRIDE that: the
/// door is held shut (<c>canOpen=false</c>, the validated lever -- same as
/// SectionGate) until the matching "<Area> Chest Key" arrives from the multiworld,
/// then released. Freely-reachable chests have no door/key -- just a check.
///
/// Two maps come from the apworld (wtg_ids.json, via tools/export_ids.py):
///   chest_doors_by_item : "Cars Chest Key" -> "CROWN_CARS" (door to hold shut)
///   chest_loc_by_oid    : "CHEST_CARS"     -> "Cars Chest" (AP location to send)
///
/// Detection: GamePatches patches ChestManager.Chest_OnPostChestOpenFirst, whose
/// Chest arg gives us chest.id.ID (a CHEST_* id) -> ReportOpened -> send the check.
///
/// Only active when the seed enabled crowns (slot data); otherwise no chest keys
/// arrive, no chest locations exist, and we must gate/send nothing.
/// </summary>
public static class ChestGate
{
    private static bool _enabled;
    private static Dictionary<string, string> _doorByItem = new();   // item -> CROWN_ id
    private static Dictionary<string, string> _locByOid = new();      // CHEST_ id -> loc name
    private static readonly HashSet<string> Gated = new();            // all CROWN_ ids we guard
    private static readonly HashSet<string> Unlocked = new();         // CROWN_ ids whose key arrived
    private static readonly HashSet<string> _loggedBlock = new();
    private static readonly HashSet<string> _loggedOpen = new();
    private static readonly HashSet<string> _loggedSent = new();

    private class IdsFile
    {
        public Dictionary<string, string> chest_doors_by_item;
        public Dictionary<string, string> chest_loc_by_oid;
    }

    public static void Load()
    {
        try
        {
            string path = Path.Combine(
                MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_ids.json");
            if (!File.Exists(path)) { Plugin.Log.LogWarning("ChestGate: wtg_ids.json missing"); return; }
            var root = JsonConvert.DeserializeObject<IdsFile>(File.ReadAllText(path));
            _doorByItem = root?.chest_doors_by_item ?? new Dictionary<string, string>();
            _locByOid = root?.chest_loc_by_oid ?? new Dictionary<string, string>();
            Gated.Clear();
            foreach (var door in _doorByItem.Values)
                if (!string.IsNullOrEmpty(door)) Gated.Add(door);
            Plugin.Log.LogInfo($"ChestGate: {_doorByItem.Count} keyed chests, {_locByOid.Count} chest locations.");
        }
        catch (Exception e) { Plugin.Log.LogError($"ChestGate.Load: {e}"); }
    }

    /// <summary>Enable gating for this seed (from slot data crowns).</summary>
    public static void SetEnabled(bool on)
    {
        _enabled = on;
        Plugin.Log.LogInfo($"ChestGate: {(on ? "ENABLED" : "disabled")}.");
    }

    public static bool Handles(string itemName) => _doorByItem.ContainsKey(itemName);

    /// <summary>Mark a chest's door unlocked from a received "<Area> Chest Key".</summary>
    public static void Unlock(string itemName)
    {
        if (_doorByItem.TryGetValue(itemName, out var door) && !string.IsNullOrEmpty(door)
            && Unlocked.Add(door))
            Plugin.Log.LogInfo($"[CHEST] '{itemName}' -> door {door} unlocked");
    }

    /// <summary>A chest just opened (from the Harmony hook). Send its AP check if the
    /// crowns option is on and we know the location.</summary>
    public static void ReportOpened(string oid)
    {
        if (!_enabled || string.IsNullOrEmpty(oid)) return;
        if (!_locByOid.TryGetValue(oid, out var locName) || string.IsNullOrEmpty(locName))
        {
            Plugin.Log.LogInfo($"[CHEST] opened unknown chest '{oid}' (no AP location -- ignored)");
            return;
        }
        long id = LocationMap.IdByName(locName);
        Plugin.Client?.SendCheck(id);
        if (_loggedSent.Add(oid))
            Plugin.Log.LogInfo($"[CHEST] opened '{oid}' -> sent '{locName}' ({id})");
    }

    /// <summary>Hold every still-locked crown door shut; release keyed ones. Call
    /// periodically from OnUpdate (self-healing across overworld reloads, like
    /// BossGate/SectionGate).</summary>
    public static void Tick()
    {
        if (!_enabled || Gated.Count == 0) return;
        try
        {
            var btns = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldButton2D>();
            for (int i = 0; i < btns.Length; i++)
            {
                var b = btns[i];
                if (b == null) continue;

                // Only the live door instances (FindObjectsOfTypeAll also returns
                // inactive templates whose canOpen is just the serialized default).
                bool active;
                try { active = b.gameObject.activeInHierarchy; } catch { continue; }
                if (!active) continue;

                string id = null;
                try { var oid = b.gameObject.GetComponent<Il2Cpp.OverworldID>(); if (oid != null) id = oid.ID; }
                catch { }
                if (string.IsNullOrEmpty(id) || !Gated.Contains(id)) continue;

                if (Unlocked.Contains(id))
                {
                    // Key received: FORCE the door open. canOpen=true alone is not
                    // enough -- a crown door also checks crown-count vs its
                    // requirement (CanDoorBeOpened/GetFlagsLeft), so on a fresh save
                    // (0 crowns) it stays shut. Call InstantOpenDoor() to bypass the
                    // crown gate. Guard on IsOpenOrOpening so we only (re)open when
                    // it's actually closed -- self-heals across overworld reloads
                    // (teleport) without re-triggering the open every tick.
                    try { if (!b.canOpen) b.canOpen = true; } catch { }
                    bool open;
                    try { open = b.IsOpenOrOpening; } catch { open = false; }
                    if (!open)
                    {
                        try { b.InstantOpenDoor(); } catch { }
                        if (_loggedOpen.Add(id))
                            Plugin.Log.LogInfo($"[CHEST] opened crown door '{id}' (key received)");
                    }
                }
                else if (b.canOpen)
                {
                    try { b.canOpen = false; } catch { }
                    if (_loggedBlock.Add(id))
                        Plugin.Log.LogInfo($"[CHEST] holding crown door '{id}' shut (need its key)");
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"ChestGate.Tick: {e}"); }
    }
}
