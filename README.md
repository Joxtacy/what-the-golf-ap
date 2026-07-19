# WHAT THE GOLF? — Archipelago

An in-progress [Archipelago](https://archipelago.gg) (multiworld randomizer)
integration for [WHAT THE GOLF?](https://store.steampowered.com/app/785790/WHAT_THE_GOLF/).

## The two pieces

An AP integration is two separate projects, both here:

1. **The apworld** (`what_the_golf/`, Python) — the "brain": items, locations,
   regions, logic, options, goals. Generates seeds. **Scaffold complete**, built
   from the real game structure (data still has placeholder hole counts/names).
2. **The game mod** (`mod/`, C#) — a [BepInEx 6 **IL2CPP**](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html)
   plugin that hooks the game to detect level clears and apply received items.
   **Scaffold complete**; the game-specific method names are `TODO` (see `mod/README.md`).

Confirmed engine: **Unity 2020.3.48f1, IL2CPP** (inspected in the installed game).

## How the game maps onto Archipelago

- **Locations (checks):** every hole `- Clear`, every hole `- Crown`, each
  `Defeat Computer NN`, and 17 `Terminal N`.
- **Items** — progression is *decoupled* from the game's native gating:
  - **Access keys** (`"Level 07 Access"`, …, `"Sporty Sports Access"`) — one per
    chamber. The mod opens a chamber when its key arrives, ignoring the native
    "all flags collected" door rule.
  - **Flag** — a *counted* token; collecting X of them satisfies the 50/75/100%
    completion-door goals. (This is how the game's area-local Flags become items.)
  - **Filler** — cosmetics/trophies to pad the pool.
- **Goals** (option): `campaign` (defeat the final Computer) or
  `door_50 / door_75 / door_100`.

### Real structure encoded (from cross-checked trophy guides)

- 10 campaign chambers: **Levels 09 → 00** (counting down), plus a separate
  **Sporty Sports** DLC chamber. **9 Computer bosses** — Level 02 has none (real).
- Each chamber has 1–4 named **sub-areas** (theme names are the game's own door
  labels: 2D Golf, Livingroom Golf, Boom/Space/Foot/Golf Game, Portal Golf,
  Super Putt, Gravity/First-Person/Kitchen, Musical/Stealthy, Jungle/Motorized,
  Sandy/Desert, Biomass, plus secrets El Duderino / Secret Saw).
- **114 holes** main / **124** with DLC → **274** possible location checks,
  **17** item types.

> Placeholder data: per-sub-area **hole counts** are mostly not publicly
> documented, and individual **hole names** aren't documented at all, so those
> are synthetic/estimated. Everything is generated from one file —
> `what_the_golf/data.py` — so correcting them is a single-file edit.

## Layout

```
what_the_golf/          the apworld (Python)
  data.py               <-- SINGLE SOURCE OF TRUTH: chambers/sub-areas/holes + ID maps
  __init__.py Options.py Items.py Locations.py Regions.py Rules.py
  docs/setup_en.md
mod/                    the BepInEx 6 (IL2CPP) game mod (C#)
  src/... WtgArchipelago.csproj NuGet.Config README.md
  ids.json              generated ID table shared with the apworld
tools/export_ids.py     dumps data.py's ID maps to mod/ids.json (framework-free)
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
