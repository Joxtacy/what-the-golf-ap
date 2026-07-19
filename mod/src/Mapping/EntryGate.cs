using WtgArchipelago.Patches;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Hard, save-independent gate. OnLevelBegin fires BEFORE the current level is
/// populated (scene reads null there), so instead of checking at begin-time we
/// "arm" a check and poll from OnUpdate until the scene is available; then, if
/// its area is locked, abort the level (kick back to the overworld).
/// </summary>
public static class EntryGate
{
    private static bool _armed;
    private static int _frames;

    /// <summary>Called from the OnLevelBegin hook.</summary>
    public static void Arm()
    {
        _armed = true;
        _frames = 0;
    }

    /// <summary>Called every frame from OnUpdate.</summary>
    public static void Tick()
    {
        if (!_armed) return;
        _frames++;

        string scene = GameState.CurrentLevelScene();
        if (scene != null)
        {
            _armed = false;   // level is known -> decide once
            if (!AreaState.IsSceneUnlocked(scene) && GameState.AbortLevel())
            {
                string area = AreaState.AreaOfScene(scene) ?? "?";
                Plugin.Log.LogInfo($"[GATE] blocked locked level '{scene}' (area '{area}')");
                MelonLoader.MelonLogger.Warning($"Locked — need '{area} Access' to play this level.");
            }
        }
        else if (_frames > 180)   // ~3s and no level scene appeared (e.g. overworld)
        {
            _armed = false;
        }
    }
}
