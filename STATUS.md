# Project status & how to resume

An Archipelago (multiworld randomizer) integration for **WHAT THE GOLF?**
Two parts: the **apworld** (`what_the_golf/`, Python — seed generation) and the
**game mod** (`mod/`, C# MelonLoader plugin — connects the running game to a
multiworld). See `README.md` for the overview and `mod/REVERSE_ENGINEERING.md`
for the game internals.

## What works (validated end-to-end)

- **apworld generates** on released **Archipelago 0.6.7** (and `main`/0.6.8):
  180 real single-player campaign holes grouped into 21 theme "areas", 351
  locations, real level names, area-gated logic.
- **Mod loads** under **MelonLoader v0.7.3** (BepInEx's Dobby detour crashes this
  game — see below) and connects to an AP server.
- **Live loop verified in-game:** clear a level → the mod reads the scene →
  sends the Clear (and Crown) check → server registers it under the real name →
  the mod receives items back (including progression Flags).
- **Full game data dumped:** all 642 `LevelData` (`mod/wtg_levels.json`) and the
  real overworld goal/hub-section structure (`mod/wtg_goals.json`).

## The open problem: hard gating — MECHANISM FOUND

**Key discovery:** on a **fresh save** the game DOES physically gate — you're
fenced into the first area until you beat the required levels. (On a 100% save
everything is already unlocked, which is why earlier tests failed. WTG has
multiple save slots, so use a dedicated fresh "dev" slot; the 100% save is safe.)

**The gate lever (chamber level):**
- **`OverworldMainDoorRobot`** — the 9 computer/chamber doors. Fields:
  `bossLevelID`, `bossLevelName`, `List<OverworldMainDoorPlate> plates`,
  `OnOpen`/`OnCompleted`.
- **`OverworldMainDoorPlate`** — the switches. `public bool isOn` +
  **`public void SetState(bool on, bool onLoad=false)`** — directly turn a plate
  on/off. When all a door's plates are on, the door opens.
- So AP gating: keep a chamber's plates off until its Access item arrives, then
  `SetState(true)` to open the door. Suppress native opening by forcing locked
  chambers' plates off each frame (override).

**Design implication:** the computer doors gate at the CHAMBER level (~9-11),
not the 21 theme-areas we currently generate. To wire gating cleanly, restructure
the apworld around the real chambers (map hub sections -> chambers) — this matches
the original vision. Interop types are global -> `Il2Cpp.OverworldMainDoorRobot`,
`Il2Cpp.OverworldMainDoorPlate`.

**Diagnostic dumpers added** (mod/src/Mapping/): GoalDumper (wtg_goals.json),
BridgeDumper (now a general gate/name scanner -> wtg_gates.json). `Mod.GatingEnabled`
const toggles the (old, area-level) GoalGate/EntryGate off for observation.

**Next steps:** (1) validate the lever — call `SetState` on a computer door's
plates and confirm it opens/closes. (2) restructure apworld around chambers.
(3) implement AP item -> open the matching computer door, native opening suppressed.

## How to resume

### Build & run the mod
```
cd mod
dotnet build -c Debug        # builds + deploys to <game>\Mods\ and wtg_ids.json
```
Requires the .NET 6 SDK, MelonLoader installed in the game
(`<game>\version.dll` + `<game>\MelonLoader\`), and the game run once so
MelonLoader generated its interop assemblies. **Kill the game before rebuilding**
(it locks the deployed DLL). The mod's Archipelago dep goes in `<game>\UserLibs\`.

### Building the mod on macOS / Linux (no game install)
The mod is managed .NET, so it *compiles* on any OS (incl. Apple Silicon) — you
just need the reference assemblies. Copy the 9 DLLs listed in `mod/refs/README.md`
into `mod/refs/` (from a Windows/Linux machine that has the game + MelonLoader),
then `cd mod && dotnet build -c Debug`. The csproj auto-switches to `refs/` when
those DLLs are present; Windows builds are unaffected.

Note: you can build there, but **running/testing** the mod still needs the game +
MelonLoader on that platform. On Apple Silicon that's dicey (MelonLoader macOS is
experimental + x64/Rosetta issues) — do live testing on Windows/Linux. The
apworld side (generate/host) runs fine natively on Mac.

### Regenerate the apworld data / IDs
```
python tools/build_levels.py --write   # rebuild what_the_golf/levels.json from wtg_levels.json
python tools/export_ids.py             # rebuild mod/ids.json (locations + area_by_scene)
```

### Generate a seed + host a local server (for testing)
Use an Archipelago source checkout (git clone ArchipelagoMW/Archipelago,
`git checkout 0.6.7`). Copy `what_the_golf/` into its `worlds/`. Then:
```
pip install websockets==13.1            # 0.6.7 REQUIRES this exact version
python Generate.py --player_files_path Players_solo --outputpath output_solo
python MultiServer.py output_solo/AP_*.archipelago     # hosts on :38281
```
The mod currently hardcodes `Connect("localhost", 38281, "Player1")` in
`mod/src/Mod.cs` (replace with a connection UI eventually).

## Key gotchas (hard-won)

- **Loader:** BepInEx 6's Dobby detour hard-crashes this game at engine init.
  Use **MelonLoader**.
- **Il2CppInterop naming:** namespaced game types get an `Il2Cpp` prefix
  (`Core.Level` → `Il2CppCore.Level`); global types keep their name but sit in
  the `Il2Cpp` namespace for C# (`OverworldGoal` → `Il2Cpp.OverworldGoal`).
- **Don't Harmony-patch methods with `Nullable<T>` / by-value struct params**
  (e.g. `Level.Complete`) — the interop trampoline crashes. Hook the static
  `GameAnalytics.OnLevelComplete` (reference param) and read state via `GameState`.
- **Server needs `websockets==13.1`** for AP 0.6.7.

## Suggested next steps

1. Solve the level-exit (try the untried candidates above) — or pivot to
   fresh-save native-progression suppression.
2. Test the whole loop on a **fresh save**.
3. Optional: rebuild `data.py` from the **real hub sections** (`wtg_goals.json`)
   instead of theme groups, for authentic, spatially-coherent areas.
4. In-game connection UI; DeathLink; polish area names.
