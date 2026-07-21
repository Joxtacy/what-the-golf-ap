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
    }

    public static void Save() => MelonPreferences.Save();
}
