using System;
using System.Collections.Generic;
using System.Text;

namespace WtgArchipelago.Mapping;

/// <summary>
/// READ-ONLY diagnostic for the teleporter / reachability thread.
///
/// FINDING (from the game dump, 2026-07-20): there is NO "reached save spots" save
/// set. SaveGame.OverWorldData has a FIXED, fully-known key vocabulary (OPEN_DOORS,
/// OPEN_MAIN_DOORS, CONSOLES_HIT, COMPLETED_LEVELS, ACCESSIBLE_LEVELS,
/// LEVEL_NOTIFICATIONS, COMPLETED_CHALLENGES, UNLOCKED_CHESTS, CROWN_AWARENESS_MODE,
/// + scalars). The only save-position state is a SINGLE `SavePosition` string (the
/// current respawn spot). So teleport reachability is RUNTIME-COMPUTED
/// (OverworldLevelSection.GetIsAvailable / the teleport menu's filter), not a save
/// set we can write to.
///
/// Also: the pause-menu teleporter only offers sections whose `saveSpotId` is a
/// TELEPORT_* id. SAVE_*/save_* sections (e.g. 08C Space = SAVE_space_01) are plain
/// save points reached by WALKING within the chamber -- never teleport destinations.
/// That is why forcing Space's door flag never made it list.
///
/// This probe dumps, once, everything needed to decide the design fork on a FRESH
/// save: every save set, the SavePosition, every loaded SaveSpot id, and per-section
/// (isAvailable after Refresh, saveSpotId, TELEPORT-vs-SAVE, whether a matching
/// SaveSpot is currently loaded in the scene). No mutation. Runs from Mod.OnUpdate
/// when Mod.ProbeEnabled is true.
/// </summary>
public static class UnlockProbe
{
    private static bool _done;

