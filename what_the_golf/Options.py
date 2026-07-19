from dataclasses import dataclass

from Options import Choice, DeathLink, PerGameCommonOptions


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


@dataclass
class WTGOptions(PerGameCommonOptions):
    goal: Goal
    death_link: DeathLink
