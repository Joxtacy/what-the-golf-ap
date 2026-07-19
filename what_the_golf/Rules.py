from math import ceil

from worlds.generic.Rules import set_rule

from .data import AREAS, START_AREA, access_item
from .Items import flag_pool


def set_rules(world) -> None:
    player = world.player
    multiworld = world.multiworld

    # Entrance gating: each area needs its Access item (except the start area).
    for area in AREAS:
        if area.name == START_AREA:
            continue
        entrance = multiworld.get_entrance(f"To {area.name}", player)
        key = access_item(area.name)
        set_rule(entrance, lambda state, k=key: state.has(k, player))

    # Completion condition depends on the chosen goal.
    goal = world.options.goal
    if goal == goal.option_campaign:
        multiworld.completion_condition[player] = \
            lambda state: state.has("Victory", player)
    else:
        pct = {
            goal.option_door_50: 0.5,
            goal.option_door_75: 0.75,
            goal.option_door_100: 1.0,
        }[goal.value]
        need = max(1, ceil(flag_pool() * pct))
        multiworld.completion_condition[player] = \
            lambda state, n=need: state.has("Flag", player, n)
