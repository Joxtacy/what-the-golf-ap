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

    public override void OnInitializeMelon()
    {
        Plugin.Client = new ArchipelagoClient();
        Mapping.LocationMap.Load();
        Mapping.AreaState.Load();
        GamePatches.Apply(HarmonyInstance);
        Plugin.Log.LogInfo($"WtgArchipelago loaded (game: {Plugin.GameName}).");

        // PROOF-OF-CONCEPT: connect to a local AP server hosting our solo seed.
        // (Replace with an in-game connection UI later.)
        Plugin.Client.Connect("localhost", 38281, "Player1");
    }

    private int _dumpTimer;
    private int _gateTimer;
    private int _unlockTimer;

    // Runs every frame on Unity's main thread -> ideal main-thread pump.
    public override void OnUpdate()
    {
        var client = Plugin.Client;
        client?.Tick();

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

        // Periodically harvest level/goal data (accumulates). ~every 5s.
        if (++_dumpTimer >= 300)
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
