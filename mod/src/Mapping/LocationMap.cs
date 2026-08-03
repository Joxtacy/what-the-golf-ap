using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Maps a level's SCENE name to Archipelago location IDs, using the id table
/// exported from the apworld (tools/export_ids.py -> ids.json, deployed as
/// wtg_ids.json in the game root). AP location names are "{display} - Clear" and
/// "{display} - Crown", where the apworld renames the raw scene to a human display
/// name. The game only reports the raw scene, so we translate scene -> display via
/// the exported name_by_scene map, then append the suffix and look up the id.
/// </summary>
public static class LocationMap
{
    private const long Missing = -1;
    private static Dictionary<string, long> _nameToId = new();
    private static Dictionary<string, string> _nameByScene = new();
    // CurrentArea lookups: "which chamber / sub-area is the player in", so a
    // PopTracker pack can auto-switch to that chamber's map tab.
    private static Dictionary<string, string> _subareaByScene = new();
    private static Dictionary<string, string> _chamberBySubarea = new();
    private static Dictionary<string, string> _subareaBySaveSpot = new();
    private static Dictionary<string, string> _episodeByCampaign = new();

#pragma warning disable 0649 // fields assigned by Newtonsoft.Json via reflection
    private class IdsFile
    {
        public Dictionary<string, long> items;
        public Dictionary<string, long> locations;
        public Dictionary<string, string> name_by_scene;
        public Dictionary<string, string> subarea_by_scene;
        public Dictionary<string, string> chamber_by_subarea;
        public Dictionary<string, string> subarea_by_savespot;
        public Dictionary<string, string> episode_by_campaign;
    }
#pragma warning restore 0649

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
            _nameByScene = root?.name_by_scene ?? new Dictionary<string, string>();
            // Older wtg_ids.json files predate these -- absent is fine, CurrentArea
            // just falls through to a coarser signal.
            _subareaByScene = root?.subarea_by_scene ?? new Dictionary<string, string>();
            _chamberBySubarea = root?.chamber_by_subarea ?? new Dictionary<string, string>();
            _subareaBySaveSpot = root?.subarea_by_savespot ?? new Dictionary<string, string>();
            _episodeByCampaign = root?.episode_by_campaign ?? new Dictionary<string, string>();
            Plugin.Log.LogInfo(
                $"LocationMap: loaded {_nameToId.Count} locations, "
                + $"{_nameByScene.Count} scene names, "
                + $"{_subareaBySaveSpot.Count} save spots.");
        }
        catch (System.Exception e) { Plugin.Log.LogError($"LocationMap.Load: {e}"); }
    }

    public static long ClearId(string scene) => Lookup(scene, " - Clear");
    public static long CrownId(string scene) => Lookup(scene, " - Crown");

    /// <summary>Resolve a full AP location name to its id (-1 if unknown). Used by
    /// ChestGate, whose locations aren't scene+suffix.</summary>
    public static long IdByName(string name) =>
        name != null && _nameToId.TryGetValue(name, out var id) ? id : Missing;

    // --- CurrentArea lookups -------------------------------------------------
    /// <summary>Sub-area code a hole lives in, e.g. "SpaceGolf6" -> "08C". Null if
    /// unknown (episode holes aren't in this map -- episodes are one area each).</summary>
    public static string SubAreaOf(string scene) =>
        scene != null && _subareaByScene.TryGetValue(scene, out var s) && s.Length > 0
            ? s : null;

    /// <summary>Chamber code for a sub-area, e.g. "08C" -> "08". This is the key the
    /// tracker routes a map tab from.</summary>
    public static string ChamberOf(string subarea) =>
        subarea != null && _chamberBySubarea.TryGetValue(subarea, out var c) ? c : null;

    /// <summary>Sub-area for a SaveGame.SavePosition respawn id, e.g.
    /// "SAVE_space_01" -> "08C". The 21 spots are 1:1 with the sub-areas.</summary>
    public static string SubAreaOfSaveSpot(string spot) =>
        spot != null && _subareaBySaveSpot.TryGetValue(spot, out var s) ? s : null;

    /// <summary>Episode display name for a campaign tag, e.g. "Olympics" ->
    /// "Sporty Sports". Null for "Main" (the base campaign).</summary>
    public static string EpisodeOfCampaign(string campaign) =>
        campaign != null && _episodeByCampaign.TryGetValue(campaign, out var e) ? e : null;

    /// <summary>The display name the apworld gave this scene (falls back to the raw
    /// scene if the map is missing it, matching the apworld's own fallback).</summary>
    private static string Display(string scene) =>
        scene != null && _nameByScene.TryGetValue(scene, out var d) ? d : scene;

    private static long Lookup(string scene, string suffix)
    {
        if (scene == null) return Missing;
        return _nameToId.TryGetValue(Display(scene) + suffix, out var id) ? id : Missing;
    }
}
