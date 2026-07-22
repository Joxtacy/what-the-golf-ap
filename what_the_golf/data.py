"""Central data model for the WHAT THE GOLF? Archipelago world.

Built from REAL game data: `levels.json` is generated from the in-game dump of
the OverworldLevelData ScriptableObject (see tools/build_levels.py and
mod/harvested-levels.md). It contains the 133 real campaign holes (132 from the
dump + 1 injected computer-5 boss) grouped into the 11 real chambers (10 -> 00). This module loads it and exposes the item/
location/region primitives the rest of the world uses; everything downstream is
data-driven from levels.json.

Model (an "Area" is a real chamber; naming kept generic for the framework code):
  * Area  = a chamber (e.g. "Chamber 08" = Platformers/Soccer/Space/Explosion).
  * GateUnit = the finer unit the GAME actually unlocks: one section unlockTriggerId
    (17 total). Some sections share a trigger and open together (Portal+Super Putt).
  * Level = one hole: opaque game id, human scene name, boss flag, challenge count.
  * Locations: every hole's "<scene> - Clear", plus "<scene> - Crown" for holes
    that have mini-challenges.
  * Items: one Access key per gate (chamber OR gate-unit, per the area_access
    option); "Flag" tokens are counted for the 50/75/100% completion-door goals;
    filler pads. The item ID table holds BOTH key sets (chamber + section) so IDs
    are stable regardless of option; a seed only creates the subset it needs.
  * Goal: campaign (reach the Final boss area) or door_50/75/100 (Flag count).
"""

import json
import os
import re
from dataclasses import dataclass

# Base offsets for IDs (derived from Steam AppID 785790 to avoid collisions).
BASE_ID = 78579000
LOC_BASE = BASE_ID + 5000

_LEVELS_PATH = os.path.join(os.path.dirname(__file__), "levels.json")


def _read_levels_json():
    """Load levels.json in every context it might run in:
      * as a folder under worlds/ (filesystem open),
      * as a zipimported .apworld (open() can't read inside the zip -> pkgutil),
      * imported standalone by tools/export_ids.py (filesystem open).
    Try the filesystem first (covers folder + standalone), fall back to pkgutil
    (covers the zipimported apworld)."""
    if os.path.exists(_LEVELS_PATH):
        with open(_LEVELS_PATH, encoding="utf-8") as f:
            return json.load(f)
    import pkgutil
    raw = pkgutil.get_data(__name__, "levels.json")
    if raw is None:
        raise FileNotFoundError("levels.json not found (folder or apworld)")
    return json.loads(raw.decode("utf-8"))


# --- Display-name transform --------------------------------------------------
# The game reports opaque, inconsistently-formatted SCENE strings (the internal
# join key the mod matches on). pretty() derives the human display name used for
# AP location names, per the agreed conventions:
#   * keep the "2D"/"FPG" engine prefixes and the original (possibly gap-y) hole
#     numbers; split camelCase into words; Title-case (also de-shouts ALL-CAPS
#     tokens like SUPERGOLF -> Supergolf); keep small joining words lowercase;
#     leave glued lowercase compounds (Livingroom, Manball) and do literal splits
#     (no pun interpretation).
#   * boss holes "2D HoleInOne <N> <desc>" -> "Computer <N> (<Desc>)" -- the
#     canonical in-game Computer numbering, including the finale's Computer 9.
# The raw scene stays the mod's join key; export_ids ships scene->display so the
# mod can resolve a completed scene to its (renamed) "<display> - Clear/Crown".
_KEEP = {"2d": "2D", "3d": "3D", "fpg": "FPG", "tv": "TV", "ol": "OL"}
_SMALL = {"and", "or", "of", "a", "an", "the", "but", "then", "on", "with",
          "in", "is", "to"}
_BOSS_RE = re.compile(r"2D HoleInOne\s+(\d+)\s*(.*)", re.I)


