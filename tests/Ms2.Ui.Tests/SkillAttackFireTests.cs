using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.World;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// An own cast's attacks: StateSkill::FireAttacksAtPoint @0x1417f0f10, FUN_1417ecce0 @0x1417ecce0 (the damage send),
/// GameObject::GenerateSerialId @0x14123cec0, and the npc body box data (CNpcData::LoadXml, npcdata.cpp:1604).
/// Client space: z up, a caster at rotation z 0 faces -y.
/// </summary>
public class SkillAttackFireTests
{
    static CastRecord Record(string attacks, float rotZ = 0)
    {
        var doc = XDocument.Parse("<ms2><skill id=\"100\"><basic mainType=\"1\"/><level value=\"1\"><motion sequenceName=\"A\">"
            + attacks + "</motion></level></skill></ms2>");
        var t = new SkillDataTable(() => new[] { doc }, (e, n, _) => e.Elements(n).ToList());
        var d = t.GetSkillData(100);
        return new CastRecord
        {
            Skill = d, Level = d.GetLevelData(1), CastId = 0x1122334455L, Motion = 0,
            Position = new Vector3(10, 20, 30), Rotation = new Vector3(0, 0, rotZ),
        };
    }

    const string Box = "<range rangeType=\"box\" distance=\"200\" height=\"100\" width=\"100\" applyTarget=\"1\"/>";

    /// <summary>A hostile npc (team 0, hp 100) with a player-sized body.</summary>
    static HitCandidate Mob(int id, float x, float y) => new()
    {
        Id = id, Position = new Vector3(x, y, 0), Body = SkillHit.PlayerBody(new Vector3(x, y, 0), 0),
        Kind = HitKind.Npc, Team = 0, Hp = 100,
    };

    /// <summary>The caster: a pc of team 1 at the origin.</summary>
    static readonly HitCandidate Owner = new()
    {
        Id = 42, Position = Vector3.Zero, Body = SkillHit.PlayerBody(Vector3.Zero, 0), IsOwner = true, Kind = HitKind.Pc, Team = 1, Hp = 1,
    };

    [Test]
    public void APointFiresTheAttacksInARowThatShareIt()
    {
        var r = Record("<attack point=\"Hit_01\"/><attack point=\"Hit_01\"/><attack point=\"Hit_02\"/><attack/>");
        Assert.That(SkillAttackFire.AttacksAtPoint(r, 0), Is.EqualTo(2));
        Assert.That(SkillAttackFire.AttacksAtPoint(r, 2), Is.EqualTo(1));
        Assert.That(SkillAttackFire.AttacksAtPoint(r, 3), Is.EqualTo(0), "an attack without a point never fires");
        Assert.That(SkillAttackFire.AttacksAtPoint(r, 4), Is.EqualTo(0), "past the motion's attacks");
    }

    [Test]
    public void TheSendCarriesTheRecordAndTheTargetsInTheArea()
    {
        var r = Record("<attack point=\"Hit_01\" targetCount=\"5\">" + Box + "</attack>", rotZ: 90);
        r.SendCount = 3;
        var ids = new SerialIds();
        var req = SkillAttackFire.Fire(r, 0, Vector3.Zero, 90, 0, new[] { Owner, Mob(7, 150, 0), Mob(8, 0, -150), Mob(9, 100, 0) },
            () => 0, () => ids.Next(42), new List<HitCandidate>(), new List<int>());
        Assert.That((req.CastId, req.Attack, req.Position, req.SendCount), Is.EqualTo((0x1122334455L, (byte)0, new Vector3(10, 20, 30), 3)));
        Assert.That(Vector3.Distance(req.Direction, new Vector3(1, 0, 0)), Is.LessThan(1e-5f), "rotation z 90 faces +x");
        Assert.That(req.Targets.Select(t => (t.TargetId, t.Hit)), Is.EqualTo(new[] { (9, (byte)0), (7, (byte)1) }),
            "8 is beside the caster, the rest nearest first");
        Assert.That(req.Targets.Select(t => t.Serial), Is.EqualTo(new[] { (42L << 32) | 1, (42L << 32) | 2 }));
        var bytes = new ByteWriter();
        req.Write(bytes);
        Assert.That(bytes.Length, Is.EqualTo(2 + 8 + 1 + 24 + 1 + 4 + 2 * (8 + 4 + 1 + 1)));
    }

