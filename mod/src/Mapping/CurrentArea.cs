using System;
using System.Threading;
using WtgArchipelago.Patches;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Publishes "which chamber / sub-area is the player in" to Archipelago data
/// storage, so a PopTracker pack can auto-switch to that chamber's map tab as you
/// walk or teleport around the overworld.
///
/// Read-only with respect to the game -- it only observes. Does nothing at all
/// while disconnected (the mod stays passive until you connect), and the network
/// write always happens off Unity's main thread: STATUS.md's hardest-won gotcha is
/// that a synchronous send into a dead socket freezes the whole game.
///
/// The tracker cannot write data storage itself (PopTracker's Lua only has
/// Get/SetNotify), which is why the mod has to be the publisher. A tracker
/// connected to an older mod simply never sees the key and falls back to guessing
/// the area from the sub-area prefix on each location check.
/// </summary>
public static class CurrentArea
{
    /// <summary>Payload schema version -- always the first field, so a reader can
    /// bail on a payload it doesn't understand. Fields 2 and 3 (area, subarea) are
    /// the frozen contract; anything after them may grow.</summary>
    public const int PayloadVersion = 1;

    // The payload is a pipe-delimited STRING, not JSON, for two reasons:
    //
    //  * DataStorageElement's JToken conversion is unusable here. The mod compiles
    //    against MelonLoader's Newtonsoft.Json, while Archipelago.MultiClient.Net
    //    bundles its own -- two different assembly identities for JToken, so the
    //    implicit operator simply doesn't apply (CS0029) and no cast can bridge it.
    //    String is unambiguous.
    //  * PopTracker's Lua has no JSON parser, so a table payload would need one
    //    shipped in the pack. Splitting on "|" is a one-liner there.
    //
    // Layout:  v | area | subarea | campaign | in_level | src | scene
    private const char Sep = '|';

    private const float EvalInterval = 0.25f;    // recompute at most 4x/sec
    private const float MinPublishGap = 0.75f;   // and publish at most ~1.3x/sec

    private static float _nextEval, _nextPublish;
    private static string _lastKey;              // change detection
    private static string _pendingJson;          // coalesced latest payload

    public static string Area { get; private set; }
    public static string SubArea { get; private set; }

    public static void Reset()
    {
        _lastKey = null;
        _pendingJson = null;
        _nextEval = 0f;
        _nextPublish = 0f;
        Area = null;
        SubArea = null;
    }

    /// <summary>Main-thread pump. Safe to call unconditionally from Mod.OnUpdate --
    /// it self-gates on connected + the preference and costs a cached-singleton
    /// read plus a dictionary lookup, nowhere near the dumpers' object sweeps.</summary>
    public static void Tick()
    {
        var client = Plugin.Client;
        if (client == null || !client.Connected) return;
        if (Preferences.PublishArea == null || !Preferences.PublishArea.Value) return;

        float now = UnityEngine.Time.unscaledTime;

        if (now >= _nextEval)
        {
            _nextEval = now + EvalInterval;
            Evaluate();
        }

        if (_pendingJson != null && now >= _nextPublish)
        {
            _nextPublish = now + MinPublishGap;
            string json = _pendingJson;
            _pendingJson = null;
            Publish(client, json);
        }
    }

    /// <summary>Resolve the current area from the best signal available. Never
    /// overwrites a good value with "unknown" -- if nothing resolves we hold the
    /// last one, which is what you want while a scene transition is in flight.</summary>
    private static void Evaluate()
    {
        string campaign = SafeCampaign();
        string episode = LocationMap.EpisodeOfCampaign(campaign);
        string scene = null, subarea = null, area = null, src = null;
        bool inLevel = SafeIsInLevel();

        // 1. Inside a hole: the scene is exact and free to read.
        if (inLevel)
        {
            scene = SafeCurrentScene();
            subarea = LocationMap.SubAreaOf(scene);
            if (subarea != null)
            {
                area = LocationMap.ChamberOf(subarea);
                src = "scene";
            }
        }

        // 2. Main overworld: the SavePosition respawn id maps 1:1 to a sub-area.
        //    Coarse (it lags behind where you're standing) but needs no dumped
        //    coordinates, so it works today.
        if (area == null && episode == null)
        {
            string spot = SafeSavePosition();
            subarea = LocationMap.SubAreaOfSaveSpot(spot);
            if (subarea != null)
            {
                area = LocationMap.ChamberOf(subarea);
                src = "save";
            }
        }

        // 3. Episode overworld: the campaign IS the area (one tab per episode).
        if (area == null && episode != null)
        {
            area = episode;
            src = "campaign";
        }

        if (area == null) return;

        string key = campaign + "|" + area + "|" + subarea + "|" + (inLevel ? 1 : 0);
        if (key == _lastKey) return;             // only publish on a real change
        _lastKey = key;
        Area = area;
        SubArea = subarea;

        _pendingJson = string.Join(Sep.ToString(), new[]
        {
            PayloadVersion.ToString(),
            Field(area),
            Field(subarea),
            Field(campaign),
            inLevel ? "1" : "0",
            Field(src),
            Field(scene),
        });
    }

    /// <summary>One payload field: never null, never contains the separator.</summary>
    private static string Field(string s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace(Sep, '/');

    /// <summary>The data-storage key. Includes the TEAM as well as the slot:
    /// MultiClient's Scope.Slot builds "Slot:{slot}:{key}" with no team component,
    /// and slot numbers repeat across teams, so it would collide in a multi-team
    /// room. An explicit key is also the only thing the Lua side can reproduce
    /// without depending on the client library's internal formatting.</summary>
    private static string KeyFor(int team, int slot) =>
        $"WTG:CurrentArea:{team}:{slot}";

    private static void Publish(ArchipelagoClient client, string payload)
    {
        var session = client.Session;
        if (session == null) return;

        int team, slot;
        try
        {
            team = session.ConnectionInfo.Team;
            slot = session.ConnectionInfo.Slot;
        }
        catch { return; }
        if (slot < 0) return;
        if (team < 0) team = 0;
        string key = KeyFor(team, slot);

        // NEVER from the main thread -- see the class docs.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                session.DataStorage[key] = payload;
                Plugin.Log.LogInfo($"[AREA] {key} <- {payload}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AREA] publish failed: {e.Message}");
            }
        });
    }

    // --- guarded game reads --------------------------------------------------
    private static string SafeCampaign()
    {
        try { return CampaignInfo.Current(); }
        catch { return null; }
    }

    private static bool SafeIsInLevel()
    {
        try { return GameState.IsInLevel(); }
        catch { return false; }
    }

    private static string SafeCurrentScene()
    {
        try { return GameState.CurrentLevelScene(); }
        catch { return null; }
    }

    /// <summary>SaveGame.SavePosition -- the current respawn-spot id. Documented in
    /// mod/REVERSE_ENGINEERING.md but not read anywhere else in the mod, so it is
    /// wrapped: a wrong member name degrades to the campaign tier, not a crash.</summary>
    private static string SafeSavePosition()
    {
        try { return Il2Cpp.SaveGame.SavePosition; }
        catch { return null; }
    }
}
