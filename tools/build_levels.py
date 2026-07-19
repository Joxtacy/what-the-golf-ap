"""Build what_the_golf/levels.json (the real campaign world) from the in-game
level dump (mod/wtg_levels.json).

Filters to real campaign holes, groups them into theme-areas (balanced: big
themes split, tiny themes merged), and writes an areas structure that data.py
consumes. Run: python tools/build_levels.py [--write]
"""

import json
import os
import re
import sys
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "mod", "wtg_levels.json")
OUT = os.path.join(ROOT, "what_the_golf", "levels.json")

MAX_AREA = 16     # split themes larger than this
MIN_AREA = 4      # themes smaller than this get merged into "Assorted"
FINAL_BOSS_SCENE = "Final boss"


def is_campaign(x):
    # Real single-player campaign holes: short opaque id, and either has
    # mini-challenges or is a boss. Exclude 2-player "multiplayer" variants,
    # which a solo player cannot clear (they'd be dead checks).
    if "multiplayer" in x["scene"].lower():
        return False
    return len(x["id"]) <= 6 and (x["challenges"] > 0 or x["boss"])


SPECIAL_CASE = {"fpg": "FPG", "supergolf": "SUPERGOLF", "fps": "FPS"}


def _canon(word):
    w = re.sub(r"\d+$", "", word) or word
    return SPECIAL_CASE.get(w.lower(), w[:1].upper() + w[1:].lower())


def theme_key(scene):
    s = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", scene.strip())  # camelCase -> spaces
    parts = [p for p in re.split(r"[\s_\-]+", s) if p]
    if not parts:
        return "Misc"
    if parts[0] == "2D" and len(parts) > 1:        # sub-divide the huge 2D theme
        return "2D " + _canon(parts[1])
    return _canon(parts[0])


def chunk(items, size):
    for i in range(0, len(items), size):
        yield items[i:i + size]


def build():
    dump = json.load(open(SRC, encoding="utf-8"))
    camp = [x for x in dump if is_campaign(x)]

    groups = defaultdict(list)
    for x in camp:
        groups[theme_key(x["scene"])].append(x)

    areas = {}          # name -> list of levels
    leftovers = []
    for name, lvls in sorted(groups.items(), key=lambda kv: -len(kv[1])):
        if len(lvls) < MIN_AREA:
            leftovers.extend(lvls)
            continue
        if len(lvls) > MAX_AREA:
            for i, part in enumerate(chunk(lvls, MAX_AREA)):
                areas[f"{name} {chr(65 + i)}"] = part   # "2D Rope A/B..."
        else:
            areas[name] = lvls

    for i, part in enumerate(chunk(leftovers, 12), 1):
        areas[f"Assorted {i}"] = part

    # order areas: put the boss areas / final boss sensibly; keep deterministic
    ordered = sorted(areas.items())

    world = {
        "final_boss_scene": FINAL_BOSS_SCENE,
        "start_area": ordered[0][0],
        "areas": [
            {"name": name,
             "levels": [{"id": l["id"], "scene": l["scene"],
                         "boss": l["boss"], "challenges": l["challenges"]}
                        for l in lvls]}
            for name, lvls in ordered
        ],
    }
    return world


def main():
    world = build()
    total = sum(len(a["levels"]) for a in world["areas"])
    crowns = sum(1 for a in world["areas"] for l in a["levels"] if l["challenges"] > 0)
    bosses = sum(1 for a in world["areas"] for l in a["levels"] if l["boss"])
    print(f"areas: {len(world['areas'])} | holes: {total} | crowns: {crowns} | bosses: {bosses}")
    print(f"start area: {world['start_area']}")
    print("\n=== areas ===")
    for a in world["areas"]:
        b = sum(1 for l in a["levels"] if l["boss"])
        print(f"  {len(a['levels']):2d}  {a['name']}" + (f"  (+{b} boss)" if b else ""))

    if "--write" in sys.argv:
        with open(OUT, "w", encoding="utf-8") as f:
            json.dump(world, f, indent=1)
        print(f"\nwrote {OUT}")
    else:
        print("\n(dry run — pass --write to save levels.json)")


if __name__ == "__main__":
    main()
