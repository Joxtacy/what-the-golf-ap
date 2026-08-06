"""Prove the PopTracker pack's access rules mean the same thing as Rules.py.

Runs the apworld's REAL logic -- a live Archipelago MultiWorld, the same code
generation uses -- side by side with an evaluator for the pack's generated
`access_rules`, over many random item subsets and every meaningful option
combination. Any disagreement is a logic bug in the pack.

    python tools/check_poptracker_logic.py
    python tools/check_poptracker_logic.py --ap ../Archipelago-Keen --subsets 60

Both sides are joined ONLY through artifacts that actually ship:

    AP item name --(data.py item_name_to_id)--> id --(item_mapping.lua)--> code
    AP location name --(data.py location_name_to_id)--> id
                     --(location_mapping.lua)--> "@Parent/Child/Section"

That matters: the name<->code bridge is exactly what the running tracker uses,
so a wrong bridge fails the test instead of hiding inside it.

SequenceBreak counts as NOT in logic. It models the documented walk-in looseness
(Options.AreaAccess), which Archipelago never assumes you can do, so a location
Archipelago considers unreachable may legitimately be SequenceBreak here -- but a
location Archipelago considers REACHABLE must be fully Normal.
"""

import argparse
import importlib.util
import json
import os
import random
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACK = os.path.join(ROOT, "poptracker")
DATA_PY = os.path.join(ROOT, "what_the_golf", "data.py")

NONE, SEQ, NORMAL = "none", "sequence_break", "normal"


def load_data():
    spec = importlib.util.spec_from_file_location("wtg_data", DATA_PY)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


# --- read the emitted pack artifacts ----------------------------------------
_LUA_ROW = re.compile(r"\[(\d+)\]\s*=\s*\{\s*\"([^\"]+)\"(?:\s*,\s*\"([^\"]+)\")?\s*\}")


def read_lua_mapping(rel):
    out = {}
    with open(os.path.join(PACK, rel), encoding="utf-8") as f:
        for m in _LUA_ROW.finditer(f.read()):
            out[int(m.group(1))] = (m.group(2), m.group(3))
    return out


def read_pack_rules():
    """'@Parent/Child/Section' -> the AND/OR rule groups guarding it."""
    rules = {}
    for rel in ("locations/main.json", "locations/episodes.json"):
        path = os.path.join(PACK, rel)
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as f:
            for parent in json.load(f):
                pr = parent.get("access_rules") or []
                for child in parent.get("children", []):
                    groups = child.get("access_rules") or []
                    # A parent rule would AND with the child's; today parents carry
                    # none, but combine anyway so that stays true if it changes.
                    if pr:
                        groups = [p + c for p in pr for c in (groups or [[]])]
                    vis = child.get("visibility_rules") or []
                    for sec in child["sections"]:
                        key = f'@{parent["name"]}/{child["name"]}/{sec["name"]}'
                        rules[key] = (groups, vis)
    return rules


# --- the pack's rule engine, reimplemented -----------------------------------
def walkable(chamber, held, chamber_gates):
    """Mirror of scripts/logic.lua:walkable."""
    if "opt_area_section" not in held:
        return NONE
    if "opt_walk" not in held:
        return NONE
    if "opt_hard" in held:
        return NONE
    for code in chamber_gates.get(chamber, ()):
        if code in held:
            return SEQ
    return NONE


def eval_groups(groups, held, chamber_gates):
    """OR of ANDs -> the best AccessibilityLevel any group grants."""
    if not groups:
        return NORMAL                      # no rule = always open (chamber 10)
    best = NONE
    for group in groups:
        ok, seq = True, False
        for code in group:
            if code.startswith("^$"):
                fn, _, arg = code[2:].partition("|")
                if fn != "walkable":
                    raise SystemExit(f"unknown rule function {fn!r}")
                r = walkable(arg, held, chamber_gates)
                if r == NONE:
                    ok = False
                    break
                if r == SEQ:
                    seq = True
            elif code not in held:
                ok = False
                break
        if ok:
            if not seq:
                return NORMAL
            best = SEQ
    return best


