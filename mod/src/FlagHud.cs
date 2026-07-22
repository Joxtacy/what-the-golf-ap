using UnityEngine;

namespace WtgArchipelago;

/// <summary>
/// On-screen "FLAGS x/N" progress counter for the door_50/75/100 goals, whose win
/// condition is collecting a fraction of all Flag items (x = Flags received this
/// session, N = the seed's target). Shown only while connected on a door_* goal
/// (ArchipelagoData.FlagGoal &gt; 0) and while Preferences.FlagHud is on.
///
/// Like DeathLinkHud it's a real in-scene TextMeshPro object so it uses the GAME's
/// font (NotoSans) with the chunky teal outline (a cream "face" plus 8 offset teal
/// copies -- TMP's own outlineWidth is atlas-capped, so the offset copies make the
/// border). Unlike the DeathLink counter this is a persistent progress meter, so
/// it's simply parked in the top-left and its text refreshed when the tally changes
/// -- no slide/tick animation. Driven from Mod.OnUpdate; safe to call every frame.
/// </summary>
public static class FlagHud
{
    private const float X = 28f;          // inset from the left edge
    private const float TopY = -28f;      // inset below the top edge (anchored top-left)
    private const float FontSize = 34f;
    private const float OutlinePx = 4f;   // outline thickness (offset-copy distance)

    // 8 directions for the offset outline copies.
    private static readonly Vector2[] Dirs =
    {
        new Vector2(-1f, -1f), new Vector2(0f, -1f), new Vector2(1f, -1f),
        new Vector2(-1f,  0f),                       new Vector2(1f,  0f),
        new Vector2(-1f,  1f), new Vector2(0f,  1f), new Vector2(1f,  1f),
    };
    private static readonly Color Cream = new Color(0.96f, 0.90f, 0.78f, 1f);   // ~#F5E6C6
    private static readonly Color32 Teal = new Color32(33, 61, 58, 255);        // ~#213D3A

    private static GameObject _root;
    private static RectTransform _rt;
    private static Il2CppTMPro.TextMeshProUGUI _text;
    private static RectTransform[] _outlineRt;
    private static Il2CppTMPro.TextMeshProUGUI[] _outline;

    private static bool _failed;
    private static int _shownCount = -1;
    private static int _shownGoal = -1;

    public static void Tick()
    {
        var client = Plugin.Client;
        var data = client?.Data;
        bool active = client != null && client.Connected && data != null
                      && data.FlagGoal > 0 && (Preferences.FlagHud?.Value ?? true);

        try
        {
            if (!active)
            {
                if (_root != null && _root.activeSelf) _root.SetActive(false);
                return;
            }

            if (_root == null)
            {
                if (_failed) return;   // build failed once; don't keep retrying/spamming
                Build();
            }
            if (_root == null) return;

            if (!_root.activeSelf) _root.SetActive(true);

            // Show the true count -- it can exceed the goal (e.g. 133/67) once you've
            // won, since the server auto-collects your remaining locations. That's kept
            // intentionally (it reads as "you smashed past the target").
            SetShown(data.FlagsCollected, data.FlagGoal);
            ApplyPosition();
        }
        catch (System.Exception e)
        {
            if (!_failed) { _failed = true; Plugin.Log.LogError($"FlagHud: {e}"); }
        }
    }

    /// <summary>Set the rendered text on the face + all outline copies (only when the
    /// tally actually changes).</summary>
    private static void SetShown(int c, int g)
    {
        if (c == _shownCount && g == _shownGoal) return;
        _shownCount = c;
        _shownGoal = g;
        string s = $"FLAGS {c}/{g}";
        if (_text != null) _text.text = s;
        if (_outline != null)
            for (int i = 0; i < _outline.Length; i++) _outline[i].text = s;
    }

    /// <summary>Park the face + its outline copies in the top-left corner (recomputed
    /// each frame so it survives resolution changes).</summary>
    private static void ApplyPosition()
    {
        var basePos = new Vector2(X, TopY);
        if (_rt != null) _rt.anchoredPosition = basePos;
        if (_outlineRt != null)
            for (int i = 0; i < _outlineRt.Length; i++)
                _outlineRt[i].anchoredPosition = basePos + Dirs[i] * OutlinePx;
    }

    private static void Build()
    {
        // Same font choice as DeathLinkHud: NotoSans is the game's menu/label font;
        // Orbitron is only its thin numeric readouts, so it doesn't read as WTG text.
        var font = FindFont("NotoSans-Black") ?? FindFont("NotoSans") ?? FindFont("Orbitron");
        if (font == null) Plugin.Log.LogWarning("FlagHud: no TMP font found; using TMP default.");

        _root = new GameObject("WtgAp_FlagHud");
        Object.DontDestroyOnLoad(_root);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;   // draw on top of the game's own UI

        // Outline layer first (added first -> rendered behind the face). Each teal copy
        // also carries a small outlineWidth so the offsets merge into a solid border.
        _outline = new Il2CppTMPro.TextMeshProUGUI[Dirs.Length];
        _outlineRt = new RectTransform[Dirs.Length];
        for (int i = 0; i < Dirs.Length; i++)
        {
            var t = MakeLabel($"Outline{i}", font);
            t.color = new Color(Teal.r / 255f, Teal.g / 255f, Teal.b / 255f, 1f);
            TrySetOutline(t, 0.25f);
            _outline[i] = t;
            _outlineRt[i] = t.rectTransform;
        }

        // Cream face on top.
        _text = MakeLabel("Face", font);
        _text.color = Cream;
        _rt = _text.rectTransform;

        Plugin.Log.LogInfo($"FlagHud built (font: {(font != null ? font.name : "default")}).");
    }

    /// <summary>Create one child TextMeshProUGUI label anchored top-left.</summary>
    private static Il2CppTMPro.TextMeshProUGUI MakeLabel(string name, Il2CppTMPro.TMP_FontAsset font)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(_root.transform, false);
        go.AddComponent<CanvasRenderer>();

        var t = go.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = FontSize;
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
        catch (System.Exception e) { Plugin.Log.LogWarning($"FlagHud outline: {e.Message}"); }
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
