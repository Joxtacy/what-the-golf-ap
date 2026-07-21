using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Computer-boss gating (the "boss_keys" option). Each keyable boss hole is
/// fought behind a real computer door (OverworldMainDoorRobot). The door's
/// <c>bossLevelID</c> equals the boss hole's LevelData.ID, and the apworld
/// exports the map "Computer N Key" -> that id (boss_by_item in wtg_ids.json).
///
/// Until a boss's key arrives we HOLD ITS DOOR SHUT: each frame we force any lit
/// plate on that door back off (OverworldMainDoorPlate.SetState(false)) — the
/// validated lever, run in reverse. So the computer can't activate and the boss
/// hole can't be played. Once the key is received we stop suppressing and the
/// door behaves natively (light the plates, fight the boss).
///
/// Only active when the seed enabled boss keys (slot data) — otherwise no boss
/// keys ever arrive and we must NOT gate anything.
/// </summary>
public static class BossGate
{
    private static bool _enabled;
    private static Dictionary<string, string> _bossByItem = new();  // item -> levelId
    private static readonly HashSet<string> Gated = new();          // all boss levelIds
    private static readonly HashSet<string> Unlocked = new();       // keys received
    private static readonly HashSet<string> _loggedActivate = new();// unlocked doors we've logged lighting
    private static readonly HashSet<string> _loggedSuppress = new();// doors we've logged suppressing
    private static readonly HashSet<string> _loggedSeen = new();    // gated doors we've logged seeing

    private class IdsFile { public Dictionary<string, string> boss_by_item; }

    public static void Load()
    {
        try
        {
            string path = Path.Combine(
                MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_ids.json");
            if (!File.Exists(path)) { Plugin.Log.LogWarning("BossGate: wtg_ids.json missing"); return; }
            var root = JsonConvert.DeserializeObject<IdsFile>(File.ReadAllText(path));
            _bossByItem = root?.boss_by_item ?? new Dictionary<string, string>();
            Gated.Clear();
            foreach (var id in _bossByItem.Values)
                if (!string.IsNullOrEmpty(id)) Gated.Add(id);
            Plugin.Log.LogInfo($"BossGate: {_bossByItem.Count} boss keys mapped.");
        }
        catch (Exception e) { Plugin.Log.LogError($"BossGate.Load: {e}"); }
    }

    /// <summary>Enable gating for this seed (from slot data boss_keys).</summary>
    public static void SetEnabled(bool on)
    {
        _enabled = on;
        Plugin.Log.LogInfo($"BossGate: {(on ? "ENABLED" : "disabled")}.");
    }

    public static bool Handles(string itemName) => _bossByItem.ContainsKey(itemName);

    /// <summary>Mark a boss unlocked from a received "Computer N Key" item.</summary>
    public static void Unlock(string itemName)
    {
        if (_bossByItem.TryGetValue(itemName, out var id) && !string.IsNullOrEmpty(id)
            && Unlocked.Add(id))
        {
            // Tick() lights this boss's plates every frame from now on (self-healing
            // across the door reloads that happen when you teleport into its chamber).
            Plugin.Log.LogInfo($"[BOSS] '{itemName}' -> door {id} unlocked");
        }
    }

    /// <summary>Hold every still-locked boss door shut. Call each frame from OnUpdate.</summary>
    public static void Tick()
    {
        if (!_enabled) return;
        try
        {
            var doors = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldMainDoorRobot>();
            for (int i = 0; i < doors.Length; i++)
            {
                var d = doors[i];
                if (d == null) continue;

                string bid;
                try { bid = d.bossLevelID; } catch { continue; }
                if (string.IsNullOrEmpty(bid) || !Gated.Contains(bid)) continue;

                var plates = d.plates;
                if (plates == null) continue;

                if (Unlocked.Contains(bid))
                {
                    // Keep the computer lit EVERY tick, not just once. The overworld
                    // reloads a chamber's door objects when you teleport in, so a
                    // one-shot re-light misses any door reached after its key arrived
                    // (Western/finale) -> that computer stays dark and unfightable.
                    // Re-lighting continuously is cheap and self-heals across reloads
                    // (same fix as ChamberUnlock's per-tick door re-open).
                    int lit = 0;
                    for (int j = 0; j < plates.Count; j++)
                    {
                        var p = plates[j];
                        if (p != null && !p.isOn) { try { p.SetState(true, false); lit++; } catch { } }
                    }
                    if (lit > 0 && _loggedActivate.Add(bid))
                        Plugin.Log.LogInfo($"[BOSS] lit door {bid} ({lit} plate(s) on — key received)");
                    continue;
                }

                // Locked: hold shut by forcing any lit plate back off.
                if (_loggedSeen.Add(bid))
                    Plugin.Log.LogInfo($"[BOSS] guarding door {bid} (locked)");
                for (int j = 0; j < plates.Count; j++)
                {
                    var p = plates[j];
                    if (p != null && p.isOn)
                    {
                        try { p.SetState(false, false); } catch { }
                        if (_loggedSuppress.Add(bid))
                            Plugin.Log.LogInfo($"[BOSS] held door {bid} shut (plate forced off — need its key)");
                    }
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"BossGate.Tick: {e}"); }
    }
}
