using MelonLoader;
using WtgArchipelago;
using WtgArchipelago.Patches;

[assembly: MelonInfo(typeof(Mod), "WtgArchipelago", "0.1.0", "Joxtacy")]
[assembly: MelonGame("Triband", "WHAT THE GOLF?")]

namespace WtgArchipelago;

/// <summary>
/// MelonLoader entry point. (We use MelonLoader rather than BepInEx because
/// BepInEx's Dobby runtime-invoke detour hard-crashes this game.)
/// </summary>
public class Mod : MelonMod
{
    // DEV/TEST: force one raw section trigger open on load, bypassing AP, for
    // fresh-save reachability/gating tests. "" = off (normal). Keep "".
    public const string ForceUnlockTrigger = "";

    // Periodic data-harvesting dumpers (LevelDumper/GoalDumper/etc.). These call
    // Resources.FindObjectsOfTypeAll (scans ALL loaded objects) + write JSON every
    // few seconds -> a visible frame stall. We already have the data (mod/wtg_*.json),
    // so keep OFF; flip to true + rebuild only when re-capturing game data.
    public const bool DumpersEnabled = false;

    public override void OnInitializeMelon()
    {
        Plugin.Client = new ArchipelagoClient();
        Mapping.LocationMap.Load();
        Mapping.ChamberUnlock.Load();
        Mapping.BossGate.Load();
        Mapping.BossGoal.Load();
        Mapping.ChestGate.Load();
        GamePatches.Apply(HarmonyInstance);

        // Passive until connected: load persisted connection settings + the in-game
        // UI, but do NOT touch the game until the player opts in. An installed mod
        // therefore plays exactly like vanilla unless AP mode is turned on.
        Preferences.Load();
        ConnectionUI.Init();

        Plugin.Log.LogInfo($"WtgArchipelago loaded (game: {Plugin.GameName}). Press F8 for the Archipelago panel.");

        // Only auto-connect if the player has explicitly opted in (persisted).
        if (Preferences.AutoConnect.Value && !string.IsNullOrWhiteSpace(Preferences.Slot.Value))
        {
            Plugin.Log.LogInfo("Auto-connect enabled -> connecting.");
            Plugin.Client.Connect(
                Preferences.Host.Value, Preferences.Port.Value,
                Preferences.Slot.Value, Preferences.Password.Value);
        }
    }

    // MelonLoader GUI callback -> draw the connection panel (and read its hotkey).
    public override void OnGUI() => ConnectionUI.OnGUI();

    private int _dumpTimer;
    private int _unlockTimer;
    private int _bossTimer;
    private int _chestDumpTimer;
    private int _goalTimer;

    // Runs every frame on Unity's main thread -> ideal main-thread pump.
    public override void OnUpdate()
    {
        var client = Plugin.Client;
        client?.Tick();

        // Freeze the game while the connection panel is open (stops the menu ball
        // reacting to clicks/mouse behind the UI). No-op when the panel is closed.
        ConnectionUI.UpdatePause();

        // In-scene TextMeshPro DeathLink counter (uses the game's font). Shows/hides
        // itself based on connection + DeathLink state; safe to call every frame.
        DeathLinkHud.Tick();

        if (client?.DeathLink != null && client.DeathLink.ConsumePending())
        {
            // Incoming DeathLink. If we're in a hole, restart it (kills the ball,
            // wipes hole progress). In the overworld there's nothing to kill, so we
            // drop it. BeginInducedDeath() stops the resulting reset from being
            // re-broadcast as our own wipe (loop suppression).
            if (GameState.IsInLevel())
            {
                client.DeathLink.BeginInducedDeath();
                bool killed = GameState.RestartLevel();
                Plugin.Log.LogInfo($"[DEATHLINK] received -> restart hole (killed={killed})");
            }
            else
            {
                Plugin.Log.LogInfo("[DEATHLINK] received in overworld -> dropped (nothing to kill)");
            }
        }

        // PASSIVE UNTIL CONNECTED: everything below writes game/save state, so it
        // runs only while an AP session is live. With no connection the mod has no
        // side effects (installed == vanilla). The dev ForceUnlockTrigger path is
        // the one exception (bypasses AP for fresh-save reachability tests).
        bool connected = client != null && client.Connected;

        // Apply any AP chamber unlocks that couldn't be applied yet (e.g. items
        // received before the overworld loaded). Cheap no-op when up to date.
        if ((connected || !string.IsNullOrEmpty(ForceUnlockTrigger)) && ++_unlockTimer >= 30)
        {
            _unlockTimer = 0;
            // DEV/TEST: force a chosen section trigger open (teleporter thread).
            if (!string.IsNullOrEmpty(ForceUnlockTrigger))
                Mapping.ChamberUnlock.ForceTrigger(ForceUnlockTrigger);
            if (connected)
                Mapping.ChamberUnlock.TryApply();
        }

        // Gate holding (~6x/sec; each self-no-ops when its option is off):
        //  - BossGate: hold still-locked computer doors shut (boss_keys).
        //  - SectionGate: hold locked within-chamber connectors shut (hard_sections).
        //  - ChestGate: hold locked crown-chest doors shut (crowns).
        if (connected && ++_bossTimer >= 10)
        {
            _bossTimer = 0;
            Mapping.BossGate.Tick();
            Mapping.SectionGate.Tick();
            Mapping.ChestGate.Tick();
        }

        // Node Clear/Crown detection (~3x/sec, overworld only). Reads the per-node
        // OverworldGoal.state so compound "several smaller levels" nodes send their
        // check only when the WHOLE node is done -- not when the first sub-level
        // finishes. Overworld goals only exist while in the hub, so skip in-level.
        if (connected && !GameState.IsInLevel() && ++_goalTimer >= 20)
        {
            _goalTimer = 0;
            Mapping.GoalWatcher.Scan();
        }

        // Periodically harvest level/goal data (accumulates). ~every 5s. OFF by
        // default (DumpersEnabled) — this scan+write is the main source of lag.
        if (DumpersEnabled && ++_dumpTimer >= 300)
        {
            _dumpTimer = 0;
            Mapping.LevelDumper.Dump();
            Mapping.GoalDumper.Dump();
            Mapping.BridgeDumper.Dump();
            Mapping.DoorDumper.Dump();
            Mapping.SectionDumper.Dump();
        }

        // Chest capture runs as part of the dumper suite when enabled.
        if (DumpersEnabled && ++_chestDumpTimer >= 300)
        {
            _chestDumpTimer = 0;
            Mapping.ChestDumper.Dump();
        }
    }
}
