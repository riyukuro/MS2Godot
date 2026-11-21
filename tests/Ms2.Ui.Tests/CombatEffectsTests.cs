using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// WorldService's time scale ramp (worldservice_1.cpp:8430, :8445, :9883; FUN_141b6fdb0, util.cpp:768), PC's invincible blink
/// (pc.cpp:3384, :2612, :3609) and CTriggerService's one-time effects (triggerservice.cpp:885).
/// </summary>
public class CombatEffectsTests
{
    [Test]
    public void TheEaseCurves()
    {
        Assert.That(TimeScaleRamp.Ease(0.5f, 0), Is.EqualTo(0.5f));
        Assert.That(TimeScaleRamp.Ease(0.5f, 1), Is.EqualTo(0.5f));
        Assert.That(TimeScaleRamp.Ease(0.5f, 2), Is.EqualTo(MathF.Log(6f) / 2.397895f));
        Assert.That(TimeScaleRamp.Ease(1f, 2), Is.EqualTo(1f).Within(1e-5));
        Assert.That(TimeScaleRamp.Ease(0.5f, 3), Is.EqualTo((MathF.Exp(2.5f) - 1f) / 147.41316f));
        Assert.That(TimeScaleRamp.Ease(1f, 3), Is.EqualTo(1f).Within(1e-5));
    }

    [Test]
    public void TheRampStartsAtTheStartScaleAndEasesToTheEnd()
    {
        var t = new TimeScaleRamp();
        Assert.That(t.Tick(100), Is.False, "nothing runs");
        Assert.That(t.Start(0.2f, 1f, 1000f, 2, 1000), Is.True);
        Assert.That(t.Scale, Is.EqualTo(0.2f));
        Assert.That(t.Start(0.5f, 1f, 1000f, 1, 1100), Is.False, "a second start while one runs is dropped");
        Assert.That(t.Tick(1500), Is.True);
        float k = MathF.Log(6f) / 2.397895f;
        Assert.That(t.Scale, Is.EqualTo(1f * k + (1f - k) * 0.2f));
        Assert.That(t.Tick(2000), Is.True);
        Assert.That(t.Scale, Is.EqualTo(1f));
        Assert.That(t.Running, Is.False);
    }

    [Test]
    public void CurveZeroHoldsTheStartScaleAndStopResets()
    {
        var t = new TimeScaleRamp();
        t.Start(0.3f, 2f, 500.9f, 0, 10);
        Assert.That(t.Tick(400), Is.False, "curve 0 sets nothing until the end");
        Assert.That(t.Scale, Is.EqualTo(0.3f));
        Assert.That(t.Tick(509), Is.False, "the end is now + (int)duration: 510");
        Assert.That(t.Tick(510), Is.True);
        Assert.That(t.Scale, Is.EqualTo(1f));
        Assert.That(t.Stop(), Is.False, "nothing runs");
        t.Start(0.5f, 1f, 1000f, 1, 0);
        Assert.That(t.Stop(), Is.True);
        Assert.That(t.Scale, Is.EqualTo(1f));
        Assert.That(t.Start(0.4f, 1f, 1000f, 1, 0), Is.True, "free again after the stop");
    }

    [Test]
    public void TheScaleIsHeldAtZeroOrAbove()
    {
        var t = new TimeScaleRamp();
        t.Start(-0.5f, -1f, 1000f, 1, 0);
        Assert.That(t.Scale, Is.EqualTo(0f), "a negative start scale");
        Assert.That(t.Tick(500), Is.True);
        Assert.That(t.Scale, Is.EqualTo(0f), "a negative end scale mid-ramp");
    }

    [Test]
    public void TheInvincibleLengthsComeFromTheConstants()
    {
        Assert.That(InvincibleBlink.TickFor(InvincibleBlink.RevivalTimer, null), Is.EqualTo(10000));
        Assert.That(InvincibleBlink.TickFor(InvincibleBlink.PortalTimer, null), Is.EqualTo(10000));
        Assert.That(InvincibleBlink.BuffEnd(InvincibleBlink.PortalTimer, 5000, null), Is.EqualTo(15000));
        Assert.That(InvincibleBlink.BuffEnd(1, 5000, null), Is.EqualTo(0));
    }

    [Test]
    public void TheBlinkFlipsPastAThousandAndSpeedsUpAfterHalfTheLength()
    {
        var b = new InvincibleBlink();
        Assert.That(b.Tick(0, out _, out _), Is.False, "not invincible");
        b.Start(1000, InvincibleBlink.PortalTimer, 0);
        Assert.That(b.Active && b.TimerId == InvincibleBlink.PortalTimer);
        // speed 5: 100 ms gives 500, then 1000 (not past), then 1500 flips to dimmed
        Assert.That(b.Tick(100, out float a, out bool ended) && a == 0.9f && !ended);
        b.Tick(200, out a, out _); Assert.That(a, Is.EqualTo(0.9f), "1000 is not past 1000");
        b.Tick(201, out a, out _); Assert.That(a, Is.EqualTo(0.5f), "1005 flips and resets the phase");
        // past half the length (delay 500): at 700, speed = (700 - 500)/1000 + 5 = 5.2; 499 ms gives 2594.8 -> flip
        b.Tick(700, out a, out _); Assert.That(a, Is.EqualTo(0.9f));
        b.Tick(1000, out a, out ended); Assert.That(ended, Is.False, "the end test is start + length < now");
        Assert.That(b.Tick(1001, out a, out ended), Is.True);
        Assert.That(ended && a == 1f && !b.Active && b.TimerId == 0);
    }

    [Test]
    public void TheBlinkPhaseCarriesOverARestart()
    {
        var b = new InvincibleBlink();
        b.Start(10000, InvincibleBlink.RevivalTimer, 0);
        b.Tick(150, out float a, out _);
        b.Tick(250, out a, out _);
        Assert.That(a, Is.EqualTo(0.5f));
        b.Start(10000, InvincibleBlink.PortalTimer, 300);
        b.Tick(310, out a, out _);
        Assert.That(a, Is.EqualTo(0.5f), "the dimmed state is not cleared by the start");
    }

    sealed class Fx : IRegionSkillEffects
    {
        public readonly List<string> Log = new();
        public int Next = 1;
        public bool Has(string effect) => true;
        public int Spawn(string effect, Xform at, bool always, int randomIndex) { Log.Add($"spawn {effect} {at.T} {always}"); return Next++; }
        public void Insert(string effect, Xform at, bool always, int randomIndex) => Log.Add($"insert {effect}");
        public void FadeOut(int handle, float seconds) => Log.Add($"fade {handle} {seconds}");
    }

    [Test]
    public void OneTimeEffectsKeepOneHandlePerId()
    {
        var fx = new Fx(); var o = new OneTimeEffects();
        o.Apply(5, true, "Eff/A.xml", fx);
        o.Apply(5, true, "Eff/B.xml", fx);
        o.Apply(6, false, "", fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { $"spawn Eff/A.xml {new Vec3(0, 0, 0)} True" }), "a kept id is left; an unknown remove does nothing");
        o.Apply(5, false, "", fx);
        Assert.That(fx.Log[^1], Is.EqualTo("fade 1 0"));
        Assert.That(o.Has(5), Is.False);
        fx.Next = 0;
        o.Apply(9, true, "Eff/C.xml", fx);
        Assert.That(o.Has(9), Is.False, "a 0 handle is not kept");
    }
}
