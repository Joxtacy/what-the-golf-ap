using UnityEngine;

namespace WtgArchipelago;

/// <summary>
/// A minimal in-game IMGUI panel to connect/disconnect to an Archipelago server,
/// replacing the old hard-coded Connect(). Toggle it with F8.
///
/// Uses the low-level GUI.* API (fixed-arg overloads) rather than GUILayout —
/// GUILayout's `params GUILayoutOption[]` methods are awkward through
/// Il2CppInterop, whereas GUI.Box/Label/TextField/Button/Toggle all have clean
/// Rect-based overloads. The F8 hotkey is read from Event.current (IMGUI's own
/// event stream), which works regardless of whether the game uses the legacy or
/// new input backend.
/// </summary>
public static class ConnectionUI
{
    public static bool Visible;

    private static bool _paused;
    private static float _prevTimeScale = 1f;

    private static bool _init;
    private static string _host = "localhost";
    private static string _port = "38281";
    private static string _slot = "Player1";
    private static string _pass = "";

    /// <summary>Seed the editable fields from persisted preferences.</summary>
    public static void Init()
    {
        _host = Preferences.Host.Value;
        _port = Preferences.Port.Value.ToString();
        _slot = Preferences.Slot.Value;
        _pass = Preferences.Password.Value ?? "";
        _init = true;
    }

    /// <summary>
    /// Freeze the game while the panel is open so stray clicks / mouse motion don't
    /// drive the menu ball behind it (IMGUI captures the mouse for its own controls,
    /// but the game still polls the raw mouse). Call once per frame from Mod.OnUpdate.
    /// Restores the prior timeScale on close, so it composes with the game's own pause.
    /// </summary>
    public static void UpdatePause()
    {
        try
        {
            // Freeze while EITHER overlay is open (they share one timeScale owner so
            // closing one while the other stays open doesn't unfreeze prematurely).
            bool want = Visible || ConsoleUI.Visible;
            if (want && !_paused)
            {
                _prevTimeScale = UnityEngine.Time.timeScale;
                UnityEngine.Time.timeScale = 0f;
                _paused = true;
            }
            else if (!want && _paused)
            {
                // Never restore a frozen scale: if the game happened to be at 0 when
                // we opened (its own pause / a load), restoring 0 would leave the game
                // stuck. Fall back to normal speed in that case.
                UnityEngine.Time.timeScale = _prevTimeScale > 0f ? _prevTimeScale : 1f;
                _paused = false;
            }
        }
        catch { }
    }

    /// <summary>Called from Mod.OnGUI: handle the toggle hotkey, then draw.</summary>
    public static void OnGUI()
    {
        try
        {
            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.F8)
                Visible = !Visible;
        }
        catch { }

