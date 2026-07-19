from BaseClasses import Item, ItemClassification

from .data import item_name_to_id, access_item_names, FLAG_ITEM, FILLER_ITEMS, num_holes

IC = ItemClassification


class WTGItem(Item):
    game = "WHAT THE GOLF?"


# One Access key per area (except the start area). Receiving a key unlocks that
# area in the randomizer -- the game mod is meant to gate the area until then.
ACCESS_ITEMS = access_item_names()


def item_classification(name: str) -> ItemClassification:
    if name in ACCESS_ITEMS:
        return IC.progression
    if name == FLAG_ITEM:
        # Counted for the % goals -> progression, no cross-player balancing.
        return IC.progression_skip_balancing
    return IC.filler


def flag_pool() -> int:
    """One Flag per hole clear."""
    return num_holes()


__all__ = ["WTGItem", "item_name_to_id", "item_classification",
           "ACCESS_ITEMS", "FLAG_ITEM", "FILLER_ITEMS", "flag_pool"]
