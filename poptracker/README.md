# WHAT THE GOLF? — PopTracker pack

A map tracker for the [WHAT THE GOLF? Archipelago](https://github.com/Joxtacy/what-the-golf-ap)
world: one map tab per chamber (10 → 00) plus one per DLC episode, with full
logic and Archipelago auto-tracking.

## Install

Copy this whole folder (or the released `.zip`) into PopTracker's `packs/`
directory, load **WHAT THE GOLF? Map Tracker**, pick the **Archipelago** variant,
then click **AP** and enter your host / slot / password.

Everything configures itself from slot data — goal, area access granularity,
boss keys, crown chests and which episodes are in your seed. The settings popup
is only needed for offline / manual tracking.

## Reading the map

Markers sit at their real overworld coordinates wherever the in-game dump has
captured them. Anything not yet captured is laid out on a tidy grid in a band
below a divider marked **ESTIMATED POSITIONS — NOT YET DUMPED**, with its
sub-area labelled `… EST` and its markers drawn dimmer. Those markers still
track correctly — only their placement is a guess.

| Marker | Meaning |
| --- | --- |
| Bright | in logic — you hold the keys Archipelago expects |
| **Yellow (sequence break)** | reachable by *walking*, but out of logic |
| Dim | not reachable yet |
| Struck through | already checked |

The yellow state is specific to this game. Sub-areas inside one chamber share an
open overworld room, so with `area_access: section` and `hard_sections` **off**
you can physically walk into a locked sibling sub-area and play ahead. That is
never a softlock and never required — turn it off with **Show walk-in access**
in the settings popup, and it disappears automatically on a `hard_sections` seed.

## Do not edit these files

**This directory is generated.** It is rebuilt wholesale by

```
python tools/build_poptracker.py
```

from `what_the_golf/data.py` — the same source the apworld and the game mod are
built from — so the pack's 379 location names can never drift from the seed's.
Hand edits are lost on the next build, and CI (`--check`) fails if the committed
output does not match a fresh build.

To change the pack, edit **`tools/poptracker_src/`** (manifest, settings, Lua) or
the generator itself, then rebuild.
