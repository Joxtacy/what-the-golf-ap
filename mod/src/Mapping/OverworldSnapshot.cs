using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Renders the loaded overworld to PNGs with an orthographic camera, one image
/// per chamber (or per episode), so the PopTracker pack can use the GAME'S OWN
/// ART as its map backgrounds instead of schematic boxes.
///
/// Why a camera rather than screenshots: we choose the exact world rectangle the
/// camera covers, and write it into wtg_snapshots.json alongside each image. The
/// pack's world->pixel transform is then *literally* that rectangle, so markers
/// land pixel-exact on the art with no fitting or eyeballing. A stitched
/// screenshot would have to be aligned by hand and would drift.
///
/// The overworld is laid out in the XY plane at z~0, so a top-down ortho camera
/// is the projection the game itself uses.
///
/// Read-only w.r.t. game state: it creates its own camera and destroys it again,
/// touching nothing else. Dev tool -- gated behind Mod.SnapshotEnabled, OFF by
/// default. Runs once per campaign per session.
/// </summary>
public static class OverworldSnapshot
{
    private const int LongEdge = 2048;      // px on the longer axis
    private const int MaxDim = 4096;
    // Margin around the goal bounding box. The box is built from FLAG positions,
    // but the art around each hole -- its room, platform, the corridor into it --
    // extends well past the flag, so a tight box crops all of it. Typical hole
    // spacing is 3-13 world units, so a fixed 4 was less than one room and every
    // map came out clipped at the edges. Per axis, not a single value from the
    // larger span, or a tall map like Snow gets a huge empty margin sideways.
    private const float PadMin = 15f;
    private const float PadFrac = 0.10f;
    // A chamber with very few goals has a tiny bbox, which zooms the camera in so
    // far the result is a postage stamp with no context (chamber 00 came out as an
    // 8x8-unit crop). Never render a window smaller than this, expanded about the
    // bbox centre.
    private const float MinSpanWorld = 40f;

    // Never render off a handful of goals: CampaignInfo flips to an episode's tag
    // while the player is still in the mode-select hub, and a single stray goal
    // there was enough to produce a picture of the MAIN MENU labelled as an
    // episode map. Require a real overworld's worth, and require the count to
    // hold steady for a tick so we don't catch a half-loaded scene.
    private const int MinGoals = 5;
    // A LineRenderer at least this long in either axis is a decorative cord, not
    // map content -- see HideLongLines.
    private const float LongLineWorld = 40f;
    // Set true to log the biggest renderers per map (how the lamp cords were found).
    // static readonly, not const: a const false makes the call site unreachable
    // (CS0162), and this repo builds warning-clean.
    private static readonly bool DiagRenderers = false;

    private static readonly Dictionary<string, int> Rendered = new();  // campaign -> goals used
    private static readonly Dictionary<string, int> LastSeen = new();  // campaign -> prev count
    private static readonly Dictionary<string, string> Manifest = new();  // map -> json

    private static string OutDir =>
        MelonLoader.Utils.MelonEnvironment.GameRootDirectory;

    private static string ManifestPath => Path.Combine(OutDir, "wtg_snapshots.json");

    public static void Tick()
    {
        try
        {
            string campaign = CampaignInfo.Current();
            if (string.IsNullOrEmpty(campaign) || campaign == "Hub") return;

            int goals;
            var groups = CollectGroups(campaign, out goals);

            if (goals < MinGoals)
            {
                LastSeen[campaign] = goals;
                return;                            // hub, or still loading
            }
            // Only act on a count that held steady since the previous tick.
            int prev = LastSeen.TryGetValue(campaign, out var v) ? v : -1;
            LastSeen[campaign] = goals;
            if (goals != prev) return;
            // Re-render only if this pass genuinely saw more of the overworld.
            if (Rendered.TryGetValue(campaign, out var had) && goals <= had) return;

            Rendered[campaign] = goals;
            Plugin.Log.LogInfo($"[SNAP] rendering {groups.Count} map(s) for {campaign} "
                               + $"from {goals} goals...");
            foreach (var kv in groups)
                Render(campaign, kv.Key, kv.Value);
            WriteManifest();
        }
        catch (Exception e) { Plugin.Log.LogError($"[SNAP] {e}"); }
    }

