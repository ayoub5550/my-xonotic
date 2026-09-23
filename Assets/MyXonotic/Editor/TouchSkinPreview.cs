using System.IO;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// dev.14: CPU-composited preview of the touch-button skin (TouchGlyphs) so the
    /// look can be reviewed from a headless sandbox — OnGUI is not captured by
    /// VisualProbe. Writes Artifacts/visual/touch-skin.png (1280×720, buttons at the
    /// TouchLayout reference positions over a neutral arena-like gradient).
    /// </summary>
    public static class TouchSkinPreview
    {
        [MenuItem("My Xonotic/Preview touch skin")]
        public static void Run()
        {
            const int W = 1280, H = 720;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    px[y * W + x] = Color.Lerp(new Color(0.16f, 0.17f, 0.20f), new Color(0.42f, 0.40f, 0.36f), (float)y / H) * (0.85f + 0.15f * Mathf.PerlinNoise(x * 0.01f, y * 0.01f));

            // Reference pixel positions (my-librequake 1280x720 canvas): centre offset from bottom-right + diameter.
            Button(px, W, H, W - 170, 170, 190, TouchGlyphs.Glyph.Bullet, false);
            Button(px, W, H, W - 340, 70, 110, TouchGlyphs.Glyph.ArrowUp, true);
            Button(px, W, H, W - 340, 185, 90, TouchGlyphs.Glyph.Crosshair, false);
            Button(px, W, H, W - 70, 345, 100, TouchGlyphs.Glyph.ChevronUp, false);
            Button(px, W, H, W - 70, 455, 100, TouchGlyphs.Glyph.ChevronDown, false);
            // Joystick (left) as it appears while a thumb is down.
            Ring(px, W, H, 260, 200, 158, 0.93f, 0.985f, new Color(1, 1, 1, 0.55f));
            Disc(px, W, H, 300, 230, 79, new Color(1, 1, 1, 0.45f));

            tex.SetPixels(px);
            tex.Apply();
            Directory.CreateDirectory("Artifacts/visual");
            File.WriteAllBytes("Artifacts/visual/touch-skin.png", tex.EncodeToPNG());
            foreach (TouchGlyphs.Glyph g in System.Enum.GetValues(typeof(TouchGlyphs.Glyph)))
                File.WriteAllBytes("Artifacts/visual/glyph-" + g + ".png", TouchGlyphs.Get(g).EncodeToPNG());
            Debug.Log("[my-xonotic] TOUCH SKIN PREVIEW Artifacts/visual/touch-skin.png");
        }

        static void Blend(Color[] px, int i, Color c) => px[i] = Color.Lerp(px[i], new Color(c.r, c.g, c.b, 1f), c.a);

        static void Disc(Color[] px, int W, int H, float cx, float cy, float r, Color c)
        {
            for (int y = (int)(cy - r) - 1; y <= cy + r + 1; y++)
                for (int x = (int)(cx - r) - 1; x <= cx + r + 1; x++)
                {
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(r - d);
                    if (a > 0f) Blend(px, y * W + x, new Color(c.r, c.g, c.b, c.a * a));
                }
        }

        static void Ring(Color[] px, int W, int H, float cx, float cy, float r, float inner, float outer, Color c)
        {
            for (int y = (int)(cy - r) - 1; y <= cy + r + 1; y++)
                for (int x = (int)(cx - r) - 1; x <= cx + r + 1; x++)
                {
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Min(Mathf.Clamp01(r * outer - d), Mathf.Clamp01(d - r * inner));
                    if (a > 0f) Blend(px, y * W + x, new Color(c.r, c.g, c.b, c.a * a));
                }
        }

        static void Button(Color[] px, int W, int H, float cx, float cy, float diameter, TouchGlyphs.Glyph glyph, bool held)
        {
            float r = diameter * 0.5f;
            Disc(px, W, H, cx, cy, r, held ? TouchGlyphs.DiscFillHeld : TouchGlyphs.DiscFill);
            Ring(px, W, H, cx, cy, r, 0.93f, 0.985f, TouchGlyphs.Rim);
            var g = TouchGlyphs.Get(glyph);
            float size = diameter * 0.52f;
            float gx = cx - size * 0.5f, gy = cy - size * 0.5f + diameter * 0.08f;
            for (int y = (int)gy; y < gy + size; y++)
                for (int x = (int)gx; x < gx + size; x++)
                {
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    float u = (x - gx) / size, v = (y - gy) / size;
                    float a = g.GetPixelBilinear(u, v).a;
                    float sa = g.GetPixelBilinear(u + 1.5f / size, v + 1.5f / size).a;
                    if (sa > 0f) Blend(px, y * W + x, new Color(0, 0, 0, sa * TouchGlyphs.IconShadow.a));
                    if (a > 0f) Blend(px, y * W + x, new Color(1, 1, 1, a * TouchGlyphs.Icon.a));
                }
        }
    }
}
