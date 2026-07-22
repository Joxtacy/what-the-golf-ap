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

    // Highest received-item index already applied. Items at or below this were
    // already granted; skip them when the server replays history on connect.
    public long ItemIndex = -1;

    // Locations already reported this session (avoid duplicate sends).
    public readonly HashSet<long> CheckedLocations = new();

    // Slot data delivered by the apworld's fill_slot_data().
    public bool DeathLinkEnabled;
    public int DeathLinkAmnesty = 10;   // local wipes per outgoing DeathLink (Options range 1..30); fallback only — real seeds always send it
    public int Goal;
    public bool BossKeysEnabled;
    public bool HardSectionsEnabled;
    public bool CrownsEnabled;

    // Flags needed to win a door_50/75/100 goal (0 for campaign/all_bosses); from
    // slot data. Drives the on-screen Flag HUD (see FlagHud); the actual win
    // condition is enforced AP-server-side.
    public int FlagGoal;
    // Flags received this session, counted by ItemApplier. The server replays the
    // full item history on connect (ItemIndex starts at -1), so this rebuilds from
    // scratch each session.
    public int FlagsCollected;
}
