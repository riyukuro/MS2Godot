using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Ms2.Formats.Pixels;

/// <summary>Pixel conversions of uncompressed DDS data.</summary>
public static class DdsPixels
{
    /// <summary>The file is a DDS file whose pixels are block-compressed (a fourCC format: Godot's own DDS reader takes it).</summary>
    public static bool IsBlockCompressed(byte[] d) =>
        IsDds(d) && (BitConverter.ToUInt32(d, 80) & 4) != 0 && BitConverter.ToUInt32(d, 84) != 0;

    static bool IsDds(byte[] d) => d != null && d.Length >= 128 && d[0] == 'D' && d[1] == 'D' && d[2] == 'S';

    /// <summary>One channel mask: its shift and bit width, measured once per file instead of once per pixel.</summary>
    readonly struct Channel
    {
        readonly uint mask; readonly int shift, width;
        public Channel(uint m)
        {
            mask = m; shift = 0; width = 0;
            if (m == 0) return;
            while ((m >> shift & 1) == 0) shift++;
            while ((m >> (shift + width) & 1) != 0) width++;
        }
        /// <summary>The channel as 8 bits: the top 8 bits of a wider field, a narrower one scaled to 0..255 (255 without a mask).</summary>
        public byte Get(uint px)
        {
            if (mask == 0) return 255;
            uint v = (px & mask) >> shift;
            return (byte)(width >= 8 ? v >> (width - 8) : v * 255 / ((1u << width) - 1));
        }
    }

    /// <summary>
    /// The pixels of an uncompressed DDS file (8, 16, 24 or 32 bits per pixel, by its channel masks) as R, G, B, A bytes,
    /// <paramref name="w"/> by <paramref name="h"/>; null when it is not a DDS file, is block-compressed
    /// (<see cref="IsBlockCompressed"/>) or has another pixel size. An alpha-only file gives white with its alpha, a
    /// luminance file grey; a missing alpha is 255. Throws InvalidDataException when the file is shorter than its pixels.
    /// </summary>
    public static byte[] ToRgba(byte[] d, out int w, out int h)
    {
        w = h = 0;
        if (!IsDds(d) || IsBlockCompressed(d)) return null;
        h = BitConverter.ToInt32(d, 12); w = BitConverter.ToInt32(d, 16);
        uint pfFlags = BitConverter.ToUInt32(d, 80);
        int bits = BitConverter.ToInt32(d, 88);
        uint rm = BitConverter.ToUInt32(d, 92), gm = BitConverter.ToUInt32(d, 96), bm = BitConverter.ToUInt32(d, 100), am = BitConverter.ToUInt32(d, 104);
        if (bits != 32 && bits != 24 && bits != 16 && bits != 8) { w = h = 0; return null; }
        int bpp = bits / 8;
        if (w < 0 || h < 0 || 128 + (long)w * h * bpp > d.Length) throw new InvalidDataException($"a {w}x{h} DDS file of {bits} bits per pixel is {d.Length} bytes");
        int stride = w * bpp;
        var rgba = new byte[w * h * 4];
        var r = new Channel(rm); var g = new Channel(gm); var b = new Channel(bm); var a = new Channel(am);
        bool alphaOnly = bits == 8 && (pfFlags & 2) != 0 && rm == 0;
        bool lum = (pfFlags & 0x20000) != 0, hasAlpha = (pfFlags & 1) != 0;
        if (!alphaOnly && !lum && bits == 32 && rm == 0x00ff0000 && gm == 0x0000ff00 && bm == 0x000000ff && (am == 0xff000000 || !hasAlpha))
        {
            // A8R8G8B8 / X8R8G8B8, the usual atlas: every channel is a whole byte, so this is a byte swizzle
            SwizzleBgra(d.AsSpan(128, rgba.Length), rgba, hasAlpha);
            return rgba;
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = 128 + y * stride + x * bpp;
                uint px = 0;
                for (int k = 0; k < bpp; k++) px |= (uint)d[o + k] << (8 * k);
                int q = (y * w + x) * 4;
                if (alphaOnly) { rgba[q] = rgba[q + 1] = rgba[q + 2] = 255; rgba[q + 3] = (byte)px; continue; }
                if (lum) { var l = r.Get(px); rgba[q] = rgba[q + 1] = rgba[q + 2] = l; rgba[q + 3] = am != 0 ? a.Get(px) : (byte)255; continue; }
                rgba[q] = r.Get(px); rgba[q + 1] = g.Get(px); rgba[q + 2] = b.Get(px);
                rgba[q + 3] = hasAlpha ? a.Get(px) : (byte)255;
            }
        return rgba;
    }

    /// <summary>
    /// A8R8G8B8 / X8R8G8B8 pixels (B, G, R, A bytes) to R, G, B, A bytes, alpha 255 when the file has none; 16 bytes at a time
    /// where the CPU has 128-bit vectors, a byte loop for the rest. Converts the whole 4-byte pixels of <paramref name="src"/>.
    /// </summary>
    public static void SwizzleBgra(ReadOnlySpan<byte> src, Span<byte> dst, bool hasAlpha)
    {
        int n = src.Length & ~3, i = 0;
        if (dst.Length < n) throw new ArgumentException("the destination is shorter than the source", nameof(dst));
        if (Vector128.IsHardwareAccelerated)
        {
            var order = Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
            var fill = Vector128.Create(hasAlpha ? 0u : 0xff000000u).AsByte();
            ref byte s0 = ref MemoryMarshal.GetReference(src);
            ref byte d0 = ref MemoryMarshal.GetReference(dst);
            for (; i + 16 <= n; i += 16)
                (Vector128.Shuffle(Vector128.LoadUnsafe(ref s0, (nuint)i), order) | fill).StoreUnsafe(ref d0, (nuint)i);
        }
        for (; i < n; i += 4)
        {
            dst[i] = src[i + 2]; dst[i + 1] = src[i + 1]; dst[i + 2] = src[i];
            dst[i + 3] = hasAlpha ? src[i + 3] : (byte)255;
        }
    }
}
