-- Auto-switch the map tab to the chamber (or episode) the player is currently in.
--
-- Two independent sources, in priority order:
--
--   1. the "WTG:CurrentArea:<team>:<slot>" data-storage key published by the game
--      mod. Live: it moves when you WALK or TELEPORT, not only when you check
--      something.
--   2. fallback -- the sub-area prefix of the most recent location check. Every
--      WTG location name is prefixed ("08C: Space Golf 6 - Clear", "08: Computer
--      1 (Basic) - Clear" for bosses, "Snow: ..." for episodes), so this needs no
--      mod support at all and works with any mod build.
--
-- Source 2 goes permanently quiet the first time source 1 speaks, so the two can
-- never fight. Version negotiation is just "does the key exist": an older mod
-- never writes it, Archipelago:Get returns nil, and we simply stay in fallback.
--
-- PopTracker's Lua cannot WRITE data storage (only Get/SetNotify), which is why
-- the mod has to be the publisher.

AutoTab = {}

local DS_KEY = nil        -- computed on connect; needs team + slot
local ds_live = false     -- has the mod ever published? -> mute the fallback
local pending_tab = nil   -- coalesced to at most one switch per frame
local burst = 0           -- location events seen this frame
local synced = false      -- ignore the connect-time location replay

-- Last tab we activated; never re-hinted. On the module table (not a local) so
-- it is observable from the test harness and from a debug print.
AutoTab.current_tab = nil

-- "autotab_off" present (count > 0) means the player turned auto-switching OFF.
-- Inverted on purpose: PopTracker toggles default to off, so the shipped default
-- is auto-switch ON with no first-run migration.
local function enabled()
    return Tracker:ProviderCountForCode("autotab_off") == 0
end

-- "08C" / "08" / "Snow" -> tab title. AREA_TABS is generated next to the maps.
function AutoTab.tab_for_area(area)
    if type(area) ~= "string" or area == "" then return nil end
    local t = AREA_TABS[area]
    if t then return t end
    local chamber = area:match("^(%d%d)")          -- "08C" -> "08"
    if chamber then return AREA_TABS[chamber] end
    return nil
end

local function request(tab)
    if not tab or tab == AutoTab.current_tab then return end
    pending_tab = tab
end

function AutoTab.onFrame()
    -- A burst of location events is the connect-time replay of everything already
    -- checked, not the player moving; switching on it would fling the tab to an
    -- arbitrary chamber.
    if burst > 3 then pending_tab = nil end
    burst = 0
    if pending_tab then
        AutoTab.current_tab = pending_tab
        pending_tab = nil
        -- Track current_tab even while disabled, so re-enabling doesn't cause a
        -- stale catch-up jump.
        if enabled() then Tracker:UiHint("ActivateTab", AutoTab.current_tab) end
    end
end

-- The mod publishes a pipe-delimited STRING, not a JSON object:
--   v | area | subarea | campaign | in_level | src | scene
-- (PopTracker's Lua has no JSON parser, and the client library's JToken belongs
-- to a different Newtonsoft assembly than the mod's, so a table payload was not
-- available on either end. Field 1 is the version; fields 2-3 are the contract.)
function AutoTab.parse(value)
    if type(value) ~= "string" or value == "" then return nil end
    local f = {}
    for part in (value .. "|"):gmatch("([^|]*)|") do
        f[#f + 1] = part
    end
    local v = tonumber(f[1])
    if not v or v < 1 then return nil end
    return { v = v, area = f[2], subarea = f[3], campaign = f[4],
             in_level = f[5] == "1", src = f[6], scene = f[7] }
end

-- Public so the handlers below stay a thin key-gate over it, and so a test can
-- drive the data-storage path without a live server (DS_KEY needs a connection).
function AutoTab.apply(value)
    -- nil = the key was never set: an older mod, or the game isn't connected yet.
    -- Stay in fallback mode; a later SetReply flips us over.
    local p = AutoTab.parse(value)
    if not p then return end
    ds_live = true
    request(AutoTab.tab_for_area(p.area ~= "" and p.area or p.subarea))
end

function AutoTab.onClear(_slot_data)
    ds_live, pending_tab, synced, burst = false, nil, false, 0
    AutoTab.current_tab = nil
    if Archipelago.PlayerNumber and Archipelago.PlayerNumber > -1 then
        local team = Archipelago.TeamNumber or 0
        if team < 0 then team = 0 end
        DS_KEY = string.format("WTG:CurrentArea:%d:%d", team, Archipelago.PlayerNumber)
        -- Both must be called from the ClearHandler.
        Archipelago:SetNotify({ DS_KEY })
        Archipelago:Get({ DS_KEY })
    end
end

function AutoTab.onRetrieved(key, value)
    if key == DS_KEY then AutoTab.apply(value) end
end

function AutoTab.onSetReply(key, value)
    if key == DS_KEY then AutoTab.apply(value) end
end

function AutoTab.onLocation(_id, location_name)
    burst = burst + 1
    if ds_live then return end        -- the mod owns the signal; don't fight it
    if not synced then
        if burst > 3 then return end  -- still replaying the connect sync
        synced = true
    end
    if type(location_name) == "string" then
        request(AutoTab.tab_for_area(location_name:match("^([^:]+):")))
    end
end

Archipelago:AddClearHandler("wtg autotab", AutoTab.onClear)
Archipelago:AddRetrievedHandler("wtg autotab", AutoTab.onRetrieved)
Archipelago:AddSetReplyHandler("wtg autotab", AutoTab.onSetReply)
Archipelago:AddLocationHandler("wtg autotab", AutoTab.onLocation)
ScriptHost:AddOnFrameHandler("wtg autotab", AutoTab.onFrame)
