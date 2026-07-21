# WHAT THE GOLF? — reverse-engineering notes

Findings from an Il2CppDumper dump of `GameAssembly.dll` + `global-metadata.dat`
(Unity 2020.3.48f1, IL2CPP, Metadata v27). These are the real class/method/field
names the mod hooks. Regenerate the dump any time with Il2CppDumper; names are
**not obfuscated**.

## Loader + interop naming (READ FIRST)

The mod runs under **MelonLoader** (BepInEx's Dobby detour crashes this game).
MelonLoader's Il2CppInterop **prefixes namespaced game types with `Il2Cpp`**, so
the names below (from the raw dump) map to interop names like this:

| Dump (raw) | Interop (use this in patches/refs) |
|---|---|
| `Core.Level` | **`Il2CppCore.Level`** |
| `Core.LevelData` | `Il2CppCore.LevelData` |
| `GameAnalytics` (no namespace) | `GameAnalytics` (unchanged) |
| `LevelManager` (no namespace) | `LevelManager` (unchanged) |

All three hooks below are **confirmed binding at runtime**.

## How the dump was produced

```
Il2CppDumper.exe "<game>\GameAssembly.dll" \
  "<game>\WHAT THE GOLF_Data\il2cpp_data\Metadata\global-metadata.dat" <outdir>
```
Outputs `dump.cs` (grep this), `DummyDll/` (reference stubs), `il2cpp.h`, `script.json`.

## Core gameplay types

### `Core.Level` : MonoBehaviour  (TypeDefIndex 18502)
The active hole. Singleton-ish via `Level.Instance` / `Level.HasInstance`.

| Member | Signature | Use |
|---|---|---|
| **`Complete`** | `void Complete(string message, Nullable<float> delay, TransitionTextEffect fx, int levelSkips=0)` | **Level-clear hook.** Postfix → send the AP Clear check. |
| **`Fail`** | `void Fail(bool doReload=true, bool showTransition=true, GolfBallController c, float delay=0, bool playFailSFX=true, string message="")` | **Failure hook** (out-of-bounds/water route here). Postfix → DeathLink send. |
| `Abort` / `Restart` / `ResetLevel` | — | level lifecycle |
| `OnComplete` | `Action<Level.CompletedState>` (field 0x40) | event alternative to patching `Complete` |
| `OnFail` | `Action<GolfBallController, string, Level.State>` (0x38) | event alternative to `Fail` |
| `OnOutOfBounds` | `Action` (0xA0) | ball left bounds |
| `levelManager` | `LevelManager` (private, 0x70) | reach `currentLevel` from a Level instance |
| `_goal` | `GolfGoal` (0xB0), prop `Goal` | the hole cup |
| `Instance` | `static Level` (prop) | global access |

Nested: `Level.State` (`NotPlaying`/`Playing`), `Level.CompletedState`,
`Level.FailState`, `Level.RestartState`, `Level.AbortState`.

### `LevelManager` : AddressableSingleton<LevelManager>  (13680, **no namespace**)
| Member | Signature | Use |
|---|---|---|
| `currentLevel` | `LevelData` (private, 0x70) | **the current level's data → its `ID`** |
| `ChallengeSelected` | `Action<string>` (0x50) | which mini-challenge is active (crown detection) |
| `OnLevelLoaded` | `Action<LevelData>` (0x48) | level loaded |
| `IsInLevel()` | `bool` | are we in a hole vs overworld |
| `Instance` | via `AddressableSingleton<T>` | singleton access |

### `Core.LevelData` : ScriptableObject  (18509)
| Field | Type | Use |
|---|---|---|
| **`ID`** | `string` (0x20) | **the level identifier** → map to AP location name |
| `completionID` | `string` (0x50) | |
| `levelChallenges` | `List<LevelData.ChallengeData>` (0x78) | the mini-challenges (each `ChallengeData.ID` string) |
| `CompletedChallenges` | `List<string>` (prop) | crown = count == levelChallenges.Count |
| `isBossBattle` | `bool` (0x81) | boss holes |
| `SceneName` | `string` (prop) | |

### `Core.GolfBallController` : MonoBehaviour  (18494)
The ball. `ShowTransitionOnOutOfBounds`, `NumberOfStrokes`, shoot events. Death
in this game = `Level.Fail` / `OnOutOfBounds`, so hook those rather than the ball.

## Overworld / progression — `GameAnalytics`  (13357, **no namespace**)
A telemetry class of **static, single-fire** handlers — ideal postfix targets:

| Method | Meaning → mod use |
|---|---|
| `OnLevelBegin(LevelData level, string challengeID)` | hole started; `challengeID` tells you if a mini-challenge is active |
| `OnLevelComplete(LevelData level)` | hole finished (alt to `Level.Complete`) |
| `OnLevelStroke()` | a stroke was taken (handy frequent pump tick) |
| **`OnFinalBossCompleted(OverworldGoal goal)`** | **campaign goal** → send Victory |
| `OnHitFlagInOverworld(OverworldGoal goal)` | a flag/goal hit in the hub |
| `OnDoorOpen(string overworldID, bool isCrownDoor, string hubsectionId)` | a hub door opened (crown vs normal) |
| `OnCampaignStarted/Ended`, `OnDailyChallengeEnded`, `OnLevelReset/Abort` | lifecycle |

Related overworld types seen: `OverworldGoal`, `OverworldMainDoorRobot`,
`OverworldMainDoorPlate` (the physical hub doors ItemApplier must open when an
Access key arrives).

## Save data & teleport reachability (probed 2026-07-20)

There is **NO "reached save spots" save set**. `SaveGame.OverWorldData` has a
FIXED, fully-known key vocabulary — `OPEN_DOORS`, `OPEN_MAIN_DOORS`,
`CONSOLES_HIT`, `COMPLETED_LEVELS`, `ACCESSIBLE_LEVELS`, `LEVEL_NOTIFICATIONS`,
`COMPLETED_CHALLENGES`, `UNLOCKED_CHESTS`, `CROWN_AWARENESS_MODE` + scalars. The
only save-position state is a SINGLE `SavePosition` string (the current respawn
spot). So teleport reachability is **RUNTIME-COMPUTED**
(`OverworldLevelSection.GetIsAvailable` / the teleport menu's filter), not a save
set the mod can write to.

The pause-menu teleporter only offers sections whose `saveSpotId` is a
`TELEPORT_*` id. `SAVE_*`/`save_*` sections (e.g. 08C Space = `SAVE_space_01`) are
plain save points reached by WALKING within the chamber — never teleport
destinations. That is why forcing Space's door flag never made it list. This is
why unlocking is done via `ChamberUnlock` (runtime trigger open), not by writing a
save set. (Confirmed by the now-removed `UnlockProbe` read-only diagnostic.)

## Recommended hooks (what the mod patches)

| Purpose | Patch (postfix) | Notes |
|---|---|---|
| Level clear → AP Clear check | `Core.Level:Complete` | read `LevelManager.currentLevel.ID` |
| Crown (all challenges) → AP Crown check | `Core.Level:Complete` | when `CompletedChallenges.Count == levelChallenges.Count` |
| Campaign goal | `GameAnalytics:OnFinalBossCompleted` | send Victory |
| DeathLink send | `GameAnalytics:OnLevelReset` | the AUTOMATIC reset = a real death (OOB/water/lost ball). Static no-arg → safe. **NOT** `Core.Level:Fail` (bad sig). Distinct from `OnLevelManualReset` (player restart) and `OnLevelAbort` (quit) — those must not count. Count-based throttle: broadcast every Nth wipe. |
| DeathLink receive (kill) | invoke `Core.Level.Instance.Restart()` | param-less → safe to invoke via interop; restarts the hole (kills ball). Guard with a suppression window so the induced reset isn't re-broadcast. |
| Main-thread pump | `GameAnalytics:OnLevelStroke` + the above | frequent enough; see note |

## Reading fields at runtime (important IL2CPP caveat)

Patching **locates** methods by string (`AccessTools.Method("Core.Level:Complete")`),
which works without referencing game assemblies. But **reading fields**
(`currentLevel.ID`, challenge lists) reliably needs the **BepInEx-generated interop
assemblies**, produced when you first run the game with BepInExPack IL2CPP
installed (in `<game>\BepInEx\interop\`). Reference those in the `.csproj`, then in
a postfix cast `__instance` to `Core.Level` and read members directly:

```csharp
// after adding <game>\BepInEx\interop\Assembly-CSharp.dll as a Reference:
static void Postfix(Core.Level __instance) {
    var lm = LevelManager.Instance;
    string id = lm?.currentLevel?.ID;
    Plugin.Client?.SendClear(id);
}
```

Do NOT reference Il2CppDumper's `DummyDll` for runtime — those stubs have different
type identity than BepInEx's interop and won't bind. DummyDll is for browsing only.

## For a per-frame pump (optional upgrade)

If you want items applied the instant a key arrives while idle in the hub (not just
on stroke/level events), register your own MonoBehaviour after interop exists:

```csharp
ClassInjector.RegisterTypeInIl2Cpp<PumpBehaviour>();
var go = new GameObject("WtgApPump");
Object.DontDestroyOnLoad(go);
go.AddComponent<PumpBehaviour>();   // its Update() calls Plugin.Client.Tick()
```
