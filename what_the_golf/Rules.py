from math import ceil

from worlds.generic.Rules import set_rule

from .data import (
    gates, BOSS_HOLES, boss_key_item, clear_loc, crown_loc, all_boss_scenes,
    CHESTS, chest_loc, chest_key_item,
)
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

    # Crown-chest gating: a crown-locked chest's location also needs its
    # "<Area> Chest Key" (on top of its region's Access rule). Free chests have no
    # key -- they're reachable as soon as their region is.
    if world.options.crowns.value:
        for chest in CHESTS:
            if not chest.gated:
                continue
            key = chest_key_item(chest.display)
            loc = multiworld.get_location(chest_loc(chest.display), player)
            set_rule(loc, lambda state, k=key: state.has(k, player))

    # Completion condition depends on the chosen goal.
    goal = world.options.goal
    if goal == goal.option_campaign:
        multiworld.completion_condition[player] = \
            lambda state: state.has("Victory", player)
    elif goal == goal.option_all_bosses:
        # Win = able to defeat every boss hole. A boss Clear is reachable exactly
        # when its region Access (and, if boss_keys is on, its Computer key) is
        # held -- those rules are already set above, so can_reach_location folds
        # both requirements in. Includes the Final boss, so all_bosses subsumes
        # the campaign goal.
        boss_clears = [clear_loc(s) for s in all_boss_scenes()]
        multiworld.completion_condition[player] = \
            lambda state, names=boss_clears: \
            all(state.can_reach_location(n, player) for n in names)
    else:
        pct = {
            goal.option_door_50: 0.5,
            goal.option_door_75: 0.75,
            goal.option_door_100: 1.0,
        }[goal.value]
        need = max(1, ceil(flag_pool() * pct))
        multiworld.completion_condition[player] = \
            lambda state, n=need: state.has("Flag", player, n)
