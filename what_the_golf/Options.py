from dataclasses import dataclass

from Options import (
    Choice, Range, Toggle, OptionSet, DeathLink, PerGameCommonOptions,
)


class Goal(Choice):
    """How the multiworld is won.

    campaign: reach and beat the Final boss (finish the campaign path).
    all_bosses: defeat EVERY campaign boss -- the 7 computer HoleInOne bosses
        AND the Final boss, not just the last one. Because the bosses sit deep
        in chambers 08/07/06/05/03/01/00, this forces far more Access (and, with
        the boss_keys option, boss) keys into logic -- deeper, more spread-out
        progression than campaign, which one long chain can satisfy.
    door_50 / door_75 / door_100: collect that percentage of all Flags,
        mirroring the game's 50% / 75% / 100% completion doors.
    """
    display_name = "Goal"
    option_campaign = 0
    option_door_50 = 1
    option_door_75 = 2
    option_door_100 = 3
    option_all_bosses = 4
    default = 0


class AreaAccess(Choice):
    """Granularity of the Access keys that gate progression.

    section: one key per in-game sub-area unlock (17 keys) — finer, more
        progression items spread across the multiworld (recommended).
    chamber: one key per chamber (10 keys) — coarser, closer to the vanilla
        overworld structure.

    NOTE on 'section': the game hard-gates chamber-to-chamber (the computer/boss
    doors), but sub-areas WITHIN one chamber share an open overworld room. So with
    section access you can physically walk to a locked sibling sub-area once any
    sub-area of that chamber is reachable (chambers 03/04/07/08/09 have multiple
    sub-areas). This is out-of-logic (logic still requires each key) but never a
    softlock — you simply *can* play ahead if you choose. 'chamber' has no such
    looseness. Fused units (05A/B/C, 06A/B) share one in-game door and always
    unlock together.
    """
    display_name = "Area Access"
    option_section = 0
    option_chamber = 1
    default = 0


class BossKeys(Toggle):
    """Gate the computer boss holes behind keys.

    When on, each of the 7 campaign computer bosses (Computers 1, 2, 3, 4, 5, 7,
    8) needs its "Computer N Key" before you can fight/clear it — the mod holds
    that computer's door shut until the key arrives (a hard gate: the computer
    doors are real walls). Adds 7 progression items and, since bosses sit deep in
    the chambers, pushes more keys into logic. The Final boss is not keyed (it's
    gated by the campaign goal).
    """
    display_name = "Boss Keys"


class HardSections(Toggle):
    """Physically hard-lock not-yet-unlocked sub-areas within a chamber.

    Only matters with 'section' area access. Sub-areas inside one chamber share an
    open overworld room, so by default you *can* walk into a locked sibling sub-area
    once any sibling is reachable (out of logic, never a softlock — see Area Access).
    Turn this on and the mod holds the connecting door shut until that sub-area's
    key arrives, so section access is a real physical gate too. No effect on the
    item pool or logic; keyed sub-areas stay directly teleport-reachable regardless.
    Under 'chamber' access this does nothing (a chamber's sub-areas unlock together).
    """
    display_name = "Hard Sub-area Locks"


class Crowns(Toggle):
    """Add the overworld crown chests as checks.

    When on, every crown chest in the overworld becomes an AP location (24 checks).
    Most of them are crown-locked behind a door in the vanilla game; for those the
    mod holds the door shut and instead hands you a "<Area> Chest Key" through the
    multiworld -- so the chest opens on the KEY, not on a crown count. The handful
    of chests that are freely reachable in vanilla (Kitchen, the sawable chest, a
    couple of secret ones) stay free: they're checks, but need no key. Adds 18
    progression keys spread across the multiworld.
    """
    display_name = "Crown Chests"


class Traps(Toggle):
    """Add trap items to the pool.

    When on, up to `trap_count` filler items are replaced by randomly-chosen
    trap items. A received trap fires a short, disruptive/funny effect in the
    game mod -- e.g. force-restarting your current hole, briefly slowing down or
    speeding up game time, or randomizing your overworld ball's shape. Traps are
    filler-class for logic (they never gate progression), so this only changes
    the *flavour* of your filler, not what's required to beat the seed.
    """
    display_name = "Traps"


class TrapPercentage(Range):
    """What percentage of your filler items to replace with traps (when Traps is on).

    Applied to however many filler slots the seed actually has, so it scales
    automatically as the game gains more checks -- no fixed count to retune. 0
    means no traps even if Traps is on; 100 turns every filler item into a trap.
    Which trap types you get is random. No effect on generation logic.
    """
    display_name = "Trap Percentage"
    range_start = 0
    range_end = 100
    default = 20


class Episodes(OptionSet):
    """Which extra episodes (DLC) to include, on top of the base campaign.

    Each listed episode adds its holes as checks (a Clear each, plus a Crown for
    the rare hole that has one) and a single "<Episode> Episode Access" key that
    gates them in logic. Episode hole-clears also grant Flags, so they count
    toward the door_50/75/100 goals (bigger board = bigger % target).

    Requires owning the corresponding DLC. Valid entries: "Sporty Sports", "Snow",
    "Hotdog", "Alive", "Among Us". Default: none (base campaign only).

    NOTE: in-game enforcement of episode access is not wired yet, so an episode
    key is a LOGIC gate only for now -- like the sub-area "physical looseness" you
    can already walk past; never a softlock.
    """
    display_name = "Episodes"
    valid_keys = frozenset({"Sporty Sports", "Snow", "Hotdog", "Alive", "Among Us"})


class DeathLinkAmnesty(Range):
    """How many of your own wipes it takes to send one DeathLink (Death Link Amnesty).

    Only matters when Death Link is on. "Death" in WTG = a level FAILURE (ball out
    of bounds / in water / lost); manual restarts and quits don't count. Because
    wiping is constant in this game, sending every wipe would spam the multiworld,
    so the mod broadcasts one DeathLink per this many local wipes (e.g. 30 = only
    every 30th wipe sends). 1 = every wipe sends a death. A wipe caused by an
    INCOMING death is never counted, so received deaths can't feed back. No effect
    on generation logic.
    """
    display_name = "Death Link Amnesty"
    range_start = 1
    range_end = 30
    default = 10


@dataclass
class WTGOptions(PerGameCommonOptions):
    goal: Goal
    area_access: AreaAccess
    boss_keys: BossKeys
    hard_sections: HardSections
    crowns: Crowns
    episodes: Episodes
    traps: Traps
    trap_percentage: TrapPercentage
    # Implemented in the game mod. "Death" = a level FAILURE (ball out of bounds /
    # in water / lost), reported via GameAnalytics.OnLevelReset -- manual restarts
    # and quits are excluded. The mod broadcasts one DeathLink per `death_link_amnesty`
    # local wipes (see that option); a wipe caused by an incoming death is never
    # re-broadcast. An incoming death restarts your current hole (or is dropped if
    # you're in the overworld). No effect on generation logic.
    death_link: DeathLink
    death_link_amnesty: DeathLinkAmnesty
