# WHAT THE GOLF? Multiworld Setup Guide

This guide covers everything a new player needs to play **WHAT THE GOLF?** in an
[Archipelago](https://archipelago.gg) multiworld: what the randomizer does, what
the mod changes about the game, the options you can set, the in-game controls, and
how to install, generate, connect, and play.

---

## What this is

Archipelago shuffles the unlocks of several games into one shared "multiworld".
In WHAT THE GOLF? this means the campaign's normal progression gating is replaced:
chambers, sub-areas, bosses, and crown chests open only when the multiworld sends
you the matching **item**, and the things you do in-game (clearing holes, earning
crowns, opening chests) are **checks** that release items into other players'
games (and your own).

You play the **real campaign, on a fresh save**, with a mod (running under
MelonLoader) driving the doors. Nothing about the golf itself changes — the levels,
physics, and challenges are all vanilla. What changes is **when** things open.

---

## What the mod changes vs. vanilla

The mod is **completely passive until you connect it to a multiworld** (press F8 to
connect). Installed but not connected, the game behaves exactly like vanilla. Once
connected, here is what it changes:

- **Progression is decoupled from the game's native gating.** Normally you unlock
  the next area by beating a computer boss. In a multiworld, areas/bosses/chests
  open when the corresponding **item** arrives from the multiworld instead — which
  may be long before or after you'd naturally reach them. The mod opens the exact
  in-game door(s) each item maps to, and holds locked doors shut until then.
- **You travel with the pause-menu teleporter.** Because unlocks arrive out of
  order, you hop between unlocked areas using the game's built-in teleporter
  (pause menu) rather than walking the campaign linearly. Any area whose key you've
  received is teleport-reachable.
- **Clearing holes sends checks.** Every hole clear (and every crown, and every
  chest you open) reports to the multiworld. You'll often be sending other players
  their items.
- **The final win condition is your chosen goal**, not just "beat the last boss"
  (see Goals below).
- **A few small on-screen additions** — a connection panel, an optional event feed,
  and progress HUDs (see In-game controls below). All are configurable or
  toggleable, and the base game's own UI is untouched.
- **Optional Death Link and Traps** can restart your hole or briefly mess with you,
  if those options are enabled in the seed.

The mod **writes save state** (which doors are open) to your save slot, so always
use a **dedicated fresh save** for a multiworld run — never your 100% save.

---

## What you check and what you receive

**Locations (checks):**

- **Hole Clears** — completing each of the 133 base-campaign holes (one check each).
- **Hole Crowns** — the crown challenge on holes that have one (119 of them),
  a separate check from the clear.
- **Crown chests** — the 24 overworld treasure chests (only when the `crowns`
  option is on).
- **Episode holes** — each enabled episode adds its holes as Clear checks (100
  holes across all 5 episodes), plus the 3 crownable episode holes (all in Sporty
  Sports), only for the episodes you enable.

With every option enabled that is up to **379 checks**.

**Items:**

- **Access keys** — the progression backbone; they open chambers / sub-areas.
  Either **17** (`section`) or **10** (`chamber`), per the `area_access` option.
- **Computer Boss keys** — 7 keys, for **Computers 1, 2, 3, 4, 5, 7, and 8**, only
  when `boss_keys` is on. The mod holds a boss's door shut until its key arrives.
  (The finale's special boss is covered by the campaign goal, not a key.)
- **Chest keys** — 18 keys for the crown-locked chests, only when `crowns` is on.
  (6 of the 24 chests are freely reachable in vanilla and need no key — they're
  still checks.)
- **Episode Access keys** — one per enabled episode; the mod keeps you out of a
  locked episode at the episodes hub until its key arrives.
- **Flags** — one per hole (base 133, plus one per enabled episode hole). These are
  the currency for the door-percentage goals. Always present.
- **Traps** — disruptive/funny items, only when `traps` is on (see below).
- **Filler** — junk/cosmetic items padding the pool.

---

## Goals (how you win)

Set with the `goal` option:

| Goal          | Win condition                                                    |
| ------------- | ---------------------------------------------------------------- |
| `campaign`    | Reach and beat the **Final boss** (the default).                 |
| `all_bosses`  | Defeat **every** campaign boss — all 7 computers **and** the Final boss. Pushes far more keys deep into logic; a longer, more spread-out game. |
| `door_50`     | Collect **50%** of all Flags, then open the in-game 50% completion door and press the button inside. |
| `door_75`     | Collect **75%** of all Flags, then open the 75% door and press its button.   |
| `door_100`    | Collect **100%** of all Flags (every hole), then open the 100% door and press its button. |

For the `door_*` goals, collecting enough Flags **opens the matching completion
door** in the game (the same 50/75/100% doors that exist in vanilla); walking into
the button behind it is what actually reports victory. Enabling episodes raises the
Flag total, so the percentage target scales up with the board.

---

## Options

Set these in your YAML options file.

- **`goal`** — see the table above. Default `campaign`.
- **`area_access`** — `section` (17 keys, finer progression spread, the
  recommended default) or `chamber` (10 keys, coarser, closer to vanilla).
- **`boss_keys`** (default off) — adds the 7 computer boss keys as progression.
- **`hard_sections`** (default off) — only relevant with `section` access. See
  "A note on logic vs. physical access" below.
- **`crowns`** (default off) — adds the 24 crown chests as checks plus their
  18 keys.
- **`episodes`** (default none) — a set of extra episodes (DLC) to fold in. Valid
  entries: **`Sporty Sports`, `Snow`, `Hotdog`, `Alive`, `Among Us`**. Each adds
  its holes as checks and one Episode Access key, and its clears grant Flags (so
  the `door_*` targets grow). **You must own the corresponding DLC.**
- **`traps`** (default off) — replaces some of your filler with trap items (see
  the In-game features section for what the traps do).
- **`trap_percentage`** (0–100, default 20) — only matters with `traps` on. The
  percentage of filler slots turned into traps; it scales with the seed's size.
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

> **Known minor quirk (`hard_sections` + `section`):** two special "Main Crown
> Doors" (leading to Bowling/Lebowski and Cars) open on a total-crown count and
> aren't held by `hard_sections`, so you may be able to walk into those two
> sub-areas early. It's out-of-logic reachability only — never a softlock.

---

## In-game controls & features

Everything the mod adds is reachable from two hotkeys:

### `F8` — the Archipelago panel

The main control panel. Here you:

- Enter **host / port / slot name / password** and **Connect** / **Disconnect**.
- Tick **Auto-connect on launch** to reconnect automatically next time.
- Toggle the on-screen extras: the **event feed** (and its per-category filters,
  size, corner, width), the **Flag progress HUD**, the **Death Link HUD
  animation**, and the **hub-portal** QoL below.

The game pauses while the panel is open.

### `` ` `` (backtick / tilde) — the command console

An in-game console for multiworld commands and a scrollback of server traffic.
Type client commands like `!hint`, `!countdown`, `!remaining`, `!players`,
`!collect`, `!release`; quick-command buttons and command history (↑/↓) are
provided. Close with `` ` `` again, Esc, or the Close button.

### On-screen HUDs & feed

- **Event feed** — a scrolling log of multiworld activity (items you receive,
  items your checks send to others, and optionally hints/chat/Death Link), drawn
  in the game's own font. By default it's on, bottom-left, showing just *your
  items* and *items you send to others*; every category and its layout is
  configurable in the F8 panel.
- **Flag progress HUD** — for `door_50/75/100` goals, a `FLAGS x/N` counter
  (top-left) showing how many Flags you have toward the target. Hidden for other
  goals. Toggle in the F8 panel.
- **Death Link HUD** — for Death Link seeds, a `DEATHS n/m` counter (left side)
  showing progress toward your next outgoing death; it slides in on each wipe and
  broadcasts at the threshold. The slide-in animation can be toggled in the panel.

### Quality-of-life

- **Keep the hub portal open (chamber 10)** — on by default. Normally the shortcut
  portal back to the central hub only opens after you beat a chamber's computer
  boss. This keeps the intro-area portal open from the start, so you can warp back
  to the hub (e.g. to reach the completion doors) early. Toggle in the F8 panel.

### Traps (only if the seed enables `traps`)

A received trap fires a short effect:

- **Mulligan** — instantly restarts your current hole (dropped if you're in the
  overworld).
- **Slow-Mo** — game time runs at ~0.35× for 10 seconds.
- **Fast-Forward** — game time runs at ~2.2× for 10 seconds.
- **Transmogrify** — randomizes your overworld ball's shape (cosmetic; only fires
  in the overworld).

Traps never gate progression — they only change the flavour of your filler.

---

## Tracking your game (optional)

There's a [PopTracker](https://github.com/black-sliver/PopTracker) pack in this
repo under `poptracker/`. It gives you a map per chamber (10 → 00) plus one per
episode, showing where every remaining check is and which ones are currently in
logic.

1. Install PopTracker, then copy the `poptracker/` folder (or the released
   `wtg-poptracker-*.zip`) into its `packs/` directory.
2. Load **WHAT THE GOLF? Map Tracker**, pick the **Archipelago** variant, click
   **AP**, and enter the same host / slot / password you gave the mod.

Everything configures itself from your seed — goal, area-access granularity,
boss keys, crown chests and which episodes you enabled. Checks tick off as you
play, and **the map follows you around the overworld**: teleport to a new
chamber and the tracker switches to that tab. (That needs the mod's *Publish
current area* option, on by default in the F8 panel. Without it the tracker
still switches, just only when you send a check.) Turn tab-switching off in the
tracker's settings if you'd rather drive it yourself.

Marker colours are PopTracker's usual ones, with one addition specific to this
game: **yellow** means a check you can physically *walk* to but that Archipelago
doesn't consider in logic — the sub-area walk-in described above. It's never
required and never a softlock. On a `hard_sections` seed it disappears, since
the mod holds those doors shut.

---

## Required software

- [Archipelago](https://github.com/ArchipelagoMW/Archipelago/releases) — the
  generator and server (0.6.7 or newer).
- The `what_the_golf` apworld installed into your Archipelago `worlds/` folder
  (or double-click a packaged `.apworld`).
- **WHAT THE GOLF?** on PC (Steam), plus any DLC episodes you enable.
- [**MelonLoader**](https://melonwiki.xyz/) v0.7.3 installed into the game, plus
  the WHAT THE GOLF? Archipelago mod.

> **Loader note:** this game must use **MelonLoader**, not BepInEx — BepInEx 6's
> Dobby detour hard-crashes WHAT THE GOLF at graphics init.

---

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

---

## Generating a game

1. Create a YAML options file for WHAT THE GOLF? with the options above (Goal,
   Area Access, Boss Keys, Crowns, Episodes, Traps, Death Link, etc.).
2. Generate a seed with your YAML included.
3. Host the resulting game locally or on [archipelago.gg](https://archipelago.gg).

---

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
   becomes reachable — use the **pause-menu teleporter** to hop there. Boss keys,
   chest keys, and episode keys open their content as they arrive.
5. **Win** by satisfying your goal (beat the Final boss / beat all bosses / reach
   your Flag percentage and press the completion-door button).

---

## Tips & gotchas for players

- **Fresh save, always.** On a progressed save the game can re-derive door state
  and fight the mod. Start clean.
- **Items can arrive before you can reach where they go** — that's normal in a
  multiworld. Teleport to whatever is unlocked and keep sending checks.
- **Received an Access key but don't see the area?** Open the pause-menu
  teleporter — a keyed area is listed and unlocked there even if you haven't
  physically walked to it.
- **The mod does nothing until you press F8 and connect.** If it seems inactive,
  check the status line in the F8 panel.
- **DLC required for episodes.** Enabling an episode you don't own will leave it
  unenterable. Only enable episodes you actually have.
