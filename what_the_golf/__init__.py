from BaseClasses import Tutorial
from worlds.AutoWorld import World, WebWorld

from .Options import WTGOptions
from .Items import (
    WTGItem, item_name_to_id, item_classification,
    access_items_for, BOSS_KEY_ITEMS, CHEST_KEY_ITEMS, TRAP_ITEMS,
    FLAG_ITEM, FILLER_ITEMS, flag_pool,
)
from .data import CHAMBER, SECTION
from .Locations import location_name_to_id
from .Regions import create_regions
from .Rules import set_rules, flag_goal


class WTGWeb(WebWorld):
    theme = "partyTime"
    tutorials = [Tutorial(
        "Multiworld Setup Guide",
        "A guide to setting up WHAT THE GOLF? for Archipelago.",
        "English",
        "setup_en.md",
        "setup/en",
        ["Joxtacy"],
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

    # Universal Tracker: this world can be rebuilt from slot_data alone, so UT
    # doesn't need the player's YAML in its Players folder -- it regenerates us
    # and feeds slot_data back through multiworld.re_gen_passthrough (read in
    # generate_early below) + interpret_slot_data.
    ut_can_gen_without_yaml = True

    item_name_to_id = item_name_to_id
    location_name_to_id = location_name_to_id

    # -- generation pipeline --------------------------------------------------

    def generate_early(self) -> None:
        # Universal Tracker re-runs generation to compute logic and passes the
        # connected seed's slot_data via multiworld.re_gen_passthrough. Apply it
        # HERE (before regions/items/rules) so the regenerated world matches the
        # real seed. Absent during normal generation (attribute doesn't exist).
        passthrough = getattr(self.multiworld, "re_gen_passthrough", None)
        if passthrough and self.game in passthrough:
            self._apply_slot_data(passthrough[self.game])

    def area_access_mode(self) -> str:
        """'section' or 'chamber' -- the granularity of the Access keys."""
        return CHAMBER if self.options.area_access.value == \
            self.options.area_access.option_chamber else SECTION

    def create_regions(self) -> None:
        create_regions(self)

    def create_item(self, name: str) -> WTGItem:
        return WTGItem(name, item_classification(name), self.item_name_to_id[name], self.player)

    def create_items(self) -> None:
        pool = []

        # Progression: one Access key per gate, per the chosen granularity.
        for name in access_items_for(self.area_access_mode()):
            pool.append(self.create_item(name))

        # Progression: computer boss keys (only when enabled).
        if self.options.boss_keys.value:
            for name in BOSS_KEY_ITEMS:
                pool.append(self.create_item(name))

        # Progression: crown-chest keys (only when the crowns option is enabled).
        if self.options.crowns.value:
            for name in CHEST_KEY_ITEMS:
                pool.append(self.create_item(name))

        # Progression: one Flag per hole (counted for the % goals).
        for _ in range(flag_pool()):
            pool.append(self.create_item(FLAG_ITEM))

        # Fill the remainder so pool size == number of real (non-event) checks.
        total_locations = sum(
            1 for loc in self.multiworld.get_locations(self.player) if loc.address is not None
        )
        filler_needed = total_locations - len(pool)

        # Traps replace a PERCENTAGE of the filler slots (self-scaling as the game
        # gains checks). Pool size is unchanged and logic is unaffected. Which traps
        # is random but deterministic (self.random is seeded per player) -- so
        # Universal Tracker's regen reproduces the exact same pool.
        n_traps = 0
        if self.options.traps.value and filler_needed > 0:
            n_traps = filler_needed * self.options.trap_percentage.value // 100
        for _ in range(n_traps):
            pool.append(self.create_item(self.random.choice(TRAP_ITEMS)))

        for i in range(filler_needed - n_traps):
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
            "area_access": self.area_access_mode(),
            "boss_keys": bool(self.options.boss_keys.value),
            "hard_sections": bool(self.options.hard_sections.value),
            "crowns": bool(self.options.crowns.value),
            "traps": bool(self.options.traps.value),
            "trap_percentage": int(self.options.trap_percentage.value),
            "death_link": bool(self.options.death_link.value),
            "death_link_amnesty": int(self.options.death_link_amnesty.value),
            # Flags needed to win a door_50/75/100 goal (0 for campaign/all_bosses).
            # Purely informational -- the mod's Flag HUD counts toward it; the real
            # win condition is set_rules' completion_condition. Not applied in
            # _apply_slot_data (no generation effect), so UT ignores it.
            "flag_goal": flag_goal(self.options.goal),
        }

    def _apply_slot_data(self, slot_data: dict) -> None:
        """Restore the options that shape the item/location layout & logic (goal,
        area_access, boss_keys, crowns) from slot_data. hard_sections/death_link
        have no generation effect but are restored for completeness."""
        o = self.options
        o.goal.value = slot_data["goal"]
        # area_access is serialized as the string "section"/"chamber" (not the
        # Choice int), so map it back.
        o.area_access.value = (
            o.area_access.option_chamber
            if slot_data["area_access"] == CHAMBER
            else o.area_access.option_section
        )
        o.boss_keys.value = int(slot_data["boss_keys"])
        o.crowns.value = int(slot_data["crowns"])
        o.hard_sections.value = int(slot_data["hard_sections"])
        # traps/trap_percentage change the pool composition, so UT's regen must
        # apply them to reproduce the same item pool. .get() keeps older seeds valid.
        o.traps.value = int(slot_data.get("traps", 0))
        o.trap_percentage.value = int(slot_data.get("trap_percentage", 0))
        o.death_link.value = int(slot_data["death_link"])

    def interpret_slot_data(self, slot_data: dict) -> dict:
        """Universal Tracker hook. UT re-runs generation locally (with no YAML,
        only slot_data) to compute which locations are in logic. Returning truthy
        tells UT to regenerate with slot_data threaded through re_gen_passthrough,
        which generate_early applies -- that regen is the authoritative one."""
        self._apply_slot_data(slot_data)
        return slot_data
