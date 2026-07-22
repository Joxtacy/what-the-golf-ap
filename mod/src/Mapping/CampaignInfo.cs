using System;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Reads the currently-active campaign/episode so the data dumpers can TAG every
/// record with the episode it came from.
///
/// WHAT THE GOLF ships one base campaign (Main) plus several "episode" campaigns —
/// Olympics (= Sporty Sports), Snow, Hotdog, Alive, Amongus — each a separate
/// ContentPack/overworld with its OWN OverworldLevelData, doors, chests and
/// (crucially) its own section numbering. Without a campaign tag, sections like
/// "01"/"08A" from two episodes collide when the dumpers accumulate several
/// overworld walks into one file.
///
/// Only ONE campaign's overworld data is loaded at a time — confirmed: the
/// base-game section pass captured exactly Main's 21 sections, not every
/// campaign's — so the active campaign read here correctly labels whatever a given
/// pass harvests.
///
/// Source: Il2Cpp.SaveGame.currentCampaignDef (a CampaignDef with .type / .packID),
/// falling back to SaveGame.LastPlayedCampaignType. Returns a short stable tag (the
/// ECampaignType name); "unknown" if nothing is resolvable yet.
/// </summary>
public static class CampaignInfo
{
    // ECampaignType (from the il2cpp dump): None=0 Main=1 Olympics=2 Snow=3
    // Hotdog=4 Hub=5 Alive=6 Amongus=7. Mapped by the integer value so it is
    // independent of how Il2CppInterop projects the enum.
    private static string Name(int v) => v switch
    {
        1 => "Main",
        2 => "Olympics",
        3 => "Snow",
        4 => "Hotdog",
        5 => "Hub",
        6 => "Alive",
        7 => "Amongus",
        _ => "None",
    };

    /// <summary>Short tag for the active campaign, e.g. "Main" / "Olympics".
    /// "unknown" if no campaign is resolvable yet (nothing loaded).</summary>
    public static string Current()
    {
        // Preferred: the live campaign def the game switched to on entering an overworld.
        try
        {
            var def = Il2Cpp.SaveGame.currentCampaignDef;
            if (def != null)
            {
                int v = (int)def.type;
                if (v != 0) return Name(v);
            }
        }
        catch { }
        // Fallback: last-played campaign type (set even before the def is populated).
        try
        {
            int v = (int)Il2Cpp.SaveGame.LastPlayedCampaignType;
            if (v != 0) return Name(v);
        }
        catch { }
        return "unknown";
    }
}
