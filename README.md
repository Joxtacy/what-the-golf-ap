# WHAT THE GOLF? — Archipelago

An in-progress [Archipelago](https://archipelago.gg) (multiworld randomizer)
integration for [WHAT THE GOLF?](https://store.steampowered.com/app/785790/WHAT_THE_GOLF/).

## The two pieces

An AP integration is two separate projects, both here:

1. **The apworld** (`what_the_golf/`, Python) — the "brain": items, locations,
   regions, logic, options, goals. Generates seeds. Built from the **real game
   structure** (dumped in-game — see below), generates solvable seeds on
   Archipelago 0.6.7.
2. **The game mod** (`mod/`, C#) — a [**MelonLoader**](https://melonwiki.xyz/)
   plugin that hooks the running game to detect level clears/crowns, send checks,
   and apply received items (unlock areas). Loads and runs end-to-end in-game.

Confirmed engine: **Unity 2020.3.48f1, IL2CPP** (inspected in the installed game).
(We use MelonLoader rather than BepInEx 6 because BepInEx's Dobby detour
hard-crashes this game at graphics init — see `STATUS.md`.)

## How the game maps onto Archipelago

- **Locations (checks):** every hole `<scene> - Clear`, plus `<scene> - Crown`
  for holes with mini-challenges. **251 locations** (132 clears + 119 crowns).
- **Items** — progression is *decoupled* from the game's native gating; the mod
  opens the matching in-game door(s) when an Access key arrives:
  - **Access keys** — gate the themed areas. Granularity is the `area_access`
    option: `section` (17 keys, one per in-game sub-area unlock — default) or
    `chamber` (10 keys, one per chamber). See the looseness note below.
  - **Flag** — a *counted* token (one per hole); collecting X% satisfies the
    50/75/100% completion-door goals.
  - **Filler** — cosmetics/trophies to pad the pool.
- **Goals** (option): `campaign` (reach the Final boss) or
  `door_50 / door_75 / door_100` (Flag %).

### Real structure (dumped from the game)

The campaign is **11 chambers** counting **10 → 00** (10 = free intro, 00 =
finale) containing **132 holes**, read from the game's own `OverworldLevelData`
asset. Chambers subdivide into **21 sub-areas** which collapse to **17 unlockable
"gate units"** (some sub-areas share one in-game door: `05A/B/C` and `06A/B`).
Level names are the game's real scene names. See `mod/harvested-levels.md` and the
dumped `mod/wtg_*.json`.

> **Section-access looseness:** the game hard-gates chamber↔chamber (the
> computer/boss doors), but sub-areas *within* a chamber share an open overworld
> room, so with `area_access: section` you *can* walk to a locked sibling sub-area
> once any sub-area of that chamber is reachable. It's out-of-logic (logic still
> requires each key) but never a softlock. `area_access: chamber` has no looseness.

## Layout

```
what_the_golf/          the apworld (Python)
  data.py               <-- loads levels.json; exposes chambers/gate-units + ID maps
  levels.json           the real campaign structure (built from the game dump)
  __init__.py Options.py Items.py Locations.py Regions.py Rules.py
  docs/setup_en.md
mod/                    the MelonLoader (IL2CPP) game mod (C#)
  src/... WtgArchipelago.csproj NuGet.Config README.md
  ids.json              generated ID table (+ unlock map) shared with the apworld
  wtg_*.json            in-game dumps (levels, sections, doors, goals)
tools/
  build_levels.py       builds what_the_golf/levels.json from the game dump
  export_ids.py         dumps data.py's ID maps + unlock map to mod/ids.json
```

## Roadmap

- [x] Phase 1 — apworld scaffold on real structure
- [x] Phase 1 — mod scaffold (BepInEx 6 IL2CPP, AP client, patches, mapping)
- [ ] Phase 0 — real per-sub-area hole counts + names into `data.py`
- [ ] Dump the game (Il2CppDumper) → fill method/field names in `mod/`
- [ ] Wire items → game effects (open doors, suppress native gating, count Flags)
- [ ] Tighter logic (gate bosses/sub-areas), in-game connect UI, DeathLink meaning
- [ ] Packaged release + setup guide

## Testing the apworld

Copy/symlink `what_the_golf/` into an [Archipelago source](https://github.com/ArchipelagoMW/Archipelago)
checkout at `worlds/what_the_golf`, then from the Archipelago root:

```
python -c "from worlds.what_the_golf import WTGWorld; print(WTGWorld.game)"
python Generate.py            # with a WHAT THE GOLF? YAML in Players/
```

A successful generate proves the whole item/location/logic model — no game
needed. `data.py` and `tools/export_ids.py` are framework-free and already
verified (274 locations / 17 items, balanced pool, unique IDs).

Validated against the **released Archipelago 0.6.7** (and `main`/0.6.8): the
world loads and generates a solvable 3-player multiworld. To test a specific
version, `git checkout <tag>` in the Archipelago checkout before generating.
