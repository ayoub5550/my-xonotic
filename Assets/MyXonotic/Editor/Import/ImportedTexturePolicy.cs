using System;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Decoded upstream images arrive as uncompressed RGBA32 without mipmaps.
    /// Shipping them that way makes a 30-map player several GB and shimmers on
    /// phones, so every persisted texture gets a mip chain and a GPU format.
    /// Format is chosen by XONOTIC_TEXTURE_FORMAT: "etc2" (default; mandatory
    /// in OpenGL ES 3.0, so it runs on every Android target we build for),
    /// "astc" (ASTC 6x6, smaller/better quality, most 2015+ GPUs) or "none"
    /// (raw RGBA32, for editor-side pixel-exact tests).
    /// </summary>
    public static class ImportedTexturePolicy
    {
        /// When an asset with the same content-hash path already exists, keep
        /// it instead of decoding+compressing again (XONOTIC_TEXTURE_REBUILD=1 forces a rebuild).
        public static bool ReuseExisting =>
            Environment.GetEnvironmentVariable("XONOTIC_TEXTURE_REBUILD") != "1";

        public static string FormatName
        {
            get
            {
                var v = Environment.GetEnvironmentVariable("XONOTIC_TEXTURE_FORMAT");
                return string.IsNullOrEmpty(v) ? "etc2" : v.Trim().ToLowerInvariant();
            }
        }

        public static Texture2D Finalize(Texture2D source, bool repeat)
        {
            if (source == null) return null;
            bool linear = !GraphicsFormatUtilityIsSrgb(source);
            Texture2D result;
            try
            {
                var pixels = source.GetPixels32();
                result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, linear)
                {
                    name = source.name,
                    wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4
                };
                result.SetPixels32(pixels);
                result.Apply(true, false);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ImportedTexturePolicy] mip generation failed for '" + source.name + "': " + e.Message);
                return source;
            }

            TextureFormat? target = null;
            switch (FormatName)
            {
                case "astc": target = TextureFormat.ASTC_6x6; break;
                case "none": break;
                default: target = TextureFormat.ETC2_RGBA8; break;
            }
            if (target.HasValue)
            {
                // Base-level alignment alone is insufficient: e.g. 600x600
                // has a 150x150 mip and Unity's ETC compressor fails internally
                // without throwing. Preserve the NPOT mip chain as RGBA32.
                bool blockAligned = target.Value != TextureFormat.ETC2_RGBA8 ||
                                    (Mathf.IsPowerOfTwo(result.width) && Mathf.IsPowerOfTwo(result.height) &&
                                     result.width >= 4 && result.height >= 4);
                if (blockAligned)
                {
                    try
                    {
                        EditorUtility.CompressTexture(result, target.Value, TextureCompressionQuality.Fast);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[ImportedTexturePolicy] compression to " + target.Value + " failed for '" +
                                         source.name + "', keeping RGBA32: " + e.Message);
                    }
                }
            }
            UnityEngine.Object.DestroyImmediate(source);
            return result;
        }

        static bool GraphicsFormatUtilityIsSrgb(Texture2D t)
        {
            return UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(t.graphicsFormat);
        }
    }
}
