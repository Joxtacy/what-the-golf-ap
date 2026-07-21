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
    private static Il2CppCore.LevelData CurrentLevel()
    {
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

    /// <summary>Are we currently playing a hole (vs. the overworld/menu)? Used to
    /// decide whether an incoming DeathLink has anything to kill.</summary>
    public static bool IsInLevel()
    {
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
