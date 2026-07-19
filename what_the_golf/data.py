"""Central data model for the WHAT THE GOLF? Archipelago world.

Built from REAL game data: `levels.json` is generated from the in-game dump of
the OverworldLevelData ScriptableObject (see tools/build_levels.py and
mod/harvested-levels.md). It contains the 132 real campaign holes grouped into
the 11 real chambers (10 -> 00). This module loads it and exposes the item/
location/region primitives the rest of the world uses; everything downstream is
data-driven from levels.json.

Model (an "Area" is a real chamber; naming kept generic for the framework code):
  * Area  = a chamber (e.g. "Chamber 08" = Platformers/Soccer/Space/Explosion).
  * Level = one hole: opaque game id, human scene name, boss flag, challenge count.
  * Locations: every hole's "<scene> - Clear", plus "<scene> - Crown" for holes
    that have mini-challenges.
  * Items: one Access key per area (except the start area) gate that area; "Flag"
    tokens are counted for the 50/75/100% completion-door goals; filler pads.
  * Goal: campaign (reach the Final boss area) or door_50/75/100 (Flag count).
"""

import json
import os
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


@dataclass(frozen=True)
class Area:
    name: str
    levels: tuple    # tuple[Level, ...]


def _load():
    with open(_LEVELS_PATH, encoding="utf-8") as f:
        w = json.load(f)
    areas = tuple(
        Area(a["name"],
             tuple(Level(l["id"], l["scene"], bool(l["boss"]), int(l["challenges"]))
                   for l in a["levels"]))
        for a in w["areas"]
    )
    return areas, w["start_area"], w["final_boss_scene"]


AREAS, START_AREA, FINAL_BOSS_SCENE = _load()

FLAG_ITEM = "Flag"
FILLER_ITEMS = (
    "Silly Hat", "Golf Ball Skin", "Confetti Burst",
    "Trophy", "Extra Putt", "Rubber Duck",
)


# --- Naming helpers (names must stay unique & stable) ------------------------
def access_item(area_name: str) -> str:
    return f"{area_name} Access"


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


# --- Name lists + ID maps (framework-free single source of truth) ------------
def access_item_names():
    return [access_item(a.name) for a in AREAS if a.name != START_AREA]


def all_item_names():
    return list(access_item_names()) + [FLAG_ITEM] + list(FILLER_ITEMS)


def all_location_names():
    names = []
    for _area, level in iter_holes():
        names.append(clear_loc(level.scene))
        if level.challenges > 0:
            names.append(crown_loc(level.scene))
    return names


item_name_to_id = {name: BASE_ID + i for i, name in enumerate(all_item_names())}
location_name_to_id = {name: LOC_BASE + i for i, name in enumerate(all_location_names())}
