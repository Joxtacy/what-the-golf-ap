from BaseClasses import Region, ItemClassification

from .data import gates, final_boss_gate, clear_loc, crown_loc
from .Items import WTGItem
from .Locations import WTGLocation, location_name_to_id


def _add_location(region, name):
    region.locations.append(
        WTGLocation(region.player, name, location_name_to_id[name], region)
    )


def create_regions(world) -> None:
    player = world.player
    multiworld = world.multiworld
    mode = world.area_access_mode()

    menu = Region("Menu", player, multiworld)
    multiworld.regions.append(menu)

    # NON-LINEAR: the game lets you teleport (pause menu) / portal-room travel to
    # any UNLOCKED gate, so each gate (chamber OR sub-area, per the area_access
    # option) is an independent region gated only by its own Access item
    # (Rules.py). The mod opens the matching in-game door(s) when the Access item
    # arrives (SaveGame.Set*DoorOpen + RefreshDoorsAndGoals). The start connects
    # freely.
    for name, _access, levels in gates(mode):
        region = Region(name, player, multiworld)
        multiworld.regions.append(region)

        for level in levels:
            _add_location(region, clear_loc(level.scene))
            if level.challenges > 0:
                _add_location(region, crown_loc(level.scene))

        menu.connect(region, f"To {name}")

    # Campaign victory: an internal event placed in the Final boss region, so it's
    # reachable exactly when that region is accessible.
    final_region = multiworld.get_region(final_boss_gate(mode), player)
    victory = WTGLocation(player, "Campaign Complete", None, final_region)
    victory.place_locked_item(
        WTGItem("Victory", ItemClassification.progression, None, player)
    )
    final_region.locations.append(victory)
