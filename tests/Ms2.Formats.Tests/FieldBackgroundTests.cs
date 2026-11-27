using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

// Background.gfx's field path (class Background: onResizeScroll, setCameraDistance, updatePos) as FieldBackground ports it
public class FieldBackgroundTests
{
    [Test]
    public void ScrollPictureIsTwiceTheStageHighAt2320By2400()
    {
        Assert.That(FieldBackground.ResizeScroll(1280, 720), Is.EqualTo((1392.0, 1440.0)));
        Assert.That(FieldBackground.ResizeScroll(1920, 1080), Is.EqualTo((2088.0, 2160.0)));
        var (w, h) = FieldBackground.ResizeScroll(2560, 1080);       // wider than 2320: as wide as the stage
        Assert.That(w, Is.EqualTo(2560));
        Assert.That(h, Is.EqualTo(2560 * 2320.0 / 2400).Within(1e-9));
    }

    [Test]
    public void CameraAngleSlidesThePictureFromTopToBottom()
    {
        Assert.That(FieldBackground.CameraY(1440, 720, FieldBackground.FarAngle), Is.EqualTo(0).Within(1e-9));
        Assert.That(FieldBackground.CameraY(1440, 720, FieldBackground.NearAngle), Is.EqualTo(-720).Within(1e-9));
        Assert.That(FieldBackground.CameraY(1440, 720, (FieldBackground.NearAngle + FieldBackground.FarAngle) / 2), Is.EqualTo(-360).Within(1e-6));
        Assert.That(FieldBackground.CameraY(1440, 720, 2.0), Is.EqualTo(0));        // past pi/2: clamped to the top
        Assert.That(FieldBackground.CameraY(1440, 720, 0.5), Is.EqualTo(-720));     // below pi/3: clamped to the bottom
        Assert.That(FieldBackground.CameraY(700, 720, 0.5), Is.EqualTo(0));         // shorter than the stage
    }

    [Test]
    public void CameraAngleIsSafeAcosOfTheUpVectorX()
    {
        Assert.That(FieldBackground.CameraAngle(0.5), Is.EqualTo(Math.PI / 3).Within(1e-6));
        Assert.That(FieldBackground.CameraAngle(1.2), Is.EqualTo(0));
        Assert.That(FieldBackground.CameraAngle(-2), Is.EqualTo(3.1415927));
    }

    [Test]
    public void PlayerScreenXSlidesThePictureOver300()
    {
        Assert.That(FieldBackground.PosX(1392, 1280, 150), Is.EqualTo(-56).Within(1e-9));
        Assert.That(FieldBackground.PosX(1392, 1280, 600), Is.EqualTo(-112));
        Assert.That(FieldBackground.PosX(1392, 1280, -10), Is.EqualTo(0));
        Assert.That(FieldBackground.PosX(1200, 1280, 150), Is.EqualTo(0));
    }

    [Test]
    public void NoScrollPictureCoversTheStageCentred()
    {
        var (x, y, w, h) = FieldBackground.ResizeNoScroll(990, 1024, 1280, 720);
        Assert.That(w, Is.InRange(1279, 1280));
        Assert.That(h, Is.InRange(1322, 1325));
        Assert.That(x, Is.EqualTo((1280 - w) / 2));
        Assert.That(y, Is.EqualTo((720 - h) / 2));

        // a subsequent resize passes the bitmap's current width and height, as AS onResizeNoScroll reads a1.width / a1.height
        var (x2, y2, w2, h2) = FieldBackground.ResizeNoScroll(w, h, 1600, 900);
        Assert.That(w2, Is.EqualTo(1600));
        Assert.That(h2, Is.EqualTo((uint)(1600 * h / w)));
        Assert.That(x2, Is.EqualTo((1600 - w2) / 2));
        Assert.That(y2, Is.EqualTo((900 - h2) / 2));
    }

