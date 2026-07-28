using System;
using System.Text;

namespace WtgArchipelago.Mapping;

/// <summary>
/// DEV/TEST probe (READ-ONLY) to understand what the two "Main Crown Door" objects
/// (OverworldButton2D with OverworldID.ID == CROWN_MAIN1 / CROWN_MAIN2) actually are
/// and how the Bowling (07B) / Cars (03B) sub-areas are reached.
///
/// Context (2026-07-27): the section data lists 07B.unlockTriggerId == CROWN_MAIN1 and
/// 03B.unlockTriggerId == CROWN_MAIN2, yet those sub-areas have no walk-through
/// connector and are reached by boss-clearing / teleport. So it's unclear whether the
/// Main Crown Door is the *entrance* to those holes (hard_sections must hold it shut)
/// or a *separate crown-bonus door* (hard_sections should ignore it). This dump splits
/// that:
///  - requireGoals tells us which holes gate the door opening (the crown/clear set).
///  - previous / previousBoss reveals whether it's chained off another door or a boss.
///  - position (vs the 07B/03B hole goals' positions) shows whether the door sits at
///    the mouth of those holes (entrance) or off on its own (bonus).
///
/// Purely reads fields + calls GetFlagsLeft()/CanDoorBeOpened()/GetTotalCount -- never
/// writes game or save state. Gated by <see cref="Mod.CrownDoorProbeEnabled"/> (OFF by
/// default) + fired from a hotkey (F5). Stand in the overworld (chamber 07 or 03 loaded)
/// and press it. Remove once the CROWN_MAIN doors are understood.
/// </summary>
public static class CrownDoorProbe
{
    public static void Dump()
    {
        try
        {
            // 1) Every OverworldButton2D loaded right now: GameObject name + OID + pos +
            //    open-state. Catches the "Main Crown Door" objects whatever their live OID,
            //    and every crown-chest/connector door for spatial context.
            var btns = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldButton2D>();
            Plugin.Log.LogInfo($"[CROWNPROBE] ==== {btns.Length} OverworldButton2D ====");
            for (int i = 0; i < btns.Length; i++)
            {
                var b = btns[i];
                if (b == null) continue;
                string oid = OidOf(b);
                string name = Safe(() => b.gameObject.name);
                bool active = Safe(() => b.gameObject.activeInHierarchy, false);
                Plugin.Log.LogInfo($"[CROWNPROBE] BTN name='{name}' oid='{oid}' pos={Pos(b)} " +
                                   $"active={active} canOpen={Safe(() => b.canOpen.ToString())} " +
                                   $"openOrOpening={Safe(() => b.openOrOpening.ToString())}");
                // Extra detail (requireGoals + chain) for the Main Crown Doors specifically.
                if ((oid != null && oid.StartsWith("CROWN_MAIN")) ||
                    (name != null && name.IndexOf("Main Crown", StringComparison.OrdinalIgnoreCase) >= 0))
                    DumpDoorDetail(oid, b);
            }

            // 2) Every OverworldGoal loaded right now, with its Pun (the in-game level
            //    title, e.g. "lab experiment #521 anger") so we can match by name, plus
            //    scene / section / position / state to locate the gateway holes.
            DumpGoals();
            Plugin.Log.LogInfo("[CROWNPROBE] ==== end ====");
        }
        catch (Exception e) { Plugin.Log.LogError($"CrownDoorProbe.Dump: {e}"); }
    }