def _split_camel(tok: str) -> str:
    s = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", tok)
    s = re.sub(r"(?<=[A-Z])(?=[A-Z][a-z])", " ", s)
    return re.sub(r"(?<=[A-Za-z])(?=[0-9])", " ", s)


def _word(w: str, first: bool) -> str:
    lw = w.lower()
    if lw in _KEEP:
        return _KEEP[lw]
    if w.isdigit():
        return w
    if not first and lw in _SMALL:
        return lw
    return w[:1].upper() + w[1:].lower()


def pretty(scene: str) -> str:
    """Human display name for a scene (see the conventions above)."""
    m = _BOSS_RE.match(scene)
    if m:
        n = int(m.group(1))
        desc = " ".join(_word(x, i == 0)
                        for i, x in enumerate(_split_camel(m.group(2).strip()).split()))
        return f"Computer {n} ({desc})" if desc else f"Computer {n}"
    return " ".join(_word(t, i == 0) for i, t in enumerate(_split_camel(scene).split()))


@dataclass(frozen=True)
class Level:
    id: str          # opaque LevelData.ID (e.g. "DI3JRA")
    scene: str       # internal scene name (the mod's join key; globally unique)
    boss: bool
    challenges: int   # number of mini-challenges (0, 1 or 2) -> crown if > 0
    trigger: str      # section unlockTriggerId ("" for the free start chamber)
    display: str      # human display name (pretty(scene)); used for location names


@dataclass(frozen=True)
class Area:
    name: str
    levels: tuple    # tuple[Level, ...]


@dataclass(frozen=True)
class GateUnit:
    """One atomic in-game unlock unit (a unique section unlockTriggerId)."""
    name: str        # display name, e.g. "Portal & Super Putt"
    trigger: str     # the game's unlockTriggerId (e.g. "YX3NO")
    chamber: int
    levels: tuple    # tuple[Level, ...]


@dataclass(frozen=True)
class Chest:
    """An overworld crown chest (the "crowns" option)."""
    id: str          # OverworldID.ID (e.g. "CHEST_CARS") -- the mod matches on this
    display: str     # human name (e.g. "Cars", "Gravity Main")
    chamber: int
    trigger: str     # its sub-area's unlockTriggerId (places it in that region)
    gated: bool      # True = behind a crown door (needs a key); False = free
    door: str        # the crown-door OverworldID.ID to hold shut ("" if free)
    boss: int        # 0 = normal; else the computer N whose defeat also gates it


def _load():
    w = _read_levels_json()
    areas = tuple(
        Area(a["name"],
             tuple(Level(l["id"], l["scene"], bool(l["boss"]),
                         int(l["challenges"]), l.get("trigger", ""),
                         pretty(l["scene"]))
                   for l in a["levels"]))
        for a in w["areas"]
    )
    by_scene = {lv.scene: lv for a in areas for lv in a.levels}
    gate_units = tuple(
        GateUnit(g["name"], g["trigger"], int(g["chamber"]),
                 tuple(by_scene[s] for s in g["scenes"]))
        for g in w["gate_units"]
    )
    # Keyable computer boss doors (built from wtg_doors.json by build_levels.py):
    # doors with plate areas AND a real campaign hole. See boss_holes() below.
    boss_doors = tuple(dict(d) for d in w.get("boss_doors", ()))
    # Overworld crown chests (the "crowns" option). See build_levels.py CHESTS.
    chests = tuple(
        Chest(c["id"], pretty(c["display"]), int(c["chamber"]), c.get("trigger", ""),
              bool(c["gated"]), c.get("door") or "", int(c.get("boss", 0)))
        for c in w.get("chests", ())
    )
    return (areas, gate_units, w["start_area"], w["final_boss_scene"],
            boss_doors, chests)


AREAS, GATE_UNITS, START_AREA, FINAL_BOSS_SCENE, BOSS_DOORS, CHESTS = _load()