    [Test]
    public void ANonTargetArrowSendsOneTargetlessRecordWithoutAnAreaPick()
    {
        var r = Record("<attack point=\"Hit_01\" targetCount=\"5\">" + Box + "<arrow nonTarget=\"1\"/></attack>");
        long n = 0;
        var req = SkillAttackFire.Fire(r, 0, Vector3.Zero, 0, 0, new[] { Owner, Mob(8, 0, -150) }, () => 0, () => ++n,
            new List<HitCandidate>(), new List<int>());
        Assert.That(req.Targets.Select(t => (t.Serial, t.TargetId)), Is.EqualTo(new[] { (1L, 0) }));
        Assert.That(SkillAttackFire.Fire(r, 1, Vector3.Zero, 0, 0, default, () => 0, () => 0, new List<HitCandidate>(), new List<int>()), Is.Null);
    }

    /// <summary>
    /// The area pick goes through FilterTarget @0x1409ff8d0 (basecollision.cpp:784) with the range's applyTarget (1, hostile):
    /// another player (same team), a friendly npc (team 2), a dead npc without a hitAble corpse and a hostile npc whose class
    /// applyTargetIgnoreNpcRanks lists get no tag; the hostile npc and a dead npc with a hitAble corpse do.
    /// </summary>
    [Test]
    public void ATownAttackTagsOnlyTheHostileTargetsTheFilterPasses()
    {
        var r = Record("<attack point=\"Hit_01\" targetCount=\"8\"><range rangeType=\"box\" distance=\"400\" height=\"100\""
            + " width=\"400\" applyTarget=\"1\" applyTargetIgnoreNpcRanks=\"3,4\"/></attack>");
        Assert.That(r.Skill.GetLevelData(1).Motions[0].Attacks[0].Range.IgnoreNpcRanks, Is.EqualTo((1 << 3) | (1 << 4)));
        var player = Mob(1, 0, -100); player.Kind = HitKind.Pc; player.Team = 1;
        var friendly = Mob(2, 50, -100); friendly.Team = 2;
        var dead = Mob(3, -50, -100); dead.Hp = 0;
        var corpse = Mob(4, -100, -100); corpse.Hp = 0; corpse.CorpseHit = true;
        var boss = Mob(5, 100, -100); boss.NpcClass = 3;
        var mob = Mob(6, 0, -200);
        long n = 0;
        var req = SkillAttackFire.Fire(r, 0, Vector3.Zero, 0, 0, new[] { Owner, player, friendly, dead, corpse, boss, mob },
            () => 0, () => ++n, new List<HitCandidate>(), new List<int>());
        Assert.That(req.Targets.Select(t => t.TargetId).OrderBy(x => x), Is.EqualTo(new[] { 4, 6 }));
    }

    /// <summary>
    /// An attack without a range has no collision (CSkillCollisionManager::GetCached, skillcollisionmanager.cpp:121): FUN_1417e95a0
    /// is 0, PickAttackTargets returns before any record, and the send goes with no target. The record keeps the fired attack
    /// as its m_nAttackIndex (FireCurrentAttack, stateskill.cpp:2264).
    /// </summary>
    [Test]
    public void AnAttackWithoutACollisionSendsNoTarget()
    {
        var r = Record("<attack point=\"Hit_01\" targetCount=\"5\"/><attack point=\"Hit_01\" targetCount=\"5\">" + Box + "</attack>");
        var req = SkillAttackFire.Fire(r, 0, Vector3.Zero, 0, 0, new[] { Owner, Mob(8, 0, -100) }, () => 0, () => 1,
            new List<HitCandidate>(), new List<int>());
        Assert.That((req.Targets.Count, r.AttackIndex), Is.EqualTo((0, 0)));
        req = SkillAttackFire.Fire(r, 1, Vector3.Zero, 0, 0, new[] { Owner, Mob(8, 0, -100) }, () => 0, () => 1,
            new List<HitCandidate>(), new List<int>());
        Assert.That((req.Targets.Count, r.AttackIndex), Is.EqualTo((1, 1)));
    }

