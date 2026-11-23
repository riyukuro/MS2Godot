using System.Numerics;

namespace Ms2.Formats.World;

/// <summary>The shown camera of <see cref="TargetCameraZoom"/>: it eases to the placed camera and tells whether its near plane is
/// blocked (CTargetCameraMode::vfunc_39 @0x14183da00 with vfunc_38 @0x14183cec0).</summary>
public interface ITargetCameraView
{
    /// <summary>One pass's shown camera: ease it toward the placed eye looking at <paramref name="look"/> by
    /// <paramref name="dt"/> seconds; <paramref name="offset"/> is the placed eye's offset from the look point. Returns whether
    /// its near plane is blocked.</summary>
    bool Show(Vector3 eye, Vector3 look, Vector3 offset, float dt);
}

/// <summary>
/// The follow camera's distance (CTargetCameraMode, targetcameramode.cpp), in Godot axes (y up): the passes of vfunc_8
/// (@0x14183a560, :186-306), the zoom coast, the wheel impulse and FUN_14183bfd0 (@0x14183bfd0, :498-531). With
/// t = (distance - nearest) / (farthest - nearest), the angle from the up axis goes from 90 (level) at the nearest zoom to
/// vangle at the farthest, and the target height from zoomInHeightOffset to zoomOutHeightOffset. Eye = target +
/// (d sin yaw, d cos yaw, d cos pitch) in MS2 axes, yaw = hangle + 180.
/// </summary>
public sealed class TargetCameraZoom
{
    /// <summary>CCameraMode's default nearest zoom (m_58).</summary>
    public const float Nearest = 400f;
    /// <summary>m_b4: the player's height, added to the farthest zoom.</summary>
    public const float PlayerHeight = 150f;
    /// <summary>vfunc_8's pass cap (nMaxLoop).</summary>
    public const int MaxPasses = 100;
    /// <summary>The client's frame rate, on which the coast counts its frames.</summary>
    public const float FrameRate = 60f;

    /// <summary>The field's camera (m_54 = Distance, the farthest zoom).</summary>
    public FieldCamera Setting = new();
    /// <summary>m_50: the eye's distance from the target in the ground plane, Nearest .. farthest + PlayerHeight.</summary>
    public float Distance;
    /// <summary>m_84: the zoom velocity, + toward the target.</summary>
    public float Velocity;
    /// <summary>m_88: the eye moves by the velocity (and keeps its place between frames) instead of following the target.</summary>
    public bool VelocityActive;
    /// <summary>m_f4: the shown camera's near plane was blocked on the last pass.</summary>
    public bool Blocked;
    /// <summary>Ctrl + up/down set the angle from the up axis in radians (0.15..1.57); a distance change goes back to the distance's own.</summary>
    public float? Pitch;
    /// <summary>m_6c: the angle from the up axis in radians, as last placed.</summary>
    public float PlacedPitch = MathF.PI / 2;
    /// <summary>m_d8's position: the placed eye.</summary>
    public Vector3? PlacedEye;
    /// <summary>m_78: the look point of the last pass.</summary>
    public Vector3? Look;

    // 60 fps frames left in the zoom coast: the client moves while |m_84| >= 5 at the frame's start, so from v that is
    // floor(ln(5 / |v|) / ln 0.9) + 1 frames
    float coastLeft;
    float coastDist, coastPhase;   // the coast's distance at its last 1/60 s grid step, and the frames since it

    float Farthest => Setting.Distance + PlayerHeight;

    /// <summary>Puts the zoom at the field's farthest distance with no motion (the field load).</summary>
    public void Reset()
    {
        Distance = coastDist = Setting.Distance;
        Velocity = 0;
        coastLeft = 0;
        coastPhase = 0;
    }

    /// <summary>Scripted runs: the zoom distance (clamped to Nearest .. farthest + PlayerHeight) and the angle in radians
    /// (null: the distance's own). Any zoom coast stops.</summary>
    public void SetView(float dist, float? pitch)
    {
        Velocity = 0;
        coastLeft = 0;
        coastPhase = 0;
        Distance = coastDist = Math.Clamp(dist, Nearest, Farthest);
        Pitch = pitch is float p ? Math.Clamp(p, 0.15f, 1.57f) : null;
    }

