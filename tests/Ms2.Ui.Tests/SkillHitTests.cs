using System.Numerics;
using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// A skill attack's data (SkillAttackData::Load, Unk_RangeProperty::LoadElement, SkillArrow::Load), its area pick
/// (CBaseCollision::TestShape / Detect @0x1409fe460 / @0x1409fc6c0, StateSkill::FUN_1417e95a0) and the target records
/// (StateSkill::PickAttackTargets @0x1417f3180). Client space: z up, a caster at rotation z 0 faces -y.
/// </summary>
public class SkillHitTests
{
    static SkillAttackData Attack(string attack)
    {
        var doc = XDocument.Parse("<ms2><skill id=\"100\"><basic mainType=\"1\"/><level value=\"1\"><motion sequenceName=\"A\">"
            + attack + "</motion></level></skill></ms2>");
        var t = new SkillDataTable(() => new[] { doc }, (e, n, _) => e.Elements(n).ToList());
        return t.GetSkillData(100).GetLevelData(1).Motions[0].Attacks[0];
    }

    const string Box = "<attack point=\"Hit_01\" targetCount=\"5\"><range rangeType=\"box\" distance=\"200\" height=\"100\" width=\"100\"/></attack>";

    static HitCandidate Pc(int id, float x, float y, bool owner = false) =>
        new() { Id = id, Position = new Vector3(x, y, 0), Body = SkillHit.PlayerBody(new Vector3(x, y, 0), 0), IsOwner = owner };

    static List<int> Detect(SkillAttackData a, float rotZ, params HitCandidate[] objs)
    {
        var rot = new Vector3(0, 0, rotZ);
        SkillHit.AreaPose(a, Vector3.Zero, rot, out var pos, out var r);
        var shape = SkillHitShape.Pose(a.Range, 0, pos, r);
        var list = new List<int>();
        SkillHit.Detect(shape, pos, r, a.Direction, a.Range.IncludeCaster != 0, false, objs, () => 0, list);
        return list;
    }

    [Test]
    public void TheAttackReadsItsPointRangeAndArrow()
    {
        var a = Attack("<attack point=\"Hit_01\" targetCount=\"3\" direction=\"5\" attackMethodType=\"1\">"
            + "<range rangeType=\"cylinder\" distance=\"200\" height=\"100\" width=\"50\" endWidth=\"60\" rangeAdd=\"0,10,5\""
            + " rangeOffset=\"1,50\" rangeZRotateDegree=\"45\" includeCaster=\"1\" applyTarget=\"2\" castTarget=\"4\"/>"
            + "<arrow overlap=\"1\" nonTarget=\"1\" bounceType=\"2\" bounceCount=\"3\" bounceRadius=\"150\" bounceOverlap=\"0\"/></attack>");
        Assert.That((a.Point, a.TargetCount, a.Direction, a.AttackMethodType), Is.EqualTo(("Hit_01", 3, 5, 1)));
        var r = a.Range;
        Assert.That((r.Shape, r.Distance, r.Height, r.Width, r.EndWidth), Is.EqualTo((2, 200f, 100f, 50f, 60f)));
        Assert.That(r.Add, Is.EqualTo(new Vector3(0, 10, 5)));
        Assert.That(r.Offset, Is.EqualTo(Vector3.Zero), "two parts: FUN_14028f060 gives the zero vector");
        Assert.That((r.ZRotateDegree, r.IncludeCaster, r.ApplyTarget, r.CastTarget), Is.EqualTo((45f, 1, 2, 4)));
        var w = a.Arrow;
        Assert.That((w.Overlap, w.IsNonTargetOne, w.BounceType, w.BounceCount, w.BounceRadius, w.BounceOverlap),
            Is.EqualTo((1, true, 2, 3, 150f, 0)));
        var d = Attack("<attack point=\"Hit_02\"/>");
        Assert.That((d.Direction, d.Range.Shape, d.Arrow.BounceOverlap), Is.EqualTo((3, 0, 1)), "the ctor defaults");
    }

    [Test]
    public void TheBoxHitsWhatIsInFrontNearestFirst()
    {
        // box 100 wide, 200 deep in front (-y); player bodies are 70 x 70 centred on their position
        var hits = Detect(Attack(Box), 0, Pc(4, 0, -230), Pc(2, 0, 150), Pc(3, 0, -260), Pc(1, 0, -150), Pc(5, 80, -100));
        Assert.That(hits, Is.EqualTo(new[] { 5, 1, 4 }), "sort mode 3: front distance up; 2 behind, 3 past the end, 5 overlaps the side");
    }

