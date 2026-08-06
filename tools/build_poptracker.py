"""Generate the PopTracker pack in poptracker/ from the apworld's own data.

Framework-free, stdlib-only -- imports what_the_golf/data.py the same way
tools/export_ids.py does, so it runs without an Archipelago checkout:

    python tools/build_poptracker.py            # build poptracker/
    python tools/build_poptracker.py --check    # CI: fail if poptracker/ is stale
    python tools/build_poptracker.py --zip dist/wtg-poptracker-v0.1.0.zip

WHY GENERATED: the pack's 379 location names must match the apworld's byte for
byte. data.py is the single source of truth for those names, so the pack is
derived from it and a rename can never silently drift -- validate() re-checks
every emitted name against data.location_name_to_id and fails the build.

    poptracker/            <- OUTPUT, committed, never hand-edited
    tools/poptracker_src/  <- hand-maintained inputs (manifest, lua, settings)

TWO PACK-FORMAT CONSTRAINTS SHAPE THE OUTPUT (see doc/PACKS.md):
  * A rule containing ':' is parsed as `code:count`. EVERY WTG location name has
    a colon ("08C: Space Golf 1"), so pack-internal names are sanitised and the
    pack emits zero "@Location/Section" access rules -- the two boss-gated chests
    inline their boss rule as a cross-product instead.
  * There is no NOT operator, so each boolean option is a 2-stage `progressive`
    item exposing BOTH polarities (opt_boss_keys_off / opt_boss_keys_on).
"""

import argparse
import importlib.util
import json
import os
import shutil
import sys
import tempfile
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wtgpng import Canvas, hex_rgb, mix, text_width  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA_PY = os.path.join(ROOT, "what_the_golf", "data.py")
LEVELS_JSON = os.path.join(ROOT, "what_the_golf", "levels.json")
IDS_JSON = os.path.join(ROOT, "mod", "ids.json")
# In-game dumps that carry world positions. Absent/partial is fine -- anything
# without a position is grid-placed instead (see Placer).
GOALS_JSON = os.path.join(ROOT, "mod", "wtg_goals.json")
CHESTS_JSON = os.path.join(ROOT, "mod", "wtg_chests.json")
DOORS_JSON = os.path.join(ROOT, "mod", "wtg_doors.json")
SRC = os.path.join(ROOT, "tools", "poptracker_src")
OUT = os.path.join(ROOT, "poptracker")
# Real overworld renders captured in-game by mod/src/Mapping/OverworldSnapshot.cs,
# each with the exact camera rect it covered. Those rects ARE the map projections,
# so markers land pixel-exact on the art. Absent -> schematic maps get drawn.
SNAPSHOTS = os.path.join(SRC, "images", "maps", "snapshots.json")
# Variant whose map backgrounds are the generated diagrams instead of the real
# renders. Must match a key in manifest.json.in's "variants".
SCHEMATIC_VARIANT = "ap_schematic"

# Auto-update ledger. PopTracker fetches versions_url (https only), takes the TOP
# entry as latest, and verifies the download against its sha256.
REPO_URL = "https://github.com/Joxtacy/what-the-golf-ap"
VERSIONS_JSON = os.path.join(ROOT, "poptracker-versions.json")
CHANGELOG_DIR = os.path.join(SRC, "changelog")


def zip_name(version):
    return f"what-the-golf-poptracker-v{version}.zip"


def record_version(version, sha256):
    """Upsert this version at the top of poptracker-versions.json.

    Idempotent: re-running for the same version replaces its entry rather than
    appending a duplicate, so a re-cut release doesn't corrupt the ledger.
    """
    doc = _load_json(VERSIONS_JSON, {"versions": []})
    versions = [v for v in doc.get("versions", [])
                if v.get("package_version") != version]
    changelog = []
    path = os.path.join(CHANGELOG_DIR, f"{version}.txt")
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            changelog = [ln.strip() for ln in f if ln.strip()]
    else:
        print(f"  note: no {os.path.relpath(path, ROOT)}; changelog left empty")
    versions.insert(0, {
        "package_version": version,
        "download_url": f"{REPO_URL}/releases/download/v{version}/{zip_name(version)}",
        "sha256": sha256,
        "changelog": changelog,
    })
    with open(VERSIONS_JSON, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"versions": versions}, f, indent=2)
        f.write("\n")
    print(f"recorded v{version} in {os.path.relpath(VERSIONS_JSON, ROOT)} "
          f"({len(versions)} version(s), {len(changelog)} changelog line(s))")

# --- palette -----------------------------------------------------------------
BG = hex_rgb("#12141a")
PANEL = hex_rgb("#1b1f29")
INK = hex_rgb("#e8ecf5")
DIM = hex_rgb("#8d97ad")
EDGE = hex_rgb("#394154")

CAT_COLORS = {
    "chamber": hex_rgb("#3f6fb5"),
    "gate": hex_rgb("#2f8f86"),
    "boss": hex_rgb("#b5453f"),
    "chest": hex_rgb("#b58a2f"),
    "episode": hex_rgb("#7a4fb5"),
    "flag": hex_rgb("#3f9a55"),
    "opt": hex_rgb("#525a6e"),
    "goal": hex_rgb("#b5762f"),
}

# 21 sub-area tints, keyed by code so a re-render is byte-identical.
SUBAREA_TINTS = [
    "#4c6fb0", "#b06f4c", "#4cb06f", "#b04c6f", "#6f4cb0", "#b0a34c",
    "#4ca3b0", "#a34cb0", "#7fb04c", "#b04c8f", "#4cb0a3", "#b08f4c",
    "#5f7fc0", "#c07f5f", "#5fc07f", "#c05f7f", "#7f5fc0", "#c0b35f",
    "#5fb3c0", "#b35fc0", "#8fc05f",
]


# --- inputs ------------------------------------------------------------------
def load_data():
    spec = importlib.util.spec_from_file_location("wtg_data", DATA_PY)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_levels():
    with open(LEVELS_JSON, encoding="utf-8") as f:
        return json.load(f)


def load_ids():
    with open(IDS_JSON, encoding="utf-8") as f:
        return json.load(f)


def _load_json(path, default):
    if not os.path.exists(path):
        return default
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return default


def load_snapshots():
    """map name -> {w, h, minx, miny, maxx, maxy} for maps rendered from the game."""
    return {s["map"]: s for s in _load_json(SNAPSHOTS, [])}


def load_world_positions(world):
    """entity key -> (x, y) in the game's overworld coordinate space.

    Three sources, because no single dumper sees everything:
      * wtg_goals.json  -- the hole "flag" objects (OverworldGoal), campaign-tagged
      * wtg_chests.json -- the 24 crown chests
      * wtg_doors.json  -- the computer doors. Boss holes are NOT OverworldGoals,
                           so this is their only coordinate source.

    Only positions captured from a LIVE scene instance are trusted:
    FindObjectsOfTypeAll also returns prefab assets whose transforms hold
    authoring-local coordinates, which would land markers in nonsense places.
    Anything missing is simply absent here and gets grid-placed.
    """
    pos = {}
    for g in _load_json(GOALS_JSON, []):
        p, scene = g.get("pos"), g.get("scene")
        # "Hub" is not a real campaign -- it's what CampaignInfo reports at the
        # main menu, and records captured there are a documented duplicate
        # artifact (STATUS.md). Dropping them also stops a Hub-tagged copy of an
        # episode scene from overwriting the real one, since these are keyed by
        # scene rather than by campaign::scene.
        if g.get("campaign") == "Hub":
            continue
        if p and scene and g.get("in_scene"):
            pos[f"scene:{scene}"] = (float(p[0]), float(p[1]))
    for key, c in _load_json(CHESTS_JSON, {}).get("chests", {}).items():
        p = c.get("pos")
        cid = c.get("id") or key.split("::")[-1]
        if p:
            pos[f"chest:{cid}"] = (float(p[0]), float(p[1]))
    # A door record identifies its boss by LevelData.ID, so join through
    # levels.json's boss_doors to get the scene the pack keys markers by.
    scene_by_boss_id = {bd["boss_level_id"]: bd["scene"]
                        for bd in world.get("boss_doors", ())}
    for d in _load_json(DOORS_JSON, {}).get("doors", {}).values():
        # Main only. An episode can contain a door reusing a campaign boss's
        # LevelData.ID -- "Alive::ID_2D_HOLEINONE_1" carries Computer 1's id
        # (349CM9) at a position 18 world units away, and without this filter it
        # overwrote the real Computer 1 and threw its marker into the next
        # chamber. No episode hole is a boss, so Main is the only source here.
        if d.get("campaign", "Main") != "Main":
            continue
        p = d.get("pos")
        scene = scene_by_boss_id.get(d.get("boss_level_id"))
        if p and scene and d.get("in_scene"):
            pos[f"scene:{scene}"] = (float(p[0]), float(p[1]))
    return pos


