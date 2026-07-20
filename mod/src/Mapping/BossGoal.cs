using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace WtgArchipelago.Mapping;

/// <summary>
/// The "all bosses" goal (Goal.all_bosses). The slot is won only when EVERY
/// campaign boss is defeated -- the 7 computer HoleInOne bosses AND the Final
/// boss -- not just the last one. We track each boss clear (by scene) and report
/// victory once all required bosses are down.
///
/// The boss set is authoritative from the apworld: wtg_ids.json lists every boss
/// scene (<c>boss_scenes</c>) plus the final boss scene (<c>final_boss_scene</c>).
/// A boss counts as defeated when its level completes
/// (GamePatches.LevelCompletePostfix -> RegisterDefeat) or, for the final boss,
/// on GameAnalytics.OnFinalBossCompleted (-> RegisterFinalBoss). Both paths are
/// idempotent (a HashSet), so covering the final boss twice is harmless.
///
/// Only active when the seed's goal is all_bosses (slot data). Otherwise every
/// call is a no-op and victory is reported the normal way (campaign: on the final
/// boss; door %: via Flag count).
/// </summary>
public static class BossGoal
{
    private static bool _enabled;
    private static bool _won;
    private static string _finalBossScene = "Final boss";
    private static readonly HashSet<string> _required = new();
    private static readonly HashSet<string> _defeated = new();

    private class IdsFile
    {
        public List<string> boss_scenes;
        public string final_boss_scene;
    }

    public static void Load()
    {
        try
        {
            string path = Path.Combine(
                MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_ids.json");
            if (!File.Exists(path)) { Plugin.Log.LogWarning("BossGoal: wtg_ids.json missing"); return; }
            var root = JsonConvert.DeserializeObject<IdsFile>(File.ReadAllText(path));
            _required.Clear();
            if (root?.boss_scenes != null)
                foreach (var s in root.boss_scenes)
                    if (!string.IsNullOrEmpty(s)) _required.Add(s);
            if (!string.IsNullOrEmpty(root?.final_boss_scene))
                _finalBossScene = root.final_boss_scene;
            Plugin.Log.LogInfo($"BossGoal: {_required.Count} boss scenes loaded.");
        }
        catch (Exception e) { Plugin.Log.LogError($"BossGoal.Load: {e}"); }
    }

    /// <summary>Enable when the seed's goal is all_bosses (from slot data).</summary>
    public static void SetEnabled(bool on)
    {
        _enabled = on;
        Plugin.Log.LogInfo($"BossGoal: {(on ? "ENABLED (all bosses)" : "disabled")}.");
    }

    /// <summary>Register the final boss defeat (from OnFinalBossCompleted).</summary>
    public static void RegisterFinalBoss() => RegisterDefeat(_finalBossScene);

    /// <summary>Register a boss defeat by scene. No-op unless all_bosses is the
    /// goal and the scene is a known boss. Reports victory once every boss is down.</summary>
    public static void RegisterDefeat(string scene)
    {
        if (!_enabled || _won || string.IsNullOrEmpty(scene)) return;
        if (!_required.Contains(scene)) return;
        if (!_defeated.Add(scene)) return;   // already counted

        Plugin.Log.LogInfo($"[GOAL] boss defeated: '{scene}' ({_defeated.Count}/{_required.Count})");
        if (_defeated.Count >= _required.Count)
        {
            _won = true;
            Plugin.Client?.SendVictory();
            Plugin.Log.LogInfo("[GOAL] all bosses defeated -> victory reported");
        }
    }
}
