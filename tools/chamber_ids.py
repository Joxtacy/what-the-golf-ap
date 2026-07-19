"""The game's real chamber/sub-area identity table, decoded from
`PlateInfoManager.AreaIDEnum` in the IL2CPP dump.

Each overworld "computer" door (OverworldMainDoorRobot) gates one chamber; its
plates are that chamber's sub-areas. The enum name encodes THEME_CHAMBER+SUBAREA,
with chambers counting DOWN 10 -> 00 (10 = intro/start, 00 = ending). This module
turns the enum into structured (chamber, subarea, theme) data so the apworld
builder can group the real level membership (from mod/wtg_doors.json) into the
true chambers.

Authoritative source: mod/harvested-levels.md ("CHAMBER STRUCTURE DECODED").
"""

# AreaIDEnum value -> raw enum name (index == the enum's integer value).
AREA_ID_NAMES = [
    "INTRO_10",        # 0
    "EASY2D_09A",      # 1
    "LIVINGROOM_09B",  # 2
    "PLATFORMERS_08A", # 3
    "SOCCER_08B",      # 4
    "SPACE_08C",       # 5
    "EXPLOSION_08D",   # 6
    "OL_07A",          # 7
    "LEBOWSKI_07B",    # 8
    "PORTAL_06A",      # 9
    "SUPERPUTT_06B",   # 10
    "KITCHEN_05A",     # 11
    "GRAVITY_05B",     # 12
    "FPG_05C",         # 13
    "MUSIC_04A",       # 14
    "STEALTH_04B",     # 15
    "JUNGLE_03A",      # 16
    "CARS_03B",        # 17
    "WATER_02",        # 18
    "WETERN_01",       # 19  (sic: "WETERN" = Western)
    "END_00",          # 20
]

# Friendlier theme labels (raw enum prefix -> display name).
THEME_LABELS = {
    "INTRO": "Intro",
    "EASY2D": "Easy 2D",
    "LIVINGROOM": "Living Room",
    "PLATFORMERS": "Platformers",
    "SOCCER": "Soccer",
    "SPACE": "Space",
    "EXPLOSION": "Explosion",
    "OL": "OL",
    "LEBOWSKI": "Lebowski",
    "PORTAL": "Portal",
    "SUPERPUTT": "Super Putt",
    "KITCHEN": "Kitchen",
    "GRAVITY": "Gravity",
    "FPG": "FPG",
    "MUSIC": "Music",
    "STEALTH": "Stealth",
    "JUNGLE": "Jungle",
    "CARS": "Cars",
    "WATER": "Water",
    "WETERN": "Western",
    "END": "End",
}


def parse_area(area_id):
    """area_id (int) or enum name -> dict(name, theme, chamber, subarea).

    chamber is an int 10..0; subarea is 'A'/'B'/... or '' when the chamber has a
    single area. Returns None for unknown ids.
    """
    if isinstance(area_id, int):
        if not (0 <= area_id < len(AREA_ID_NAMES)):
            return None
        name = AREA_ID_NAMES[area_id]
    else:
        name = area_id
        if name not in AREA_ID_NAMES:
            return None

    prefix, _, tail = name.rpartition("_")   # "PLATFORMERS", "08A"
    digits = "".join(c for c in tail if c.isdigit())
    subarea = "".join(c for c in tail if c.isalpha())
    chamber = int(digits) if digits else None
    return {
        "name": name,
        "theme": THEME_LABELS.get(prefix, prefix.title()),
        "chamber": chamber,
        "subarea": subarea,
    }


# chamber int -> ordered list of (area_id_int, parsed) sub-areas
def chambers():
    out = {}
    for i, _ in enumerate(AREA_ID_NAMES):
        p = parse_area(i)
        out.setdefault(p["chamber"], []).append((i, p))
    return dict(sorted(out.items(), reverse=True))   # 10 -> 0


if __name__ == "__main__":
    for ch, subs in chambers().items():
        label = ", ".join(f"{p['theme']}{('/'+p['subarea']) if p['subarea'] else ''}"
                           for _, p in subs)
        print(f"Chamber {ch:02d}: {label}")