_BY_SCENE = {lv.scene: lv for a in AREAS for lv in a.levels}

# scene -> display name (the mod resolves a completed scene to its location name
# via this, exported as name_by_scene).
_DISPLAY_BY_SCENE = {lv.scene: lv.display for a in AREAS for lv in a.levels}
# computer number -> the chamber its boss sits in (for the boss-key display name).
_SCENE_CHAMBER = {lv.scene: g.chamber for g in GATE_UNITS for lv in g.levels}
_BOSS_CHAMBER = {int(bd["computer"]): _SCENE_CHAMBER[bd["scene"]]
                 for bd in BOSS_DOORS if bd["scene"] in _SCENE_CHAMBER}

FLAG_ITEM = "Flag"
FILLER_ITEMS = (
    "Silly Hat", "Golf Ball Skin", "Confetti Burst",
    "Trophy", "Extra Putt", "Rubber Duck",
)
# Trap items (the "traps" option). A received trap triggers a disruptive/funny
# effect in the game mod, routed purely by item NAME -- so these strings must
# match the names in the mod's TrapManager EXACTLY. They replace filler slots
# when the option is on and are classified ItemClassification.trap. Add more here
# (and a matching effect in the mod) to grow the set; they append to the ID table
# so existing IDs stay stable.
TRAP_ITEMS = (
    "Mulligan Trap",       # force-restart the current hole (Level.Restart)
    "Slow-Mo Trap",        # briefly slow game time (Chronos clocks)
    "Fast-Forward Trap",   # briefly speed up game time (Chronos clocks)
    "Transmogrify Trap",   # randomize the overworld ball's shape (cosmetic)
)


# --- Naming helpers (names must stay unique & stable) ------------------------
def access_item(gate_name: str) -> str:
    """Access-key item name for a gate (a chamber name OR a gate-unit name)."""
    return f"{gate_name} Access"


def boss_key_item(n: int) -> str:
    """Boss-key item name for computer N (e.g. "Computer 3 Key (Chamber 06)").

    The chamber suffix disambiguates the two counting systems: computer numbers
    climb as chamber codes count down, so "Computer 3" lives in chamber 06.
    """
    ch = _BOSS_CHAMBER.get(n)
    return f"Computer {n} Key (Chamber {ch:02d})" if ch is not None else f"Computer {n} Key"


def chest_loc(display: str) -> str:
    """AP location name for a crown chest (e.g. "Cars Chest")."""
    return f"{display} Chest"


def chest_key_item(display: str) -> str:
    """AP key item name for a crown-gated chest (e.g. "Cars Chest Key")."""
    return f"{display} Chest Key"


def clear_loc(scene: str) -> str:
    return f"{_DISPLAY_BY_SCENE.get(scene, scene)} - Clear"


def crown_loc(scene: str) -> str:
    return f"{_DISPLAY_BY_SCENE.get(scene, scene)} - Crown"


def name_by_scene() -> dict:
    """scene -> display name. Exported to the mod so it can turn a completed
    scene into the (renamed) AP location name '<display> - Clear/Crown'."""
    return dict(_DISPLAY_BY_SCENE)


def iter_holes():
    """Yield (area, level) for every campaign hole, in table order."""
    for area in AREAS:
        for level in area.levels:
            yield area, level


def num_holes() -> int:
    return sum(1 for _ in iter_holes())


def final_boss_area() -> str:
    for area in AREAS:
        for level in area.levels:
            if level.scene == FINAL_BOSS_SCENE:
                return area.name
    return AREAS[-1].name


