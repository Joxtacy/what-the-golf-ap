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
/// </summary>
public static class DeathLinkHud
{
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
    private static bool _failed;
    private static int _lastCount = -1;
    private static int _lastThreshold = -1;

    public static void Tick()
    {
        var client = Plugin.Client;
        var dl = client?.DeathLink;
        bool show = dl != null && client != null && client.Connected && dl.Enabled && dl.Threshold > 0;

        try
        {
            if (!show)
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

            // Keep it ~1/3 down the screen even if the resolution changes; the outline
            // copies track the face with their fixed pixel offsets.
            var basePos = new Vector2(28f, -Screen.height / 3f);
            if (_rt != null) _rt.anchoredPosition = basePos;
            if (_outlineRt != null)
                for (int i = 0; i < _outlineRt.Length; i++)
                    _outlineRt[i].anchoredPosition = basePos + Dirs[i] * OutlinePx;

            int c = dl.WipeCount, t = dl.Threshold;
            if (_text != null && (c != _lastCount || t != _lastThreshold))
            {
                _lastCount = c;
                _lastThreshold = t;
                string s = $"DEATHS {c}/{t}";
                _text.text = s;
                if (_outline != null)
                    for (int i = 0; i < _outline.Length; i++) _outline[i].text = s;
            }
        }
        catch (System.Exception e)
        {
            if (!_failed) { _failed = true; Plugin.Log.LogError($"DeathLinkHud: {e}"); }
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
