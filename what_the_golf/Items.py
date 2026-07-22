from BaseClasses import Item, ItemClassification

from .data import (
    item_name_to_id, access_item_names, chamber_access_names,
    section_access_names, boss_key_names, chest_key_names,
    FLAG_ITEM, FILLER_ITEMS, TRAP_ITEMS, num_holes, CHAMBER, SECTION,
)

IC = ItemClassification


class WTGItem(Item):
    game = "WHAT THE GOLF?"


# The full universe of Access keys (both granularities). item_name_to_id holds
# them all so IDs stay stable; a seed only CREATES the subset its option needs.
# Receiving a key unlocks that gate in the randomizer (the game mod opens the
# matching in-game door(s) -- see tools/export_ids.py unlocks_by_item).
ACCESS_ITEMS = access_item_names()

# Computer boss keys (added to the pool only when the boss_keys option is on).
BOSS_KEY_ITEMS = boss_key_names()

# Crown-chest keys (added to the pool only when the crowns option is on).
CHEST_KEY_ITEMS = chest_key_names()

# Trap items (added to the pool only when the traps option is on; they replace
# an equal number of filler slots -- see WTGWorld.create_items).
TRAP_ITEMS = TRAP_ITEMS


def access_items_for(mode: str) -> list:
    """The Access keys to actually put in the pool for the chosen granularity."""
    return section_access_names() if mode == SECTION else chamber_access_names()


def item_classification(name: str) -> ItemClassification:
    if name in ACCESS_ITEMS or name in BOSS_KEY_ITEMS or name in CHEST_KEY_ITEMS:
        return IC.progression
    if name == FLAG_ITEM:
        # Counted for the % goals -> progression, no cross-player balancing.
        return IC.progression_skip_balancing
    if name in TRAP_ITEMS:
        return IC.trap
    return IC.filler


def flag_pool() -> int:
    """One Flag per hole clear."""
    return num_holes()


__all__ = ["WTGItem", "item_name_to_id", "item_classification",
           "ACCESS_ITEMS", "BOSS_KEY_ITEMS", "CHEST_KEY_ITEMS", "TRAP_ITEMS",
           "access_items_for", "FLAG_ITEM", "FILLER_ITEMS", "flag_pool",
           "CHAMBER", "SECTION"]
