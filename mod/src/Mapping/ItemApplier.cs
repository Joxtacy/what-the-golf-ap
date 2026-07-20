using Archipelago.MultiClient.Net.Models;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Applies a received Archipelago item. Runs on the main thread (queued from
/// ArchipelagoClient). Access items open the matching in-game door(s) so their
/// holes become teleport-reachable; Flags count toward the % goals; rest = filler.
/// </summary>
public static class ItemApplier
{
    public static void Apply(ItemInfo item)
    {
        string name = item.ItemName;

        if (name == "Flag")
        {
            AreaState.AddFlag();
        }
        else if (BossGate.Handles(name))
        {
            // Computer boss key: stop holding that computer's door shut.
            BossGate.Unlock(name);
            MelonLoader.MelonLogger.Msg($"Boss unlocked: {name}");
        }
        else if (name.EndsWith(" Access"))
        {
            // Open the exact in-game door(s) this Access item maps to. Works for
            // both chamber- and section-granularity seeds (unlocks_by_item).
            ChamberUnlock.RequestItem(name);
            MelonLoader.MelonLogger.Msg($"Access unlocked: {name}");
        }
        // else: filler / cosmetic -- nothing to apply.

        Plugin.Log.LogInfo($"AP item applied: {name}");
    }
}
