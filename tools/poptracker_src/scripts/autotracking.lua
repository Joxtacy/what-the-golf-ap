-- Archipelago autotracking.
--
-- Slot data configures the whole pack: the apworld ships goal / area_access /
-- boss_keys / crowns / episodes / flag_goal, which is everything needed to
-- reproduce the seed's board. See what_the_golf/__init__.py fill_slot_data.

ScriptHost:LoadScript("scripts/autotracking/item_mapping.lua")
ScriptHost:LoadScript("scripts/autotracking/location_mapping.lua")

CUR_INDEX = -1
SLOT_DATA = nil

local APPLIED = {}      -- location ids already applied (onLocation is idempotent)

local function obj(code)
    if not code then return nil end
    return Tracker:FindObjectForCode(code)
end

local function set_stage(code, stage)
    local o = obj(code)
    if o then o.CurrentStage = stage end
end

local function set_active(code, on)
    local o = obj(code)
    if o then o.Active = on and true or false end
end

local function reset_all()
    for _, v in pairs(LOCATION_MAPPING) do
        local o = obj(v[1])
        if o then o.AvailableChestCount = o.ChestCount end
    end
    for _, v in pairs(ITEM_MAPPING) do
        local o = obj(v[1])
        if o then
            if v[2] == "flag" then
                o.AcquiredCount = 0
            elseif v[2] == "consumable" then
                o.AcquiredCount = 0
            else
                o.Active = false
            end
        end
    end
    APPLIED = {}
end

-- Sections whose feature is off are hidden by visibility_rules; zero their
-- counts too so they can never show up as checks you still owe.
local function retire(paths)
    for _, p in ipairs(paths or {}) do
        local o = obj(p)
        if o then o.AvailableChestCount = 0 end
    end
end

local function apply_slot_data(sd)
    sd = sd or {}

    -- area_access arrives as the STRING "section"/"chamber", not the Choice int.
    set_stage("opt_area", (sd.area_access == "chamber") and 1 or 0)
    set_stage("opt_boss_keys", sd.boss_keys and 1 or 0)
    -- Goal stage order is exactly Options.Goal 0..4, so this is a direct assign.
    set_stage("opt_goal", tonumber(sd.goal) or 0)
    set_active("opt_crowns", sd.crowns == true)
    set_active("opt_hard", sd.hard_sections == true)
    if sd.hard_sections == true then set_active("opt_walk", false) end

    if sd.crowns ~= true then retire(WTG.CONDITIONAL.crowns) end

    local on = {}
    if type(sd.episodes) == "table" then
        for _, n in ipairs(sd.episodes) do on[n] = true end
    end
    for _, ep in ipairs(WTG.EPISODES) do
        set_active(ep.opt, on[ep.name] == true)
        if not on[ep.name] then retire(WTG.CONDITIONAL.episodes[ep.name]) end
    end

    -- Flag target: door goals ship a real number, campaign/all_bosses ship 0.
    WTG.FLAG_GOAL = tonumber(sd.flag_goal) or 0
    local f = obj("flag")
    if f then
        f.MaxCount = (WTG.FLAG_GOAL > 0) and WTG.FLAG_GOAL or WTG.MAX_FLAGS
    end

    -- Forward-compat: a newer apworld may add Flag names this pack predates.
    if type(sd.flag_items) == "table" then
        for _, n in ipairs(sd.flag_items) do WTG.FLAG_NAMES[n] = true end
    end

    Tracker:UiHint("ActivateTab",
        (sd.area_access == "chamber") and "Chamber Keys" or "Section Keys")
end

function onClear(slot_data)
    SLOT_DATA = slot_data
    CUR_INDEX = -1
    Tracker.BulkUpdate = true
    -- BulkUpdate MUST be cleared even if this throws, or every logic update
    -- stays frozen for the rest of the session.
    local ok, err = pcall(function()
        reset_all()
        apply_slot_data(slot_data)
    end)
    Tracker.BulkUpdate = false
    if not ok then print("WTG onClear failed: " .. tostring(err)) end
end

function onItem(index, item_id, item_name, _player)
    if index <= CUR_INDEX then return end
    CUR_INDEX = index

    -- 47 Flag NAMES, one counter. Match by id first (stable), name second.
    if WTG.FLAG_IDS[item_id] or WTG.FLAG_NAMES[item_name] then
        local f = obj("flag")
        if f then f.AcquiredCount = f.AcquiredCount + 1 end
        return
    end

    local v = ITEM_MAPPING[item_id]
    if not v then return end            -- filler / traps are not tracked
    local o = obj(v[1])
    if not o then
        print(("WTG onItem: no object for code %s (id %s)")
            :format(tostring(v[1]), tostring(item_id)))
        return
    end
    if v[2] == "consumable" then
        o.AcquiredCount = o.AcquiredCount + 1
    elseif v[2] == "progressive" then
        o.CurrentStage = o.CurrentStage + 1
    else
        o.Active = true
    end
end

function onLocation(location_id, _location_name)
    if APPLIED[location_id] then return end
    local v = LOCATION_MAPPING[location_id]
    if not v then return end
    local o = obj(v[1])
    if not o then return end
    APPLIED[location_id] = true
    o.AvailableChestCount = math.max(0, o.AvailableChestCount - 1)
end

Archipelago:AddClearHandler("wtg clear", onClear)
Archipelago:AddItemHandler("wtg items", onItem)
Archipelago:AddLocationHandler("wtg locations", onLocation)
