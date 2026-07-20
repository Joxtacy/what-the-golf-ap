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
(`mod/src/Mapping/ChamberUnlock.cs`) opens the exact trigger(s) an Access item
maps to (from `unlocks_by_item` in wtg_ids.json) via `SaveGame.SetDoorOpen(trig)`
+ `SaveGame.SetMainDoorOpen(trig)` then `OverworldManager2d.RefreshDoorsAndGoals()`.
`SaveGame.GetStringList(key,slot)` / `AddToSet(key,elems,slot)` are the generic
save accessors. `ItemApplier` calls `ChamberUnlock.RequestItem(name)`;
`Mod.OnUpdate` re-applies pending unlocks once an overworld is loaded (items can
arrive at connect before the overworld exists). LIVE-VALIDATED 2026-07-20 in
section mode: `08C: Space Access` → opened `door_space_00`, Space reachable.

AP logic is non-linear: each gate (chamber OR sub-area, per `area_access`) is an
independent region gated by its own Access item (Regions.py connects all from
Menu; chamber 10 = free start).

**PHYSICAL LOOSENESS (known, accepted, 2026-07-20).** The game hard-gates
chamber↔chamber via the computer/boss doors, but sub-areas WITHIN one chamber
share an open overworld room — opening one sub-area's door lets you *walk* to
locked siblings (confirmed in-game: unlocking Space let you reach the rest of
chamber 08). So `area_access: section` is physically loose within the
multi-sub-area chambers (03/04/07/08/09); also chamber 09 is walk-reachable from
the intro. This is **out-of-logic but never a softlock** — logic still requires
each key; the player merely *can* play ahead. DECISION: keep `section` as default
and document (done: `Options.AreaAccess` docstring). `chamber` granularity has no
looseness (computer doors are hard walls). `mod/src/Mapping/UnlockProbe.cs` = the
read-only save-vocabulary probe (`Mod.ProbeEnabled`). ChamberGate/EntryGate/
GoalGate are dead.

### Within-chamber hard-lock — SPIKE DONE, FEASIBLE (2026-07-20)

The looseness above **can** be fixed. Validated in-game on a FRESH save: sub-areas
connect via `OverworldButton2D` connectors that open on ball-touch while
`canOpen==true`, and each connector's `OverworldID.ID` equals its section's
`unlockTriggerId`. Forcing `canOpen=false` on a locked section's connector makes
its door refuse to open (hard gate); restoring `canOpen=true` on unlock re-opens
it. Proven: fresh save, only Easy 2D (09A) keyed → Living Room (09B/`Z4UZC`) door
stayed shut while Easy 2D opened. **Only works on a FRESH save** — a progressed
save re-derives door state from the persistent `OPEN_DOORS` flag each frame and
overwrites the poke (this fooled earlier same-save tests into a premature
"infeasible"). Experiment: `mod/src/Mapping/ActiveGateTest.cs` (+ read-only
`WalkGateProbe.cs`; `ChamberUnlock.AllTriggers()`/`IsTriggerUnlocked()`), toggles
`Mod.ActiveGateTestEnabled`/`WalkProbeEnabled` (both OFF). TODO: productionize as a
`SectionGate` (sibling to `BossGate`) behind a `hard_sections` apworld option.

### Teleporter / reachability — OPEN THREAD (resume here, 2026-07-20)

Big open issue for the non-linear model. On a **fresh save** a keyed-but-unvisited
chamber may not be teleport-reachable, so "receive a chamber key → teleport there"
can fail. Investigation so far (all prior success was on progressed saves where
everything was already reached, which masked this):

- **`ChamberUnlock` fix already made (kept):** after `SetDoorOpen`/`SetMainDoorOpen`
  it now calls each unlocked `OverworldLevelSection.Refresh()` and sets
  `isAvailable=true`, **retried each tick** until `OverworldLevelData` loads (logs
  `section 'X' now teleport-available`). This made **Easy 2D (09A)** teleportable
  on a fresh save. Necessary but not sufficient in all cases (below).
- **Space (08C) still didn't list** — but 08C is a bad test target: its
  `saveSpotId='SAVE_space_01'` while every sibling is `TELEPORT_*`
  (`TELEPORT_PLATFORMERS`/`_SOCCER`/`_EXPLOSION`/`_EASY2D`/…). Space may simply not
  be a teleport destination (walk-in + save point only). **Re-test with a real
  `TELEPORT_` section that's unreached (e.g. 08A Platformers).**
