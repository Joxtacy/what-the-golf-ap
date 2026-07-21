using UnityEngine;

namespace WtgArchipelago;

/// <summary>
/// The on-screen DeathLink wipe counter, rendered as a real in-scene TextMeshPro
/// object so it uses the GAME's own font (NotoSans, the rounded font WTG uses for
/// its menus/labels) rather than IMGUI's Arial. IMGUI can only use legacy
/// UnityEngine.Font assets, and the game ships its fonts only as TextMeshPro assets
/// (their legacy sourceFontFile is null), so a TMP object is the only way to match
/// the game. (Orbitron, the other bundled font, is only used for thin numeric
/// readouts and doesn't read as WTG text.)
///
/// A persistent (DontDestroyOnLoad) ScreenSpaceOverlay Canvas holds the counter.
/// For a genuinely CHUNKY outline (the WTG logo look) the text is drawn 9 times: a
/// cream "face" on top, plus 8 dark-teal copies offset in all directions behind it.
/// TMP's own outlineWidth is capped by the font atlas's SDF padding (~0.4) so it
/// can't get thick enough on its own; the offset copies aren't limited that way.
///
/// Driven from Mod.OnUpdate: shown while DeathLink is active + connected, hidden
/// otherwise, text refreshed only when the count changes.
///
/// Two display modes (Preferences.HudAnimate):
///  - animated (default): the counter lives off-screen to the left and slides in on
///    each wipe (and once on connect, to show the current tally), holds ~2.5s, then
///    slides back out -- Celeste-style, unobtrusive. Driven by Time.unscaledDeltaTime
///    so it keeps animating while the F8 panel pauses the game.
///  - always-on: the counter is parked at its on-screen position permanently.
/// </summary>
public static class DeathLinkHud
{
    private enum HudState { Hidden, SlideIn, Hold, SlideOut }

    private const float OnScreenX = 28f;    // resting x when visible
    private const float OffScreenX = -700f; // fully tucked away (label is 600 wide)
    private const float SlideSpeed = 2600f; // px/sec -> a slide takes ~0.28s
    private const float HoldSeconds = 2.5f; // dwell on screen before sliding out
    // 8 directions for the offset outline copies.
    private static readonly Vector2[] Dirs =
    {
        new Vector2(-1f, -1f), new Vector2(0f, -1f), new Vector2(1f, -1f),
        new Vector2(-1f,  0f),                       new Vector2(1f,  0f),
        new Vector2(-1f,  1f), new Vector2(0f,  1f), new Vector2(1f,  1f),
    };
    private const float OutlinePx = 5f;   // outline thickness in pixels (copy offset)
    private const float FontSize = 40f;

    private static readonly Color Cream = new Color(0.96f, 0.90f, 0.78f, 1f);   // ~#F5E6C6
    private static readonly Color32 Teal = new Color32(33, 61, 58, 255);        // ~#213D3A

    private static GameObject _root;
    private static RectTransform _rt;
    private static Il2CppTMPro.TextMeshProUGUI _text;
    private static RectTransform[] _outlineRt;
    private static Il2CppTMPro.TextMeshProUGUI[] _outline;
    // Number "tick up" so you SEE the count change: on a wipe the counter slides in
    // still showing the OLD number, then after a short beat on screen it ticks up to
    // the new value with a scale pop. On the wipe that hits the threshold and fires a
    // DeathLink, it peaks at N/N first, then flips to 0/N -- a "cash register" reset --
    // instead of jumping straight from (N-1)/N to 0/N.
    private const float BumpDelay = 0.35f;    // wait after arriving before ticking the number
    private const float PeakHold = 1.1f;      // dwell on the N/N peak before the reset flip
    private const float ResetDwell = 1.2f;    // dwell on 0/N after a broadcast before sliding out
    private const float PopDuration = 0.28f;  // scale-pop decay time
    private const float PopScale = 0.35f;     // extra scale at the peak of the pop

    private static bool _failed;
    private static int _lastCount = -1;       // last actual WipeCount seen (change detection)
    private static int _lastThreshold = -1;
    private static int _lastSent = -1;        // last DeathsSent seen (broadcast detection)
    private static int _shownCount = -1;      // number currently rendered (lags a wipe)
    private static int _shownThreshold = -1;

