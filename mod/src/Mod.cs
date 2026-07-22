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

    // DEV/TEST: run ShortcutPortalDumper (captures the shortcut-portal topology to
    // wtg_portals.json) to identify the chamber-10 hub portal for PortalGate.TargetKeys.
    // Read-only scan; keep OFF except during a capture session.
    public const bool PortalProbeEnabled = false;

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

    // DEV/TEST: fire trap effects locally via F9-F12 (no server round-trip / no
    // tabbing out to send). Set true only for local testing. Read from Event.current
    // in OnGUI (works regardless of the game's input backend, same as the F8 toggle).
    public const bool DebugTrapHotkeys = false;

    // MelonLoader GUI callback -> draw the connection panel (and read its hotkey).
    public override void OnGUI()
    {
        ConnectionUI.OnGUI();
        ConsoleUI.OnGUI();
        if (DebugTrapHotkeys) HandleTrapHotkeys();
    }

    private static void HandleTrapHotkeys()
    {
        try
        {
            var e = UnityEngine.Event.current;
            if (e == null || e.type != UnityEngine.EventType.KeyDown) return;
            switch (e.keyCode)
            {
                case UnityEngine.KeyCode.F9: Mapping.TrapManager.Apply(Mapping.TrapManager.SlowMo); break;
                case UnityEngine.KeyCode.F10: Mapping.TrapManager.Apply(Mapping.TrapManager.FastForward); break;
                case UnityEngine.KeyCode.F11: Mapping.TrapManager.Apply(Mapping.TrapManager.Mulligan); break;
                case UnityEngine.KeyCode.F12: Mapping.TrapManager.Apply(Mapping.TrapManager.Transmogrify); break;
            }
        }
        catch { }
    }

    // Runs after every game Update -> re-assert the time-warp scale here so we win
    // the race against the game's TimeControl (which rewrites the clock each Update).
    public override void OnLateUpdate() => Mapping.TrapManager.Tick();

    // Overworld polling is EVENT-DRIVEN to keep the idle hub cheap. The expensive
    // FindObjectsOfTypeAll sweeps run at full rate only in a short "burst" after the
    // overworld (re)loads -- returning from a hole, or just after connecting, when a
    // node may have completed and doors/goals were rebuilt -- and otherwise only on a
    // slow heartbeat (to cover teleport door reloads). Standing idle in the hub costs
    // almost nothing, and all scanning is skipped entirely while in a hole.
    // Timestamps are wall-clock (unscaled seconds), NOT frame counts: a frame-count
    // throttle fires ~2.4x more often at 144 Hz than at 60 Hz -- inflating the cost
    // exactly when the framerate is highest.
    private bool _wasInLevel;
    private bool _wasConnected;
    private float _scanUntil;         // active-burst window end
    private float _lastHeartbeat;
    private float _lastUnlock;
    private float _lastGate;
    private float _lastGoal;
    private int _gateSlot;            // rotates Boss/Section/Chest -> one scan per tick
    private int _dumpTimer;           // dumpers are OFF by default; frame-based is fine
    private int _chestDumpTimer;
    private int _portalProbeTimer;

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

        // In-scene "FLAGS x/N" progress counter (door_50/75/100 goals). Shows/hides
        // itself based on connection + goal (FlagGoal > 0); safe to call every frame.
        FlagHud.Tick();

        // Live on-screen feed of AP activity (items/hints/chat/DeathLink). Drains its
        // queue + renders here on the main thread; no-op when disabled or empty.
        MessageFeed.Tick();

        // In-game command console scrollback: drain queued server messages (main thread).
        ConsoleUI.Tick();

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
                MessageFeed.PushLocal("DeathLink received — hole restarted");
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
        if (connected || !string.IsNullOrEmpty(ForceUnlockTrigger))
        {
            float now = UnityEngine.Time.unscaledTime;
            bool inLevel = GameState.IsInLevel();

            // Open a 2s active-scan burst when the overworld (re)loads: returning from
            // a hole (a node may have just completed; doors/goals were rebuilt) or the
            // first frame after connecting (reconcile already-completed nodes).
            if ((_wasInLevel && !inLevel) || (!_wasConnected && connected))
                _scanUntil = now + 2f;
            _wasInLevel = inLevel;
            _wasConnected = connected;

            // In a hole: nothing overworld to poll -> zero scanning (in-level = 144).
            if (!inLevel)
            {
                bool burst = now < _scanUntil;
                // Idle in the hub: sweep only on a slow heartbeat (catches teleport
                // door reloads + keyed-door opening). During a burst, sweep at rate.
                if (burst || now - _lastHeartbeat >= 3f)
                {
                    _lastHeartbeat = now;

                    // Chamber unlocks: re-apply door-open flags (self-heal after a
                    // save/overworld reload). Dev force path for fresh-save tests.
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
                        // One gate per tick (staggered). Each no-ops (no scan) when its
                        // option is off. Boss correctness is the CanBeOpened override,
                        // so this poll is only visual upkeep and can run slowly.
                        if (now - _lastGate >= 0.34f)
                        {
                            _lastGate = now;
                            switch (_gateSlot++ % 4)
                            {
                                case 0: Mapping.BossGate.Tick(); break;
                                case 1: Mapping.SectionGate.Tick(); break;
                                case 2: Mapping.ChestGate.Tick(); break;
                                case 3: Mapping.PortalGate.Tick(); break;
                            }
                        }

                        // Node Clear/Crown detection: only during a burst -- a node can
                        // only newly-complete when you come back from a hole, so idle
                        // never scans goals.
                        if (burst && now - _lastGoal >= 0.5f)
                        {
                            _lastGoal = now;
                            Mapping.GoalWatcher.Scan();
                        }
                    }
                }
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

        // Shortcut-portal capture: its own dev toggle so we can probe portals without
        // the full (laggy) dumper suite. ~every 5s.
        if (PortalProbeEnabled && ++_portalProbeTimer >= 300)
        {
            _portalProbeTimer = 0;
            Mapping.ShortcutPortalDumper.Dump();
        }
    }
}
