from BaseClasses import Region, ItemClassification

from .data import (
    gates, final_boss_gate, clear_loc, crown_loc, CHESTS, chest_loc, chest_region,
    episode_gates,
)
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

    # Episodes (DLC): each enabled episode is one region gated by its own
    # "<Episode> Episode Access" key (Rules.py), connected freely from Menu (like
    # the other gates -- non-linear). Holds that episode's Clear/Crown checks.
    for name, _access, levels in episode_gates(world.enabled_episodes()):
        region = Region(name, player, multiworld)
        multiworld.regions.append(region)
        for level in levels:
            _add_location(region, clear_loc(level.scene))
            if level.challenges > 0:
                _add_location(region, crown_loc(level.scene))
        menu.connect(region, f"To {name}")

    # Crown chests (crowns option): each chest is a location in its sub-area's
    # region, so the region's Access rule gates reaching it. Gated chests get an
    # extra key requirement in Rules.py.
    if world.options.crowns.value:
        for chest in CHESTS:
            region = multiworld.get_region(chest_region(chest, mode), player)
            _add_location(region, chest_loc(chest.display))

    # Campaign victory: an internal event placed in the Final boss region, so it's
    # reachable exactly when that region is accessible.
    final_region = multiworld.get_region(final_boss_gate(mode), player)
    victory = WTGLocation(player, "Campaign Complete", None, final_region)
    victory.place_locked_item(
        WTGItem("Victory", ItemClassification.progression, None, player)
    )
    final_region.locations.append(victory)