    /// <summary>map name -> world bbox (minx, miny, maxx, maxy) of its goals.
    /// Main splits by chamber; an episode is one map.</summary>
    private static Dictionary<string, float[]> CollectGroups(string campaign, out int counted)
    {
        counted = 0;
        var boxes = new Dictionary<string, float[]>();
        var all = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldGoal>();
        for (int i = 0; i < all.Length; i++)
        {
            var g = all[i];
            if (g == null) continue;
            bool live = false;
            try { live = g.gameObject.scene.IsValid(); } catch { }
            if (!live) continue;               // prefab asset, not a placed instance

            string scene = null;
            try { scene = g.levelData != null ? g.levelData.SceneName : null; } catch { }
            // Only goals the apworld knows. The mode-select hub has its own goals
            // (daily / special event / level editor) which are not campaign holes,
            // and counting them is what produced a "map" of the main menu.
            if (!LocationMap.IsKnownScene(scene)) continue;

            string map;
            if (campaign == "Main")
            {
                string sub = LocationMap.SubAreaOf(scene);
                string ch = LocationMap.ChamberOf(sub);
                if (ch == null) continue;      // bosses etc. -- bbox comes from holes
                map = "chamber_" + ch;
            }
            else
            {
                map = "ep_" + campaign.ToLowerInvariant();
            }

            Vector3 p;
            try { p = g.transform.position; } catch { continue; }
            counted++;
            if (!boxes.TryGetValue(map, out var b))
                boxes[map] = new[] { p.x, p.y, p.x, p.y };
            else
            {
                if (p.x < b[0]) b[0] = p.x;
                if (p.y < b[1]) b[1] = p.y;
                if (p.x > b[2]) b[2] = p.x;
                if (p.y > b[3]) b[3] = p.y;
            }
        }
        return boxes;
    }

