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
}
