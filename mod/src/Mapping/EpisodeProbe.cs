using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WtgArchipelago.Mapping;

/// <summary>
/// DEV/TEST probe (read-only) for designing episode-key ENFORCEMENT.
///
/// Episodes (Olympics/Snow/Hotdog/Alive/Amongus) are separate <c>ContentPack</c>s,
/// NOT gated by the Main <c>OverworldMainDoorPlate.SetState</c> lever (episodes have
/// no computer doors). The dump shows the registry the game itself uses:
///   RuntimeStoreData.instance.contentPacksDefs : List&lt;ContentPackDef&gt;
///   ContentPackDef { ContentPack pack; bool visible; bool accessible; }
///   ContentPack    { string contentPackID; ECampaignType campaignType; ... }
/// and the load entry point OverworldSceneLoader.LoadOverworld(ContentPack, bool).
///
/// This probe answers the two open questions before we build the gate:
///   (1) DUMP contentPacksDefs once (it's a fully-loaded ScriptableObject list, so a
///       single pass captures every pack) -> wtg_episodes.json, giving us each
///       episode's contentPackID <-> campaignType <-> visible/accessible. That's the
///       packID<->episode-name mapping the apworld side needs.
///   (2) LOG every LoadOverworld call (see GamePatches.LoadOverworldLogPrefix) so we
///       can confirm HOW an episode is actually entered in this build (menu vs hub
///       portal vs resume) and that LoadOverworld is the choke point to gate on.
///
/// Purely read-only: it never writes accessible/visible or vetoes a load. Gated by
/// <see cref="Mod.EpisodeProbeEnabled"/> (OFF by default). Remove once the gate is built.
/// </summary>
public static class EpisodeProbe
{
    private static bool _dumped;

    private static string OutPath =>
        Path.Combine(MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtg_episodes.json");

    /// <summary>Dump the whole content-pack registry once. Cheap after the first
    /// successful write (guarded by _dumped). Call periodically from OnUpdate.</summary>
    public static void Dump()
    {
        if (_dumped) return;
        try
        {
            var store = Il2Cpp.RuntimeStoreData.instance;
            if (store == null) return;
            var defs = store.contentPacksDefs;
            if (defs == null || defs.Count == 0) return;

            var lines = new List<string>();
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def == null) continue;
                var pack = def.pack;

                string packId = null, packName = null, titleId = null;
                int campaign = 0;
                try { packId = pack != null ? pack.contentPackID : null; } catch { }
                try { campaign = pack != null ? (int)pack.campaignType : 0; } catch { }
                try { packName = pack != null ? pack.PackName : null; } catch { }
                try { titleId = pack != null ? pack.TitleID : null; } catch { }

                lines.Add("{" +
                    $"\"packId\":{J(packId)}," +
                    $"\"campaign\":{campaign}," +
                    $"\"campaignName\":{J(CampaignInfo.NameOf(campaign))}," +
                    $"\"visible\":{(def.visible ? "true" : "false")}," +
                    $"\"accessible\":{(def.accessible ? "true" : "false")}," +
                    $"\"packName\":{J(packName)}," +
                    $"\"titleId\":{J(titleId)}" +
                    "}");
            }

            if (lines.Count == 0) return;

            var sb = new StringBuilder("[\n");
            for (int i = 0; i < lines.Count; i++)
                sb.Append("  ").Append(lines[i]).Append(i + 1 < lines.Count ? ",\n" : "\n");
            sb.Append("]\n");
            File.WriteAllText(OutPath, sb.ToString());

            _dumped = true;
            Plugin.Log.LogInfo($"[EPISODES] dumped {lines.Count} content packs -> {OutPath}");
        }
        catch (Exception e) { Plugin.Log.LogError($"EpisodeProbe.Dump: {e}"); }
    }

    private static string J(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < 0x20) sb.Append(' ');
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }
}
