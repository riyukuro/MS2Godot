using Ms2.Formats.Pack;
using Ms2.Video.Mpeg1;
using Ms2.Video.Usm;
using NUnit.Framework;
using static Ms2.Video.Tests.Synthetic;

namespace Ms2.Video.Tests;

public class VideoTests
{
    // ---- bit reader ----

    [Test]
    public void BitBufferReadsAndFindsStartCodes()
    {
        var b = new BitBuffer(new byte[] { 0b1011_0011, 0xff, 0, 0, 1, 0xb3, 0x12, 0, 0, 1, 0x00, 0, 0, 0, 0, 0 });
        Assert.That(b.Read(3), Is.EqualTo(0b101));
        Assert.That(b.Read(7), Is.EqualTo(0b1_0011_11));
        Assert.That(b.NextStartCode(), Is.EqualTo(0xb3));
        Assert.That(b.Read(8), Is.EqualTo(0x12));
        Assert.That(b.HasStartCode(0x00), Is.EqualTo(0x00));
        Assert.That(b.BitIndex, Is.EqualTo(7 * 8), "HasStartCode leaves the position");
        Assert.That(b.PeekNonZero(23), Is.False);
        Assert.That(b.FindStartCode(0x00), Is.EqualTo(0x00));
        Assert.That(b.NextStartCode(), Is.EqualTo(-1));
    }

    [TestCase("100", 0), TestCase("00", 1), TestCase("01", 2), TestCase("101", 3), TestCase("1110", 5)]
    [TestCase("1111110", 8), TestCase("11111110", 9), TestCase("111111110", 10), TestCase("111111111", 11)]
    public void LumaDcSizeVlc(string code, int size)
    {
        var w = new BitWriter().Code(code).Bits(0, 16);
        Assert.That(new BitBuffer(w.ToArray()).ReadVlc(Tables.DctSizeLuminance), Is.EqualTo(size));
    }

    [TestCase("00", 0), TestCase("10", 2), TestCase("11111110", 8), TestCase("1111111110", 10), TestCase("1111111111", 11)]
    public void ChromaDcSizeVlc(string code, int size)
    {
        var w = new BitWriter().Code(code).Bits(0, 16);
        Assert.That(new BitBuffer(w.ToArray()).ReadVlc(Tables.DctSizeChrominance), Is.EqualTo(size));
    }

    // ---- MPEG-1 ----

    static void AssertFlat(YuvFrame f, int y, int cb, int cr)
    {
        Assert.That(f.Width, Is.EqualTo(16));
        Assert.That(f.Height, Is.EqualTo(16));
        Assert.That(f.Y.Take(16 * 16).Distinct(), Is.EqualTo(new[] { (byte)y }));
        Assert.That(f.Cb.Take(8 * 8).Distinct(), Is.EqualTo(new[] { (byte)cb }));
        Assert.That(f.Cr.Take(8 * 8).Distinct(), Is.EqualTo(new[] { (byte)cr }));
    }

    [TestCase(8), TestCase(9), TestCase(11)]
    public void DecodesIntraPicturesInOrder(int precision)
    {
        var d = new Mpeg1Decoder(MakeMpeg1(8, precision, new Picture(100, 90, 200), new Picture(50, 128, 128), new Picture(235, 16, 240)));
        var f = d.Decode();
        Assert.That(d.Width, Is.EqualTo(16));
        Assert.That(d.FrameRate, Is.EqualTo(60));
        Assert.That(d.IntraDcPrecision, Is.EqualTo(precision), "IDCPREC user data");
        AssertFlat(f, 100, 90, 200);
        Assert.That(f.Time, Is.EqualTo(0));
        f = d.Decode();
        AssertFlat(f, 50, 128, 128);
        Assert.That(f.Time, Is.EqualTo(1 / 60.0).Within(1e-9));
        AssertFlat(d.Decode(), 235, 16, 240);   // the held-back last reference picture
        Assert.That(d.Decode(), Is.Null);
        Assert.That(d.HasEnded, Is.True);
        Assert.That(d.FramesDecoded, Is.EqualTo(3));
    }