    private static void DumpDoorDetail(string oid, Il2Cpp.OverworldButton2D b)
    {
        var sb = new StringBuilder();
        sb.Append($"[CROWNPROBE]   >> MAIN CROWN DOOR '{oid}' ");
        sb.Append($"OnlyAcceptCrowns={Safe(() => b.OnlyAcceptCrowns.ToString())} ");
        sb.Append($"totalCount={Safe(() => b.GetTotalCount.ToString())} ");
        sb.Append($"flagsLeft={Safe(() => b.GetFlagsLeft().ToString())} ");
        sb.Append($"canDoorBeOpened={Safe(() => b.CanDoorBeOpened().ToString())}");
        Plugin.Log.LogInfo(sb.ToString());

        // Door chain: previous OverworldButton2D and/or previousBoss computer door.
        var prev = Safe(() => b.previous, (Il2Cpp.OverworldButton2D)null);
        if (prev != null)
            Plugin.Log.LogInfo($"[CROWNPROBE]   previous door = '{OidOf(prev)}' pos={Pos(prev)}");
        else
            Plugin.Log.LogInfo("[CROWNPROBE]   previous door = null");

        var pboss = Safe(() => b.previousBoss, (Il2Cpp.OverworldMainDoorRobot)null);
        if (pboss != null)
            Plugin.Log.LogInfo($"[CROWNPROBE]   previousBoss = '{Safe(() => pboss.bossLevelID)}' " +
                               $"('{Safe(() => pboss.bossLevelName)}')");
        else
            Plugin.Log.LogInfo("[CROWNPROBE]   previousBoss = null");

        // requireGoals: the holes that must be completed for this door to open.
        try
        {
            var rg = b.requireGoals;
            if (rg == null) { Plugin.Log.LogInfo("[CROWNPROBE]   requireGoals = null"); return; }
            Plugin.Log.LogInfo($"[CROWNPROBE]   requireGoals.Count = {rg.Count}");
            for (int k = 0; k < rg.Count; k++)
            {
                var g = rg[k];
                if (g == null) { Plugin.Log.LogInfo($"[CROWNPROBE]     goal[{k}] = null"); continue; }
                string scene = Safe(() => g.levelData != null ? g.levelData.SceneName : "<no levelData>");
                string sect = Safe(() => g.ParentHubSection != null ? g.ParentHubSection.name : "<no section>");
                string state = Safe(() => ((int)g.state).ToString());
                Plugin.Log.LogInfo($"[CROWNPROBE]     goal[{k}] scene='{scene}' section='{sect}' state={state}");
            }
        }
        catch (Exception e) { Plugin.Log.LogInfo($"[CROWNPROBE]   requireGoals err: {e.GetType().Name}"); }
    }

    // Log EVERY loaded OverworldGoal with its Pun (the in-game level title, e.g. "lab
    // experiment #521 anger"), scene, section, position, and state -- so the gateway
    // holes can be matched by title AND located spatially next to a Main Crown Door.
    private static void DumpGoals()
    {
        try
        {
            var goals = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldGoal>();
            Plugin.Log.LogInfo($"[CROWNPROBE] ==== {goals.Length} OverworldGoal ====");
            foreach (var g in goals)
            {
                if (g == null) continue;
                string scene = Safe(() => g.levelData != null ? g.levelData.SceneName : "?");
                string pun = Safe(() => g.levelData != null ? OneLine(g.levelData.Pun) : "?");
                string sect = Safe(() => g.ParentHubSection != null ? g.ParentHubSection.name : null);
                string state = Safe(() => ((int)g.state).ToString());
                string unlocked = Safe(() => g.IsUnlocked().ToString());
                Plugin.Log.LogInfo($"[CROWNPROBE] GOAL scene='{scene}' pun='{pun}' " +
                                   $"section='{sect}' pos={Pos(g)} state={state} unlocked={unlocked}");
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"CrownDoorProbe.DumpGoals: {e}"); }
    }

    // Puns are authored multi-line; flatten newlines so each goal stays on one log line.
    private static string OneLine(string s)
        => string.IsNullOrEmpty(s) ? s : s.Replace("\r", " ").Replace("\n", " ");

    private static string OidOf(Il2Cpp.OverworldButton2D b)
    {
        try { var o = b.gameObject.GetComponent<Il2Cpp.OverworldID>(); return o != null ? o.ID : null; }
        catch { return null; }
    }

    private static string Pos(UnityEngine.MonoBehaviour mb)
    {
        try
        {
            var p = mb.transform.position;
            return $"({p.x:0.0},{p.y:0.0})";
        }
        catch { return "?"; }
    }

    private static string Safe(Func<string> f)
    {
        try { return f() ?? "null"; } catch (Exception e) { return $"<{e.GetType().Name}>"; }
    }

    private static T Safe<T>(Func<T> f, T fallback)
    {
        try { return f(); } catch { return fallback; }
    }
}
