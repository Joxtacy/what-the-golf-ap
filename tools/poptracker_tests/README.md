# Verifying the PopTracker pack

Three layers, cheapest first. The first is automatable; the other two need
PopTracker itself, because only its own Lua/rule engine can confirm it reads the
generated JSON the way we intend.

## 1. Build integrity (no PopTracker needed)

```
python tools/build_poptracker.py --check
```

Fails if `poptracker/` differs from a fresh build. The build itself asserts that
the emitted location/item name sets exactly equal `data.py`'s tables, that no
access rule references an undeclared item code, that no emitted name contains a
character the rule parser treats as syntax (`:`, `,`, `|`, `@`), and that no two
top-level locations — or two children of one parent — share a name.

That last one matters: PopTracker silently renames a duplicate to `"Name[1]"`,
after which every `@Name/...` path resolves to the *first* node and the second
node's sections can never be found. It cost us all 24 chest checks once.

## 2. Logic parity against the apworld's real logic

```
python tools/check_poptracker_logic.py --ap ../Archipelago-Keen
```

Builds a live Archipelago `MultiWorld` per option combo — the same code path
generation uses — and compares `location.can_reach(state)` against an evaluator
for the pack's emitted `access_rules`, over ~60–85 item subsets per combo
(empty, full, random, and each progression item alone). Also checks that the
locations a seed does *not* contain are exactly the ones the pack hides behind a
`visibility_rules`.

Currently 8 combos / ~165k comparisons. The two sides are joined only through
artifacts that ship (`item_mapping.lua`, `location_mapping.lua` and `data.py`'s
id tables), so a broken name↔code bridge fails the test instead of hiding in it.

`SequenceBreak` counts as **not** in logic — it models the documented walk-in
looseness Archipelago never assumes — so a location AP calls unreachable may
legitimately be SequenceBreak, but one AP calls reachable must be fully Normal.

## 3. In-PopTracker replay (closes the loop)

Layer 2 validates *our* evaluator. To confirm PopTracker's engine agrees:

```
python tools/check_poptracker_logic.py --dump-lua parity_cases.lua
```

writes a self-checking harness of already-verified cases, evenly split across
None / SequenceBreak / Normal. Alongside it, `slotdata_cases.lua` here exercises
`onClear`/`apply_slot_data` — the other risky half, translating the apworld's
`fill_slot_data` into tracker state (the `area_access` string, the Goal stage
order, `hard_sections` forcing the walk-in hint off, retiring disabled features,
`flag_goal` vs the whole pool, all 47 Flag names feeding one counter,
`onLocation` idempotency).

`autotab_cases.lua` covers the map auto-switch: payload parsing, area→tab
routing, the check-inference fallback, connect-burst suppression, and the
one-way handover from fallback to the mod's data-storage signal.
`AutoTab.apply()` is public precisely so that path can be driven with no live
server (the data-storage key name needs a connection to build).

To run any of them:

1. Copy the pack to PopTracker's `packs/`, then copy the `.lua` into its
   `scripts/` and append to that copy's `init.lua`:
   `ScriptHost:LoadScript("scripts/<file>.lua")`
   Do this to the **installed copy**, never to `poptracker/` — that directory is
   regenerated wholesale.
2. Back up `%APPDATA%\PopTracker\PopTracker.json`, set `"log": true`, and point
   its `pack` block at the pack (`uid` `joxtacy_what_the_golf`, `variant` `ap`).
3. Launch PopTracker, wait a few seconds, quit, and read
   `%APPDATA%\PopTracker\log.txt` — `print()` output lands there.
4. Restore the config and reinstall the clean pack.

Last run (PopTracker 0.35.1): `PARITY: 240/240`, `SLOTDATA: 28/28`,
`AUTOTAB: 42/42`.

## What none of this covers

**A live Archipelago session.** Connecting requires clicking **AP** in the UI, so
it can't be driven headlessly — the handler wiring (`AddClearHandler` etc.) and
the real item/check feed still want one manual pass against a hosted room.

**The mod's half of the auto-switch.** `mod/src/Mapping/CurrentArea.cs` builds
clean but has never run in-game. One play session validates it, and the same
session can do the `GoalDumper` position walk that Phase 3's real map
coordinates need:

1. Set `Mod.DumpersEnabled = true` (`mod/src/Mod.cs`), `dotnet build -c Debug`.
2. Fresh save, connect via F8, and confirm `Publish current area (tracker map
   auto-switch)` is ticked.
3. Walk/teleport around every chamber, then enter each of the five episode
   overworlds. Watch for `[AREA]` lines in the MelonLoader log and the tracker's
   map tab following you; `[GOALS] N with live pos` should climb.
4. Set `DumpersEnabled = false`, rebuild, and commit the refreshed
   `mod/wtg_goals.json`.

The dumper merges across sessions now, so a partial walk is still useful and a
later pass adds to it rather than replacing it.