    [Test]
    public void TheBoxTurnsWithTheCaster()
    {
        // rotation z 90: forward is +x
        var hits = Detect(Attack(Box), 90, Pc(1, 0, -150), Pc(2, 150, 0));
        Assert.That(hits, Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void TheOffsetRunsAlongTheCastersForward()
    {
        var a = Attack("<attack><range rangeType=\"box\" distance=\"100\" height=\"100\" width=\"100\" rangeOffset=\"0,300,0\"/></attack>");
        SkillHit.AreaPose(a, new Vector3(10, 20, 30), new Vector3(0, 0, 90), out var pos, out _);
        Assert.That(Vector3.Distance(pos, new Vector3(310, 20, 30)), Is.LessThan(1e-3f));
        Assert.That(Detect(a, 0, Pc(1, 0, -150), Pc(2, 0, -350)), Is.EqualTo(new[] { 2 }), "the area starts 300 ahead");
        var keep = Attack("<attack attackMethodType=\"1\"><range rangeType=\"box\" distance=\"100\" rangeOffset=\"0,300,0\"/></attack>");
        SkillHit.AreaPose(keep, Vector3.Zero, Vector3.Zero, out pos, out _);
        Assert.That(pos, Is.EqualTo(Vector3.Zero), "attackMethodType 1 keeps the area at the caster");
    }

    [Test]
    public void TheCasterIsSkippedUnlessTheRangeIncludesIt()
    {
        Assert.That(Detect(Attack(Box), 0, Pc(9, 0, 0, owner: true), Pc(1, 0, -150)), Is.EqualTo(new[] { 1 }));
        var inc = Attack("<attack><range rangeType=\"box\" distance=\"200\" height=\"100\" width=\"100\" includeCaster=\"1\"/></attack>");
        Assert.That(Detect(inc, 0, Pc(1, 0, -150), Pc(9, 0, 900, owner: true)), Is.EqualTo(new[] { 9, 1 }),
            "the caster goes first without its own hit test");
    }

    [Test]
    public void TheCylinderHitsByDistanceToTheBodyNearestFirst()
    {
        var a = Attack("<attack><range rangeType=\"cylinder\" distance=\"100\" height=\"100\"/></attack>");
        // body half width 35: 120 away is 85 from the box, 150 away is 115
        var hits = Detect(a, 0, Pc(3, 0, 150), Pc(1, 120, 0), Pc(2, 0, 50));
        Assert.That(hits, Is.EqualTo(new[] { 2, 1 }));
        var high = new HitCandidate { Id = 4, Body = SkillHit.PlayerBody(new Vector3(0, 0, 200), 0) };
        Assert.That(Detect(a, 0, high), Is.Empty, "the body's bottom is above the cylinder's top");
    }

    [Test]
    public void SideSortAndFacingSortOrderTheHits()
    {
        string wide = "<range rangeType=\"box\" distance=\"300\" height=\"100\" width=\"600\"/>";
        // right axis at rotation 0 is -x
        var left = Detect(Attack("<attack direction=\"1\">" + wide + "</attack>"), 0, Pc(1, -100, -100), Pc(2, 100, -100), Pc(3, 0, -100));
        Assert.That(left, Is.EqualTo(new[] { 2, 3, 1 }), "mode 1: the side distance up");
        var right = Detect(Attack("<attack direction=\"2\">" + wide + "</attack>"), 0, Pc(1, -100, -100), Pc(2, 100, -100), Pc(3, 0, -100));
        Assert.That(right, Is.EqualTo(new[] { 1, 3, 2 }));
        var facing = Detect(Attack("<attack direction=\"5\">" + wide + "</attack>"), 0, Pc(1, 200, -50), Pc(2, 0, -200), Pc(3, 100, -100));
        Assert.That(facing, Is.EqualTo(new[] { 2, 3, 1 }), "mode 5: the most in front first");
    }

    [Test]
    public void SortModeZeroShufflesFromTheEnd()
    {
        var a = Attack("<attack direction=\"0\"><range rangeType=\"box\" distance=\"300\" height=\"100\" width=\"600\"/></attack>");
        SkillHit.AreaPose(a, Vector3.Zero, Vector3.Zero, out var pos, out var r);
        var shape = SkillHitShape.Pose(a.Range, 0, pos, r);
        var list = new List<int>();
        var rolls = new Queue<int>(new[] { 0, 0 });
        SkillHit.Detect(shape, pos, r, 0, false, false, new[] { Pc(1, -100, -100), Pc(2, 0, -100), Pc(3, 100, -100) },
            rolls.Dequeue, list);
        // i 2 swaps with 0, then i 1 swaps with 0
        Assert.That(list, Is.EqualTo(new[] { 2, 3, 1 }));
    }

    static List<(int Target, byte Hit)> Pick(SkillAttackData a, IReadOnlyList<int> hits, params int[] rolls)
    {
        long serial = 0;
        var q = new Queue<int>(rolls);
        var tags = new List<SkillDamageTarget>();
        SkillHit.PickTargets(a, hits, q.Dequeue, () => ++serial, tags);
        Assert.That(tags.Select(t => t.Serial), Is.EqualTo(Enumerable.Range(1, tags.Count).Select(i => (long)i)), "a new serial each");
        return tags.Select(t => (t.TargetId, t.Hit)).ToList();
    }

    [Test]
    public void TheTargetCountCutsTheHitsAndOverlapRepeatsThem()
    {
        Assert.That(Pick(Attack("<attack targetCount=\"2\"/>"), new[] { 7, 8, 9 }),
            Is.EqualTo(new[] { (7, (byte)0), (8, (byte)1) }));
        Assert.That(Pick(Attack("<attack targetCount=\"4\"><arrow overlap=\"1\"/></attack>"), new[] { 7, 8 }, 5, 2),
            Is.EqualTo(new[] { (7, (byte)0), (8, (byte)1), (8, (byte)2), (7, (byte)3) }), "rand() % 2 picks the repeats");
        Assert.That(Pick(Attack("<attack targetCount=\"3\"/>"), new[] { 7 }), Is.EqualTo(new[] { (7, (byte)0) }));
    }

    [Test]
    public void TargetlessRecordsFillTheCountOrStandForANonTargetArrow()
    {
        Assert.That(Pick(Attack("<attack targetCount=\"3\" attackMethodType=\"1\"/>"), new[] { 7 }),
            Is.EqualTo(new[] { (7, (byte)0), (0, (byte)1), (0, (byte)2) }));
        Assert.That(Pick(Attack("<attack targetCount=\"3\"><arrow nonTarget=\"1\"/></attack>"), null),
            Is.EqualTo(new[] { (0, (byte)0) }));
    }
}
