from math import ceil

from worlds.generic.Rules import set_rule, add_rule

from .data import (
    gates, BOSS_HOLES, boss_key_item, clear_loc, crown_loc, all_boss_scenes,
    CHESTS, chest_loc, chest_key_item, boss_scene_for_computer, episode_gates,
    boss_chamber_access_items, SECTION,
)
from .Items import flag_pool


def flag_goal(world) -> int:
    """Flags required to win, given the `goal` option; 0 for non-door goals.

    Shared by set_rules (the actual completion condition) and fill_slot_data (the
    number the in-game Flag HUD counts toward) so the displayed target can never
    drift from the real win condition. The Flag pool -- and hence the target --
    includes the enabled episodes' holes.
    """
    goal = world.options.goal
    pct = {
        goal.option_door_50: 0.5,
        goal.option_door_75: 0.75,
        goal.option_door_100: 1.0,
    }.get(goal.value)
    if pct is None:
        return 0
    return max(1, ceil(flag_pool(world.enabled_episodes()) * pct))


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

    # Episode entrances: each enabled episode needs its own Episode Access key.
    for name, access, _levels in episode_gates(world.enabled_episodes()):
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

    # Boss reachability with boss_keys OFF (section granularity only): the computer
    # door lights only once its whole chamber's sub-areas are natively completed (the
    # mod's BossPlateSync heals the plates), so a boss's Clear/Crown needs EVERY
    # sub-area Access key of its chamber -- not just the section the boss hole sits in.
    # Without this the tracker shows a boss in logic with one sub-area key (e.g.
    # Computer 1 with just Platformers, though it needs all of chamber 08). Skipped
    # when boss_keys is on (BossGate force-lights on key) or under chamber granularity
    # (the one chamber key already covers all sub-areas; the section keys don't exist).
    elif mode == SECTION:
        for level, _n in BOSS_HOLES:
            keys = boss_chamber_access_items(level)
            if not keys:
                continue
            names = [clear_loc(level.scene)]
            if level.challenges > 0:
                names.append(crown_loc(level.scene))
            for loc_name in names:
                loc = multiworld.get_location(loc_name, player)
                add_rule(loc, lambda state, ks=keys: state.has_all(ks, player))

    # Crown-chest gating. Two independent requirements can apply on top of the
    # chest's region Access rule:
    #   * gated  -> its "<Area> Chest Key" (behind a crown door). Free chests skip.
    #   * boss   -> the chest physically sits past a chamber's computer boss, so it
    #     also needs that boss beatable. can_reach_location on the boss's Clear
    #     folds in the boss's own region access + its Computer key (if boss_keys),
    #     regardless of granularity. Without this the chest reads as in-logic with
    #     just its sub-area key (e.g. the Lebowski secret behind Computer 2).
    if world.options.crowns.value:
        for chest in CHESTS:
            loc = multiworld.get_location(chest_loc(chest.display), player)
            if chest.gated:
                key = chest_key_item(chest.display)
                set_rule(loc, lambda state, k=key: state.has(k, player))
            if chest.boss:
                boss_scene = boss_scene_for_computer(chest.boss)
                if boss_scene is not None:
                    bclear = clear_loc(boss_scene)
                    add_rule(loc, lambda state, n=bclear:
                             state.can_reach_location(n, player))

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
        need = flag_goal(world)
        multiworld.completion_condition[player] = \
            lambda state, n=need: state.has("Flag", player, n)
