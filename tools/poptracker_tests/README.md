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

## 4. Live session against a real server

Validated 2026-08-03 against Archipelago 0.6.7 + PopTracker 0.35.1. To redo it:

```powershell
# Generate. SKIP_REQUIREMENTS_UPDATE avoids a stdin prompt for an unrelated
# apworld's missing dep (dolphin-memory-engine); the Bash tool does NOT
# propagate env vars to python here, so run this from PowerShell.
$env:SKIP_REQUIREMENTS_UPDATE = "1"
python Generate.py --player_files_path Players_wtg --outputpath output_wtg
python MultiServer.py --port 38281 output_wtg\AP_*.zip
```

The test YAML puts **all 17 Access keys in `start_inventory`**, so every sub-area
is teleport-reachable immediately and you can hop between chambers at will.

Set `autoConnect = true` in `<game>\UserData\MelonPreferences.cfg` and the mod
connects on launch with no F8 click. PopTracker still needs one **AP** click —
`at_uri`/`at_slot` only prefill the dialog.

`tools/watch_area_key.py` is the server's-eye view: it connects as a tracker and
Get/SetNotify's the area key, printing every update. That validates the mod half
with no UI at all.

**Use a fresh save slot, never the 100% save** — connected, `GoalWatcher` would
fire ~250 checks at once and `ChamberUnlock` writes door state to whatever slot
is loaded.

Results: 15 publishes, correct chamber for every one, no spam between changes,
no errors. All three signal tiers exercised —

| tier | fires when | observed |
| --- | --- | --- |
| `src=scene` | inside a hole | `1\|09\|09B\|Main\|1\|scene\|Livingroom couch` |
| `src=save` | overworld, base campaign | `1\|08\|08B\|Main\|0\|save\|` |
| `src=campaign` | inside an episode | `1\|Among Us\|\|Amongus\|0\|campaign\|` |

— and PopTracker followed along, switching map tabs as the key changed.

## What none of this covers

The item/check feed against a live server was only exercised incidentally. The
handlers themselves are unit-tested (see 3), and the connection wiring is now
proven, but a full playthrough comparing every check against the server has not
been done.
