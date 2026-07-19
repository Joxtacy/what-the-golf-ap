using Archipelago.MultiClient.Net.Models;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Applies a received Archipelago item. Runs on the main thread (queued from
/// ArchipelagoClient). Access items unlock areas (GoalGate then reveals their
/// overworld goals); Flags count toward the % goals; the rest is filler.
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
        else if (name.EndsWith(" Access"))
        {
            string area = name.Substring(0, name.Length - " Access".Length);
            AreaState.Unlock(area);   // legacy scene->area tracking
            if (ChamberGate.TryParseChamber(area, out int chamber))
                ChamberUnlock.Request(chamber);   // make it teleport-reachable
            MelonLoader.MelonLogger.Msg($"Chamber unlocked: {area}");
        }
        // else: filler / cosmetic -- nothing to apply.

        Plugin.Log.LogInfo($"AP item applied: {name}");
    }
}
