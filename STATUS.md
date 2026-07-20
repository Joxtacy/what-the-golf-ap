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
2. **Computer boss keys. ✅ DONE (apworld + mod; live-tested via all_bosses run).**
   New `boss_keys` toggle option. Gates campaign computer bosses behind a
   `Computer N Key` each, using the VALIDATED `OverworldMainDoorPlate.SetState`
   lever run in reverse: `mod/src/Mapping/BossGate.cs` holds a locked boss's door
   shut (forces its lit plates off ~6x/sec) until the key arrives, then re-lights
   them **every tick** (self-healing — see the 2026-07-20 fix below). apworld:
   `data.BOSS_HOLES` / `boss_key_to_level_id()`, `Options.BossKeys`, boss-key
   items (progression) + Clear/Crown rules in `Rules.py`; `export_ids.py` emits
   `boss_by_item` (key → boss LevelData.ID). Generates solvable on 0.6.7.

   **✅ KEYABLE-SET BUG FIXED (2026-07-20, later session).** The keyable set is
   now sourced from the door topology (`mod/wtg_doors.json`) instead of parsing
   campaign scene names. `build_levels.py:keyable_boss_doors()` keeps only doors
   that BOTH (a) have plate areas — so the mod's `OverworldMainDoorPlate.SetState`
   lever can actually hold them shut — AND (b) map to a real campaign hole — so a
   `<scene> - Clear` location exists to gate. It emits `boss_doors` into
   `levels.json`; `data.BOSS_HOLES` reads that (no more regex on scene names).
   It drops **Computer 9** (the plateless finale-special `IKCV7`, chamber -1 —
   `SetState` has no plates to toggle, so it could never be gated). Initially it
   also excluded **Computer 5** (its boss `2D HoleInOne 05 basic` / `0WUALV` was in
   no `wtg_sections.json` section), giving 6 keys — but C5 has since been added
   (below), so **the result is now computers 1,2,3,4,5,7,8 (7 keys)**.

   **✅ COMPUTER 5 ADDED + LIVE-VERIFIED (2026-07-20).** `0WUALV` is a real
   plate-lit boss (plates `FPG_05C` + `GRAVITY_05B`) that the section dump simply
   omits. Confirmed in-game via a throwaway `C5Probe` diagnostic (now removed): it
   IS reachable, and beating it fires `GameAnalytics.OnLevelComplete` with scene
   `2D HoleInOne 05 basic` (boss, 0 challenges → Clear only, no Crown). It's fought
   in the chamber-5 overworld, lit by the FPG + Gravity holes, so `build_levels.py`
   now injects it into **chamber 5** gated by the shared chamber-5 trigger `9DSBG`
   (see `INJECT_LEVELS`). It flows through automatically: `keyable_boss_doors()`
   now includes it (7th key `Computer 5 Key` → `0WUALV`), and `all_boss_scenes()`
   includes it → **`all_bosses` now requires 8 bosses** (7 computers + Final).
   Regenerated `levels.json` (133 holes) + `mod/ids.json` (**41 items / 252 locs /
   7 boss keys / 8 boss scenes**). The mod needs no code change (`BossGate`/
   `BossGoal` read `boss_by_item` / `boss_scenes` from `wtg_ids.json`, redeployed
   on rebuild). **VALIDATED:** 3-player seed (all_bosses×section / campaign×chamber
   / door_100×section, all boss_keys on) generates solvable on 0.6.7; spoiler shows
   `Computer {1,2,3,4,5,7,8} Key` with the C5 key placed + used in the playthrough,
   and the `2D HoleInOne 05 basic - Clear` location present.
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

## ROADMAP — goal options — "all bosses" ✅ DONE (2026-07-20)

Goals are now `campaign` (beat the final boss), `door_50/75/100` (Flag count), and
the new **`all_bosses`** (`Goal.option_all_bosses = 4`): win requires **defeating
every campaign boss** — the 7 computer HoleInOne bosses **and** the Final boss
(8 total), not just the final one. Rationale (confirmed in the test spoiler):
reaching only the final-area gate can be satisfied by one progression chain, but
requiring every boss forces the chamber-access (and, with `boss_keys`, the boss)
keys deep in chambers 08/07/06/05/03/01/00 into logic → deeper, more spread-out
progression. Since the Final boss is included, `all_bosses` subsumes `campaign`.

