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
            if (Visible && !_paused)
            {
                _prevTimeScale = UnityEngine.Time.timeScale;
                UnityEngine.Time.timeScale = 0f;
                _paused = true;
            }
            else if (!Visible && _paused)
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

        DrawHud();
        Draw();
    }

    /// <summary>Always-on HUD (independent of the F8 panel) showing the DeathLink
    /// wipe counter — "x/N" — while DeathLink is active. The count loops back to 0
    /// each time it reaches N and a death is broadcast. Hidden unless connected and
    /// DeathLink is enabled for this slot; hidden entirely if outgoing is disabled
    /// (threshold 0).</summary>
    private static void DrawHud()
    {
        try
        {
            var dl = Plugin.Client?.DeathLink;
            if (dl == null || !dl.Enabled || Plugin.Client == null || !Plugin.Client.Connected)
                return;
            if (dl.Threshold <= 0) return;   // outgoing disabled -> nothing to count

            const float w = 128f, h = 30f, margin = 12f;
            float x = Screen.width - w - margin;
            GUI.Box(new Rect(x, margin, w, h), $"☠ DeathLink {dl.WipeCount}/{dl.Threshold}");
        }
        catch { }
    }

    private static void Draw()
    {
        if (!Visible || !_init) return;
        try
        {
            const float x = 20f, y = 20f, w = 320f, rowH = 22f, gap = 6f;
            float h = 356f;
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
        }
        catch (System.Exception e) { Plugin.Log.LogError($"ConnectionUI.Draw: {e}"); }
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