    /// <summary>The angle from the up axis in radians the distance gives (FUN_14183ce10 @0x14183ce10, with 90 at the nearest).</summary>
    public float PitchOf(float dist)
    {
        float range = Setting.Distance - Nearest;
        float t = range > 0 ? MathF.Min((dist - Nearest) / range, 1f) : 1f;
        return (90f + (Setting.VAngleDeg - 90f) * t) * (MathF.PI / 180f);
    }

    /// <summary>The eye and look point for distance <paramref name="dist"/> about <paramref name="target"/> (the player's position).</summary>
    public void Place(Vector3 target, float dist, float yaw, out Vector3 eye, out Vector3 look, out Vector3 offset)
    {
        float range = Setting.Distance - Nearest;
        float pitch = Pitch ?? PitchOf(dist);
        PlacedPitch = pitch;
        // vfunc_27: the height follows the distance past the farthest zoom too (only the angle is capped)
        float th = range > 0 ? (dist - Nearest) / range : 1f;
        float height = Setting.ZoomInHeightOffset + (Setting.ZoomOutHeightOffset - Setting.ZoomInHeightOffset) * th;
        // MS2 (d sin yaw, d cos yaw, d cos pitch) in Godot axes (x, z, -y)
        offset = new Vector3((float)(dist * Math.Sin(yaw)), (float)(dist * Math.Cos(pitch)), (float)-(dist * Math.Cos(yaw)));
        look = target + new Vector3(0, height, 0);
        eye = look + offset;
    }

    /// <summary>
    /// One frame of vfunc_8: one pass, plus another while the target outruns the eye's step or while the shown view is
    /// blocked, at most <see cref="MaxPasses"/>; every pass shows the camera again with the frame's time. <paramref name="zoom"/>
    /// is the frame's wheel notch (+1 in, -1 out, 0 none). Returns the number of passes.
    /// </summary>
    public int Update(Vector3 target, float yaw, float dt, int zoom, ITargetCameraView view)
    {
        Vector3? prevLook = Look, prevEye = PlacedEye;
        int passes = 1;
        for (int i = 0; i < passes && i < MaxPasses; i++)
        {
            Place(target, Distance, yaw, out var eye, out var look, out var offset);
            if (!VelocityActive || PlacedEye == null) PlacedEye = eye;
            if (MathF.Abs(Velocity) >= 5f)
            {
                VelocityActive = true;
                // the client's pass (targetcameramode.cpp:219-228) decays m_84 by 0.9 and then moves the eye by m_84 along
                // the eye-to-target line, once a frame at its 60 fps, until |m_84| < 5. The pitch follows the distance, so
                // the path depends on the step size: the coast runs on its own 1/60 s grid with the client's step
                // (CoastStep), and a drawn frame shows the distance between two grid steps at its phase. The path at the
                // grid times and the settled distance are the 60 fps ones at any frame rate.
                float frames = dt > 0 ? dt * FrameRate : 1f;
                var start = PlacedEye.Value;
                // ptPos (:224-228): the pass's eye moved along the line to the target before FUN_14183bfd0 clamps and
                // places it; the target outruns the eye when it moved farther than that ptPos since the frame began (:229-233)
                float moved;
                if (Blocked)
                {
                    // the blocked view's -10 (no decay) moves by m_84 each 60 fps frame
                    moved = Velocity * frames;
                    var pos = start + Unit(look - start) * moved;
                    float maxDist = Farthest;
                    bool outside = Distance < Nearest || Distance > maxDist;
                    SetDistance(pos, look, !(outside && ((Velocity > 0 && Distance > maxDist) || (Velocity < 0 && Distance < Nearest))));
                    coastDist = Distance;
                    coastPhase = 0;
                }
                else
                {
                    moved = 0;
                    coastPhase += frames;
                    while (coastPhase >= 1f - 1e-4f && coastLeft > 0)
                    {
                        moved += Velocity * 0.9f;
                        coastDist = CoastStep(target, coastDist, yaw, ref Velocity);
                        Pitch = null;
                        coastPhase -= 1f;
                        if (--coastLeft <= 0 || Velocity == 0) { coastLeft = 0; break; }
                    }
                    if (coastLeft <= 0) { Distance = coastDist; Velocity = 0; coastPhase = 0; }   // |m_84| is under 5 now
                    else
                    {
                        float v = Velocity, ph = Math.Clamp(coastPhase, 0f, 1f);
                        float next = CoastStep(target, coastDist, yaw, ref v);
                        Distance = coastDist + (next - coastDist) * ph;
                        moved += Velocity * 0.9f * ph;
                    }
                }
                var stepped = start + Unit(look - start) * moved;
                if (Planar(prevLook ?? look, look) > Planar(prevEye ?? stepped, stepped)) passes++;
                Place(target, Distance, yaw, out eye, out look, out offset);
                PlacedEye = eye;
            }
            else if (VelocityActive)
            {
                Velocity = 0;
                VelocityActive = false;
                PlacedEye = eye;                               // vfunc_37
            }
            // the zoom action (one frame per wheel step): m_84 += x * min(dt, 1/15) * 2000, within +-150. The impulse is
            // the client's at a 1/60 s frame, per notch, so a notch zooms as far at any frame rate (the coast above
            // counts 60 fps frames)
            if (i == 0 && zoom != 0)
            {
                Velocity = Math.Clamp(Velocity + zoom * (1f / FrameRate) * 2000f, -150f, 150f);
                coastLeft = CoastFrames(Velocity);
                coastDist = Distance;
                coastPhase = 0;
            }
            // vfunc_39: the near plane of the SHOWN camera; while blocked the next pass pushes the eye out by 10
            Blocked = view.Show(PlacedEye.Value, look, offset, dt);
            if (Blocked) { Velocity = -10f; coastLeft = CoastFrames(Velocity); coastDist = Distance; coastPhase = 0; passes++; }
            Look = look;
        }
        return passes;
    }

