-- Access-rule helpers called from the generated locations JSON as "^$walkable|08".
--
-- Sub-areas inside one chamber share an open overworld room, so with SECTION
-- area access -- and the mod's hard_sections lock OFF -- you can physically WALK
-- into a locked sibling sub-area once any sibling is reachable. Options.py
-- documents this as out of logic but never a softlock, so the tracker shows it
-- as a SEQUENCE BREAK (yellow) rather than as real access: reachable if you want
-- it, but Archipelago never assumed you could.
--
-- Returns an AccessibilityLevel directly (the "^$" rule form).

function walkable(chamber)
    if Tracker:ProviderCountForCode("opt_area_section") < 1 then
        return AccessibilityLevel.None      -- chamber access has no looseness
    end
    if Tracker:ProviderCountForCode("opt_walk") < 1 then
        return AccessibilityLevel.None      -- player switched the hint off
    end
    if Tracker:ProviderCountForCode("opt_hard") > 0 then
        return AccessibilityLevel.None      -- hard_sections closes the walk-in
    end
    local gates = WTG.CHAMBER_GATES[chamber]
    if not gates then return AccessibilityLevel.None end
    for _, code in ipairs(gates) do
        if Tracker:ProviderCountForCode(code) > 0 then
            return AccessibilityLevel.SequenceBreak
        end
    end
    return AccessibilityLevel.None
end