        Draw();
    }

    private static void Draw()
    {
        if (!Visible || !_init) return;
        try
        {
            const float x = 20f, y = 20f, w = 320f, rowH = 22f, gap = 6f;
            float h = 732f;
            GUI.Box(new Rect(x, y, w, h), "WHAT THE GOLF?  —  Archipelago");

            float ix = x + 14f, iw = w - 28f, cy = y + 34f;

            cy = Field(ix, cy, iw, rowH, gap, "Host", ref _host);
            cy = Field(ix, cy, iw, rowH, gap, "Port", ref _port);
            cy = Field(ix, cy, iw, rowH, gap, "Slot name", ref _slot);
            cy = Field(ix, cy, iw, rowH, gap, "Password (optional)", ref _pass);

            bool ac = Preferences.AutoConnect.Value;
            bool nac = GUI.Toggle(new Rect(ix, cy, iw, rowH), ac, " Auto-connect on launch");
            if (nac != ac) { Preferences.AutoConnect.Value = nac; Preferences.Save(); }
            cy += rowH + gap;

            // Client cosmetic: slide the DeathLink counter in on each wipe (on) vs.
            // keep it always on screen (off). Takes effect immediately.
            bool ha = Preferences.HudAnimate.Value;
            bool nha = GUI.Toggle(new Rect(ix, cy, iw, rowH), ha, " Animate DeathLink HUD (slide-in)");
            if (nha != ha) { Preferences.HudAnimate.Value = nha; Preferences.Save(); }
            cy += rowH + gap;

            // Flag progress HUD (door_50/75/100 goals). Only actually shows when the
            // connected seed is a door goal; harmless to leave on for other goals.
            bool fh = Preferences.FlagHud.Value;
            bool nfh = GUI.Toggle(new Rect(ix, cy, iw, rowH), fh, " Show Flag progress HUD (door goals)");
            if (nfh != fh) { Preferences.FlagHud.Value = nfh; Preferences.Save(); }
            cy += rowH + gap;

            var client = Plugin.Client;
            bool connected = client != null && client.Connected;
            GUI.Label(new Rect(ix, cy, iw, rowH * 2f), client?.StatusMessage ?? "n/a");
            cy += rowH + gap;

            // Runtime DeathLink on/off (per-session). Only meaningful once connected,
            // since the handler + slot data exist then. Amnesty is seed-controlled.
            var dl = connected ? client.DeathLink : null;
            if (dl != null)
            {
                bool de = dl.Enabled;
                bool nde = GUI.Toggle(new Rect(ix, cy, iw, rowH), de,
                    $" DeathLink  (amnesty {dl.Threshold})");
                if (nde != de) dl.SetEnabled(nde);
            }
            else
            {
                GUI.Label(new Rect(ix, cy, iw, rowH), "DeathLink: connect to toggle");
            }
            cy += rowH + gap;

            float bw = (iw - gap) / 2f;
            if (connected)
            {
                if (GUI.Button(new Rect(ix, cy, bw, rowH + 4f), "Disconnect"))
                    client.Disconnect();
            }
            else if (GUI.Button(new Rect(ix, cy, bw, rowH + 4f), "Connect"))
            {
                DoConnect();
            }
            if (GUI.Button(new Rect(ix + bw + gap, cy, bw, rowH + 4f), "Close"))
                Visible = false;
            cy += rowH + 4f + gap + 6f;

            DrawFeedSection(ix, cy, iw, rowH, gap);
        }
        catch (System.Exception e) { Plugin.Log.LogError($"ConnectionUI.Draw: {e}"); }
    }

    private static readonly string[] CornerNames = { "Top-Left", "Top-Right", "Bottom-Left", "Bottom-Right" };

    /// <summary>The live-feed controls: master switch, per-category filter, and layout
    /// knobs. Every change is persisted immediately and read live by MessageFeed.</summary>
    private static void DrawFeedSection(float x, float y, float w, float rowH, float gap)
    {
        GUI.Label(new Rect(x, y, w, rowH), "— Live feed —");
        y += rowH;

        y = Toggle(x, y, w, rowH, gap, " Show live feed", Preferences.FeedEnabled);

        y = Toggle2(x, y, w, rowH, gap,
            " My items", Preferences.FeedShowMyItems,
            " Sent by me", Preferences.FeedShowSentToOthers);
        y = Toggle2(x, y, w, rowH, gap,
            " Others' items", Preferences.FeedShowOthersItems,
            " Hints", Preferences.FeedShowHints);
        y = Toggle2(x, y, w, rowH, gap,
            " Chat/joins", Preferences.FeedShowChat,
            " DeathLink", Preferences.FeedShowLocal);

        // Corner cycles through the four screen corners.
        int corner = Mathf.Clamp(Preferences.FeedCorner.Value, 0, 3);
        if (GUI.Button(new Rect(x, y, w, rowH), $"Corner: {CornerNames[corner]}"))
        { Preferences.FeedCorner.Value = (corner + 1) % 4; Preferences.Save(); }
        y += rowH + gap;

        y = Toggle(x, y, w, rowH, gap, " Keep messages on screen (no fade)", Preferences.FeedPersist);

        // Box width as a screen-width percentage (5% steps).
        int wpct = Mathf.RoundToInt(Preferences.FeedWidthPct.Value * 100f);
        GUI.Label(new Rect(x, y, w - 92f, rowH), $"Width: {wpct}% of screen");
        if (GUI.Button(new Rect(x + w - 88f, y, 42f, rowH), "-"))
        { Preferences.FeedWidthPct.Value = Mathf.Max(0.1f, Preferences.FeedWidthPct.Value - 0.05f); Preferences.Save(); }
        if (GUI.Button(new Rect(x + w - 42f, y, 42f, rowH), "+"))
        { Preferences.FeedWidthPct.Value = Mathf.Min(0.6f, Preferences.FeedWidthPct.Value + 0.05f); Preferences.Save(); }
        y += rowH + gap;

        y = StepInt(x, y, w, rowH, gap, "Lines", Preferences.FeedMaxLines, 1, 20, 1);
        y = StepFloat(x, y, w, rowH, gap, "Size", Preferences.FeedFontSize, 10f, 60f, 2f, "0");
        StepFloat(x, y, w, rowH, gap, "Seconds", Preferences.FeedSeconds, 1f, 60f, 1f, "0");
    }

    private static float Toggle(float x, float y, float w, float rowH, float gap,
        string label, MelonLoader.MelonPreferences_Entry<bool> entry)
    {
        bool v = entry.Value;
        bool nv = GUI.Toggle(new Rect(x, y, w, rowH), v, label);
        if (nv != v) { entry.Value = nv; Preferences.Save(); }
        return y + rowH + gap;
    }

    private static float Toggle2(float x, float y, float w, float rowH, float gap,
        string la, MelonLoader.MelonPreferences_Entry<bool> a,
        string lb, MelonLoader.MelonPreferences_Entry<bool> b)
    {
        float bw = (w - gap) / 2f;
        bool av = a.Value, nav = GUI.Toggle(new Rect(x, y, bw, rowH), av, la);
        if (nav != av) { a.Value = nav; Preferences.Save(); }
        bool bv = b.Value, nbv = GUI.Toggle(new Rect(x + bw + gap, y, bw, rowH), bv, lb);
        if (nbv != bv) { b.Value = nbv; Preferences.Save(); }
        return y + rowH + gap;
    }

    private static float StepInt(float x, float y, float w, float rowH, float gap,
        string label, MelonLoader.MelonPreferences_Entry<int> entry, int min, int max, int step)
    {
        GUI.Label(new Rect(x, y, w - 92f, rowH), $"{label}: {entry.Value}");
        if (GUI.Button(new Rect(x + w - 88f, y, 42f, rowH), "-"))
        { entry.Value = Mathf.Max(min, entry.Value - step); Preferences.Save(); }
        if (GUI.Button(new Rect(x + w - 42f, y, 42f, rowH), "+"))
        { entry.Value = Mathf.Min(max, entry.Value + step); Preferences.Save(); }
        return y + rowH + gap;
    }

    private static float StepFloat(float x, float y, float w, float rowH, float gap,
        string label, MelonLoader.MelonPreferences_Entry<float> entry, float min, float max, float step, string fmt)
    {
        GUI.Label(new Rect(x, y, w - 92f, rowH), $"{label}: {entry.Value.ToString(fmt)}");
        if (GUI.Button(new Rect(x + w - 88f, y, 42f, rowH), "-"))
        { entry.Value = Mathf.Max(min, entry.Value - step); Preferences.Save(); }
        if (GUI.Button(new Rect(x + w - 42f, y, 42f, rowH), "+"))
        { entry.Value = Mathf.Min(max, entry.Value + step); Preferences.Save(); }
        return y + rowH + gap;
    }

    /// <summary>Draw a labelled text field; returns the next y offset.</summary>
    private static float Field(float x, float y, float w, float rowH, float gap, string label, ref string value)
    {
        GUI.Label(new Rect(x, y, w, rowH), label);
        y += rowH;
        value = GUI.TextField(new Rect(x, y, w, rowH), value ?? "");
        return y + rowH + gap;
    }

    private static void DoConnect()
    {
        if (!int.TryParse(_port, out int port))
        {
            Plugin.Log.LogWarning($"ConnectionUI: invalid port '{_port}'");
            return;
        }
        // Persist the entered details for next launch.
        Preferences.Host.Value = _host;
        Preferences.Port.Value = port;
        Preferences.Slot.Value = _slot;
        Preferences.Password.Value = _pass ?? "";
        Preferences.Save();

        Plugin.Client?.Connect(_host, port, _slot, string.IsNullOrEmpty(_pass) ? null : _pass);
    }
}
