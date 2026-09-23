using System.Collections.Generic;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// dev.14 touch-button skin, modelled on the owner's Warzone-Mobile reference
    /// screenshot: every action button is a dark translucent disc with a thin
    /// white rim and a white pictogram (bullet = fire, crosshair = alt/aim,
    /// arrow = jump, chevrons = weapon switch), no coloured fills. Glyphs are
    /// rasterised once into small textures from a few strokes (signed-distance
    /// to capsules/rings), so there are no sprite assets to ship or licence.
    /// </summary>
    public static class TouchGlyphs
    {
        public enum Glyph { Bullet, Crosshair, ArrowUp, ChevronUp, ChevronDown, Pause }

        public static readonly Color DiscFill = new Color32(10, 12, 16, 120);      // dark, ~47% alpha
        public static readonly Color DiscFillHeld = new Color32(255, 255, 255, 70); // lit while pressed
        public static readonly Color Rim = new Color32(255, 255, 255, 190);
        public static readonly Color Icon = new Color32(255, 255, 255, 235);
        public static readonly Color IconShadow = new Color32(0, 0, 0, 120);

        const int Size = 96;
        static readonly Dictionary<Glyph, Texture2D> Cache = new Dictionary<Glyph, Texture2D>();

        public static Texture2D Get(Glyph glyph)
        {
            if (Cache.TryGetValue(glyph, out var tex) && tex != null) return tex;
            tex = Rasterise(glyph);
            Cache[glyph] = tex;
            return tex;
        }

        // ---- tiny stroke rasteriser (coordinates in 0..1 of the texture) ----------

        struct Stroke { public Vector2 A, B; public float R; public bool Hollow; public float Inner; }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        static Texture2D Rasterise(Glyph glyph)
        {
            var strokes = new List<Stroke>();
            void Line(float x0, float y0, float x1, float y1, float r) =>
                strokes.Add(new Stroke { A = new Vector2(x0, y0), B = new Vector2(x1, y1), R = r });
            void Ring(float cx, float cy, float radius, float width) =>
                strokes.Add(new Stroke { A = new Vector2(cx, cy), B = new Vector2(cx, cy), R = radius + width * 0.5f, Hollow = true, Inner = radius - width * 0.5f });

            switch (glyph)
            {
                case Glyph.Bullet:
                    // Cartridge tilted 45° (tip top-right): rim, straight case, tapering nose.
                    Line(0.22f, 0.22f, 0.26f, 0.26f, 0.15f);  // rim
                    Line(0.27f, 0.27f, 0.50f, 0.50f, 0.125f); // case
                    Line(0.50f, 0.50f, 0.58f, 0.58f, 0.115f); // nose base
                    Line(0.58f, 0.58f, 0.66f, 0.66f, 0.085f);
                    Line(0.66f, 0.66f, 0.73f, 0.73f, 0.05f);  // tip
                    break;
                case Glyph.Crosshair:
                    Ring(0.5f, 0.5f, 0.25f, 0.07f);
                    Line(0.5f, 0.10f, 0.5f, 0.30f, 0.04f);
                    Line(0.5f, 0.70f, 0.5f, 0.90f, 0.04f);
                    Line(0.10f, 0.5f, 0.30f, 0.5f, 0.04f);
                    Line(0.70f, 0.5f, 0.90f, 0.5f, 0.04f);
                    Line(0.5f, 0.5f, 0.5f, 0.5f, 0.045f);
                    break;
                case Glyph.ArrowUp:
                    Line(0.5f, 0.24f, 0.5f, 0.80f, 0.065f);
                    Line(0.5f, 0.80f, 0.25f, 0.55f, 0.065f);
                    Line(0.5f, 0.80f, 0.75f, 0.55f, 0.065f);
                    Line(0.28f, 0.16f, 0.72f, 0.16f, 0.045f); // ground line
                    break;
                case Glyph.ChevronUp:
                    Line(0.22f, 0.38f, 0.5f, 0.66f, 0.07f);
                    Line(0.5f, 0.66f, 0.78f, 0.38f, 0.07f);
                    break;
                case Glyph.ChevronDown:
                    Line(0.22f, 0.62f, 0.5f, 0.34f, 0.07f);
                    Line(0.5f, 0.34f, 0.78f, 0.62f, 0.07f);
                    break;
                case Glyph.Pause:
                    Line(0.40f, 0.30f, 0.40f, 0.70f, 0.055f);
                    Line(0.60f, 0.30f, 0.60f, 0.70f, 0.055f);
                    break;
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[Size * Size];
            float aa = 1.2f / Size;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2((x + 0.5f) / Size, (y + 0.5f) / Size);
                    float alpha = 0f;
                    foreach (var s in strokes)
                    {
                        float d = SegmentDistance(p, s.A, s.B);
                        float cov = s.Hollow
                            ? Mathf.Min(Mathf.Clamp01((s.R - d) / aa), Mathf.Clamp01((d - s.Inner) / aa))
                            : Mathf.Clamp01((s.R - d) / aa);
                        alpha = Mathf.Max(alpha, cov);
                    }
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
