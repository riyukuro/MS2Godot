using System.Numerics;
using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The own cast request path (CKeyTable::GetSkillIdByKey, CSkillSystem::FUN_141764e30 / CreateSkillRecord /
/// FUN_141769b70) and the cast's cooldown key (SkillLevelData::GetMotionKey) over hand-made skill rows.</summary>
public class SkillCastTests
{
    static List<XElement> Levels(XElement e, string name, string attr) => e.Elements(name).ToList();

    static SkillTables Skills(params string[] skills)
    {
        var doc = XDocument.Parse("<ms2>" + string.Concat(skills) + "</ms2>");
        return new SkillTables(JobTable.Empty, new SkillDataTable(() => new[] { doc }, Levels));
    }

    // main type 1 is not a book skill kind, so JobSystemCli gives it level 1 without a book entry
    const string Attack = "<skill id=\"100\"><basic mainType=\"1\"><kinds jump=\"101\"/>"
        + "<stateAttr autoTargeting=\"1\" autoTargetingType=\"2\" moveType=\"1\"/>"
        + "<autoTargeting autoTargetingMaxDegree=\"60\" autoTargetingMaxDistance=\"300\" autoTargetingMaxHeight=\"150\" autoTargetUseMove=\"1\"/>"
        + "</basic><level value=\"1\" sp=\"3\"><motion sequenceName=\"Attack_01\"/><motion sequenceName=\"Attack_02\" sequenceSpeed=\"1.5\" movetype=\"2\"/></level></skill>";
    const string JumpAttack = "<skill id=\"101\"><basic mainType=\"1\"/><level value=\"1\"><motion sequenceName=\"Jump_Attack\"/></level></skill>";
    // a skill that falls back to the normal attack when sp runs short (useDefaultSkill)
    const string Costly = "<skill id=\"300\"><basic mainType=\"1\"><stateAttr useDefaultSkill=\"1\"/></basic>"
        + "<level value=\"1\" sp=\"50\"><motion sequenceName=\"Big\"/></level></skill>";
    // no sequence: the cast reuse window applies
    const string Instant = "<skill id=\"400\"><basic mainType=\"1\"/><level value=\"1\"/></skill>";

    static SlotWorld World(SkillTables t) => new()
    {
        Tables = t, Jobs = new JobSystem(t), Level = 10, Hp = 100, Sp = 10, Ep = 100, Field = new SlotField(),
    };

    static CastRequest Req(uint id, bool seq = true) => new(id, 1, 77, 5000) { HasSequence = seq };

    [Test]
    public void TheCooldownKeyIsTheTargetingKeyCombinedWithTheMotion()
    {
        var lv = Skills(Attack).GetSkillData(100).GetLevelData(1);
        Assert.That(lv.Skill.TargetingKey, Is.EqualTo(60381815085UL),
            "1 + 150<<1 + 300<<11 + 60<<22 + 2<<32 + 1<<34 + 1<<35");
        Assert.That(lv.Motions[0].MoveKey, Is.EqualTo(100UL), "movetype 0, sequenceSpeed 1");
        Assert.That(lv.Motions[1].MoveKey, Is.EqualTo(2198UL), "2 << 10 + 1.5 * 100");
        Assert.That(lv.MotionKey(0), Is.EqualTo(0x389eb5b7c85UL));
        Assert.That(lv.MotionKey(1), Is.EqualTo(0x389eb5b44f7UL));
        Assert.That(lv.MotionKey(2), Is.EqualTo(0UL), "past the level's motions");
    }

    [Test]
    public void TheAttackKeyGivesTheWeaponsNormalAttack()
    {
        var table = WeaponActionTable.Load(XDocument.Parse(
            "<ms2><weapon rh=\"0\" lh=\"0\" normalAttack=\"10\"/><weapon rh=\"52\" lh=\"0\" normalAttack=\"100\"/><weapon rh=\"53\" lh=\"0\" normalAttack=\"0\"/></ms2>"));
        var w = World(Skills(Attack));
        var c = new CastWorld();
        Assert.That(SkillCast.AttackKeySkill(w, c, table.FindAction(52, 0), table, 0), Is.EqualTo(100u));
        Assert.That(SkillCast.AttackKeySkill(w, c, null, table, 0), Is.EqualTo(10u), "no weapon action: FindAction(0, 0)");
        Assert.That(SkillCast.AttackKeySkill(w, c, table.FindAction(53, 0), table, 555), Is.EqualTo(555u), "normalAttack 0: the right-hand item's skill");
        c.StateId = 0x1b;
        Assert.That(SkillCast.AttackKeySkill(w, c, table.FindAction(52, 0), table, 0), Is.EqualTo(0x1312d01u));
        w.IsRiding = true;
        Assert.That(SkillCast.AttackKeySkill(w, c, table.FindAction(52, 0), table, 0), Is.EqualTo(0u), "riding");
    }

    [Test]
    public void ARequestBuildsTheRecordTheCastSends()
    {
        var w = World(Skills(Attack, JumpAttack));
        var c = new CastWorld { Aim = r => { r.Position = new Vector3(1, 2, 3); r.Direction = new Vector3(0, 1, 0); r.Rotation = new Vector3(0, 0, 90); } };
        var res = SkillCast.Request(w, c, Req(100), _ => 100);
        Assert.That(res.Record, Is.Not.Null);
        var r = res.Record;
        Assert.That((r.Skill.Id, r.Level.Level, r.CastId, r.Motion), Is.EqualTo((100u, (ushort)1, 77L, (byte)0)));
        var p = SkillCast.Send(r, 6000, 1234, holdAttackFeature: false);
        var expect = new SkillCastRequest(77, 6000, 100, 1, 0, new Vector3(1, 2, 3), new Vector3(0, 1, 0), new Vector3(0, 0, 90),
            0f, 1234, false, 0, 0x389eb5b7c85, false);
        Assert.That(p, Is.EqualTo(expect));

        c.Jumping = c.Rising = true;
        Assert.That(SkillCast.Request(w, c, Req(100), _ => 100).Record.Skill.Id, Is.EqualTo(101u), "jumping up: the jump skill");
        c.Jumping = c.Rising = false;
        Assert.That(SkillCast.Request(w, c, Req(100, seq: false), _ => 100), Is.EqualTo(new SkillCast.Result(null, 1, null)),
            "b38 must match the level's sequence");
        c.Falling = true;
        var jumpLevel = w.Tables.Skills.GetSkillLevelData(101, 1);
        Assert.That(SkillCast.Check(w, c, jumpLevel, Req(101)), Is.EqualTo(1), "a sequence skill while falling, not jumping up");
        Assert.That(SkillCast.CreateRecord(w, c, Req(101)).Record, Is.Null, "falling casts the jump skill, and 101 has none (0)");
    }

    [Test]
    public void AnUnaffordableSkillFallsBackToTheNormalAttack()
    {
        var w = World(Skills(Attack, Costly));
        var c = new CastWorld();
        var res = SkillCast.Request(w, c, Req(300), _ => 100);
        Assert.That(res.Record?.Skill.Id, Is.EqualTo(100u), "reason 6 (sp) casts the default skill");
        Assert.That(res.Record.Origin, Is.EqualTo(300u));
        Assert.That(res.Record.HasSequence, Is.True);
        w.Sp = 1;
        Assert.That(SkillCast.Request(w, c, Req(300), _ => 100).Record, Is.Null, "the default skill costs 3 sp too");
    }

    [Test]
    public void TheCastReuseWindowBlocksASecondInstantCast()
    {
        var w = World(Skills(Instant));
        var c = new CastWorld { CastReuse = 0.5f, GameTime = 10 };
        var first = SkillCast.Request(w, c, Req(400, seq: false), _ => 0).Record;
        Assert.That(first, Is.Not.Null);
        SkillCast.Casted(c, first);
        Assert.That(SkillCast.Request(w, c, Req(400, seq: false), _ => 0).Why, Is.EqualTo(1), "inside the window");
        c.CastRepeat = 1;
        Assert.That(SkillCast.Request(w, c, Req(400, seq: false), _ => 0).Why, Is.EqualTo(1), "repeat only lets another skill through");
        c.GameTime = 10.6;
        Assert.That(SkillCast.Request(w, c, Req(400, seq: false), _ => 0).Record, Is.Not.Null, "after the window");
    }

    [Test]
    public void ANotLearntSkillIsRefusedWithItsNotice()
    {
        var w = World(Skills(Attack));
        w.Jobs = null;
        Assert.That(SkillCast.Request(w, new CastWorld(), Req(100), _ => 100), Is.EqualTo(new SkillCast.Result(null, 0, "s_err_not_learn_skill")));
    }

    [Test]
    public void TheSkillStateSyncGoesOnItsFirstUpdateThenOnlyWhenSomethingChanged()
    {
        var w = World(Skills(Attack, JumpAttack));
        var c = new CastWorld { Aim = r => { r.Position = new Vector3(1, 2, 3); r.Direction = new Vector3(0, 1, 0); r.Rotation = new Vector3(0, 0, 90); } };
        var r = SkillCast.Request(w, c, Req(100), _ => 100).Record;
        r.TargetDistance = 4.5f;
        var sent = new ChargeSync();
        // StateSkill::vfunc_4 (stateskill.cpp:484-487): the owner's rotation, a zero position: the first update sends (:1275)
        sent.Reset(new Vector3(0, 0, 90));
        var at = new Vector3(1, 2, 3);
        Assert.That(SkillCast.SyncIfChanged(sent, r, at, false, false), Is.EqualTo(new SkillSyncRequest(77, 100, 1, 0,
            new Vector3(1, 2, 3), new Vector3(0, 1, 0), new Vector3(0, 0, 90), at, false, false, 4.5f)));
        Assert.That(SkillCast.SyncIfChanged(sent, r, at, false, false), Is.Null, "nothing changed");
        Assert.That(SkillCast.SyncIfChanged(sent, r, at + Vector3.UnitX, false, false)?.OwnerPosition, Is.EqualTo(at + Vector3.UnitX), "the owner moved");
        Assert.That(SkillCast.SyncIfChanged(sent, r, at + Vector3.UnitX, true, false)?.Charging, Is.True, "charging");
        Assert.That(SkillCast.SyncIfChanged(sent, r, at + Vector3.UnitX, true, true)?.Loop, Is.True, "the loop");
        r.Rotation = new Vector3(0, 0, 45);
        Assert.That(SkillCast.SyncIfChanged(sent, r, at + Vector3.UnitX, true, true)?.Rotation, Is.EqualTo(r.Rotation), "the record turned");
        sent.Reset(new Vector3(0, 0, 45));
        Assert.That(SkillCast.SyncIfChanged(sent, new CastRecord { Skill = r.Skill, Level = r.Level, Rotation = r.Rotation }, Vector3.Zero, false, false),
            Is.Null, "an owner at the origin with the same rotation: the first update sends nothing");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) SkillCast.SyncIfChanged(sent, r, Vector3.Zero, false, false);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(512), "100000 unchanged updates");
    }
}
