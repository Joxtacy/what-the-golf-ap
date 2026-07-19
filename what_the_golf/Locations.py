from BaseClasses import Location

from .data import location_name_to_id  # framework-free single source (data.py)


class WTGLocation(Location):
    game = "WHAT THE GOLF?"


# NOTE: the campaign-complete check is an internal EVENT (no id), created in
# Regions.py in the Final boss area and holding the locked "Victory" item.

__all__ = ["WTGLocation", "location_name_to_id"]
