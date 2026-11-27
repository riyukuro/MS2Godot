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

    /// <summary>
    /// GitHub issue #14: vfunc_13 turns the avatar by FromEulerAnglesXYZ (Gamebryo's clockwise rotations) of angles that
    /// start at the copy's facing read back (270), and vfunc_14 takes 0.35 a pixel off z for a right drag. With the camera
    /// on Godot +x (the screen's right is Godot -z), the front (the client's -y, Godot +z) starts at the camera and turns
    /// toward the screen's right, with the mouse.
    /// </summary>
    [Test]
    public void RightDragTurnsTheFrontWithTheMouse()
    {
        var a = new AvatarControl(None, 0, 0, AvatarControl.PlayerStartZ);
        Assert.That(Yaw(a), Is.EqualTo(0).Within(1e-3), "facing the camera");
        a.MouseDown(1);
        a.MouseMove(100, 0, 1);                                              // z: 270 - 35
        Assert.That(a.Angle.Z, Is.EqualTo(235f).Within(1e-3));
        Assert.That(Yaw(a), Is.EqualTo(35).Within(1e-3), "turned 35 toward the screen's right");
        a.MouseMove(-100, 0, 1);                                             // z: 270 + 35
        Assert.That(Yaw(a), Is.EqualTo(-35).Within(1e-3), "turned 35 toward the screen's left");
    }

    [Test]
    public void RotationIsGamebryosClockwiseEulerXyz()
    {
        var a = new AvatarControl(None, 0, 0, 30);
        var m = a.Rotation();                                                // MakeZRotation(30): +sin at [0][1]
        Assert.That(m[1], Is.EqualTo(Math.Sin(30 * Math.PI / 180)).Within(1e-6));
        Assert.That(m[3], Is.EqualTo(-Math.Sin(30 * Math.PI / 180)).Within(1e-6));
    }

    /// <summary>The front (Godot +z through <see cref="AvatarControl.GodotRotation"/>) in degrees from the camera (+x)
    /// toward the screen's right (-z).</summary>
    static double Yaw(AvatarControl a)
    {
        var g = a.GodotRotation();
        return Math.Atan2(-g[8], g[2]) * 180 / Math.PI;
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
    public void StepsDoneStartsTheCameraThatFarOut()
    {
        // the skill preview's camera (uiskillpreviewdialog.cpp:84-87): eye 190, min 1000, max 150, 5 steps, 2 done
        var z = new AvatarZoom(190, 1000, 150, 700, 5, 2);                   // range 150..1000, step 170
        Assert.That(z.Distance, Is.EqualTo(530f), "190 + 2 x 170");
        z.Start(true);
        while (z.Update(1 / 60f)) { }
        Assert.That(z.Distance, Is.InRange(350f, 360f), "one step (170) in from 530, the last frame past it");
        Assert.That(new AvatarZoom(190, 1000, 150, 700, 5, 9).Distance, Is.EqualTo(1040f), "at most the steps: 190 + 5 x 170");
        Assert.That(new AvatarZoom(190, 1000, 150, 700, 5).Distance, Is.EqualTo(190f));
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
