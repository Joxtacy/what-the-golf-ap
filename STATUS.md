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
   access keys, chamber = 10, both 132 flags. Mod builds + deploys. **✅ Section
   unlocking LIVE-VALIDATED in-game (`12df9b1`).**
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
3. **Chests as locations (~24 checks). ✅ DONE (`7aefeed`, `crowns` option).**
   Every overworld chest becomes an AP location (24 checks); crown-LOCKED chests
   open on a `<Area> Chest Key` from the multiworld (the mod holds the door shut).
   apworld: `Options.Crowns`, `data.Chest` / `chest_key_names` / `chest_location_names`,
   `Items.CHEST_KEY_ITEMS`, Rules crown-chest gating. Mod: `ChestGate` / `ChestDumper`
   / `ChestProbe` / `ItemApplier`. This key-based approach SUPERSEDES the counted-crown
   idea below.
4. **Crown-gating (counted Crown item). SUPERSEDED — not built.** The original idea
   (make Crown a counted progression item + gate sections behind N crowns) was
   replaced by the key-based crown-door gating in the `crowns` option (item 3). Gating
   *sections* behind crown counts remains unbuilt; likely YAGNI.
5. **DLC / episodes (Sporty Sports + 4 more).** Adds more sections → more of
   everything. (Option, likely per-episode.) **IN PROGRESS — dumpers now
   episode-aware (2026-07-22); a fresh multi-episode dump is the next action.**

   The game has **7 playable campaigns** (`ECampaignType` in the il2cpp dump):
   `Main=1` (base), `Olympics=2` (= Sporty Sports), `Snow=3`, `Hotdog=4`,
   `Hub=5`, `Alive=6`, `Amongus=7` → **5 extra episodes** (Olympics/Snow/Hotdog/
   Alive/Amongus; the user owns all 5). Each is a separate `ContentPack`/overworld
   with its OWN `OverworldLevelData`, doors, chests **and its own section
   numbering** — so episodes reuse section codes (`01`, `08A`) that would collide
   in the shared dump files.

   **Dumpers made episode-aware (built + `dotnet build` clean, 2026-07-22).** New
   `mod/src/Mapping/CampaignInfo.cs` reads the active campaign via
   `Il2Cpp.SaveGame.currentCampaignDef.type` (fallback `LastPlayedCampaignType`),
   mapped by int → tag (`Main`/`Olympics`/…). `SectionDumper`, `DoorDumper`,
   `ChestDumper`, `GoalDumper` now stamp every record with its `campaign` (Section
   also `source` = the `OverworldLevelData` asset name) and **key records by
   `campaign::<id>`** so accumulating several overworld walks never overwrites
   across episodes. Section/Door/Chest dumpers gained/kept cross-session
   `LoadOnce` that **migrates legacy un-tagged records to `Main`** (so re-dumping
   base game is lossless). Only the active campaign's overworld data is loaded at a
   time (confirmed: the base pass captured exactly Main's 21 sections), so the
   active-campaign tag correctly labels each pass. `build_levels.py` now **filters
   to `Main`** (sections + boss doors) so the base-game world stays byte-identical
   (re-verified: 11 chambers, 133 holes, 7 boss keys) — episode integration is a
   later, separate step.

   **DUMP SESSION DONE + INSPECTED (2026-07-22).** All 5 episodes walked (100%
   save). **Key discovery: episodes do NOT use `OverworldLevelData`** — that
   ScriptableObject is Main-only, so `SectionDumper` just relabeled Main's 21
   sections under each active-campaign tag (every episode "section" had
   `source='Main overworld'`); its episode output is bogus and was discarded. **The
   real episode structure is the goal graph** (`OverworldGoal.ParentHubSection`),
   captured by `GoalDumper` (campaign-tagged) and merged into `mod/wtg_goals.json`
   (243 goals: Main 139 + episodes 104; the `Hub` mis-tag artifact — a duplicate of
   Amongus — dropped). `wtg_sections.json`/`wtg_levels.json`/doors/chests were
   already correct and left untouched.

   Per-episode structure (holes / hub-sections): **Olympics** (Sporty Sports) 11/1
   (genuinely short); **Snow** 24/12; **Hotdog** 24/6; **Alive** 25/8; **Amongus**
   20/9 — **~101 unique scenes, 100% joined to `wtg_levels.json`** for par/crown.

   Findings that shape integration: only **3/101** episode holes are crownable (so
   `crowns` barely applies); **zero** episode holes are `isBossBattle` (no
   computer-doors — the DOORS dump got only Main's 8; so `campaign`/`all_bosses`/
   `boss_keys` do NOT extend to episodes); **gating is the goal-unlock graph, not
   the `SetState` plate lever** (episode `requires` chains read empty only because
   it's a 100% save — capturing them needs a fresh-save RE pass, later).

   **APWORLD INTEGRATION DONE + VALIDATED (2026-07-22).** Episodes are a first-
   class apworld feature now:
   - `build_levels.py` gained a **goal-graph path** (`build_episodes`) emitting an
     `episodes` block into `levels.json` (5 episodes, **100 holes**, 3 crowns —
     the shared "Special Day" hole is dropped; scenes de-duped vs Main + across
     episodes).
   - `Options.py` **`episodes` OptionSet** (valid keys: Sporty Sports / Snow /
     Hotdog / Alive / Among Us; default none). `data.py` loads an `Episode` model,
     adds episode scenes to the display map + ID tables (appended last → existing
     IDs unchanged). Each enabled episode = one region gated by a
     `"<Episode> Episode Access"` key; episode hole-clears grant Flags (so the
     `door_%` target scales — `flag_pool`/`flag_goal` are episode-aware).
   - `Regions/Rules/Items/__init__` wired; `fill_slot_data` carries `episodes` and
     UT restores it (`_apply_slot_data`). `WTGWorld.enabled_episodes()` reads the
     option.
   - **VALIDATED:** `ut_validate.py` 9/9 PASS incl. 3 episode cases (real gen ==
     UT round-trip, identical locations/items/slot_data). Real `Generate.py`
     2-player episode multiworld (all 5 / door_100 / crowns + Snow&AmongUs /
     all_bosses / boss_keys) fills **solvable** on 0.6.7; Episode Access keys land
     in the **playthrough** (required progression, cross-world). `mod/ids.json`
     regen'd (68 items / 379 locations) + redeployed; episode clears will report
     in-game via `name_by_scene` (no mod code change).

   **Access ENFORCEMENT ✅ DONE + live-validated (2026-07-23).** The Episode Access
   key hard-locks entry: a Harmony prefix on `BasePackStarter.StartPack(ContentPack,
   object[])` (GeneralCampaignStarter + CampaignStarter) returns false for a locked
   episode, so nothing starts and the player is kept out at the episodes hub. Keyed
   on the stable `contentPackID` (captured via the read-only `EpisodeProbe`);
   `EpisodeGate` maps items/names from `wtg_ids.json` (`episode_pack_by_item` /
   `episode_pack_by_name`) + the enabled set from slot data. NO goal-graph RE was
   needed — it's one key per whole episode, gated at the episode boundary.
   **Display-name polish ✅ DONE (2026-07-23):** `pretty_episode()` →
   "Snow: Snowball Role and Grow" (episode-name prefix required for uniqueness).

   **Clears + flags ✅ DONE + live-validated (2026-07-23).** Solo door_100 seed
   (all 5 episodes, keys via `start_inventory`) on 0.6.7: cleared Sporty Sports holes
   in-game → server logged `Sporty Sports: Man Ball Olympic - Clear` and
   `... Golf Into Olympics - Clear` (the latter yielding a `Flag`), confirming episode
   clears report under polished names AND feed the Flag pool (door_100 N = 233 vs 133
   base). **Episodes fully validated end-to-end.**

   **Native portal grey-out — investigated + abandoned (2026-07-23; not possible).**
   Forcing a locked pack's `ContentPackDef.accessible=false` sticks but greys nothing in
   the hub: disassembly of `EpisodeEntranceManager` (the hub portal) shows its `Start()`
   reads only play-count / `IsFresh` / `SavePosition` to drive its banners and never
   checks `accessible`/ownership. The game has no native locked visual for owned-episode
   portals (`accessible` only drives the main-menu mode-select carousel / "Coming Soon"
   tile). Grey-out code was reverted; the lock stays communicated by the StartPack veto +
   a MessageFeed line. A custom fabricated locked look (dim illustration / close
   `_curtainsAnimator`) is the only remaining option — cosmetic, fragile, not worth it.
6. **Ball shapes / Transmogrif (stretch). ❌ INVESTIGATED + CLOSED (2026-07-23,
   not worth building).** Two questions, both now answered:
   - *As progression* — DEAD. The in-LEVEL ball shape is level-scripted
     (`Transmogrif` trigger zones placed per hole), NOT a global setter, so it can't
     be force-set without breaking levels (found during the traps work). So ball
     shape can't gate anything → no "most WTG-flavoured progression".
   - *As a cosmetic collectible* — possible but too thin. The OVERWORLD shape IS
     globally settable via `OverworldBallManager.Load(BALLSHAPES)` (cosmetic only),
     but a full 15-shape sweep (via `BallShapeProbe`, a read-only dev probe on F7,
     `Mod.BallShapeProbeEnabled`, OFF) found only **6 render as a distinct skin**:
     `ball`(default), `bread`, `speedboat`, `saw`, `companioncube`, `endball`.
     `boat` renders as the plain ball + a grey dot/wake (not distinct); `fish` +
     `islandBall`/`waterBall`/`puckBall`/`snowball`/`pizza`/`turkey`/`goo` render
     nothing. So ~5 usable non-default skins (speedboat reverts to the plain ball
     when "docked"). A collectible would also need re-assert-on-load (the game
     overwrites the shape on transitions), a selection UI, and persistence — a lot
     of plumbing for a cosmetic-only reward, and the **Transmogrify trap already
     delivers the silly-ball-shape novelty**. Decision: close it.
   Kept: `BallShapeProbe.cs` as a gated-off dev tool; `endball` added to the
   Transmogrify trap's shape pool (it's a distinct, self-contained shape).

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

## PopTracker map pack — DONE except real map coordinates (2026-08-03)

`poptracker/` is a PopTracker pack (map per chamber 10→00 + one per episode, full
logic, AP autotracking). **Generated wholesale** by `tools/build_poptracker.py`
from `data.py` + `mod/ids.json`; hand-maintained inputs live in
`tools/poptracker_src/`. `tools/wtgpng.py` is a stdlib-only PNG writer (no Pillow
under MSYS) that rasterizes the maps and all 76 item icons procedurally.

**Two pack-format constraints shaped it, both easy to regress:**
- A rule containing `:` is parsed as `code:count`, and EVERY WTG location name has
  a colon. So pack-internal names are sanitised and the pack emits **zero**
  `@Location/Section` rules — the two boss-gated chests inline their boss rule as
  a cross-product rather than using `can_reach_location`.
- Access rules have **no NOT operator**, so each option is a 2-stage `progressive`
  item exposing both polarities (`opt_boss_keys_off`/`_on`).
- `crowns` gates only the 24 chests; a hole's Crown location is unconditional.

**Verification** (`tools/poptracker_tests/README.md`): build-integrity asserts;
`check_poptracker_logic.py` diffs the pack's rules against a live AP MultiWorld
per option combo (8 combos, ~165k comparisons, PASS) plus visibility parity;
`--dump-lua` replays verified cases inside PopTracker (240/240); Lua harnesses
cover slot data (28/28) and the map auto-switch (42/42). Live-loaded in
PopTracker 0.35.1 with zero warnings. **A live AP session is still untested** —
connecting needs a UI click that can't be driven headlessly.

**Map auto-switch — LIVE-VALIDATED 2026-08-03** (AP 0.6.7 + PopTracker 0.35.1,
solo seed with all 17 Access keys in `start_inventory`): 15 publishes, correct
chamber every time, no spam between changes, no errors, and the tracker's map tab
followed. All three signal tiers exercised: `src=scene` in a hole
(`1|09|09B|Main|1|scene|Livingroom couch`), `src=save` teleporting the overworld
(`1|08|08B|Main|0|save|`), `src=campaign` inside an episode
(`1|Among Us||Amongus|0|campaign|` -- episode holes have no sub-area, so the
episode name is the routing key). `tools/watch_area_key.py` reads the key
server-side, which validates the mod half with no UI clicks.

`mod/src/Mapping/CurrentArea.cs` publishes the current
chamber/sub-area to data storage under `WTG:CurrentArea:<team>:<slot>`; the pack
`SetNotify`s on it, with a no-mod-needed fallback that reads the sub-area prefix
off each location check. **Gotcha:** you cannot pass a `JObject`/`JToken` to
`session.DataStorage[key]` — the mod compiles against MelonLoader's
Newtonsoft.Json while MultiClient bundles its own, so the two `JToken` types have
different assembly identities (CS0029, uncastable). The payload is therefore a
pipe-delimited string, `v|area|subarea|campaign|in_level|src|scene`, which also
suits PopTracker's Lua (no JSON parser). `Il2Cpp.SaveGame.SavePosition` is a real
string member — previously documented but never read by mod code.

**REMAINING — one in-game dump pass.** `GoalDumper` and `DoorDumper` now record
world positions (guarded on `gameObject.scene.IsValid()`, because
`FindObjectsOfTypeAll` also returns prefab assets with authoring-local
transforms), and both merge across sessions. Until that walk happens the pack
places 24 chest markers at real coordinates and grid-estimates the other 233,
under a divider labelled "ESTIMATED POSITIONS — NOT YET DUMPED".
Note the current `wtg_goals.json` is a PARTIAL capture: 117/133 Main holes. The
16 absent are the 8 computer bosses (not `OverworldGoal`s at all — hence the
`DoorDumper` change) plus `SoccerBall`/`SoccerBall 2`/`3`, `Explosion4`/`5`, and
`2D Super Golf Boy 0`/`1`/`2`, which should appear with more walking.
Procedure is in `tools/poptracker_tests/README.md`.

## Suggested next steps

0. **`hard_sections` mis-modelled 07B Bowling / 03B Cars — ✅ FIXED + LIVE-VALIDATED 2026-07-28.**
   Under section granularity + `hard_sections`, `SectionGate` was trying to clamp the two
   **"Main Crown Door" / "…Variant"** doors — `CROWN_MAIN1` (07B Bowling/Lebowski) and
   `CROWN_MAIN2` (03B Cars) — as if they were walk-connectors. They aren't.

   **Root cause (found via `CrownDoorProbe`, a read-only F5 dev probe, + `disq_objdump`):**
   07B and 03B are the ONLY two sub-areas whose section `unlockTriggerId` is a crown-based
   id (`CROWN_MAIN1/2`) rather than a genuine `door_*`/short-code walk-connector. Confirmed
   in-game: those two sub-areas have **no walk-connector at all** — they're reached by
   teleport (works — `ChamberUnlock.SetDoorOpen(CROWN_MAIN*)` makes them teleport-available)
   / boss-clearing, and there's no walkable sibling path into them. So there is no
   within-chamber looseness to close there, and the "Main Crown Door" our data points at is
   NOT their entrance (it's a global crown gate). Note: each sub-area ALSO has a separate
   *crown-collection* door (`CROWN_LEBOWSKI`, `CROWN_CARS`, `CROWN_OL`, …) opened by
   crowning that area's holes → a crown chest; those are the `crowns`/ChestGate doors and
   were already correct (`chests[].door` = `CROWN_LEBOWSKI`/`CROWN_CARS`, matching the live
   objects). `CROWN_MAIN1/2` were only ever the *section trigger* for 07B/03B.

   **Fix:** `SectionGate` now gates only genuine walk-connectors —
   `SectionGate.WalkConnectorTriggers()` excludes any `CROWN*` trigger, so 07B/03B are no
   longer clamped (their AP key still opens the teleport flag via `ChamberUnlock`,
   untouched). No apworld/data/seed/ID change. Every other sub-area (real connectors, e.g.
   08/09) is unaffected.

   **Also kept (general hardening, from the same investigation):** a Harmony prefix on
   `OverworldButton2D:InstantOpenDoor` (`GamePatches.ButtonInstantOpenPrefix`). The game
   has a SECOND door-open path — `GeneralCampaignStarter.CheckDoors(level)` →
   `<CheckDoors>b__1` → `InstantOpenDoor()` (the only game caller, per a binary call-site
   scan) — that force-opens a level's preceding door when you navigate toward it, bypassing
   both `canOpen` and `CheckOpen` (and `SetDoorOpen`-persisting it). The prefix blocks it
   for any still-locked gated OID (`ChestGate.IsLocked || SectionGate.IsLocked`, shared via
   `IsLockedGatedDoor`), closing that race for real connectors + crown-chest doors.
   `CROWN_MAIN1/2` are deliberately out of scope now (SectionGate excludes them).

   Builds clean + deployed. `CrownDoorProbe` left in place, gated OFF (`Mod.
   CrownDoorProbeEnabled`). **LIVE-VALIDATED 2026-07-28** (section + hard_sections, fresh
   save, seed `ap-build/crownfix`): keyed 07B/03B teleported in and played fine with no
   `[SECTIONGATE]`/`[GATE]` lines about `CROWN_MAIN*`; the real connector 09A→09B still
   hard-locked (`[SECTIONGATE] holding connector 'Z4UZC' shut`).

0b. **DEFERRED / not built — add the two crown-gateway holes (`EvilFlag 2`, `EvilFlag Magic 0`).**
   Investigating item 0 mapped the two levels that sit between each Main Crown Door and its
   chamber (full details in `mod/REVERSE_ENGINEERING.md` → "Overworld doors … crown doors"):
   - **`EvilFlag Magic 0`** (pun "NOW YOU SEE ME", ch03 / `CROWN_MAIN2`) — the game's
     `OverworldLevelData` already puts it in **03A (Jungle), trigger `RVQTA`**, so it's
     **already an AP location**, unlocked by the "03A: Jungle" key. Nothing to do (worth a
     one-time sanity check that teleport-to-Jungle actually reaches it, but the game's own
     grouping says `RVQTA` gates it, and crown-gated 03B/Cars teleported fine, so low risk).
   - **`EvilFlag 2`** (pun "CAMPER FLAG", ch07 / `CROWN_MAIN1`) — belongs to **NO section**
     in `OverworldLevelData` (purely 5-crown-gated) and has **no teleport spot**, so
     `build_levels.py` drops it → NOT an AP location. Deliberately left out (tracking a hole
     with no key/teleport home risks an unreachable check).
   **If you want it in later:** it shares `CROWN_MAIN1` with 07B, so fold it into the 07B
   gate unit (trigger `CROWN_MAIN1`) — the existing "07B: Lebowski" key already `SetDoorOpen`s
   that flag. Steps: (1) **first verify in-game** it's physically walk-reachable once
   `CROWN_MAIN1` is open (teleport to 07A/07B, walk to the door, confirm you can enter
   `EvilFlag 2` — it has no teleport spot, so this is the risk); (2) inject it into 07B via a
   `build_levels.py` `INJECT_LEVELS`-style entry (mirror the Computer-5 injection) so its
   location ID is **appended last** and no existing IDs shift; (3) regen `levels.json` +
   `export_ids.py`, rebuild+reinstall the apworld, redeploy `wtg_ids.json`. Adds 1 Clear
   (+Crown if it has challenges). `CrownDoorProbe` (F5) is the tool to re-inspect.

1. **Content options** (all additive, apworld Options): DLC Sporty Sports; ball
   shapes / Transmogrif (stretch, needs RE). (Chests + crown-door gating are DONE —
   the `crowns` option; see the numbered list above.)
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
   `fill_slot_data`, `ConnectionUI` (F8 toggle), `DeathLinkHud` (on-screen counter).
   **On-screen HUD = `mod/src/DeathLinkHud.cs`** — a real in-scene TextMeshPro object
   (NOT IMGUI): a DontDestroyOnLoad ScreenSpaceOverlay Canvas + `TextMeshProUGUI`,
   driven from `Mod.OnUpdate`. Uses the GAME's font (NotoSans-Black — the rounded
   font WTG uses for menus/labels; Orbitron is only its thin numeric readouts and
   looked wrong). Text `DEATHS n/m` (~1/3 down the left), WTG palette: cream fill
   `#F5E6C6` + thick dark-teal outline `#213D3A` (the logo/sign-label look); no skull
   (glyph absent from these fonts, dropped). IMGUI can't use the game fonts (only
   legacy `UnityEngine.Font`, and the game's are TMP with null `sourceFontFile`),
   which is why the HUD is TMP. Needs csproj refs: `Unity.TextMeshPro`,
   `UnityEngine.UIModule`, `UnityEngine.UI`, `UnityEngine.TextRenderingModule`.
   The chunky outline is faked with 8 dark-teal copies offset 5px behind the cream
   face (TMP's own `outlineWidth` is SDF-capped ~0.4, too thin). Known-minor: the
   offset copies can show slight rendering artifacts at the edges — accepted.
   **LIVE-VALIDATED 2026-07-20**: `OnLevelReset` binds + fires on real wipes and NOT
   on manual restart/quit; counter broadcasts at N and loops; incoming death (fired
   via `tools/send_deathlink.py`, a 2nd DeathLink-tagged connection) restarts the
   current hole (`killed=True`) and is dropped in the overworld; loop-suppression
   held; TMP HUD renders in-game in the game font/palette. `send_deathlink.py` =
   reusable incoming-death test tool.

   **HUD slide-in animation — DONE + LIVE-VALIDATED 2026-07-21.** `DeathLinkHud` now
   has a `Hidden → SlideIn → Hold → SlideOut` state machine in `Tick()`: the counter
   lives off-screen left (`OffScreenX = -700`) and slides in on each wipe (and once on
   connect, to show the tally), holds `HoldSeconds` (2.5s), then slides out.
   `Mathf.MoveTowards(x, …, SlideSpeed*dt)` at `SlideSpeed = 2600 px/s` (~0.28s/slide),
   driven by `Time.unscaledDeltaTime` so it animates even while the F8 panel pauses the
   game. Position (`ApplyPosition`) recomputed each frame so it tracks resolution; the
   face + 8 outline copies move + scale together. Config toggle = `Preferences.HudAnimate`
   (`hudAnimate`, default true = slide-in; false = always-on, parked on screen), exposed
   as a checkbox in the F8 `ConnectionUI` panel ("Animate DeathLink HUD (slide-in)").
   **Number tick-up:** on a wipe the counter slides in still showing the OLD number,
   then after `BumpDelay` (0.35s) on screen ticks up to the new value with a scale pop
   (`PopScale` 0.35, `PopDuration` 0.28s) — so the increment is SEEN, not pre-applied.
   **Peak→reset "cash register":** on the wipe that hits the threshold and fires a
   DeathLink, it peaks at `N/N` (`PeakHold` 1.1s) then flips to `0/N` (`ResetDwell` 1.2s)
   instead of jumping `(N-1)/N → 0/N`. Implemented via a monotonic `DeathLinkHandler.
   DeathsSent` counter (increments per broadcast) that the HUD watches (`_lastSent`) to
   distinguish a broadcast wipe from a normal one — needed because the throttle counter
   resets to 0 the instant it broadcasts, so `WipeCount` alone never shows the peak. The
   sequencer (`AdvanceDisplay`) is shared by both display modes; `TickSlide` holds on
   screen while a bump/reset is pending so the sequence is never cut off. All four tunables
   (`BumpDelay`/`PeakHold`/`ResetDwell` + `SlideSpeed`/`HoldSeconds`) are consts at the top
   of `DeathLinkHud`. Files: `DeathLinkHud.cs`, `DeathLinkHandler.cs` (DeathsSent),
   `Preferences.cs` (HudAnimate), `ConnectionUI.cs` (F8 toggle).
3. Polish: friendlier area/section display names.

   **Location-name sub-area prefixes ✅ DONE (2026-07-25).** Every Main-campaign
   check now carries a location prefix, mirroring the episode `"Snow: <name>"`
   convention, so a bare AP location tells you which section it's in:
   - normal hole -> full sub-area code, e.g. `07A: 2D 100meter - Clear`;
   - boss hole -> **chamber number only**, e.g. `07: Computer 2 (Basic) - Clear`
     (a computer boss is a chamber-level encounter lit by several sub-areas, not
     tied to one section, so the sub-area letter would mislead);
   - chest -> sub-area code, e.g. `03B: Cars Chest` (the chest KEY item name is
     deliberately NOT prefixed — keys are items, not checks);
   - episodes keep their existing `"<Episode>: <name>"` prefix, unchanged.

   Implemented at the single `display`-name chokepoint in `data.py`
   (`_area_prefix()` / `prefixed_display()`; `Chest` gained a `subarea` field;
   `chest_loc()` now takes the `Chest`). Because location names flow through
   `display` -> `name_by_scene`, both the AP location names AND the mod's scene->
   location resolution pick it up automatically — **no mod C# change**, only a
   `wtg_ids.json` regen. `Regions.py`/`Rules.py` needed only the two
   `chest_loc(chest.display)` callers updated to `chest_loc(chest)`.

   **This is a full RENAME of every location name** (item names/IDs unchanged, and
   location IDs are position-stable), so in-progress pre-rename seeds/spoilers won't
   match — but a freshly generated seed + the redeployed mod are fully consistent.
   VALIDATED: `export_ids.py` duplicate-name asserts pass (379 locs / 114 items all
   unique); every `get_location()` reference the rules perform (Main clears/crowns,
   all boss clears, chests, episode holes, `chest_loc_by_oid`) resolves against the
   ID table. Mod rebuilt + `wtg_ids.json` redeployed; apworld reinstalled into the
   0.6.7 checkout.
4. Optional: rebuild `data.py` from the **real hub sections** (`wtg_goals.json`)
   for authentic, spatially-coherent areas.
5. **Event-driven crown/section door gating. ✅ DONE + LIVE-VALIDATED (2026-07-23).**
   The crown-chest doors (`ChestGate`) and within-chamber section
   connectors (`SectionGate`) now have the same race-free treatment the boss doors
   got: a Harmony **prefix on `OverworldButton2D.CheckOpen`** (`GamePatches.
   ButtonCheckOpenPrefix`) that returns false for a still-locked door OID, so the
   natural ball-contact open can never fire — decoupled from the per-tick
   `canOpen=false` poll (which could miss the door for up to ~3s after a *teleport*,
   since teleporting skips the overworld poll burst → a rare, non-softlocking "open a
   check early" gap). Closed now.

   **RE (via `tools/disq_objdump.py`):** the ball-contact open path is
   `OverworldButton2D.OnCollisionEnter2D` → checks `canOpen` (field 0x74) → calls
   `CheckOpen()`, which is what actually opens (`SetDoorOpen`/`Hit`/`openOrOpening=1`).
   Our own force-open uses `InstantOpenDoor()`, which **bypasses `CheckOpen`**, so
   prefixing `CheckOpen` blocks only the natural open and never our keyed-door opens.
   (`CanDoorBeOpened()` turned out to check save-state + the `previous`-door chain, NOT
   `canOpen` — so it was the wrong lever, matching the earlier PercentGate finding.)
   `CheckOpen` is parameterless (safe signature) and inherited unchanged by
   `OverworldButton2DPercentage`, so one patch covers both; the % goal door defers
   (its OID isn't in either locked set).

   **Two-layer model now (mirrors BossGate):** HARD = the `CheckOpen` prefix
   (correctness, race-free), driven by new `ChestGate.IsLocked(oid)` /
   `SectionGate.IsLocked(oid)`. SOFT = the existing `Tick` polls kept for VISUALS
   (hold a locked door's `canOpen=false` so it shows locked) + force-opening keyed
   crown doors via `InstantOpenDoor` (needed because a crown door also gates on crown
   count). Builds + deploys clean.

   **LIVE-VALIDATED (2026-07-23, fresh save, crowns + hard_sections + section seed).**
   The `CheckOpen` prefix bound (`patched (prefix): OverworldButton2D:CheckOpen`); both
   gates held every locked section connector + crown door shut; `01: Western Access` →
   opened connector `8LARX`; `Desert 2 Chest Key` → `CROWN_WESTERN2` unlocked +
   InstantOpenDoor; chest open → `Desert 1 Chest` check sent; normal play (level clears,
   checks, items) unaffected. Note: no `[GATE] blocked` log fires in steady state — the
   soft poll's `canOpen=false` makes `OnCollisionEnter2D` short-circuit before it reaches
   `CheckOpen`, so the prefix (and its block log) only fire in the post-teleport race
   window it exists to cover.

   **Chest↔door pairing bug found + FIXED (2026-07-23, pre-existing in `crowns`, not this
   change).** The test surfaced that opening `CHEST_DESERT1` needed only `Desert 2 Chest
   Key` (`CROWN_WESTERN2`). Western (01) is the ONLY area with two gated chests + two crown
   doors, and `build_levels.py`'s hand-authored `CHESTS` table had paired them by the
   DESERT1/2 ↔ WESTERN1/2 numbers, which are CROSSED. Positions prove it: `CHEST_DESERT1`
   (x=-10) sits at `CROWN_WESTERN2` (x=-10.5); `CHEST_DESERT2` (x=-40) at `CROWN_WESTERN1`
   (x=-40.5). Swapped the door column (comment added: do not re-"fix" to matching numbers).
   Was a real bug, not cosmetic: AP logic gated "Desert 1 Chest" on "Desert 1 Chest Key"
   but the chest physically opened with the other key → the intended key couldn't reach it
   (softlock risk if it held progression). Now `Desert 1 Chest Key`→`CROWN_WESTERN2`,
   `Desert 2 Chest Key`→`CROWN_WESTERN1`. Regenerated `levels.json` + `mod/ids.json`
   (counts unchanged: 68 items/379 locs/18 keyed → no ID shift, only door values), rebuilt
   + reinstalled the apworld, redeployed `wtg_ids.json`.
