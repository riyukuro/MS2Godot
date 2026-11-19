using Ms2.Formats.Pack;
using Ms2.Formats.Pixels;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// DdsPixels.SwizzleBgra gives the bytes the per-byte loop it replaced gave (kept here as the reference): on synthetic
/// buffers of every length around the vector width, and on every A8R8G8B8 / X8R8G8B8 DDS file of Resource/Gfx.
/// DdsPixels.ToRgba gives the bytes of the game's DDS reader it was moved from (its channel-mask loop, kept here as
/// <see cref="ReferenceRgba"/>) on every uncompressed DDS file of Resource/Gfx, and ImageBlob gives back what it stored.
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

    /// <summary>The game's DDS reader before the move (UiAssets.cs DdsLoader.Load, its pixel loop for every mask).</summary>
    static byte[] ReferenceRgba(byte[] d)
    {
        int h = BitConverter.ToInt32(d, 12), w = BitConverter.ToInt32(d, 16);
        uint pfFlags = BitConverter.ToUInt32(d, 80);
        int bits = BitConverter.ToInt32(d, 88);
        uint rm = BitConverter.ToUInt32(d, 92), gm = BitConverter.ToUInt32(d, 96), bm = BitConverter.ToUInt32(d, 100), am = BitConverter.ToUInt32(d, 104);
        int bpp = bits / 8, stride = w * bpp;
        var rgba = new byte[w * h * 4];
        static (uint m, int s, int n) Ch(uint m) { int s = 0, n = 0; if (m != 0) { while ((m >> s & 1) == 0) s++; while ((m >> (s + n) & 1) != 0) n++; } return (m, s, n); }
        static byte Get((uint m, int s, int n) c, uint px) { if (c.m == 0) return 255; uint v = (px & c.m) >> c.s; return (byte)(c.n >= 8 ? v >> (c.n - 8) : v * 255 / ((1u << c.n) - 1)); }
        var r = Ch(rm); var g = Ch(gm); var b = Ch(bm); var a = Ch(am);
        bool alphaOnly = bits == 8 && (pfFlags & 2) != 0 && rm == 0;
        bool lum = (pfFlags & 0x20000) != 0, hasAlpha = (pfFlags & 1) != 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = 128 + y * stride + x * bpp;
                uint px = 0;
                for (int k = 0; k < bpp; k++) px |= (uint)d[o + k] << (8 * k);
                int q = (y * w + x) * 4;
                if (alphaOnly) { rgba[q] = rgba[q + 1] = rgba[q + 2] = 255; rgba[q + 3] = (byte)px; continue; }
                if (lum) { var l = Get(r, px); rgba[q] = rgba[q + 1] = rgba[q + 2] = l; rgba[q + 3] = am != 0 ? Get(a, px) : (byte)255; continue; }
                rgba[q] = Get(r, px); rgba[q + 1] = Get(g, px); rgba[q + 2] = Get(b, px);
                rgba[q + 3] = hasAlpha ? Get(a, px) : (byte)255;
            }
        return rgba;
    }

    static byte[] Dds(int w, int h, uint flags, int bits, uint rm, uint gm, uint bm, uint am, Random rnd)
    {
        var d = new byte[128 + w * h * bits / 8];
        "DDS "u8.CopyTo(d);
        BitConverter.TryWriteBytes(d.AsSpan(12), h); BitConverter.TryWriteBytes(d.AsSpan(16), w);
        BitConverter.TryWriteBytes(d.AsSpan(80), flags); BitConverter.TryWriteBytes(d.AsSpan(88), bits);
        BitConverter.TryWriteBytes(d.AsSpan(92), rm); BitConverter.TryWriteBytes(d.AsSpan(96), gm);
        BitConverter.TryWriteBytes(d.AsSpan(100), bm); BitConverter.TryWriteBytes(d.AsSpan(104), am);
        rnd.NextBytes(d.AsSpan(128));
        return d;
    }

    [Test]
    public void ToRgbaMatchesTheReaderOnEveryMaskKind()
    {
        var rnd = new Random(5);
        var kinds = new (uint flags, int bits, uint r, uint g, uint b, uint a)[]
        {
            (0x41, 32, 0x00ff0000, 0x0000ff00, 0x000000ff, 0xff000000),   // A8R8G8B8
            (0x40, 32, 0x00ff0000, 0x0000ff00, 0x000000ff, 0),            // X8R8G8B8
            (0x41, 32, 0x000000ff, 0x0000ff00, 0x00ff0000, 0xff000000),   // A8B8G8R8
            (0x40, 24, 0xff0000, 0x00ff00, 0x0000ff, 0),                  // R8G8B8
            (0x41, 16, 0x7c00, 0x03e0, 0x001f, 0x8000),                   // A1R5G5B5
            (0x41, 16, 0x0f00, 0x00f0, 0x000f, 0xf000),                   // A4R4G4B4
            (0x40, 16, 0xf800, 0x07e0, 0x001f, 0),                        // R5G6B5
            (0x02, 8, 0, 0, 0, 0xff),                                     // A8
            (0x20000, 8, 0xff, 0, 0, 0),                                  // L8
            (0x20001, 16, 0x00ff, 0, 0, 0xff00),                          // A8L8
        };
        foreach (var k in kinds)
            foreach (var (w, h) in new[] { (1, 1), (3, 5), (17, 9) })
            {
                var d = Dds(w, h, k.flags, k.bits, k.r, k.g, k.b, k.a, rnd);
                var got = DdsPixels.ToRgba(d, out int gw, out int gh);
                Assert.That((gw, gh), Is.EqualTo((w, h)));
                Assert.That(got, Is.EqualTo(ReferenceRgba(d)), $"{k.bits} bits {k.r:x}/{k.g:x}/{k.b:x}/{k.a:x}, {w}x{h}");
            }
        Assert.That(DdsPixels.ToRgba(new byte[100], out _, out _), Is.Null);
        var dxt = Dds(4, 4, 4, 0, 0, 0, 0, 0, rnd); BitConverter.TryWriteBytes(dxt.AsSpan(84), 0x31545844u);
        Assert.That(DdsPixels.IsBlockCompressed(dxt), Is.True);
        Assert.That(DdsPixels.ToRgba(dxt, out _, out _), Is.Null);
        var shortFile = Dds(8, 8, 0x41, 32, 0x00ff0000, 0x0000ff00, 0x000000ff, 0xff000000, rnd)[..200];
        Assert.Throws<InvalidDataException>(() => DdsPixels.ToRgba(shortFile, out _, out _));
    }

    [Test]
    public void ImageBlobGivesBackWhatItStoredAndRefusesDamage()
    {
        var rnd = new Random(3);
        var px = new byte[64 * 32 * 4];
        for (int i = 0; i < px.Length; i++) px[i] = (byte)(i / 64 % 7 == 0 ? rnd.Next() : i / 256);
        var body = Cache.ImageBlob.Write(Cache.ImageBlob.Kind.Rgba, 64, 32, px);
        var (kind, w, h, bytes) = Cache.ImageBlob.Read(body);
        Assert.That((kind, w, h), Is.EqualTo((Cache.ImageBlob.Kind.Rgba, 64, 32)));
        Assert.That(bytes, Is.EqualTo(px));
        var file = Cache.ImageBlob.Read(Cache.ImageBlob.Write(Cache.ImageBlob.Kind.File, 0, 0, new byte[] { 1, 2, 3 }));
        Assert.That((file.kind, file.bytes), Is.EqualTo((Cache.ImageBlob.Kind.File, new byte[] { 1, 2, 3 })));
        Assert.That(Cache.ImageBlob.Read(Cache.ImageBlob.Write(Cache.ImageBlob.Kind.File, 0, 0, Array.Empty<byte>())).bytes, Is.Empty);
        // a short body, a wrong kind, a size that is not the pixels' length, a cut block
        Assert.Throws<InvalidDataException>(() => Cache.ImageBlob.Read(body.AsSpan(0, 5)));
        var k = (byte[])body.Clone(); k[0] = 9;
        Assert.Throws<InvalidDataException>(() => Cache.ImageBlob.Read(k));
        var s = (byte[])body.Clone(); s[1] = 65;
        Assert.Throws<InvalidDataException>(() => Cache.ImageBlob.Read(s));
        Assert.Throws<InvalidDataException>(() => Cache.ImageBlob.Read(body.AsSpan(0, body.Length - 10)));
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
        int files = 0, other = 0, converted = 0, blocks = 0, stored = 0;
        foreach (var name in packs.List("Resource/Gfx").Where(n => n.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)))
        {
            var d = packs.Read("Resource/Gfx", name);
            if (d.Length < 128) continue;
            // the moved reader: the same pixels as before on every uncompressed file; every 16th through the cache body
            var rgba = DdsPixels.ToRgba(d, out int rw, out int rh);
            if (DdsPixels.IsBlockCompressed(d)) { blocks++; Assert.That(rgba, Is.Null, name); }
            else if (rgba != null)
            {
                Assert.That(rgba.AsSpan().SequenceEqual(ReferenceRgba(d)), Is.True, name + ": ToRgba");
                if (converted++ % 16 == 0)
                {
                    var back = Cache.ImageBlob.Read(Cache.ImageBlob.Write(Cache.ImageBlob.Kind.Rgba, rw, rh, rgba));
                    Assert.That((back.w, back.h) == (rw, rh) && back.bytes.AsSpan().SequenceEqual(rgba), Is.True, name + ": ImageBlob");
                    stored++;
                }
            }
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
        TestContext.WriteLine($"swizzled atlases {files}, other formats {other}; converted {converted} (through ImageBlob {stored}), block-compressed {blocks}");
        Assert.That(files, Is.GreaterThan(10));
    }
}
