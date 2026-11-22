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

    // continue skills (kinds continue): moveType 0 and 2
    const string Continue0 = "<skill id=\"500\"><basic mainType=\"1\"><kinds continue=\"1\"/></basic><level value=\"1\"><motion sequenceName=\"Spin\"/></level></skill>";
    const string Continue2 = "<skill id=\"501\"><basic mainType=\"1\"><kinds continue=\"1\"/><stateAttr moveType=\"2\"/></basic><level value=\"1\"><motion sequenceName=\"Spin\"/></level></skill>";

    static SlotWorld World(SkillTables t) => new()
    {
        Tables = t, Jobs = new JobSystem(t), Level = 10, Hp = 100, Sp = 10, Ep = 100, Field = new SlotField(),
    };

    static CastRequest Req(uint id, bool seq = true) => new(id, 1, 77, 5000) { HasSequence = seq };

    /// <summary>A double-clicked bag item's skill (inventory.cpp:1379-1397) is CreateSkillRecord only: a refusal gives nothing
    /// where the quick slot chain (FUN_141764e30) casts the useDefaultSkill fallback; a cast carries the item uid at level 1.</summary>
    [Test]
    public void ABagItemSkillHasNoDefaultSkillFallback()
    {
        var w = World(Skills(Attack, JumpAttack, Costly));
        var c = new CastWorld();
        Assert.That(SkillCast.Request(w, c, Req(300), _ => 100).Record?.Skill.Id, Is.EqualTo(100u), "the quick slot chain falls back");
        var res = SkillCast.ItemRequest(w, c, new CastRequest(0, 0, 78, 5000), 300, 0x99);
        Assert.That(res.Record, Is.Null);
        Assert.That(res.Why, Is.Not.Zero, "the sp refusal itself");

        var r = SkillCast.ItemRequest(w, c, new CastRequest(0, 0, 79, 5000), 100, 0x99).Record;
        Assert.That(r, Is.Not.Null);
        Assert.That((r.Skill.Id, r.Level.Level, r.ItemUid), Is.EqualTo((100u, (ushort)1, 0x99L)));
        Assert.That(SkillCast.ItemRequest(w, c, new CastRequest(0, 0, 80, 5000), 999, 0x99).Record, Is.Null, "no level 1 row");
    }

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

    /// <summary>A skill press while the own player is dead (DeadUser set the flag) gives no record, so nothing is sent: FUN_14176a380
    /// refuses with 0xc on PCBase::IsDead. A world taken while alive is brought up to date at the request.</summary>
    [Test]
    public void APressWhileDeadGivesNoRecord()
    {
        var w = World(Skills(Attack));
        var c = new CastWorld();
        var me = new MyCharacter();
        SkillCast.OwnDead(w, me);
        Assert.That(SkillCast.Request(w, c, Req(100), _ => 100).Record, Is.Not.Null, "alive");
        me.Dead = true;
        SkillCast.OwnDead(w, me);
        Assert.That(SkillCast.Request(w, c, Req(100), _ => 100), Is.EqualTo(new SkillCast.Result(null, 0xc, null)), "dead");
        me.Dead = false;
        SkillCast.OwnDead(w, me);
        Assert.That(SkillCast.Request(w, c, Req(100), _ => 100).Record, Is.Not.Null, "revived");
    }

    /// <summary>CSkillSystem::FUN_141764e30's quick slot item (skillslot.cpp:2093-2103): the key's item uid goes on the record;
    /// an item the bag lacks gives nothing, one above the own level the s_err_not_enough_level notice.</summary>
    [Test]
    public void AKeysQuickSlotItemGoesOnTheRecord()
    {
        var w = World(Skills(Attack));
        var c = new CastWorld();
        bool? atLevel = true;
        var req = Req(100);
        req.Key = 3;
        Assert.That(SkillCast.Request(w, c, req, _ => 100, k => k == 3 ? 0x55 : 0, _ => atLevel).Record.ItemUid, Is.EqualTo(0x55));
        atLevel = false;
        Assert.That(SkillCast.Request(w, c, Req(100), _ => 100, _ => 0x55, _ => atLevel),
            Is.EqualTo(new SkillCast.Result(null, 0, "s_err_not_enough_level")));
        atLevel = null;
        Assert.That(SkillCast.Request(w, c, Req(100), _ => 100, _ => 0x55, _ => atLevel), Is.EqualTo(new SkillCast.Result(null, 0, null)));
        Assert.That(SkillCast.Request(w, c, Req(100), _ => 100, _ => 0, _ => atLevel).Record.ItemUid, Is.Zero, "no item on the key");
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

    // main type 11: action kind 0x1c, a state skill; main type 14: an emotion
    const string StateKind = "<skill id=\"600\"><basic mainType=\"11\"/><level value=\"2\"/></skill>";
    const string Emotion = "<skill id=\"601\"><basic mainType=\"14\"/><level value=\"1\"/></skill>";
    // main type 7: action kind 0x1e (glide); main type 10: 0x26 (ride / pet summon)
    const string Glide = "<skill id=\"602\"><basic mainType=\"7\"/><level value=\"1\"/></skill>";
    const string Ridee = "<skill id=\"603\"><basic mainType=\"10\"/><level value=\"1\"/></skill>";

    [Test]
    public void AStateSkillGoesAsTheStateSkillSendWithItsRecordedTick()
    {
        var t = Skills(Attack, StateKind, Emotion);
        CastRecord Rec(uint id, ushort lv) => new()
        {
            Skill = t.Skills.GetSkillData(id), Level = t.Skills.GetSkillLevelData(id, lv), Tick = 4321, CastId = 88, ItemUid = 9,
        };
        Assert.That(SkillCast.Use(Rec(600, 2), 6000, 55, false),
            Is.EqualTo(new StateSkillRequest(88, 4321, 600, 2, 0x1c, 55, 9)), "UseStateSkill: the record's tick, not the server tick");
        Assert.That(SkillCast.Use(Rec(100, 1), 6000, 55, false), Is.InstanceOf<SkillCastRequest>(), "action kind 0: the mode 0 send");
        Assert.That(SkillCast.Use(Rec(601, 1), 6000, 55, false), Is.InstanceOf<SkillCastRequest>(), "an emotion is no state skill");
    }

    [Test]
    public void UseStateSendsNothingWhereTheClientsChecksEndWithoutASend()
    {
        var t = Skills(Attack, Glide, Ridee);
        CastRecord Rec(uint id, long uid) => new()
        {
            Skill = t.Skills.GetSkillData(id), Level = t.Skills.GetSkillLevelData(id, 1), Tick = 4321, CastId = 88, ItemUid = uid,
        };
        Assert.That(SkillCast.Use(Rec(603, 9), 6000, 55, false), Is.Null, "kind 0x26: MyPC's ride / pet summon branch sends no 0x21");
        Assert.That(SkillCast.Use(Rec(602, 0), 6000, 55, false), Is.Null, "kind 0x1e without a glide item: no send");
        Assert.That(SkillCast.Use(Rec(602, 7), 6000, 55, false), Is.EqualTo(new StateSkillRequest(88, 4321, 602, 1, 0x1e, 55, 7)));
    }

    [Test]
    public void ANextCastIsAimedFromWhereTheOwnerIsWhenItGoes()
    {
        var w = World(Skills(Attack, JumpAttack));
        var c = new CastWorld { Aim = r => { r.Position = new Vector3(1, 2, 3); r.Direction = new Vector3(0, -1, 0); } };
        var r = SkillCast.Request(w, c, Req(100), _ => 100).Record;
        SkillCast.Aim(r, new Vector3(40, 50, 60), 90, null, false);   // moved and turned after the press (PCBase::vfunc_100 at the end step)
        var p = SkillCast.Send(r, 6000, 0, holdAttackFeature: false);
        Assert.That(p.Position, Is.EqualTo(new Vector3(40, 50, 60)), "the position now");   // m_k50
        Assert.That(p.Direction.X, Is.EqualTo(1f).Within(1e-6f), "facing rotZ 90");
        Assert.That(p.Direction.Y, Is.EqualTo(0f).Within(1e-6f));
        Assert.That(p.Rotation, Is.EqualTo(new Vector3(0, 0, 90)));
    }

    [Test]
    public void AContinueSkillAgainKeepsTheRunningRecordsAim()
    {
        // PCBase::vfunc_100 (pcbase.cpp:2575-2591) in the skill state
        var w = World(Skills(Attack, JumpAttack, Continue0, Continue2));
        var c = new CastWorld();
        CastRecord Rec(uint id) => SkillCast.Request(w, c, Req(id), _ => (int)id).Record;
        var running = Rec(500);
        running.Direction = new Vector3(0, 1, 0);
        running.Rotation = new Vector3(0, 0, 180);
        var next = Rec(500);
        SkillCast.Aim(next, new Vector3(40, 50, 60), 90, running, false);
        Assert.That(next.Position, Is.EqualTo(new Vector3(40, 50, 60)), "the position is the owner's now");
        Assert.That(next.Direction, Is.EqualTo(new Vector3(0, 1, 0)), "moveType 0: the running record's aim");
        Assert.That(next.Rotation, Is.EqualTo(new Vector3(0, 0, 180)));

        SkillCast.Aim(running, new Vector3(7, 8, 9), 90, running, false);   // the continue round, vfunc_100(m_spRecord, 1)
        Assert.That(running.Rotation, Is.EqualTo(new Vector3(0, 0, 180)), "the round keeps its own aim");
        Assert.That(running.Position, Is.EqualTo(new Vector3(7, 8, 9)), "at the position now");

        var other = Rec(100);
        SkillCast.Aim(other, new Vector3(40, 50, 60), 90, running, false);
        Assert.That(other.Rotation, Is.EqualTo(new Vector3(0, 0, 90)), "another skill is aimed again");

        var running2 = Rec(501);
        running2.Rotation = new Vector3(0, 0, 180);
        var next2 = Rec(501);
        SkillCast.Aim(next2, Vector3.Zero, 90, running2, false);
        Assert.That(next2.Rotation, Is.EqualTo(new Vector3(0, 0, 90)), "moveType 2 without feature 0xca: aimed again");
        SkillCast.Aim(next2, Vector3.Zero, 90, running2, true);
        Assert.That(next2.Rotation, Is.EqualTo(new Vector3(0, 0, 180)), "moveType 2 with feature 0xca: kept");
        SkillCast.Aim(next2, Vector3.Zero, 90, null, true);
        Assert.That(next2.Rotation, Is.EqualTo(new Vector3(0, 0, 90)), "no running record");
    }

    [Test]
    public void TheEndStepRunsOnceAFrame()
    {
        var once = new FrameOnce();
        Assert.That(once.Take(0), Is.True, "frame 0 is a frame");
        Assert.That(once.Take(0), Is.False, "the sequence's end ran it, the HUD's frame does not");
        Assert.That(once.Take(1), Is.True);
        Assert.That(once.Take(1), Is.False);
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

        Assert.That(Alloc.Least(() => { for (int i = 0; i < 100000; i++) SkillCast.SyncIfChanged(sent, r, Vector3.Zero, false, false); }), Is.LessThan(512), "100000 unchanged updates");
    }

    [Test]
    public void AContinueRoundSendsModeThreeCountsTheSendAndResendsTheCharging()
    {
        var w = World(Skills(Attack, JumpAttack));
        var r = SkillCast.Request(w, new CastWorld(), Req(100), _ => 100).Record;
        // CSkillRecord::SendSkill (skillrecord.cpp:443-449): m_nSendTick, the packet, m_nSendCount++
        Assert.That(SkillCast.SendSkill(r, 5000), Is.EqualTo(new SkillRoundRequest(77, 5000)));
        Assert.That((r.SendTick, r.SendCount), Is.EqualTo((5000, 1)));
        SkillCast.SendSkill(r, 5100);
        Assert.That((r.SendTick, r.SendCount), Is.EqualTo((5100, 2)));
        // loopend (stateskill.cpp:899): m_bSentCharging = false, the same charging sync goes again
        var sent = new ChargeSync();
        sent.Reset(Vector3.Zero);
        Assert.That(SkillCast.SyncIfChanged(sent, r, Vector3.UnitX, true, false), Is.Not.Null);
        Assert.That(SkillCast.SyncIfChanged(sent, r, Vector3.UnitX, true, false), Is.Null);
        sent.ClearCharging();
        Assert.That(SkillCast.SyncIfChanged(sent, r, Vector3.UnitX, true, false)?.Charging, Is.True);
    }
}