- **apworld:** `Options.Goal.option_all_bosses`; `data.all_boss_scenes()`;
  `Rules.py` completion = `all(state.can_reach_location("<scene> - Clear"))` over
  every boss (reuses the existing Access/boss-key rules, so it folds both in).
  `export_ids.py` now emits `boss_scenes` (all 8) + `final_boss_scene`.
- **mod:** new `mod/src/Mapping/BossGoal.cs` — loads the boss scenes, enabled from
  slot data (`goal == 4`), counts each boss clear (`GamePatches.LevelCompletePostfix
  → RegisterDefeat`, and the Final boss via `OnFinalBossCompleted →
  RegisterFinalBoss`), and reports Victory once all are down. `ArchipelagoData`
  gained `GoalCampaign/Door50/75/100/AllBosses` constants.
- **Latent bug fixed:** `FinalBossPostfix` previously always sent Victory, which
  would wrongly complete a `door_%` seed on the final boss. It now branches on the
  goal (campaign → Victory; all_bosses → count the boss; door → nothing).
- **VALIDATED (generation + LIVE in-game, 2026-07-20).** 3-player seed
  (all_bosses×section+boss_keys / all_bosses×chamber / campaign) generates solvable
  on 0.6.7; spoiler shows `Goal: All Bosses` and `Computer N Key` items in-logic.
  Live: solo all_bosses seed (all keys via `start_inventory`), beat the 7 reachable
  bosses → `[GOAL] ... (7/7)` → `all bosses defeated -> victory reported` → server
  accepted the goal.

- **Two mod bugs found + fixed live (commit after 37a5261):**
  (a) `BossGate` lit each unlocked computer's plates only ONCE; a door reached AFTER
  its key arrived (overworld reloads door objects on teleport) stayed dark and
  unfightable (hit Western + finale). Now re-lights every tick (self-healing).
  (b) `BossGoal` forgot bosses beaten before the process started; now reconciles
  against the server's already-checked boss Clear locations on connect.

- **DESIGN FIX — `all_bosses` excludes the finale's `HoleInOne 09 3d`.** It's the
  only boss door with no plate-areas / chamber -1 (`wtg_doors.json`): a scripted
  finale-sequence encounter, not an independently-reachable computer, so requiring
  it made the goal unbeatable (teleport lands you on the Final boss, can't trigger
  09). `data.all_boss_scenes()` now drops any boss sharing the Final boss's area →
  the finale is represented by the Final boss alone → 7 required bosses.

## ROADMAP — mod UX / lifecycle — ✅ DONE (2026-07-20)

The mod no longer hard-codes `Connect(...)` or writes state on load. It is now
**passive until connected** with an **in-game connection UI**:

1. **Passive until connected.** With no live AP session the mod has ZERO side
   effects — no auto-connect, no save writes, no gate ticks. Installed == vanilla
   until you opt in. In `Mod.OnUpdate` the ChamberUnlock/BossGate/SectionGate ticks
   are gated behind `Plugin.Client.Connected`; the Harmony postfixes already no-op
   when `Session == null`. (`Client.Tick()` still drains its main-thread queue
   unconditionally — harmless.)
