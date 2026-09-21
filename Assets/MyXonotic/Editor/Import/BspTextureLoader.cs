using System;
using System.IO;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Editor-only, from-scratch image loaders for the on-disk upstream formats
    /// <see cref="XonoticContentResolver"/> resolves paths for. Unity's built-in
    /// <c>ImageConversion.LoadImage</c> only understands PNG/JPG, so a minimal
    /// TGA decoder is implemented here against the public, decades-old TGA
    /// file-format specification (image/color-map type byte, 18-byte header,
    /// optional run-length packets) — no engine or third-party source was
    /// read or copied to write it. DDS (block-compressed) is explicitly not
    /// decoded by this pass; callers get a clear failure reason instead of a
    /// silently wrong texture.
    /// </summary>
    public static class BspTextureLoader
    {
        // A legitimate Xonotic map/UI texture is at most a few thousand
        // pixels per side. This ceiling only guards against treating a
        // corrupt/hostile header as a huge allocation request.
        private const int MaxDimension = 8192;
        private const long MaxFileSizeBytes = 64L * 1024 * 1024;

        /// <summary>
        /// Loads <paramref name="absolutePath"/> into a new, non-persisted
        /// Texture2D. Returns null and sets <paramref name="failureReason"/>
        /// on any recognized failure (missing file, oversized, unsupported
        /// TGA variant, DDS, corrupt data) instead of throwing, so a single
        /// bad/unsupported source image only drops one material's texture
        /// with a warning rather than aborting the whole import.
        /// </summary>
        public static Texture2D Load(string absolutePath, bool srgb, out string failureReason)
        {
            failureReason = null;
            try
            {
                if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                {
                    failureReason = "file not found: " + absolutePath;
                    return null;
                }

                var info = new FileInfo(absolutePath);
                if (info.Length > MaxFileSizeBytes)
                {
                    failureReason = string.Format("'{0}' is {1} bytes, exceeding the {2}-byte safety ceiling.",
                        absolutePath, info.Length, MaxFileSizeBytes);
                    return null;
                }

                string ext = Path.GetExtension(absolutePath).ToLowerInvariant();
                byte[] bytes = File.ReadAllBytes(absolutePath);

                switch (ext)
                {
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                    {
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, !srgb);
                        if (!UnityEngine.ImageConversion.LoadImage(tex, bytes, false))
                        {
                            UnityEngine.Object.DestroyImmediate(tex);
                            failureReason = "ImageConversion.LoadImage failed to decode '" + absolutePath + "'.";
                            return null;
                        }
                        tex.name = Path.GetFileNameWithoutExtension(absolutePath);
                        return tex;
                    }
                    case ".tga":
                        return LoadTga(bytes, absolutePath, srgb, out failureReason);
                    case ".dds":
                        failureReason = "DDS ('" + absolutePath + "') is not decoded by this importer pass; " +
                                         "block-compressed texture support is a known gap, see AGENTS.md.";
                        return null;
                    default:
                        failureReason = "unrecognized image extension '" + ext + "' for '" + absolutePath + "'.";
                        return null;
                }
            }
            catch (Exception e)
            {
                failureReason = "exception loading '" + absolutePath + "': " + e.Message;
                return null;
            }
        }

        /// <summary>
        /// Decodes uncompressed (type 2) or run-length encoded (type 10)
        /// 24/32-bit true-color TGA data. Color-mapped and greyscale TGA
        /// variants are recognized but rejected with a clear reason rather
        /// than silently mis-decoded — none appear in the current upstream
        /// resource pack, but a future/unbounded content root could contain
        /// one, and this must fail loud rather than pass corrupt pixels on.
        /// </summary>
        private static Texture2D LoadTga(byte[] data, string path, bool srgb, out string failureReason)
        {
            failureReason = null;
            const int HeaderSize = 18;
            if (data.Length < HeaderSize)
            {
                failureReason = "'" + path + "' is smaller than a TGA header.";
                return null;
            }

            byte idLength = data[0];
            byte colorMapType = data[1];
            byte imageType = data[2];
            int width = data[12] | (data[13] << 8);
            int height = data[14] | (data[15] << 8);
            byte bpp = data[16];
            byte descriptor = data[17];
            bool topToBottom = (descriptor & 0x20) != 0;

            if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
            {
                failureReason = string.Format("'{0}' has an out-of-range size {1}x{2}.", path, width, height);
                return null;
            }
            if (colorMapType != 0)
            {
                failureReason = "'" + path + "' uses a TGA color map; only true-color TGA is supported.";
                return null;
            }
            if (imageType != 2 && imageType != 10)
            {
                failureReason = string.Format(
                    "'{0}' has TGA image type {1}; only uncompressed(2)/RLE(10) true-color is supported.", path, imageType);
                return null;
            }
            if (bpp != 24 && bpp != 32)
            {
                failureReason = string.Format("'{0}' has {1} bits/pixel; only 24/32-bit TGA is supported.", path, bpp);
                return null;
            }

            int bytesPerPixel = bpp / 8;
            int dataStart = HeaderSize + idLength;
            if (dataStart > data.Length)
            {
                failureReason = "'" + path + "' image-ID field runs past end of file.";
                return null;
            }

            var pixels = new Color32[width * height]; // row 0 = bottom, matching Texture2D.SetPixels32.
            int fileRowStride = width * bytesPerPixel;

            if (imageType == 2)
            {
                long need = (long)dataStart + (long)fileRowStride * height;
                if (need > data.Length)
                {
                    failureReason = "'" + path + "' uncompressed pixel data runs past end of file.";
                    return null;
                }
                for (int row = 0; row < height; row++)
                {
                    int destRow = topToBottom ? (height - 1 - row) : row;
                    int srcOffset = dataStart + row * fileRowStride;
                    for (int x = 0; x < width; x++)
                    {
                        int so = srcOffset + x * bytesPerPixel;
                        byte b = data[so + 0], g = data[so + 1], r = data[so + 2];
                        byte a = bytesPerPixel == 4 ? data[so + 3] : (byte)255;
                        pixels[destRow * width + x] = new Color32(r, g, b, a);
                    }
                }
            }
            else // RLE (type 10)
            {
                int pos = dataStart;
                // Decode into file-order rows first (top-of-file row 0), then
                // remap to Unity's bottom-up pixel array according to origin.
                var fileOrder = new Color32[width * height];
                int pixelIndex = 0;
                int totalPixels = width * height;
                while (pixelIndex < totalPixels)
                {
                    if (pos >= data.Length)
                    {
                        failureReason = "'" + path + "' RLE stream ends before all pixels were decoded.";
                        return null;
                    }
                    byte packetHeader = data[pos++];
                    int count = (packetHeader & 0x7F) + 1;
                    bool isRun = (packetHeader & 0x80) != 0;
                    if (isRun)
                    {
                        if (pos + bytesPerPixel > data.Length)
                        {
                            failureReason = "'" + path + "' RLE run packet truncated.";
                            return null;
                        }
                        byte b = data[pos + 0], g = data[pos + 1], r = data[pos + 2];
                        byte a = bytesPerPixel == 4 ? data[pos + 3] : (byte)255;
                        pos += bytesPerPixel;
                        var c = new Color32(r, g, b, a);
                        for (int i = 0; i < count && pixelIndex < totalPixels; i++)
                            fileOrder[pixelIndex++] = c;
                    }
                    else
                    {
                        int need = count * bytesPerPixel;
                        if (pos + need > data.Length)
                        {
                            failureReason = "'" + path + "' RLE raw packet truncated.";
                            return null;
                        }
                        for (int i = 0; i < count && pixelIndex < totalPixels; i++)
                        {
                            int so = pos + i * bytesPerPixel;
                            byte b = data[so + 0], g = data[so + 1], r = data[so + 2];
                            byte a = bytesPerPixel == 4 ? data[so + 3] : (byte)255;
                            fileOrder[pixelIndex++] = new Color32(r, g, b, a);
                        }
                        pos += need;
                    }
                }
                for (int row = 0; row < height; row++)
                {
                    int destRow = topToBottom ? (height - 1 - row) : row;
                    Array.Copy(fileOrder, row * width, pixels, destRow * width, width);
                }
            }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, !srgb);
            tex.name = Path.GetFileNameWithoutExtension(path);
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>
        /// Builds a Texture2D from one internal 128x128 RGB lightmap block
        /// (see BspReader.ReadLightmaps / BspDocument.Lightmaps). Row order
        /// in the lump is top-to-bottom like most Quake-family binary
        /// formats; flipped here to Unity's bottom-up SetPixels32 order.
        /// This orientation is an implementation assumption consistent with
        /// the format's other top-to-bottom image data, not independently
        /// verified pixel-for-pixel against the reference renderer.
        /// </summary>
        public static Texture2D LoadInternalLightmap(byte[] block, bool srgb, string debugName)
        {
            const int Size = 128;
            if (block == null || block.Length != Size * Size * 3) return null;
            var pixels = new Color32[Size * Size];
            for (int row = 0; row < Size; row++)
            {
                int destRow = Size - 1 - row; // top-to-bottom source -> bottom-up Unity order.
                int srcOffset = row * Size * 3;
                for (int x = 0; x < Size; x++)
                {
                    int so = srcOffset + x * 3;
                    pixels[destRow * Size + x] = new Color32(block[so], block[so + 1], block[so + 2], 255);
                }
            }
            var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false, !srgb);
            tex.name = debugName;
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}
