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
    static string Arrow(string arrow, string attack, string children = "") =>
        "<attack point=\"Hit_01\" targetCount=\"1\" attackMethodType=\"1\" magicPathID=\"600\" brokenOffence=\"1\"" + attack + ">"
        + "<range rangeType=\"box\" distance=\"200\" height=\"100\" width=\"100\" applyTarget=\"1\"/>"
        + "<arrow nonTarget=\"1\" rayPhysxTest=\"1\" collision=\"25,100,15\" collisionAdd=\"0,25,15\"" + arrow + "/>" + children + "</attack>";

    static CastRecord Record(string arrow = "", string attack = "", bool nonTarget = true, string children = "", int attackMethodType = 1, bool gms2Rows = false)
    {
        var xml = Arrow(arrow, attack, children);
        if (!nonTarget) xml = xml.Replace("nonTarget=\"1\"", "nonTarget=\"0\"");
        xml = xml.Replace("attackMethodType=\"1\"", "attackMethodType=\"" + attackMethodType + "\"");
        var doc = XDocument.Parse("<ms2><skill id=\"100\"><basic mainType=\"1\"/><level value=\"1\"><motion sequenceName=\"A\">"
            + xml + "</motion></level></skill></ms2>");
        var t = new SkillDataTable(() => new[] { doc }, (e, n, _) => e.Elements(n).ToList(), gms2Rows: gms2Rows);
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

    static readonly HitCandidate Me = MeAt(Vector3.Zero);

    static HitCandidate MeAt(Vector3 p) => new()
    {
        Id = 42, Position = p, Body = SkillHit.PlayerBody(p, 0), IsOwner = true, Kind = HitKind.Pc, Team = 1, Hp = 1,
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
    public void AnArrowAimsAtItsRecordsTargetOffTheFacingLine()
    {
        // UpdateTargetPosition (magicdata.cpp:1459) in FUN_1414609e0 and each flight: GetTargetEntity is the record's target
        // (m_nCasterId), so the arrow flies at the mob 300 to the side of the facing (-y), and the fill (not nonTarget) sends it
        var r = Record(nonTarget: false);
        var m = new OwnTagMagics();
        var objects = new[] { Me, Mob(9, 300, -250) };
        var tags = new List<SkillDamageTarget> { new SkillDamageTarget(7, 9, 0, Array.Empty<SkillDamageLink>()) };
        Assert.That(m.Add(r.Skill, SkillAttackFire.AttackAt(r, 0), r, tags, T, Paths(Linear), _ => Vector3.Zero, ownerId: 42,
            objects: objects), Is.EqualTo(1));
        Assert.That(m.Flight(0).Target.X, Is.EqualTo(objects[1].Body.Center.X).Within(1e-3f), "aimed at the mob from the start");
        var sends = new List<SkillHitRequest>();
        for (int t = 0; t <= 400; t += 100)
            m.Tick(T + t, 0, Owner, objects, r, null, sends, () => 0, null);
        Assert.That(sends.Single().Targets, Is.EqualTo(new[] { new SkillHitTarget(9, 0) }));
    }

    [Test]
    public void AnArrowsHitPlaysAHitSoundWithTheWeaponsMaterialWhenTheAttackHasNone()
    {
        // magicdata.cpp:1955: PlayHitEffects before the send, only for the local owner; the attack without attackMaterial
        // (row +0x1a0 0) takes the worn weapon's, the npc its defence material, at its position and the VolumeMyPcToNpc volume
        var r = Record(nonTarget: false);
        var mob = Mob(9, 0, -300);
        mob.DefenceMaterial = 4;
        var objects = new[] { Me, mob };
        var tags = new List<SkillDamageTarget> { new SkillDamageTarget(7, 9, 0, Array.Empty<SkillDamageLink>()) };
        foreach (bool local in new[] { true, false })
        {
            var m = new OwnTagMagics { HitVolume = 0.7f };
            m.Add(r.Skill, SkillAttackFire.AttackAt(r, 0), r, tags, T, Paths(Linear), _ => Vector3.Zero, ownerId: 42, objects: objects);
            var sounds = new List<MagicHitSound>();
            var owner = new MagicOwner(Vector3.Zero, 0, local, false, false, WeaponMaterial: 5);
            for (int t = 0; t <= 400; t += 100)
                m.Tick(T + t, 0, owner, objects, r, null, new List<SkillHitRequest>(), () => 0, null, hitSounds: sounds);
            if (local) Assert.That(sounds, Is.EqualTo(new[] { new MagicHitSound(5, 4, mob.Position, 0.7f) }));
            else Assert.That(sounds, Is.Empty, "not the local owner: no hit sound here");
        }
    }

    [Test]
    public void AnArrowsAttackMaterialComesFromItsDamage()
    {
        var r = Record(nonTarget: false, children: "<damage attackMaterial=\"2\"/>");
        Assert.That(SkillAttackFire.AttackAt(r, 0).AttackMaterial, Is.EqualTo(2));
    }

    static OwnTagMagics Chain(CastRecord r, HitCandidate[] objects)
    {
        var m = new OwnTagMagics();
        var tags = new List<SkillDamageTarget> { new SkillDamageTarget(7, 9, 0, new[] { new SkillDamageLink(8, 10, 1, 0) }) };
        // a delay keeps the link still in the tick that starts it (it runs right after the record in that tick)
        Assert.That(m.Add(r.Skill, SkillAttackFire.AttackAt(r, 0), r, tags, T, Paths(Linear + " delayTime=\"0.1\""), _ => Vector3.Zero,
            ownerId: 42, objects: objects), Is.EqualTo(2), "the record and its bounce link each get a magic");
        return m;
    }

    [Test]
    public void ABounceLinkWaitsThenFliesFromWhereTheRecordBeforeItEnded()
    {
        // stateskill.cpp:1356: the link's model starts with m_nStartTick 0, so its tick does nothing (magicdata.cpp:758);
        // the record's end (magicdata.cpp:1139) moves it to the end point (FUN_14145ddd0) and starts it then
        var r = Record(nonTarget: false);
        var objects = new[] { Me, Mob(9, 0, -300), Mob(10, 300, -300) };
        var m = Chain(r, objects);
        var sends = new List<SkillHitRequest>();
        Vector3 before = default;
        int t = 0;
        for (; t <= 2000 && m.Count == 2; t += 50)
        {
            before = m.Flight(0).Current;
            m.Tick(T + t, 0, Owner, objects, r, null, sends, () => 0, null);
            if (m.Count == 2)
                Assert.That((m.Flight(1).State, m.Flight(1).Current), Is.EqualTo((0, Vector3.Zero)), "the link waits");
        }
        Assert.That(m.Count, Is.EqualTo(1), "the record ended");
        Assert.That(sends.Select(x => x.Targets.Single().Id), Is.EqualTo(new[] { 9 }));
        var link = m.Flight(0);
        // the record's last tick flew it 90 further (vel 1800, 50 ms) to its hit, where it ended
        Assert.That(Vector3.Distance(link.Current, before), Is.EqualTo(90f).Within(1e-2f), "the link starts at the record's last point");
        Assert.That(link.Current.Y, Is.LessThan(before.Y), "on toward the record's target");
        Assert.That(link.Last, Is.EqualTo(link.Current));
        Assert.That(Vector3.Distance(link.Target, objects[2].Body.Center), Is.LessThan(1e-3f), "aimed at its own target");
        for (; t <= 4000 && m.Count == 1; t += 50)
            m.Tick(T + t, 0, Owner, objects, r, null, sends, () => 0, null);
        Assert.That(m.Count, Is.EqualTo(0));
        Assert.That(sends.Select(x => (x.Serial, x.Targets.Single().Id)), Is.EqualTo(new[] { (7L, 9), (8L, 10) }),
            "the link hits the next target under its own serial");
    }

    [Test]
    public void ABounceLinkWhoseTargetIsGoneNeverStarts()
    {
        var r = Record(nonTarget: false);
        var objects = new[] { Me, Mob(9, 0, -300) };
        var m = Chain(r, objects);
        var sends = new List<SkillHitRequest>();
        for (int t = 0; t <= 2000; t += 50)
            m.Tick(T + t, 0, Owner, objects, r, null, sends, () => 0, null);
        Assert.That(m.Count, Is.EqualTo(0), "FindObjectById finds no target: the link is not started (dropped here)");
        Assert.That(sends.Select(x => x.Targets.Single().Id), Is.EqualTo(new[] { 9 }));
    }

    [Test]
    public void AnArrowWhoseTargetIsGoneFliesAlongTheFacing()
    {
        // GetTargetEntity null (FindObjectById finds nothing): no tracking, the row's direction times distance stays the target
        var r = Record(nonTarget: false);
        var m = new OwnTagMagics();
        var tags = new List<SkillDamageTarget> { new SkillDamageTarget(7, 9, 0, Array.Empty<SkillDamageLink>()) };
        m.Add(r.Skill, SkillAttackFire.AttackAt(r, 0), r, tags, T, Paths(Linear), _ => Vector3.Zero, ownerId: 42,
            objects: new[] { Me });
        Assert.That(Vector3.Distance(m.Flight(0).Target, new Vector3(0, -810, 0)), Is.LessThan(1e-3f));
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

    static HitCandidate Pc(int id, float x, float y, int state = 0, int invincible = 0) => new()
    {
        Id = id, Position = new Vector3(x, y, 0), Body = SkillHit.PlayerBody(new Vector3(x, y, 0), 0),
        Kind = HitKind.Pc, Team = 0, Hp = 100, StateId = state, Invincible = invincible,
    };

    [TestCase(5, false)]
    [TestCase(6, true)]
    public void AStateHitIgnoreArrowSkipsAPcInThatState(int state, bool hit)
    {
        // FUN_141465b30 (magicdata.cpp:1912): nonTarget 1 and the damage's stateHitIgnore (m_n200) drop each PC whose
        // GetCurStateId is that state; none left still sends, with no target
        var r = Record(children: "<damage stateHitIgnore=\"5\"/>");
        var m = Add(r, Paths(Linear));
        var objects = new[] { Me, Pc(9, 0, -250, state) };
        var sends = new List<SkillHitRequest>();
        m.Tick(T + 0, 0, Owner, objects, r, null, sends, () => 0, null);
        m.Tick(T + 100, 0, Owner, objects, r, null, sends, () => 0, null);
        Assert.That(sends.Single().Targets, Is.EqualTo(hit ? new[] { new SkillHitTarget(9, 0) } : Array.Empty<SkillHitTarget>()));
        Assert.That(m.Count, Is.EqualTo(hit ? 0 : 1), "a skipped PC does not end the flight");
    }

    [Test]
    public void AnInvincibleTwoPcTurnsTheArrowAside()
    {
        // FUN_141465b30 (magicdata.cpp:1980): m_n528 == 2 sends the hit, then turns the reverse direction by
        // -1 * (0 * pi/4 + pi/4) (both rands 0), vel 1.2 times, and the flight goes on instead of ending at the hit
        var r = Record();
        var m = Add(r, Paths(Linear));
        var objects = new[] { Me, Pc(9, 0, -250, invincible: 2) };
        var sends = new List<SkillHitRequest>();
        m.Tick(T + 0, 0, Owner, objects, r, null, sends, () => 0, null);
        m.Tick(T + 100, 0, Owner, objects, r, null, sends, () => 0, null);
        Assert.That(sends.Single().Targets, Is.EqualTo(new[] { new SkillHitTarget(9, 0) }));
        Assert.That(m.Count, Is.EqualTo(1), "the bounce keeps the magic");
        var f = m.Flight(0);
        var side = Vector3.Normalize(f.Target - f.Current);
        Assert.That(Math.Abs(side.X), Is.EqualTo(MathF.Sqrt(0.5f)).Within(1e-4f), "45 degrees off the line");
        Assert.That(side.Y, Is.EqualTo(MathF.Sqrt(0.5f)).Within(1e-4f), "back along the reverse direction");
        var before = f.Current;
        m.Tick(T + 150, 0, Owner, objects, r, null, sends, () => 0, null);
        Assert.That(Vector3.Distance(m.Flight(0).Current, before), Is.EqualTo(1800f * 1.2f * 0.05f).Within(0.5f), "vel 1.2 times");
        Assert.That(sends.Count, Is.EqualTo(1), "filled: no second send");
    }

    [Test]
    public void ABounceOffPlaysTheEffectHitThroughTheTargetsThrottle()
    {
        // magicdata.cpp:1990-2008: after the turn, the damage's effectHit at the current point, unturned, when
        // FUN_14146c990 lets it through for the record's target (m_nCasterId): 150 ms since that object's last play and
        // 300 ms since this key's last play on it
        var r = Record(nonTarget: false, children: "<damage effectHit=\"Hit.xml\"/>");
        var m = new OwnTagMagics();
        var objects = new[] { Me, Pc(9, 0, -250, invincible: 2) };
        var effects = new List<MagicEndEffect>();
        int Fire(int start, long serial)
        {
            var tags = new List<SkillDamageTarget> { new SkillDamageTarget(serial, 9, 0, Array.Empty<SkillDamageLink>()) };
            m.Add(r.Skill, SkillAttackFire.AttackAt(r, 0), r, tags, start, Paths(Linear), _ => Vector3.Zero, ownerId: 42,
                objects: objects);
            int before = effects.Count;
            m.Tick(start, 0, Owner, objects, r, null, null, () => 0, null, endEffects: effects);
            m.Tick(start + 100, 0, Owner, objects, r, null, null, () => 0, null, endEffects: effects);
            return effects.Count - before;
        }
        Assert.That(Fire(T, 7), Is.EqualTo(1));
        var e = effects[0];
        Assert.That((e.File, e.Along, e.AxisX, e.AxisY, e.AxisZ), Is.EqualTo(("Hit.xml", false, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ)));
        Assert.That(e.At, Is.EqualTo(m.Flight(m.Count - 1).Current), "at the current point of the turn");
        Assert.That(Fire(T + 200, 8), Is.EqualTo(0), "200 ms after the key's last play on the target");
        Assert.That(Fire(T + 500, 9), Is.EqualTo(1), "500 ms after it");

        var nonTarget = Record(children: "<damage effectHit=\"Hit.xml\"/>");
        var n = Add(nonTarget, Paths(Linear));
        var none = new List<MagicEndEffect>();
        n.Tick(T, 0, Owner, objects, nonTarget, null, null, () => 0, null, endEffects: none);
        n.Tick(T + 100, 0, Owner, objects, nonTarget, null, null, () => 0, null, endEffects: none);
        Assert.That(Math.Abs(n.Flight(0).Target.X), Is.GreaterThan(1f), "it bounced");
        Assert.That(none, Is.Empty, "a record without a target finds no object (FindObjectById(0))");
    }

    [Test]
    public void TheHitEffectThrottleKeepsAnObjectGapAndAKeyGap()
    {
        var t = new HitEffectThrottle();
        Assert.That(t.TryPlay(9, "", 5000), Is.False, "no name, no number");
        Assert.That(t.TryPlay(9, "A", 100), Is.False, "the object's last tick starts at 0");
        Assert.That(t.TryPlay(9, "A", 5000), Is.True);
        Assert.That(t.TryPlay(9, "B", 5149), Is.False, "the object gap");
        Assert.That(t.TryPlay(9, "B", 5150), Is.True, "another key after 150 ms");
        Assert.That(t.TryPlay(9, "A", 5299), Is.False, "the key gap");
        Assert.That(t.TryPlay(9, "A", 5300), Is.True);
        Assert.That(t.TryPlay(10, "A", 5300), Is.True, "another object keeps its own times");
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
    public void ADeadOwnersArrowStillHits()
    {
        // FUN_141465b30 and FUN_1417e9d20 test no death: an arrow in flight when its owner dies still sends its hit
        var r = Record();
        var m = Add(r, Paths(Linear));
        var dead = new MagicOwner(Vector3.Zero, 0, true, true, false);
        var objects = new[] { Me, Mob(9, 0, -250) };
        var sends = new List<SkillHitRequest>();
        m.Tick(T + 0, 0, dead, objects, r, null, sends, () => 0, null);
        m.Tick(T + 100, 0, dead, objects, r, null, sends, () => 0, null);
        Assert.That(sends.Single().Targets, Is.EqualTo(new[] { new SkillHitTarget(9, 0) }));
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
    public void APiercingArrowFliesThroughItsHitsUpToTheBounceCount()
    {
        var r = Record(" bounceType=\"3\" bounceCount=\"2\"");
        var m = Add(r, Paths(Linear));
        var objects = new[] { Me, Mob(9, 0, -250), Mob(10, 0, -450), Mob(11, 0, -650) };
        var sends = new List<SkillHitRequest>();
        for (int t = 0; t <= 400; t += 100) m.Tick(T + t, 0, Owner, objects, r, null, sends, () => 0, null);
        Assert.That(sends.Select(s => s.Targets.Single()), Is.EqualTo(new[] { new SkillHitTarget(9, 0), new SkillHitTarget(10, 1) }),
            "each hit once, the hits before it its index; the third past the bounce count");
        Assert.That(m.Count, Is.EqualTo(1), "a piercing arrow flies on after its last hit");
        Assert.That(m.Flight(0).Current.Y, Is.EqualTo(-720f).Within(1e-2f));
    }

    [Test]
    public void APiercingIntervalForgetsTheHitsAndSendsItsSplash()
    {
        var r = Record(" bounceType=\"3\" bounceCount=\"5\"", " cubeMagicPathID=\"1\"");
        var m = Add(r, Paths("vel=\"100\" distance=\"810\" piercingAttackInterval=\"150\""));
        var objects = new[] { Me, Mob(9, 0, -50) };
        var sends = new List<SkillHitRequest>();
        var splashes = new List<SkillSplashRequest>();
        foreach (int t in new[] { 0, 100, 149 }) m.Tick(T + t, 0, Owner, objects, r, null, sends, () => 0, null, splashes: splashes);
        Assert.That(sends.Select(s => s.Targets.Single()), Is.EqualTo(new[] { new SkillHitTarget(9, 0) }), "hit once, then skipped");
        Assert.That(splashes, Is.Empty);
        m.Tick(T + 150, 0, Owner, objects, r, null, sends, () => 0, null, splashes: splashes);
        Assert.That(sends.Select(s => s.Targets.Single()), Is.EqualTo(new[] { new SkillHitTarget(9, 0), new SkillHitTarget(9, 1) }),
            "the interval forgets the skipped targets: hit again, the hits kept");
        var sp = splashes.Single();
        Assert.That((sp.CastId, sp.Attack, sp.SendCount, sp.Hit), Is.EqualTo((r.CastId, (byte)0, 3, 0)));
        Assert.That(sp.Position.Y, Is.EqualTo(-15f).Within(1e-3f), "the current point");
        Assert.That(sp.Direction.Length(), Is.LessThan(1e-3f), "flying along -y: m_matRotation is the identity");
    }

    [Test]
    public void APiercingIntervalPlaysTheArrowsEffectInvokeAtTheCurrentPoint()
    {
        var r = Record(" bounceType=\"3\" bounceCount=\"5\" effectInvoke=\"invoke.xml\"", "");
        var m = Add(r, Paths("vel=\"100\" distance=\"810\" piercingAttackInterval=\"150\""));
        var objects = new[] { Me, Mob(9, 0, -50) };
        var fx = new List<MagicEndEffect>();
        foreach (int t in new[] { 0, 100, 149 }) m.Tick(T + t, 0, Owner, objects, r, null, null, () => 0, null, endEffects: fx);
        Assert.That(fx, Is.Empty, "no interval yet");
        m.Tick(T + 150, 0, Owner, objects, r, null, null, () => 0, null, endEffects: fx);
        var e = fx.Single();
        Assert.That((e.File, e.Along), Is.EqualTo(("invoke.xml", false)));
        Assert.That(e.At.Y, Is.EqualTo(-15f).Within(1e-3f), "the current point");
        AssertAxes(e, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
    }

    [Test]
    public void ThePiercingMaxTargetCountCapsTheHits()
    {
        var r = Record(" bounceType=\"3\" bounceCount=\"5\"");
        var m = Add(r, Paths(Linear + " piercingAttackMaxTargetCount=\"1\""));
        var objects = new[] { Me, Mob(9, 0, -250), Mob(10, 0, -260), Mob(11, 0, -450) };
        var sends = new List<SkillHitRequest>();
        for (int t = 0; t <= 200; t += 100) m.Tick(T + t, 0, Owner, objects, r, null, sends, () => 0, null);
        Assert.That(sends[0].Targets, Has.Count.EqualTo(1), "two in the box, one let through");
        Assert.That(sends.Skip(1).All(s => s.Targets.Count == 0), "the cap reached: the later sends carry no targets");
    }

    [Test]
    public void AReturningArrowFliesBackToTheOwnerAndHitsAgain()
    {
        var r = Record(" bounceType=\"4\" bounceCount=\"2\"");
        var m = Add(r, Paths(Linear));
        var objects = new[] { Me, Mob(9, 0, -250) };
        var sends = new List<SkillHitRequest>();
        for (int t = 0; t <= 500; t += 50) m.Tick(T + t, 0, Owner, objects, r, null, sends, () => 0, null);
        Assert.That(m.Flight(0).State, Is.EqualTo(5), "the flight's end turns it back");
        Assert.That(sends.Count(s => s.Targets.Count != 0), Is.EqualTo(1));
        for (int t = 550; t <= 850; t += 50) m.Tick(T + t, 0, Owner, objects, r, null, sends, () => 0, null);
        var hits = sends.Where(s => s.Targets.Count != 0).ToList();
        Assert.That(hits.Select(s => s.Targets.Single()), Is.EqualTo(new[] { new SkillHitTarget(9, 0), new SkillHitTarget(9, 0) }),
            "the turn back forgets the hits");
        Assert.That(hits[1].Rotation.Y, Is.EqualTo(-1f).Within(1e-2f), "flying back along +y");
        Assert.That(m.Flight(0).Current.Y, Is.GreaterThan(-200f));
        for (int t = 900; t <= 1100; t += 50) m.Tick(T + t, 0, Owner, objects, r, null, sends, () => 0, null);
        Assert.That(m.Count, Is.EqualTo(0), "at the owner it ends: the hold is not set again");
    }

    [Test]
    public void AReturningArrowFollowsTheOwnerUntilTheTraceDurationRunsOut()
    {
        float XAt(string trace)
        {
            var r = Record(" bounceType=\"4\"");
            var m = Add(r, Paths(Linear + trace));
            for (int t = 0; t <= 500; t += 50) m.Tick(T + t, 0, Owner, new[] { Me }, r, null, null, () => 0, null);
            var moved = new[] { MeAt(new Vector3(300, 0, 0)) };
            for (int t = 550; t <= 650; t += 50) m.Tick(T + t, 0, Owner, moved, r, null, null, () => 0, null);
            return m.Flight(0).Current.X;
        }
        Assert.That(XAt(""), Is.GreaterThan(50f), "the owner moved: the arrow turns after it");
        Assert.That(XAt(" traceTargetDuration=\"0.04\""), Is.EqualTo(0f).Within(1e-3f), "past the trace duration: no turn");
    }

    [Test]
    public void TheMagicPathRowReadsThePiercingAndTraceValues()
    {
        var d = Paths("piercingAttackInterval=\"300\" piercingAttackMaxTargetCount=\"4\" traceTargetDuration=\"1.5\"").Get(600, 0);
        Assert.That((d.PiercingAttackInterval, d.PiercingAttackMaxTargetCount, d.TraceTargetDuration), Is.EqualTo((300u, 4, 1.5f)));
        // explosionByDestroy: XmlGetBool over the CMagicData ctor's 1
        Assert.That((d.ExplosionByDestroy, Paths("explosionByDestroy=\"0\"").Get(600, 0).ExplosionByDestroy,
            Paths("explosionByDestroy=\"1\"").Get(600, 0).ExplosionByDestroy), Is.EqualTo((true, false, true)));
    }

    [TestCase("", true, false)]
    [TestCase("0", false, false)]
    [TestCase("1", true, true)]
    [TestCase("yes", true, true)]
    [TestCase("No", false, false)]
    [TestCase("TRUE1", true, true)]
    [TestCase("falsehood", false, false)]
    [TestCase("2", true, false)]
    [TestCase("on", true, false)]
    public void TheMagicPathFlagsReadAsXmlGetBool(string value, bool explosion, bool ignore)
    {
        // XmlGetBool (tinyxmlhelper.cpp:218): QueryBoolAttribute matches true / yes / 1 and false / no / 0 as prefixes in any
        // case (tinyxml.cpp:620, StringEqual); an empty or other value keeps the CMagicData ctor's value (explosionByDestroy 1,
        // ignorePhysxTestInitPosition and ignoreCancelAtSpawnTime 0)
        var d = Paths($"explosionByDestroy=\"{value}\" ignorePhysxTestInitPosition=\"{value}\" ignoreCancelAtSpawnTime=\"{value}\"").Get(600, 0);
        Assert.That((d.ExplosionByDestroy, d.IgnorePhysxTestInitPosition, d.IgnoreCancelAtSpawnTime), Is.EqualTo((explosion, ignore, ignore)));
    }

    [TestCase("", false, true, true)]
    [TestCase(" explosionByDestroy=\"0\"", false, true, false)]
    [TestCase(" explosionByDestroy=\"0\"", true, true, true)]
    [TestCase("", false, false, false)]
    public void AnArrowsEndSendsItsSplash(string path, bool hit, bool local, bool splash)
    {
        // FUN_14145f570 (magicdata.cpp:1150): explosionByDestroy, else the fill done (m_n1CC) or a ray stop (m_n1C8), an
        // attack with a cubeMagicPathID and FUN_141464410 (the owner the local player) send the splash at the end
        var r = Record(attack: " cubeMagicPathID=\"1\"");
        var m = Add(r, Paths(Linear + path));
        var owner = new MagicOwner(Vector3.Zero, 0, local, false, false);
        var objects = hit ? new[] { Me, Mob(9, 0, -250) } : new[] { Me };
        var splashes = new List<SkillSplashRequest>();
        foreach (int t in new[] { 0, 100, 400, 500 })
            m.Tick(T + t, 0, owner, objects, r, null, null, () => 0, null, splashes: splashes);
        Assert.That(m.Count, Is.EqualTo(0), "ended");
        Assert.That(splashes, Has.Count.EqualTo(splash ? 1 : 0));
        if (!splash) return;
        var sp = splashes[0];
        Assert.That((sp.CastId, sp.Attack, sp.SendCount), Is.EqualTo((r.CastId, (byte)0, 3)));
        Assert.That(sp.Position.Y, Is.EqualTo(hit ? -180f : -810f).Within(1e-2f), "the current point at the end");
    }

    const string EndFx = " explosion=\"1\" effectRemain=\"remain.xml\" effectDestroy=\"destroy.xml\"";
    const string HitFx = "<damage effectHit=\"hit.xml\"/>";

    static List<MagicEndEffect> EndEffects(string arrow, string children, string path, MagicRayCast ray = null,
        MagicOwner? owner = null)
    {
        var r = Record(arrow, children: children);
        var m = Add(r, Paths(path));
        var fx = new List<MagicEndEffect>();
        for (int t = 0; t <= 1000 && m.Count > 0; t += 100)
            m.Tick(T + t, 0, owner ?? Owner, new[] { Me }, r, null, null, () => 0, null, rayCast: ray, endEffects: fx);
        Assert.That(m.Count, Is.EqualTo(0), "ended");
        return fx;
    }

    [Test]
    public void AnArrowsEndPlaysEffectHitThenEffectRemain()
    {
        // FUN_14145f570 (magicdata.cpp:1057): at the end check (m_n52C 0) the arrow's explosion adds the damage's effectHit
        // (kind 1, FUN_141467f60: the y column the direction), then effectRemain (kind 0, the scene's rotation) without a ray stop
        var fx = EndEffects(EndFx, HitFx, Linear);
        Assert.That(fx.Select(f => (f.File, f.Along)), Is.EqualTo(new[] { ("hit.xml", true), ("remain.xml", false) }));
        Assert.That(fx.Select(f => f.At.Y), Is.All.EqualTo(-810f).Within(1e-2f), "at the current point");
        // the flight runs -y: the scene's y column is +y (lookAtType 0, -direction); kind 1's y column is -y (its x column is
        // +-x: the z angle atan2(dir.x, -1) is +-pi with the sign of the turn's float rest in dir.x)
        AssertAxes(fx[1], Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        AssertAxes(fx[0], fx[0].AxisX.X > 0 ? Vector3.UnitX : -Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ);
    }

    [Test]
    public void AnOwnerOtherThanTheLocalPlayerPlaysNoEndEffectsOutsideTheSightRange()
    {
        // ShouldCullByDistance (magicdata.cpp:2263): the end point farther than the range from the local player culls the
        // end effects of an owner whose relation is not 1; the local player's own magic never
        float d = EndEffects(EndFx, HitFx, Linear)[0].At.Length();
        Assert.That(d, Is.EqualTo(810f).Within(1f));
        MagicOwner Other(float? range, Vector3? player) => Owner with { LocalPlayer = false, Sight = new FieldSight(range, player) };
        Assert.That(EndEffects(EndFx, HitFx, Linear, owner: Other(d - 10, Vector3.Zero)), Is.Empty, "farther than the range");
        Assert.That(EndEffects(EndFx, HitFx, Linear, owner: Other(d + 10, Vector3.Zero)), Has.Count.EqualTo(2), "inside the range");
        Assert.That(EndEffects(EndFx, HitFx, Linear, owner: Other(null, Vector3.Zero)), Has.Count.EqualTo(2), "no sight range");
        Assert.That(EndEffects(EndFx, HitFx, Linear, owner: Other(d - 10, null)), Has.Count.EqualTo(2), "no local player");
        Assert.That(EndEffects(EndFx, HitFx, Linear, owner: Owner with { Sight = new FieldSight(d - 10, Vector3.Zero) }),
            Has.Count.EqualTo(2), "the local player's own magic");
    }

    static void AssertAxes(MagicEndEffect f, Vector3 x, Vector3 y, Vector3 z)
    {
        Assert.That(Vector3.Distance(f.AxisX, x), Is.LessThan(1e-4f), $"x column {f.AxisX}");
        Assert.That(Vector3.Distance(f.AxisY, y), Is.LessThan(1e-4f), $"y column {f.AxisY}");
        Assert.That(Vector3.Distance(f.AxisZ, z), Is.LessThan(1e-4f), $"z column {f.AxisZ}");
    }

    [Test]
    public void TheKindOneTransformMirrorsAndLevelsADirectionWithAPositiveZAngle()
    {
        // FUN_141467f60: dir (0.6, 0.48, -0.64): right = dir x Z unit = (0.6247, -0.7809, 0), z angle = atan2(0.6, 0.6247) > 0,
        // so the columns become (-cos z, sin z, 0), (sin z, cos z, 0), Z: the pitch is gone and x points the other way
        var dir = new Vector3(0.6f, 0.48f, -0.64f);
        OwnTagMagics.AlongAxes(dir, out var x, out var y, out var z);
        float h = MathF.Sqrt(0.6f * 0.6f + 0.48f * 0.48f), ang = MathF.Atan2(0.6f, 0.48f / h);
        Assert.That(Vector3.Distance(x, new Vector3(-MathF.Cos(ang), MathF.Sin(ang), 0)), Is.LessThan(1e-5f), $"x {x}");
        Assert.That(Vector3.Distance(y, new Vector3(MathF.Sin(ang), MathF.Cos(ang), 0)), Is.LessThan(1e-5f), $"y {y}");
        Assert.That(z, Is.EqualTo(Vector3.UnitZ));
    }

    [Test]
    public void TheKindOneTransformKeepsThePitchOfANonPositiveZAngle()
    {
        // dir (-0.6, 0.48, -0.64): z angle atan2(-0.6, ...) < 0, the columns stay (right, dir, up) with the pitch
        var dir = new Vector3(-0.6f, 0.48f, -0.64f);
        OwnTagMagics.AlongAxes(dir, out var x, out var y, out var z);
        Assert.That(y, Is.EqualTo(dir));
        var right = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitZ));
        Assert.That(Vector3.Distance(x, right), Is.LessThan(1e-5f), $"x {x}");
        Assert.That(Vector3.Distance(z, Vector3.Normalize(Vector3.Cross(right, dir))), Is.LessThan(1e-5f), $"z {z}");
        OwnTagMagics.AlongAxes(Vector3.Zero, out x, out y, out z);
        Assert.That((x, y, z), Is.EqualTo((Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ)), "a zero direction is identity");
    }

    [Test]
    public void TheLookAtTypeTurnsTheScene()
    {
        // UpdateLookAtRotation: 0 against the direction, 1 the same with z 0 unitized, 2 against the last move
        var dir = Vector3.Normalize(new Vector3(0, -1, -1));
        var cur = new Vector3(10, 0, 0);
        var last = new Vector3(0, 0, 0);
        OwnTagMagics.LookAt(0, dir, cur, last, Vector3.UnitY, out _, out var y, out _);
        Assert.That(y, Is.EqualTo(-dir));
        OwnTagMagics.LookAt(1, dir, cur, last, Vector3.UnitY, out var x, out y, out var z);
        Assert.That(Vector3.Distance(y, Vector3.UnitY), Is.LessThan(1e-6f), $"flattened {y}");
        Assert.That(Vector3.Distance(x, Vector3.UnitX), Is.LessThan(1e-6f), $"right {x}");
        Assert.That(Vector3.Distance(z, Vector3.UnitZ), Is.LessThan(1e-6f), $"up {z}");
        OwnTagMagics.LookAt(2, dir, cur, last, Vector3.UnitY, out _, out y, out _);
        Assert.That(y, Is.EqualTo(-Vector3.UnitX), "against the move");
        OwnTagMagics.LookAt(1, Vector3.UnitZ, cur, last, new Vector3(0, 1, 0), out _, out y, out _);
        Assert.That(y, Is.EqualTo(Vector3.UnitY), "a flattened zero takes the record's direction");
    }

    [TestCase("", 0.70710677f)]
    [TestCase(" lookAtType=\"1\"", 0f)]
    public void ARemainEffectTakesTheScenesLookAtRotation(string lookAt, float upZ)
    {
        // a flight down and along -y: lookAtType 0 keeps the scene's pitch (y column -direction), lookAtType 1 levels it
        var fx = EndEffects(EndFx, "", "vel=\"1800\" distance=\"810\" direction=\"0,1,-1\"" + lookAt);
        Assert.That(fx.Select(f => f.File), Is.EqualTo(new[] { "remain.xml" }));
        Assert.That(fx[0].AxisY.Z, Is.EqualTo(upZ).Within(1e-4f), $"y column {fx[0].AxisY}");
    }

    [Test]
    public void ARayStoppedArrowPlaysEffectDestroy()
    {
        // m_n1C8 (the ray stop) picks effectDestroy, kind 1, instead of effectRemain
        MagicRayCast ray = (Vector3 from, Vector3 dir, float len, out Vector3 hit) =>
        {
            hit = new Vector3(0, -100, 0);
            return from.Y - len < -100f;
        };
        var fx = EndEffects(EndFx, HitFx, Linear, ray);
        Assert.That(fx.Select(f => (f.File, f.Along)), Is.EqualTo(new[] { ("hit.xml", true), ("destroy.xml", true) }));
        Assert.That(fx[1].At.Y, Is.EqualTo(-95f).Within(1e-2f), "five before the ray's hit");
    }

    [TestCase(EndFx, HitFx, new[] { "hit.xml", "remain.xml" })]
    [TestCase(" effectRemain=\"remain.xml\"", HitFx, new[] { "remain.xml" })]
    [TestCase(" explosion=\"1\"", HitFx, new[] { "hit.xml" })]
    [TestCase(EndFx, "", new[] { "remain.xml" })]
    public void AnArrowsLifeTimeEndPlaysEffectHitAndEffectRemain(string arrow, string children, string[] files)
    {
        // the lifeTime's end (m_n52C 1): effectHit with the explosion, then effectRemain (kind 0); an empty name inserts nothing
        var fx = EndEffects(arrow, children, "vel=\"1\" distance=\"100000\" lifeTime=\"0.2\"");
        Assert.That(fx.Select(f => f.File), Is.EqualTo(files));
        Assert.That(fx.Where(f => f.File == "remain.xml").Select(f => f.Along), Is.All.False);
    }

    [Test]
    public void AnArrowWithoutEndEffectsPlaysNone()
    {
        Assert.That(EndEffects("", HitFx, Linear), Is.Empty, "effectHit needs the arrow's explosion");
        Assert.That(EndEffects(" effectDestroy=\"destroy.xml\"", "", Linear), Is.Empty, "effectDestroy needs a ray stop");
    }

    [Test]
    public void AnArrowThatBouncedOffDoesNotTurnBack()
    {
        // FUN_141463fb0 (magicdata.cpp:2238): bounceType 4 turns back only when m_n528 is not 2
        var r = Record(" bounceType=\"4\" bounceCount=\"2\"");
        var m = Add(r, Paths(Linear));
        var objects = new[] { Me, Pc(9, 0, -250, invincible: 2) };
        for (int t = 0; t <= 600 && m.Count > 0; t += 50)
        {
            m.Tick(T + t, 0, Owner, objects, r, null, null, () => 0, null);
            if (m.Count > 0) Assert.That(m.Flight(0).State, Is.EqualTo(2), "never state 5");
        }
        Assert.That(m.Count, Is.EqualTo(0), "the end at the bounce's target");
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
    [Test]
    public void TheArrowShowsItsEffectOnItsSceneFromItsStartToItsEnd()
    {
        // SkillArrow::Load "effect" (skillattackdata.cpp:114); CMagic::FUN_14145e960 (magicdata.cpp:914-935) keeps it on the
        // magic scene from state 0 (start + delay), the scene at the current point (SetPosition :2194-2199); it goes with the magic
        var r = Record(" effect=\"CH/Ball.xml\"");
        Assert.That(SkillAttackFire.AttackAt(r, 0).Arrow.Effect, Is.EqualTo("CH/Ball.xml"));
        var m = Add(r, Paths("vel=\"1000\" distance=\"300\" delayTime=\"0.1\""));
        var objects = new[] { Me };
        var fx = new List<MagicSceneEffect>();
        m.Tick(T + 50, 0, Owner, objects, r, null, new List<SkillHitRequest>(), () => 0, null);
        m.SceneEffects(fx);
        Assert.That(fx, Is.Empty, "before the delay");
        m.Tick(T + 100, 0, Owner, objects, r, null, new List<SkillHitRequest>(), () => 0, null);
        m.Tick(T + 150, 0, Owner, objects, r, null, new List<SkillHitRequest>(), () => 0, null);
        m.SceneEffects(fx);
        Assert.That(fx.Count, Is.EqualTo(1));
        Assert.That((fx[0].File, fx[0].At), Is.EqualTo(("CH/Ball.xml", m.Flight(0).Current)));
        Assert.That(fx[0].At.Y, Is.LessThan(0f), "flown along the facing (-y)");
        int key = fx[0].Key;
        fx.Clear();
        m.Tick(T + 200, 0, Owner, objects, r, null, new List<SkillHitRequest>(), () => 0, null);
        m.SceneEffects(fx);
        Assert.That((fx[0].Key, fx[0].At.Y < -40f), Is.EqualTo((key, true)), "the same magic, further on");
        for (int t = 250; t <= 1000; t += 50) m.Tick(T + t, 0, Owner, objects, r, null, new List<SkillHitRequest>(), () => 0, null);
        Assert.That(m.Count, Is.Zero);
        fx.Clear();
        m.SceneEffects(fx);
        Assert.That(fx, Is.Empty, "gone with the magic");
    }

    [Test]
    public void AGms2ThrownAttacksMagicShowsItsArrowEffect()
    {
        // GMS2 SkillTargetRecord::GetArrowEffect (0x140c356c0) asks the level for the attack's effect string kind 2 (0x140467330,
        // attack +0x148) with no attackMethodType test, and CMagic 0x140aed630 shows it as KMS FUN_14145e960 does
        var r = Record(" effect=\"CH/Star.xml\"", attackMethodType: 0, gms2Rows: true);
        var a = SkillAttackFire.AttackAt(r, 0);
        Assert.That((a.AttackMethodType, a.ArrowRecord), Is.EqualTo((0, true)));
        var m = Add(r, Paths(Linear));
        m.Tick(T + 50, 0, Owner, new[] { Me }, r, null, new List<SkillHitRequest>(), () => 0, null);
        var fx = new List<MagicSceneEffect>();
        m.SceneEffects(fx);
        Assert.That((fx.Count, fx[0].File), Is.EqualTo((1, "CH/Star.xml")));
    }

    [Test]
    public void OnlyAGms2RowGivesTheArrowEffectOfAnAttackNotOfType1()
    {
        // KMS SkillTargetRecord::GetArrowEffect (skillrecord.cpp:119) gives none unless attackMethodType is 1 (548 skilldata
        // attacks of type 2 carry an effect); the GMS2 one (@0x140c356c0) has no type test
        var kms = SkillAttackFire.AttackAt(Record(" effect=\"CH/Star.xml\"", attackMethodType: 2), 0);
        Assert.That((kms.ArrowRecord, kms.Arrow.Effect, OwnTagMagics.MagicEffect(kms)), Is.EqualTo((false, "CH/Star.xml", "")));
        var gms = SkillAttackFire.AttackAt(Record(" effect=\"CH/Star.xml\"", attackMethodType: 2, gms2Rows: true), 0);
        Assert.That((gms.ArrowRecord, OwnTagMagics.MagicEffect(gms)), Is.EqualTo((true, "CH/Star.xml")));
        var bow = SkillAttackFire.AttackAt(Record(" effect=\"CH/Ball.xml\""), 0);
        Assert.That(OwnTagMagics.MagicEffect(bow), Is.EqualTo("CH/Ball.xml"));
    }

    [Test]
    public void AnArrowWithoutAnEffectShowsNone()
    {
        var r = Record();
        var m = Add(r, Paths(Linear));
        m.Tick(T + 50, 0, Owner, new[] { Me }, r, null, new List<SkillHitRequest>(), () => 0, null);
        var fx = new List<MagicSceneEffect>();
        m.SceneEffects(fx);
        Assert.That((m.Count, fx.Count), Is.EqualTo((1, 0)));
    }

    [Test]
    public void TheSceneEffectListAllocatesNothing()
    {
        var r = Record(" effect=\"CH/Ball.xml\"");
        var m = Add(r, Paths("vel=\"1\" distance=\"100000\""));
        m.Tick(T + 50, 0, Owner, new[] { Me }, r, null, new List<SkillHitRequest>(), () => 0, null);
        var fx = new List<MagicSceneEffect>(4);
        m.SceneEffects(fx);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { fx.Clear(); m.SceneEffects(fx); }
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        Assert.That(fx.Count, Is.EqualTo(1));
    }

    [Test]
    public void TheDataNamesTheStarsBall()
    {
        // skill 900021 (the assassin's star attack) attack 0: arrow effect="CH/Weapon/Star/Eff_Star_Attack01_Ball_01.xml"
        string data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set (skill data)");
        var s = new GameTables(Ms2.Formats.Pack.PackSet.Shared(data), "en").Skills.GetSkillData(900021);
        Assert.That(s?.GetLevelData(1)?.Motions[0].Attacks[0].Arrow.Effect, Is.EqualTo("CH/Weapon/Star/Eff_Star_Attack01_Ball_01.xml"));
    }
}
