using System.Numerics;
using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The path segment effects of a magic (CMagic::FUN_141462900, magicdata.cpp:1214-1365; the GMS2 x64 client's @0x140af0840):
/// the attacks that lay them, the pick, the points per rayType, the start following the fire node, the destroy hold and
/// another owner's. Client space: z up, rotation z 0 faces -y.
/// </summary>
public class OwnRayMagicTests
{
    const int T = 1000;
    static readonly Vector3 Node = new(0, 0, 50);

    static (SkillData Skill, SkillAttackData Attack) Ray(int rayType, int targetCount = 2, string remain = "Remain.xml")
    {
        var doc = XDocument.Parse("<ms2><skill id=\"100\"><basic mainType=\"1\"/><level value=\"1\"><motion sequenceName=\"A\">"
            + $"<attack point=\"p0\" targetCount=\"{targetCount}\" magicPathID=\"31\" attackMethodType=\"2\">"
            + "<range rangeType=\"box\" distance=\"400\" height=\"150\" width=\"200\" applyTarget=\"1\"/>"
            + $"<arrow nonTarget=\"1\" effect=\"Ray.xml\" effectRemain=\"{remain}\" rayType=\"{rayType}\"/></attack>"
            + "</motion></level></skill></ms2>");
        var t = new SkillDataTable(() => new[] { doc }, (e, n, _) => e.Elements(n).ToList());
        var d = t.GetSkillData(100);
        return (d, d.GetLevelData(1).Motions[0].Attacks[0]);
    }

    // Chain Lightning's path (magicPath 10300031): vel 0, distance 600, destroyTime 0.3
    static MagicPathTable Paths(float destroy = 0.3f) => new(XDocument.Parse(
        $"<ms2><type id=\"31\"><move distance=\"600\" destroyTime=\"{destroy}\" lookAtType=\"1\"/></type></ms2>"));

    static HitCandidate Mob(int id, float x, float y) => new()
    {
        Id = id, Position = new Vector3(x, y, 0), Body = SkillHit.PlayerBody(new Vector3(x, y, 0), 0), Kind = HitKind.Npc, Hp = 100,
    };

    static HitCandidate Me(int id = 42) => new()
    {
        Id = id, Position = Vector3.Zero, Body = SkillHit.PlayerBody(Vector3.Zero, 0), IsOwner = true, Kind = HitKind.Pc, Team = 1, Hp = 1,
    };

    static readonly MagicOwner Owner = new(Vector3.Zero, 0, true, false, false);

    // one target-less record (nonTarget 1: SkillHit.PickTargets), as the own fire makes
    static List<SkillDamageTarget> Tags() => new() { new SkillDamageTarget(1, 0, 0, Array.Empty<SkillDamageLink>()) };

    static OwnTagMagics Made(SkillData skill, SkillAttackData attack, Func<Vector3> node = null, float destroy = 0.3f)
    {
        var m = new OwnTagMagics { NodePosition = node == null ? _ => Node : _ => node() };
        Assert.That(m.Add(skill, attack, new CastRecord { CastId = 7 }, Tags(), T, Paths(destroy), null), Is.EqualTo(1));
        return m;
    }

    static List<MagicRaySegment> Tick(OwnTagMagics m, int now, HitCandidate[] objects, List<MagicEndEffect> ends = null,
        MagicOwner? owner = null)
    {
        m.Tick(now, 0, owner ?? Owner, objects, null, null, null, () => 0, null, false, null, null, ends, null);
        var into = new List<MagicRaySegment>();
        m.RaySegments(into);
        return into;
    }

    static Vector3 HitPoint(in HitCandidate o) => o.Body.Center;

    [Test]
    public void TheAttacksThatLayRaySegments()
    {
        var kms = new SkillAttackData { AttackMethodType = 2 };
        kms.Arrow.Effect = "CH/Ray.xml";
        Assert.That((kms.RaySegments, kms.RayEffect, kms.FollowHand), Is.EqualTo((true, "CH/Ray.xml", true)), "KMS: attackMethodType 2, the arrow effect");
        Assert.That(new SkillAttackData { AttackMethodType = 1 }.RaySegments, Is.False);
        Assert.That(new SkillAttackData().RaySegments, Is.False);

        // GMS2 per-skill rows: no attackMethodType; an arrow record (a set effect) returns, an empty effectRay returns
        var gms = new SkillAttackData { Gms2Row = true };
        gms.Arrow.EffectRay = "CH/Ray.xml";
        Assert.That((gms.RaySegments, gms.RayEffect, gms.FollowHand), Is.EqualTo((true, "CH/Ray.xml", true)));
        gms.Arrow.Effect = "CH/Arrow.xml";
        Assert.That(gms.RaySegments, Is.False, "an arrow record lays none");
        var plain = new SkillAttackData { Gms2Row = true, AttackMethodType = 2 };
        plain.Arrow.Effect = "CH/Ray.xml";
        Assert.That((plain.RaySegments, plain.FollowHand), Is.EqualTo((false, false)), "GMS2 reads effectRay, not attackMethodType");

        var e = XElement.Parse("<attack><arrow rayType=\"2\" effectRay=\"A.xml\"/></attack>");
        var loaded = SkillAttackData.Load(e, gms2Row: true);
        Assert.That((loaded.Arrow.RayType, loaded.Arrow.EffectRay, loaded.RaySegments), Is.EqualTo((2, "A.xml", true)));
    }

    [Test]
    public void RayTypeOneChainsThePickedTargetsUpToTheTargetCount()
    {
        var (skill, attack) = Ray(1, targetCount: 2);
        var objects = new[] { Me(), Mob(9, 0, -100), Mob(10, 0, -200), Mob(11, 0, -300), Mob(12, 0, -900) };
        var m = Made(skill, attack);
        var segs = Tick(m, T, objects);
        Assert.That(segs, Has.Count.EqualTo(2), "two targets of the three in the box");
        Assert.That(segs.Select(s => s.Slot), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(segs.All(s => s.File == "Ray.xml"), "the arrow effect (GetArrowEffect, no type test)");
        Assert.That(segs[0].From, Is.EqualTo(Node), "the start is the fire node");
        Assert.That(segs[1].From, Is.EqualTo(segs[0].To), "each segment from the one before");
        var centres = objects.Skip(1).Take(3).Select(o => HitPoint(o)).ToList();
        Assert.That(centres, Does.Contain(segs[0].To).And.Contain(segs[1].To));
        Assert.That(segs[0].To, Is.Not.EqualTo(segs[1].To));
        Assert.That(segs[0].Key, Is.EqualTo(segs[1].Key), "one magic");
    }

    [Test]
    public void RayTypeZeroStartsEverySegmentAtTheStart()
    {
        var (skill, attack) = Ray(0, targetCount: 3);
        var objects = new[] { Me(), Mob(9, 0, -100), Mob(10, 50, -200), Mob(11, -50, -300) };
        var segs = Tick(Made(skill, attack), T, objects);
        Assert.That(segs, Has.Count.EqualTo(3));
        Assert.That(segs.All(s => s.From == Node));
        Assert.That(segs.Select(s => s.To), Is.EquivalentTo(objects.Skip(1).Select(o => HitPoint(o))));
    }

    [Test]
    public void RayTypeTwoLaysEachHitOnTheDirectionAndKeepsItsSlots()
    {
        var (skill, attack) = Ray(2, targetCount: 2);
        var objects = new[] { Me(), Mob(9, 60, -100), Mob(10, -60, -250) };
        var m = Made(skill, attack, destroy: 10);
        var segs = Tick(m, T, objects);
        Assert.That(segs, Has.Count.EqualTo(2));
        Assert.That(segs.All(s => s.From == Node), "rayType 2 starts every segment at the start");
        var f = m.Flight(0);
        var dir = Vector3.Normalize(f.Target - f.Current);
        Assert.That(segs.Select(s => IdAt(s.To, objects, dir)), Is.EquivalentTo(new[] { 9, 10 }),
            "each hit point laid at the start plus the direction times its flat distance");
        // a target gone: rayType 2 stops no slot past the segments (magicdata.cpp:1354), so slot 1 keeps its last points
        var before = segs[1];
        int gone = IdAt(before.To, objects, dir);
        segs = Tick(m, T + 50, objects.Where(o => o.Id != gone).ToArray());
        Assert.That(segs, Has.Count.EqualTo(2), "slot 1 still plays");
        Assert.That(segs[1].To, Is.EqualTo(before.To));
    }

    // the object whose hit point, laid on the direction at its flat distance from the start, is the point
    static int IdAt(Vector3 to, HitCandidate[] objects, Vector3 dir)
    {
        foreach (var o in objects.Skip(1))
        {
            var c = HitPoint(o);
            float flat = MathF.Sqrt((c.X - Node.X) * (c.X - Node.X) + (c.Y - Node.Y) * (c.Y - Node.Y));
            if (Vector3.Distance(to, Node + dir * flat) < 1e-3f) return o.Id;
        }
        return -1;
    }

    [Test]
    public void ATargetGoneStopsTheSlotsPastTheSegmentsForTheOtherRayTypes()
    {
        var (skill, attack) = Ray(1, targetCount: 2);
        var objects = new[] { Me(), Mob(9, 0, -100), Mob(10, 0, -200) };
        var m = Made(skill, attack, destroy: 10);
        var segs = Tick(m, T, objects);
        Assert.That(segs, Has.Count.EqualTo(2));
        int last = objects.Skip(1).First(o => HitPoint(o) == segs[1].To).Id;
        segs = Tick(m, T + 50, objects.Where(o => o.Id != last).ToArray());
        Assert.That(segs, Has.Count.EqualTo(1), "slot 1 stopped (FUN_1411a6060)");
        segs = Tick(m, T + 100, objects);
        Assert.That(segs, Has.Count.EqualTo(1), "a stopped slot is not started again; only the first tick requests effects");
    }

    [Test]
    public void NoTargetLaysOneSegmentAndInsertsTheEffectRemain()
    {
        var mob = new[] { Me() };
        var (skill1, attack1) = Ray(1);
        var ends = new List<MagicEndEffect>();
        var m = Made(skill1, attack1);
        var segs = Tick(m, T, mob, ends);
        var f = m.Flight(0);
        Assert.That(segs, Has.Count.EqualTo(1));
        Assert.That((segs[0].From, segs[0].To), Is.EqualTo((Node, f.Target)), "rayType 1: the start to the target point");
        Assert.That(f.Target, Is.Not.EqualTo(Node));
        // bSingle's effectRemain at ptLast first (the segment tick runs before the state's), then the magic's own end
        // (vel 0: FUN_14145f570 in the same tick) plays effectRemain at the current point, the start
        Assert.That(ends.Where(e => e.File == "Remain.xml").Select(e => e.At).ToArray(), Is.EqualTo(new[] { f.Target, Node }));
        Tick(m, T + 50, mob, ends);
        Assert.That(ends.Count(e => e.File == "Remain.xml"), Is.EqualTo(2), "bSingle's only with the first request");

        var (skill0, attack0) = Ray(0);
        ends.Clear();
        segs = Tick(Made(skill0, attack0), T, mob, ends);
        Assert.That((segs[0].From, segs[0].To), Is.EqualTo((Node, Node)), "rayType 0: the start twice");
        Assert.That(ends.Where(e => e.File == "Remain.xml").Select(e => e.At).ToArray(), Is.EqualTo(new[] { Node, Node }), "ptLast the start");

        var (skillN, attackN) = Ray(1, remain: "");
        ends.Clear();
        Tick(Made(skillN, attackN), T, mob, ends);
        Assert.That(ends, Is.Empty, "an empty effectRemain inserts nothing");
    }

    [Test]
    public void TheStartFollowsTheFireNodeEveryTick()
    {
        var (skill, attack) = Ray(1, targetCount: 1);
        var objects = new[] { Me(), Mob(9, 0, -100) };
        var node = Node;
        var m = Made(skill, attack, () => node, destroy: 10);
        Assert.That(Tick(m, T, objects)[0].From, Is.EqualTo(Node));
        node = new Vector3(30, -10, 60);
        var segs = Tick(m, T + 50, objects);
        Assert.That(segs[0].From, Is.EqualTo(node), "FUN_1414609e0 each tick");
        Assert.That(segs[0].At, Is.EqualTo(node), "the magic's scene is at the start");
        Assert.That(segs[0].To, Is.EqualTo(HitPoint(objects[1])));
    }

    [Test]
    public void TheSegmentsStayThroughDestroyTimeAfterTheEnd()
    {
        var (skill, attack) = Ray(1, targetCount: 1);
        var objects = new[] { Me(), Mob(9, 0, -100) };
        var m = Made(skill, attack);   // vel 0: it ends at its first tick in state 2
        Assert.That(Tick(m, T, objects), Has.Count.EqualTo(1));
        Assert.That(m.Flight(0).State, Is.EqualTo(3));
        Assert.That(Tick(m, T + 299, objects), Has.Count.EqualTo(1), "state 3 for destroyTime 0.3");
        Assert.That(Tick(m, T + 300, objects), Is.Empty, "state 4 stops them; the model goes");
        Assert.That(m.Count, Is.EqualTo(0));

        var none = Made(skill, attack, destroy: 0);
        Assert.That(Tick(none, T, objects), Is.Empty, "destroyTime 0: state 4 in the same tick");
        Assert.That(none.Count, Is.EqualTo(0));
    }

    [Test]
    public void ADeadOwnerLaysNothing()
    {
        var (skill, attack) = Ray(1);
        var objects = new[] { Me(), Mob(9, 0, -100) };
        var m = Made(skill, attack, destroy: 10);
        Assert.That(Tick(m, T, objects, owner: new MagicOwner(Vector3.Zero, 0, true, true, false)), Is.Empty);
        Assert.That(Tick(m, T + 50, objects), Has.Count.EqualTo(1), "the pick and the request wait for a live owner");
    }

    [Test]
    public void AnotherOwnersRayMagicIsMadeAndFollowsItsNode()
    {
        // FUN_14171c1d0 makes the magic for any owner; FUN_1414609e0 takes the fire node for follow-hand ones (m_n530 1 too)
        var (skill, attack) = Ray(1, targetCount: 1);
        const int ownerId = 5;
        var r = new RemoteTagMagics { NodePosition = (id, _) => id == ownerId ? new Vector3(0, 0, 70) : default };
        var d = new SkillDamage
        {
            Mode = 0, OwnerId = ownerId, CastUid = 7, AttackPoint = 0, Position = new Vector3S(0, 0, 0),
            MagicDirection = new Vector3(0, -1, 0), FromServer = true,
            Projectiles = new List<SkillDamageTarget> { new(1, 0, 0, Array.Empty<SkillDamageLink>()) },
        };
        var objects = new[] { Me(ownerId), Mob(9, 0, -100) };
        Assert.That(r.OnDamage(d, skill, attack, Paths(), T, null, objects), Is.EqualTo(1));
        r.Tick(ownerId, true, T, new MagicOwner(Vector3.Zero, 0, false, false, false), objects, () => 0, null, new List<MagicEndEffect>());
        var segs = new List<MagicRaySegment>();
        r.RaySegments(segs);
        Assert.That(segs, Has.Count.EqualTo(1));
        Assert.That((segs[0].From, segs[0].To), Is.EqualTo((new Vector3(0, 0, 70), HitPoint(objects[1]))));

        // a non-server packet's root waits for its start tick
        var w = new RemoteTagMagics();
        w.BeginCast(ownerId, 7, 0);
        w.SkillEntered(ownerId);
        d.FromServer = false;
        Assert.That(w.OnDamage(d, skill, attack, Paths(), T, null, objects), Is.EqualTo(1));
        Assert.That(w.Pending(ownerId), Is.EqualTo(1));
        w.Tick(ownerId, true, T, new MagicOwner(Vector3.Zero, 0, false, false, false), objects, () => 0, null, null);
        segs.Clear();
        w.RaySegments(segs);
        Assert.That(segs, Is.Empty, "start tick 0: nothing (magicdata.cpp:758)");
    }

    [Test]
    public void ATickAllocatesNothing()
    {
        var (skill, attack) = Ray(1, targetCount: 3);
        var objects = new[] { Me(), Mob(9, 0, -100), Mob(10, 0, -200), Mob(11, 0, -300) };
        var m = Made(skill, attack, destroy: 100);
        var into = new List<MagicRaySegment>(16);
        Func<int> rand = () => 0;
        var owner = Owner;
        for (int i = 0; i < 10; i++)
        {
            m.Tick(T + i, 0, owner, objects, null, null, null, rand, null, false, null, null, null, null);
            into.Clear();
            m.RaySegments(into);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 10; i < 1010; i++)
        {
            m.Tick(T + i, 0, owner, objects, null, null, null, rand, null, false, null, null, null, null);
            into.Clear();
            m.RaySegments(into);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(into, Has.Count.EqualTo(3));
        Assert.That(bytes, Is.EqualTo(0), "1000 ticks with three segments");
    }

    /// <summary>
    /// The start follows the fire node each tick (<see cref="OwnTagMagics.NodePosition"/>): the game's lookup is the dummy
    /// memo, which misses every tick while the caster moves, then <see cref="Ms2.Formats.Model.DummyWorld.FindPoint"/> on the
    /// model's moved pose. The magic's ticks and that lookup together allocate nothing.
    /// </summary>
    [Test]
    public void ATickWhileTheCasterMovesAllocatesNothing()
    {
        var (skill, attack) = Ray(1, targetCount: 3);
        var objects = new[] { Me(), Mob(9, 0, -100), Mob(10, 0, -200), Mob(11, 0, -300) };
        var rows = new List<Ms2.Formats.World.EffectDummy>
        {
            new() { Name = "Eff_FireNode", ParentNode = "Scene Root", ApplyWorldTransform = true, ApplyLocalScale = false, Translate = new Vector3(10, -20, 90) },
        };
        var memo = new Ms2.Formats.Model.DummyMemo();
        var pose = new Ms2.Formats.Model.DummyWorld.Pose { S = 1 };
        pose.R[0] = pose.R[4] = pose.R[8] = 1;
        float x = 0;
        Vector3 Fire(string node)
        {
            var at = new Ms2.Formats.Model.DummyMemo.Key(1, 0, 0, 0, 1, 0, 0, 0, 1, x, 0, 0);
            if (!memo.TryGet(rows, node, "Scene Root", at, out var r))
            {
                pose.T = new Ms2.Formats.Model.Vec3(x, 0, 0);
                r = Ms2.Formats.Model.DummyWorld.FindPoint(rows, node, pose, "Scene Root", out var p)
                    ? new Ms2.Formats.Model.DummyMemo.Point((float)p.X, (float)p.Y, (float)p.Z, true) : default;
                memo.Set(rows, node, "Scene Root", at, r);
            }
            return new Vector3(r.X, r.Y, r.Z);
        }
        var m = new OwnTagMagics { NodePosition = Fire };
        var paths = new MagicPathTable(XDocument.Parse(
            "<ms2><type id=\"31\"><move distance=\"600\" destroyTime=\"100\" lookAtType=\"1\" fireNode=\"Eff_FireNode\"/></type></ms2>"));
        Assert.That(m.Add(skill, attack, new CastRecord { CastId = 7 }, Tags(), T, paths, null), Is.EqualTo(1));
        var into = new List<MagicRaySegment>(16);
        Func<int> rand = () => 0;
        void Step(int i)
        {
            x = i;   // the caster walks one unit a tick
            m.Tick(T + i, 0, new MagicOwner(new Vector3(x, 0, 0), 0, true, false, false), objects, null, null, null, rand, null, false,
                null, null, null, null);
            into.Clear();
            m.RaySegments(into);
        }
        for (int i = 0; i < 10; i++) Step(i);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 10; i < 1010; i++) Step(i);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(into, Has.Count.EqualTo(3));
        Assert.That(into[0].From.X, Is.EqualTo(1009f + 10f), "the start is the moved fire node");
        Assert.That(bytes, Is.EqualTo(0), "1000 ticks of a moving caster");
    }
}