    [Test]
    public void PredictedPictureCopiesItsReference()
    {
        var d = new Mpeg1Decoder(MakeMpeg1(5, 8, new Picture(77, 100, 150), new Picture(0, 0, 0, Predicted: true), new Picture(10, 20, 30)));
        AssertFlat(d.Decode(), 77, 100, 150);
        Assert.That(d.FrameRate, Is.EqualTo(30));
        AssertFlat(d.Decode(), 77, 100, 150);
        AssertFlat(d.Decode(), 10, 20, 30);
        Assert.That(d.Decode(), Is.Null);
    }

    [Test]
    public void RewindStartsOver()
    {
        var d = new Mpeg1Decoder(MakeMpeg1(8, 11, new Picture(100, 90, 200), new Picture(50, 128, 128)));
        while (d.Decode() != null) { }
        d.Rewind();
        Assert.That(d.HasEnded, Is.False);
        AssertFlat(d.Decode(), 100, 90, 200);
        Assert.That(d.FramesDecoded, Is.EqualTo(1));
    }

    [Test]
    public void ToRgbaIsStudioRangeBt601()
    {
        var f = new YuvFrame(2, 2, 16, 16, 8, 8);
        Array.Fill(f.Y, (byte)16); Array.Fill(f.Cb, (byte)128); Array.Fill(f.Cr, (byte)128);
        var rgba = new byte[16];
        f.ToRgba(rgba, 8);
        Assert.That(rgba, Is.EqualTo(Enumerable.Repeat(new byte[] { 0, 0, 0, 255 }, 4).SelectMany(x => x)));
        Array.Fill(f.Y, (byte)235);
        f.ToRgba(rgba, 8);
        Assert.That(rgba.Take(3), Is.All.GreaterThanOrEqualTo(254));   // pl_mpeg's integer coefficients
        Array.Fill(f.Y, (byte)82); Array.Fill(f.Cb, (byte)90); Array.Fill(f.Cr, (byte)240);   // BT.601 red
        f.ToRgba(rgba, 8);
        Assert.That(rgba[0], Is.GreaterThan(250));
        Assert.That(rgba[1], Is.LessThan(5));
        Assert.That(rgba[2], Is.LessThan(5));
    }

    // ---- @UTF and USM ----

