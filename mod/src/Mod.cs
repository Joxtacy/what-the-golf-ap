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

    // Wall-clock timestamps (unscaled seconds) for throttling the expensive
    // FindObjectsOfTypeAll polling. TIME-based, not frame-based: a frame-count
    // throttle fires ~2.4x more often at 144 Hz than at the 60 Hz it was tuned for,
    // multiplying the scan cost exactly when the framerate is highest.
    private float _lastUnlock;
    private float _lastGate;
    private float _lastGoal;
    private int _gateSlot;            // rotates Boss/Section/Chest -> one scan per tick
    private int _dumpTimer;           // dumpers are OFF by default; frame-based is fine
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

        // PASSIVE UNTIL CONNECTED: everything below reads/writes game state, so it
        // runs only while an AP session is live (installed == vanilla otherwise). The
        // dev ForceUnlockTrigger path is the one exception.
        //
        // PERFORMANCE: every helper below calls Resources.FindObjectsOfTypeAll, which
        // walks the ENTIRE loaded object graph and marshals it across the IL2CPP
        // bridge -- by far the biggest cost the mod adds. So we
        //   (1) throttle by WALL-CLOCK time, not frame count (144 Hz would otherwise
        //       scan 2.4x more than 60 Hz),
        //   (2) skip it all while playing a hole -- every target is an overworld
        //       object, so there's nothing to do in-level -- keeping in-level FPS
        //       near vanilla, and
        //   (3) stagger the three gates so at most ONE scan runs per tick.
        bool connected = client != null && client.Connected;
        if ((connected || !string.IsNullOrEmpty(ForceUnlockTrigger)) && !GameState.IsInLevel())
        {
            float now = UnityEngine.Time.unscaledTime;

            // Re-apply chamber unlocks (~2x/sec): self-heals after the save reload on
            // overworld load, and covers items received before the overworld existed.
            if (now - _lastUnlock >= 0.5f)
            {
                _lastUnlock = now;
                if (!string.IsNullOrEmpty(ForceUnlockTrigger))   // DEV/TEST only
                    Mapping.ChamberUnlock.ForceTrigger(ForceUnlockTrigger);
                if (connected)
                    Mapping.ChamberUnlock.TryApply();
            }

            if (connected)
            {
                // One gate per slot (~3 slots/sec -> each gate ~1x/sec). Each no-ops
                // (no scan) when its option is off. Boss gating correctness is the
                // CanBeOpened override now, so this poll is only visual upkeep.
                if (now - _lastGate >= 0.34f)
                {
                    _lastGate = now;
                    switch (_gateSlot++ % 3)
                    {
                        case 0: Mapping.BossGate.Tick(); break;
                        case 1: Mapping.SectionGate.Tick(); break;
                        case 2: Mapping.ChestGate.Tick(); break;
                    }
                }

                // Node Clear/Crown detection (~2x/sec) from per-node OverworldGoal.state.
                if (now - _lastGoal >= 0.5f) { _lastGoal = now; Mapping.GoalWatcher.Scan(); }
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

        // Chest capture runs as part of the dumper suite when enabled.
        if (DumpersEnabled && ++_chestDumpTimer >= 300)
        {
            _chestDumpTimer = 0;
            Mapping.ChestDumper.Dump();
        }
    }
}