def read_chamber_gates():
    """Parse WTG.CHAMBER_GATES out of the generated wtg_data.lua."""
    txt = open(os.path.join(PACK, "scripts", "wtg_data.lua"), encoding="utf-8").read()
    block = txt.split("WTG.CHAMBER_GATES = {", 1)[1].split("\n}", 1)[0]
    out = {}
    for m in re.finditer(r'\["(\w+)"\]\s*=\s*\{([^}]*)\}', block):
        out[m.group(1)] = re.findall(r'"([^"]+)"', m.group(2))
    return out


# --- option combos -----------------------------------------------------------
COMBOS = [
    dict(name="default (section, campaign)",
         opts=dict(goal="campaign", area_access="section")),
    dict(name="chamber + all_bosses + boss_keys",
         opts=dict(goal="all_bosses", area_access="chamber", boss_keys=True)),
    dict(name="section + boss_keys + crowns",
         opts=dict(goal="campaign", area_access="section", boss_keys=True, crowns=True)),
    dict(name="section + crowns, boss_keys OFF (whole-chamber boss rule)",
         opts=dict(goal="campaign", area_access="section", crowns=True)),
    dict(name="chamber + crowns, boss_keys OFF",
         opts=dict(goal="campaign", area_access="chamber", crowns=True)),
    dict(name="section + door_100 + crowns + all episodes",
         opts=dict(goal="door_100", area_access="section", crowns=True,
                   episodes=["Sporty Sports", "Snow", "Hotdog", "Alive", "Among Us"])),
    dict(name="section + hard_sections (no walk-in looseness)",
         opts=dict(goal="campaign", area_access="section", hard_sections=True)),
    dict(name="chamber + all episodes + boss_keys + crowns",
         opts=dict(goal="all_bosses", area_access="chamber", boss_keys=True,
                   crowns=True, episodes=["Snow", "Among Us"])),
]


def opt_codes(opts, data):
    """The opt_* codes autotracking.lua would set from this slot data."""
    held = set()
    held.add("opt_area_chamber" if opts.get("area_access") == "chamber"
             else "opt_area_section")
    held.add("opt_boss_keys_on" if opts.get("boss_keys") else "opt_boss_keys_off")
    if opts.get("crowns"):
        held.add("opt_crowns")
    if opts.get("hard_sections"):
        held.add("opt_hard")
        # apply_slot_data force-clears opt_walk on a hard_sections seed
    else:
        held.add("opt_walk")
    enabled = set(opts.get("episodes") or ())
    for ep in data.EPISODES:
        if ep.name in enabled:
            held.add(f"opt_ep_{ep.campaign.lower()}")
    return held


def is_visible(vis, held_opts):
    """visibility_rules are OR-of-ANDs, same as access_rules."""
    if not vis:
        return True
    return any(all(c in held_opts for c in g) for g in vis)


LEVEL_NUM = {NONE: 0, SEQ: 5, NORMAL: 6}

# The opt_* codes are STAGES of progressive items, so they can't just be toggled
# on -- they are selected by setting the parent item's CurrentStage.
STAGE_OF = {
    "opt_area_section": ("opt_area", 0), "opt_area_chamber": ("opt_area", 1),
    "opt_boss_keys_off": ("opt_boss_keys", 0), "opt_boss_keys_on": ("opt_boss_keys", 1),
}


