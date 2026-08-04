"""Watch the mod's "which area am I in" data-storage key on a running AP server.

This is the server's-eye view of what CurrentArea publishes and what a PopTracker
pack reads back -- the same Get + SetNotify a tracker does, minus the UI. Useful
for validating the map auto-switch without clicking anything, and for debugging a
tracker that isn't following the player.

    python tools/watch_area_key.py                  # localhost:38281, slot Player1
    python tools/watch_area_key.py myslot 1         # slot name, team

Prints one line per update:
    20:41:07  area=08  subarea=08C  campaign=Main  in_level=1  src=scene  scene=SpaceGolf6
"""
import asyncio
import json
import sys
import uuid
from datetime import datetime

import websockets

URI = "ws://localhost:38281"
GAME = "WHAT THE GOLF?"
SLOT = sys.argv[1] if len(sys.argv) > 1 else "Player1"
TEAM = int(sys.argv[2]) if len(sys.argv) > 2 else 0

# Layout of the pipe-delimited payload (see mod/src/Mapping/CurrentArea.cs). A
# string rather than JSON because the mod's Newtonsoft and the AP client
# library's are different assemblies, and PopTracker's Lua has no JSON parser.
FIELDS = ("v", "area", "subarea", "campaign", "in_level", "src", "scene")


def show(value):
    stamp = datetime.now().strftime("%H:%M:%S")
    if not isinstance(value, str) or not value:
        print(f"{stamp}  <empty / not set>")
        return
    parts = value.split("|")
    named = dict(zip(FIELDS, parts))
    extra = parts[len(FIELDS):]
    line = "  ".join(f"{k}={named.get(k, '')}" for k in FIELDS[1:])
    print(f"{stamp}  v{named.get('v', '?')}  {line}"
          + (f"  (+{len(extra)} unknown fields)" if extra else ""))


async def main():
    key = f"WTG:CurrentArea:{TEAM}:{{slot}}"
    async with websockets.connect(URI, max_size=None) as ws:
        await ws.recv()                                   # RoomInfo
        await ws.send(json.dumps([{
            "cmd": "Connect", "password": None, "game": GAME, "name": SLOT,
            "uuid": str(uuid.uuid4()),
            "version": {"major": 0, "minor": 6, "build": 7, "class": "Version"},
            "items_handling": 0, "tags": ["Tracker"], "slot_data": False,
        }]))

        while True:
            for msg in json.loads(await ws.recv()):
                cmd = msg.get("cmd")
                if cmd == "ConnectionRefused":
                    print("connection refused:", msg.get("errors"))
                    return
                if cmd == "Connected":
                    key = key.format(slot=msg["slot"])
                    print(f"connected as {SLOT} (team {TEAM}, slot {msg['slot']})")
                    print(f"watching {key}\n")
                    # Get for the current value, SetNotify for every later change.
                    await ws.send(json.dumps([
                        {"cmd": "Get", "keys": [key]},
                        {"cmd": "SetNotify", "keys": [key]},
                    ]))
                elif cmd == "Retrieved":
                    show(msg.get("keys", {}).get(key))
                elif cmd == "SetReply" and msg.get("key") == key:
                    show(msg.get("value"))


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