2. **In-game connection UI** — `mod/src/ConnectionUI.cs`, an IMGUI panel toggled
   with **F8**: host / port / slot / password fields, an "Auto-connect on launch"
   checkbox, a Connect/Disconnect button, and a live status line
   (`ArchipelagoClient.State` / `StatusMessage`, a new `ConnState` enum +
   `Disconnect()`). Uses the low-level `GUI.*` API (clean fixed-arg overloads — safer
   than `GUILayout`'s `params` arrays under Il2CppInterop) and reads the F8 hotkey
   from `Event.current` (works regardless of the game's input backend). Needs
   `UnityEngine.IMGUIModule.dll` (added to both csproj ref groups + `refs/README.md`).
3. **Persisted + off by default.** `mod/src/Preferences.cs` = a `MelonPreferences`
   category (`<game>\UserData\MelonPreferences.cfg`): host/port/slot/password +
   `autoConnect` (default **false**). Auto-connect only fires when the player has
   ticked the box; otherwise the mod waits for a manual Connect.
4. **Pause while open.** `ConnectionUI.UpdatePause()` (called each frame from
   `Mod.OnUpdate`) sets `Time.timeScale = 0` while the panel is visible and restores
   the prior scale on close, so the menu ball doesn't move behind the UI.
   **Known minor residual:** the game still *polls* the mouse each frame (input
   polling ignores timeScale), so a click made while the panel is open is buffered
   and applied on close (the ball nudges once). Fully eliminating it needs disabling
   the game's Rewired overworld input while open — deferred as low-value.

**VALIDATED (2026-07-20):** builds + deploys clean; launched in-game — mod loads,
all data + 3 patches bind, logs `Press F8 for the Archipelago panel`, and **makes
no connection attempt** (passive). `OnGUI` runs per-frame with no errors.
**Interactive F8 test (type fields, Connect/Disconnect) — DONE, verified by the
user in-game (2026-07-20). Looks good; the mod-UX work is fully complete.**

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
The mod no longer hardcodes a connect — press **F8** in-game and enter
`localhost` / `38281` / `Player1`, then Connect (tick "Auto-connect on launch" to
skip this next time). See the mod-UX section above.

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
- **Never send to AP from the game's main thread.** `SendCheck`/`SendVictory` run
  the network send on a `ThreadPool` thread and gate on `Connected` (not just a
  non-null `Session` — the object lingers after a socket close). A synchronous send
  into a dead socket blocks the caller; from an `OnLevelComplete` postfix that is
  Unity's main thread → the whole game freezes (with the socket-close exception
  logged from the background receive thread while frozen — the tell).

## Suggested next steps

1. **Content options** (all additive, apworld Options): chests as ~24 locations
   (`OPEN_CHESTS`/`SetChestUnlocked`); crown-gating (Crown as a counted progression
   item); DLC Sporty Sports; ball shapes / Transmogrif (stretch, needs RE).
2. **DeathLink — DONE + LIVE-VALIDATED (2026-07-20).** "Death" = a level
   FAILURE (ball OOB/water/lost), hooked via the safe static no-arg
   `GameAnalytics:OnLevelReset` (NOT `Level.Fail` — bad sig; and distinct from
   `OnLevelManualReset`/`OnLevelAbort`, which are excluded). Because wiping is
   constant in WTG, outgoing uses a **count-based throttle** (user's design): one
   DeathLink broadcast per Nth local wipe. **`N` = the apworld `death_link_amnesty`
   option** (Celeste-style `Range` 1..30, default 10), delivered via slot data →
   `ArchipelagoData.DeathLinkAmnesty` (the seed owns it; it is NOT a client pref).
   A wipe caused by an INCOMING death is suppressed
   from the count (`DeathLinkHandler.BeginInducedDeath`, ~30-frame window) so received
   deaths never feed back — no ping-pong loops. Incoming (consumed in `Mod.OnUpdate`):
   if `GameState.IsInLevel()` → `Level.Instance.Restart()` (kills ball, wipes hole
   progress); in the overworld → dropped. A runtime **F8 on/off toggle**
   (`DeathLinkHandler.SetEnabled`, per-session — re-reads the seed's `death_link` on
   each connect) enables/disables via the AP service's DeathLink tag. Files:
   `DeathLinkHandler.cs`, `GamePatches.LevelResetPostfix`,
   `GameState.IsInLevel`/`RestartLevel`, apworld `Options.DeathLinkAmnesty` +
   `fill_slot_data`, `ConnectionUI` (HUD + toggle). An on-screen HUD
   (`ConnectionUI.DrawHud`, always-on while DeathLink active + connected) shows the
   wipe counter `x/N`, looping to 0 on each broadcast. **LIVE-VALIDATED 2026-07-20**:
   `OnLevelReset` binds + fires on real wipes and NOT on manual restart/quit; counter
   broadcasts at N and loops; incoming death (fired via `tools/send_deathlink.py`,
   a 2nd DeathLink-tagged connection) restarts the current hole (`killed=True`) and is
   dropped in the overworld; loop-suppression held; HUD confirmed; the
   `death_link_amnesty` option flows to the mod (log `per 5 wipes` from a
   `death_link_amnesty: 5` seed). Remaining: interactive F8-toggle check + cosmetic
   HUD polish (user has feedback, deferred). `send_deathlink.py` = reusable
   incoming-death test tool.
3. Polish: friendlier area/section display names.
4. Optional: rebuild `data.py` from the **real hub sections** (`wtg_goals.json`)
   for authentic, spatially-coherent areas.
