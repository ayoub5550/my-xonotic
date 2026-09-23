using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// dev.13: bakes the original Xonotic HUD/menu art into
    /// <c>Assets/MyXonotic/Generated/Resources/Hud/&lt;name&gt;.asset</c> so the
    /// runtime HUD (health/armor/ammo/weapon icons), the pause screen and the
    /// menu (luminos background, gametype icons) draw the real art instead of
    /// text. DarkPlaces stores opacity in a separate <c>&lt;name&gt;_alpha.jpg</c>
    /// (JPEG has no alpha); this importer merges it into the RGBA texture, then
    /// applies <see cref="ImportedTexturePolicy.Finalize"/> (mipmaps + ETC2 —
    /// the dev.12 texture rule: never persist raw RGBA32). Deterministic: same
    /// content path -> same asset path -> same GUID. Missing sources are
    /// listed in the manifest, never fabricated; the runtime falls back to text.
    /// Runs inside PrepareFullGame (prepare-maps gate and the Android build).
    /// </summary>
    public static class HudArtImporter
    {
        public const string ResourcesFolder = "Assets/MyXonotic/Generated/Resources/Hud";
        public const string ManifestPath = ResourcesFolder + "/hud-art-manifest.json";
        /// The luminos background is 2560x2048; the menu never shows more than ~1500 px, so halve it.
        public const int BackgroundMaxSize = 1280;

        [Serializable]
        public sealed class Entry { public string name, source, alphaSource, sha256; public int width, height; public string format; public bool ok; public string note; }

        [Serializable]
        public sealed class Manifest { public string utc; public int imported, missing; public List<Entry> entries = new List<Entry>(); }

        /// (resource name, upstream content path without extension, treat as sRGB, downscale max)
        static readonly (string name, string path, int maxSize)[] Sources = BuildSourceList();

        static (string, string, int)[] BuildSourceList()
        {
            var list = new List<(string, string, int)>
            {
                ("health", "gfx/hud/luma/health", 0), ("armor", "gfx/hud/luma/armor", 0),
                ("ammo_shells", "gfx/hud/luma/ammo_shells", 0), ("ammo_bullets", "gfx/hud/luma/ammo_bullets", 0),
                ("ammo_rockets", "gfx/hud/luma/ammo_rockets", 0), ("ammo_cells", "gfx/hud/luma/ammo_cells", 0),
                ("strength", "gfx/hud/luma/strength", 0), ("shield", "gfx/hud/luma/shield", 0),
                ("notify_death", "gfx/hud/luma/notify_death", 0),
                ("flag_red_taken", "gfx/hud/luma/flag_red_taken", 0), ("flag_blue_taken", "gfx/hud/luma/flag_blue_taken", 0),
                ("gametype_dm", "gfx/menu/luminos/gametype_dm", 0), ("gametype_tdm", "gfx/menu/luminos/gametype_tdm", 0),
                ("gametype_ctf", "gfx/menu/luminos/gametype_ctf", 0),
                ("menu_background", "gfx/menu/luminos/background", BackgroundMaxSize),
            };
            foreach (var w in HudArt.WeaponIconNames) list.Add((w, "gfx/hud/luma/" + w, 0));
            return list.ToArray();
        }

        [MenuItem("My Xonotic/Import/Generate HUD + menu art (luma)")]
        public static void GenerateMenu() => Generate(new XonoticContentResolver());

        public static Manifest Generate(XonoticContentResolver resolver)
        {
            EnsureFolders();
            var manifest = new Manifest { utc = DateTime.UtcNow.ToString("O") };
            foreach (var src in Sources)
            {
                var entry = new Entry { name = src.name, source = src.path };
                try
                {
                    string file = resolver.FindImage(src.path);
                    if (file == null)
                    {
                        entry.note = "source image not found in content roots";
                        manifest.missing++;
                        manifest.entries.Add(entry);
                        continue;
                    }
                    string reason;
                    var rgb = BspTextureLoader.Load(file, srgb: true, failureReason: out reason);
                    if (rgb == null) { entry.note = reason; manifest.missing++; manifest.entries.Add(entry); continue; }
                    string alphaFile = FindAlpha(file);
                    if (alphaFile != null)
                    {
                        var alpha = BspTextureLoader.Load(alphaFile, srgb: false, failureReason: out reason);
                        if (alpha != null)
                        {
                            MergeAlpha(rgb, alpha);
                            UnityEngine.Object.DestroyImmediate(alpha);
                            entry.alphaSource = Path.GetFileName(alphaFile);
                        }
                        else entry.note = "alpha companion failed: " + reason;
                    }
                    if (src.maxSize > 0 && (rgb.width > src.maxSize || rgb.height > src.maxSize)) rgb = Downscale(rgb, src.maxSize);
                    rgb.name = src.name;
                    var final = ImportedTexturePolicy.Finalize(rgb, repeat: false);
                    var persisted = Persist(final, ResourcesFolder + "/" + src.name + ".asset");
                    entry.width = persisted.width; entry.height = persisted.height; entry.format = persisted.format.ToString();
                    entry.sha256 = Md3WeaponModelBuilder.Sha256OrNull(file);
                    entry.ok = true;
                    manifest.imported++;
                }
                catch (Exception e)
                {
                    entry.note = "exception: " + e.Message;
                    manifest.missing++;
                }
                manifest.entries.Add(entry);
            }
            File.WriteAllText(ManifestPath, JsonUtility.ToJson(manifest, true));
            AssetDatabase.SaveAssets();
            Debug.Log("[HudArtImporter] imported " + manifest.imported + ", missing " + manifest.missing + " -> " + ResourcesFolder);
            return manifest;
        }

        static string FindAlpha(string rgbFile)
        {
            string dir = Path.GetDirectoryName(rgbFile) ?? "";
            string stem = Path.GetFileNameWithoutExtension(rgbFile);
            foreach (var ext in new[] { ".jpg", ".png", ".tga", ".jpeg" })
            {
                string p = Path.Combine(dir, stem + "_alpha" + ext);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// DarkPlaces convention: the _alpha image's luminance is the opacity.
        static void MergeAlpha(Texture2D rgb, Texture2D alpha)
        {
            var px = rgb.GetPixels32();
            Color32[] a = alpha.width == rgb.width && alpha.height == rgb.height ? alpha.GetPixels32() : null;
            for (int i = 0; i < px.Length; i++)
            {
                byte v;
                if (a != null) v = a[i].r;
                else
                {
                    int x = i % rgb.width, y = i / rgb.width;
                    var c = alpha.GetPixelBilinear((x + 0.5f) / rgb.width, (y + 0.5f) / rgb.height);
                    v = (byte)Mathf.RoundToInt(c.r * 255f);
                }
                px[i].a = v;
            }
            rgb.SetPixels32(px);
            rgb.Apply(false, false);
        }

        static Texture2D Downscale(Texture2D src, int maxSize)
        {
            float k = Mathf.Min((float)maxSize / src.width, (float)maxSize / src.height);
            int w = Mathf.Max(4, Mathf.RoundToInt(src.width * k)), h = Mathf.Max(4, Mathf.RoundToInt(src.height * k));
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { name = src.name };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = src.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
            dst.SetPixels32(px);
            dst.Apply(false, false);
            UnityEngine.Object.DestroyImmediate(src);
            return dst;
        }

        static Texture2D Persist(Texture2D asset, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing == null) { AssetDatabase.CreateAsset(asset, path); return asset; }
            EditorUtility.CopySerialized(asset, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(asset);
            return existing;
        }

        static void EnsureFolders()
        {
            string[] parts = ResourcesFolder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
