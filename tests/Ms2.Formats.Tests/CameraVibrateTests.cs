using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The vibrate part's camera shake (<see cref="CameraVibrate"/>, CVibrateCameraEffect::vfunc_1) and the camera manager's
/// effect queue (<see cref="CameraEffects"/>), worked by hand from the client's formulas.</summary>
public class CameraVibrateTests
{
    /// <summary>The client's draws in order: each sign from <see cref="Signs"/> (GetRandomRange(2, 0)), each rand01 from
    /// <see cref="Units"/> (FUN_140768090).</summary>
    sealed class Script : Random
    {
        public int[] Signs = { 1 };
        public float[] Units = { 0.5f };
        public int SignDraws, UnitDraws;
        public override int Next(int max) { Assert.That(max, Is.EqualTo(2)); return Signs[SignDraws++ % Signs.Length]; }
        public override float NextSingle() => Units[UnitDraws++ % Units.Length];
    }

    static List<EffectPart> Parts(string xml) => EffectTable.Parse(XDocument.Parse(xml));

    [Test]
    public void TheWaveFollowsTheSineAndTheFadeFromTheDampingTime()
    {
        // start 10, end 11, 2 Hz over the run, amp 8, min scale 0.25, fading from 10.5
        var v = new CameraVibrate(10, 11, 2, 8, 0.25f, 10.5, true);
        var r = new Script { Signs = new[] { 1, 1, 1 }, Units = new[] { 1f, 0f, 0f } };   // x 1, z 0, y 0: along +x
        var eye = new Vector3(100, 200, 300);
        Assert.That(v.Update(9.5, ref eye, r), Is.True, "before the start it runs");
        Assert.That(eye, Is.EqualTo(new Vector3(100, 200, 300)), "and leaves the eye");
        Assert.That(r.SignDraws, Is.Zero);

        // t 10.125: phase 1/8, sin(2 pi * 2 / 8) = 1, no fade: wave 8
        eye = new Vector3(100, 200, 300);
        Assert.That(v.Update(10.125, ref eye, r), Is.True);
        Assert.That(eye.X, Is.EqualTo(108f).Within(1e-4f));
        Assert.That((eye.Y, eye.Z), Is.EqualTo((200f, 300f)));
        Assert.That((r.SignDraws, r.UnitDraws), Is.EqualTo((3, 3)), "the first positive wave picks a direction (the sign flag starts negative)");

        // t 10.875: phase 7/8, sin(2 pi * 14 / 8) = -1; fade 1 - 0.375 / 0.5 = 0.25; scale (0.25 * 0.75 + 0.25) * 8 = 3.5
        r.Units = new[] { 0f, 1f, 0f };   // the flip picks again: x 0, z 1: along +z
        eye = Vector3.Zero;
        Assert.That(v.Update(10.875, ref eye, r), Is.True);
        Assert.That(eye.Z, Is.EqualTo(-3.5f).Within(1e-4f));
        Assert.That(eye.X, Is.Zero);
        Assert.That(v.Direction, Is.EqualTo(Vector3.UnitZ));

        Assert.That(v.Update(11, ref eye, r), Is.False, "the end is past it");
    }

    [Test]
    public void TheDirectionIsPickedAgainOnlyWhenTheWaveFlips()
    {
        var v = new CameraVibrate(0, 1, 1, 10, 1f, 0, true);   // min scale 1: no fade; one period
        var r = new Script { Signs = new[] { 0, 1, 1 }, Units = new[] { 0.6f, 0.8f, 0f } };
        var eye = Vector3.Zero;
        v.Update(0.1, ref eye, r);
        // x = 0.6 * -1, z = 0.8 * 1, y = 0: unit already
        Assert.That(Vector3.Distance(v.Direction, new Vector3(-0.6f, 0, 0.8f)), Is.LessThan(1e-6f));
        int draws = r.UnitDraws;
        v.Update(0.2, ref eye, r);
        v.Update(0.4, ref eye, r);
        Assert.That(r.UnitDraws, Is.EqualTo(draws), "the same sign keeps the direction");
        v.Update(0.6, ref eye, r);
        Assert.That(r.UnitDraws, Is.EqualTo(draws + 3), "the negative half picks again");
        v.Update(0.9, ref eye, r);
        Assert.That(r.UnitDraws, Is.EqualTo(draws + 3));

        // a draw of all zero unitizes to nothing: the eye is not moved
        var z = new CameraVibrate(0, 1, 1, 10, 1f, 0, true);
        var none = new Script { Units = new[] { 0f } };
        var e = new Vector3(1, 2, 3);
        Assert.That(z.Update(0.25, ref e, none), Is.True);
        Assert.That(e, Is.EqualTo(new Vector3(1, 2, 3)));
    }

