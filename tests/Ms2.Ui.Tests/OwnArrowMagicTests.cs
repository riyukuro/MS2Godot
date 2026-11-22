using System.Numerics;
using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// An own arrow record's magic (attackMethodType 1): CMagic::FUN_1414609e0 and FUN_14145d9d0 (its target and curve),
/// FUN_14145de80 (the tick), FUN_14145ec50 (the flight), FUN_141465b30 (the arrow fill and its send) and FUN_141463fb0
/// (the end). Client space: z up, rotation z 0 faces -y.
/// </summary>
public class OwnArrowMagicTests
{
    const string Arrow = "<attack point=\"Hit_01\" targetCount=\"1\" attackMethodType=\"1\" magicPathID=\"600\" brokenOffence=\"1\">"
        + "<range rangeType=\"box\" distance=\"200\" height=\"100\" width=\"100\" applyTarget=\"1\"/>"
        + "<arrow nonTarget=\"1\" rayPhysxTest=\"1\" collision=\"25,100,15\" collisionAdd=\"0,25,15\"/></attack>";

    static CastRecord Record()
    {
        var doc = XDocument.Parse("<ms2><skill id=\"100\"><basic mainType=\"1\"/><level value=\"1\"><motion sequenceName=\"A\">"
            + Arrow + "</motion></level></skill></ms2>");
        var t = new SkillDataTable(() => new[] { doc }, (e, n, _) => e.Elements(n).ToList());
        var d = t.GetSkillData(100);
        return new CastRecord { Skill = d, Level = d.GetLevelData(1), CastId = 0x1122334455L, Motion = 0, SendCount = 3 };
    }

    static MagicPathTable Paths(string move) =>
        new(XDocument.Parse("<ms2><type id=\"600\"><move " + move + "/></type></ms2>"));

    // the clock of the checks: m_nLastTick 0 is "not set" (FUN_14145de80), so no tick runs at 0
    const int T = 1000;

    const string Linear = "vel=\"1800\" distance=\"810\"";

    static HitCandidate Mob(int id, float x, float y) => new()
    {
        Id = id, Position = new Vector3(x, y, 0), Body = SkillHit.PlayerBody(new Vector3(x, y, 0), 0),
        Kind = HitKind.Npc, Team = 0, Hp = 100,
    };

    static readonly HitCandidate Me = new()
    {
        Id = 42, Position = Vector3.Zero, Body = SkillHit.PlayerBody(Vector3.Zero, 0), IsOwner = true, Kind = HitKind.Pc, Team = 1, Hp = 1,
    };

    static readonly MagicOwner Owner = new(Vector3.Zero, 0, true, false, false);

    static readonly List<SkillDamageTarget> OneRecord = new() { new SkillDamageTarget(7, 0, 0, Array.Empty<SkillDamageLink>()) };

    static OwnTagMagics Add(CastRecord r, MagicPathTable paths, MagicRayCast ray = null)
    {
        var m = new OwnTagMagics();
        Assert.That(m.Add(r.Skill, SkillAttackFire.AttackAt(r, 0), r, OneRecord, T, paths, _ => Vector3.Zero, ownerId: 42,
            rayCast: ray), Is.EqualTo(1), "the bow's nonTarget record makes an arrow magic");
        return m;
    }

    [Test]
    public void ABowArrowFliesAlongTheCastDirectionAndSendsItsSweep()
    {
        var r = Record();
        var m = Add(r, Paths(Linear));
        var objects = new[] { Me, Mob(9, 0, -250) };
        var sends = new List<SkillHitRequest>();
        m.Tick(T + 0, 0, Owner, objects, r, null, sends, () => 0, null);
        Assert.That(sends, Is.Empty, "the first tick does not move: the box reaches 100 ahead");
        Assert.That(m.Flight(0).State, Is.EqualTo(2));
        var pass = new TombTagPass();
        Assert.That(m.Tick(T + 100, 0, Owner, objects, r, pass, sends, () => 0, null), Is.EqualTo(1), "the box registers");
        var s = sends.Single();
        Assert.That((s.CastId, s.Serial, s.Attack, s.SendCount), Is.EqualTo((r.CastId, 7L, (byte)0, 3)));
        Assert.That(s.Start, Is.EqualTo(Vector3.Zero), "the last point");
        Assert.That(s.OwnerPosition.Y, Is.EqualTo(-180f).Within(1e-3f), "the current point: 0.1 s at vel 1800 along -y");
        Assert.That(Vector3.Distance(s.Rotation, new Vector3(0, 1, 0)), Is.LessThan(1e-5f), "m_matRotation's y column, -direction");
        Assert.That(s.Targets, Is.EqualTo(new[] { new SkillHitTarget(9, 0) }));
        Assert.That(m.Count, Is.EqualTo(0), "a bounce count of 1 ends the flight at the hit");
    }

