using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using Archipelago.MultiClient.Net.MessageLog.Messages;

namespace WtgArchipelago;

/// <summary>
/// An in-game Archipelago command console (toggle with the ` / ~ key). Type any chat
/// line or server command -- <c>!hint &lt;item&gt;</c>, <c>!countdown &lt;sec&gt;</c>,
/// <c>!remaining</c>, <c>!players</c>, <c>!admin &lt;server command&gt;</c>, ... -- and
/// it's sent to the server via ArchipelagoClient.Say. Server responses arrive through
/// the MessageLog stream (same source as the feed) and are shown in the scrollback.
///
/// NOTE: <c>!</c>-commands are the CLIENT command set. Bare <c>/send</c> is a HOST
/// console command a client can't run directly; use <c>!admin /send &lt;player&gt;
/// &lt;item&gt;</c> after <c>!admin login &lt;password&gt;</c> (the admin field does the
/// login for you).
///
/// IMGUI, like ConnectionUI: a scroll-view of recent messages plus an input row,
/// quick-command buttons, and an admin-login field. Enter sends; Up/Down recall the
/// command history. Ingest() runs off the network thread (queue only); Tick() drains
/// it on the main thread; Draw() only reads.
///
/// Toggled with the ` / ~ key (also closes with Esc / the Close button). IMGUI
/// delivers a keypress as a separate character event that the toggle handler can't
/// consume, so the backtick would leak into the field; we discard whatever is typed
/// on the exact frame the console opens (_justOpened) to swallow it. Focus only takes
/// on a Repaint pass, so FocusControl is re-issued until then.
/// </summary>
public static class ConsoleUI
{
    public static bool Visible;

    private const int MaxLines = 300;
    private static readonly ConcurrentQueue<string> _incoming = new();
    private static readonly List<string> _lines = new();
    private static string _content = "";
    private static bool _contentDirty;

    private static string _input = "";
    private static string _adminPw = "";
    private static readonly List<string> _history = new();
    private static int _histIdx;

    private static Vector2 _scroll;
    private static bool _pendingScroll;
    private static bool _focusPending;
    private static bool _justOpened;   // discard the toggle char that leaks in on open
    private static GUIStyle _style;

    // --- Ingest / update (off-thread ingest, main-thread drain) --------------

    /// <summary>Queue a server message for the scrollback. Safe off the main thread.</summary>
    public static void Ingest(LogMessage m)
    {
        try { if (m != null) _incoming.Enqueue(MessageFeed.Render(m)); }
        catch { }
    }

    /// <summary>Drain queued lines into the ring buffer. Call from Mod.OnUpdate.</summary>
    public static void Tick()
    {
        bool any = false;
        while (_incoming.TryDequeue(out var line)) { _lines.Add(line); any = true; }
        if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
        if (any) { _contentDirty = true; _pendingScroll = true; }
    }

    private static void Echo(string richLine)
    {
        _lines.Add(richLine);
        if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
        _contentDirty = true;
        _pendingScroll = true;
    }

    // --- Input ---------------------------------------------------------------

    private static void Send(string text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        Echo($"<color=#8899AA>> {Escape(text)}</color>");
        _history.Add(text);
        _histIdx = _history.Count;
        Plugin.Client?.Say(text);
        _input = "";
        _focusPending = true;
    }

    private static void AdminLogin()
    {
        if (string.IsNullOrEmpty(_adminPw)) return;
        Plugin.Client?.Say("!admin login " + _adminPw);
        Echo("<color=#8899AA>> !admin login ****</color>");
    }

    private static void RecallHistory(int dir)
    {
        if (_history.Count == 0) return;
        _histIdx = Mathf.Clamp(_histIdx + dir, 0, _history.Count);
        _input = _histIdx >= _history.Count ? "" : _history[_histIdx];
        _focusPending = true;
    }

    // --- GUI -----------------------------------------------------------------

    /// <summary>Handle the toggle + editing hotkeys, then draw. Called from Mod.OnGUI.</summary>
    public static void OnGUI()
    {
        try
        {
            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown)
            {
                // Backquote/tilde toggles; consume it so it never reaches the game (the
                // char that leaks into the field on open is handled by _justOpened).
                if (e.keyCode == KeyCode.BackQuote)
                {
                    Visible = !Visible;
                    if (Visible) { _justOpened = true; _focusPending = true; }
                    e.Use();
                }
                else if (Visible)
                {
                    switch (e.keyCode)
                    {
                        case KeyCode.Escape: Visible = false; e.Use(); break;
                        case KeyCode.Return:
                        case KeyCode.KeypadEnter: Send(_input); e.Use(); break;
                        case KeyCode.UpArrow: RecallHistory(-1); e.Use(); break;
                        case KeyCode.DownArrow: RecallHistory(1); e.Use(); break;
                    }
                }
            }
        }
        catch { }

