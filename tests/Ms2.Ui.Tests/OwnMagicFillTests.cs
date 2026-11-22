using System.Numerics;
using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// An own magic's region fill and its send (CMagic::FUN_141464d00, magicdata.cpp:1750-1862), its start point
/// (CMagic::FUN_1414609e0, :1397) and GameObject::DirectionToYawDegrees. Client space: z up, rotation z 0 faces -y.
/// </summary>
public class OwnMagicFillTests
{
    static CastRecord Record(string attacks)
    {
        var doc = XDocument.Parse("<ms2><skill id=\"100\"><basic mainType=\"1\"/><level value=\"1\"><motion sequenceName=\"A\">"
            + attacks + "</motion></level></skill></ms2>");
        var t = new SkillDataTable(() => new[] { doc }, (e, n, _) => e.Elements(n).ToList());
        var d = t.GetSkillData(100);
        return new CastRecord
        {
            Skill = d, Level = d.GetLevelData(1), CastId = 0x1122334455L, Motion = 0, SendCount = 3,
            Position = new Vector3(10, 20, 30),
        };
    }

    const string Box = "<range rangeType=\"box\" distance=\"200\" height=\"100\" width=\"100\" applyTarget=\"1\"/>";

    static HitCandidate Mob(int id, float x, float y) => new()
    {
        Id = id, Position = new Vector3(x, y, 0), Body = SkillHit.PlayerBody(new Vector3(x, y, 0), 0),
        Kind = HitKind.Npc, Team = 0, Hp = 100,
    };

    static HitCandidate Pc(int id, float x, float y, long party) => new()
    {
        Id = id, Position = new Vector3(x, y, 0), Body = SkillHit.PlayerBody(new Vector3(x, y, 0), 0),
        Kind = HitKind.Pc, Team = 1, Hp = 1, PartyId = party,
    };

    static HitCandidate Me(long party = 0) => new()
    {
        Id = 42, Position = Vector3.Zero, Body = SkillHit.PlayerBody(Vector3.Zero, 0), IsOwner = true, Kind = HitKind.Pc, Team = 1, Hp = 1,
        PartyId = party,
    };

    static readonly MagicOwner Owner = new(Vector3.Zero, 0, true, false, false);

    static List<SkillDamageTarget> Tags(params int[] targets) =>
        targets.Select((id, i) => new SkillDamageTarget(100 + i, id, (byte)i, Array.Empty<SkillDamageLink>())).ToList();

    static List<SkillHitRequest> Run(CastRecord r, IReadOnlyList<SkillDamageTarget> tags, HitCandidate[] objects,
        MagicRayBlocked ray = null, OwnTagMagics m = null)
    {
        m ??= new OwnTagMagics();
        m.Add(r.Skill, SkillAttackFire.AttackAt(r, 0), r, tags, 0, MagicPathTable.Empty, null);
        var sends = new List<SkillHitRequest>();
        m.Tick(0, 0, Owner, objects, r, null, sends, () => 0, ray);
        return sends;
    }

    [Test]
    public void ARegionFillPlaysAHitSoundPerHitButTheOwnPcBeforeItsSend()
    {
        // CMagic::PlayHitEffects (magicdata.cpp:1697) from FUN_141464d00 (:1838): the attack's attackMaterial; an npc with its
        // defence material, another PC with 1, the own PC (MyPC) none; nonTarget 1 so the whole region is sent
        var r = Record("<attack point=\"Hit_01\" targetCount=\"5\" brokenOffence=\"1\">" + Box
            + "<arrow nonTarget=\"1\"/><damage attackMaterial=\"3\"/></attack>");
        var mob = Mob(9, 0, -100);
        mob.DefenceMaterial = 6;
        // pkMode: another PC out of the party is hostile
        var pc = Pc(11, 0, -150, 0);
        var objects = new[] { Me(), mob, pc };
        var m = new OwnTagMagics();
        m.Add(r.Skill, SkillAttackFire.AttackAt(r, 0), r, Tags(9), 0, MagicPathTable.Empty, null);
        var sends = new List<SkillHitRequest>();
        var sounds = new List<MagicHitSound>();
        m.Tick(0, 0, new MagicOwner(Vector3.Zero, 0, true, false, false, WeaponMaterial: 8), objects, r, null, sends, () => 0, null,
            pkMode: true, hitSounds: sounds);
        Assert.That(sends.Single().Targets.Select(t => t.Id), Is.SupersetOf(new[] { 9, 11 }));
        Assert.That(sounds, Is.EquivalentTo(new[] { new MagicHitSound(3, 6, mob.Position), new MagicHitSound(3, 1, objects[2].Position) }));
    }

