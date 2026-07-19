from BaseClasses import Tutorial
from worlds.AutoWorld import World, WebWorld

from .Options import WTGOptions
from .Items import (
    WTGItem, item_name_to_id, item_classification,
    ACCESS_ITEMS, FLAG_ITEM, FILLER_ITEMS, flag_pool,
)
from .Locations import location_name_to_id
from .Regions import create_regions
from .Rules import set_rules


class WTGWeb(WebWorld):
    theme = "partyTime"
    tutorials = [Tutorial(
        "Multiworld Setup Guide",
        "A guide to setting up WHAT THE GOLF? for Archipelago.",
        "English",
        "setup_en.md",
        "setup/en",
        ["YourName"],
    )]


class WTGWorld(World):
    """WHAT THE GOLF? -- a comedic game where anything can be golf. This world
    randomizes access to themed areas of the lab, so you golf your way through
    an unfamiliar order, receiving the keys to new areas from the multiworld."""

    game = "WHAT THE GOLF?"
    web = WTGWeb()

    options_dataclass = WTGOptions
    options: WTGOptions

    topology_present = True

    item_name_to_id = item_name_to_id
    location_name_to_id = location_name_to_id

    # -- generation pipeline --------------------------------------------------

    def create_regions(self) -> None:
        create_regions(self)

    def create_item(self, name: str) -> WTGItem:
        return WTGItem(name, item_classification(name), self.item_name_to_id[name], self.player)

    def create_items(self) -> None:
        pool = []

        # Progression: one Access key per non-start area.
        for name in ACCESS_ITEMS:
            pool.append(self.create_item(name))

        # Progression: one Flag per hole (counted for the % goals).
        for _ in range(flag_pool()):
            pool.append(self.create_item(FLAG_ITEM))

        # Fill the remainder so pool size == number of real (non-event) checks.
        total_locations = sum(
            1 for loc in self.multiworld.get_locations(self.player) if loc.address is not None
        )
        for i in range(total_locations - len(pool)):
            pool.append(self.create_item(FILLER_ITEMS[i % len(FILLER_ITEMS)]))

        self.multiworld.itempool += pool

    def set_rules(self) -> None:
        set_rules(self)

    # -- helpers --------------------------------------------------------------

    def get_filler_item_name(self) -> str:
        return FILLER_ITEMS[0]

    def fill_slot_data(self) -> dict:
        return {
            "goal": self.options.goal.value,
            "death_link": bool(self.options.death_link.value),
        }