        Draw();
    }

    private static void Draw()
    {
        if (!Visible) return;
        try
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
                _style.alignment = TextAnchor.UpperLeft;
                _style.fontSize = 13;
            }

            const float bw = 660f, bh = 440f, pad = 12f, rowH = 26f, gap = 6f;
            float bx = Mathf.Max(20f, Screen.width / 2f - bw / 2f);
            float by = 70f;
            GUI.Box(new Rect(bx, by, bw, bh), "Archipelago Console   (` toggles · Esc closes · Enter sends · ↑/↓ history)");

            float ix = bx + pad, iw = bw - 2f * pad;

            // Rows from the bottom up: admin, quick-buttons, input, then the scroll pane
            // fills the remaining space.
            float yAdmin = by + bh - pad - rowH;
            float yBtns = yAdmin - gap - rowH;
            float yInput = yBtns - gap - rowH;
            float paneTop = by + 30f;
            var paneRect = new Rect(ix, paneTop, iw, yInput - gap - paneTop);

            DrawScrollback(paneRect);

            // Input row: field + Send. On the frame we opened, discard whatever was
            // typed (the "T" character event that leaks past the toggle handler).
            float sendW = 80f;
            GUI.SetNextControlName("apc_input");
            string typed = GUI.TextField(new Rect(ix, yInput, iw - sendW - gap, rowH), _input ?? "");
            _input = _justOpened ? "" : typed;
            if (GUI.Button(new Rect(ix + iw - sendW, yInput, sendW, rowH), "Send")) Send(_input);
            // Focus only actually applies on a Repaint pass -- keep re-issuing until then.
            if (_focusPending)
            {
                GUI.FocusControl("apc_input");
                if (Event.current.type == EventType.Repaint) _focusPending = false;
            }
            if (Event.current.type == EventType.Repaint) _justOpened = false;

            // Quick-command buttons: no-arg ones send immediately, others pre-fill.
            float qw = (iw - 3f * gap) / 4f;
            if (GUI.Button(new Rect(ix + 0 * (qw + gap), yBtns, qw, rowH), "!hint")) { _input = "!hint "; _focusPending = true; }
            if (GUI.Button(new Rect(ix + 1 * (qw + gap), yBtns, qw, rowH), "!countdown")) { _input = "!countdown 10"; _focusPending = true; }
            if (GUI.Button(new Rect(ix + 2 * (qw + gap), yBtns, qw, rowH), "!remaining")) Send("!remaining");
            if (GUI.Button(new Rect(ix + 3 * (qw + gap), yBtns, qw, rowH), "!players")) Send("!players");

            // Admin row: password field + login + close.
            GUI.Label(new Rect(ix, yAdmin, 74f, rowH), "Admin pw:");
            _adminPw = GUI.PasswordField(new Rect(ix + 76f, yAdmin, iw - 76f - 2f * (100f + gap), rowH), _adminPw ?? "", '*');
            if (GUI.Button(new Rect(ix + iw - 2f * 100f - gap, yAdmin, 100f, rowH), "Admin login")) AdminLogin();
            if (GUI.Button(new Rect(ix + iw - 100f, yAdmin, 100f, rowH), "Close")) Visible = false;
        }
        catch (System.Exception e) { Plugin.Log.LogError($"ConsoleUI.Draw: {e}"); }
    }

    private static void DrawScrollback(Rect paneRect)
    {
        if (_contentDirty || _content == null)
        {
            _content = string.Join("\n", _lines);
            _contentDirty = false;
        }

        float viewW = paneRect.width - 18f;   // leave room for the scrollbar
        float h;
        try { h = _style.CalcHeight(new GUIContent(_content), viewW); }
        catch { h = _lines.Count * 16f; }
        float contentH = Mathf.Max(h, paneRect.height);
        var viewRect = new Rect(0f, 0f, viewW, contentH);

        _scroll = GUI.BeginScrollView(paneRect, _scroll, viewRect);
        GUI.Label(viewRect, _content, _style);
        GUI.EndScrollView();

        // Snap to the newest line the frame after one arrives; otherwise respect
        // wherever the user scrolled to.
        if (_pendingScroll)
        {
            _scroll.y = Mathf.Max(0f, contentH - paneRect.height);
            _pendingScroll = false;
        }
    }

    private static string Escape(string s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace("<", "‹");
}