    /// <summary>FilterTarget's pet, hidden and PvP cases (basecollision.cpp:784), and flag 2's friendly pick.</summary>
    [Test]
    public void TheFilterRejectsPetsHiddenAndPvPMismatchedTargets()
    {
        var me = Owner;
        var pet = Mob(1, 0, 0); pet.Kind = HitKind.Pet;
        Assert.That((SkillHit.FilterTarget(me, pet, 1, 0), SkillHit.FilterTarget(me, pet, 9, 0)), Is.EqualTo((false, true)));
        pet.PetOwned = true;
        Assert.That(SkillHit.FilterTarget(me, pet, 9, 0), Is.False, "an owned pet");
        var hidden = Mob(2, 0, 0); hidden.Hidden = true;
        Assert.That((SkillHit.FilterTarget(me, hidden, 1, 0), SkillHit.FilterTarget(me, hidden, 5, 0)), Is.EqualTo((false, true)));
        var pvp = Mob(3, 0, 0); pvp.Kind = HitKind.Pc; pvp.Team = 0; pvp.PvPPoint = true;
        Assert.That(SkillHit.FilterTarget(me, pvp, 1, 0), Is.False, "a pc with the other PvP point");
        pvp.PvPPoint = false;
        Assert.That(SkillHit.FilterTarget(me, pvp, 1, 0), Is.True);
        var ally = Mob(4, 0, 0); ally.Kind = HitKind.Pc; ally.Team = 1;
        Assert.That((SkillHit.FilterTarget(me, ally, 1, 0), SkillHit.FilterTarget(me, ally, 2, 0)), Is.EqualTo((false, true)));
        ally.Hp = 0;
        Assert.That(SkillHit.FilterTarget(me, ally, 2, 0), Is.False, "a dead ally");
    }

    [Test]
    public void SerialIdsShareOneCounterAcrossObjects()
    {
        var ids = new SerialIds();
        Assert.That(new[] { ids.Next(5), ids.Next(6), ids.Next(-1) },
            Is.EqualTo(new[] { (5L << 32) | 1, (6L << 32) | 2, unchecked((long)0xFFFFFFFF00000003UL) }));
    }

    [Test]
    public void TheNpcRowReadsItsCollisionBox()
    {
        var row = NpcTable.ParseRow(XElement.Parse("<npc id=\"21000001\"><environment>"
            + "<collision shape=\"box\" width=\"120\" depth=\"80\" height=\"200\"/></environment></npc>"), Array.Empty<EffectDummy>());
        Assert.That((row.CollisionShape, row.CollisionWidth, row.CollisionDepth, row.CollisionHeight), Is.EqualTo(("box", 120f, 80f, 200f)));
        var none = NpcTable.ParseRow(XElement.Parse("<npc id=\"2\"><environment/></npc>"), Array.Empty<EffectDummy>());
        Assert.That((none.CollisionShape, none.CollisionWidth, none.CorpseHitAble), Is.EqualTo(("", 0f, false)));
        var corpse = NpcTable.ParseRow(XElement.Parse("<npc id=\"3\"><environment><corpse hitAble=\"1\"/></environment></npc>"),
            Array.Empty<EffectDummy>());
        Assert.That(corpse.CorpseHitAble, Is.True);
    }
}