    public static void RunOnce()
    {
        if (_done) return;
        try
        {
            var doors = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldMainDoorRobot>();
            if (doors.Length == 0) return;   // wait until an overworld is loaded
            _done = true;

            var sb = new StringBuilder("\n===== UNLOCK PROBE =====\n");

            // 1) Every save set + scalar of interest. On a fresh save this is the
            // ground truth for what the game persists (spoiler: no reached-spots set).
            try
            {
                var ow = Il2Cpp.SaveGame.currentOverworld;
                if (ow != null)
                {
                    DumpSet(sb, "OPEN_DOORS", ow.OPEN_DOORS);
                    DumpSet(sb, "OPEN_MAIN_DOORS", ow.OPEN_MAIN_DOORS);
                    DumpSet(sb, "CONSOLES_HIT", ow.CONSOLES_HIT);
                    DumpSet(sb, "COMPLETED_LEVELS", ow.COMPLETED_LEVELS);
                    DumpSet(sb, "ACCESSIBLE_LEVELS", ow.ACCESSIBLE_LEVELS);
                    DumpSet(sb, "LEVEL_NOTIFICATIONS", ow.LEVEL_NOTIFICATIONS);
                    DumpSet(sb, "COMPLETED_CHALLENGES", ow.COMPLETED_CHALLENGES);
                    DumpSet(sb, "UNLOCKED_CHESTS", ow.UNLOCKED_CHESTS);
                    // Single-value position state (the closest thing to "reached").
                    sb.Append($"SAVE_POSITION key='{ow.SAVE_POSITION}' value='{Try(() => Il2Cpp.SaveGame.SavePosition)}'\n");
                    sb.Append($"prefix='{ow.prefix}' COMPLETED_PERCENTAGE='{Try(() => Il2Cpp.SaveGame.CompletedPercentage.ToString())}'\n");
                }
                else sb.Append("currentOverworld is null\n");
            }
            catch (Exception e) { sb.Append($"save-set dump ERR: {e.Message}\n"); }

            // 2) Every SaveSpot currently loaded in the scene, with its id. The
            // teleport menu can only offer a section if the game can resolve a matching
            // (TELEPORT_*) save spot; this tells us which are actually present.
            try
            {
                var spots = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.SaveSpot>();
                sb.Append($"SAVESPOTS loaded n={spots.Length}: ");
                var ids = new List<string>();
                for (int i = 0; i < spots.Length; i++)
                {
                    if (spots[i] == null) continue;
                    string id = null; try { id = spots[i].ID; } catch { }
                    ids.Add(id ?? "<null>");
                }
                sb.Append(string.Join(" | ", ids)).Append('\n');
            }
            catch (Exception e) { sb.Append($"savespots ERR: {e.Message}\n"); }

            // 3) Each loaded main (computer) door + whether the save says it's open.
            for (int i = 0; i < doors.Length; i++)
            {
                var d = doors[i];
                if (d == null) continue;
                string boss = d.bossLevelID, bossName = d.bossLevelName;
                sb.Append($"DOOR bossLevelID='{boss}' bossLevelName='{bossName}'"
                          + $" open(id)={Try(() => Il2Cpp.SaveGame.GetIsMainDoorOpen(boss))}\n");
            }

            // 4) Section availability straight from the campaign asset, cross-referenced
            // with whether its save spot is a teleport destination and currently loaded.
            try
            {
                var loadedSpotIds = LoadedSaveSpotIds();
                var datas = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldLevelData>();
                for (int a = 0; a < datas.Length; a++)
                {
                    var secs = datas[a] != null ? datas[a].Sections : null;
                    if (secs == null) continue;
                    for (int s = 0; s < secs.Count; s++)
                    {
                        var sec = secs[s];
                        if (sec == null) continue;
                        try { sec.Refresh(); } catch { }
                        string save = sec.saveSpotId ?? "";
                        bool isTeleport = save.StartsWith("TELEPORT", StringComparison.OrdinalIgnoreCase);
                        bool spotLoaded = !string.IsNullOrEmpty(save) && loadedSpotIds.Contains(save);
                        sb.Append($"   SECTION '{sec.name}' avail={sec.isAvailable}"
                                  + $" trig='{sec.unlockTriggerId}' save='{save}'"
                                  + $" {(isTeleport ? "TELEPORT" : "walk-only")} spotLoaded={spotLoaded}\n");
                    }
                }
            }
            catch (Exception e) { sb.Append($"sections ERR: {e.Message}\n"); }

            sb.Append("===== END PROBE =====");
            Plugin.Log.LogInfo(sb.ToString());
            MelonLoader.MelonLogger.Msg("[PROBE] unlock state logged — see log file");
        }
        catch (Exception e) { Plugin.Log.LogError($"UnlockProbe: {e}"); }
    }

    private static HashSet<string> LoadedSaveSpotIds()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var spots = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.SaveSpot>();
            for (int i = 0; i < spots.Length; i++)
            {
                if (spots[i] == null) continue;
                try { var id = spots[i].ID; if (!string.IsNullOrEmpty(id)) set.Add(id); } catch { }
            }
        }
        catch { }
        return set;
    }

    private static string Try(Func<bool> f)
    {
        try { return f().ToString(); } catch (Exception e) { return "ERR:" + e.Message; }
    }

    private static string Try(Func<string> f)
    {
        try { return f(); } catch (Exception e) { return "ERR:" + e.Message; }
    }

    // Read a save set by key (key = the currentOverworld field value) and log it.
    private static void DumpSet(StringBuilder sb, string label, string key)
    {
        try
        {
            var slot = new Il2CppSystem.Nullable<int>();   // no value -> current slot
            var list = Il2Cpp.SaveGame.GetStringList(key, slot);
            sb.Append($"{label} (key='{key}') n={(list != null ? list.Count : 0)}: ");
            if (list != null) for (int i = 0; i < list.Count && i < 60; i++) sb.Append(list[i]).Append(" | ");
            sb.Append('\n');
        }
        catch (Exception e) { sb.Append($"{label} (key='{key}') ERR: {e.Message}\n"); }
    }
}
