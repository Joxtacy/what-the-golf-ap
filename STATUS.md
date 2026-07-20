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

### Within-chamber hard-lock — DONE: `hard_sections` option (2026-07-20)

The `area_access: section` walk-looseness is now closeable via the **`hard_sections`**
apworld option (Toggle, default off). Sub-areas connect via `OverworldButton2D`
connectors that open on ball-touch while `canOpen==true`, and each connector's
`OverworldID.ID` equals its section's `unlockTriggerId`. When enabled the mod forces
`canOpen=false` on every connector whose id is a not-yet-unlocked section trigger
(hard gate) and restores `canOpen=true` on unlock. Proven in the spike: fresh save,
only Easy 2D (09A) keyed → Living Room (09B/`Z4UZC`) door stayed shut.

**Productionized as `mod/src/Mapping/SectionGate.cs`** (sibling to `BossGate`):
`SetEnabled` from slot data (`hard_sections`), `Tick()` from `Mod.OnUpdate` (~6x/sec,
alongside BossGate). Softlock-safe now that the teleporter lists every keyed section
directly (a section is teleport-reachable iff its door is open — see the teleporter
section), so a locked connector can never trap you; auto-no-op under `chamber`
granularity (a chamber's sub-areas share triggers that unlock together). Apworld:
`Options.HardSections` + `fill_slot_data` `hard_sections`; mod: `ArchipelagoData.
HardSectionsEnabled`, `ReadSlotData` wires `SectionGate.SetEnabled`. The old spike
file `ActiveGateTest.cs` was removed (superseded); read-only `WalkGateProbe.cs`
(`Mod.WalkProbeEnabled`, OFF) kept. **VALIDATED:** mod builds + deploys; seed with
`hard_sections: true` generates solvable on 0.6.7 (slot data + spoiler carry it).
**LIVE-VALIDATED in-game (2026-07-20, fresh save):** keyed only Easy 2D (09A) →
walked into Easy 2D fine (a keyed section's door is already open — the AP key sets its
door-open flag, so the key IS the opener; no console button-push needed) while
Living Room (09B/`Z4UZC`) stayed hard-locked. Log showed `[SECTIONGATE] opened
connector 'DOOR_easy2d_00'` + `holding connector 'Z4UZC' shut`. NOTE: like all door
pokes, only reliable on a FRESH save.

**Caveat — only reliable on a FRESH save:** on a progressed/contaminated save the
game re-derives door state from persistent `OPEN_DOORS` each frame and can overwrite
the poke.

### Teleporter / reachability — SOLVED (2026-07-20)

Verified in-game on a fresh save: receive a chamber/section key → that section
becomes an **unlocked** teleport destination → hop there and play. The long-standing
"keyed-but-unvisited section won't teleport" worry was a single bug, not a design
gap. Both a `TELEPORT_` section (08A Platformers) and a `SAVE_` section (08C Space)
teleported.

**Root cause found by DISASSEMBLING `GameAssembly.dll`** (dump.cs has no method
bodies; used `objdump` + an RVA→name index — see `tools/disq_objdump.py`). The
teleport gate is far simpler than assumed:

- `OverworldLevelSection.Refresh()` sets
  `isAvailable = IsNullOrWhiteSpace(unlockTriggerId) || SaveGame.GetIsDoorOpen(trig)
  || SaveGame.GetIsMainDoorOpen(trig)`. That is the *entire* availability rule.
- The pause-menu teleporter (`OverworldTeleportMenu.PopulateMainCampaignSections`)
  creates a button for **every** section and only sets `isLocked = !isAvailable`.
  **There is NO `saveSpotId` / `TELEPORT_` filtering** — the `TELEPORT_`-vs-`SAVE_`
  prefix only chooses where the ball is placed on arrival, not whether you can go.
- So a section is teleport-reachable **iff its `unlockTriggerId` is an open door**
  (`OPEN_DOORS` or `OPEN_MAIN_DOORS`). Nothing else — no reached-spots set (there
  isn't one), no `isAvailable` forcing, no `TELEPORT_` requirement. The earlier
  "Space won't teleport", "reached-spots gate", and "Water/Western have no anchor"
  conclusions were **all symptoms of the doors being empty**, not real gates.

**The actual bug:** `SetDoorOpen(id)` = `AddToSet(currentOverworld.OPEN_DOORS, id)`,
an **in-memory** write to `campaignDatas[currentCampaign]`. `ChamberUnlock` applied
it *during the intro*, the campaign overworld load then **reloaded the save from
disk** (discarding the write), and a sticky `Applied` set stopped it ever re-writing.
Confirmed live: the mod logged `(re)opened 3 door trigger(s)` twice ~4s apart — the
reload wiped the doors and the new self-heal re-applied them; `OPEN_DOORS` then held
`door_platformer_00`/`door_space_00`/`DOOR_easy2d_00` stably.

**The fix (in `ChamberUnlock.TryApply`, built + deployed):** no permanent "applied"
set — every tick, for each requested trigger, check `GetIsDoorOpen || GetIsMainDoorOpen`
and re-`SetDoorOpen`+`SetMainDoorOpen` any that got dropped, then
`RefreshDoorsAndGoals()` + `section.Refresh()`. Self-healing against the reload; a
cheap no-op once everything is open. Removed the `isAvailable=true` forcing (futile —
the menu re-derives it from the door flag anyway).

**Dev levers (left in place, OFF):** `UnlockProbe` (`Mod.ProbeEnabled`) now dumps
every save set + `SavePosition` + all loaded `SaveSpot` ids + per-section availability;
`ChamberUnlock.ForceTrigger(id)` + `Mod.ForceUnlockTrigger` ("") force one trigger open
without AP for fresh-save reachability tests. `tools/disq_objdump.py` disassembles any
method by dump offset/VA with resolved call targets — reusable for future RE.

**Implication for the roadmap:** the `area_access: section` "physical looseness"
(walking to locked siblings) is unchanged, but there is now **no teleport-reachability
caveat** — any keyed section (TELEPORT_ or SAVE_) is directly teleport-reachable. The
within-chamber hard-lock (`SectionGate`) remains the optional way to remove the walk
looseness.

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