# --- name hygiene ------------------------------------------------------------
_BAD = ":,|@"


def sane(s):
    """Pack-internal name: strip the characters PopTracker's rule parser treats
    as syntax. ':' would turn a rule into a `code:count` check (silently!)."""
    out = s.replace("/", "-")
    for ch in _BAD:
        out = out.replace(ch, "")
    return " ".join(out.split())


def short_label(s, n=4):
    """A <=n char icon label that stays readable at 24-32px.

    Truncation alone produced mush ("Gravity Main" -> "GRAVIT", "Goal: Campaign"
    -> "CAMPAI"), so keep a trailing digit when there is one -- that is the only
    thing telling Desert 1 from Desert 2, or Soccer 1 from Soccer 2.
    """
    alnum = [c for c in s.upper() if c.isalnum()]
    if not alnum:
        return "?"
    if alnum[-1].isdigit() and len(alnum) > n:
        letters = [c for c in alnum if not c.isdigit()][:n - 1]
        return "".join(letters) + alnum[-1]
    return "".join(alnum[:n])


def strip_prefix(display):
    """'08C: Space Golf 1' -> 'Space Golf 1'. Every location name is prefixed
    with its sub-area / chamber / episode, and the parent node already says
    which, so the child carries only the hole name."""
    head, sep, tail = display.partition(": ")
    return tail if sep else display


# --- rule helpers ------------------------------------------------------------
MODE_CODES = ("opt_area_section", "opt_area_chamber")
BOSSKEY_CODES = ("opt_boss_keys_on", "opt_boss_keys_off")


def _dedupe(seq):
    seen, out = set(), []
    for x in seq:
        if x not in seen:
            seen.add(x)
            out.append(x)
    return out


def _contradictory(group):
    g = set(group)
    return (len(g & set(MODE_CODES)) > 1) or (len(g & set(BOSSKEY_CODES)) > 1)


def and_groups(a, b):
    """AND two OR-of-AND rule sets: (a1|a2) AND (b1|b2) -> a1b1|a1b2|a2b1|a2b2.

    Groups that would require both area-access modes at once (or both boss-key
    polarities) are unsatisfiable and dropped -- that is what keeps the
    cross-product for a boss-gated chest down to four readable groups.
    """
    out = []
    for ga in a:
        for gb in b:
            merged = _dedupe(list(ga) + list(gb))
            if _contradictory(merged):
                continue
            out.append(tuple(merged))
    return [list(g) for g in _dedupe(out)]


# --- model -------------------------------------------------------------------
class Model:
    def __init__(self, data, world, ids):
        self.data = data
        self.world = world
        self.ids = ids

        self.loc_ids = data.location_name_to_id
        self.item_ids = data.item_name_to_id

        # sub-area code -> theme, chamber (from levels.json, which data.py drops)
        self.subarea_theme, self.subarea_chamber = {}, {}
        self.subarea_order = []
        self.chamber_subareas = {}
        for area in world["areas"]:
            ch = int(area["chamber"])
            self.chamber_subareas.setdefault(ch, [])
            for lv in area["levels"]:
                sa = lv.get("subarea") or ""
                if not sa:
                    continue
                if sa not in self.subarea_theme:
                    self.subarea_theme[sa] = lv.get("theme") or sa
                    self.subarea_chamber[sa] = ch
                    self.subarea_order.append(sa)
                    self.chamber_subareas[ch].append(sa)

        # item name -> pack code, and sub-area -> its gate code
        self.code_by_item = {}
        self.gate_code_by_subarea = {}
        self._build_codes()

        self.boss_by_scene = {lv.scene: n for lv, n in data.BOSS_HOLES}

    # -- codes ---------------------------------------------------------------
    def _build_codes(self):
        d = self.data
        for area in d.AREAS:
            if area.name == d.START_AREA:
                continue
            ch = area.name.split()[-1]                     # "Chamber 08" -> "08"
            self.code_by_item[d.access_item(area.name)] = f"ch_{ch}"
        for g in d.GATE_UNITS:
            spec = g.name.partition(":")[0]                # "06A/B: ..." -> "06A/B"
            code = "gate_" + spec.replace("/", "")
            self.code_by_item[d.access_item(g.name)] = code
            # every sub-area this gate unit covers, via its holes
            for lv in g.levels:
                sa = self.subarea_of_scene(lv.scene)
                if sa:
                    self.gate_code_by_subarea[sa] = code
        for _lv, n in d.BOSS_HOLES:
            self.code_by_item[d.boss_key_item(n)] = f"bosskey_{n}"
        for c in d.CHESTS:
            if c.gated:
                short = c.id[6:] if c.id.startswith("CHEST_") else c.id
                self.code_by_item[d.chest_key_item(c.display)] = f"chestkey_{short}"
        for ep in d.EPISODES:
            self.code_by_item[d.episode_access_item(ep.name)] = \
                f"epkey_{ep.campaign.lower()}"

    def subarea_of_scene(self, scene):
        for area in self.world["areas"]:
            for lv in area["levels"]:
                if lv["scene"] == scene:
                    return lv.get("subarea") or ""
        return ""

    def chamber_code(self, chamber):
        return f"ch_{int(chamber):02d}"

    def episode_opt(self, ep):
        return f"opt_ep_{ep.campaign.lower()}"

    def parent_name(self, subarea):
        return sane(f"{subarea} {self.subarea_theme.get(subarea, subarea)}")

    # -- rules ---------------------------------------------------------------
    def region_groups(self, subarea, walkable=True):
        """The plain "can I get into this sub-area" rule (Rules.py class 1)."""
        gate = self.gate_code_by_subarea.get(subarea)
        if gate is None:                                   # chamber 10: free start
            return []
        ch = self.chamber_code(self.subarea_chamber[subarea])
        groups = [["opt_area_section", gate], ["opt_area_chamber", ch]]
        if walkable:
            # Sub-areas inside one chamber share an open overworld room, so with
            # section access and hard_sections OFF you can WALK into a locked
            # sibling. That is out of logic but never a softlock (Options.py), so
            # surface it as a sequence break rather than as normal access.
            groups.append([f"^$walkable|{int(self.subarea_chamber[subarea]):02d}"])
        return groups

    def boss_groups(self, computer_n, subarea):
        """Rules.py class 2: boss keys on -> its own key; boss keys off in section
        mode -> EVERY gate-unit key of the boss's chamber (the computer door only
        lights once the whole chamber is natively complete)."""
        ch_num = self.subarea_chamber[subarea]
        ch = self.chamber_code(ch_num)
        gate = self.gate_code_by_subarea.get(subarea)
        key = f"bosskey_{computer_n}"
        chamber_gates = _dedupe([
            self.gate_code_by_subarea[sa]
            for sa in self.chamber_subareas.get(ch_num, [])
            if sa in self.gate_code_by_subarea
        ])
        groups = [
            ["opt_area_chamber", "opt_boss_keys_off", ch],
            ["opt_area_chamber", "opt_boss_keys_on", ch, key],
        ]
        if gate:
            groups.append(["opt_area_section", "opt_boss_keys_on", gate, key])
            groups.append(["opt_area_section", "opt_boss_keys_off"] + chamber_gates)
        return groups