    [Test]
    public void ATargetedNormalAttackSendsModeOneOnePerTargetRecord()
    {
        // no brokenOffence, a targeted skill: the attack's send (1/0) then each record's magic fills and sends 1/1
        var r = Record("<attack point=\"Hit_01\" targetCount=\"5\">" + Box + "</attack>");
        var objects = new[] { Me(), Mob(7, 0, -150), Mob(9, 0, -100) };
        long n = 0;
        var damage = SkillAttackFire.Fire(r, 0, Vector3.Zero, 0, 0, objects, () => 0, () => ++n, new List<HitCandidate>(), new List<int>());
        Assert.That(damage.Targets.Select(t => t.TargetId), Is.EqualTo(new[] { 9, 7 }));
        var m = new OwnTagMagics();
        Assert.That(m.Add(r.Skill, SkillAttackFire.AttackAt(r, 0), r, damage.Targets, 0, MagicPathTable.Empty, null), Is.EqualTo(2));
        var sends = new List<SkillHitRequest>();
        m.Tick(0, 0, Owner, objects, r, new TombTagPass(), sends, () => 0, null);
        Assert.That(sends.Select(s => (s.Serial, s.Targets.Single())),
            Is.EqualTo(new[] { (1L, new SkillHitTarget(9, 0)), (2L, new SkillHitTarget(7, 0)) }),
            "each record's own target, hit byte 0, in the target records' order");
        foreach (var s in sends)
        {
            Assert.That((s.CastId, s.Attack, s.SendCount, s.Start, s.OwnerPosition, s.Rotation),
                Is.EqualTo((0x1122334455L, (byte)0, 3, new Vector3(10, 20, 30), Vector3.Zero, Vector3.Zero)));
            var w = new Maple2.PacketLib.Tools.ByteWriter();
            s.Write(w);
            Assert.That(w.Buffer.AsSpan(0, 2).ToArray(), Is.EqualTo(new byte[] { 1, 1 }));
        }
        Assert.That(m.Count, Is.EqualTo(0));
    }

    [Test]
    public void ATargetedFillWithoutItsTargetSendsNothing()
    {
        var r = Record("<attack point=\"Hit_01\" targetCount=\"5\">" + Box + "</attack>");
        Assert.That(Run(r, Tags(9), new[] { Me(), Mob(7, 0, -150) }), Is.Empty, "the record's target left the area");
        Assert.That(Run(r, Tags(9), new[] { Me(), Mob(9, 0, 300) }), Is.Empty, "no hit");
    }

    [Test]
    public void ANonTargetArrowSendsTheHitsUpToTheTargetCount()
    {
        var r = Record("<attack point=\"Hit_01\" targetCount=\"2\">" + Box + "<arrow nonTarget=\"1\"/></attack>");
        var s = Run(r, Tags(0), new[] { Me(), Mob(7, 0, -150), Mob(8, 0, -120), Mob(9, 0, -100) }).Single();
        Assert.That(s.Targets, Is.EqualTo(new[] { new SkillHitTarget(9, 0), new SkillHitTarget(8, 0) }));
        Assert.That(s.Serial, Is.EqualTo(100));
    }

    [Test]
    public void TheRayTestDropsABlockedHit()
    {
        var r = Record("<attack point=\"Hit_01\" targetCount=\"5\">" + Box + "<arrow nonTarget=\"1\" rayPhysxTest=\"1\"/></attack>");
        var rays = new List<(Vector3, Vector3, float)>();
        var s = Run(r, Tags(0), new[] { Me(), Mob(7, 0, -150), Mob(9, 0, -100) },
            (from, dir, len) => { rays.Add((from, dir, len)); return len > 120; }).Single();
        Assert.That(s.Targets, Is.EqualTo(new[] { new SkillHitTarget(9, 0) }));
        Assert.That(rays, Is.EqualTo(new[] { (Vector3.Zero, new Vector3(0, -1, 0), 100f), (Vector3.Zero, new Vector3(0, -1, 0), 150f) }));
        Assert.That(Run(r, Tags(0), new[] { Me(), Mob(7, 0, -150) }, (_, _, _) => true), Is.Empty, "all blocked: no hits, no send");
    }

