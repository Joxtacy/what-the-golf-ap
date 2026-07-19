using System;
using System.Text;

namespace WtgArchipelago.Mapping;

/// <summary>
/// READ-ONLY diagnostic for building non-linear (teleport-based) chamber unlocking.
///
/// The game stores progression in the save via SaveGame (static): SetMainDoorOpen/
/// GetIsMainDoorOpen(id), SetConsoleHit/GetConsolesHit(), SetLevelCompleted/
/// GetIsLevelCompleted(id). Unlocked chambers become reachable through the pause-
/// menu teleport (OverworldTeleportMenu) / portal room. To drive that we first need
/// to learn the id vocabulary: what ids the save uses for main doors / consoles,
/// and which sections the teleport menu currently offers.
///
/// This probe logs that state once (no mutation). Runs from Mod.OnUpdate when
/// Mod.ProbeEnabled is true; toggle off once the vocabulary is known.
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

            // 1) Dump the raw save SETS via the generic GetStringList accessor, using
            // the real key names from currentOverworld. On a 100% save this reveals the
            // exact id vocabulary the teleport/availability system reads (so we know
            // what AddToSet needs for unlocking).
            try
            {
                var ow = Il2Cpp.SaveGame.currentOverworld;
                if (ow != null)
                {
                    DumpSet(sb, "OPEN_MAIN_DOORS", ow.OPEN_MAIN_DOORS);
                    DumpSet(sb, "OPEN_DOORS", ow.OPEN_DOORS);
                    DumpSet(sb, "CONSOLES_HIT", ow.CONSOLES_HIT);
                    DumpSet(sb, "COMPLETED_LEVELS", ow.COMPLETED_LEVELS);
                    DumpSet(sb, "ACCESSIBLE_LEVELS", ow.ACCESSIBLE_LEVELS);
                    DumpSet(sb, "UNLOCKED_CHESTS", ow.UNLOCKED_CHESTS);
                }
                else sb.Append("currentOverworld is null\n");
            }
            catch (Exception e) { sb.Append($"save-set dump ERR: {e.Message}\n"); }

            // 2) Each loaded main door: candidate ids + whether the save says it's open.
            for (int i = 0; i < doors.Length; i++)
            {
                var d = doors[i];
                if (d == null) continue;
                string boss = d.bossLevelID, bossName = d.bossLevelName, notif = null;
                try { notif = d.notificationId; } catch { }
                sb.Append($"DOOR bossLevelID='{boss}' bossLevelName='{bossName}' notificationId='{notif}'\n");
                sb.Append($"   GetIsMainDoorOpen(bossLevelID)={Try(() => Il2Cpp.SaveGame.GetIsMainDoorOpen(boss))}"
                          + $"  (bossLevelName)={Try(() => Il2Cpp.SaveGame.GetIsMainDoorOpen(bossName))}"
                          + $"  (notificationId)={Try(() => Il2Cpp.SaveGame.GetIsMainDoorOpen(notif))}\n");
            }

            // 3) What the teleport menu currently offers.
            try
            {
                var menus = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldTeleportMenu>();
                sb.Append($"OverworldTeleportMenu instances: {menus.Length}\n");
            }
            catch (Exception e) { sb.Append($"teleport menu ERR: {e.Message}\n"); }

            // 4) Section availability straight from the campaign asset.
            try
            {
                var datas = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldLevelData>();
                for (int a = 0; a < datas.Length; a++)
                {
                    var secs = datas[a] != null ? datas[a].Sections : null;
                    if (secs == null) continue;
                    for (int s = 0; s < secs.Count; s++)
                    {
                        var sec = secs[s];
                        if (sec == null) continue;
                        sb.Append($"   SECTION '{sec.name}' hasBoss={sec.hasBoss} isAvailable={sec.isAvailable}"
                                  + $" unlockTrigger='{sec.unlockTriggerId}' saveSpot='{sec.saveSpotId}'\n");
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

    private static string Try(Func<bool> f)
    {
        try { return f().ToString(); } catch (Exception e) { return "ERR:" + e.Message; }
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
