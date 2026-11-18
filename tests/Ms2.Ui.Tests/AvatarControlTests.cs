using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIAvatarBase's drag turn, UIModelView's zoom and CLimitCharRotate (no data needed).</summary>
public class AvatarControlTests
{
    static readonly AvatarRotateLimit None = new(0, 360);

    [Test]
    public void RightDragTurnsFromTheStartAngle()
    {
        var a = new AvatarControl(None, 0, 0, 90);
        int turned = 0;
        a.Turned += () => turned++;
        Assert.That(a.OnCallback("OnAvatarMouseDown", new object[] { 1 }), Is.True);
        Assert.That(a.Mode, Is.EqualTo(1));
        a.OnCallback("OnAvatarMouseMove", new object[] { 2, -2, 1 });        // within 2 pixels: nothing
        Assert.That(turned, Is.EqualTo(0));
        a.OnCallback("OnAvatarMouseMove", new object[] { 100, 0, 1 });
        Assert.That(a.Angle, Is.EqualTo((0f, 0f, 55f)));
        a.OnCallback("OnAvatarMouseMove", new object[] { 300, 0, 1 });       // cumulative since the press: 90 - 105
        Assert.That(a.Angle.Z, Is.EqualTo(345f).Within(1e-3));
        a.OnCallback("OnAvatarMouseMove", new object[] { 0, 0, 0 });         // another button's move is ignored
        Assert.That(turned, Is.EqualTo(2));
        Assert.That(a.OnCallback("OnAvatarMouseUp", Array.Empty<object>()), Is.False);
        a.OnCallback("OnAvatarMouseDown", new object[] { 1 });               // the next drag starts where this one ended
        a.OnCallback("OnAvatarMouseMove", new object[] { -100, 0, 1 });
        Assert.That(a.Angle.Z, Is.EqualTo(20f).Within(1e-3));
    }

    [Test]
    public void LeftPressDoesNotTurn()
    {
        var a = new AvatarControl(None, 0, 0, 90);
        a.MouseDown(0);
        Assert.That(a.Mode, Is.EqualTo(0));
        a.MouseMove(100, 100, 0);
        Assert.That(a.Angle, Is.EqualTo((0f, 0f, 90f)));
    }

    [Test]
    public void VerticalDragTurnsYAndReversesZPastNinety()
    {
        var a = new AvatarControl(None, 0, 0, 90);
        a.MouseDown(1);
        a.MouseMove(0, -400, 1);                                             // y: 0 + 140
        Assert.That(a.Angle.Y, Is.EqualTo(140f).Within(1e-3));
        a.MouseDown(1);
        a.MouseMove(100, 0, 1);                                              // y in (90, 270): z turns the other way
        Assert.That(a.Angle.Z, Is.EqualTo(125f).Within(1e-3));
    }

    [Test]
    public void WheelZooms()
    {
        var a = new AvatarControl(None, 0, 0, 90);
        var got = new List<bool>();
        a.Zoom += got.Add;
        a.OnCallback("OnAvatarMouseWheel", new object[] { 3 });
        a.OnCallback("OnAvatarMouseWheel", new object[] { -3 });
        Assert.That(got, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    public void ZoomMovesOneStepAndStopsAtTheEnds()
    {
        var z = new AvatarZoom(190, 300, 150, 700, 5);                       // range 150..300, step 30
        z.Start(true);
        z.Start(false);                                                      // ignored while one runs
        while (z.Update(1 / 60f)) { }
        Assert.That(z.Distance, Is.EqualTo(160f), "one step (30) from 190, ceil(d / 60) a frame: 4 then 3");
        z.Start(true);
        while (z.Update(1 / 60f)) { }
        Assert.That(z.Distance, Is.EqualTo(150f), "snapped to the closest end");
        z.Start(true);
        Assert.That(z.Update(1 / 60f), Is.False);
        for (int i = 0; i < 10; i++) { z.Start(false); while (z.Update(1 / 60f)) { } }
        Assert.That(z.Distance, Is.EqualTo(300f));
    }

    [Test]
    public void RotateLimit()
    {
        Assert.That(new AvatarRotateLimit(0, 360).Limited, Is.False);
        Assert.That(new AvatarRotateLimit(178, 8).Limited, Is.True);
        var l = new AvatarRotateLimit(300, 60);                              // wraps past 360
        Assert.That(l.Apply(10, 10, 0, 0), Is.EqualTo(10f));
        Assert.That(l.Apply(100, 100, 0, 5), Is.EqualTo(5f), "outside the range: the fallback");
        Assert.That(l.Apply(50, 50, 0, 0), Is.EqualTo(50f));
        Assert.That(l.Apply(-10, -10, 0, 0), Is.EqualTo(350f));
    }
}