# --- emitters ----------------------------------------------------------------
class Emitter:
    def __init__(self, model, out_dir):
        self.m = model
        self.out = out_dir
        self.location_mapping = {}     # AP location id -> "@Parent/Child/Section"
        self.item_mapping = {}         # AP item id -> (code, kind)
        self.section_paths = set()
        self.declared_codes = set()
        self.rule_codes = set()
        self.emitted_locs = set()
        self.emitted_items = set()
        self.icons = []                # (relpath, label, category)
        self.maps = []                 # map descriptors for maps.json + rendering

    # -- small helpers -------------------------------------------------------
    def _w(self, rel, obj):
        path = os.path.join(self.out, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(obj, f, indent=2, ensure_ascii=False)
            f.write("\n")

    def _wt(self, rel, text):
        path = os.path.join(self.out, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)

    def _note_rules(self, groups):
        for g in groups:
            for code in g:
                if not code.startswith("^$"):
                    self.rule_codes.add(code)

    def _icon(self, code, label, cat):
        rel = f"images/items/{code}.png"
        self.icons.append((rel, label, cat))
        return rel

    # -- items ---------------------------------------------------------------
    def items(self):
        d, m = self.m.data, self.m
        keys = []

        def add(name, code, label, cat, kind="toggle"):
            keys.append({
                "name": name, "type": "toggle",
                "img": self._icon(code, label, cat),
                "disabled_img_mods": "@disabled",
                "codes": code,
            })
            self.declared_codes.add(code)
            self.item_mapping[d.item_name_to_id[name]] = (code, kind)
            self.emitted_items.add(name)

        for area in d.AREAS:
            if area.name == d.START_AREA:
                continue
            name = d.access_item(area.name)
            add(name, m.code_by_item[name], area.name.split()[-1], "chamber")
        for g in d.GATE_UNITS:
            name = d.access_item(g.name)
            code = m.code_by_item[name]
            add(name, code, code[5:], "gate")
        for _lv, n in d.BOSS_HOLES:
            name = d.boss_key_item(n)
            add(name, m.code_by_item[name], f"C{n}", "boss")
        for c in d.CHESTS:
            if not c.gated:
                continue
            name = d.chest_key_item(c.display)
            add(name, m.code_by_item[name], short_label(c.display), "chest")
        for ep in d.EPISODES:
            name = d.episode_access_item(ep.name)
            add(name, m.code_by_item[name], short_label(ep.name), "episode")

        self._w("items/keys.json", keys)

        # The 47 Flag names all collapse into ONE counter (see wtg_data.lua);
        # max is the whole possible pool, narrowed to flag_goal on connect.
        max_flags = d.num_holes() + d.episode_hole_count()
        self.declared_codes.add("flag")
        for fname in d.FLAG_ITEMS:
            self.item_mapping[d.item_name_to_id[fname]] = ("flag", "flag")
            self.emitted_items.add(fname)
        # Filler and traps are not tracked; record them so validation sees a
        # total mapping over the item table.
        for fname in list(d.FILLER_ITEMS) + list(d.TRAP_ITEMS):
            self.emitted_items.add(fname)
        self._w("items/progress.json", [{
            "name": "Flags", "type": "consumable",
            "img": self._icon("flag", "FLG", "flag"),
            "codes": "flag",
            "min_quantity": 0, "max_quantity": max_flags,
            "increment": 1, "decrement": 1,
            "overlay_align": "right", "overlay_font_size": 14,
            "overlay_background": "#80000000",
        }])

        self._w("items/settings.json", self._settings_items())

    def _settings_items(self):
        d, m = self.m.data, self.m

        def stage(code_shared, code_stage, label, cat, short):
            self.declared_codes.update({code_shared, code_stage})
            return {"name": label, "codes": f"{code_shared},{code_stage}",
                    "inherit_codes": False,
                    "img": self._icon(code_stage, short, cat)}

        out = [
            {"name": "Area Access", "type": "progressive", "allow_disabled": False,
             "loop": True, "initial_stage_idx": 0, "stages": [
                 stage("opt_area", "opt_area_section", "Area Access: Section",
                       "opt", "SECT"),
                 stage("opt_area", "opt_area_chamber", "Area Access: Chamber",
                       "opt", "CHAM")]},
            {"name": "Boss Keys", "type": "progressive", "allow_disabled": False,
             "loop": True, "stages": [
                 stage("opt_boss_keys", "opt_boss_keys_off", "Boss Keys: Off",
                       "opt", "BK-"),
                 stage("opt_boss_keys", "opt_boss_keys_on", "Boss Keys: On",
                       "opt", "BK+")]},
            # Stage order IS Options.Goal's 0-4, so onClear can assign it directly.
            {"name": "Goal", "type": "progressive", "allow_disabled": False,
             "loop": True, "stages": [
                 stage("opt_goal", "goal_campaign", "Goal: Campaign", "goal", "CAMP"),
                 stage("opt_goal", "goal_door50", "Goal: 50% Door", "goal", "D50"),
                 stage("opt_goal", "goal_door75", "Goal: 75% Door", "goal", "D75"),
                 stage("opt_goal", "goal_door100", "Goal: 100% Door", "goal", "D100"),
                 stage("opt_goal", "goal_all_bosses", "Goal: All Bosses",
                       "goal", "BOSS")]},
        ]
        for code, label, short in (
                ("opt_crowns", "Crown Chests", "CHST"),
                ("opt_hard", "Hard Sub-area Locks", "HARD"),
                ("opt_walk", "Show walk-in access (out of logic)", "WALK"),
                ("autotab_off", "Auto-switch map tabs: OFF", "TAB-")):
            self.declared_codes.add(code)
            entry = {"name": label, "type": "toggle", "codes": code,
                     "img": self._icon(code, short, "opt"),
                     "disabled_img_mods": "@disabled"}
            if code == "opt_walk":
                entry["initial_active_state"] = True
            out.append(entry)
        for ep in self.m.data.EPISODES:
            code = self.m.episode_opt(ep)
            self.declared_codes.add(code)
            out.append({"name": f"Episode: {ep.name}", "type": "toggle",
                        "codes": code, "disabled_img_mods": "@disabled",
                        "img": self._icon(code, short_label(ep.name), "episode")})
        return out

    # -- locations -----------------------------------------------------------
    def locations(self, placer):
        d, m = self.m.data, self.m
        parents = {}          # subarea -> node
        order = []

        def parent_for(sa):
            if sa not in parents:
                node = {"name": m.parent_name(sa), "children": []}
                parents[sa] = node
                order.append(sa)
            return parents[sa]

        for area, lv in d.iter_holes():
            sa = m.subarea_of_scene(lv.scene)
            if not sa:
                continue
            node = parent_for(sa)
            computer = m.boss_by_scene.get(lv.scene)
            if computer is not None:
                groups = m.boss_groups(computer, sa)
                kind = "boss"
            else:
                groups = m.region_groups(sa)
                kind = "boss" if lv.boss else "hole"
            child = {"name": sane(strip_prefix(lv.display))}
            pts = placer.points(f"scene:{lv.scene}")
            if pts:
                child["map_locations"] = pts
            if groups:
                child["access_rules"] = groups
                self._note_rules(groups)
            child["sections"] = [self._section("Clear", kind)]
            self._map_loc(d.clear_loc(lv.scene), node["name"], child["name"], "Clear")
            if lv.challenges > 0:
                # NOTE: a hole's Crown is NOT gated by the crowns option -- that
                # option only adds the 24 overworld chests (Regions.py).
                child["sections"].append(self._section("Crown", "crown"))
                self._map_loc(d.crown_loc(lv.scene), node["name"], child["name"], "Crown")
            node["children"].append(child)

        # Chests go into the SAME sub-area parents as the holes. PopTracker keys
        # top-level locations by NAME globally, so emitting a second node called
        # "09A Easy 2D" gets it renamed to "09A Easy 2D[1]" and every
        # "@09A Easy 2D/<chest>/Chest" path then resolves to the holes node --
        # silently, so chest checks would just never register.
        for c in d.CHESTS:
            self._chest_child(parent_for(c.subarea), c, placer)

        self._w("locations/main.json", [parents[sa] for sa in order])
        self._episodes(placer)

    def _section(self, name, kind):
        return {"name": name,
                "chest_unopened_img": f"images/sections/{kind}.png",
                "chest_opened_img": f"images/sections/{kind}_opened.png"}

    def _map_loc(self, ap_name, parent, child, section):
        path = f"@{parent}/{child}/{section}"
        self.section_paths.add(path)
        self.location_mapping[self.m.loc_ids[ap_name]] = path
        self.emitted_locs.add(ap_name)

    def _chest_child(self, node, c, placer):
        d, m = self.m.data, self.m
        sa = c.subarea or ""
        groups = m.region_groups(sa, walkable=False) if sa else []
        if c.gated:
            key = m.code_by_item[d.chest_key_item(c.display)]
            groups = [g + [key] for g in groups] or [[key]]
        if c.boss:
            # A free-but-boss-gated chest (Lebowski Secret / Sawable) sits past a
            # computer, so fold the boss's whole rule in as a cross-product --
            # Rules.py uses can_reach_location, which would need an "@" path here,
            # and "@" paths would carry a ':' into the rule parser.
            scene = d.boss_scene_for_computer(c.boss)
            if scene is not None:
                bgroups = m.boss_groups(c.boss, m.subarea_of_scene(scene))
                groups = and_groups(groups, bgroups) if groups else bgroups
        child = {"name": sane(f"{c.display} Chest")}
        pts = placer.points(f"chest:{c.id}")
        if pts:
            child["map_locations"] = pts
        if groups:
            child["access_rules"] = groups
            self._note_rules(groups)
        child["visibility_rules"] = [["opt_crowns"]]
        self.rule_codes.add("opt_crowns")
        child["sections"] = [self._section("Chest", "chest")]
        self._map_loc(d.chest_loc(c), node["name"], child["name"], "Chest")
        node["children"].append(child)

    def _episodes(self, placer):
        d, m = self.m.data, self.m
        out = []
        for ep in d.EPISODES:
            key = m.code_by_item[d.episode_access_item(ep.name)]
            opt = m.episode_opt(ep)
            self.rule_codes.update({key, opt})
            node = {"name": sane(f"{ep.name} Episode"), "children": []}
            for lv in ep.levels:
                child = {"name": sane(strip_prefix(lv.display))}
                pts = placer.points(f"scene:{lv.scene}")
                if pts:
                    child["map_locations"] = pts
                child["access_rules"] = [[key]]
                child["visibility_rules"] = [[opt]]
                child["sections"] = [self._section("Clear", "hole")]
                self._map_loc(d.clear_loc(lv.scene), node["name"], child["name"], "Clear")
                if lv.challenges > 0:
                    child["sections"].append(self._section("Crown", "crown"))
                    self._map_loc(d.crown_loc(lv.scene), node["name"],
                                  child["name"], "Crown")
                node["children"].append(child)
            out.append(node)
        self._w("locations/episodes.json", out)

    # -- maps ----------------------------------------------------------------
    def maps_json(self, placer):
        self._w("maps/maps.json", [
            {"name": mp["name"], "img": f"images/maps/{mp['name']}.png",
             "location_size": 22, "location_border_thickness": 2,
             "location_shape": "rect"}
            for mp in placer.maps
        ])

    # -- layouts -------------------------------------------------------------
    def layouts(self, placer):
        d, m = self.m.data, self.m
        gate_rows, chamber_rows = [], []
        for ch in sorted(m.chamber_subareas, reverse=True):
            row = _dedupe([m.gate_code_by_subarea[sa]
                           for sa in m.chamber_subareas[ch]
                           if sa in m.gate_code_by_subarea])
            if row:
                gate_rows.append(row)
        chamber_codes = [m.code_by_item[d.access_item(a.name)]
                         for a in d.AREAS if a.name != d.START_AREA]
        for i in range(0, len(chamber_codes), 5):
            chamber_rows.append(chamber_codes[i:i + 5])
        boss_codes = [f"bosskey_{n}" for _lv, n in d.BOSS_HOLES]
        chest_codes = [m.code_by_item[d.chest_key_item(c.display)]
                       for c in d.CHESTS if c.gated]
        ep_codes = [m.code_by_item[d.episode_access_item(e.name)] for e in d.EPISODES]
        ep_opts = [m.episode_opt(e) for e in d.EPISODES]

        self._w("layouts/items.json", {"item_panel": {
            "type": "array", "orientation": "vertical", "margin": "4,4,4,4",
            "content": [
                {"type": "itemgrid", "item_size": "48,48",
                 "rows": [["opt_goal", "flag"]]},
                {"type": "tabbed", "tabs": [
                    {"title": "Section Keys",
                     "content": {"type": "itemgrid", "rows": gate_rows}},
                    {"title": "Chamber Keys",
                     "content": {"type": "itemgrid", "rows": chamber_rows}}]},
                {"type": "group", "header": "Computer Keys",
                 "content": {"type": "itemgrid",
                             "rows": [boss_codes[:4], boss_codes[4:]]}},
                # 32px, not 24: the icons are 32x32 and downscaling turned the
                # labels to mush in the real UI.
                {"type": "group", "header": "Chest Keys",
                 "content": {"type": "itemgrid", "item_size": "32,32",
                             "rows": [chest_codes[i:i + 6]
                                      for i in range(0, len(chest_codes), 6)]}},
                {"type": "group", "header": "Episodes",
                 "content": {"type": "itemgrid", "rows": [ep_codes]}},
                {"type": "recentpins", "style": "wrapped", "compact": True},
            ]}})

        self._w("layouts/maps.json", {"maps_tabbed": {
            "type": "tabbed",
            "tabs": [{"title": mp["title"],
                      "content": {"type": "map", "maps": [mp["name"]]}}
                     for mp in placer.maps]}})

        self._w("layouts/tracker.json", {"tracker_default": {
            "type": "dock", "content": [
                {"type": "layout", "key": "item_panel",
                 "dock": "left", "max_width": 340},
                {"type": "layout", "key": "maps_tabbed"}]}})

        self._w("layouts/broadcast.json", {"tracker_broadcast": {
            "type": "array", "orientation": "vertical", "margin": "2,2,2,2",
            "content": [
                {"type": "itemgrid", "item_size": "32,32",
                 "rows": [["opt_goal", "flag"]]},
                {"type": "itemgrid", "item_size": "24,24",
                 "rows": gate_rows + chamber_rows + [boss_codes, ep_codes]}]}})

        self._w("layouts/settings.json", {"settings_popup": {
            "type": "array", "orientation": "vertical", "margin": "6,6,6,6",
            "content": [
                {"type": "text",
                 "text": "Set automatically when you connect to Archipelago."},
                {"type": "text",
                 "text": "Change by hand only for offline / manual tracking."},
                {"type": "itemgrid", "item_size": "40,40",
                 "rows": [["opt_area", "opt_goal", "opt_boss_keys", "opt_crowns"]]},
                {"type": "group", "header": "Episodes",
                 "content": {"type": "itemgrid", "rows": [ep_opts]}},
                {"type": "group", "header": "Display",
                 "content": {"type": "itemgrid",
                             "rows": [["opt_hard", "opt_walk", "autotab_off"]]}},
            ]}})

    # -- lua + manifest ------------------------------------------------------
    def scripts(self, placer):
        d, m = self.m.data, self.m
        L = []
        L.append("-- GENERATED by tools/build_poptracker.py -- DO NOT EDIT")
        L.append("WTG = {}\n")
        L.append("-- chamber -> every gate-unit key in it (the boss_keys-off rule)")
        L.append("WTG.CHAMBER_GATES = {")
        for ch in sorted(m.chamber_subareas, reverse=True):
            codes = _dedupe([m.gate_code_by_subarea[sa]
                             for sa in m.chamber_subareas[ch]
                             if sa in m.gate_code_by_subarea])
            if codes:
                inner = ", ".join(f'"{c}"' for c in codes)
                L.append(f'  ["{ch:02d}"] = {{ {inner} }},')
        L.append("}\n")
        L.append("WTG.EPISODES = {")
        for ep in d.EPISODES:
            L.append(f'  {{ name = "{ep.name}", opt = "{m.episode_opt(ep)}", '
                     f'key = "{m.code_by_item[d.episode_access_item(ep.name)]}", '
                     f'tab = "{ep.name}" }},')
        L.append("}\n")
        L.append("-- every AP item id that is a Flag (data.FLAG_ITEMS) -> one counter")
        L.append("WTG.FLAG_IDS = {")
        for fname in d.FLAG_ITEMS:
            L.append(f"  [{d.item_name_to_id[fname]}] = true,")
        L.append("}")
        L.append("WTG.FLAG_NAMES = {")
        for fname in d.FLAG_ITEMS:
            L.append(f'  ["{fname}"] = true,')
        L.append("}")
        L.append(f"WTG.MAX_FLAGS = {d.num_holes() + d.episode_hole_count()}\n")
        L.append("-- all_bosses goal: the 7 computers + the Final boss")
        L.append("WTG.BOSS_CLEAR_PATHS = {")
        for scene in d.all_boss_scenes():
            L.append(f'  "{self.location_mapping[d.location_name_to_id[d.clear_loc(scene)]]}",')
        L.append("}")
        final = self.location_mapping[d.location_name_to_id[d.clear_loc(d.FINAL_BOSS_SCENE)]]
        L.append(f'WTG.FINAL_BOSS_PATH = "{final}"\n')
        L.append("-- sections hidden when their feature is off, so onClear can also")
        L.append("-- zero their counts (invisible AND not owed)")
        L.append("WTG.CONDITIONAL = { crowns = {")
        for c in d.CHESTS:
            L.append(f'  "{self.location_mapping[d.location_name_to_id[d.chest_loc(c)]]}",')
        L.append("}, episodes = {")
        for ep in d.EPISODES:
            L.append(f'  ["{ep.name}"] = {{')
            for lv in ep.levels:
                L.append(f'    "{self.location_mapping[d.location_name_to_id[d.clear_loc(lv.scene)]]}",')
                if lv.challenges > 0:
                    L.append(f'    "{self.location_mapping[d.location_name_to_id[d.crown_loc(lv.scene)]]}",')
            L.append("  },")
        L.append("} }\n")
        # Area code -> map tab title, for the auto-switch. Keyed by BOTH the bare
        # chamber code and every sub-area code, because the mod may report either
        # granularity and a boss location's prefix is chamber-only. Chambers with a
        # single sub-area share the code (01, 02, 10, 00), hence the de-dupe.
        L.append("-- area code (chamber / sub-area / episode) -> map tab title")
        L.append("AREA_TABS = {")
        tabs = {}
        for mp in placer.maps:
            tabs[mp["area"]] = mp["title"]
        for sa in m.subarea_order:
            tabs.setdefault(sa, f"Chamber {m.subarea_chamber[sa]:02d}")
        for key in sorted(tabs):
            L.append(f'  ["{key}"] = "{tabs[key]}",')
        L.append("}")
        self._wt("scripts/wtg_data.lua", "\n".join(L) + "\n")

        im = ["-- GENERATED -- AP item id -> { pack code, kind }", "ITEM_MAPPING = {"]
        for iid in sorted(self.item_mapping):
            code, kind = self.item_mapping[iid]
            im.append(f'  [{iid}] = {{ "{code}", "{kind}" }},')
        im.append("}")
        self._wt("scripts/autotracking/item_mapping.lua", "\n".join(im) + "\n")

        lm = ["-- GENERATED -- AP location id -> pack section path", "LOCATION_MAPPING = {"]
        for lid in sorted(self.location_mapping):
            lm.append(f'  [{lid}] = {{ "{self.location_mapping[lid]}" }},')
        lm.append("}")
        self._wt("scripts/autotracking/location_mapping.lua", "\n".join(lm) + "\n")

    def manifest(self, version):
        with open(os.path.join(SRC, "manifest.json.in"), encoding="utf-8") as f:
            raw = f.read()
        self._wt("manifest.json", raw.replace("{package_version}", version))


# --- marker placement --------------------------------------------------------
class GridPlacer:
    """Where each check's marker goes on its map.

    Uses the REAL overworld coordinate for any entity the in-game dumps captured,
    and falls back to a deterministic grid slot for the rest -- per entity, not
    per map, so a partial dump still yields a correct map for what it covered.

    Projection is per chamber, from the bounding box of that chamber's OWN
    positioned members. It must not be a horizontal slice of the world: chambers
    05/06 and 02/05 overlap in y, so slicing would mix them.
    """
    W, MARGIN, HEADER, PAD, CELL, DOT = 1024, 20, 46, 12, 58, 22
    # PopTracker letterboxes a map into the available area, so a 1024x180 image
    # (chamber 10 has 3 holes) floats in a sea of background. Floor the height.
    MIN_H = 420
    # Padding around a chamber's world bbox, in world units / as a fraction.
    PAD_WORLD, PAD_FRAC = 4.0, 0.08
    # Don't project from too little evidence: two chests a few units apart would
    # be blown up to fill the map and imply a geometry that isn't there.
    MIN_PROJ_PTS = 3
    MIN_PROJ_SPAN = 6.0        # world units across the larger axis

    def __init__(self, model, positions=None, snapshots=None):
        self.m = model
        self.pos = positions or {}
        self.snaps = snapshots or {}
        self.maps = []
        self._pts = {}
        self.stats = {"dumped": 0, "grid": 0}
        self.clamped = []          # markers pushed onto the image edge
        self._build_chambers()
        self._build_episodes()

    def _snapshot_layout(self, name, groups):
        """Placement for a map backed by a real in-game render.

        The recorded camera rect is the projection -- no bbox fitting, no
        guessing. `groups` is [(key, label, [entity keys])]; the resulting pixel
        positions are kept per group so the schematic variant can draw a labelled
        region around each sub-area using the SAME projection, which is what lets
        both variants share one set of marker coordinates.
        """
        s = self.snaps.get(name)
        if not s:
            return None
        w, h = int(s["w"]), int(s["h"])
        dx = max(1e-6, s["maxx"] - s["minx"])
        dy = max(1e-6, s["maxy"] - s["miny"])

        out, n_missing = [], 0
        for gkey, label, keys in groups:
            pts, missing = [], []
            for k in keys:
                p = self.pos.get(k)
                if p is None:
                    missing.append(k)
                    continue
                px = int(round((p[0] - s["minx"]) / dx * w))
                py = int(round((s["maxy"] - p[1]) / dy * h))  # Unity y-up -> y-down
                # Keep the marker on the image even if it sits on the very edge.
                cx = max(self.DOT, min(w - self.DOT, px))
                cy = max(self.DOT, min(h - self.DOT, py))
                if (cx, cy) != (px, py):
                    # Clamping means the thing is OUTSIDE the rendered window, so
                    # the pin no longer points at it. Silent clamping is how a
                    # badly misplaced Computer 1 went unnoticed -- report it.
                    self.clamped.append(
                        f"{k} on {name}: ({px},{py}) -> ({cx},{cy})")
                px, py = cx, cy
                self._emit(name, k, (px, py))
                pts.append((px, py))
            # Anything with no dumped position (Computer 9 has none) goes in a row
            # along the bottom rather than on a grid over the art.
            for i, k in enumerate(missing):
                xy = (self.MARGIN + self.DOT + (n_missing + i) * self.CELL,
                      h - self.DOT * 2)
                self._emit(name, k, xy)
                pts.append(xy)
            n_missing += len(missing)
            self.stats["dumped"] += len(keys) - len(missing)
            self.stats["grid"] += len(missing)
            if pts:
                out.append({"key": gkey, "label": label, "pts": pts})
        return {"w": w, "h": h, "rendered": True, "groups": out}

    # -- projection ----------------------------------------------------------
    def _world_bbox(self, keys):
        pts = [self.pos[k] for k in keys if k in self.pos]
        if not pts:
            return None
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        return min(xs), min(ys), max(xs), max(ys)

    MAX_H, MIN_W = 900, 420

    def _canvas_size(self, keys, body_y):
        """Canvas shaped like the content, long edge 1024.

        A fixed 1024-wide box wastes most of the image whenever a chamber isn't
        landscape -- chamber 02 is a tall narrow strip, so it rendered as a sliver
        in a sea of background. Fitting BOTH dimensions to the world bbox's aspect
        keeps every map filled and keeps the scale honest (still uniform, so
        distances stay comparable within a map).
        """
        bb = self._world_bbox(keys)
        if bb is None:
            return self.W, self.MIN_H
        dx, dy = max(1e-6, bb[2] - bb[0]), max(1e-6, bb[3] - bb[1])
        aspect = dy / dx
        if aspect <= 1.0:
            w = self.W
            content_h = (w - 2 * self.MARGIN) * aspect
        else:
            content_h = self.MAX_H - body_y - self.PAD
            w = int(content_h / aspect) + 2 * self.MARGIN
        w = int(max(self.MIN_W, min(self.W, w)))
        h = int(max(self.MIN_H, min(self.MAX_H, body_y + content_h + self.PAD)))
        return w, h

    def _projectable(self, keys):
        pts = [self.pos[k] for k in keys if k in self.pos]
        if len(pts) < self.MIN_PROJ_PTS:
            return False
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        return max(max(xs) - min(xs), max(ys) - min(ys)) >= self.MIN_PROJ_SPAN

    def _project(self, keys, x0, y0, w, h):
        """Fit the positioned entities among `keys` into the pixel box, returning
        (place(key) -> (px, py) or None, had_any)."""
        pts = [self.pos[k] for k in keys if k in self.pos]
        if not pts:
            return (lambda _k: None), False
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        minx, maxx, miny, maxy = min(xs), max(xs), min(ys), max(ys)
        padx = max(self.PAD_WORLD, (maxx - minx) * self.PAD_FRAC)
        pady = max(self.PAD_WORLD, (maxy - miny) * self.PAD_FRAC)
        minx, maxx, miny, maxy = minx - padx, maxx + padx, miny - pady, maxy + pady
        dx, dy = max(1e-6, maxx - minx), max(1e-6, maxy - miny)
        scale = min(w / dx, h / dy)
        # Centre the fitted content in the box.
        offx = x0 + (w - dx * scale) / 2.0
        offy = y0 + (h - dy * scale) / 2.0

        def place(key):
            p = self.pos.get(key)
            if p is None:
                return None
            px = offx + (p[0] - minx) * scale
            # Unity is y-up, images are y-down.
            py = offy + (maxy - p[1]) * scale
            return (int(round(px)), int(round(py)))

        return place, True

    def points(self, key):
        return self._pts.get(key, [])

    def _grid(self, entities, x0, y0, width):
        """Place entities in a grid inside a column; returns (positions, rows)."""
        inner = max(self.CELL, width - 2 * self.PAD)
        per_row = max(1, inner // self.CELL)
        pos = []
        for i, key in enumerate(entities):
            cx = x0 + self.PAD + (i % per_row) * self.CELL + self.CELL // 2
            cy = y0 + (i // per_row) * self.CELL + self.CELL // 2
            pos.append((key, cx, cy))
        rows = (len(entities) + per_row - 1) // per_row if entities else 0
        return pos, rows

    def _entities(self):
        m = self.m
        chest_by_sa, holes_by_sa = {}, {}
        for c in m.data.CHESTS:
            chest_by_sa.setdefault(c.subarea, []).append(f"chest:{c.id}")
        for area in m.world["areas"]:
            for lv in area["levels"]:
                holes_by_sa.setdefault(lv.get("subarea") or "", []).append(
                    f"scene:{lv['scene']}")
        return holes_by_sa, chest_by_sa

    def _emit(self, map_name, key, xy):
        self._pts.setdefault(key, []).append(
            {"map": map_name, "x": xy[0], "y": xy[1]})

    def _build_chambers(self):
        m = self.m
        holes_by_sa, chest_by_sa = self._entities()

        for area in m.world["areas"]:
            ch = int(area["chamber"])
            name = f"chamber_{ch:02d}"
            subs = m.chamber_subareas.get(ch, [])
            all_keys = [k for sa in subs
                        for k in holes_by_sa.get(sa, []) + chest_by_sa.get(sa, [])]

            snap = self._snapshot_layout(name, [
                (sa, f"{sa} {m.subarea_theme.get(sa, sa)}",
                 holes_by_sa.get(sa, []) + chest_by_sa.get(sa, []))
                for sa in subs])
            if snap:
                self.maps.append({"name": name, "title": f"Chamber {ch:02d}",
                                  "area": f"{ch:02d}", "w": snap["w"], "h": snap["h"],
                                  "header": f"CHAMBER {ch:02d}", "cols": [],
                                  "projected": True, "rendered": True,
                                  "groups": snap["groups"]})
                continue

            # Real overworld geometry for the whole chamber at once, so sub-areas
            # keep their true spatial relationship to each other.
            body_y = self.HEADER + self.PAD
            have = self._projectable(all_keys)
            n_missing = sum(1 for k in all_keys if k not in self.pos)
            canvas_w, canvas_h = (self._canvas_size(all_keys, body_y) if have
                                  else (self.W, self.MIN_H))
            body_h = canvas_h - body_y - self.PAD
            # Split the canvas into a projected band on top and an "estimated"
            # band below, so grid slots can never land on top of real markers.
            proj_h = 0 if not have else (body_h if not n_missing else int(body_h * 0.55))
            grid_y = body_y + (proj_h + 16 if proj_h else 0)
            place, have = self._project(
                all_keys, self.MARGIN, body_y, canvas_w - 2 * self.MARGIN, proj_h) \
                if proj_h else ((lambda _k: None), False)

            cols, max_rows = [], 0
            ncol = max(1, len(subs))
            col_w = (canvas_w - 2 * self.MARGIN) // ncol
            for i, sa in enumerate(subs):
                x0 = self.MARGIN + i * col_w
                ents = holes_by_sa.get(sa, []) + chest_by_sa.get(sa, [])
                dumped, missing = [], []
                for k in ents:
                    xy = place(k) if have else None
                    if xy is None:
                        missing.append(k)
                    else:
                        dumped.append((k, xy[0], xy[1]))
                # Anything the dump hasn't captured gets a deterministic grid slot
                # in its own sub-area column, so a partial dump is still usable.
                grid, rows = self._grid(missing, x0, grid_y, col_w)
                max_rows = max(max_rows, rows)
                self.stats["dumped"] += len(dumped)
                self.stats["grid"] += len(missing)
                cols.append({"sa": sa, "x": x0, "w": col_w,
                             "pos": [(k, x, y) for k, x, y in grid],
                             "dumped": dumped, "projected": have})

            height = max(canvas_h, grid_y + max(0, max_rows) * self.CELL + self.PAD * 2)
            for col in cols:
                for key, cx, cy in col["pos"] + col["dumped"]:
                    self._emit(name, key, (cx, cy))
            self.maps.append({"name": name, "title": f"Chamber {ch:02d}",
                              "area": f"{ch:02d}", "w": canvas_w, "h": height,
                              "header": f"CHAMBER {ch:02d}", "cols": cols,
                              "projected": have})

    def _build_episodes(self):
        """Same treatment as a chamber, but each episode is one region: its holes
        are a flat set (no sub-areas) living in its own coordinate space."""
        m = self.m
        for ep in m.data.EPISODES:
            name = f"ep_{ep.campaign.lower()}"
            ents = [f"scene:{lv.scene}" for lv in ep.levels]

            snap = self._snapshot_layout(name, [(ep.name, ep.name, ents)])
            if snap:
                self.maps.append({"name": name, "title": ep.name, "area": ep.name,
                                  "w": snap["w"], "h": snap["h"],
                                  "header": ep.name.upper(), "cols": [],
                                  "projected": True, "rendered": True,
                                  "groups": snap["groups"]})
                continue
            body_y = self.HEADER + self.PAD
            have = self._projectable(ents)
            n_missing = sum(1 for k in ents if k not in self.pos)
            canvas_w, canvas_h = (self._canvas_size(ents, body_y) if have
                                  else (self.W, self.MIN_H))
            width = canvas_w - 2 * self.MARGIN
            body_h = canvas_h - body_y - self.PAD
            proj_h = 0 if not have else (body_h if not n_missing else int(body_h * 0.55))
            grid_y = body_y + (proj_h + 16 if proj_h else 0)
            place, have = self._project(ents, self.MARGIN, body_y, width, proj_h) \
                if proj_h else ((lambda _k: None), False)

            dumped, missing = [], []
            for k in ents:
                xy = place(k) if have else None
                (missing if xy is None else dumped).append(
                    k if xy is None else (k, xy[0], xy[1]))
            grid, rows = self._grid(missing, self.MARGIN, grid_y, width)
            height = max(canvas_h, grid_y + max(0, rows) * self.CELL + self.PAD * 2)

            for key, cx, cy in list(grid) + dumped:
                self._emit(name, key, (cx, cy))
            self.stats["dumped"] += len(dumped)
            self.stats["grid"] += len(missing)
            self.maps.append({
                "name": name, "title": ep.name, "area": ep.name,
                "w": canvas_w, "h": height, "header": ep.name.upper(), "projected": have,
                "cols": [{"sa": ep.name, "x": self.MARGIN, "w": width,
                          "pos": list(grid), "dumped": dumped, "projected": have}]})

    # -- rendering -----------------------------------------------------------
    def render(self, out_dir):
        m = self.m
        tint_of = {sa: hex_rgb(SUBAREA_TINTS[i % len(SUBAREA_TINTS)])
                   for i, sa in enumerate(m.subarea_order)}
        d = os.path.join(out_dir, "images", "maps")
        os.makedirs(d, exist_ok=True)
        for mp in self.maps:
            # Backed by a real in-game render, which copy_static already placed --
            # don't draw a schematic over it.
            if mp.get("rendered"):
                continue
            c = Canvas(mp["w"], mp["h"], BG)
            c.fill_rect(0, 0, mp["w"], self.HEADER - 8, PANEL)
            c.text(self.MARGIN, 14, mp["header"], INK, 3)

            # Divider + caption between the real-geometry band and the estimated
            # one, so a half-dumped chamber can't be mistaken for a finished map.
            est_ys = [y for col in mp["cols"] for _k, _x, y in col["pos"]]
            if mp.get("projected") and est_ys:
                dy = min(est_ys) - self.CELL // 2 - 16
                c.hline(self.MARGIN, dy, mp["w"] - 2 * self.MARGIN, mix(BG, INK, 0.28))
                c.text(self.MARGIN, dy + 5,
                       "ESTIMATED POSITIONS - NOT YET DUMPED", mix(BG, INK, 0.55), 1)

            for col in mp["cols"]:
                sa = col["sa"]
                tint = tint_of.get(sa, hex_rgb("#4c6fb0"))
                theme = m.subarea_theme.get(sa, sa)
                label = (f"{sa} {theme}" if sa in m.subarea_theme else str(theme)).upper()

                def region(x, y, w, h, text, short, strong):
                    c.blend_rect(x, y, w, h, tint, 0.20 if strong else 0.10)
                    c.frame(x, y, w, h, mix(tint, INK, 0.25 if strong else 0.05))
                    # Fall back to the short form rather than truncating -- an
                    # elided " EST" would hide that a marker is a guess.
                    head = text if text_width(text, 2) <= w - 16 else short
                    c.text(x + 6, y + 4, head, mix(tint, INK, 0.75 if strong else 0.4), 2)

                if col["dumped"]:
                    # Real geometry: hug the actual markers, so the sub-area's true
                    # shape and its position relative to its siblings both show.
                    xs = [x for _k, x, _y in col["dumped"]]
                    ys = [y for _k, _x, y in col["dumped"]]
                    pad = self.DOT
                    region(min(xs) - pad, min(ys) - pad - 12,
                           max(xs) - min(xs) + 2 * pad,
                           max(ys) - min(ys) + 2 * pad + 12, label, sa, True)
                if col["pos"] and not col["dumped"]:
                    # Nothing dumped for this sub-area: fall back to the tidy
                    # full-column block.
                    y0 = self.HEADER
                    region(col["x"] + 3, y0, col["w"] - 6,
                           mp["h"] - y0 - self.PAD // 2, label, sa, True)
                elif col["pos"]:
                    xs = [x for _k, x, _y in col["pos"]]
                    ys = [y for _k, _x, y in col["pos"]]
                    pad = self.DOT
                    region(min(xs) - pad, min(ys) - pad - 12,
                           max(xs) - min(xs) + 2 * pad,
                           max(ys) - min(ys) + 2 * pad + 12,
                           label + " EST", sa + " EST", False)

                s = self.DOT
                for _key, cx, cy in col["dumped"]:
                    c.fill_rect(cx - s // 2, cy - s // 2, s, s, mix(BG, tint, 0.35))
                    c.frame(cx - s // 2, cy - s // 2, s, s, mix(tint, BG, 0.35))
                # Estimated markers read dimmer, so it's obvious at a glance which
                # holes the in-game dump hasn't reached yet.
                for _key, cx, cy in col["pos"]:
                    c.fill_rect(cx - s // 2, cy - s // 2, s, s, mix(BG, tint, 0.18))
                    c.frame(cx - s // 2, cy - s // 2, s, s, mix(tint, BG, 0.6))
            c.write_png(os.path.join(d, mp["name"] + ".png"))


    def render_schematic(self, out_dir, variant):
        """Draw the diagram-style backgrounds for the alternate variant.

        Same canvas size and same projection as the real render, so both variants
        share ONE set of marker coordinates -- only the background image differs.
        Without that they would each need their own locations JSON.
        """
        m = self.m
        tint_of = {sa: hex_rgb(SUBAREA_TINTS[i % len(SUBAREA_TINTS)])
                   for i, sa in enumerate(m.subarea_order)}
        d = os.path.join(out_dir, variant, "images", "maps")
        os.makedirs(d, exist_ok=True)
        for mp in self.maps:
            groups = mp.get("groups")
            if not groups:
                # No in-game render for this map: the primary image is already the
                # schematic, so reuse it rather than drawing a second one.
                src = os.path.join(out_dir, "images", "maps", mp["name"] + ".png")
                if os.path.exists(src):
                    shutil.copyfile(src, os.path.join(d, mp["name"] + ".png"))
                continue

            c = Canvas(mp["w"], mp["h"], BG)
            c.fill_rect(0, 0, mp["w"], self.HEADER - 8, PANEL)
            c.text(self.MARGIN, 14, mp["header"], INK, 3)
            for g in groups:
                tint = tint_of.get(g["key"], hex_rgb("#4c6fb0"))
                xs = [p[0] for p in g["pts"]]
                ys = [p[1] for p in g["pts"]]
                pad = self.DOT
                x = max(0, min(xs) - pad)
                y = max(self.HEADER, min(ys) - pad - 12)
                w = min(mp["w"], max(xs) + pad) - x
                h = min(mp["h"], max(ys) + pad) - y
                c.blend_rect(x, y, w, h, tint, 0.20)
                c.frame(x, y, w, h, mix(tint, INK, 0.25))
                label = g["label"].upper()
                if text_width(label, 2) > w - 12:
                    label = g["key"].upper()
                c.text(x + 6, y + 4, label, mix(tint, INK, 0.75), 2)
                s = self.DOT
                for px, py in g["pts"]:
                    c.fill_rect(px - s // 2, py - s // 2, s, s, mix(BG, tint, 0.35))
                    c.frame(px - s // 2, py - s // 2, s, s, mix(tint, BG, 0.35))
            c.write_png(os.path.join(d, mp["name"] + ".png"))


# --- generated artwork -------------------------------------------------------
def render_icons(out_dir, icons):
    """A 32x32 label chip per item. Procedural art keeps the pack asset-free:
    the repo has no images at all, and PopTracker warns on missing files."""
    seen = set()
    for rel, label, cat in icons:
        if rel in seen:
            continue
        seen.add(rel)
        base = CAT_COLORS.get(cat, CAT_COLORS["opt"])
        c = Canvas(32, 32, mix(BG, base, 0.30))
        c.frame(0, 0, 32, 32, mix(base, INK, 0.35))
        c.fill_rect(2, 2, 28, 6, mix(base, INK, 0.10))
        txt = str(label).upper()[:6]
        scale = 2 if text_width(txt, 2) <= 28 else 1
        c.text_centered(16, 16 - (7 * scale) // 2 + 2, txt, INK, scale)
        path = os.path.join(out_dir, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        c.write_png(path)


SECTION_ART = {
    "hole":  ("#dfe6f5", "#4c6fb0"),
    "crown": ("#f2d06b", "#8a6a12"),
    "boss":  ("#f0908a", "#8a2f2a"),
    "chest": ("#e8c07a", "#8a651f"),
}


def render_sections(out_dir):
    d = os.path.join(out_dir, "images", "sections")
    os.makedirs(d, exist_ok=True)
    for kind, (face, edge) in SECTION_ART.items():
        for opened in (False, True):
            c = Canvas(24, 24, BG)
            f = hex_rgb(face)
            e = hex_rgb(edge)
            if opened:
                f, e = mix(f, BG, 0.62), mix(e, BG, 0.45)
            c.fill_rect(3, 3, 18, 18, f)
            c.frame(3, 3, 18, 18, e, 2)
            if opened:
                c.hline(6, 11, 12, e, 2)          # struck through = collected
            name = kind + ("_opened" if opened else "")
            c.write_png(os.path.join(d, name + ".png"))


def render_pack_icon(out_dir):
    c = Canvas(64, 64, BG)
    c.frame(0, 0, 64, 64, CAT_COLORS["gate"], 2)
    c.fill_rect(4, 4, 56, 18, mix(BG, CAT_COLORS["gate"], 0.5))
    c.text_centered(32, 8, "WTG", INK, 2)
    c.text_centered(32, 30, "GOLF", INK, 2)
    c.text_centered(32, 46, "AP", CAT_COLORS["flag"], 2)
    path = os.path.join(out_dir, "images", "icon.png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    c.write_png(path)


# --- validation --------------------------------------------------------------
def validate(model, em, placer):
    """The anti-drift gate. Every failure here is fatal: a silently wrong pack is
    far worse than a build that stops."""
    d = model.data
    problems = []

    want_locs = set(d.location_name_to_id)
    if em.emitted_locs != want_locs:
        missing = sorted(want_locs - em.emitted_locs)[:6]
        extra = sorted(em.emitted_locs - want_locs)[:6]
        problems.append(f"location set mismatch (missing={missing} extra={extra})")

    want_items = set(d.item_name_to_id)
    if em.emitted_items != want_items:
        missing = sorted(want_items - em.emitted_items)[:6]
        extra = sorted(em.emitted_items - want_items)[:6]
        problems.append(f"item set mismatch (missing={missing} extra={extra})")

    unknown = em.rule_codes - em.declared_codes
    if unknown:
        problems.append(f"access rules reference undeclared codes: {sorted(unknown)[:8]}")

    # A ':' in a name would be parsed as `code:count` by the rule engine.
    for path in sorted(em.section_paths):
        body = path[1:]
        if any(ch in body for ch in _BAD):
            problems.append(f"section path contains rule syntax: {path}")
            break

    # PopTracker keys top-level locations by NAME globally and silently renames a
    # duplicate to "Name[1]" -- after which every "@Name/..." path resolves to the
    # FIRST node and the second node's sections can never be found. Same story for
    # two children sharing a name inside one parent. Both are silent in-game, so
    # they have to be build failures here.
    seen_parents, dup_parents, dup_children = set(), [], []
    for rel in ("locations/main.json", "locations/episodes.json"):
        path = os.path.join(em.out, rel)
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as f:
            for parent in json.load(f):
                if parent["name"] in seen_parents:
                    dup_parents.append(parent["name"])
                seen_parents.add(parent["name"])
                kids = [c["name"] for c in parent.get("children", [])]
                dup_children += [f'{parent["name"]}/{n}'
                                 for n in kids if kids.count(n) > 1]
    if dup_parents:
        problems.append(f"duplicate top-level location names: {sorted(set(dup_parents))}")
    if dup_children:
        problems.append(f"duplicate child names in a parent: {sorted(set(dup_children))[:6]}")

    # Every map needs a background in BOTH variants. PopTracker falls back to the
    # pack root when a variant lacks a file, so a gap here would silently show the
    # real art inside the schematic variant instead of failing.
    with open(os.path.join(em.out, "manifest.json"), encoding="utf-8") as f:
        variants = list(json.load(f).get("variants", {}))
    for mp in placer.maps:
        for v in variants:
            rel = (f"{v}/images/maps/{mp['name']}.png" if v == SCHEMATIC_VARIANT
                   else f"images/maps/{mp['name']}.png")
            if not os.path.exists(os.path.join(em.out, rel.replace("/", os.sep))):
                problems.append(f"missing map image for variant {v}: {rel}")

    map_names = {mp["name"] for mp in placer.maps}
    for key, pts in placer._pts.items():
        for p in pts:
            if p["map"] not in map_names:
                problems.append(f"{key} points at unknown map {p['map']}")
                break

    if len(em.location_mapping) != len(want_locs):
        problems.append(f"location_mapping has {len(em.location_mapping)} entries, "
                        f"expected {len(want_locs)}")

    if problems:
        for p in problems:
            print(f"  ERROR: {p}", file=sys.stderr)
        raise SystemExit("build_poptracker: validation failed")


# --- static files + packaging -----------------------------------------------
# Build inputs that live under poptracker_src/ but must NOT ship in the pack:
# snapshots.json is the camera rects, changelog/ feeds the version ledger.
SKIP_SRC = {"manifest.json.in", "pack_version.txt",
            "images/maps/snapshots.json"}
SKIP_DIRS = {"changelog"}


def copy_static(out_dir):
    for base, dirs, files in os.walk(SRC):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for fn in files:
            rel = os.path.relpath(os.path.join(base, fn), SRC)
            if rel.replace("\\", "/") in SKIP_SRC or fn in SKIP_SRC:
                continue
            dst = os.path.join(out_dir, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copyfile(os.path.join(base, fn), dst)


def read_version():
    with open(os.path.join(SRC, "pack_version.txt"), encoding="utf-8") as f:
        return f.read().strip()


def build(out_dir, version):
    data = load_data()
    world = load_levels()
    ids = load_ids()
    # A stale mod/ids.json means the mod and the tracker disagree about IDs.
    if ids.get("locations") != data.location_name_to_id:
        raise SystemExit("mod/ids.json is stale -- run: python tools/export_ids.py")
    if ids.get("items") != data.item_name_to_id:
        raise SystemExit("mod/ids.json is stale -- run: python tools/export_ids.py")

    model = Model(data, world, ids)
    placer = GridPlacer(model, load_world_positions(world), load_snapshots())
    em = Emitter(model, out_dir)

    em.items()
    em.locations(placer)
    em.maps_json(placer)
    em.layouts(placer)
    em.scripts(placer)
    em.manifest(version)
    copy_static(out_dir)
    placer.render(out_dir)
    # Alternate variant: same maps, diagram backgrounds. PopTracker resolves an
    # image from <variant>/ before the pack root, so only these 16 files differ --
    # locations, layouts, items and Lua are all shared.
    placer.render_schematic(out_dir, SCHEMATIC_VARIANT)
    render_icons(out_dir, em.icons)
    render_sections(out_dir)
    render_pack_icon(out_dir)
    validate(model, em, placer)
    return model, em, placer


def tree_equal(a, b):
    def snap(root):
        out = {}
        for base, _dirs, files in os.walk(root):
            for fn in files:
                p = os.path.join(base, fn)
                rel = os.path.relpath(p, root).replace("\\", "/")
                with open(p, "rb") as f:
                    out[rel] = f.read()
        return out
    return snap(a) == snap(b), snap(a), snap(b)


def write_zip(pack_dir, zip_path):
    """Zip with the manifest at the archive ROOT and forward-slash entries --
    PowerShell's Compress-Archive writes back-slashes, which PopTracker rejects."""
    os.makedirs(os.path.dirname(os.path.abspath(zip_path)) or ".", exist_ok=True)
    names = []
    for base, _dirs, files in os.walk(pack_dir):
        for fn in files:
            p = os.path.join(base, fn)
            names.append((os.path.relpath(p, pack_dir).replace("\\", "/"), p))
    names.sort()
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        for rel, p in names:
            z.write(p, rel)
    return len(names)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true",
                    help="build to a temp dir and fail if poptracker/ differs")
    ap.add_argument("--zip", metavar="PATH", help="also write a distributable zip")
    ap.add_argument("--record-version", nargs=2, metavar=("VERSION", "SHA256"),
                    help="upsert this version into poptracker-versions.json")
    ap.add_argument("--print-version", action="store_true",
                    help="print the pack version and exit")
    args = ap.parse_args()

    version = read_version()

    if args.print_version:
        print(version)
        return

    if args.record_version:
        record_version(args.record_version[0], args.record_version[1])
        return

    if args.check:
        tmp = tempfile.mkdtemp(prefix="wtgpop-")
        try:
            build(tmp, version)
            same, a, b = tree_equal(tmp, OUT)
            if not same:
                diff = sorted(set(a) ^ set(b)) or \
                    sorted(k for k in a if k in b and a[k] != b[k])
                print("poptracker/ is stale. Run: python tools/build_poptracker.py",
                      file=sys.stderr)
                for k in diff[:15]:
                    print(f"  differs: {k}", file=sys.stderr)
                raise SystemExit(1)
            print("poptracker/ is up to date")
        finally:
            shutil.rmtree(tmp, ignore_errors=True)
        return

    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    os.makedirs(OUT, exist_ok=True)
    _model, em, placer = build(OUT, version)

    print(f"wrote {OUT} (v{version}): "
          f"{len(em.emitted_locs)} locations, {len(em.emitted_items)} items, "
          f"{len(em.location_mapping)} id->section, {len(placer.maps)} maps, "
          f"{len(set(r for r, _l, _c in em.icons))} icons")
    d_, g_ = placer.stats["dumped"], placer.stats["grid"]
    pct = (100.0 * d_ / (d_ + g_)) if (d_ + g_) else 0.0
    print(f"markers: {d_} at real overworld coordinates, {g_} grid-estimated "
          f"({pct:.0f}% dumped)")
    for mp in placer.maps:
        if not mp.get("projected"):
            continue
        est = sum(len(c["pos"]) for c in mp["cols"])
        if est:
            print(f"  {mp['name']}: {est} marker(s) still estimated")
    if placer.clamped:
        print(f"WARNING: {len(placer.clamped)} marker(s) fall outside their map's "
              f"rendered window and were pinned to the edge:")
        for c in placer.clamped:
            print(f"  {c}")

    if args.zip:
        n = write_zip(OUT, args.zip)
        print(f"wrote {args.zip} ({n} entries)")


if __name__ == "__main__":
    main()