- **`ACCESSIBLE_LEVELS` save set is EMPTY yet teleport-to-reached works** → it is
  NOT the teleport gate. Gate is likely the game's **SaveSpot "reached" system**
  (`SaveSpot`/`OnEnterSaveSpot`, section `saveSpotId`). There may be a
  "reached save spots" save key `UnlockProbe` doesn't dump yet.
- **Levers on hand:** `SaveGame.AddToSet(key, elem, slot)` (generic set writer),
  `SetLevelCompleted(id)`, `SaveGame.currentOverworld.*` set keys. `UnlockProbe`
  (`Mod.ProbeEnabled`) dumps OPEN_DOORS/OPEN_MAIN_DOORS/CONSOLES_HIT/
  COMPLETED_LEVELS/ACCESSIBLE_LEVELS/UNLOCKED_CHESTS + section state.

**NEXT (fresh thread):** (1) extend `UnlockProbe` to dump *all* `currentOverworld`
keys → find the "reached save spots" set; (2) clean re-test on a fresh save with a
`TELEPORT_` unreached section, probe timed AFTER unlock; (3) decide the design fork
(see task): (A) mod marks keyed sections reached/teleportable via that set, or (B)
rework apworld into a chamber progression chain, or (C) hybrid. NOTE the committed
non-linear "teleport anywhere keyed" claim (below) is **only verified on progressed
saves** — treat as unconfirmed for fresh saves until this thread closes.

## ROADMAP — richer progression (agreed 2026-07-19)

Planned, several as **apworld Options**:
1. **Section-level access (~17 items). ✅ DONE (2026-07-20).** New `area_access`
   option: `section` (17 gate-unit keys, default) or `chamber` (10 keys, prior
   model). A "gate unit" = a unique section `unlockTriggerId`; sections sharing a
   trigger open together (Portal+Super Putt = `YX3NO`; Kitchen+Gravity+FPG =
   `9DSBG`), all others distinct (08 = 4 separate) → 17. `build_levels.py` emits
   `gate_units` + per-level `trigger` in levels.json; `data.py` exposes both
   granularities (item ID table holds the union so IDs stay stable); Options/Items/
   Regions/Rules branch on `area_access`. `export_ids.py` emits `unlocks_by_item`
   (Access-item name → trigger ids). Mod: `ChamberUnlock` now opens triggers
   straight from that map (granularity-agnostic); `ItemApplier` routes any
   "* Access" → `ChamberUnlock.RequestItem(name)`. VALIDATED: 4-player matrix
   (section/chamber × campaign/door) generates solvable on 0.6.7 — section = 17
   access keys, chamber = 10, both 132 flags. Mod builds + deploys. **Live in-game
   test of section unlocking still pending.**
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

## ROADMAP — goal options (idea, 2026-07-20)

Current goals: `campaign` (beat the final boss) and `door_50/75/100` (Flag count).
Idea: add a **"beat all bosses"** goal — win requires **defeating all 9 Computer
bosses**, not just the final one. Rationale: reaching only the final-area gate can
be satisfied by one progression chain, leaving many chamber-access items unneeded;
requiring every boss forces MORE progression items (every chamber's Access key) to
actually be in logic → deeper, more spread-out progression, fewer hub shortcuts.
Keep final-boss-only as an option too (new `Options` value, e.g.
`goal: campaign / all_bosses / door_50/75/100`).

Implementation sketch: track per-boss Defeat events (mod already has
`GameAnalytics.OnFinalBossCompleted` + boss detection via `GameState.isBossBattle`
and boss level IDs `ID_2D_HOLEINONE_N`) and fire the Victory event once all 9 are
registered. Pairs naturally with progression roadmap #2 (computer boss keys).

## ROADMAP — mod UX / lifecycle (agreed 2026-07-19)

Right now the mod hard-codes `Connect("localhost",38281,"Player1")` in
`Mod.OnInitializeMelon` and starts pumping/dumping on load, so to play vanilla you
must delete `<game>\Mods\WtgArchipelago.dll`. Should not require that:

1. **Passive until connected.** With no active AP connection the mod must have ZERO
   side effects — no auto-connect, no save writes (ChamberUnlock), no periodic
   dumpers. Installed mod == vanilla play until you opt in. (Gate all gameplay
   effects behind "connected".)
2. **In-game connection UI.** Enter host / port / slot name / password and hit
   Connect from within the game (e.g. a MelonLoader IMGUI panel on a hotkey, or a
   main-menu button) instead of the hard-coded Connect. Persist last-used details
   (MelonPreferences). A disconnect/toggle to drop back to vanilla mid-session.
3. Optional: a MelonPreferences "enabled" flag / main-menu toggle so AP mode is
   off by default.

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
