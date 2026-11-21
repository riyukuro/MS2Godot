using System.Numerics;
using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The tomb part of WorldService::FUN_141bc3560 (worldservice_1.cpp:9319-9375) and its registration FUN_141bc3470.</summary>
public class TombTagPassTests
{
    // the MS2TombStone flat: Enabled True, ShapeDimensions 100, 100, 150, reactionArrow True, SoundMaterial 4
    static Tombstone Tomb(bool enabled = true, bool arrow = true, Vector3? size = null)
    {
        var t = new Tombstone("Regen_A", "Damg_A", "Idle_A", "Dead_A", n => 0);
        t.Breakable(enabled, size ?? new Vector3(100, 100, 150), arrow, 4);
        return t;
    }

    // a 200 x 200 x 200 box area at the caster's place facing -y (HitBox.Pose reaches forward from the position)
    static SkillHitShape Box(Vector3 at) =>
        SkillHitShape.Pose(new SkillRangeShape { Shape = 1, Width = 200, Distance = 200, Height = 200 }, 0, at, Vector3.Zero);

    static TagRecord Tag(int brokenOffence = 1) => new() { BrokenOffence = brokenOffence };

    [Test]
    public void TheFlatPropertiesMakeTheTombBox()
    {
        var t = Tomb();
        Assert.That((t.Hittable, t.ReactionArrow, t.HitSound, t.CanBeHit), Is.EqualTo((true, true, 4, true)));
        var b = t.Box(new Vector3(10, 20, 30), Vector3.Zero);
        // size (100, 50, 150) and offset (0, 50, 0): x -50..50, y -50..50 negated about the position, z 0..150
        Assert.That(b.Extents, Is.EqualTo(new Vector3(50, 50, 75)));
        Assert.That(b.Center, Is.EqualTo(new Vector3(10, 20, 105)));
        Assert.That(Tomb(size: Vector3.Zero).Hittable, Is.False, "a zero ShapeDimensions is not hittable");
        Assert.That(Tomb(enabled: false).CanBeHit, Is.False);
    }

    [Test]
    public void ShapeDimensionsParse()
    {
        Assert.That(Tombstone.ParseVector("100, 100, 150"), Is.EqualTo(new Vector3(100, 100, 150)));
        Assert.That(Tombstone.ParseVector(null), Is.EqualTo(Vector3.Zero));
        Assert.That(Tombstone.ParseVector("1, 2"), Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void RegistrationNeedsBrokenOffence()
    {
        var pass = new TombTagPass();
        Assert.That(pass.Register(Tag(0), Box(Vector3.Zero)), Is.False);
        Assert.That(pass.Register(null, Box(Vector3.Zero)), Is.False);
        var tag = Tag();
        Assert.That(pass.Register(tag, Box(Vector3.Zero)), Is.True);
        Assert.That(pass.Register(tag, Box(new Vector3(1, 0, 0))), Is.True);
        Assert.That(pass.Count, Is.EqualTo(1), "the record is the key: its area is replaced");
    }

    [Test]
    public void AnAreaOnAnotherPcsTombSendsTombstoneOnceAndEmpties()
    {
        var pass = new TombTagPass();
        var tag = Tag();
        int armed = 0;
        tag.Hit = () => armed++;
        var tomb = Tomb();
        var targets = new[] { new TombTarget(77, false, tomb, new Vector3(0, -100, 0), Vector3.Zero) };
        var hits = new List<TombHit>();

        pass.Register(tag, Box(Vector3.Zero));
        Assert.That(pass.Run(targets, hits), Is.EqualTo(1));
        Assert.That(hits[0], Is.EqualTo(new TombHit(new TombstoneRequest(77, 1), 4, new Vector3(0, -100, 0))));
        Assert.That(tag.Entered, Does.Contain(tomb.EntityId));
        Assert.That((armed, pass.Count), Is.EqualTo((1, 0)), "FUN_1417591b0 ran; m_3f0 emptied");

        Assert.That(pass.Run(targets, hits), Is.EqualTo(0), "nothing registered: no pass");
        pass.Register(tag, Box(Vector3.Zero));
        Assert.That(pass.Run(targets, hits), Is.EqualTo(0), "the record already entered this tomb");
        var other = Tag();
        pass.Register(other, Box(Vector3.Zero));
        Assert.That(pass.Run(targets, hits), Is.EqualTo(1), "a new record hits it again");
    }

    [Test]
    public void TheOwnTombABusyTombNoArrowOrAMissSendNothing()
    {
        var hits = new List<TombHit>();
        int Run(TombTarget t, Vector3 areaAt)
        {
            var pass = new TombTagPass();
            pass.Register(Tag(), Box(areaAt));
            return pass.Run(new[] { t }, hits);
        }
        var at = new Vector3(0, -100, 0);
        Assert.That(Run(new TombTarget(9, true, Tomb(), at, Vector3.Zero), Vector3.Zero), Is.EqualTo(0), "IsOwnedBy the local player");
        Assert.That(Run(new TombTarget(9, false, Tomb(arrow: false), at, Vector3.Zero), Vector3.Zero), Is.EqualTo(0), "no reactionArrow");
        var hit = Tomb();
        hit.Apply(1, 5, 0);                         // the server's hit: state 6 for 1000 ms
        Assert.That(Run(new TombTarget(9, false, hit, at, Vector3.Zero), Vector3.Zero), Is.EqualTo(0), "not idle");
        hit.Update(1000);
        Assert.That(Run(new TombTarget(9, false, hit, at, Vector3.Zero), Vector3.Zero), Is.EqualTo(1), "idle again");
        Assert.That(Run(new TombTarget(9, false, Tomb(), new Vector3(0, -1000, 0), Vector3.Zero), Vector3.Zero), Is.EqualTo(0), "out of the area");
    }

    [Test]
    public void ACylinderAreaHitsByRadiusAndHeight()
    {
        var cyl = SkillHitShape.Pose(new SkillRangeShape { Shape = 2, Distance = 100, Height = 100 }, 0, Vector3.Zero, Vector3.Zero);
        var tomb = Tomb();
        Assert.That(cyl.Intersects(tomb.Box(new Vector3(140, 0, 0), Vector3.Zero)), Is.True, "box edge 90 from the centre");
        Assert.That(cyl.Intersects(tomb.Box(new Vector3(160, 0, 0), Vector3.Zero)), Is.False);
        Assert.That(cyl.Intersects(tomb.Box(new Vector3(0, 0, 100), Vector3.Zero)), Is.False, "the tomb starts at the cylinder's top");
    }

    [Test]
    public void AttacksReadBrokenOffence()
    {
        var a = SkillAttackData.Load(XElement.Parse("<attack point=\"Attack_01\" brokenOffence=\"3\"/>"));
        Assert.That(a.BrokenOffence, Is.EqualTo(3));
        Assert.That(SkillAttackData.Load(XElement.Parse("<attack/>")).BrokenOffence, Is.EqualTo(0));
    }
}
