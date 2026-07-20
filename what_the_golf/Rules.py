from math import ceil

from worlds.generic.Rules import set_rule

from .data import gates, BOSS_HOLES, boss_key_item, clear_loc, crown_loc
from .Items import flag_pool


def set_rules(world) -> None:
    player = world.player
    multiworld = world.multiworld
    mode = world.area_access_mode()

    # Entrance gating: each gate needs its Access item (the free start has none).
    for name, access, _levels in gates(mode):
        if access is None:
            continue
        entrance = multiworld.get_entrance(f"To {name}", player)
        set_rule(entrance, lambda state, k=access: state.has(k, player))

    # Boss-key gating: a boss hole's Clear (and Crown, if any) also needs its
    # "Computer N Key" -- on top of the region's Access rule.
    if world.options.boss_keys.value:
        for level, n in BOSS_HOLES:
            key = boss_key_item(n)
            names = [clear_loc(level.scene)]
            if level.challenges > 0:
                names.append(crown_loc(level.scene))
            for loc_name in names:
                loc = multiworld.get_location(loc_name, player)
                set_rule(loc, lambda state, k=key: state.has(k, player))

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
