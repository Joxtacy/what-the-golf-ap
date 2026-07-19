using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Tracks which areas are unlocked (from received AP Access items) and maps each
/// level scene to its area, so GoalGate can lock/unlock overworld goals. Loaded
/// from wtg_ids.json (start_area + area_by_scene, exported by the apworld).
/// </summary>
public static class AreaState
{
    private static readonly HashSet<string> Unlocked = new();
    private static Dictionary<string, string> _sceneToArea = new();
    public static int FlagCount { get; private set; }

    private class IdsFile
    {
        public string start_area;
        public Dictionary<string, string> area_by_scene;
    }

    public static void Load()
    {
        try
        {
            string path = Path.Combine(
                MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_ids.json");
            if (!File.Exists(path)) { Plugin.Log.LogWarning("AreaState: wtg_ids.json missing"); return; }
            var root = JsonConvert.DeserializeObject<IdsFile>(File.ReadAllText(path));
            _sceneToArea = root?.area_by_scene ?? new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(root?.start_area)) Unlocked.Add(root.start_area);
            Plugin.Log.LogInfo(
                $"AreaState: {_sceneToArea.Count} scenes mapped; start area '{root?.start_area}' unlocked.");
        }
        catch (System.Exception e) { Plugin.Log.LogError($"AreaState.Load: {e}"); }
    }

    public static void Unlock(string area)
    {
        if (Unlocked.Add(area)) Plugin.Log.LogInfo($"AreaState: unlocked '{area}'");
    }

    public static void AddFlag() => FlagCount++;

    public static string AreaOfScene(string scene)
        => scene != null && _sceneToArea.TryGetValue(scene, out var a) ? a : null;

    /// <summary>Scenes not in our world (non-campaign) are treated as unlocked.</summary>
    public static bool IsSceneUnlocked(string scene)
    {
        var area = AreaOfScene(scene);
        return area == null || Unlocked.Contains(area);
    }
}
