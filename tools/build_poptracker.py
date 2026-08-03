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
SRC = os.path.join(ROOT, "tools", "poptracker_src")
OUT = os.path.join(ROOT, "poptracker")

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


# --- grid placement (Phase 1: real coordinates land in Phase 3) --------------
class GridPlacer:
    """Deterministic fallback layout: sub-areas as columns, holes on a grid.

    Phase 3 replaces the coordinates with the real overworld positions once the
    in-game dump exists. Everything else about the pack stays identical, which
    is why the coordinates live behind this one interface.
    """
    W, MARGIN, HEADER, PAD, CELL, DOT = 1024, 20, 46, 12, 58, 22
    # PopTracker letterboxes a map into the available area, so a 1024x180 image
    # (chamber 10 has 3 holes) floats in a sea of background. Floor the height.
    MIN_H = 420

    def __init__(self, model):
        self.m = model
        self.maps = []
        self._pts = {}
        self._build_chambers()
        self._build_episodes()

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

    def _build_chambers(self):
        m = self.m
        chest_by_sa = {}
        for c in m.data.CHESTS:
            chest_by_sa.setdefault(c.subarea, []).append(f"chest:{c.id}")
        holes_by_sa = {}
        for area in m.world["areas"]:
            for lv in area["levels"]:
                holes_by_sa.setdefault(lv.get("subarea") or "", []).append(
                    f"scene:{lv['scene']}")

        for area in m.world["areas"]:
            ch = int(area["chamber"])
            name = f"chamber_{ch:02d}"
            subs = m.chamber_subareas.get(ch, [])
            ncol = max(1, len(subs))
            col_w = (self.W - 2 * self.MARGIN) // ncol
            cols, max_rows = [], 0
            for i, sa in enumerate(subs):
                x0 = self.MARGIN + i * col_w
                ents = holes_by_sa.get(sa, []) + chest_by_sa.get(sa, [])
                pos, rows = self._grid(ents, x0, self.HEADER + self.PAD, col_w)
                max_rows = max(max_rows, rows)
                cols.append({"sa": sa, "x": x0, "w": col_w, "pos": pos})
            height = max(self.MIN_H, self.HEADER + self.PAD * 2
                         + max(1, max_rows) * self.CELL + self.PAD)
            for col in cols:
                for key, cx, cy in col["pos"]:
                    self._pts.setdefault(key, []).append(
                        {"map": name, "x": cx, "y": cy})
            self.maps.append({"name": name, "title": f"Chamber {ch:02d}",
                              "area": f"{ch:02d}", "w": self.W, "h": height,
                              "header": f"CHAMBER {ch:02d}", "cols": cols})

    def _build_episodes(self):
        m = self.m
        for ep in m.data.EPISODES:
            name = f"ep_{ep.campaign.lower()}"
            ents = [f"scene:{lv.scene}" for lv in ep.levels]
            width = self.W - 2 * self.MARGIN
            pos, rows = self._grid(ents, self.MARGIN, self.HEADER + self.PAD, width)
            height = max(self.MIN_H, self.HEADER + self.PAD * 2
                         + max(1, rows) * self.CELL + self.PAD)
            for key, cx, cy in pos:
                self._pts.setdefault(key, []).append({"map": name, "x": cx, "y": cy})
            self.maps.append({
                "name": name, "title": ep.name, "area": ep.name,
                "w": self.W, "h": height, "header": ep.name.upper(),
                "cols": [{"sa": ep.name, "x": self.MARGIN, "w": width, "pos": pos}]})

    # -- rendering -----------------------------------------------------------
    def render(self, out_dir):
        m = self.m
        tint_of = {sa: hex_rgb(SUBAREA_TINTS[i % len(SUBAREA_TINTS)])
                   for i, sa in enumerate(m.subarea_order)}
        d = os.path.join(out_dir, "images", "maps")
        os.makedirs(d, exist_ok=True)
        for mp in self.maps:
            c = Canvas(mp["w"], mp["h"], BG)
            c.fill_rect(0, 0, mp["w"], self.HEADER - 8, PANEL)
            c.text(self.MARGIN, 14, mp["header"], INK, 3)
            for col in mp["cols"]:
                sa = col["sa"]
                tint = tint_of.get(sa, hex_rgb("#4c6fb0"))
                y0 = self.HEADER
                h = mp["h"] - y0 - self.PAD // 2
                c.blend_rect(col["x"] + 3, y0, col["w"] - 6, h, tint, 0.20)
                c.frame(col["x"] + 3, y0, col["w"] - 6, h, mix(tint, INK, 0.25))
                label = sa if sa in m.subarea_theme else ""
                theme = m.subarea_theme.get(sa, sa)
                head = f"{label} {theme}".strip().upper()
                if text_width(head, 2) > col["w"] - 16:
                    head = (label or theme).upper()
                c.text(col["x"] + 9, y0 + 4, head, mix(tint, INK, 0.75), 2)
                for _key, cx, cy in col["pos"]:
                    s = self.DOT
                    c.fill_rect(cx - s // 2, cy - s // 2, s, s, mix(BG, tint, 0.35))
                    c.frame(cx - s // 2, cy - s // 2, s, s, mix(tint, BG, 0.35))
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
SKIP_SRC = {"manifest.json.in", "pack_version.txt"}


def copy_static(out_dir):
    for base, _dirs, files in os.walk(SRC):
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
    placer = GridPlacer(model)
    em = Emitter(model, out_dir)

    em.items()
    em.locations(placer)
    em.maps_json(placer)
    em.layouts(placer)
    em.scripts(placer)
    em.manifest(version)
    copy_static(out_dir)
    placer.render(out_dir)
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
    args = ap.parse_args()

    version = read_version()

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

    if args.zip:
        n = write_zip(OUT, args.zip)
        print(f"wrote {args.zip} ({n} entries)")


if __name__ == "__main__":
    main()