    [Test]
    public void APartsShakeStartsAfterItsDelayAndFadesFromItsDampingTime()
    {
        var parts = Parts("""
            <ms2><effect type="vibrate" startDelay="0.5" duration="0.4" freq="15" amp="10" damping="0.5" dampingTime="0.1"/>
            <effect type="vibrate" duration="0.3" freq="20" amp="7"/></ms2>
            """);
        var p = parts[0];
        Assert.That((p.VibrateFrequency, p.VibrateAmplitude, p.VibrateDamping, p.VibrateDampingTime), Is.EqualTo((15f, 10f, 0.5f, 0.1f)));
        Assert.That((parts[1].VibrateDamping, parts[1].VibrateDampingTime), Is.EqualTo((1f, 0f)), "damping 1 and dampingTime 0 when absent");
        var v = CameraVibrate.For(p, 100);
        Assert.That(v.Start, Is.EqualTo(100.5).Within(1e-6), "the request's time plus the start delay");
        Assert.That(v.End, Is.EqualTo(100.9).Within(1e-6), "plus the duration");
        Assert.That(v.FadeStart, Is.EqualTo(100.6).Within(1e-6), "the start plus the damping time");
        Assert.That((v.Frequency, v.Amplitude, v.MinScale, v.Vertical), Is.EqualTo((15f, 10f, 0.5f, true)));
    }

    [Test]
    public void AVibrateWhoseDurationIsNotPastItsDampingTimeIsNotMade()
    {
        var parts = Parts("""
            <ms2><effect type="vibrate" duration="0.5" dampingTime="0.5" freq="10" amp="5"/>
            <effect type="vibrate" duration="0.5" dampingTime="1.2" freq="10" amp="5"/>
            <effect type="vibrate" duration="0.5" dampingTime="0.4" freq="10" amp="5"/>
            <effect type="vibrate" loop="true" freq="10" amp="5"/></ms2>
            """);
        Assert.That(EffectRandom.ParamMade(parts, parts[0]), Is.False, "equal (the param is made only when duration > dampingTime)");
        Assert.That(EffectRandom.ParamMade(parts, parts[1]), Is.False, "past it");
        Assert.That(EffectRandom.ParamMade(parts, parts[2]), Is.True);
        Assert.That(EffectRandom.ParamMade(parts, parts[3]), Is.False, "a loop of duration 0 passes the base test, not this one");
    }

    [Test]
    public void OnlyTheFirstQueuedShakeRunsAndIsDroppedAtItsEnd()
    {
        var q = new CameraEffects(new Script { Units = new[] { 1f, 0f, 0f } });
        var a = new CameraVibrate(0, 1, 1, 4, 1f, 0, true);
        var b = new CameraVibrate(0, 3, 1, 4, 1f, 0, true);
        q.Add(a, vibrateOption: true);
        q.Add(b, vibrateOption: true);
        var eye = Vector3.Zero;
        Assert.That(q.Update(0.25, ref eye), Is.True, "the first moves the eye");
        Assert.That(eye.X, Is.EqualTo(4f).Within(1e-4f));
        eye = Vector3.Zero;
        Assert.That(q.Update(1.5, ref eye), Is.False, "the first ended: dropped, no move");
        Assert.That(eye, Is.EqualTo(Vector3.Zero));
        Assert.That(q.Count, Is.EqualTo(1));
        Assert.That(q[0], Is.SameAs(b));
        // b started at 0: at 1.75 it is at phase 7/12 of its one period
        Assert.That(q.Update(1.75, ref eye), Is.True);
        Assert.That(eye.Length(), Is.EqualTo(MathF.Abs(MathF.Sin(1.75f / 3 * 2 * 3.1415927f) * 4)).Within(1e-4f));
        q.Update(3, ref eye);
        Assert.That(q.Count, Is.Zero);
        Assert.That(q.Update(4, ref eye), Is.False);

        var before = new CameraEffects();
        before.Add(new CameraVibrate(5, 6, 1, 4, 1f, 5, true), true);
        eye = Vector3.One;
        Assert.That(before.Update(4, ref eye), Is.False, "not started: it runs but does not move the eye");
        Assert.That(before.Count, Is.EqualTo(1));
    }

    [Test]
    public void TheEffectsAreQueuedOnlyWhenOn()
    {
        var q = new CameraEffects();
        q.Add(new CameraVibrate(0, 1, 1, 1, 1, 0), vibrateOption: false);
        Assert.That(q.Count, Is.Zero, "the vibrateEffect option off");
        q.EnableCameraEffect = false;
        q.Add(new CameraVibrate(0, 1, 1, 1, 1, 0), vibrateOption: true);
        Assert.That(q.Count, Is.Zero, "EnableCameraEffect off");
        Assert.That(q.On(true), Is.False);
        q.EnableCameraEffect = true;
        Assert.That(q.On(true), Is.True);
        q.Add(new CameraVibrate(0, 1, 1, 1, 1, 0), vibrateOption: true);
        Assert.That(q.Count, Is.EqualTo(1));
        q.Clear();
        Assert.That(q.Count, Is.Zero);
    }

    [Test]
    public void AFrameAllocatesNothing()
    {
        var q = new CameraEffects(new Random(7));
        q.Add(new CameraVibrate(0, 1e9, 15, 10, 0.5f, 0, true), true);
        Frames(q, 200);
        // the least of a few passes: a tier-up of the loop while measuring is the runtime's, not the update's
        long least = long.MaxValue;
        for (int pass = 0; pass < 5 && least != 0; pass++) least = Math.Min(least, Frames(q, 1000));
        Assert.That(least, Is.EqualTo(0));
    }

    static long Frames(CameraEffects q, int count)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++)
        {
            var eye = new Vector3(i, 0, 0);
            q.Update(i / 60.0, ref eye);
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
