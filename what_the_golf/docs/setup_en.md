# WHAT THE GOLF? Multiworld Setup Guide

This guide covers everything you need to play **WHAT THE GOLF?** in an
[Archipelago](https://archipelago.gg) multiworld: what the randomizer does, the
options you can set, and how to install, generate, connect, and play.

## What this is

Archipelago shuffles the unlocks of several games into one shared "multiworld".
In WHAT THE GOLF? this means the campaign's normal progression gating is replaced:
chambers, sub-areas, bosses, and crown chests open only when the multiworld sends
you the matching **item**, and the things you do in-game (clearing holes, earning
crowns, opening chests) are **checks** that release items into other players'
games. You play the real campaign, on a fresh save, with a mod driving the doors.

## What you check and what you receive

**Locations (checks):**

- **Hole Clears** — completing each of the 133 campaign holes (one check each).
- **Hole Crowns** — the crown challenge on holes that have one (119 of them),
  a separate check from the clear.
- **Crown chests** — the 24 overworld treasure chests (only when the `crowns`
  option is on).

With every option enabled that is **276 checks**.

**Items:**

- **Access keys** — the progression backbone; they open chambers / sub-areas.
  Either **17** (`section`) or **10** (`chamber`), per the `area_access` option.
- **Computer Boss keys** — 7 keys (Computers 1, 2, 3, 4, 7, 8, 9), only when
  `boss_keys` is on. The mod holds a boss's door shut until its key arrives.
- **Chest keys** — 18 keys for the crown-locked chests, only when `crowns` is on.
- **Flags** — one per hole (133). These are the currency for the door-percentage
  goals. Always present.
- **Filler** — junk items padding the pool.

## Goals (how you win)

Set with the `goal` option:

| Goal          | Win condition                                                    |
| ------------- | ---------------------------------------------------------------- |
| `campaign`    | Reach and beat the **Final boss** (the default).                 |
| `all_bosses`  | Defeat **every** campaign boss — all 7 computers **and** the Final boss. Pushes far more keys deep into logic; a longer, more spread-out game. |
| `door_50`     | Collect **50%** of all Flags.                                    |
| `door_75`     | Collect **75%** of all Flags.                                    |
| `door_100`    | Collect **100%** of all Flags (every hole).                      |

## Options

Set these in your YAML options file.

- **`goal`** — see the table above. Default `campaign`.
- **`area_access`** — `section` (17 keys, finer progression spread, the
  recommended default) or `chamber` (10 keys, coarser, closer to vanilla).
- **`hard_sections`** (default off) — only relevant with `section` access. See
  "A note on logic vs. physical access" below.
- **`boss_keys`** (default off) — adds the 7 computer boss keys as progression.
- **`crowns`** (default off) — adds the 24 crown chests as checks plus their
  18 keys.
- **`death_link`** (default off) — when you wipe, everyone else linked dies too,
  and vice-versa. A "death" is a real level failure (ball out of bounds / in
  water / lost) — manual restarts and quits don't count. An incoming death
  restarts your current hole (or is dropped if you're in the overworld).
- **`death_link_amnesty`** (1–30, default 10) — only matters with Death Link on.
  Because wiping is constant in WTG, this throttles outgoing deaths: one
  broadcast per this many local wipes (`1` = every wipe). A wipe caused by an
  incoming death is never re-broadcast, so deaths can't ping-pong.

### A note on logic vs. physical access

`hard_sections` is the one option whose description most easily confuses, so
here is the distinction:

- The **generation logic** — the model Archipelago uses to place items so the
  seed is always beatable — *always* requires each sub-area's own Access key,
  whether `hard_sections` is on or off. Item placement is identical either way,
  and the seed is always completable by following logic.
- The **physical game**, however, is looser with `section` access: sub-areas
  inside one chamber share an open overworld room, so with `hard_sections` **off**
  you *can* walk into a locked sibling sub-area before its key arrives and play
  ahead. That is "playing out of logic" — doing something the logic didn't
  require you to be able to do. It's an optional bonus; it can never softlock you
  or make a seed unbeatable.
- Turn `hard_sections` **on** and the mod physically holds those connecting doors
  shut until the key arrives, so the game matches the logic exactly. This changes
  only the *physical* enforcement — **not** the item placement or logic.

Under `chamber` access there is no looseness (the computer/boss doors are hard
walls), so `hard_sections` does nothing.

## Required software

- [Archipelago](https://github.com/ArchipelagoMW/Archipelago/releases) — the
  generator and server (0.6.7 or newer).
- The `what_the_golf` apworld installed into your Archipelago `worlds/` folder
  (or double-click a packaged `.apworld`).
- **WHAT THE GOLF?** on PC (Steam).
- [**MelonLoader**](https://melonwiki.xyz/) v0.7.3 installed into the game, plus
  the WHAT THE GOLF? Archipelago mod.

> **Loader note:** this game must use **MelonLoader**, not BepInEx — BepInEx 6's
> Dobby detour hard-crashes WHAT THE GOLF at graphics init.

## Installing the mod

1. Install **MelonLoader v0.7.3** into your WHAT THE GOLF? folder (copy
   `version.dll` and the `MelonLoader/` folder to the game root), then **launch
   the game once** so MelonLoader generates its interop assemblies, and quit.
2. Copy the mod's `WtgArchipelago.dll` into `<game>\Mods\`.
3. Copy `Archipelago.MultiClient.Net.dll` into `<game>\UserLibs\`.
4. Copy the generated `wtg_ids.json` into the game root (next to the game exe).

(Building from source: `cd mod && dotnet build -c Debug` compiles and
auto-deploys all of the above. Requires the .NET 6 SDK. Kill the game before
rebuilding — it locks the DLL.)

## Generating a game

1. Create a YAML options file for WHAT THE GOLF? with the options above (Goal,
   Area Access, Boss Keys, Crowns, Death Link, etc.).
2. Generate a seed with your YAML included.
3. Host the resulting game locally or on [archipelago.gg](https://archipelago.gg).

## Playing

1. **Use a fresh save.** The mod writes door/unlock state and only drives gating
   reliably from a clean save slot — do **not** use a 100%-complete save. WTG has
   multiple slots; start a fresh one for your multiworld run.
2. **Launch the game** (with MelonLoader + the mod installed) and press **F8** to
   open the Archipelago connection panel.
3. Enter the **host / port / slot name / password** and click **Connect**. Tick
   "Auto-connect on launch" to skip this next time.
4. **Play the campaign.** Clearing holes fires their checks; the multiworld hands
   out items in return. When you receive an **Access key**, that chamber/sub-area
   becomes reachable — use the **pause-menu teleporter** to hop there. Boss keys
   and chest keys open their doors as they arrive.
5. **Win** by satisfying your goal (beat the Final boss / beat all bosses / reach
   your Flag percentage).

## Death Link HUD

When Death Link is active, an on-screen counter shows your progress toward the
next outgoing death (`DEATHS n/m`, where `m` is your amnesty value). It slides in
on each wipe and broadcasts when it reaches the threshold. You can toggle its
slide-in animation in the F8 panel.