    [Test]
    public void FieldDataReadsTheScrollFlags()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new Ms2.Formats.Pack.PackSet(data);
        var login = FieldData.FindById(packs, 1);                     // <ui vscroll="0" hscroll="0">
        Assert.That((login.VScroll, login.HScroll), Is.EqualTo((0, 0)));
        var forest = FieldData.FindByXBlock(packs, "02000004_bf");    // no flags: CFieldDataCli's default 1
        Assert.That((forest.VScroll, forest.HScroll), Is.EqualTo((1, 1)));
    }

    /// <summary>GFxMovieBackground::GetBackgroundName (gfxmoviebackground.cpp:942): only a name holding ".png" or ".dds" (a
    /// case-sensitive Find) becomes a BG/ picture; anything else goes to loadBG unchanged.</summary>
    [Test]
    public void OnlyAPngOrDdsNameIsABgPicture()
    {
        Assert.That(FieldBackground.ImageEntry("BG_Cave_D.dds"), Is.EqualTo("bg/bg_cave_d.dds"));
        Assert.That(FieldBackground.ImageEntry("SW_BG_Iceage_C.png"), Is.EqualTo("bg/sw_bg_iceage_c.png"));
        Assert.That(FieldBackground.ImageEntry("BG_Cave_D.DDS"), Is.Null);
        Assert.That(FieldBackground.ImageEntry("Background"), Is.Null);
        Assert.That(FieldBackground.ImageEntry(""), Is.Null);
        Assert.That(FieldBackground.ImageEntry(null), Is.Null);
    }

    static string Attr(XElement e, string name) =>
        (string)e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? "";

    static bool IsDds(byte[] b) => b.Length >= 128 && b[0] == (byte)'D' && b[1] == (byte)'D' && b[2] == (byte)'S' && b[3] == (byte)' '
        && BitConverter.ToInt32(b, 4) == 124;

    /// <summary>
    /// Every ChangeBackground of the trigger xmls (the Maple2 server sends its dds name as is): the name is a BG picture by
    /// GetBackgroundName and Image.m2d holds it as a DDS. And every field's own bgDay names the same entry through
    /// GetBackgroundName as the old bg/ lookup found, so the field backdrop shows what it showed.
    /// </summary>
    static void EveryUse(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        int uses = 0, files = 0;
        var names = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            var bytes = packs.Read("Xml", entry);
            if (bytes.AsSpan().IndexOf("ChangeBackground"u8) < 0) continue;
            XDocument doc;
            try { doc = PackXml.Parse(bytes); } catch (System.Xml.XmlException) { continue; }
            var acts = doc.Descendants("action").Where(a => Attr(a, "name").Equals("ChangeBackground", StringComparison.OrdinalIgnoreCase)).ToList();
            if (acts.Count == 0) continue;
            files++;
            foreach (var a in acts)
            {
                string name = Attr(a, "dds");
                uses++;
                names[name] = names.GetValueOrDefault(name) + 1;
                string img = FieldBackground.ImageEntry(name);
                Assert.That(img, Is.Not.Null, $"{entry}: '{name}' is not a picture name");
                Assert.That(packs.Exists("Resource/Image", img), Is.True, $"{entry}: {img} not in Image.m2d");
                Assert.That(IsDds(packs.Read("Resource/Image", img)), Is.True, $"{entry}: {img} is not a DDS");
            }
        }
        int fields = 0, pictures = 0;
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("mapxblock/", StringComparison.OrdinalIgnoreCase)))
        {
            XDocument x;
            try { x = PackXml.Parse(packs.Read("Xml", entry)); } catch (System.Xml.XmlException) { continue; }
            var first = ((string)x.Root?.Element("clientProperty")?.Attribute("bgDay"))?.Split(',')[0].Trim();
            if (string.IsNullOrEmpty(first)) continue;
            fields++;
            string old = "bg/" + first.ToLowerInvariant();
            if (!packs.Exists("Resource/Image", old)) continue;
            pictures++;
            Assert.That(FieldBackground.ImageEntry(first), Is.EqualTo(old), $"{entry}: bgDay '{first}'");
        }
        TestContext.Progress.WriteLine($"{variable}: {uses} ChangeBackground uses in {files} files ({string.Join(", ", names.Select(kv => $"{kv.Key} {kv.Value}"))}); "
            + $"{fields} fields with a bgDay, {pictures} of them on an Image.m2d picture");
        Assert.That(uses, Is.GreaterThan(0));
        Assert.That(pictures, Is.GreaterThan(0));
    }

    [Test] public void EveryKmsUse() => EveryUse("MS2_DATA");
    [Test] public void EveryGms2Use() => EveryUse("MS2_GMS2_DATA");
}
