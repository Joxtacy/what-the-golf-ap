# Project status & how to resume

An Archipelago (multiworld randomizer) integration for **WHAT THE GOLF?**
Two parts: the **apworld** (`what_the_golf/`, Python — seed generation) and the
**game mod** (`mod/`, C# MelonLoader plugin — connects the running game to a
multiworld). See `README.md` for the overview and `mod/REVERSE_ENGINEERING.md`
for the game internals.

## What works (validated end-to-end)

- **apworld generates** on released **Archipelago 0.6.7** (and `main`/0.6.8):
  the 132 real campaign holes in the 11 real chambers (10→00), 251 locations
  (Clears + Crowns), real level names, non-linear per-chamber gating.
- **Mod loads** under **MelonLoader v0.7.3** (BepInEx's Dobby detour crashes this
  game — see below) and connects to an AP server.
- **Full randomizer loop verified in-game (non-linear):** clear a level → the mod
  sends the Clear (+Crown) check → server registers it → the mod receives items,
  including `Chamber NN Access` → that chamber becomes teleport-reachable → hop
  there and play. (See the gating section below for the unlock mechanism.)
- **Full game data dumped:** all 642 `LevelData` (`mod/wtg_levels.json`), the real
  overworld goal/hub structure (`mod/wtg_goals.json`), and the authoritative
  chamber/section structure (`mod/wtg_sections.json`) + door topology
  (`mod/wtg_doors.json`).

## Real chamber structure — CAPTURED, apworld RESTRUCTURED (2026-07-19)

**Authoritative source found:** the game's `OverworldLevelData` ScriptableObject
(a fully-loaded asset — one pass, no overworld-walking gaps) lists all **21
sections** of the campaign with exact hole membership. `mod/src/Mapping/
SectionDumper.cs` dumps it to **`mod/wtg_sections.json`**: per section `name`
(chamber code e.g. "08A"/"02"), `hasBoss`, `saveSpotId`, `unlockTriggerId`, and
ordered `levels` (scenes). = **11 chambers, 132 holes** (all resolve in
wtg_levels.json; 119 crowns). The sub-area themes decode from
`PlateInfoManager.AreaIDEnum` (see `mod/harvested-levels.md`).

Real chambers (10 -> 00, counting DOWN; 10 = intro/free start, 00 = finale):
`10`(3) `09`(10) `08`(25) `07`(10) `06`(12) `05`(18) `04`(10) `03`(20) `02`(5)
`01`Western(17) `00`finale(2).

**apworld rebuilt around chambers (DONE):** `tools/build_levels.py` now groups
`wtg_sections.json` into chambers and emits the same `levels.json` schema data.py
already consumes (an "area" is now a chamber). **Nothing downstream changed.**
Locations = 132 Clears + 119 Crowns = 251. Items = 10 Chamber Access keys
(chambers 09..00; chamber 10 free) + Flags + filler = 17 names. **Generates clean
& solvable on 0.6.7** for campaign and door_100 goals (verified 2026-07-19); all
10 Access keys placed as progression, real chamber gating in the spoiler.
Rebuild: `python tools/build_levels.py --write && python tools/export_ids.py`.

## Gating — SOLVED via non-linear teleport unlocking (2026-07-19)

The randomizer is **non-linear** (matches the game: pause-menu teleport + portal
room let you travel to any UNLOCKED chamber — no forced linear walking). Full loop
VALIDATED in-game: clear levels → receive `Chamber NN Access` → that chamber
becomes teleport-reachable → hop there and play.

**The unlock lever (found by probing a 100% save's SaveGame sets):** a section
unlocks when its **`unlockTriggerId`** is registered as an open door. The save
stores these in `OPEN_DOORS` (regular, e.g. `door_platformer_00`, `Z4UZC`) and
`OPEN_MAIN_DOORS` (computer, e.g. `YX3NO`, `9DSBG`, `OS8GA`). So the mod
(`mod/src/Mapping/ChamberUnlock.cs`) does, per section of the unlocked chamber:
`SaveGame.SetDoorOpen(trig)` + `SaveGame.SetMainDoorOpen(trig)` then
`OverworldManager2d.RefreshDoorsAndGoals()`. `SaveGame.GetStringList(key,slot)` /
`AddToSet(key,elems,slot)` are the generic save accessors. `ItemApplier` calls
`ChamberUnlock.Request(N)`; `Mod.OnUpdate` re-applies pending unlocks once an
overworld is loaded (items can arrive at connect before the overworld exists).

AP logic is non-linear: each chamber is an independent region gated by its own
`Chamber NN Access` (Regions.py connects all from Menu; chamber 10 = free start).
NOTE physical looseness: chamber 09 is walk-reachable from the intro (no computer
door there) so it's not hard-gated — harmless (checks can send out-of-logic; no
softlock). `mod/src/Mapping/UnlockProbe.cs` = the read-only save-vocabulary probe
(toggle `Mod.ProbeEnabled`). ChamberGate/EntryGate/GoalGate/DoorDumper are now
dead/secondary (door-suppression approach abandoned in favour of teleport unlock).

## ROADMAP — richer progression (agreed 2026-07-19)

Current: only ~10 progression items (chamber access) vs 251 locations — too few.
Planned, several as **apworld Options**:
1. **Section-level access (~17 items).** Gate per section, not per chamber — the
   mod already unlocks per section. CEILING is 17: chambers 05 (Kitchen/Gravity/FPG
   share `9DSBG`) and 06 (Portal/Superhot share `YX3NO`) unlock as one unit; all
   other sections have distinct triggers (08 = 4 separate!). Name by theme.
2. **Computer boss keys (9 items).** Gate each of the 9 boss doors behind a key,
   using the VALIDATED `OverworldMainDoorPlate.SetState` lever. Bosses become gates.
3. **Chests as locations (~24 checks).** Save tracks 24 overworld chests
   (`CHEST_KITCHEN`…, key `OPEN_CHESTS` / `SetChestUnlocked`). More checks = better
   spread. (Option.)
4. **Crown-gating.** Some sections unlock natively via crowns (`CROWN_MAIN1`→Lebowski
   07B, `CROWN_MAIN2`→Cars 03B). Make Crown a counted progression item + gate some
   sections behind N crowns. (Option.)
5. **DLC (Sporty Sports).** Adds more sections → more of everything. (Option.)
6. **Ball shapes / Transmogrif (stretch).** Section `ballShape` = `Transmogrif.
   BALLSHAPES`; gating ball abilities as items = most WTG-flavoured progression, but
   needs R&D on whether ball shape is force-settable.

**Note:** on a **fresh save** the game natively gates progression; the 100% save
has everything unlocked. Use a dedicated fresh dev save slot to test; the 100%
save is safe. ChamberUnlock WRITES save state (persists on that slot).

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