    [Test]
    public void APointGroupSkipsTheTargetsItAlreadyHit()
    {
        var r = Record("<attack point=\"Hit_01\" targetCount=\"5\" pointGroupID=\"3\">" + Box + "<arrow nonTarget=\"1\"/></attack>");
        var objects = new[] { Me(), Mob(7, 0, -150), Mob(9, 0, -100) };
        Assert.That(Run(r, Tags(0), objects).Single().Targets.Count, Is.EqualTo(2));
        Assert.That((r.HasTarget(3, 7), r.HasTarget(3, 9), r.HasTarget(4, 7)), Is.EqualTo((true, true, false)));
        Assert.That(Run(r, Tags(0), objects), Is.Empty, "the group's targets dropped: none left");
        var other = Record("<attack point=\"Hit_01\" targetCount=\"5\" pointGroupID=\"3\">" + Box + "<arrow nonTarget=\"1\"/></attack>");
        other.CastId = 5;
        var m = new OwnTagMagics();
        m.Add(r.Skill, SkillAttackFire.AttackAt(r, 0), r, Tags(0), 0, MagicPathTable.Empty, null);
        var sends = new List<SkillHitRequest>();
        m.Tick(0, 0, Owner, objects, other, null, sends, () => 0, null);
        Assert.That(sends.Single().Targets.Count, Is.EqualTo(2), "another cast's record is not the magic's: no group");
    }

    [Test]
    public void AFriendlyAttackPutsThePartyFirst()
    {
        const string friendly = "<range rangeType=\"box\" distance=\"200\" height=\"100\" width=\"100\" applyTarget=\"2\"/>";
        var r = Record("<attack point=\"Hit_01\" targetCount=\"5\">" + friendly + "<arrow nonTarget=\"1\"/></attack>");
        var objects = new[] { Me(party: 9), Pc(5, 0, -100, 0), Pc(6, 0, -150, 9), Pc(4, 0, -120, 0) };
        Assert.That(Run(r, Tags(0), objects).Single().Targets.Select(t => t.Id), Is.EqualTo(new[] { 6, 5, 4 }));
        objects[0] = Me();
        Assert.That(Run(r, Tags(0), objects).Single().Targets.Select(t => t.Id), Is.EqualTo(new[] { 5, 4, 6 }), "not in a party");
    }

    [Test]
    public void TheStartIsTheNodeOrTheFixedPointPlusTheTurnedOffset()
    {
        var forward = new Vector3(1, 0, 0);   // rotation z 90
        var node = new Vector3(1, 2, 3);
        Assert.That(OwnTagMagics.StartPoint(new MagicPathMove(0, 0, 0, 0), forward, node), Is.EqualTo(node));
        Assert.That(OwnTagMagics.StartPoint(new MagicPathMove(0, 0, 0, 0, FireFixed: new Vector3(7, 8, 9)), forward, node),
            Is.EqualTo(new Vector3(7, 8, 9)));
        // rotation 1: the offset turned by yaw - 180 + dirRot counter-clockwise: (0, 10) at yaw 90 goes to (10, 0)
        var p = OwnTagMagics.StartPoint(new MagicPathMove(0, 0, 0, 0, FireOffset: new Vector3(0, 10, 5)), forward, node);
        Assert.That(Vector3.Distance(p, new Vector3(11, 2, 8)), Is.LessThan(1e-4f));
        // rotation 0: only dirRotZDegree, 90 turns (10, 0) to (0, 10)
        p = OwnTagMagics.StartPoint(new MagicPathMove(0, 0, 0, 0, FireOffset: new Vector3(10, 0, 0), Rotation: 0, DirRotZ: 90), forward, node);
        Assert.That(Vector3.Distance(p, new Vector3(1, 12, 3)), Is.LessThan(1e-4f));
    }

    [Test]
    public void TheYawIsTheFacingInWholeDegrees()
    {
        Assert.That(OwnTagMagics.YawDegrees(new Vector3(0, -1, 0)), Is.EqualTo(0f));
        Assert.That(OwnTagMagics.YawDegrees(new Vector3(1, 0, 0)), Is.EqualTo(90f));
        Assert.That(OwnTagMagics.YawDegrees(new Vector3(0, 1, 0)), Is.EqualTo(180f));
        Assert.That(OwnTagMagics.YawDegrees(new Vector3(-1, 0, 5)), Is.EqualTo(270f));
        SkillHit.Axes(33.7f, out var f, out _);
        Assert.That(OwnTagMagics.YawDegrees(f), Is.EqualTo(33f), "the int part");
    }

    [Test]
    public void TheMagicPathsReadTheFireOffsetAndRotation()
    {
        var paths = new MagicPathTable(XDocument.Parse("<ms2><type id=\"1\"><move fireOffsetPosition=\"1,2,3\" dirRotZDegree=\"45\"/>"
            + "<move rotation=\"0\" fireFixedPosition=\"1,2\"/></type></ms2>"));
        Assert.That(paths.Get(1, 0), Is.EqualTo(new MagicPathMove(0, 0, 0, 0, FireOffset: new Vector3(1, 2, 3), DirRotZ: 45)));
        Assert.That(paths.Get(1, 1), Is.EqualTo(new MagicPathMove(0, 0, 0, 0, Rotation: 0)), "two floats are not a point");
    }
}
