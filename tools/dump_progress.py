"""Report how much of the map-coordinate dump is captured, while you walk.

    python tools/dump_progress.py            # read the LIVE game-root dumps
    python tools/dump_progress.py --repo     # read the committed copies

Reads the dumpers' own output, so it is an honest view of what a rebuild would
actually place at real coordinates. See tools/poptracker_tests/README.md.
"""
import argparse
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = r"C:\Program Files (x86)\Steam\steamapps\common\WHAT THE GOLF"


def load(path, default):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return default


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", action="store_true", help="read mod/ instead of the game dir")
    ap.add_argument("--dir", default=None, help="explicit directory holding wtg_*.json")
    args = ap.parse_args()
    src = args.dir or (os.path.join(ROOT, "mod") if args.repo else GAME)

    world = load(os.path.join(ROOT, "what_the_golf", "levels.json"), {})
    goals = load(os.path.join(src, "wtg_goals.json"), [])
    doors = load(os.path.join(src, "wtg_doors.json"), {})

    # scene -> live position, ignoring the "Hub" main-menu artifact.
    live = {g["scene"] for g in goals
            if g.get("scene") and g.get("in_scene") and g.get("campaign") != "Hub"}
    boss_scene = {bd["boss_level_id"]: bd["scene"] for bd in world.get("boss_doors", ())}
    for d in doors.get("doors", {}).values():
        if d.get("in_scene") and boss_scene.get(d.get("boss_level_id")):
            live.add(boss_scene[d["boss_level_id"]])

    print(f"source: {src}\n")
    total_have = total_need = 0
    for area in world.get("areas", []):
        subs = {}
        for lv in area["levels"]:
            sa = lv.get("subarea") or "?"
            have, need = subs.get(sa, (0, 0))
            subs[sa] = (have + (1 if lv["scene"] in live else 0), need + 1)
        have = sum(h for h, _ in subs.values())
        need = sum(n for _, n in subs.values())
        total_have += have
        total_need += need
        gaps = " ".join(f"{sa}:{h}/{n}" for sa, (h, n) in sorted(subs.items()) if h < n)
        flag = "OK " if have == need else "   "
        print(f"  {flag}{area['name']:12} {have:3}/{need:<3} {gaps}")

    for ep in world.get("episodes", []):
        have = sum(1 for lv in ep["levels"] if lv["scene"] in live)
        need = len(ep["levels"])
        total_have += have
        total_need += need
        flag = "OK " if have == need else "   "
        print(f"  {flag}{ep['name']:12} {have:3}/{need:<3}")

    pct = 100.0 * total_have / total_need if total_need else 0
    print(f"\n  {total_have}/{total_need} holes have a live position ({pct:.0f}%)")
    missing = [lv["scene"] for area in world.get("areas", [])
               for lv in area["levels"] if lv["scene"] not in live]
    if missing and len(missing) <= 20:
        print("  still missing (Main):")
        for s in missing:
            print(f"    {s}")


if __name__ == "__main__":
    main()
