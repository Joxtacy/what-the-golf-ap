# Harvested level data (sample)

First successful in-game harvest via `GameAnalytics:OnLevelComplete` +
`GameState.CurrentLevelInfo()` (2026-07-19). No crash; hook fires reliably.

## Key facts learned
- **`LevelData.ID` is an opaque 6-char code** (e.g. `DI3JRA`), NOT human-readable.
- **`LevelData.SceneName` IS human-readable** (e.g. `Livingroom champagne`) — the
  useful label for identifying a level.
- **`isBossBattle` works** — `Final boss` reported `boss=True`.
- **Crown detection works** — `challenges=done/total` (on a 100% save, done==total).
  Some levels have 0 challenges (no crown), others 1 or 2.
- Main-game and **Sporty Sports ("episode 1")** levels share the same pipeline.

## Sample (id — scene — boss — challenges)
| id | scene | boss | challenges |
|----|-------|------|-----------|
| DI3JRA | AHoleInAOne | no | 2/2 |
| 0A6P6X | Bowling | no | 1/1 |
| HO2VFP | Bowling 1 - UTurn | no | 0/0 |
| 1KO6F3 | Bowling 2 | no | 1/1 |
| 3HGCVU | SUPERGOLF - tutorial 1 | no | 1/1 |
| ZNMSS0 | Livingroom champagne | no | 2/2 |
| 114GF5 | 2D credits | no | 2/2 |
| 059JVP | Final boss | **YES** | 0/0 |
| 2834C4 | Horse Jumping | no | 2/2 | (Sporty Sports)
| EIGN3T | ManBall RunningTrack Long | no | 0/0 | (Sporty Sports)
| WH165D | HorseRacing Simple | no | 0/0 | (Sporty Sports)
| 9OF5VT | CityCycling | no | 0/0 | (Sporty Sports)
| 9JA8HZ | ReversePolo rings | no | 1/1 | (Sporty Sports)

## Implication
The placeholder structure in `what_the_golf/data.py` (invented `9-B Livingroom
Golf 1` names) doesn't match reality. The real world is keyed by opaque IDs with
human scene names. Next: enumerate the FULL level list authoritatively (in-mod
`Resources.FindObjectsOfTypeAll<LevelData>` and/or the game's level/overworld
containers) to rebuild `data.py` + `LocationMap` from real data.

## FULL DUMP (2026-07-19) — mod/wtg_levels.json
`Resources.FindObjectsOfTypeAll<LevelData>()` captured **642** LevelData in one
pass (game keeps all level assets in memory — no walking needed).

- 385 short-id (<=6 chars) = real game; 257 long-id = daily/seasonal/special
  (Daily_Massive, S21_HungryHole, WelcomeBark, Horseparty...) — NOT campaign.
- **223 short-id levels have challenges (1 or 2)** = likely the real campaign
  holes (crowns). Scenes: Livingroom couch, AHoleInAOne, CatBall, EvilFlag,
  SpaceGolf, Bowling, SUPERGOLF, FPG (first-person), car (motorized)...
- 9 bosses (matches 9 Computers): "2D HoleInOne 0N" variants + "Final boss".
- challenge distribution: 0→415, 1→75, 2→152.
- Fields captured per level: id, scene, pun (completion msg), boss, par, challenges.

MISSING: chamber/area grouping is NOT in LevelData — it lives in the overworld/
hub structure (which hubsection contains which levels + flag/crown door gates).
Design decision pending: (A) flat/count-based world from the 223 campaign holes
(playable now), or (B) also dump overworld structure to keep chamber-access design.

## CHAMBER STRUCTURE DECODED (2026-07-19) — from dump.cs `PlateInfoManager.AreaIDEnum`

The real gate structure is the "computer" doors (`OverworldMainDoorRobot`, one per
chamber, `bossLevelID = ID_2D_HOLEINONE_N`). Each door has plates
(`OverworldMainDoorPlate`); each plate's `PlateInfoManager` has an `AreaIDEnum Name`
plus `List<LevelData> levels` / `List<OverworldGoal> goals` = the holes behind that
sub-area. The enum reveals the whole `<THEME>_<CHAMBER><SUBAREA>` scheme, chambers
counting **10 -> 00**:

| Chamber | Sub-areas (plates)                         |
|---------|--------------------------------------------|
| 10 INTRO| (start, free)                              |
| 09      | EASY2D_09A, LIVINGROOM_09B                  |
| 08      | PLATFORMERS_08A, SOCCER_08B, SPACE_08C, EXPLOSION_08D |
| 07      | OL_07A, LEBOWSKI_07B                        |
| 06      | PORTAL_06A, SUPERPUTT_06B                   |
| 05      | KITCHEN_05A, GRAVITY_05B, FPG_05C           |
| 04      | MUSIC_04A, STEALTH_04B                      |
| 03      | JUNGLE_03A, CARS_03B                        |
| 02      | WATER_02                                    |
| 01      | WETERN_01 (Western, final run)              |
| 00      | END_00                                      |

21 AreaIDEnum values total (INTRO_10..END_00). NOTE: WATER_02 exists (earlier
"no Computer 02" note was wrong). `DoorDumper.cs` reads this live -> wtg_doors.json
(door -> plate.area_id/area_name + levels/goals scenes) = the level->sub-area->chamber
membership needed to rebuild the apworld around real chambers.