    [Test]
    public void UtfTableColumns()
    {
        var t = UtfTable.Read(Utf("TEST", 2,
            new Column { Name = "name", Type = 0xa, Values = new object[] { "a.usm", "b.avi" } },
            new Column { Name = "kind", Type = 0xa, Constant = true, Values = new object[] { "movie" } },
            new Column { Name = "count", Type = 2, Values = new object[] { 7, 65535 } },
            new Column { Name = "size", Type = 4, Values = new object[] { 123456, 0 } },
            new Column { Name = "big", Type = 7, Values = new object[] { -5L, 1L << 40 } },
            new Column { Name = "rate", Type = 8, Values = new object[] { 29.97f, 60f } },
            new Column { Name = "none", Type = 5, Zero = true },
            new Column { Name = "blob", Type = 0xb, Values = new object[] { new byte[] { 1, 2, 3 }, Array.Empty<byte>() } }));
        Assert.That(t.Name, Is.EqualTo("TEST"));
        Assert.That(t.RowCount, Is.EqualTo(2));
        Assert.That(t.Columns, Has.Count.EqualTo(8));
        Assert.That(t.String(0, "name"), Is.EqualTo("a.usm"));
        Assert.That(t.String(1, "name"), Is.EqualTo("b.avi"));
        Assert.That(t.String(1, "kind"), Is.EqualTo("movie"));
        Assert.That(t.Long(1, "count"), Is.EqualTo(65535));
        Assert.That(t.Long(0, "size"), Is.EqualTo(123456));
        Assert.That(t.Long(0, "big"), Is.EqualTo(-5));
        Assert.That(t.Long(1, "big"), Is.EqualTo(1L << 40));
        Assert.That((float)t[0, "rate"], Is.EqualTo(29.97f));
        Assert.That(t.Long(0, "none"), Is.EqualTo(0));
        Assert.That((byte[])t[0, "blob"], Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(t[0, "missing"], Is.Null);
        Assert.Throws<InvalidDataException>(() => UtfTable.Read(new byte[40]));
    }

    [Test]
    public void UsmDemuxJoinsVideoPayloads()
    {
        var video = Enumerable.Range(0, 1000).Select(i => (byte)(i * 7)).ToArray();
        var usm = UsmFile.Read(MakeUsm(video, VideoHeader(16, 16, 30000, 1001, 3, 8), chunk: 37));
        Assert.That(usm.Directory?.Name, Is.EqualTo("CRIUSF_DIR_STREAM"));
        Assert.That(usm.Directory.String(1, "filename"), Is.EqualTo("test.avi"));
        Assert.That(usm.Video, Has.Count.EqualTo(1));
        Assert.That(usm.HasAudio, Is.False);
        Assert.That(usm.UnknownChunks, Is.EqualTo(1));
        var s = usm.Video[0];
        Assert.That(s.Header?.Name, Is.EqualTo("VIDEO_HDRINFO"));
        Assert.That(s.DataChunks, Is.EqualTo((1000 + 36) / 37));
        Assert.That(s.Data.AsSpan(0, s.Length).ToArray(), Is.EqualTo(video), "payloads without their padding");
    }

    [Test]
    public void UsmReportsAudio()
    {
        var usm = UsmFile.Read(MakeUsm(new byte[100], VideoHeader(16, 16, 60, 1, 1, 8), audio: new byte[] { 0x80, 0, 0, 0x1c }));
        Assert.That(usm.HasAudio, Is.True);
        Assert.That(usm.Audio[0].Header?.Long(0, "sampling_rate"), Is.EqualTo(48000));
        Assert.That(usm.Audio[0].Data.Take(usm.Audio[0].Length), Is.EqualTo(new byte[] { 0x80, 0, 0, 0x1c }));
    }

    [Test]
    public void UsmVideoUsesTheStreamHeader()
    {
        var es = MakeMpeg1(8, 11, new Picture(100, 90, 200), new Picture(50, 128, 128));
        var v = UsmVideo.Open(MakeUsm(es, VideoHeader(16, 16, 30000, 1001, 2, 11)));
        Assert.That(v, Is.Not.Null);
        Assert.That(v.Width, Is.EqualTo(16));
        Assert.That(v.FrameRate, Is.EqualTo(30000 / 1001.0).Within(1e-9), "header rate over the sequence header's 60");
        Assert.That(v.FrameCount, Is.EqualTo(2));
        Assert.That(v.Duration, Is.EqualTo(2 * 1001 / 30000.0).Within(1e-9));
        Assert.That(v.Decoder.IntraDcPrecision, Is.EqualTo(11));
        AssertFlat(v.FrameAt(0), 100, 90, 200);
        Assert.That(v.FrameAt(0.01), Is.Null, "still the first frame");
        AssertFlat(v.FrameAt(0.04), 50, 128, 128);
        Assert.That(v.FrameAt(1), Is.Null);
        Assert.That(v.Ended, Is.True);
        Assert.That(UsmVideo.Open(MakeUsm(new byte[64], VideoHeader(16, 16, 60, 1, 0, 8))), Is.Null, "no MPEG sequence header");
    }

    [Test]
    public void PlaybackFollowsTheClockAndLoops()
    {
        var es = MakeMpeg1(8, 8, new Picture(100, 90, 200), new Picture(50, 128, 128));
        using (var once = new VideoPlayback(UsmVideo.Open(MakeUsm(es, VideoHeader(16, 16, 10, 1, 2, 8))), loop: false))
        {
            Assert.That(once.WaitFirstFrame(5000), Is.True);
            AssertFlat(once.Update(0), 100, 90, 200);
            Assert.That(once.Update(0.05), Is.Null);
            var deadline = Environment.TickCount64 + 5000;
            YuvFrame second = null;
            while (second == null && Environment.TickCount64 < deadline) { second = once.Update(0.15); Thread.Sleep(1); }
            AssertFlat(second, 50, 128, 128);
            while (!once.Finished && Environment.TickCount64 < deadline) { once.Update(10); Thread.Sleep(1); }
            Assert.That(once.Finished, Is.True);
        }
        using var loop = new VideoPlayback(UsmVideo.Open(MakeUsm(es, VideoHeader(16, 16, 10, 1, 2, 8))), loop: true);
        Assert.That(loop.WaitFirstFrame(5000), Is.True);
        var seen = new List<int>();
        var end = Environment.TickCount64 + 5000;
        for (int k = 0; k < 7 && Environment.TickCount64 < end;)
        {
            var f = loop.Update(k * 0.1 + 1e-6);
            if (f != null) { seen.Add(f.Y[0]); k++; }
            else Thread.Sleep(1);
        }
        Assert.That(seen, Is.EqualTo(new[] { 100, 50, 100, 50, 100, 50, 100 }));
        Assert.That(loop.Loops, Is.GreaterThanOrEqualTo(3));
        Assert.That(loop.Finished, Is.False);
    }

    // ---- client data (skipped without MS2_DATA) ----

    static PackSet Packs()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA is not set");
        if (!File.Exists(Path.Combine(data, "Resource/Movie/Common.m2d"))) Assert.Ignore("no Resource/Movie/Common.m2d under MS2_DATA");
        return new PackSet(data);
    }

