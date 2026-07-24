using System;
using System.Text;

namespace WtgArchipelago.Mapping;

/// <summary>
/// DEV/TEST probe (READ-ONLY) to diagnose why a computer boss door won't light even
/// though its chamber's holes are all cleared -- specifically Computer 8 (Western,
/// bossLevelID "AXCX7"), reported 2026-07-24 as dark after all 16 non-boss Western
/// holes were cleared on a progressed, teleport-based save (boss_keys OFF).
///
/// Two hypotheses this splits:
///  (a) STALE "beaten" flag: an older mod build wrote the door's completed/main-door
///      save flag, so the game loads the door already in State.Completed (removed /
///      unfightable). Tell: state == Completed / IsCompleted() == true, or
///      SaveGame.GetIsMainDoorOpen(id) == true for a boss you never beat.
///  (b) Plate not re-evaluating: the area IS complete but the plate stayed off. Tell:
///      plate.isOn == false while plateInfo.areaState >= LEVELS_COMLETE (or all of
///      plateInfo.goals are Won). If instead areaState is still DOOR_OPEN with all
///      goals Won, the game isn't counting the area complete (level-set mismatch, cf.
///      the C5 injection divergence).
///
/// Purely reads fields / calls IsOpen()/IsCompleted()/GetIsMainDoorOpen -- it never
/// writes game or save state (unlike DoorTest, which is gone). Gated by
/// <see cref="Mod.BossDoorProbeEnabled"/> (OFF by default) + fired from a hotkey (F6).
/// Stand in the overworld and press it. Remove once Computer 8 is understood.
/// </summary>
public static class BossDoorProbe
{
    /// <summary>Dump every computer door's state + plate/area completion. Call from a
    /// hotkey while in the overworld (doors are loaded per-chamber, so the door you
    /// care about must be in the currently-loaded overworld region).</summary>
    public static void Dump()
    {
        try
        {
            var doors = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldMainDoorRobot>();
            Plugin.Log.LogInfo($"[DOORPROBE] ==== {doors.Length} OverworldMainDoorRobot(s) ====");
            for (int i = 0; i < doors.Length; i++)
            {
                var d = doors[i];
                if (d == null) continue;
                var sb = new StringBuilder();

                string bid = Safe(() => d.bossLevelID);
                string bname = Safe(() => d.bossLevelName);
                sb.Append($"[DOORPROBE] door bossLevelID='{bid}' name='{bname}' ");
                sb.Append($"state={Safe(() => ((int)d.state).ToString())}({Safe(() => d.state.ToString())}) ");
                sb.Append($"IsOpen={Safe(() => d.IsOpen().ToString())} IsCompleted={Safe(() => d.IsCompleted().ToString())} ");

                // Save-flag check (hypothesis a): is a main-door "open/passed" flag set
                // for this door even though the boss may be unbeaten? Try both ids.
                sb.Append($"MainDoorOpen[byID]={Safe(() => Il2Cpp.SaveGame.GetIsMainDoorOpen(bid).ToString())} ");
                sb.Append($"MainDoorOpen[byName]={Safe(() => Il2Cpp.SaveGame.GetIsMainDoorOpen(bname).ToString())}");
                Plugin.Log.LogInfo(sb.ToString());

                var plates = d.plates;
                if (plates == null) { Plugin.Log.LogInfo("[DOORPROBE]   plates=null"); continue; }
                for (int j = 0; j < plates.Count; j++)
                {
                    var p = plates[j];
                    if (p == null) { Plugin.Log.LogInfo($"[DOORPROBE]   plate[{j}]=null"); continue; }
                    string area = "?", areaState = "?", goalSummary = "?";
                    var pi = Safe(() => p.plateInfo, (Il2Cpp.PlateInfoManager)null);
                    if (pi != null)
                    {
                        area = Safe(() => pi.Name.ToString());
                        areaState = Safe(() => pi.areaState.ToString());
                        goalSummary = SummarizeGoals(pi);
                    }
                    Plugin.Log.LogInfo(
                        $"[DOORPROBE]   plate[{j}] isOn={Safe(() => p.isOn.ToString())} " +
                        $"area={area} areaState={areaState} goals[{goalSummary}]");
                }
            }
            Plugin.Log.LogInfo("[DOORPROBE] ==== end ====");
        }
        catch (Exception e) { Plugin.Log.LogError($"BossDoorProbe.Dump: {e}"); }
    }

    /// <summary>Count how many of an area's OverworldGoals are Won (>=2) / Crown (==3),
    /// so we can tell whether the game itself considers the area complete.</summary>
    private static string SummarizeGoals(Il2Cpp.PlateInfoManager pi)
    {
        try
        {
            var goals = pi.goals;
            if (goals == null) return "goals=null";
            int total = goals.Count, won = 0, crown = 0;
            for (int k = 0; k < total; k++)
            {
                var g = goals[k];
                if (g == null) continue;
                int st = -1;
                try { st = (int)g.state; } catch { }
                if (st >= 2) won++;
                if (st >= 3) crown++;
            }
            return $"{won}/{total} won, {crown} crown";
        }
        catch (Exception e) { return $"goals-err({e.GetType().Name})"; }
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
