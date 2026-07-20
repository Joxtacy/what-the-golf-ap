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
    // Legacy gating experiments (door-plate suppression / area goal-hiding). Kept
    // off: non-linear teleport unlocking (ChamberUnlock) is the real mechanism.
    public const bool LegacyGatingEnabled = false;

    // Read-only save-vocabulary diagnostic (UnlockProbe). Off outside R&D.
    public const bool ProbeEnabled = false;

    // DEV/TEST: force one raw section trigger open on load, bypassing AP, for
    // fresh-save reachability/gating tests. "" = off (normal). Keep "".
    public const string ForceUnlockTrigger = "";

    // Periodic data-harvesting dumpers (LevelDumper/GoalDumper/etc.). These call
    // Resources.FindObjectsOfTypeAll (scans ALL loaded objects) + write JSON every
    // few seconds -> a visible frame stall. We already have the data (mod/wtg_*.json),
    // so keep OFF; flip to true + rebuild only when re-capturing game data.
    public const bool DumpersEnabled = false;

    // Within-chamber hard-lock: productionized as SectionGate (the "hard_sections"
    // apworld option; enabled from slot data). WalkGateProbe is the read-only gating
    // diagnostic from the spike, kept OFF.
    public const bool WalkProbeEnabled = false;      // read-only gating probe

    // DEV/DIAGNOSTIC: one-shot dump of computer-door world positions (BossGate.
    // LogDoors) to locate a hard-to-find boss. Keep OFF in normal builds.
    public const bool BossLocateEnabled = false;

    // DEV SPIKE for the "crowns" option (roadmap #3/#4): ChestProbe logs the live
    // chest/crown-door inventory + polls chest-open detection. ChestBlockTest also
    // forces every crown door shut (canOpen=false) to prove the block lever on
    // "Main Crown Door" variants. Keep both OFF in normal builds.
    public const bool ChestProbeEnabled = false;   // DEV: crowns spike. Superseded by ChestGate.
    public const bool ChestBlockTest = false;       // DEV pass 2 (done): force crown doors shut.

    public override void OnInitializeMelon()
    {
        Plugin.Client = new ArchipelagoClient();
        Mapping.LocationMap.Load();
        Mapping.AreaState.Load();
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
    private int _gateTimer;
    private int _unlockTimer;
    private int _bossTimer;
    private int _walkTimer;
    private int _chestTimer;
    private int _chestDumpTimer;

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

        // Spike diagnostic: periodic read-only overworld gating snapshot
        // (WalkGateProbe) for the within-chamber hard-lock investigation. ~every
        // 12s when enabled; correlate before/after with when a section is unlocked.
        if (WalkProbeEnabled && ++_walkTimer >= 720)
        {
            _walkTimer = 0;
            Mapping.WalkGateProbe.Snapshot();
        }

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

        if (ProbeEnabled) Mapping.UnlockProbe.RunOnce();

        // DEV SPIKE (crowns option): chest/crown-door probe. Read-mostly; runs
        // regardless of connection like the other diagnostics. ~6x/sec.
        if (ChestProbeEnabled && ++_chestTimer >= 10)
        {
            _chestTimer = 0;
            Mapping.ChestProbe.Tick(ChestBlockTest);
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
            if (BossLocateEnabled) Mapping.BossGate.LogDoors();
        }

        if (LegacyGatingEnabled)
        {
            // Hard gate: kick the player out of a locked level (every frame).
            Mapping.EntryGate.Tick();

            // Gating: hide overworld goals whose area isn't unlocked (~3x/sec).
            if (++_gateTimer >= 20)
            {
                _gateTimer = 0;
                Mapping.GoalGate.Apply();
            }
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

        // Chest capture can run alone (under the crowns spike) without paying the
        // full dumper-suite lag; also runs as part of the suite when enabled.
        if ((DumpersEnabled || ChestProbeEnabled) && ++_chestDumpTimer >= 300)
        {
            _chestDumpTimer = 0;
            Mapping.ChestDumper.Dump();
        }
    }
}
