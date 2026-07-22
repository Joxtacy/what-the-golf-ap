using MelonLoader;

namespace WtgArchipelago;

/// <summary>
/// Persisted connection settings (MelonLoader writes these to
/// &lt;game&gt;\UserData\MelonPreferences.cfg). Populated once at load; edited by the
/// in-game connection UI. Keeps the mod passive by default: `AutoConnect` is off,
/// so a freshly installed mod never touches the game until you opt in.
/// </summary>
public static class Preferences
{
    public static MelonPreferences_Category Category;
    public static MelonPreferences_Entry<string> Host;
    public static MelonPreferences_Entry<int> Port;
    public static MelonPreferences_Entry<string> Slot;
    public static MelonPreferences_Entry<string> Password;
    public static MelonPreferences_Entry<bool> AutoConnect;
    public static MelonPreferences_Entry<bool> HudAnimate;

    // --- Live event feed (see MessageFeed) -----------------------------------
    // A scrolling on-screen log of Archipelago traffic (items found/sent, hints,
    // chat, DeathLink...), rendered in the game's own font. Master switch plus a
    // per-category filter and layout knobs -- all live-editable from the F8 panel.
    public static MelonPreferences_Entry<bool> FeedEnabled;
    public static MelonPreferences_Entry<bool> FeedShowMyItems;      // items sent TO me
    public static MelonPreferences_Entry<bool> FeedShowSentToOthers; // items my checks send to others
    public static MelonPreferences_Entry<bool> FeedShowOthersItems;  // traffic between other players
    public static MelonPreferences_Entry<bool> FeedShowHints;        // hint messages
    public static MelonPreferences_Entry<bool> FeedShowChat;         // chat + server notices + join/leave/goal
    public static MelonPreferences_Entry<bool> FeedShowLocal;        // local events (DeathLink)
    public static MelonPreferences_Entry<int> FeedMaxLines;          // how many lines to keep on screen
    public static MelonPreferences_Entry<float> FeedSeconds;         // dwell before a line fades out
    public static MelonPreferences_Entry<float> FeedFontSize;        // line text size
    public static MelonPreferences_Entry<int> FeedCorner;            // 0=TL 1=TR 2=BL 3=BR
    public static MelonPreferences_Entry<bool> FeedPersist;          // keep lines on screen (no fade)
    public static MelonPreferences_Entry<float> FeedWidthPct;        // box width as a fraction of screen width

    public static void Load()
    {
        Category = MelonPreferences.CreateCategory("WtgArchipelago", "WTG Archipelago");
        Host = Category.CreateEntry("host", "localhost", "Server host");
        Port = Category.CreateEntry("port", 38281, "Server port");
        Slot = Category.CreateEntry("slot", "Player1", "Slot name");
        Password = Category.CreateEntry("password", "", "Password");
        AutoConnect = Category.CreateEntry("autoConnect", false, "Auto-connect on launch");
        // DeathLink HUD: true = slide the counter in from the left on each wipe, hold,
        // then slide out (unobtrusive); false = keep it always on screen.
        HudAnimate = Category.CreateEntry("hudAnimate", true, "Animate DeathLink HUD (slide in on death)");
        // NOTE: the DeathLink outgoing throttle (wipes per sent death) is NOT a client
        // preference -- it's the apworld's `death_link_amnesty` option, delivered via
        // slot data (see ArchipelagoData.DeathLinkAmnesty), so the seed owns it.

        // Live feed. Defaults: on, showing MY items + the items I hand to others
        // (the two lines you care about most); everything else off so a fresh install
        // is quiet. All toggleable at runtime.
        FeedEnabled = Category.CreateEntry("feedEnabled", true, "Show live event feed");
        FeedShowMyItems = Category.CreateEntry("feedMyItems", true, "Feed: items I receive");
        FeedShowSentToOthers = Category.CreateEntry("feedSentToOthers", true, "Feed: items I send to others");
        FeedShowOthersItems = Category.CreateEntry("feedOthersItems", false, "Feed: items between other players");
        FeedShowHints = Category.CreateEntry("feedHints", false, "Feed: hints");
        FeedShowChat = Category.CreateEntry("feedChat", false, "Feed: chat / server / joins / goals");
        FeedShowLocal = Category.CreateEntry("feedLocal", false, "Feed: local events (DeathLink)");
        FeedMaxLines = Category.CreateEntry("feedMaxLines", 6, "Feed: max lines on screen");
        FeedSeconds = Category.CreateEntry("feedSeconds", 8f, "Feed: seconds a line stays before fading");
        FeedFontSize = Category.CreateEntry("feedFontSize", 22f, "Feed: text size");
        FeedCorner = Category.CreateEntry("feedCorner", 2, "Feed: screen corner (0=TL 1=TR 2=BL 3=BR)");
        FeedPersist = Category.CreateEntry("feedPersist", false, "Feed: keep messages on screen (no fade-out)");
        FeedWidthPct = Category.CreateEntry("feedWidthPct", 0.28f, "Feed: box width as fraction of screen (0.1-0.6)");
    }

    public static void Save() => MelonPreferences.Save();
}
