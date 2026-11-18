using Ms2.Formats.Pack;
using Ms2.Formats.Pixels;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// DdsPixels.SwizzleBgra gives the bytes the per-byte loop it replaced gave (kept here as the reference): on synthetic
/// buffers of every length around the vector width, and on every A8R8G8B8 / X8R8G8B8 DDS file of Resource/Gfx.
/// </summary>
public class DdsPixelsTests
{
    static byte[] Reference(ReadOnlySpan<byte> src, bool hasAlpha)
    {
        var dst = new byte[src.Length & ~3];
        for (int o = 0; o + 4 <= src.Length; o += 4)
        {
            dst[o] = src[o + 2]; dst[o + 1] = src[o + 1]; dst[o + 2] = src[o];
            dst[o + 3] = hasAlpha ? src[o + 3] : (byte)255;
        }
        return dst;
    }

    [Test]
    public void SyntheticBuffersMatchTheByteLoop()
    {
        var rnd = new Random(11);
        foreach (bool alpha in new[] { true, false })
            for (int len = 0; len <= 70; len++)
            {
                var src = new byte[len]; rnd.NextBytes(src);
                var dst = new byte[len & ~3];
                DdsPixels.SwizzleBgra(src, dst, alpha);
                Assert.That(dst, Is.EqualTo(Reference(src, alpha)), $"length {len}, alpha {alpha}");
            }
        Assert.Throws<ArgumentException>(() => DdsPixels.SwizzleBgra(new byte[8], new byte[4], true));
    }

    [Test]
    public void EveryGfxAtlasMatchesTheByteLoop()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new PackSet(data);
        int files = 0, other = 0;
        foreach (var name in packs.List("Resource/Gfx").Where(n => n.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)))
        {
            var d = packs.Read("Resource/Gfx", name);
            if (d.Length < 128) continue;
            int h = BitConverter.ToInt32(d, 12), w = BitConverter.ToInt32(d, 16);
            uint flags = BitConverter.ToUInt32(d, 80), fourCc = BitConverter.ToUInt32(d, 84);
            int bits = BitConverter.ToInt32(d, 88);
            uint rm = BitConverter.ToUInt32(d, 92), gm = BitConverter.ToUInt32(d, 96), bm = BitConverter.ToUInt32(d, 100), am = BitConverter.ToUInt32(d, 104);
            bool hasAlpha = (flags & 1) != 0;
            if ((flags & 4) != 0 && fourCc != 0 || bits != 32 || rm != 0x00ff0000 || gm != 0x0000ff00 || bm != 0x000000ff
                || !(am == 0xff000000 || !hasAlpha) || (flags & 0x20000) != 0 || d.Length < 128 + w * h * 4) { other++; continue; }
            var src = d.AsSpan(128, w * h * 4);
            var dst = new byte[w * h * 4];
            DdsPixels.SwizzleBgra(src, dst, hasAlpha);
            Assert.That(dst.AsSpan().SequenceEqual(Reference(src, hasAlpha)), Is.True, name);
            files++;
        }
        TestContext.WriteLine($"swizzled atlases {files}, other formats {other}");
        Assert.That(files, Is.GreaterThan(10));
    }
}
