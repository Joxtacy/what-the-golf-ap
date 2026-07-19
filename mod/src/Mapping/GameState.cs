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

    public static string CurrentLevelId()
    {
        var ld = CurrentLevel();
        return ld != null ? ld.ID : null;
    }

    /// <summary>The current level's scene name -- the key AP locations use.</summary>
    public static string CurrentLevelScene()
    {
        var ld = CurrentLevel();
        return ld != null ? ld.SceneName : null;
    }

    /// <summary>Leave the current level and return to the overworld, via the
    /// pause menu's Abandon action. Returns true if it was invoked.</summary>
    public static bool AbortLevel()
    {
        var level = Il2CppCore.Level.Instance;
        if (level == null) return false;
        var lm = level.levelManager;
        var menu = lm != null ? lm.InGameMenu : null;
        if (menu == null) return false;
        menu.AbandonLevel();
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