    private static HudState _state = HudState.Hidden;
    private static bool _wasActive;      // was the HUD active last frame (for connect detection)
    private static float _x = OffScreenX;
    private static float _holdTimer;
    private static bool _pendingBump;    // a wipe is waiting to tick up once on screen
    private static int _bumpTarget;      // count to tick up to
    private static float _bumpTimer;
    private static bool _pendingReset;   // after a peak, flip to 0/N (broadcast only)
    private static float _resetTimer;
    private static float _pop;           // 1 -> 0, drives the scale pop

    public static void Tick()
    {
        var client = Plugin.Client;
        var dl = client?.DeathLink;
        bool active = dl != null && client != null && client.Connected && dl.Enabled && dl.Threshold > 0;

        try
        {
            if (!active)
            {
                if (_root != null && _root.activeSelf) _root.SetActive(false);
                // Reset so the next connection slides in fresh.
                _wasActive = false;
                _state = HudState.Hidden;
                _x = OffScreenX;
                _pendingBump = _pendingReset = false;
                _pop = 0f;
                return;
            }

            if (_root == null)
            {
                if (_failed) return;   // build failed once; don't keep retrying/spamming
                Build();
            }
            if (_root == null) return;

            if (!_root.activeSelf) _root.SetActive(true);

            int c = dl.WipeCount, t = dl.Threshold, sent = dl.DeathsSent;
            bool countChanged = c != _lastCount || t != _lastThreshold;
            bool broadcast = sent != _lastSent;
            _lastCount = c;
            _lastThreshold = t;
            _lastSent = sent;

            bool justConnected = !_wasActive;
            // A wipe happened this frame: either the counter moved, or a broadcast fired
            // (at threshold=1 the counter stays 0, so the broadcast is the only signal).
            bool wipe = _wasActive && (countChanged || broadcast);

            if (justConnected)
            {
                // Show the current tally immediately, no tick-up.
                SetShown(c, t);
                _pendingBump = _pendingReset = false;
            }
            else if (wipe)
            {
                // Queue the tick-up (applied once on screen). A broadcast peaks at N/N
                // then resets to 0/N; a normal wipe just ticks to the current count.
                _pendingBump = true;
                _bumpTimer = BumpDelay;
                _bumpTarget = broadcast ? t : c;
                _pendingReset = broadcast;
            }

            bool animate = Preferences.HudAnimate?.Value ?? true;
            float dt = Time.unscaledDeltaTime;
            if (animate)
            {
                if (justConnected || wipe) { _state = HudState.SlideIn; _holdTimer = HoldSeconds; }
                TickSlide(dt);
                AdvanceDisplay(dt, _state == HudState.Hold);   // only tick while on screen
            }
            else
            {
                // Always-on: parked on screen, so the tick-up/peak/reset play in place.
                _x = OnScreenX;
                _state = HudState.Hold;
                AdvanceDisplay(dt, true);
            }

            DecayPop();
            ApplyPosition();
            _wasActive = true;
        }
        catch (System.Exception e)
        {
            if (!_failed) { _failed = true; Plugin.Log.LogError($"DeathLinkHud: {e}"); }
        }
    }

    /// <summary>Advance the slide-in/hold/slide-out position one frame. Holds on screen
    /// while a tick-up/reset is still pending so the sequence isn't cut off.</summary>
    private static void TickSlide(float dt)
    {
        switch (_state)
        {
            case HudState.SlideIn:
                _x = Mathf.MoveTowards(_x, OnScreenX, SlideSpeed * dt);
                if (_x >= OnScreenX - 0.5f) { _x = OnScreenX; _state = HudState.Hold; }
                break;
            case HudState.Hold:
                if (!_pendingBump && !_pendingReset)
                {
                    _holdTimer -= dt;
                    if (_holdTimer <= 0f) _state = HudState.SlideOut;
                }
                break;
            case HudState.SlideOut:
                _x = Mathf.MoveTowards(_x, OffScreenX, SlideSpeed * dt);
                if (_x <= OffScreenX + 0.5f) { _x = OffScreenX; _state = HudState.Hidden; }
                break;
            case HudState.Hidden:
                _x = OffScreenX;
                break;
        }
    }