# --- Boss holes / computer keys ----------------------------------------------
# The campaign's KEYABLE "computer" boss holes, each fought behind a real computer
# door (OverworldMainDoorRobot). The keyable set is sourced from the door topology
# (mod/wtg_doors.json, baked into levels.json's "boss_doors" by build_levels.py):
# a door is keyable only if it BOTH has plate areas (so the mod's
# OverworldMainDoorPlate.SetState lever can hold it shut until the key arrives)
# AND maps to a real campaign hole (so a "<scene> - Clear" location exists to gate).
# The door's bossLevelID equals the boss hole's LevelData.ID (Level.id), and its
# ID_2D_HOLEINONE_N gives the computer number. This EXCLUDES the plateless finale
# computer 9 (chamber -1, a scripted encounter -- can't be SetState-gated). It
# includes computer 5, whose boss "2D HoleInOne 05 basic" is in no section dump
# but is a real reachable plate-lit boss (live-verified 2026-07-20) that
# build_levels.py injects into chamber 5. Result today: computers 1,2,3,4,5,7,8.
# The Final boss is never keyed (no door; gated by the campaign goal).
def boss_holes():
    """Yield (level, computer_number) for each keyable campaign boss hole."""
    for bd in BOSS_DOORS:
        level = _BY_SCENE.get(bd["scene"])
        if level is not None:
            yield level, int(bd["computer"])


BOSS_HOLES = tuple(boss_holes())


def boss_scene_for_computer(n: int):
    """Scene of the keyable computer-N boss hole, or None. Used to gate a chest
    that physically sits behind that boss (Chest.boss)."""
    for level, num in BOSS_HOLES:
        if num == n:
            return level.scene
    return None


def all_boss_scenes():
    """Scenes of every REQUIRED boss for the all_bosses goal: each chamber's
    computer boss plus the Final boss.

    EXCLUDES any other boss hole that shares the Final boss's area -- namely the
    finale's 'HoleInOne 09 3d'. Per wtg_doors.json that door alone has NO plate
    areas and chamber -1 (every real computer is lit by completing its plate
    areas' holes): it's a scripted finale-sequence encounter, not an
    independently-reachable computer, so it can't be beaten standalone. Reaching
    and beating the Final boss already represents the finale. Without this filter
    all_bosses is effectively unbeatable -- you teleport onto the Final boss and
    can never trigger HoleInOne 09. Exported to the mod as boss_scenes."""
    final_area = final_boss_area()
    return tuple(
        lv.scene for area, lv in iter_holes()
        if lv.boss and not (area.name == final_area and lv.scene != FINAL_BOSS_SCENE)
    )


def boss_key_names():
    return [boss_key_item(n) for _lv, n in BOSS_HOLES]


def boss_key_to_level_id():
    """Boss-key item name -> boss hole LevelData.ID (the mod matches a door's
    bossLevelID to this to suppress/open that computer)."""
    return {boss_key_item(n): lv.id for lv, n in BOSS_HOLES}


# --- Crown chests / keys (the "crowns" option) -------------------------------
def chest_key_names():
    """Key item names for the crown-GATED chests only (free chests need no key)."""
    return [chest_key_item(c.display) for c in CHESTS if c.gated]


def chest_location_names():
    """AP location names for every chest (gated + free)."""
    return [chest_loc(c.display) for c in CHESTS]


def chest_region(chest: "Chest", mode: str) -> str:
    """Region a chest's location belongs to -- its sub-area's gate, so the region's
    Access rule gates reaching the chest (a gated chest also needs its key)."""
    if mode == SECTION:
        for g in GATE_UNITS:
            if g.trigger == chest.trigger:
                return g.name
        return START_AREA
    return f"Chamber {chest.chamber:02d}"


def chest_doors_by_item():
    """Gated chest key name -> the crown-door OverworldID.ID the mod holds shut
    (canOpen=false) until the key arrives."""
    return {chest_key_item(c.display): c.door for c in CHESTS if c.gated}


def chest_loc_by_oid():
    """Chest OverworldID.ID -> its AP location name (the mod resolves the just-
    opened chest to this and sends the check)."""
    return {c.id: chest_loc(c.display) for c in CHESTS}