    private static void Render(string campaign, string map, float[] b)
    {
        float padx = Mathf.Max(PadMin, (b[2] - b[0]) * PadFrac);
        float pady = Mathf.Max(PadMin, (b[3] - b[1]) * PadFrac);
        float minx = b[0] - padx, miny = b[1] - pady;
        float maxx = b[2] + padx, maxy = b[3] + pady;

        // Floor the window size so a sparse chamber still shows its surroundings.
        if (maxx - minx < MinSpanWorld)
        {
            float c = (minx + maxx) / 2f;
            minx = c - MinSpanWorld / 2f;
            maxx = c + MinSpanWorld / 2f;
        }
        if (maxy - miny < MinSpanWorld)
        {
            float c = (miny + maxy) / 2f;
            miny = c - MinSpanWorld / 2f;
            maxy = c + MinSpanWorld / 2f;
        }

        float dx = Mathf.Max(0.001f, maxx - minx), dy = Mathf.Max(0.001f, maxy - miny);

        int w, h;
        if (dx >= dy) { w = LongEdge; h = Mathf.RoundToInt(LongEdge * dy / dx); }
        else { h = LongEdge; w = Mathf.RoundToInt(LongEdge * dx / dy); }
        w = Mathf.Clamp(w, 16, MaxDim);
        h = Mathf.Clamp(h, 16, MaxDim);
        // Keep the camera rect EXACTLY consistent with the pixel aspect, or the
        // recorded rect would not match what was drawn.
        float aspect = (float)w / h;
        float halfH = dy / 2f;
        if (dx / dy > aspect) halfH = (dx / aspect) / 2f;
        float cx = (minx + maxx) / 2f, cy = (miny + maxy) / 2f;

        if (DiagRenderers) LogBigRenderers(map, minx, miny, maxx, maxy);

        // Decorative lamp cords and cables are LineRenderers anchored far off,
        // spanning 80-145 world units. At play zoom you only ever see the short
        // stretch beside a lamp; zoomed out to a whole chamber they read as pale
        // diagonal stripes across the entire map. Hide them for the shot.
        var hidden = HideLongLines(minx, miny, maxx, maxy);

        var go = new GameObject("wtg_snapshot_cam");
        RenderTexture rt = null;
        Texture2D tex = null;
        var prevActive = RenderTexture.active;
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = halfH;
            cam.aspect = aspect;
            cam.transform.rotation = Quaternion.identity;
            cam.allowHDR = false;
            cam.allowMSAA = false;

            // Inherit the game's own view rather than inventing one. Sitting at
            // z=-100 with a 0.01 near plane rendered foreground overlays the game
            // never shows -- chamber 06 came out with decorative diagonal light
            // shafts striped across the whole image. Copying depth, clip planes
            // and culling mask means we see exactly what the player sees, just
            // orthographic and zoomed out.
            var src = FindOverworldCamera();
            float camZ = -100f;
            if (src != null)
            {
                camZ = src.transform.position.z;
                cam.nearClipPlane = src.nearClipPlane;
                cam.farClipPlane = src.farClipPlane;
                cam.cullingMask = src.cullingMask;
                cam.clearFlags = src.clearFlags;
                cam.backgroundColor = src.backgroundColor;
                Plugin.Log.LogInfo($"[SNAP] copied camera '{src.name}' z={camZ:0.##} "
                                   + $"near={src.nearClipPlane:0.##} far={src.farClipPlane:0.##} "
                                   + $"mask=0x{src.cullingMask:X}");
            }
            else
            {
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 500f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f, 1f);
                Plugin.Log.LogWarning("[SNAP] no source camera found; using defaults");
            }
            cam.transform.position = new Vector3(cx, cy, camZ);

            rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            tex.Apply(false);

            var png = ImageConversion.EncodeToPNG(tex);
            string file = Path.Combine(OutDir, "wtg_map_" + map + ".png");
            File.WriteAllBytes(file, png);

