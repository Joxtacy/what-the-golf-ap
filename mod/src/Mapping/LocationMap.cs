using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Maps a level's SCENE name to Archipelago location IDs, using the id table
/// exported from the apworld (tools/export_ids.py -> ids.json, deployed as
/// wtg_ids.json in the game root). AP location names are "{scene} - Clear" and
/// "{scene} - Crown", so we just append the suffix and look up the id.
/// </summary>
public static class LocationMap
{
    private const long Missing = -1;
    private static Dictionary<string, long> _nameToId = new();

    private class IdsFile
    {
        public Dictionary<string, long> items;
        public Dictionary<string, long> locations;
    }

    public static void Load()
    {
        try
        {
            string path = Path.Combine(
                MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_ids.json");
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning($"LocationMap: id table not found at {path}");
                return;
            }
            var root = JsonConvert.DeserializeObject<IdsFile>(File.ReadAllText(path));
            _nameToId = root?.locations ?? new Dictionary<string, long>();
            Plugin.Log.LogInfo($"LocationMap: loaded {_nameToId.Count} locations.");
        }
        catch (System.Exception e) { Plugin.Log.LogError($"LocationMap.Load: {e}"); }
    }

    public static long ClearId(string scene) => Lookup(scene, " - Clear");
    public static long CrownId(string scene) => Lookup(scene, " - Crown");

    /// <summary>Resolve a full AP location name to its id (-1 if unknown). Used by
    /// ChestGate, whose locations aren't scene+suffix.</summary>
    public static long IdByName(string name) =>
        name != null && _nameToId.TryGetValue(name, out var id) ? id : Missing;

    private static long Lookup(string scene, string suffix)
    {
        if (scene == null) return Missing;
        return _nameToId.TryGetValue(scene + suffix, out var id) ? id : Missing;
    }
}
