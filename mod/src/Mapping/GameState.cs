namespace WtgArchipelago.Patches;

/// <summary>
/// Reads current game state (level id, crown status) for the patches, using the
/// MelonLoader interop types. Namespaced game types are Il2Cpp-prefixed:
/// Core.Level -> Il2CppCore.Level, Core.LevelData -> Il2CppCore.LevelData.
/// (See mod/REVERSE_ENGINEERING.md.)
///
/// Field path (from the dump): Level.Instance -> levelManager -> currentLevel
/// (LevelData) -> ID. Crown = CompletedChallenges.Count == levelChallenges.Count.
/// </summary>
public static class GameState
{
    // IMPORTANT: gate EVERY Level.Instance access behind Level.HasInstance.
    // Level.get_Instance() does a FindObjectOfType<Level>() search whenever no level
    // is active (the overworld) -- a full object-graph scan that, called per frame
    // from OnUpdate, cost ~8 ms/frame and halved the overworld framerate. HasInstance
    // just checks the cached reference (no search), and when an instance DOES exist
    // Instance returns it cached (cheap). So this guard makes the overworld free while
    // staying correct in a hole.
    private static Il2CppCore.LevelData CurrentLevel()
    {
        if (!Il2CppCore.Level.HasInstance) return null;
        var level = Il2CppCore.Level.Instance;
        return level != null && level.levelManager != null
            ? level.levelManager.currentLevel
            : null;
    }

    /// <summary>The current level's scene name -- the key AP locations use.</summary>
    public static string CurrentLevelScene()
    {
        var ld = CurrentLevel();
        return ld != null ? ld.SceneName : null;
    }

    /// <summary>Is the current level a computer/final boss? Bosses are triggered by
    /// the computer doors (not overworld flags), so GoalWatcher never sees them --
    /// their Clear/Crown must be sent from the OnLevelComplete path instead.</summary>
    public static bool CurrentLevelIsBoss()
    {
        var ld = CurrentLevel();
        return ld != null && ld.isBossBattle;
    }

    /// <summary>Are we currently playing a hole (vs. the overworld/menu)? Used to
    /// decide whether an incoming DeathLink has anything to kill.</summary>
    public static bool IsInLevel()
    {
        if (!Il2CppCore.Level.HasInstance) return false;   // overworld: no search
        var level = Il2CppCore.Level.Instance;
        var lm = level != null ? level.levelManager : null;
        try { return lm != null && lm.IsInLevel(); }
        catch { return false; }
    }

    /// <summary>Restart the current hole from the start (resets the ball, wipes hole
    /// progress). The param-less Restart() is safe to invoke via interop -- unlike
    /// Level.Fail, whose Nullable/by-value signature crashes the trampoline. Used to
    /// apply an incoming DeathLink. Returns true if invoked.</summary>
    public static bool RestartLevel()
    {
        if (!Il2CppCore.Level.HasInstance) return false;
        var level = Il2CppCore.Level.Instance;
        if (level == null) return false;
        level.Restart();
        return true;
    }

    public static bool CurrentLevelCrowned()
    {
        var ld = CurrentLevel();
        if (ld == null) return false;
        var challenges = ld.levelChallenges;
        var completed = ld.CompletedChallenges;
        return challenges != null && challenges.Count > 0
               && completed != null && completed.Count >= challenges.Count;
    }

    /// <summary>Human-readable dump of the current level, for harvesting real
    /// game data (level ids, scenes, boss flags) during a play session.</summary>
    public static string CurrentLevelInfo()
    {
        var ld = CurrentLevel();
        if (ld == null) return "<no current level>";
        int challenges = ld.levelChallenges != null ? ld.levelChallenges.Count : 0;
        int done = ld.CompletedChallenges != null ? ld.CompletedChallenges.Count : 0;
        return $"id='{ld.ID}' scene='{ld.SceneName}' boss={ld.isBossBattle} " +
               $"challenges={done}/{challenges}";
    }
}
