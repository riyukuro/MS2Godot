using Ms2.Formats.Pack;
using Ms2.Swf;
using NUnit.Framework;

namespace Ms2.Swf.Tests;

public class SyntheticTests
{
    // minimal uncompressed movie: 100x50 stage, one sprite (id 1) holding a DefineShape-less placement, root places it named "box"
    static byte[] Tag(int code, byte[] body)
    {
        var l = new List<byte>();
        if (body.Length < 63) l.AddRange(BitConverter.GetBytes((ushort)(code << 6 | body.Length)));
        else { l.AddRange(BitConverter.GetBytes((ushort)(code << 6 | 63))); l.AddRange(BitConverter.GetBytes(body.Length)); }
        l.AddRange(body);
        return l.ToArray();
    }

    // RECT 0..2000 x 0..1000 twips, nbits=12: 5 + 4*12 = 53 bits -> 7 bytes
    static byte[] Rect(int w, int h)
    {
        var bits = new List<bool>();
        void Put(int v, int n) { for (int i = n - 1; i >= 0; i--) bits.Add((v >> i & 1) != 0); }
        Put(13, 5); Put(0, 13); Put(w, 13); Put(0, 13); Put(h, 13);
        while (bits.Count % 8 != 0) bits.Add(false);
        var r = new byte[bits.Count / 8];
        for (int i = 0; i < bits.Count; i++) if (bits[i]) r[i / 8] |= (byte)(0x80 >> i % 8);
        return r;
    }

    [Test]
    public void ParsesStageNamedPlacementAndLabel()
    {
        var body = new List<byte>();
        body.AddRange(Rect(2000, 1000));
        body.AddRange(new byte[] { 0, 10, 1, 0 });                       // 10 fps, 1 frame
        // DefineEditText id 5: bounds 0,0,40x20 px, flags HasText|HasTextColor|HasFont, font 3 height 12px, colour, text "x"
        var et = new List<byte>(); et.AddRange(BitConverter.GetBytes((ushort)5)); et.AddRange(Rect(800, 400));
        et.Add(0x80 | 0x04 | 0x01); et.Add(0); et.AddRange(BitConverter.GetBytes((ushort)3)); et.AddRange(BitConverter.GetBytes((ushort)240));
        et.AddRange(new byte[] { 255, 0, 0, 255 }); et.Add(0); et.Add((byte)'x'); et.Add(0);
        body.AddRange(Tag(37, et.ToArray()));
        // PlaceObject2 depth 1 char 5 name "t" at translate (20,10) px: matrix = no scale, no rotate, nbits 9, tx=400, ty=200
        var pl = new List<byte> { 2 | 4 | 32 }; pl.AddRange(BitConverter.GetBytes((ushort)1)); pl.AddRange(BitConverter.GetBytes((ushort)5));
        // matrix bits: 0,0, 01001 (9), 0110010000 -> needs 10 bits for 400; use nbits 11
        var bits = new List<bool>(); void Put(int v, int n) { for (int i = n - 1; i >= 0; i--) bits.Add((v >> i & 1) != 0); }
        Put(0, 1); Put(0, 1); Put(11, 5); Put(400, 11); Put(200, 11); while (bits.Count % 8 != 0) bits.Add(false);
        var mb = new byte[bits.Count / 8]; for (int i = 0; i < bits.Count; i++) if (bits[i]) mb[i / 8] |= (byte)(0x80 >> i % 8);
        pl.AddRange(mb); pl.AddRange(new byte[] { (byte)'t', 0 });
        body.AddRange(Tag(26, pl.ToArray()));
        body.AddRange(Tag(43, new byte[] { (byte)'a', (byte)'b', 0 }));
        body.AddRange(Tag(1, Array.Empty<byte>()));
        body.AddRange(Tag(0, Array.Empty<byte>()));
        var file = new List<byte> { (byte)'F', (byte)'W', (byte)'S', 9 };
        file.AddRange(BitConverter.GetBytes(8 + body.Count)); file.AddRange(body);

        var m = SwfMovie.Parse(file.ToArray());
        Assert.That(m.Stage.W, Is.EqualTo(100)); Assert.That(m.Stage.H, Is.EqualTo(50));
        Assert.That(m.FrameRate, Is.EqualTo(10));
        var c = m.Characters[5];
        Assert.That(c.Text.Size, Is.EqualTo(12)); Assert.That(c.Text.Color.Hex, Is.EqualTo("#ff0000"));
        Assert.That(c.Bounds.W, Is.EqualTo(40));
        var p = m.Root.Frames[0].Single().Place;
        Assert.That(p.Name, Is.EqualTo("t")); Assert.That(p.Matrix.Tx, Is.EqualTo(20)); Assert.That(p.Matrix.Ty, Is.EqualTo(10));
        Assert.That(m.Root.Labels.Single().Name, Is.EqualTo("ab"));
        Assert.That(m.Problems, Is.Empty);

        var json = LayoutJson.Build(m, "synthetic");
        Assert.That((string)json["namedInstances"][0]["path"], Is.EqualTo("t"));
        Assert.That((double)json["namedInstances"][0]["bounds"]["x"], Is.EqualTo(20));
    }

