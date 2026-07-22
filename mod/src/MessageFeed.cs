using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Archipelago.MultiClient.Net.MessageLog.Messages;

namespace WtgArchipelago;

/// <summary>
/// An in-game scrolling feed of Archipelago activity -- the same stream the AP text
/// client shows (items found/sent, hints, chat, joins, goals) plus local mod events
/// (DeathLink). This is the exact source Refunct's AP mod renders: the MultiClient
/// <c>MessageLog.OnMessageReceived</c> stream, previously only written to the log
/// (see ArchipelagoClient.ConnectImpl).
///
/// Rendering reuses the DeathLinkHud approach: a persistent (DontDestroyOnLoad)
/// ScreenSpaceOverlay Canvas holding a pool of TextMeshProUGUI lines drawn in the
/// game's own font, plus a translucent backdrop so the AP dark-palette colors read.
/// Each LogMessage's Parts carry per-segment palette colors, reproduced with TMP
/// &lt;color&gt; rich-text tags so the feed matches the real client.
///
/// Threading: OnMessageReceived arrives OFF Unity's main thread (like every other AP
/// callback), so Ingest only classifies + enqueues into a thread-safe queue. Tick()
/// (called from Mod.OnUpdate on the main thread) drains it and touches Unity objects.
///
/// Config lives in Preferences (master toggle + per-category filter + layout), all
/// live-editable from the F8 panel.
/// </summary>
public static class MessageFeed
{
    private enum Category { MyItems, SentToOthers, OthersItems, Hints, Chat, Local }

    private class Entry
    {
        public string Text;
        public float BornAt;   // Time.unscaledTime when it arrived
    }

    private const float FadeSeconds = 1.0f;     // linear fade-out tail after the dwell
    private const float PanelAlpha = 0.5f;      // backdrop opacity at full strength
    private const float LineHeightFactor = 1.3f;
    private const float Pad = 12f;              // inner padding / corner inset
    private const int MaxPool = 20;             // hard cap on line objects

    private static readonly ConcurrentQueue<string> _incoming = new();
    private static readonly List<Entry> _entries = new();

    private static readonly Color32 Backdrop = new Color32(12, 20, 24, 255);
    private static readonly Color LocalColor = new Color(1f, 0.85f, 0.4f, 1f); // DeathLink etc.

    private static GameObject _root;
    private static RectTransform _panelRt;
    private static UnityEngine.UI.Image _panel;
    private static Il2CppTMPro.TextMeshProUGUI[] _lines;
    private static RectTransform[] _lineRt;
    private static string[] _lineText;   // last string pushed to each label (change detection)
    private static Il2CppTMPro.TMP_FontAsset _font;

    private static bool _failed;
    private static int _builtLines = -1;

    // --- Ingest (off the main thread) ---------------------------------------

    /// <summary>Classify an AP log message, drop it if its category is filtered off,
    /// otherwise render it to a colored string and queue it. Safe off-thread.</summary>
    public static void Ingest(LogMessage m)
    {
        try
        {
            if (m == null) return;
            if (!(Preferences.FeedEnabled?.Value ?? false)) return;
            if (!TryClassify(m, out var cat)) return;
            if (!Allowed(cat)) return;
            _incoming.Enqueue(Render(m));
        }
        catch (Exception e) { Plugin.Log.LogError($"MessageFeed.Ingest: {e.Message}"); }
    }