    /// <summary>Drive the number sequence once it's visible: wait a beat, tick up to the
    /// target (pop); if that was a broadcast, hold the N/N peak then flip to 0/N.</summary>
    private static void AdvanceDisplay(float dt, bool onScreen)
    {
        if (!onScreen) return;

        if (_pendingBump)
        {
            _bumpTimer -= dt;
            if (_bumpTimer <= 0f)
            {
                SetShown(_bumpTarget, _lastThreshold);
                _pop = 1f;
                _pendingBump = false;
                if (_pendingReset) _resetTimer = PeakHold;   // show N/N, then reset
                else _holdTimer = HoldSeconds;               // dwell after the change
            }
        }
        else if (_pendingReset)
        {
            _resetTimer -= dt;
            if (_resetTimer <= 0f)
            {
                SetShown(0, _lastThreshold);                 // the "reset" flip
                _pop = 1f;
                _pendingReset = false;
                _holdTimer = ResetDwell;                     // brief dwell on 0/N, then out
            }
        }
    }

    private static void DecayPop()
    {
        if (_pop > 0f) _pop = Mathf.MoveTowards(_pop, 0f, Time.unscaledDeltaTime / PopDuration);
    }

    /// <summary>Set the rendered text on the face + all outline copies.</summary>
    private static void SetShown(int c, int t)
    {
        if (c == _shownCount && t == _shownThreshold) return;
        _shownCount = c;
        _shownThreshold = t;
        string s = $"DEATHS {c}/{t}";
        if (_text != null) _text.text = s;
        if (_outline != null)
            for (int i = 0; i < _outline.Length; i++) _outline[i].text = s;
    }

    /// <summary>Place the face + its outline copies at the current x, ~1/3 down the
    /// screen (recomputed each frame so it tracks resolution changes), applying the
    /// current pop scale.</summary>
    private static void ApplyPosition()
    {
        var basePos = new Vector2(_x, -Screen.height / 3f);
        float s = 1f + PopScale * _pop;
        var scale = new Vector3(s, s, 1f);
        if (_rt != null) { _rt.anchoredPosition = basePos; _rt.localScale = scale; }
        if (_outlineRt != null)
            for (int i = 0; i < _outlineRt.Length; i++)
            {
                _outlineRt[i].anchoredPosition = basePos + Dirs[i] * OutlinePx;
                _outlineRt[i].localScale = scale;
            }
    }

    private static void Build()
    {
        // NotoSans is the game's main readable text font (menus/labels like "MAIN
        // CAMPAIGN"); Orbitron is only used for thin numeric readouts, so it doesn't
        // read as "WTG text". Prefer the bold Black weight to match the game's labels.
        var font = FindFont("NotoSans-Black") ?? FindFont("NotoSans") ?? FindFont("Orbitron");
        if (font == null) Plugin.Log.LogWarning("DeathLinkHud: no TMP font found; using TMP default.");

        _root = new GameObject("WtgAp_DeathLinkHud");
        Object.DontDestroyOnLoad(_root);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;   // draw on top of the game's own UI

        // Outline layer first (added first -> rendered behind the face). Each copy is
        // teal, and carries a small teal outlineWidth of its own so the offset copies
        // merge into a solid border with no gaps.
        _outline = new Il2CppTMPro.TextMeshProUGUI[Dirs.Length];
        _outlineRt = new RectTransform[Dirs.Length];
        for (int i = 0; i < Dirs.Length; i++)
        {
            var t = MakeLabel($"Outline{i}", font, Cream);
            t.color = new Color(Teal.r / 255f, Teal.g / 255f, Teal.b / 255f, 1f);
            TrySetOutline(t, 0.25f);
            _outline[i] = t;
            _outlineRt[i] = t.rectTransform;
        }

        // Cream face on top.
        _text = MakeLabel("Face", font, Cream);
        _rt = _text.rectTransform;

        Plugin.Log.LogInfo($"DeathLinkHud built (font: {(font != null ? font.name : "default")}).");
    }

    /// <summary>Create one child TextMeshProUGUI label sharing the HUD's layout.</summary>
    private static Il2CppTMPro.TextMeshProUGUI MakeLabel(string name, Il2CppTMPro.TMP_FontAsset font, Color color)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(_root.transform, false);
        go.AddComponent<CanvasRenderer>();

        var t = go.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = FontSize;
        t.color = color;
        t.alignment = Il2CppTMPro.TextAlignmentOptions.TopLeft;

        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(600f, 90f);
        return t;
    }

    private static void TrySetOutline(Il2CppTMPro.TextMeshProUGUI t, float width)
    {
        try { t.outlineWidth = width; t.outlineColor = Teal; }
        catch (System.Exception e) { Plugin.Log.LogWarning($"DeathLinkHud outline: {e.Message}"); }
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
                    && f.name.IndexOf(contains, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return f;
            }
        }
        catch { }
        return null;
    }
}
