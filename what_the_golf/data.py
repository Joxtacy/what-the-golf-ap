"""Central data model for the WHAT THE GOLF? Archipelago world.

Built from REAL game data: `levels.json` is generated from the in-game dump of
the OverworldLevelData ScriptableObject (see tools/build_levels.py and
mod/harvested-levels.md). It contains the 132 real campaign holes grouped into
the 11 real chambers (10 -> 00). This module loads it and exposes the item/
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


@dataclass(frozen=True)
class Level:
    id: str          # opaque LevelData.ID (e.g. "DI3JRA")
    scene: str       # human-readable scene name (globally unique)
    boss: bool
    challenges: int   # number of mini-challenges (0, 1 or 2) -> crown if > 0
    trigger: str      # section unlockTriggerId ("" for the free start chamber)


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


def _load():
    with open(_LEVELS_PATH, encoding="utf-8") as f:
        w = json.load(f)
    areas = tuple(
        Area(a["name"],
             tuple(Level(l["id"], l["scene"], bool(l["boss"]),
                         int(l["challenges"]), l.get("trigger", ""))
                   for l in a["levels"]))
        for a in w["areas"]
    )
    by_scene = {lv.scene: lv for a in areas for lv in a.levels}
    gate_units = tuple(
        GateUnit(g["name"], g["trigger"], int(g["chamber"]),
                 tuple(by_scene[s] for s in g["scenes"]))
        for g in w["gate_units"]
    )
    return areas, gate_units, w["start_area"], w["final_boss_scene"]


AREAS, GATE_UNITS, START_AREA, FINAL_BOSS_SCENE = _load()

FLAG_ITEM = "Flag"
FILLER_ITEMS = (
    "Silly Hat", "Golf Ball Skin", "Confetti Burst",
    "Trophy", "Extra Putt", "Rubber Duck",
)


# --- Naming helpers (names must stay unique & stable) ------------------------
def access_item(gate_name: str) -> str:
    """Access-key item name for a gate (a chamber name OR a gate-unit name)."""
    return f"{gate_name} Access"


def boss_key_item(n: int) -> str:
    """Boss-key item name for computer N (e.g. "Computer 3 Key")."""
    return f"Computer {n} Key"


def clear_loc(scene: str) -> str:
    return f"{scene} - Clear"


def crown_loc(scene: str) -> str:
    return f"{scene} - Crown"


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
# The campaign's "computer" boss holes, each fought behind a real computer door
# (OverworldMainDoorRobot). The door's bossLevelID equals the boss hole's
# LevelData.ID, and the scene's "HoleInOne N" number equals the door's
# ID_2D_HOLEINONE_N (verified via mod/wtg_doors.json). We exclude the Final boss
# (no door; it's gated by the campaign goal). Result: computers 1,2,3,4,7,8,9.
def boss_holes():
    """Yield (level, computer_number) for each keyable campaign boss hole."""
    for _area, level in iter_holes():
        if not level.boss or level.scene == FINAL_BOSS_SCENE:
            continue
        m = re.search(r"HoleInOne\s+(\d+)", level.scene)
        if m:
            yield level, int(m.group(1))


BOSS_HOLES = tuple(boss_holes())


def boss_key_names():
    return [boss_key_item(n) for _lv, n in BOSS_HOLES]


def boss_key_to_level_id():
    """Boss-key item name -> boss hole LevelData.ID (the mod matches a door's
    bossLevelID to this to suppress/open that computer)."""
    return {boss_key_item(n): lv.id for lv, n in BOSS_HOLES}


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
    # access keys (both granularities) + all boss keys + Flag + filler.
    return (list(access_item_names()) + list(boss_key_names())
            + [FLAG_ITEM] + list(FILLER_ITEMS))


def all_location_names():
    names = []
    for _area, level in iter_holes():
        names.append(clear_loc(level.scene))
        if level.challenges > 0:
            names.append(crown_loc(level.scene))
    return names


item_name_to_id = {name: BASE_ID + i for i, name in enumerate(all_item_names())}
location_name_to_id = {name: LOC_BASE + i for i, name in enumerate(all_location_names())}
