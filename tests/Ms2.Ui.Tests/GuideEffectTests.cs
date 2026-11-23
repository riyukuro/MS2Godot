using System.Xml.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Ui.Guide;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The guide's effect action (CGuideActionEffect, guideaction.cpp:432-461): each cube's effect goes through the field's
/// effect service, spawned at the cube's transform on the start (FUN_1411a49c0) and removed at once on the stop (FUN_1411a6060).
/// </summary>
public class GuideEffectTests
{
    sealed class Fx : IRegionSkillEffects
    {
        public readonly List<(string Effect, Xform At, bool Always, int Random)> Spawned = new();
        public readonly List<(int Handle, float Seconds)> Faded = new();
        public int Next = 7;
        public bool Has(string effect) => true;
        public int Spawn(string effect, Xform at, bool always, int randomIndex) { Spawned.Add((effect, at, always, randomIndex)); return Next++; }
        public void Insert(string effect, Xform at, bool always, int randomIndex) { }
        public void FadeOut(int handle, float seconds) => Faded.Add((handle, seconds));
    }

    sealed class World(IRegionSkillEffects fx) : GuideWorld
    {
        public override IRegionSkillEffects Effects => fx;
    }

    static EffectAction Action()
    {
        var a = new EffectAction();
        a.Load(XElement.Parse("""
            <action type="effect" path="UGC_Test/Eff_Tutorial_Sound_target.xml">
              <cube pos="10,20,30" rotate="90,0,45" scale="2" />
              <cube pos="-675,525,625" />
            </action>
            """));
        return a;
    }

    [Test]
    public void CubesSpawnAtTheirTransformAndStopAtOnce()
    {
        var fx = new Fx();
        var g = new GuideManager(new World(fx));
        var a = Action();
        a.Start(g);

        Assert.That(fx.Spawned.Count, Is.EqualTo(2));
        var (effect, at, always, random) = fx.Spawned[0];
        Assert.That(effect, Is.EqualTo("UGC_Test/Eff_Tutorial_Sound_target.xml"));
        Assert.That(at.T, Is.EqualTo(new Vec3(10, 20, 30)));
        Assert.That(at.S, Is.EqualTo(2).Within(1e-6));
        const float rad = 3.1415927f / 180.0f;
        var r = RegionSkillObject.EulerXyzRadians(90 * rad, 0, 45 * rad);
        for (int i = 0; i < 9; i++) Assert.That(at.R[i], Is.EqualTo(r[i]).Within(1e-5), $"rotation [{i}]");
        Assert.That(always, Is.False, "the target carries no always flag");
        Assert.That(random, Is.EqualTo(0));
        Assert.That(fx.Spawned[1].At.T, Is.EqualTo(new Vec3(-675, 525, 625)));
        Assert.That(fx.Spawned[1].At.S, Is.EqualTo(1).Within(1e-6), "the default scale");
        Assert.That(a.Cubes.Select(c => c.Handle), Is.EqualTo(new[] { 7, 8 }));

        a.Stop(g);
        Assert.That(fx.Faded, Is.EqualTo(new[] { (7, 0f), (8, 0f) }));
        Assert.That(a.Cubes.All(c => c.Handle == 0));
        a.Stop(g);
        Assert.That(fx.Faded.Count, Is.EqualTo(2), "a stopped cube is not removed again");
    }

    [Test]
    public void NoEffectServiceSpawnsNothing()
    {
        var g = new GuideManager(new World(null));
        var a = Action();
        a.Start(g);
        Assert.That(a.Cubes.All(c => c.Handle == 0));
        a.Stop(g);
    }
}
