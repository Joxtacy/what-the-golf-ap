namespace WtgArchipelago.Mapping;

/// <summary>
/// One-shot validation: prove that OverworldMainDoorPlate.SetState(true) opens a
/// computer door. Finds a partially-activated door (some plates on, some off --
/// i.e. the one the player is working on) and turns all its plates on. If the
/// door then opens, the gating lever is confirmed. Temporary/dev only.
/// </summary>
public static class DoorTest
{
    private static bool _done;

    public static void MaybeRun()
    {
        if (_done) return;
        try
        {
            var doors = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldMainDoorRobot>();
            if (doors.Length == 0) return;   // not in the overworld yet

            Il2Cpp.OverworldMainDoorRobot target = null;
            Il2Cpp.OverworldMainDoorRobot fallback = null;
            for (int i = 0; i < doors.Length; i++)
            {
                var d = doors[i];
                var plates = d != null ? d.plates : null;
                if (plates == null || plates.Count == 0) continue;
                if (fallback == null) fallback = d;
                int on = 0;
                for (int j = 0; j < plates.Count; j++) { var p = plates[j]; if (p != null && p.isOn) on++; }
                if (on > 0 && on < plates.Count) { target = d; break; }   // partially activated
            }
            target = target ?? fallback;
            if (target == null) return;

            _done = true;
            var pl = target.plates;
            string boss = string.IsNullOrEmpty(target.bossLevelName) ? target.bossLevelID : target.bossLevelName;
            Plugin.Log.LogInfo($"[DOORTEST] opening door boss='{boss}' plates={pl.Count}");
            for (int j = 0; j < pl.Count; j++)
            {
                var p = pl[j];
                if (p == null) continue;
                Plugin.Log.LogInfo($"[DOORTEST]   plate {j} isOn={p.isOn} -> SetState(true)");
                p.SetState(true, false);
            }
            MelonLoader.MelonLogger.Msg($"[DOORTEST] turned all plates ON for '{boss}' — watch if the door opens!");
        }
        catch (System.Exception e) { Plugin.Log.LogError($"DoorTest: {e}"); }
    }
}
