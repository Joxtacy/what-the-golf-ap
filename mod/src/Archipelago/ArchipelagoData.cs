using System.Collections.Generic;

namespace WtgArchipelago;

/// <summary>
/// Session state that must survive reconnects / save-reloads. In a finished mod
/// you would persist this to disk keyed by (seed, slot) so a reconnect doesn't
/// re-grant every item. See the item-index guard in ArchipelagoClient.
/// </summary>
public class ArchipelagoData
{
    // Goal values must match Options.py's Goal choice (fill_slot_data sends the int).
    public const int GoalCampaign = 0;
    public const int GoalDoor50 = 1;
    public const int GoalDoor75 = 2;
    public const int GoalDoor100 = 3;
    public const int GoalAllBosses = 4;

    public string Host = "localhost";
    public int Port = 38281;
    public string SlotName = "Player1";
    public string Password = null;

    public string Seed;

    // Highest received-item index already applied. Items at or below this were
    // already granted; skip them when the server replays history on connect.
    public long ItemIndex = -1;

    // Locations already reported this session (avoid duplicate sends).
    public readonly HashSet<long> CheckedLocations = new();

    // Slot data delivered by the apworld's fill_slot_data().
    public bool DeathLinkEnabled;
    public int Goal;
    public string AreaAccess = "section";
    public bool BossKeysEnabled;
    public bool HardSectionsEnabled;
}
