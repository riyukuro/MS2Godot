using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Ms2.Formats.Pixels;

/// <summary>Pixel conversions of uncompressed DDS data.</summary>
public static class DdsPixels
{
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