            // The recorded rect is the contract the pack projects markers with.
            float halfW = halfH * aspect;
            Manifest[map] = "{"
                + $"\"map\":\"{map}\",\"campaign\":\"{campaign}\","
                + $"\"file\":\"wtg_map_{map}.png\",\"w\":{w},\"h\":{h},"
                + $"\"minx\":{(cx - halfW):0.####},\"miny\":{(cy - halfH):0.####},"
                + $"\"maxx\":{(cx + halfW):0.####},\"maxy\":{(cy + halfH):0.####}"
                + "}";
            Plugin.Log.LogInfo($"[SNAP] {map} {w}x{h} "
                               + $"world=({cx - halfW:0.#},{cy - halfH:0.#})-"
                               + $"({cx + halfW:0.#},{cy + halfH:0.#}) "
                               + $"-> {png.Length / 1024}kb");
        }
        finally
        {
            foreach (var r in hidden)
            {
                try { r.enabled = true; } catch { }
            }
            RenderTexture.active = prevActive;
            try { if (tex != null) UnityEngine.Object.Destroy(tex); } catch { }
            try { if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); } } catch { }
            try { UnityEngine.Object.Destroy(go); } catch { }
        }
    }

    /// <summary>Temporarily hide over-long LineRenderers overlapping the window, and
    /// return them so the caller can switch them back on.
    ///
    /// Identified from the diagnostic below: "GravityLamp*" / "Cable" cords, 80-145
    /// world units end to end. Matched by TYPE AND SIZE rather than by name, so a
    /// differently-named cord elsewhere is caught too -- nothing that long and thin
    /// is map content worth showing.</summary>
    private static List<Renderer> HideLongLines(float minx, float miny,
                                                float maxx, float maxy)
    {
        var hidden = new List<Renderer>();
        try
        {
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<LineRenderer>();
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null) continue;
                bool live = false;
                try { live = r.enabled && r.gameObject.activeInHierarchy
                             && r.gameObject.scene.IsValid(); } catch { }
                if (!live) continue;

                Bounds b;
                try { b = r.bounds; } catch { continue; }
                if (b.size.x < LongLineWorld && b.size.y < LongLineWorld) continue;
                if (b.max.x < minx || b.min.x > maxx || b.max.y < miny || b.min.y > maxy)
                    continue;

                try { r.enabled = false; hidden.Add(r); } catch { }
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[SNAP] HideLongLines: {e.Message}"); }
        return hidden;
    }

    /// <summary>DIAGNOSTIC: log the biggest renderers overlapping a map's window.
    /// Chamber 06 came out with pale diagonal lines striped across the whole image;
    /// they survived copying the game camera's depth/clip/culling, so they are real
    /// scene objects. This says which, so they can be excluded by name or layer.</summary>
    private static void LogBigRenderers(string map, float minx, float miny,
                                        float maxx, float maxy)
    {
        try
        {
            var found = new List<string>();
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null) continue;
                bool live = false;
                try { live = r.enabled && r.gameObject.activeInHierarchy
                             && r.gameObject.scene.IsValid(); } catch { }
                if (!live) continue;

                Bounds b;
                try { b = r.bounds; } catch { continue; }
                // Big enough to stripe a whole chamber, and actually in this window.
                if (b.size.x < 25f && b.size.y < 25f) continue;
                if (b.max.x < minx || b.min.x > maxx || b.max.y < miny || b.min.y > maxy)
                    continue;

                string name = "?", parent = "?", sort = "?";
                try { name = r.gameObject.name; } catch { }
                try { parent = r.transform.parent != null ? r.transform.parent.name : "-"; } catch { }
                try { sort = r.sortingLayerName + "/" + r.sortingOrder; } catch { }
                found.Add($"      {name} (parent={parent}) layer={r.gameObject.layer} "
                          + $"sort={sort} z={r.transform.position.z:0.##} "
                          + $"size={b.size.x:0.#}x{b.size.y:0.#} type={r.GetIl2CppType().Name}");
            }
            if (found.Count == 0) return;
            Plugin.Log.LogInfo($"[SNAPDIAG] {map}: {found.Count} large renderer(s)");
            for (int i = 0; i < found.Count && i < 25; i++)
                Plugin.Log.LogInfo(found[i]);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[SNAPDIAG] {e.Message}"); }
    }

    /// <summary>The camera the game is actually drawing the overworld with, so the
    /// snapshot inherits its depth, clip planes and culling mask.</summary>
    private static Camera FindOverworldCamera()
    {
        try
        {
            var main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;
        }
        catch { }
        try
        {
            // Fall back to the enabled camera that renders last to the screen.
            Camera best = null;
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<Camera>();
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c == null) continue;
                bool live = false;
                try { live = c.isActiveAndEnabled && c.gameObject.scene.IsValid(); } catch { }
                if (!live || c.targetTexture != null) continue;
                if (best == null || c.depth > best.depth) best = c;
            }
            return best;
        }
        catch { return null; }
    }

    private static void WriteManifest()
    {
        // Keyed by map, so a re-render (a later pass that saw more of the
        // overworld) replaces its entry instead of appending a duplicate.
        var keys = new List<string>(Manifest.Keys);
        keys.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder("[\n");
        for (int i = 0; i < keys.Count; i++)
            sb.Append("  ").Append(Manifest[keys[i]])
              .Append(i + 1 < keys.Count ? ",\n" : "\n");
        sb.Append("]\n");
        File.WriteAllText(ManifestPath, sb.ToString());
        Plugin.Log.LogInfo($"[SNAP] wrote {Manifest.Count} entries -> {ManifestPath}");
    }
}
