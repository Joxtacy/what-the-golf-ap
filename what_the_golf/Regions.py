from BaseClasses import Region, ItemClassification

from .data import AREAS, START_AREA, FINAL_BOSS_SCENE, final_boss_area, clear_loc, crown_loc
from .Items import WTGItem
from .Locations import WTGLocation, location_name_to_id


def _add_location(region, name):
    region.locations.append(
        WTGLocation(region.player, name, location_name_to_id[name], region)
    )


def create_regions(world) -> None:
    player = world.player
    multiworld = world.multiworld

    menu = Region("Menu", player, multiworld)
    multiworld.regions.append(menu)

    # NON-LINEAR: the game lets you teleport (pause menu) / portal-room travel to
    # any UNLOCKED chamber, so chambers are independent regions each gated only by
    # their own Access item (Rules.py). The mod unlocks a chamber for teleport when
    # its Access arrives (SaveGame.SetMainDoorOpen + RefreshDoorsAndGoals). The
    # start chamber connects freely.
    for area in AREAS:
        region = Region(area.name, player, multiworld)
        multiworld.regions.append(region)

        for level in area.levels:
            _add_location(region, clear_loc(level.scene))
            if level.challenges > 0:
                _add_location(region, crown_loc(level.scene))

        menu.connect(region, f"To {area.name}")

    # Campaign victory: an internal event placed in the Final boss area, so it's
    # reachable exactly when that area is accessible.
    final_region = multiworld.get_region(final_boss_area(), player)
    victory = WTGLocation(player, "Campaign Complete", None, final_region)
    victory.place_locked_item(
        WTGItem("Victory", ItemClassification.progression, None, player)
    )
    final_region.locations.append(victory)
