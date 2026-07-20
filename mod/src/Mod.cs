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

    // Periodic data-harvesting dumpers (LevelDumper/GoalDumper/etc.). These call
    // Resources.FindObjectsOfTypeAll (scans ALL loaded objects) + write JSON every
    // few seconds -> a visible frame stall. We already have the data (mod/wtg_*.json),
    // so keep OFF; flip to true + rebuild only when re-capturing game data.
    public const bool DumpersEnabled = false;

    // Within-chamber hard-lock spike (VALIDATED, 2026-07-20): forcing a locked
    // section connector's canOpen=false hard-gates the sub-area on a FRESH save.
    // Kept OFF; productionize as a real SectionGate option. See ActiveGateTest.cs.
    public const bool WalkProbeEnabled = false;      // read-only gating probe
    public const bool ActiveGateTestEnabled = false; // active force-lock experiment

    public override void OnInitializeMelon()
    {
        Plugin.Client = new ArchipelagoClient();
        Mapping.LocationMap.Load();
        Mapping.AreaState.Load();
        Mapping.ChamberUnlock.Load();
        Mapping.BossGate.Load();
        GamePatches.Apply(HarmonyInstance);
        Plugin.Log.LogInfo($"WtgArchipelago loaded (game: {Plugin.GameName}).");

        // PROOF-OF-CONCEPT: connect to a local AP server hosting our solo seed.
        // (Replace with an in-game connection UI later.)
        Plugin.Client.Connect("localhost", 38281, "Player1");
    }

    private int _dumpTimer;
    private int _gateTimer;
    private int _unlockTimer;
    private int _bossTimer;
    private int _walkTimer;

    // Runs every frame on Unity's main thread -> ideal main-thread pump.
    public override void OnUpdate()
    {
        var client = Plugin.Client;
        client?.Tick();

        // Spike diagnostic: periodic read-only overworld gating snapshot
        // (WalkGateProbe) for the within-chamber hard-lock investigation. ~every
        // 12s when enabled; correlate before/after with when a section is unlocked.
        if (WalkProbeEnabled && ++_walkTimer >= 720)
        {
            _walkTimer = 0;
            Mapping.WalkGateProbe.Snapshot();
        }

        // Spike: actively force a chosen section locked, to test feasibility.
        if (ActiveGateTestEnabled) Mapping.ActiveGateTest.Tick();

        if (client?.DeathLink != null && client.DeathLink.ConsumePending())
        {
            // TODO: reset/kill the ball on an incoming DeathLink.
        }

        if (ProbeEnabled) Mapping.UnlockProbe.RunOnce();

        // Apply any AP chamber unlocks that couldn't be applied yet (e.g. items
        // received before the overworld loaded). Cheap no-op when up to date.
        if (++_unlockTimer >= 30)
        {
            _unlockTimer = 0;
            Mapping.ChamberUnlock.TryApply();
        }

        // Boss gating: hold still-locked computer doors shut (~6x/sec; no-op when
        // the seed didn't enable boss keys).
        if (++_bossTimer >= 10)
        {
            _bossTimer = 0;
            Mapping.BossGate.Tick();
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
    }
}
