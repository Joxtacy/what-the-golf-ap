-- Exercises scripts/autotab.lua: payload parsing, area -> tab routing, the
-- check-inference fallback, connect-burst suppression, and the one-way handover
-- from fallback to the mod's data-storage signal.
--
-- Runs without a server: AutoTab.apply() is public precisely so the
-- data-storage path can be driven with no live connection (DS_KEY needs one).
--
-- See tools/poptracker_tests/README.md for how to run.

local pass, fail = 0, 0

local function check(label, got, want)
    if got == want then
        pass = pass + 1
    else
        fail = fail + 1
        print(string.format("AUTOTAB MISMATCH %s: expected %s got %s",
            label, tostring(want), tostring(got)))
    end
end

-- 1. Payload parsing. Layout: v|area|subarea|campaign|in_level|src|scene
local p = AutoTab.parse("1|08|08C|Main|1|scene|SpaceGolf6")
check("parse v", p and p.v, 1)
check("parse area", p and p.area, "08")
check("parse subarea", p and p.subarea, "08C")
check("parse campaign", p and p.campaign, "Main")
check("parse in_level", p and p.in_level, true)
check("parse src", p and p.src, "scene")
check("parse scene", p and p.scene, "SpaceGolf6")

local q = AutoTab.parse("1|Snow||Snow|0|campaign|")
check("episode payload area", q and q.area, "Snow")
check("episode payload empty subarea", q and q.subarea, "")
check("episode payload in_level false", q and q.in_level, false)

check("parse nil", AutoTab.parse(nil), nil)
check("parse empty", AutoTab.parse(""), nil)
check("parse non-string", AutoTab.parse(42), nil)
check("parse junk", AutoTab.parse("garbage"), nil)
check("parse v0 rejected", AutoTab.parse("0|08|08C|Main|1|scene|x"), nil)
-- A future mod may append fields; the leading ones must still read correctly.
local fwd = AutoTab.parse("2|03|03B|Main|0|ball|x|extra|more")
check("forward-compatible area", fwd and fwd.area, "03")

-- 2. Area -> tab routing.
check("sub-area routes to chamber", AutoTab.tab_for_area("08C"), "Chamber 08")
check("bare chamber routes", AutoTab.tab_for_area("08"), "Chamber 08")
check("fused sub-area routes", AutoTab.tab_for_area("05C"), "Chamber 05")
-- Not a real area code, but proves the numeric-prefix fallback: anything starting
-- with two digits still lands on that chamber rather than nowhere.
check("unknown code falls back to chamber", AutoTab.tab_for_area("05ABC"), "Chamber 05")
check("single-sub-area chamber", AutoTab.tab_for_area("01"), "Chamber 01")
check("intro chamber", AutoTab.tab_for_area("10"), "Chamber 10")
check("finale chamber", AutoTab.tab_for_area("00"), "Chamber 00")
check("episode routes to itself", AutoTab.tab_for_area("Snow"), "Snow")
check("multiword episode", AutoTab.tab_for_area("Sporty Sports"), "Sporty Sports")
check("unknown area", AutoTab.tab_for_area("ZZ9"), nil)
check("empty area", AutoTab.tab_for_area(""), nil)
check("nil area", AutoTab.tab_for_area(nil), nil)

-- 3. Fallback: infer the area from a location-check name. Needs onFrame to
--    commit, since switches are coalesced to one per frame.
AutoTab.onClear({})
AutoTab.onLocation(1, "08C: Space Golf 6 - Clear")
AutoTab.onFrame()
check("fallback from sub-area prefix", AutoTab.current_tab, "Chamber 08")

AutoTab.onLocation(2, "01: Desert 1 Chest")
AutoTab.onFrame()
check("fallback from chest name", AutoTab.current_tab, "Chamber 01")

AutoTab.onLocation(3, "03: Computer 7 (Basic) - Clear")
AutoTab.onFrame()
check("fallback from boss chamber prefix", AutoTab.current_tab, "Chamber 03")

AutoTab.onLocation(4, "Snow: House on Ice - Clear")
AutoTab.onFrame()
check("fallback from episode prefix", AutoTab.current_tab, "Snow")

-- 4. Connect-burst suppression: the replay of already-checked locations must not
--    fling the tab. More than 3 events in one frame is treated as a sync burst.
AutoTab.onClear({})
for i = 1, 6 do AutoTab.onLocation(i, "08C: Space Golf 6 - Clear") end
AutoTab.onFrame()
check("connect burst suppressed", AutoTab.current_tab, nil)
-- ...and a single check on a later frame still works.
AutoTab.onLocation(9, "07A: OL Golf 1 - Clear")
AutoTab.onFrame()
check("single check after burst", AutoTab.current_tab, "Chamber 07")

-- 5. Data-storage path wins, and permanently mutes the fallback.
AutoTab.onClear({})
AutoTab.apply("1|05|05B|Main|0|save|")
AutoTab.onFrame()
check("data storage sets tab", AutoTab.current_tab, "Chamber 05")
AutoTab.onLocation(1, "08C: Space Golf 6 - Clear")
AutoTab.onFrame()
check("fallback muted once mod speaks", AutoTab.current_tab, "Chamber 05")
AutoTab.apply("1|02|02|Main|0|save|")
AutoTab.onFrame()
check("data storage keeps updating", AutoTab.current_tab, "Chamber 02")

-- 6. A garbage payload must not clear a good tab or unmute the fallback.
AutoTab.apply("nonsense")
AutoTab.onFrame()
check("garbage payload ignored", AutoTab.current_tab, "Chamber 02")

-- 7. onClear resets, so reconnecting to a different slot starts clean.
AutoTab.onClear({})
check("onClear resets tab", AutoTab.current_tab, nil)
AutoTab.onLocation(1, "04B: Stealth Golf 1 - Clear")
AutoTab.onFrame()
check("fallback live again after reconnect", AutoTab.current_tab, "Chamber 04")

-- 8. Unparseable / unprefixed location names must not throw or switch.
AutoTab.onClear({})
AutoTab.onLocation(1, "no prefix here")
AutoTab.onFrame()
check("unprefixed name ignored", AutoTab.current_tab, nil)
AutoTab.onLocation(2, nil)
AutoTab.onFrame()
check("nil name ignored", AutoTab.current_tab, nil)

print(string.format("AUTOTAB: %d/%d checks pass", pass, pass + fail))
