using System.Numerics;
using Ms2.Formats.World;
using NUnit.Framework;
namespace Ms2.Net.Tests;

/// <summary>
/// The follow camera's distance passes (CTargetCameraMode::vfunc_8 @0x14183a560): at the farthest zoom plus the player's
/// height, a wheel-out notch while the player walks. The client adds a pass only while the target moved farther than the
/// pass's eye step before FUN_14183bfd0 clamps and places it (targetcameramode.cpp:224-233), so at the clamp the coast does
/// not run a pass per step in one frame (and the shown camera does not ease a whole coast's worth of passes at once).
/// </summary>
public class TargetCameraZoomTests
{
    /// <summary>The shown camera: it takes the placed eye, and its near plane is blocked while the eye is within
    /// <see cref="Radius"/> of <see cref="Obstacle"/> (a bush the eye passes through).</summary>
    sealed class View : ITargetCameraView
    {
        public Vector3? Obstacle;
        public float Radius = 80f;
        public int Shows;
        public bool Show(Vector3 eye, Vector3 look, Vector3 offset, float dt)
        {
            Shows++;
            return Obstacle is { } o && Vector3.Distance(eye, o) < Radius;
        }
    }

    const float Dt = 1f / 60f, Speed = 350f;   // a 60 fps frame, the player's run speed in units a second
    static readonly float Yaw = (45f + 180f) * MathF.PI / 180f;   // hangle 45 (the default field camera)

    static TargetCameraZoom AtFarthest(Vector3 start, View view)
    {
        var z = new TargetCameraZoom { Setting = new FieldCamera() };
        z.Reset();
        z.Update(start, Yaw, Dt, 0, view);
        for (int i = 0; i < 4; i++)                       // out to the farthest + the player's height
            for (int f = 0; f < 30; f++) z.Update(start, Yaw, Dt, f == 0 ? -1 : 0, view);
        Assert.That(z.Distance, Is.EqualTo(z.Setting.Distance + TargetCameraZoom.PlayerHeight).Within(0.01f));
        return z;
    }

    /// <summary>The ground direction from the target toward the camera (Godot x, z).</summary>
    static Vector3 TowardCamera => Vector3.Normalize(new Vector3(MathF.Sin(Yaw), 0, -MathF.Cos(Yaw)));

    [TestCase(0f)]     // toward the camera
    [TestCase(90f)]    // across the view
    [TestCase(270f)]
    public void WheelOutAtFarthestWhileWalkingTakesOnePassAFrame(float turnDeg)
    {
        var view = new View();
        var at = new Vector3(-4715.5f, 719.9f, 212.7f);
        var z = AtFarthest(at, view);
        var dir = Vector3.Transform(TowardCamera, Matrix4x4.CreateRotationY(turnDeg * MathF.PI / 180f));
        int most = 0;
        for (int f = 0; f < 240; f++)
        {
            at += dir * Speed * Dt;
            int passes = z.Update(at, Yaw, Dt, f % 15 == 0 ? -1 : 0, view);
            most = Math.Max(most, passes);
            Assert.That(z.Distance, Is.EqualTo(z.Setting.Distance + TargetCameraZoom.PlayerHeight).Within(0.01f), $"frame {f}");
        }
        Assert.That(most, Is.LessThanOrEqualTo(2), "passes in one frame");
    }

    [Test]
    public void BlockedAtFarthestWhileWalkingKeepsTheDistance()
    {
        // walking toward the camera at the farthest zoom with wheel-out notches, the placed eye passes through a bush: the
        // blocked view pushes the eye out (vfunc_39: m_84 = -10, :869-871), never in, and the clamp holds it at the farthest
        var view = new View();
        var at = new Vector3(-4715.5f, 719.9f, 212.7f);
        var z = AtFarthest(at, view);
        var eye0 = z.PlacedEye!.Value;
        view.Obstacle = eye0 + TowardCamera * (Speed * 1.0f);    // the eye reaches it after about a second
        float least = float.MaxValue;
        int blocked = 0;
        for (int f = 0; f < 150; f++)
        {
            at += TowardCamera * Speed * Dt;
            int passes = z.Update(at, Yaw, Dt, f % 6 == 0 ? -1 : 0, view);
            Assert.That(passes, Is.LessThanOrEqualTo(TargetCameraZoom.MaxPasses + 1));
            if (z.Blocked) blocked++;
            least = MathF.Min(least, z.Distance);
        }
        Assert.That(blocked, Is.GreaterThan(0), "the eye went through the obstacle");
        Assert.That(least, Is.EqualTo(z.Setting.Distance + TargetCameraZoom.PlayerHeight).Within(0.01f), "distance");
    }
}
