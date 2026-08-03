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
SECTIONS = os.path.join(ROOT, "mod", "wtg_sections.json")
OUT = os.path.join(ROOT, "mod", "ids.json")

# Episode campaign tag (ECampaignType) -> the episode's AP display name. The mod
# reads CampaignInfo.Current() and needs to name the episode the player is in.
EPISODE_BY_CAMPAIGN = {
    "Olympics": "Sporty Sports",
    "Snow": "Snow",
    "Hotdog": "Hotdog",
    "Alive": "Alive",
    "Amongus": "Among Us",
}


def _load_data():
    spec = importlib.util.spec_from_file_location("wtg_data", DATA_PY)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _area_maps(data):
    """Lookups the mod's CurrentArea uses to answer "which area am I in", so a
    PopTracker pack can auto-switch to that chamber's map tab.

    Three granularities because the mod has three signals of differing quality:
    the scene while you're inside a hole, the SavePosition respawn id while
    you're in the overworld, and the campaign tag while you're in an episode.
    """
    world = data._read_levels_json()
    subarea_by_scene = {
        lv["scene"]: (lv.get("subarea") or "")
        for area in world["areas"] for lv in area["levels"]
    }
    chamber_by_subarea = {sa: sa[:2] for sa in sorted(set(subarea_by_scene.values())) if sa}
    # saveSpotId -> sub-area. The 21 spots are 1:1 with the 21 sub-areas, so this
    # is exact (if lagging -- it is the last respawn point, not where you stand).
    # Entries with no campaign tag predate the campaign-aware dumper = Main.
    with open(SECTIONS, encoding="utf-8") as f:
        sections = json.load(f)
    subarea_by_savespot = {
        s["saveSpotId"]: s["name"] for s in sections
        if s.get("saveSpotId") and s.get("campaign", "Main") == "Main"
    }
    return subarea_by_scene, chamber_by_subarea, subarea_by_savespot


def main():
    data = _load_data()
    # scene -> area name, so the mod can gate each overworld goal by its area.
    area_by_scene = {
        level.scene: area.name for area in data.AREAS for level in area.levels
    }
    subarea_by_scene, chamber_by_subarea, subarea_by_savespot = _area_maps(data)
    # access-item name -> in-game unlockTriggerId(s) it opens. The mod applies any
    # received "* Access" item by opening these doors, so it works identically for
    # chamber-granularity and section-granularity seeds.
    unlocks_by_item = data.unlocks_by_item()
    # boss-key item name -> boss hole LevelData.ID. The mod suppresses the
    # computer door whose bossLevelID matches, until this key arrives.
    boss_by_item = data.boss_key_to_level_id()
    # Every boss scene (7 computers + final) + the final boss scene, so the mod's
    # all_bosses goal knows which clears count and when all are down.
    boss_scenes = list(data.all_boss_scenes())
    # crowns option. chest_doors_by_item: gated chest key -> crown-door OverworldID
    # the mod holds shut until the key arrives. chest_loc_by_oid: chest
    # OverworldID.ID -> its AP location name (the mod sends this check when the
    # chest opens).
    chest_doors_by_item = data.chest_doors_by_item()
    chest_loc_by_oid = data.chest_loc_by_oid()
    # scene -> display name. The mod detects a completed level by its raw scene,
    # then uses this to build the (renamed) location name "<display> - Clear/Crown".
    name_by_scene = data.name_by_scene()
    # episodes (DLC) enforcement. episode_pack_by_item: Access item -> in-game
    # ContentPack id the mod unlocks when it arrives. episode_pack_by_name: episode
    # display name -> pack id, so the mod maps the seed's enabled episodes (slot
    # data) to the packs it hard-gates via the LoadOverworld veto.
    episode_pack_by_item = data.episode_pack_by_item()
    episode_pack_by_name = data.episode_pack_by_name()
    payload = {
        "game": "WHAT THE GOLF?",
        "start_area": data.START_AREA,
        "items": data.item_name_to_id,
        "locations": data.location_name_to_id,
        "name_by_scene": name_by_scene,
        "area_by_scene": area_by_scene,
        # --- CurrentArea lookups (tracker map auto-switch) ---
        "subarea_by_scene": subarea_by_scene,
        "chamber_by_subarea": chamber_by_subarea,
        "subarea_by_savespot": subarea_by_savespot,
        "episode_by_campaign": EPISODE_BY_CAMPAIGN,
        "unlocks_by_item": unlocks_by_item,
        "boss_by_item": boss_by_item,
        "boss_scenes": boss_scenes,
        "final_boss_scene": data.FINAL_BOSS_SCENE,
        "chest_doors_by_item": chest_doors_by_item,
        "chest_loc_by_oid": chest_loc_by_oid,
        "episode_pack_by_item": episode_pack_by_item,
        "episode_pack_by_name": episode_pack_by_name,
    }
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2)
    print(f"wrote {OUT}: {len(payload['items'])} items, "
          f"{len(payload['locations'])} locations, "
          f"{len(unlocks_by_item)} access->door maps, "
          f"{len(boss_by_item)} boss keys, "
          f"{len(boss_scenes)} boss scenes, "
          f"{len(chest_loc_by_oid)} chests ({len(chest_doors_by_item)} keyed), "
          f"{len(episode_pack_by_name)} episode packs, "
          f"{len(subarea_by_savespot)} save spots")


if __name__ == "__main__":
    main()
