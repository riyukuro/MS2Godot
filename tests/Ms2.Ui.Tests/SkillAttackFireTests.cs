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

    const string Box = "<range rangeType=\"box\" distance=\"200\" height=\"100\" width=\"100\"/>";

    static HitCandidate Pc(int id, float x, float y) =>
        new() { Id = id, Position = new Vector3(x, y, 0), Body = SkillHit.PlayerBody(new Vector3(x, y, 0), 0) };

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
        var req = SkillAttackFire.Fire(r, 0, Vector3.Zero, 90, 0, new[] { Pc(7, 150, 0), Pc(8, 0, -150), Pc(9, 100, 0) },
            () => 0, () => ids.Next(42), new List<int>());
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
        var req = SkillAttackFire.Fire(r, 0, Vector3.Zero, 0, 0, new[] { Pc(8, 0, -150) }, () => 0, () => ++n, new List<int>());
        Assert.That(req.Targets.Select(t => (t.Serial, t.TargetId)), Is.EqualTo(new[] { (1L, 0) }));
        Assert.That(SkillAttackFire.Fire(r, 1, Vector3.Zero, 0, 0, default, () => 0, () => 0, new List<int>()), Is.Null);
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
        Assert.That((none.CollisionShape, none.CollisionWidth), Is.EqualTo(("", 0f)));
    }
}