    /// <summary>Push a locally-generated line (e.g. DeathLink) into the feed. Safe from
    /// any thread. Gated behind the master switch + the "local events" category.</summary>
    public static void PushLocal(string text)
    {
        try
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!(Preferences.FeedEnabled?.Value ?? false)) return;
            if (!(Preferences.FeedShowLocal?.Value ?? false)) return;
            _incoming.Enqueue($"<color=#{Hex(LocalColor)}>{Escape(text)}</color>");
        }
        catch { }
    }

    private static bool TryClassify(LogMessage m, out Category cat)
    {
        cat = Category.Chat;
        // HintItemSendLogMessage derives from ItemSendLogMessage, so test it first.
        if (m is HintItemSendLogMessage) { cat = Category.Hints; return true; }
        if (m is ItemSendLogMessage its)
        {
            bool toMe = its.IsReceiverTheActivePlayer;
            bool fromMe = its.IsSenderTheActivePlayer;
            // A cheat grant (server `/send`) has no real sender player, so both flags
            // can come back false; if it isn't clearly to someone else, it's an item
            // WE received -> "my items", not the (off-by-default) others bucket.
            if (m is ItemCheatLogMessage && !toMe && !fromMe) toMe = true;
            cat = toMe ? Category.MyItems : fromMe ? Category.SentToOthers : Category.OthersItems;
            return true;
        }
        if (m is ChatLogMessage || m is ServerChatLogMessage
            || m is JoinLogMessage || m is LeaveLogMessage
            || m is GoalLogMessage || m is CountdownLogMessage)
        {
            cat = Category.Chat;
            return true;
        }
        // Command results, admin, tags-changed, tutorial: not feed-worthy.
        return false;
    }

    private static bool Allowed(Category c) => c switch
    {
        Category.MyItems => Preferences.FeedShowMyItems.Value,
        Category.SentToOthers => Preferences.FeedShowSentToOthers.Value,
        Category.OthersItems => Preferences.FeedShowOthersItems.Value,
        Category.Hints => Preferences.FeedShowHints.Value,
        Category.Chat => Preferences.FeedShowChat.Value,
        Category.Local => Preferences.FeedShowLocal.Value,
        _ => false,
    };

    /// <summary>Turn a message's colored Parts into a TMP rich-text string, matching
    /// the AP dark palette the real client uses. Shared with the console (ConsoleUI).</summary>
    internal static string Render(LogMessage m)
    {
        var parts = m.Parts;
        if (parts == null) return Escape(m.ToString());
        var sb = new StringBuilder(128);
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p == null) continue;
            var c = p.Color;   // Models.Color (dark-palette RGB for this part)
            sb.Append("<color=#").Append(Hex(c.R)).Append(Hex(c.G)).Append(Hex(c.B)).Append('>')
              .Append(Escape(p.Text)).Append("</color>");
        }
        return sb.ToString();
    }

    // --- Render (main thread) -----------------------------------------------

    /// <summary>Drain the queue, age out old lines, and lay out the feed. Call once per
    /// frame from Mod.OnUpdate.</summary>
    public static void Tick()
    {
        try
        {
            bool enabled = Preferences.FeedEnabled?.Value ?? false;
            float now = Time.unscaledTime;

            // Drain incoming (discard if disabled so re-enabling starts fresh).
            while (_incoming.TryDequeue(out var text))
                if (enabled) _entries.Add(new Entry { Text = text, BornAt = now });

            int maxLines = Mathf.Clamp(Preferences.FeedMaxLines?.Value ?? 6, 1, MaxPool);
            float life = Mathf.Max(1f, Preferences.FeedSeconds?.Value ?? 8f);

            // Age out fully-faded lines (unless "keep on screen" is on), then cap to
            // maxLines (drop oldest). Persistent mode only ever drops via the cap.
            if (!(Preferences.FeedPersist?.Value ?? false))
                for (int i = _entries.Count - 1; i >= 0; i--)
                    if (now - _entries[i].BornAt > life + FadeSeconds) _entries.RemoveAt(i);
            while (_entries.Count > maxLines) _entries.RemoveAt(0);

            if (!enabled || _entries.Count == 0)
            {
                if (_root != null && _root.activeSelf) _root.SetActive(false);
                return;
            }

            if (!EnsureBuilt(maxLines)) return;
            if (!_root.activeSelf) _root.SetActive(true);

            Layout(maxLines, life, now);
        }
        catch (Exception e)
        {
            if (!_failed) { _failed = true; Plugin.Log.LogError($"MessageFeed: {e}"); }
        }
    }

    /// <summary>Position the backdrop + line pool for the current corner/size settings.
    /// Slot 0 (nearest the corner) is the newest line; older lines stack away from it.</summary>
    private static void Layout(int maxLines, float life, float now)
    {
        int corner = Mathf.Clamp(Preferences.FeedCorner?.Value ?? 2, 0, 3);
        bool top = corner == 0 || corner == 1;
        bool left = corner == 0 || corner == 2;
        bool persist = Preferences.FeedPersist?.Value ?? false;
        float fontSize = Mathf.Clamp(Preferences.FeedFontSize?.Value ?? 22f, 8f, 80f);
        float minH = fontSize * LineHeightFactor;
        // Width is a fraction of the screen so it scales across resolutions.
        float pct = Mathf.Clamp(Preferences.FeedWidthPct?.Value ?? 0.28f, 0.1f, 0.6f);
        float width = Mathf.Max(200f, Screen.width * pct);
        float wrapW = width - 2f * Pad;

        var anchor = new Vector2(left ? 0f : 1f, top ? 1f : 0f);
        var align = left
            ? (top ? Il2CppTMPro.TextAlignmentOptions.TopLeft : Il2CppTMPro.TextAlignmentOptions.BottomLeft)
            : (top ? Il2CppTMPro.TextAlignmentOptions.TopRight : Il2CppTMPro.TextAlignmentOptions.BottomRight);

        int n = _entries.Count;
        float maxAlpha = 0f;
        float cum = 0f;   // accumulated height of the lines nearer the corner

        for (int j = 0; j < _lines.Length; j++)
        {
            var lbl = _lines[j];
            var rt = _lineRt[j];
            if (j >= n)
            {
                if (lbl.gameObject.activeSelf) lbl.gameObject.SetActive(false);
                continue;
            }
            if (!lbl.gameObject.activeSelf) lbl.gameObject.SetActive(true);

            var entry = _entries[n - 1 - j];   // newest nearest the corner
            float age = now - entry.BornAt;
            float a = persist ? 1f
                : age <= life ? 1f : Mathf.Clamp01(1f - (age - life) / FadeSeconds);
            if (a > maxAlpha) maxAlpha = a;

            lbl.fontSize = fontSize;
            lbl.alignment = align;
            lbl.alpha = a;
            if (_lineText[j] != entry.Text) { lbl.text = entry.Text; _lineText[j] = entry.Text; }

            // Measure the wrapped height so long messages get the rows they need
            // instead of spilling out of the box.
            float h = minH;
            try { h = Mathf.Max(minH, lbl.GetPreferredValues(entry.Text, wrapW, 0f).y); } catch { }

            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.sizeDelta = new Vector2(wrapW, h);
            float x = left ? Pad : -Pad;
            float y = top ? -(Pad + cum) : (Pad + cum);
            rt.anchoredPosition = new Vector2(x, y);
            cum += h;
        }

        // Backdrop covers the active lines, fading with the strongest line.
        _panelRt.anchorMin = _panelRt.anchorMax = _panelRt.pivot = anchor;
        _panelRt.sizeDelta = new Vector2(width, cum + 2f * Pad);
        _panelRt.anchoredPosition = Vector2.zero;
        var bg = Backdrop;
        _panel.color = new Color(bg.r / 255f, bg.g / 255f, bg.b / 255f, PanelAlpha * maxAlpha);
    }

    // --- Build ---------------------------------------------------------------

    /// <summary>Ensure the canvas + line pool exist and are sized for maxLines. Rebuilds
    /// if maxLines changed. Returns false if construction has permanently failed.</summary>
    private static bool EnsureBuilt(int maxLines)
    {
        if (_root != null && _builtLines != maxLines)
        {
            UnityEngine.Object.Destroy(_root);
            _root = null;
        }
        if (_root != null) return true;
        if (_failed) return false;

        try
        {
            _font = FindFont("NotoSans-Black") ?? FindFont("NotoSans") ?? FindFont("Orbitron");

            _root = new GameObject("WtgAp_MessageFeed");
            UnityEngine.Object.DontDestroyOnLoad(_root);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29900;   // just under the DeathLink counter (30000)

            // Backdrop (added first -> behind the text).
            var panelGo = new GameObject("Backdrop");
            _panelRt = panelGo.AddComponent<RectTransform>();
            _panelRt.SetParent(_root.transform, false);
            panelGo.AddComponent<CanvasRenderer>();
            _panel = panelGo.AddComponent<UnityEngine.UI.Image>();
            _panel.raycastTarget = false;

            _lines = new Il2CppTMPro.TextMeshProUGUI[maxLines];
            _lineRt = new RectTransform[maxLines];
            _lineText = new string[maxLines];
            for (int i = 0; i < maxLines; i++)
            {
                var go = new GameObject($"Line{i}");
                var rt = go.AddComponent<RectTransform>();
                rt.SetParent(_root.transform, false);
                go.AddComponent<CanvasRenderer>();
                var t = go.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                if (_font != null) t.font = _font;
                t.richText = true;
                t.enableWordWrapping = true;   // long messages wrap within the box width
                t.overflowMode = Il2CppTMPro.TextOverflowModes.Overflow;
                t.raycastTarget = false;
                _lines[i] = t;
                _lineRt[i] = rt;
                _lineText[i] = null;
            }

            _builtLines = maxLines;
            Plugin.Log.LogInfo($"MessageFeed built ({maxLines} lines, font: {(_font != null ? _font.name : "default")}).");
            return true;
        }
        catch (Exception e)
        {
            _failed = true;
            Plugin.Log.LogError($"MessageFeed.Build: {e}");
            return false;
        }
    }

    private static Il2CppTMPro.TMP_FontAsset FindFont(string contains)
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll<Il2CppTMPro.TMP_FontAsset>();
            for (int i = 0; i < all.Length; i++)
            {
                var f = all[i];
                if (f != null && !string.IsNullOrEmpty(f.name)
                    && f.name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                    return f;
            }
        }
        catch { }
        return null;
    }

    // --- Helpers -------------------------------------------------------------

    private static string Hex(byte b) => b.ToString("X2");
    private static string Hex(Color c) =>
        $"{Hex((byte)Mathf.RoundToInt(c.r * 255))}{Hex((byte)Mathf.RoundToInt(c.g * 255))}{Hex((byte)Mathf.RoundToInt(c.b * 255))}";

    /// <summary>Neutralise stray '&lt;' so item/player names can't inject TMP tags.</summary>
    private static string Escape(string s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace("<", "‹");
}
