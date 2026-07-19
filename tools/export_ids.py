"""Export the apworld's item/location ID tables to JSON for the game mod.

Framework-free: imports only what_the_golf/data.py, so you can run it without a
full Archipelago checkout:

    python tools/export_ids.py

Writes mod/ids.json ({ "items": {name: id}, "locations": {name: id} }). The mod
loads this into LocationMap/ItemApplier so the two sides share identical IDs.
"""

import importlib.util
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA_PY = os.path.join(ROOT, "what_the_golf", "data.py")
OUT = os.path.join(ROOT, "mod", "ids.json")


def _load_data():
    spec = importlib.util.spec_from_file_location("wtg_data", DATA_PY)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    data = _load_data()
    # scene -> area name, so the mod can gate each overworld goal by its area.
    area_by_scene = {
        level.scene: area.name for area in data.AREAS for level in area.levels
    }
    payload = {
        "game": "WHAT THE GOLF?",
        "start_area": data.START_AREA,
        "items": data.item_name_to_id,
        "locations": data.location_name_to_id,
        "area_by_scene": area_by_scene,
    }
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2)
    print(f"wrote {OUT}: {len(payload['items'])} items, {len(payload['locations'])} locations")


if __name__ == "__main__":
    main()