    [Test]
    public void AnArrowWithoutHitsEndsAtItsDistance()
    {
        var r = Record();
        var m = Add(r, Paths(Linear));
        var sends = new List<SkillHitRequest>();
        var objects = new[] { Me };
        m.Tick(T + 0, 0, Owner, objects, r, null, sends, () => 0, null);
        m.Tick(T + 400, 0, Owner, objects, r, null, sends, () => 0, null);
        Assert.That(m.Flight(0).Current.Y, Is.EqualTo(-720f).Within(1e-2f));
        m.Tick(T + 500, 0, Owner, objects, r, null, sends, () => 0, null);
        Assert.That(m.Count, Is.EqualTo(0), "the target reached: no hold, dropped");
        Assert.That(sends, Is.Empty);
    }

    [Test]
    public void TheMoveEndHoldKeepsTheMagicPastItsEnd()
    {
        var r = Record();
        var m = Add(r, Paths(Linear + " moveEndHoldDuration=\"200\""));
        var objects = new[] { Me };
        m.Tick(T + 0, 0, Owner, objects, r, null, null, () => 0, null);
        m.Tick(T + 500, 0, Owner, objects, r, null, null, () => 0, null);
        m.Tick(T + 700, 0, Owner, objects, r, null, null, () => 0, null);
        Assert.That(m.Count, Is.EqualTo(1));
        m.Tick(T + 701, 0, Owner, objects, r, null, null, () => 0, null);
        Assert.That(m.Count, Is.EqualTo(0));
    }

    [Test]
    public void TheControlValuesBendTheFlightIntoABezier()
    {
        // controlValue0 = controlValue1: the predicted point 0.8 of the way plus 60 up, whatever the random picks
        var r = Record();
        var m = Add(r, Paths(Linear + " controlValue0=\"0,0.8,60\" controlValue1=\"0,0.8,60\""));
        var objects = new[] { Me };
        m.Tick(T + 0, 0, Owner, objects, r, null, null, () => 0, null);
        m.Tick(T + 225, 0, Owner, objects, r, null, null, () => 0, null);
        var p = m.Flight(0).Current;
        // t 0.5: 0.5 * predicted (0, -648, 60) + 0.25 * target (0, -810, 0)
        Assert.That(p.X, Is.EqualTo(0f).Within(1e-3f));
        Assert.That(p.Y, Is.EqualTo(-526.5f).Within(1e-2f));
        Assert.That(p.Z, Is.EqualTo(30f).Within(1e-3f));
    }

    [Test]
    public void TheFlightRayStopsTheArrowFiveBeforeTheHit()
    {
        var r = Record();
        var m = Add(r, Paths(Linear));
        var objects = new[] { Me };
        MagicRayCast ray = (Vector3 from, Vector3 dir, float len, out Vector3 hit) =>
        {
            hit = new Vector3(0, -100, 0);
            return from.Y - len < -100f;
        };
        m.Tick(T + 0, 0, Owner, objects, r, null, null, () => 0, null, rayCast: ray);
        m.Tick(T + 100, 0, Owner, objects, r, null, null, () => 0, null, rayCast: ray);
        Assert.That(m.Count, Is.EqualTo(0), "the stop ends the flight");
    }

    [Test]
    public void AFlyingArrowIsNotCancelled()
    {
        var r = Record();
        var m = Add(r, Paths(Linear));
        m.Tick(T + 0, 0, Owner, new[] { Me }, r, null, null, () => 0, null);
        Assert.That(m.CancelCaster(r.CastId, T), Is.EqualTo(0), "CanCancel: state 0 or 1 only");
    }

    [Test]
    public void AFlyingArrowTickAllocatesNothing()
    {
        var r = Record();
        var m = Add(r, Paths("vel=\"1\" distance=\"100000\""));
        var objects = new[] { Me, Mob(9, 500, 500) };
        var sends = new List<SkillHitRequest>();
        var pass = new TombTagPass();
        Func<int> rand = () => 0;
        for (int t = 0; t < 50; t++) m.Tick(T + t, 0, Owner, objects, r, pass, sends, rand, null);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int t = 50; t < 1050; t++) m.Tick(T + t, 0, Owner, objects, r, pass, sends, rand, null);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(64));
        Assert.That(m.Count, Is.EqualTo(1));
    }
}