    /// <summary>
    /// One client coast frame from distance d (targetcameramode.cpp:219-247): m_84 *= 0.9, the eye moves by m_84 toward
    /// the target, and the distance is clamped and snapped as <see cref="SetDistance"/> does.
    /// </summary>
    float CoastStep(Vector3 target, float d, float yaw, ref float v)
    {
        v *= 0.9f;
        Place(target, d, yaw, out var eye, out var look, out _);
        var pos = eye + Unit(look - eye) * v;
        float maxDist = Farthest, nd = Planar(look, pos);
        bool outside = d < Nearest || d > maxDist;
        if (!(outside && ((v > 0 && d > maxDist) || (v < 0 && d < Nearest)))) nd = Math.Clamp(nd, Nearest, maxDist);
        if ((v < 0 && nd > Setting.Distance && d < Setting.Distance) || (v > 0 && d > Setting.Distance && nd < Setting.Distance)) { nd = Setting.Distance; v = 0; }
        return nd;
    }

    static float CoastFrames(float v) => MathF.Abs(v) < 5f ? 0f : MathF.Floor(MathF.Log(5f / MathF.Abs(v)) / MathF.Log(0.9f) + 1e-4f) + 1f;

    /// <summary>FUN_14183bfd0: the distance becomes the eye's ground-plane distance from the target, within nearest ..
    /// farthest + player height when clamped. A step that crosses the farthest zoom (m_54) stops on it and ends the motion;
    /// the angle and height then follow the distance.</summary>
    void SetDistance(Vector3 pos, Vector3 look, bool clamp)
    {
        float prev = Distance;
        Distance = Planar(look, pos);
        if (clamp) Distance = Math.Clamp(Distance, Nearest, Farthest);
        if ((Velocity < 0 && Distance > Setting.Distance && prev < Setting.Distance) || (Velocity > 0 && prev > Setting.Distance && Distance < Setting.Distance))
        {
            Distance = Setting.Distance;
            Velocity = 0;
        }
        Pitch = null;
    }

    /// <summary>GetPlanarDistance: the distance in the ground plane (Godot x and z).</summary>
    public static float Planar(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    static Vector3 Unit(Vector3 v)
    {
        float l = v.Length();
        return l == 0 ? Vector3.Zero : v / l;
    }
}
