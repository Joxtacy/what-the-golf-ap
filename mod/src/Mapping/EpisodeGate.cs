using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Episode gating (the "episodes" option). Each enabled episode (Sporty Sports /
/// Snow / Hotdog / Alive / Among Us) is a separate in-engine <c>ContentPack</c>,
/// entered from the episodes-hub side room. Episodes have NO computer doors, so the
/// Main <c>SetState</c> plate lever does not apply. Instead we gate at the single
/// synchronous entry choke point: a prefix on <c>BasePackStarter.StartPack(ContentPack,
/// object[])</c> (see <see cref="Patches.GamePatches"/>).
///
/// The prefix vetoes that call (sets <c>__result=false</c> + skips the original -> nothing
/// starts, the player stays in the hub) whenever the
/// target pack is a LOCKED episode: one this seed enabled whose "&lt;name&gt; Episode
/// Access" item hasn't arrived. The pack is identified by its stable
/// <c>contentPackID</c> (e.g. CP_SNOWY_SNOW), captured live via EpisodeProbe and
/// exported by the apworld (episode_pack_by_item / episode_pack_by_name in wtg_ids.json).
///
/// Only episodes the seed enabled are gated (from slot data "episodes"); episodes not
/// in the seed aren't randomized, so they stay freely playable. The veto is the
/// authoritative (and only) gate — no per-frame Tick / self-heal needed.
///
/// NOTE: there is no native "greyed/locked" visual for the in-world episode entrances to
/// reuse. <c>EpisodeEntranceManager.Start()</c> (the hub portal) only reads play-count /
/// IsFresh / SavePosition to drive its new/complete/progress banners — it never checks
/// <c>ContentPackDef.accessible</c> or ownership. That <c>accessible</c> flag only drives
/// the main-menu mode-select carousel (the game's own CP_SOON_FAKE "Coming Soon" tile);
/// setting it on an episode pack greys nothing in the hub. So the lock is communicated
/// functionally (the veto + a MessageFeed line), not by a portal grey-out.
/// </summary>
public static class EpisodeGate
{
    private static Dictionary<string, string> _packByItem = new();  // access item -> packId
    private static Dictionary<string, string> _packByName = new();  // episode name -> packId
    private static readonly Dictionary<string, string> _nameByPack = new(); // packId -> episode name
    private static readonly HashSet<string> Gated = new();     // packIds enabled by this seed
    private static readonly HashSet<string> Unlocked = new();  // packIds whose key arrived
    private static readonly HashSet<string> _loggedBlock = new();

    private class IdsFile
    {
        public Dictionary<string, string> episode_pack_by_item;
        public Dictionary<string, string> episode_pack_by_name;
    }

    public static void Load()
    {
        try
        {
            string path = Path.Combine(
                MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_ids.json");
            if (!File.Exists(path)) { Plugin.Log.LogWarning("EpisodeGate: wtg_ids.json missing"); return; }
            var root = JsonConvert.DeserializeObject<IdsFile>(File.ReadAllText(path));
            _packByItem = root?.episode_pack_by_item ?? new Dictionary<string, string>();
            _packByName = root?.episode_pack_by_name ?? new Dictionary<string, string>();
            _nameByPack.Clear();
            foreach (var kv in _packByName)
                if (!string.IsNullOrEmpty(kv.Value)) _nameByPack[kv.Value] = kv.Key;
            Plugin.Log.LogInfo($"EpisodeGate: {_packByName.Count} episode packs mapped.");
        }
        catch (Exception e) { Plugin.Log.LogError($"EpisodeGate.Load: {e}"); }
    }

    /// <summary>Set which episodes this seed enabled (their display names, from slot
    /// data "episodes"); only those packs are gated. Called once on connect.</summary>
    public static void SetEnabled(IEnumerable<string> episodeNames)
    {
        Gated.Clear();
        if (episodeNames != null)
            foreach (var name in episodeNames)
                if (name != null && _packByName.TryGetValue(name, out var pack)
                    && !string.IsNullOrEmpty(pack))
                    Gated.Add(pack);
        Plugin.Log.LogInfo(Gated.Count > 0
            ? $"EpisodeGate: gating {Gated.Count} episode(s): {string.Join(", ", Gated)}"
            : "EpisodeGate: no episodes enabled (nothing gated).");
    }

    /// <summary>Is this an Episode Access item we handle?</summary>
    public static bool Handles(string itemName) => _packByItem.ContainsKey(itemName);

    /// <summary>Mark an episode unlocked from its received "&lt;name&gt; Episode Access".</summary>
    public static void Unlock(string itemName)
    {
        if (_packByItem.TryGetValue(itemName, out var pack) && !string.IsNullOrEmpty(pack)
            && Unlocked.Add(pack))
            Plugin.Log.LogInfo($"[EPISODE] '{itemName}' -> pack {pack} unlocked");
    }

    /// <summary>Is entering this ContentPack currently blocked (a seed-enabled episode
    /// whose Access key hasn't arrived)? False for the hub, Main, and non-seed packs.</summary>
    public static bool IsBlocked(string packId) =>
        !string.IsNullOrEmpty(packId) && Gated.Contains(packId) && !Unlocked.Contains(packId);

    /// <summary>Episode display name for a pack id (for user-facing messages).</summary>
    public static string NameOf(string packId) =>
        (packId != null && _nameByPack.TryGetValue(packId, out var n)) ? n : packId;

    /// <summary>Log a blocked entry once per pack (keeps the log clean across retries).</summary>
    public static bool ShouldLogBlock(string packId) => _loggedBlock.Add(packId);
}
