namespace WtgArchipelago;

/// <summary>
/// Loader-agnostic static holder + logging adapter. The MelonLoader entry point
/// (Mod) populates Client; the rest of the mod logs via Plugin.Log and reads
/// Plugin.Client / Plugin.GameName, so it never references the loader directly.
/// </summary>
public static class Plugin
{
    public const string GameName = "WHAT THE GOLF?";   // must match the apworld's `game`
    public static ArchipelagoClient Client;
    public static readonly LogAdapter Log = new LogAdapter();
}

/// <summary>Wraps MelonLogger so existing code can keep calling LogInfo/Warn/Error.</summary>
public class LogAdapter
{
    public void LogInfo(string message) => MelonLoader.MelonLogger.Msg(message);
    public void LogWarning(string message) => MelonLoader.MelonLogger.Warning(message);
    public void LogError(string message) => MelonLoader.MelonLogger.Error(message);
}