def write_lua_cases(path, samples):
    """Emit a self-checking Lua harness: for each case, force the tracker into
    exactly that item state and assert PopTracker's own AccessibilityLevel."""
    lines = [
        "-- GENERATED by tools/check_poptracker_logic.py -- temporary test harness.",
        "-- Replays cases already proven against the apworld's real logic, to confirm",
        "-- PopTracker's rule engine reads the pack's JSON the same way.",
        "local STAGE_OF = {",
    ]
    for code, (parent, stage) in STAGE_OF.items():
        lines.append(f'  ["{code}"] = {{ "{parent}", {stage} }},')
    lines += [
        "}",
        "local CASES = {",
    ]
    for level in (NORMAL, SEQ, NONE):
        for held, p, lvl in samples[level]:
            codes = ", ".join(f'"{c}"' for c in held)
            lines.append(f'  {{ {{ {codes} }}, "{p}", {LEVEL_NUM[lvl]} }},')
    lines += [
        "}",
        "",
        "local ALL_TOGGLES = {}",
        "for _, v in pairs(ITEM_MAPPING) do",
        "  if v[2] == \"toggle\" then ALL_TOGGLES[v[1]] = true end",
        "end",
        "for _, c in ipairs({\"opt_crowns\", \"opt_hard\", \"opt_walk\"}) do",
        "  ALL_TOGGLES[c] = true",
        "end",
        "for _, ep in ipairs(WTG.EPISODES) do ALL_TOGGLES[ep.opt] = true end",
        "",
        "local function apply(codes)",
        "  Tracker.BulkUpdate = true",
        "  for code, _ in pairs(ALL_TOGGLES) do",
        "    local o = Tracker:FindObjectForCode(code)",
        "    if o then o.Active = false end",
        "  end",
        "  for _, code in ipairs(codes) do",
        "    local st = STAGE_OF[code]",
        "    if st then",
        "      local o = Tracker:FindObjectForCode(st[1])",
        "      if o then o.CurrentStage = st[2] end",
        "    else",
        "      local o = Tracker:FindObjectForCode(code)",
        "      if o then o.Active = true end",
        "    end",
        "  end",
        "  Tracker.BulkUpdate = false",
        "end",
        "",
        "local pass, fail = 0, 0",
        "for i, case in ipairs(CASES) do",
        "  apply(case[1])",
        "  local o = Tracker:FindObjectForCode(case[2])",
        "  local got = o and o.AccessibilityLevel or -1",
        "  if got == case[3] then pass = pass + 1 else",
        "    fail = fail + 1",
        "    if fail <= 10 then",
        "      print(string.format(\"PARITY MISMATCH #%d %s expected=%d got=%d\",",
        "        i, case[2], case[3], got))",
        "    end",
        "  end",
        "end",
        "print(string.format(\"PARITY: %d/%d cases match PopTracker's engine\",",
        "  pass, pass + fail))",
    ]
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ap", default=os.path.join(ROOT, "..", "Archipelago-Keen"),
                    help="path to an Archipelago source checkout")
    ap.add_argument("--subsets", type=int, default=40,
                    help="random item subsets per option combo")
    ap.add_argument("--seed", type=int, default=1234)
    ap.add_argument("--dump-lua", metavar="PATH",
                    help="also write a Lua case file that replays a sample of the "
                         "verified cases inside PopTracker, to confirm ITS rule "
                         "engine agrees with this evaluator")
    ap.add_argument("--dump-count", type=int, default=80,
                    help="cases per accessibility outcome in --dump-lua")
    args = ap.parse_args()
    # Resolve before the chdir into the Archipelago checkout below, or a relative
    # --dump-lua would land in the wrong repo.
    if args.dump_lua:
        args.dump_lua = os.path.abspath(args.dump_lua)

    data = load_data()
    item_map = read_lua_mapping("scripts/autotracking/item_mapping.lua")
    loc_map = read_lua_mapping("scripts/autotracking/location_mapping.lua")
    pack_rules = read_pack_rules()
    chamber_gates = read_chamber_gates()

    code_by_item = {}
    for name, iid in data.item_name_to_id.items():
        if iid in item_map:
            code_by_item[name] = item_map[iid][0]
    path_by_loc = {name: loc_map[iid][0]
                   for name, iid in data.location_name_to_id.items() if iid in loc_map}

    ap_root = os.path.abspath(args.ap)
    if not os.path.exists(os.path.join(ap_root, "Generate.py")):
        raise SystemExit(f"not an Archipelago checkout: {ap_root}")
    sys.path.insert(0, ap_root)
    os.chdir(ap_root)
    from test.general import setup_multiworld            # noqa: E402
    from worlds.what_the_golf import WTGWorld            # noqa: E402
    from BaseClasses import CollectionState              # noqa: E402

    rng = random.Random(args.seed)
    total_checks = 0
    failures = []
    samples = {NONE: [], SEQ: [], NORMAL: []}

    for combo in COMBOS:
        mw = setup_multiworld(WTGWorld, options=combo["opts"])
        world = mw.worlds[1]
        held_opts = opt_codes(combo["opts"], data)

        # Locations that really exist in this seed (skip the Victory event).
        locs = [loc for loc in mw.get_locations(1) if loc.address is not None]

        # Progression items that can gate a location in this seed.
        pool = sorted({it.name for it in mw.itempool
                       if it.player == 1 and it.name in code_by_item
                       and not it.name.startswith("Flag")
                       and it.name not in data.FLAG_ITEMS})

        subsets = [set(), set(pool)]
        for _ in range(args.subsets):
            k = rng.randint(0, len(pool))
            subsets.append(set(rng.sample(pool, k)))
        for one in pool:                     # each key alone: catches over-gating
            subsets.append({one})

        combo_fail = 0
        for subset in subsets:
            state = CollectionState(mw)
            for name in subset:
                state.collect(world.create_item(name), prevent_sweep=True)
            held = held_opts | {code_by_item[n] for n in subset}

            for loc in locs:
                path = path_by_loc.get(loc.name)
                if path is None:
                    failures.append(f"[{combo['name']}] no pack path for {loc.name!r}")
                    continue
                groups, _vis = pack_rules[path]
                pack = eval_groups(groups, held, chamber_gates)
                ap_ok = loc.can_reach(state)
                total_checks += 1
                if args.dump_lua and len(samples[pack]) < args.dump_count \
                        and rng.random() < 0.02:
                    samples[pack].append((sorted(held), path, pack))
                if ap_ok and pack != NORMAL:
                    combo_fail += 1
                    if combo_fail <= 4:
                        failures.append(
                            f"[{combo['name']}] AP reachable but pack={pack}: "
                            f"{loc.name} | held={sorted(held)} | rules={groups}")
                elif not ap_ok and pack == NORMAL:
                    combo_fail += 1
                    if combo_fail <= 4:
                        failures.append(
                            f"[{combo['name']}] pack in-logic but AP unreachable: "
                            f"{loc.name} | held={sorted(held)} | rules={groups}")

        # Visibility parity: exactly the locations this seed does NOT have must be
        # the ones hidden behind a visibility rule.
        ap_names = {loc.name for loc in locs}
        for name, path in path_by_loc.items():
            _groups, vis = pack_rules[path]
            hidden = not is_visible(vis, held_opts)
            if name in ap_names and hidden:
                failures.append(f"[{combo['name']}] {name} exists in seed but pack hides it")
            if name not in ap_names and not hidden:
                failures.append(f"[{combo['name']}] {name} not in seed but pack shows it")

        status = "FAIL" if combo_fail else "ok"
        print(f"  {status:4} {combo['name']}: {len(locs)} locations x "
              f"{len(subsets)} item subsets"
              + (f" -- {combo_fail} mismatches" if combo_fail else ""))

    print(f"\n{total_checks} location/state comparisons across {len(COMBOS)} option combos")

    if args.dump_lua:
        write_lua_cases(args.dump_lua, samples)
        n = sum(len(v) for v in samples.values())
        print(f"wrote {args.dump_lua}: {n} replay cases "
              + ", ".join(f"{k}={len(v)}" for k, v in samples.items()))

    if failures:
        print(f"\n{len(failures)} problem(s):", file=sys.stderr)
        for f in failures[:25]:
            print("  " + f, file=sys.stderr)
        raise SystemExit(1)
    print("logic parity: PASS")


if __name__ == "__main__":
    main()