# --- Region layout per granularity option ------------------------------------
# A "gate" is one region: (region_name, access_item_or_None, levels). The start
# region has access None (reachable for free). Both layouts cover all 132 holes.
CHAMBER, SECTION = "chamber", "section"


def gates(mode: str):
    """List of (region_name, access_item|None, levels) for the chosen granularity."""
    if mode == SECTION:
        start = next(a for a in AREAS if a.name == START_AREA)
        out = [(start.name, None, start.levels)]
        out += [(g.name, access_item(g.name), g.levels) for g in GATE_UNITS]
        return out
    # default: chamber granularity
    return [(a.name, None if a.name == START_AREA else access_item(a.name), a.levels)
            for a in AREAS]


def final_boss_gate(mode: str) -> str:
    """Region name holding the Final boss, for the chosen granularity."""
    for name, _access, levels in gates(mode):
        if any(lv.scene == FINAL_BOSS_SCENE for lv in levels):
            return name
    return final_boss_area()


def unlocks_by_item():
    """Map every access-item name -> the game unlockTriggerId(s) it opens.

    The mod uses this to open the right doors regardless of granularity: a
    chamber key opens all its sections' triggers; a gate-unit key opens its one.
    """
    m = {}
    for a in AREAS:
        if a.name == START_AREA:
            continue
        trigs, seen = [], set()
        for lv in a.levels:
            if lv.trigger and lv.trigger not in seen:
                seen.add(lv.trigger)
                trigs.append(lv.trigger)
        m[access_item(a.name)] = trigs
    for g in GATE_UNITS:
        m[access_item(g.name)] = [g.trigger]
    return m


# --- Name lists + ID maps (framework-free single source of truth) ------------
def chamber_access_names():
    """Access keys for the chamber-granularity option (10 keys; 10 is free)."""
    return [access_item(a.name) for a in AREAS if a.name != START_AREA]


def section_access_names():
    """Access keys for the section-granularity option (17 gate-unit keys)."""
    return [access_item(g.name) for g in GATE_UNITS]


def access_item_names():
    """The full universe of access keys (both granularities), stable order.

    item_name_to_id must cover every item any option can produce, so the ID
    table stays fixed; each seed creates only its option's subset.
    """
    names = list(chamber_access_names())
    for n in section_access_names():
        if n not in names:          # de-dupe (none overlap today, but be safe)
            names.append(n)
    return names


def all_item_names():
    # Universe of every item any option can produce (IDs must stay stable): all
    # access keys (both granularities) + all boss keys + crown-chest keys + Flag +
    # filler + traps. A seed only CREATES the subset its options need. TRAP_ITEMS
    # stay LAST so adding/removing traps never shifts the earlier IDs.
    return (list(access_item_names()) + list(boss_key_names())
            + list(chest_key_names()) + [FLAG_ITEM] + list(FILLER_ITEMS)
            + list(TRAP_ITEMS))


def all_location_names():
    names = []
    for _area, level in iter_holes():
        names.append(clear_loc(level.scene))
        if level.challenges > 0:
            names.append(crown_loc(level.scene))
    # Chest locations always in the ID table (stable IDs); Regions.py only CREATES
    # them when the crowns option is on.
    names += chest_location_names()
    return names


_all_items = all_item_names()
_all_locs = all_location_names()
# Names become the AP item/location keys, so a duplicate would silently collapse
# two entries and shift every later ID. Fail loudly if the display transform (or
# a data edit) ever produces a collision.
assert len(_all_items) == len(set(_all_items)), \
    f"duplicate item name(s): {[n for n in _all_items if _all_items.count(n) > 1]}"
assert len(_all_locs) == len(set(_all_locs)), \
    f"duplicate location name(s): {[n for n in _all_locs if _all_locs.count(n) > 1]}"

item_name_to_id = {name: BASE_ID + i for i, name in enumerate(_all_items)}
location_name_to_id = {name: LOC_BASE + i for i, name in enumerate(_all_locs)}
