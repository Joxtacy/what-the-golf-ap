"""Build what_the_golf/levels.json (the real campaign world) from the in-game
section dump (mod/wtg_sections.json) + level metadata (mod/wtg_levels.json).

wtg_sections.json is the AUTHORITATIVE campaign structure, read straight from the
game's OverworldLevelData ScriptableObject: 21 ordered sections (sub-areas) with
their exact hole membership, grouped into the real chambers (10 -> 00, counting
down; 10 = intro/start, 00 = finale). We group the sections into chambers and
emit the schema data.py consumes ({final_boss_scene, start_area, areas,
gate_units}), so an "area" is a real chamber.

Two levels of gating are emitted so the apworld can offer either granularity:
  * chambers  (areas)      -- 10 unlockable chambers (09..00; 10 is free).
  * gate_units             -- the finer unit the GAME actually unlocks: each is a
    unique section unlockTriggerId (17 total). Sections that share a trigger open
    as one unit (e.g. Portal+Super Putt = YX3NO; Kitchen+Gravity+FPG = 9DSBG).
Each level also carries its section's `trigger` so downstream can regroup freely.

Run: python tools/build_levels.py [--write]
"""

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SECTIONS = os.path.join(ROOT, "mod", "wtg_sections.json")
LEVELS = os.path.join(ROOT, "mod", "wtg_levels.json")
OUT = os.path.join(ROOT, "what_the_golf", "levels.json")

FINAL_BOSS_SCENE = "Final boss"

# Section code (name suffix) -> display theme, from PlateInfoManager.AreaIDEnum
# (THEME_CHAMBER+SUBAREA). See tools/chamber_ids.py / mod/harvested-levels.md.
SECTION_THEME = {
    "10": "Intro",
    "09A": "Easy 2D", "09B": "Living Room",
    "08A": "Platformers", "08B": "Soccer", "08C": "Space", "08D": "Explosion",
    "07A": "OL", "07B": "Lebowski",
    "06A": "Portal", "06B": "Super Putt",
    "05A": "Kitchen", "05B": "Gravity", "05C": "FPG",
    "04A": "Music", "04B": "Stealth",
    "03A": "Jungle", "03B": "Cars",
    "02": "Water",
    "01": "Western",
    "00": "Finale",
}


def chamber_num(section_name):
    m = re.match(r"(\d+)", section_name.strip())
    return int(m.group(1)) if m else None


def build():
    sections = json.load(open(SECTIONS, encoding="utf-8"))
    meta = {x["scene"]: x for x in json.load(open(LEVELS, encoding="utf-8"))}

    # Group sections into chambers, preserving progression order of first sight.
    # In parallel, group sections into gate_units keyed by their unlockTriggerId
    # (the atomic unit the game actually unlocks -- some sections share a trigger).
    chambers = {}       # num -> {"themes": [...], "levels": [...]}
    order = []
    gate_units = {}     # trigger -> {"name", "chamber", "sections", "scenes"}
    gate_order = []
    for sec in sections:
        code = sec["name"].strip()
        num = chamber_num(code)
        if num is None:
            raise SystemExit(f"section '{code}' has no chamber number")
        if num not in chambers:
            chambers[num] = {"themes": [], "levels": []}
            order.append(num)
        theme = SECTION_THEME.get(code, code)
        if theme not in chambers[num]["themes"]:
            chambers[num]["themes"].append(theme)
        trigger = sec.get("unlockTriggerId", "") or ""
        if trigger:     # empty trigger == chamber 10, the free start (no gate)
            if trigger not in gate_units:
                gate_units[trigger] = {"chamber": num, "themes": [],
                                       "sections": [], "scenes": []}
                gate_order.append(trigger)
            g = gate_units[trigger]
            g["sections"].append(code)
            if theme not in g["themes"]:
                g["themes"].append(theme)
        for scene in sec["levels"]:
            m = meta.get(scene, {})
            level = {
                "id": m.get("id", ""),
                "scene": scene,
                "boss": bool(m.get("boss", False)),
                "challenges": int(m.get("challenges", 0)),
                "subarea": code,
                "theme": theme,
                "trigger": trigger,
            }
            chambers[num]["levels"].append(level)
            if trigger:
                gate_units[trigger]["scenes"].append(scene)

    areas = []
    for num in order:
        c = chambers[num]
        areas.append({
            "name": f"Chamber {num:02d}",
            "chamber": num,
            "themes": c["themes"],
            "levels": c["levels"],
        })

    start_area = areas[0]["name"]   # Chamber 10 (intro) — the free start
    final_scenes = {l["scene"] for a in areas for l in a["levels"]}
    if FINAL_BOSS_SCENE not in final_scenes:
        raise SystemExit(f"final boss scene '{FINAL_BOSS_SCENE}' not found in sections")

    # Finalise gate units: name = "<section code(s)>: <themes>", so the section
    # is easy to identify and fused units (shared trigger) read as e.g.
    # "05A/B/C: Kitchen, Gravity & FPG".
    units = []
    for trig in gate_order:
        g = gate_units[trig]
        units.append({
            "trigger": trig,
            "name": f"{_section_label(g['sections'])}: {_join_themes(g['themes'])}",
            "chamber": g["chamber"],
            "sections": g["sections"],
            "scenes": g["scenes"],
        })

    return {
        "final_boss_scene": FINAL_BOSS_SCENE,
        "start_area": start_area,
        "areas": areas,
        "gate_units": units,
    }


def _join_themes(themes):
    """['Portal', 'Super Putt'] -> 'Portal & Super Putt'; three+ use commas."""
    if len(themes) <= 1:
        return themes[0] if themes else ""
    if len(themes) == 2:
        return f"{themes[0]} & {themes[1]}"
    return ", ".join(themes[:-1]) + f" & {themes[-1]}"


def _section_label(sections):
    """['05A','05B','05C'] -> '05A/B/C'; ['08D'] -> '08D'; ['02'] -> '02'.

    All sections in a gate unit share a chamber (they share a trigger), so we
    show the 2-digit chamber once and join the sub-area letters.
    """
    chamber = sections[0][:2]
    letters = [s[2:] for s in sections if s[2:]]
    return chamber + "/".join(letters) if letters else chamber


def main():
    world = build()
    total = sum(len(a["levels"]) for a in world["areas"])
    crowns = sum(1 for a in world["areas"] for l in a["levels"] if l["challenges"] > 0)
    bosses = sum(1 for a in world["areas"] for l in a["levels"] if l["boss"])
    print(f"chambers: {len(world['areas'])} | holes: {total} | crowns: {crowns} | bosses: {bosses}")
    print(f"start (free): {world['start_area']}\n")
    for a in world["areas"]:
        b = sum(1 for l in a["levels"] if l["boss"])
        themes = ", ".join(a["themes"])
        print(f"  {a['name']}  ({len(a['levels']):2d} holes)  {themes}" + (f"  [+{b} boss]" if b else ""))

    print(f"\ngate units (section granularity): {len(world['gate_units'])}")
    for g in world["gate_units"]:
        print(f"  {g['name']:24} ({len(g['scenes']):2d} holes)  "
              f"ch{g['chamber']:02d} {g['trigger']}  <- {'/'.join(g['sections'])}")

    if "--write" in sys.argv:
        with open(OUT, "w", encoding="utf-8") as f:
            json.dump(world, f, indent=1)
        print(f"\nwrote {OUT}")
    else:
        print("\n(dry run — pass --write to save levels.json)")


if __name__ == "__main__":
    main()
