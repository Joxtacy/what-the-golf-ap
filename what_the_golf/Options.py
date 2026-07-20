from dataclasses import dataclass

from Options import Choice, Toggle, DeathLink, PerGameCommonOptions


class Goal(Choice):
    """How the multiworld is won.

    campaign: reach the Final boss area (finish the campaign path).
    door_50 / door_75 / door_100: collect that percentage of all Flags,
    mirroring the game's 50% / 75% / 100% completion doors.
    """
    display_name = "Goal"
    option_campaign = 0
    option_door_50 = 1
    option_door_75 = 2
    option_door_100 = 3
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

    When on, each of the 7 campaign computer bosses (Computers 1, 2, 3, 4, 7, 8,
    9) needs its "Computer N Key" before you can fight/clear it — the mod holds
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


@dataclass
class WTGOptions(PerGameCommonOptions):
    goal: Goal
    area_access: AreaAccess
    boss_keys: BossKeys
    hard_sections: HardSections
    death_link: DeathLink