    [Test]
    public void MatrixComposeAndTransform()
    {
        var parent = new Matrix(2, 0, 0, 2, 10, 10);
        var child = new Matrix(1, 0, 0, 1, 5, 5);
        var abs = parent.Compose(child);
        Assert.That(abs.Tx, Is.EqualTo(20)); Assert.That(abs.Ty, Is.EqualTo(20));
        var r = abs.Transform(new Rect(0, 0, 10, 10));
        Assert.That(r.W, Is.EqualTo(20));
    }
}

/// <summary>Runs on the player's own Gfx.m2d; skipped when MS2_DATA is not set.</summary>
public class GameDataTests
{
    static PackSet Packs()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return new PackSet(d);
    }

    static SwfMovie Load(string name) => SwfMovie.Parse(Packs().Read("Resource/Gfx", name + ".gfx"));

    [TestCase("uilogindialog")]
    [TestCase("uicharacterlistdialog")]
    [TestCase("uiupperhuddialog")]
    [TestCase("uiscenelogin")]
    public void FirstPlayableMoviesParseWithoutFailures(string name)
    {
        var m = Load(name);
        Assert.That(m.Problems, Is.Empty);
        Assert.That(m.Stats.Values.Sum(s => s.Failed), Is.Zero);
        Assert.That(m.Stage.W, Is.GreaterThan(0));
        var json = LayoutJson.Build(m, name);
        Assert.That(((System.Text.Json.Nodes.JsonObject)json["symbols"]).Count, Is.GreaterThan(0));
    }

    [Test]
    public void LoginDialogHasNamedInputsAndAtlas()
    {
        var m = Load("uilogindialog");
        var json = LayoutJson.Build(m, "uilogindialog");
        var paths = ((System.Text.Json.Nodes.JsonArray)json["namedInstances"]).Select(n => (string)n["path"]).ToList();
        Assert.That(paths, Does.Contain("m_dialog.m_inputID"));
        Assert.That(m.Characters.Values.Any(c => c.Kind == CharKind.Image && c.ImageFile != null));
        Assert.That(m.Characters.Values.Where(c => c.Kind == CharKind.SubImage).All(c => c.AtlasRect.W > 0));
    }

    [Test]
    public void EveryMovieInGfxPackParses()
    {
        var p = Packs();
        int n = 0;
        foreach (var e in p.List("Resource/Gfx").Where(e => e.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase)))
        {
            SwfMovie m;
            try { m = SwfMovie.Parse(p.Read("Resource/Gfx", e)); }
            catch (FormatException) { continue; }   // a pack-reader limitation (fonts_kr.gfx), not a parse error
            Assert.That(m.Problems, Is.Empty, e);
            n++;
        }
        Assert.That(n, Is.GreaterThan(400));
    }
}
