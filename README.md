# WHAT THE GOLF? — Archipelago

An [Archipelago](https://archipelago.gg) (multiworld randomizer) integration for
[WHAT THE GOLF?](https://store.steampowered.com/app/785790/WHAT_THE_GOLF/).
Works end-to-end: generate a seed, launch the game with the mod, connect, and
play the campaign as a multiworld. See `what_the_golf/docs/setup_en.md` for the
player setup guide and `STATUS.md` for detailed project status.

## The two pieces

An AP integration is two separate projects, both here:

1. **The apworld** (`what_the_golf/`, Python) — the "brain": items, locations,
   regions, logic, options, goals. Built from the **real game structure** (dumped
   in-game — see below); generates solvable seeds on Archipelago 0.6.7.
2. **The game mod** (`mod/`, C#) — a [**MelonLoader**](https://melonwiki.xyz/)
   plugin that hooks the running game to detect level clears/crowns/chest-opens,
   send checks, apply received items (open the matching doors), drive the goal,
   and handle DeathLink. Passive until connected; in-game connection UI on **F8**.

Confirmed engine: **Unity 2020.3.48f1, IL2CPP** (inspected in the installed game).
(We use MelonLoader rather than BepInEx 6 because BepInEx's Dobby detour
hard-crashes this game at graphics init — see `STATUS.md`.)

## How the game maps onto Archipelago

- **Locations (checks):** every hole `<scene> - Clear`, plus `<scene> - Crown`
  for holes with a crown challenge, plus (optionally) each overworld crown chest.
  **Up to 276 locations** — 133 clears + 119 crowns + 24 chests.
- **Items** — progression is *decoupled* from the game's native gating; the mod
  opens the matching in-game door(s) when a key arrives:
  - **Access keys** — gate the themed areas. Granularity is the `area_access`
    option: `section` (17 keys, one per in-game sub-area unlock — default) or
    `chamber` (10 keys, one per chamber). See the looseness note below.
  - **Computer Boss keys** — 7 keys (`boss_keys` option); each holds a computer
    boss's door shut until received.
  - **Chest keys** — 18 keys for the crown-locked chests (`crowns` option).
  - **Flag** — a *counted* token (one per hole); collecting X% satisfies the
    50/75/100% completion-door goals.
  - **Filler** — cosmetics/trophies to pad the pool.
- **Goals** (option): `campaign` (reach the Final boss), `all_bosses` (defeat all
  7 computers + the Final boss), or `door_50 / door_75 / door_100` (Flag %).
- **DeathLink** (option) — a "death" is a level failure (ball OOB / water / lost),
  throttled by `death_link_amnesty` (one broadcast per N wipes).

See `what_the_golf/Options.py` for the full option docstrings.

### Real structure (dumped from the game)

The campaign is **11 chambers** counting **10 → 00** (10 = free intro, 00 =
finale) containing **133 holes**, read from the game's own `OverworldLevelData`
asset. Chambers subdivide into **21 sub-areas** which collapse to **17 unlockable
"gate units"** (some sub-areas share one in-game door: `05A/B/C` and `06A/B`).
Level names are the game's real scene names. See `mod/harvested-levels.md` and the
dumped `mod/wtg_*.json`.

> **Section-access looseness:** the game hard-gates chamber↔chamber (the
> computer/boss doors), but sub-areas *within* a chamber share an open overworld
> room, so with `area_access: section` you *can* walk to a locked sibling sub-area
> once any sub-area of that chamber is reachable. It's out-of-logic (logic still
> requires each key) but never a softlock, and the `hard_sections` option closes
> it physically. `area_access: chamber` has no looseness.

## Layout

```
what_the_golf/          the apworld (Python)
  data.py               <-- loads levels.json; exposes chambers/gate-units + ID maps
  levels.json           the real campaign structure (built from the game dump)
  __init__.py Options.py Items.py Locations.py Regions.py Rules.py
  docs/setup_en.md      player setup guide
mod/                    the MelonLoader (IL2CPP) game mod (C#)
  src/... WtgArchipelago.csproj NuGet.Config README.md
  ids.json              generated ID table (+ unlock/boss/chest maps) shared with the apworld
  wtg_*.json            in-game dumps (levels, sections, doors, goals, chests)
tools/
  build_levels.py       builds what_the_golf/levels.json from the game dump
  export_ids.py         dumps data.py's ID maps + unlock map to mod/ids.json
```

## Roadmap

- [x] apworld built on the real game structure (chambers/sections/holes)
- [x] mod loads under MelonLoader; AP client, Harmony patches, mapping
- [x] Wire items → game effects (open doors, decouple native gating, count Flags)
- [x] Access gating (`area_access`: section/chamber) — live-validated
- [x] Richer progression options: `boss_keys`, `hard_sections`, `crowns`
- [x] Goal options: `campaign`, `all_bosses`, `door_50/75/100`
- [x] In-game connection UI (F8), passive-until-connected lifecycle
- [x] DeathLink (count-based throttle + on-screen HUD)
- [ ] Real 2-player multiworld test (all live tests so far have been solo)
- [ ] Packaged release (`.apworld` + mod install bundle)
- [ ] Stretch: DLC (Sporty Sports), ball shapes / Transmogrif, friendlier names

## Testing the apworld

Copy/symlink `what_the_golf/` into an [Archipelago source](https://github.com/ArchipelagoMW/Archipelago)
checkout at `worlds/what_the_golf`, then from the Archipelago root:

```
python -c "from worlds.what_the_golf import WTGWorld; print(WTGWorld.game)"
python Generate.py            # with a WHAT THE GOLF? YAML in Players/
```

A successful generate proves the whole item/location/logic model — no game
needed. `data.py` and `tools/export_ids.py` are framework-free.

Validated against the **released Archipelago 0.6.7** (and `main`/0.6.8): the
world loads and generates a solvable multiworld across all option combinations
(59 item names / up to 276 locations). To test a specific version,
`git checkout <tag>` in the Archipelago checkout before generating.
