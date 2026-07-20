using System;
using System.Collections.Generic;

namespace WtgArchipelago.Mapping;

/// <summary>
/// DEV SPIKE for the "crowns" option (roadmap #3/#4). Read-mostly diagnostic that
/// validates the three unknowns before we commit the apworld + a ChestGate:
///
///  1. INVENTORY (one-shot): logs every Chest (id, IsUnlocked, pos) and every
///     crown-door OverworldButton2D (id, name, canOpen), plus ChestManager.
///     OpenedChests — so we can see the real gated set and door ids in a live run.
///
///  2. DOOR-BLOCK LEVER (Mod.ChestBlockTest): every tick forces canOpen=false on
///     every crown-door button. This is the SectionGate lever aimed at "Main Crown
///     Door" variants — confirms a crown room can be held shut (and that the game
///     doesn't immediately re-open it). Player observes the room stays sealed.
///
///  3. OPEN DETECTION: polls ChestManager.OpenedChests each tick and logs any
///     increment — the candidate signal for "send this chest's AP check" (vs.
///     Harmony-patching ChestManager.Chest_OnPostChestOpenFirst later).
///
/// Entirely gated by Mod.ChestProbeEnabled; keep OFF in normal builds. Writes NO
/// files and (unless ChestBlockTest) mutates NO state — safe to leave in tree like
/// UnlockProbe / WalkGateProbe.
/// </summary>
public static class ChestProbe
{
    private static bool _inventoried;
    private static int _lastOpened = -1;
    private static readonly HashSet<string> _blocked = new();

    public static void Tick(bool blockTest)
    {
        try
        {
            // Wait for the overworld to stream in before the one-shot inventory —
            // on a cold start OnUpdate fires while 0 chests are loaded (the pass-1
            // "=== 0 Chest ===" mistiming). Only latch once we actually see some.
            if (!_inventoried)
            {
                var loaded = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.Chest>();
                if (loaded != null && loaded.Length > 0) { Inventory(); _inventoried = true; }
            }

            if (blockTest) BlockCrownDoors();

            // Open detection: ChestManager.OpenedChests is a running count.
            var mgrs = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.ChestManager>();
            if (mgrs.Length > 0 && mgrs[0] != null)
            {
                int opened;
                try { opened = mgrs[0].OpenedChests; } catch { return; }
                if (_lastOpened < 0) _lastOpened = opened;
                else if (opened != _lastOpened)
                {
                    Plugin.Log.LogInfo($"[CHESTPROBE] OpenedChests {_lastOpened} -> {opened} (a chest just opened)");
                    _lastOpened = opened;
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"ChestProbe.Tick: {e}"); }
    }

    private static void Inventory()
    {
        try
        {
            var chests = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.Chest>();
            Plugin.Log.LogInfo($"[CHESTPROBE] === {chests.Length} Chest object(s) ===");
            for (int i = 0; i < chests.Length; i++)
            {
                var c = chests[i];
                if (c == null) continue;
                string id = "?"; try { id = c.id != null ? c.id.ID : "(null id)"; } catch { }
                bool unlocked = false; try { unlocked = c.IsUnlocked(); } catch { }
                bool active = false; try { active = c.gameObject.activeInHierarchy; } catch { }
                var p = c.transform.position;
                Plugin.Log.LogInfo($"[CHESTPROBE] chest id='{id}' unlocked={unlocked} active={active} pos=({p.x:F1},{p.y:F1},{p.z:F1}) parent='{Parent(c)}'");
            }

            var btns = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldButton2D>();
            int crown = 0;
            for (int i = 0; i < btns.Length; i++)
            {
                var b = btns[i];
                if (b == null) continue;
                string name; try { name = b.gameObject.name; } catch { continue; }
                if (name == null || name.IndexOf("Crown", StringComparison.OrdinalIgnoreCase) < 0) continue;
                crown++;
                string id = "?"; try { var oid = b.gameObject.GetComponent<Il2Cpp.OverworldID>(); id = oid != null ? oid.ID : "(no OverworldID)"; } catch { }
                bool canOpen = false; try { canOpen = b.canOpen; } catch { }
                bool active = false; try { active = b.gameObject.activeInHierarchy; } catch { }
                var p = b.transform.position;
                Plugin.Log.LogInfo($"[CHESTPROBE] crown-door id='{id}' name='{name}' canOpen={canOpen} active={active} pos=({p.x:F1},{p.y:F1},{p.z:F1})");
            }
            Plugin.Log.LogInfo($"[CHESTPROBE] === {crown} crown-door button(s) ===");

            var plates = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.PlateInfoManager>();
            for (int i = 0; i < plates.Length; i++)
            {
                var pm = plates[i];
                if (pm == null) continue;
                string area, chest;
                try { area = pm.Name.ToString(); chest = pm.chestID.ToString(); } catch { continue; }
                Plugin.Log.LogInfo($"[CHESTPROBE] area '{area}' -> chest '{chest}'");
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"ChestProbe.Inventory: {e}"); }
    }

    // Force every crown-door button shut this tick (validates the block lever).
    private static void BlockCrownDoors()
    {
        var btns = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldButton2D>();
        for (int i = 0; i < btns.Length; i++)
        {
            var b = btns[i];
            if (b == null) continue;
            string name; try { name = b.gameObject.name; } catch { continue; }
            if (name == null || name.IndexOf("Crown", StringComparison.OrdinalIgnoreCase) < 0) continue;
            try
            {
                // Only the live (active) door instances matter — FindObjectsOfTypeAll
                // also returns inactive templates whose canOpen is just the default.
                if (!b.gameObject.activeInHierarchy) continue;
                b.canOpen = false;   // force shut every tick; we win against the game's own re-open
                string id = "?"; try { var oid = b.gameObject.GetComponent<Il2Cpp.OverworldID>(); id = oid != null ? oid.ID : name; } catch { }
                if (_blocked.Add(id))
                    Plugin.Log.LogInfo($"[CHESTPROBE] BLOCK holding active crown door '{id}' shut (canOpen forced false)");
            }
            catch { }
        }
    }

    private static string Parent(UnityEngine.Component c)
    { try { return c.transform != null && c.transform.parent != null ? c.transform.parent.name : "?"; } catch { return "?"; } }
}
