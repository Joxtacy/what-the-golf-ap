using Archipelago.MultiClient.Net.Models;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Applies a received Archipelago item. Runs on the main thread (queued from
/// ArchipelagoClient). Access items open the matching in-game door(s) so their
/// holes become teleport-reachable; boss/chest keys release their gates; Flags and
/// the rest are filler (% goals are evaluated AP-server-side, not by the mod).
/// </summary>
public static class ItemApplier
{
    public static void Apply(ItemInfo item)
    {
        string name = item.ItemName;

        if (BossGate.Handles(name))
        {
            // Computer boss key: stop holding that computer's door shut.
            BossGate.Unlock(name);
            MelonLoader.MelonLogger.Msg($"Boss unlocked: {name}");
        }
        else if (ChestGate.Handles(name))
        {
            // Crown-chest key: stop holding that chest's crown door shut.
            ChestGate.Unlock(name);
            MelonLoader.MelonLogger.Msg($"Chest unlocked: {name}");
        }
        else if (EpisodeGate.Handles(name))
        {
            // "<Episode> Episode Access": stop vetoing entry to that episode's pack.
            // Checked BEFORE the generic " Access" branch below, since "X Episode
            // Access" also ends with " Access" (it must NOT route to ChamberUnlock).
            EpisodeGate.Unlock(name);
            MelonLoader.MelonLogger.Msg($"Episode unlocked: {name}");
        }
        else if (name.EndsWith(" Access"))
        {
            // Open the exact in-game door(s) this Access item maps to. Works for
            // both chamber- and section-granularity seeds (unlocks_by_item).
            ChamberUnlock.RequestItem(name);
            MelonLoader.MelonLogger.Msg($"Access unlocked: {name}");
        }
        else if (TrapManager.Handles(name))
        {
            // Trap item (the "traps" option): fire its disruptive/funny effect.
            TrapManager.Apply(name);
        }
        else if (name == "Flag")
        {
            // Progress token for the door_50/75/100 goals. Nothing to apply in-game;
            // just tally it for the on-screen Flag HUD (matches data.py's FLAG_ITEM).
            var data = Plugin.Client?.Data;
            if (data != null) data.FlagsCollected++;
        }
        // else: filler / cosmetic -- nothing to apply.

        Plugin.Log.LogInfo($"AP item applied: {name}");
    }
}