    static UsmVideo OpenMovie(PackSet packs, string file)
    {
        var entry = packs.FindByFileName("Resource/Movie/Common", file);
        if (entry == null) Assert.Ignore($"{file} is not in Resource/Movie/Common");
        var bytes = packs.Read("Resource/Movie/Common", entry);
        Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("CRID"), "XOR-stored entry read");
        return UsmVideo.Open(bytes);
    }

    /// <summary>Mean absolute difference of horizontally neighbouring luma pixels: a misdecoded picture is noise.</summary>
    static double Roughness(YuvFrame f)
    {
        long sum = 0;
        for (int y = 0; y < f.Height; y++)
            for (int x = 1; x < f.Width; x++) sum += Math.Abs(f.Y[y * f.LumaStride + x] - f.Y[y * f.LumaStride + x - 1]);
        return sum / (double)(f.Height * (f.Width - 1));
    }

    [Test]
    public void JobPreviewDecodes()
    {
        using var packs = Packs();
        var v = OpenMovie(packs, "jobpreview_10.usm");
        Assert.That(v, Is.Not.Null);
        Assert.That((v.Width, v.Height), Is.EqualTo((384, 240)));
        Assert.That(v.FrameRate, Is.EqualTo(60));
        Assert.That(v.FrameCount, Is.GreaterThan(600));
        Assert.That(v.HasAudio, Is.False);
        Assert.That(v.Decoder.IntraDcPrecision, Is.EqualTo(11));
        double rough = 0;
        for (int i = 0; i < 300; i++)
        {
            var f = v.Next();
            Assert.That(f, Is.Not.Null, $"frame {i}");
            Assert.That((f.Width, f.Height), Is.EqualTo((384, 240)));
            Assert.That(f.Time, Is.EqualTo(i / 60.0).Within(1e-9));
            if (i >= 240) rough = Math.Max(rough, Roughness(f));
        }
        Assert.That(rough, Is.LessThan(20), "picture, not noise");
    }

    /// <summary>The cut scene movies the new characters' first fields play (trigger PlaySceneMovie, the folder and case as the
    /// scripts write them; UICutSceneMovie::PlayUsm opens them by the path under the movie folder): each opens and decodes
    /// its first second.</summary>
    [TestCase(@"common\Common_Opening.usm")]
    [TestCase(@"common\JobIntro_Assassin.usm")]
    public void TutorialCutSceneMovieDecodes(string path)
    {
        using var packs = Packs();
        var v = OpenMovie(packs, path);
        Assert.That(v, Is.Not.Null);
        Assert.That(v.Width, Is.GreaterThan(0));
        Assert.That(v.FrameCount, Is.GreaterThan(v.FrameRate), "longer than a second");
        for (int i = 0; i < v.FrameRate; i++)
        {
            var f = v.Next();
            Assert.That(f, Is.Not.Null, $"frame {i}");
            Assert.That((f.Width, f.Height), Is.EqualTo((v.Width, v.Height)));
        }
    }

    [Test]
    public void WholeMovieHasTheHeaderFrameCount()
    {
        using var packs = Packs();
        var v = OpenMovie(packs, "customize_hair.usm");
        Assert.That(v, Is.Not.Null);
        int n = 0;
        while (v.Next() != null) n++;
        Assert.That(n, Is.EqualTo(v.FrameCount));
        Assert.That(v.Decoder.HasEnded, Is.True);
    }
}
