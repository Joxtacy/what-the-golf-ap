-- Exercises autotracking.lua's onClear/apply_slot_data against representative
-- slot data. The access-rule parity harness (tools/check_poptracker_logic.py)
-- covers the logic; this covers the OTHER risky half -- translating the
-- apworld's fill_slot_data into tracker state. In particular:
--   * area_access arrives as the STRING "section"/"chamber", not the Choice int
--   * the Goal progressive's stage order must equal Options.Goal 0..4
--   * hard_sections must force the walk-in hint off
--   * a disabled feature's sections must be retired (AvailableChestCount = 0),
--     not just hidden
--   * flag MaxCount must follow flag_goal for door goals and the whole pool
--     otherwise
--
-- To run: copy into an installed pack's scripts/ and append to init.lua
--   ScriptHost:LoadScript("scripts/slotdata_cases.lua")
-- then enable "log": true in PopTracker.json and read log.txt.

local pass, fail = 0, 0

local function check(label, got, want)
    if got == want then
        pass = pass + 1
    else
        fail = fail + 1
        print(string.format("SLOTDATA MISMATCH %s: expected %s got %s",
            label, tostring(want), tostring(got)))
    end
end

local function stage(code)
    local o = Tracker:FindObjectForCode(code)
    return o and o.CurrentStage or -1
end

local function active(code)
    local o = Tracker:FindObjectForCode(code)
    if not o then return "missing" end
    return o.Active and true or false
end

local function avail(path)
    local o = Tracker:FindObjectForCode(path)
    return o and o.AvailableChestCount or -1
end

local function maxflags()
    local o = Tracker:FindObjectForCode("flag")
    return o and o.MaxCount or -1
end

-- 1. Apworld defaults: section access, campaign goal, no crowns, no episodes.
onClear({
    goal = 0, area_access = "section", boss_keys = false, hard_sections = false,
    crowns = false, episodes = {}, traps = false, trap_percentage = 20,
    death_link = false, death_link_amnesty = 10, flag_goal = 0,
    flag_items = { "Flag" },
})
check("default opt_area stage", stage("opt_area"), 0)
check("default opt_boss_keys stage", stage("opt_boss_keys"), 0)
check("default opt_goal stage", stage("opt_goal"), 0)
check("default opt_crowns", active("opt_crowns"), false)
check("default opt_hard", active("opt_hard"), false)
check("default chest retired", avail("@03B Cars/Cars Chest/Chest"), 0)
check("default episode retired", avail("@Snow Episode/House on Ice/Clear"), 0)
check("default hole live", avail("@09A Easy 2D/2D Rubber 1/Clear"), 1)
check("default flag MaxCount = whole pool", maxflags(), WTG.MAX_FLAGS)

-- 2. door_100 + chamber access + boss keys + crowns + Snow + hard_sections.
onClear({
    goal = 3, area_access = "chamber", boss_keys = true, hard_sections = true,
    crowns = true, episodes = { "Snow" }, traps = true, trap_percentage = 20,
    death_link = true, death_link_amnesty = 10, flag_goal = 156,
    flag_items = { "Flag", "Dannebrog" },
})
check("door100 opt_area stage", stage("opt_area"), 1)
check("door100 opt_boss_keys stage", stage("opt_boss_keys"), 1)
check("door100 opt_goal stage", stage("opt_goal"), 3)
check("door100 opt_crowns", active("opt_crowns"), true)
check("door100 opt_hard", active("opt_hard"), true)
check("door100 opt_walk forced off", active("opt_walk"), false)
check("door100 opt_ep_snow", active("opt_ep_snow"), true)
check("door100 opt_ep_hotdog", active("opt_ep_hotdog"), false)
check("door100 chest live", avail("@03B Cars/Cars Chest/Chest"), 1)
check("door100 snow live", avail("@Snow Episode/House on Ice/Clear"), 1)
check("door100 hotdog retired", avail("@Hotdog Episode/Simply Holes 1/Clear"), 0)
check("door100 flag MaxCount = flag_goal", maxflags(), 156)

-- 3. all_bosses is stage 4 (the last of Options.Goal).
onClear({
    goal = 4, area_access = "section", boss_keys = false, hard_sections = false,
    crowns = false, episodes = {}, flag_goal = 0, flag_items = { "Flag" },
})
check("all_bosses opt_goal stage", stage("opt_goal"), 4)
check("all_bosses flag MaxCount", maxflags(), WTG.MAX_FLAGS)

-- 4. Missing/legacy slot data must not throw or corrupt state.
onClear({})
check("empty slot data opt_area stage", stage("opt_area"), 0)
check("empty slot data opt_goal stage", stage("opt_goal"), 0)
check("empty slot data BulkUpdate cleared", Tracker.BulkUpdate, false)

-- 5. Item handling: every Flag NAME must feed the single counter.
onClear({ goal = 1, area_access = "section", flag_goal = 67,
          flag_items = { "Flag", "Dannebrog", "Jolly Roger" } })
local idx = 0
for _, nm in ipairs({ "Flag", "Dannebrog", "Jolly Roger", "Old Glory" }) do
    idx = idx + 1
    onItem(idx, -1, nm, 1)
end
local f = Tracker:FindObjectForCode("flag")
check("4 differently-named flags counted", f and f.AcquiredCount or -1, 4)

-- 6. A location check decrements exactly once, even if replayed.
onClear({ goal = 0, area_access = "section", flag_goal = 0 })
local lid = nil
for id, v in pairs(LOCATION_MAPPING) do
    if v[1] == "@09A Easy 2D/2D Rubber 1/Clear" then
        lid = id
        break
    end
end
if lid then
    onLocation(lid, nil)
    onLocation(lid, nil)
    check("location check is idempotent", avail("@09A Easy 2D/2D Rubber 1/Clear"), 0)
else
    fail = fail + 1
    print("SLOTDATA MISMATCH: could not find the sample location id")
end

print(string.format("SLOTDATA: %d/%d checks pass", pass, pass + fail))
