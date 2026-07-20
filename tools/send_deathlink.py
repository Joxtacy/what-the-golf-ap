"""Fire a single DeathLink into a running AP server, for testing WTG's incoming
DeathLink handling. Connects as a second DeathLink-tagged client (items_handling=0,
like a tracker) and sends a Bounce with a foreign `source` so the game's client
(which ignores its own deaths) will act on it.

Usage: python send_deathlink.py [server_name] [cause]
Defaults connect to ws://localhost:38281 as slot "Player1".
"""
import asyncio
import json
import sys
import uuid

import websockets

URI = "ws://localhost:38281"
SLOT = "Player1"                 # a valid slot in the hosted seed
GAME = "WHAT THE GOLF?"
SOURCE = sys.argv[1] if len(sys.argv) > 1 else "Grim Reaper"
CAUSE = sys.argv[2] if len(sys.argv) > 2 else f"{SOURCE} sent you a DeathLink"


async def main():
    async with websockets.connect(URI, max_size=None) as ws:
        # 1. RoomInfo arrives first.
        room = json.loads(await ws.recv())
        print("<-", room[0]["cmd"] if room else room)

        # 2. Connect as a DeathLink-tagged tracker (no item handling).
        await ws.send(json.dumps([{
            "cmd": "Connect",
            "password": None,
            "game": GAME,
            "name": SLOT,
            "uuid": str(uuid.uuid4()),
            "version": {"major": 0, "minor": 6, "build": 7, "class": "Version"},
            "items_handling": 0,
            "tags": ["DeathLink", "Tracker"],
            "slot_data": False,
        }]))

        # 3. Wait for Connected (or bail on refusal).
        while True:
            msg = json.loads(await ws.recv())
            cmd = msg[0]["cmd"]
            print("<-", cmd)
            if cmd == "Connected":
                break
            if cmd == "ConnectionRefused":
                print("REFUSED:", msg[0].get("errors"))
                return

        # 4. Bounce a DeathLink to every DeathLink-tagged client. `time` uses the
        #    server-provided clock so we don't depend on local time sync.
        now = room[0].get("time", 0.0)
        await ws.send(json.dumps([{
            "cmd": "Bounce",
            "tags": ["DeathLink"],
            "data": {"time": now, "cause": CAUSE, "source": SOURCE},
        }]))
        print("-> DeathLink sent:", CAUSE)

        # Give the server a moment to relay before we disconnect.
        await asyncio.sleep(1.0)


asyncio.run(main())
