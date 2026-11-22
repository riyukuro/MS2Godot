using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The skill state's auto change: the changeSkill block's autoChange (Unk_SkillBlockB0::Load, skillleveldata.cpp:609), the
/// "startAutoChangeSkill" / "endAutoChangeSkill" text keys (StateSkill::TextKeyEvent, stateskill.cpp:1029-1038),
/// StateSkill::FUN_1417f0570 @0x1417f0570 (:2161-2199), its use at the end step (FUN_1417ee120, :2905-2915) and the chain
/// veto of FUN_1417ef4a0 (:3150-3163), over hand-made skill rows.
/// </summary>
public class SkillAutoChangeTests
{
    static List<XElement> Levels(XElement e, string name, string attr) => e.Elements(name).ToList();

    // 40 (autoChange 1) and 41 (autoChange 2) change to 42 under effect 900; 43 has no autoChange; 42 is immediateActive 1;
    // 44 changes to 45, which costs more sp than the world has; main type 1: learnt at level 1 without a book entry
    static string Changing(uint id, int auto, uint to) => $"<skill id=\"{id}\"><basic mainType=\"1\">"
        + $"<changeSkill changeSkillCheckEffectID=\"900\" changeSkillCheckEffectLevel=\"0\" changeSkillCheckEffectOverlapCount=\"1\""
        + $" changeSkillID=\"{to}\" autoChange=\"{auto}\"/></basic><level value=\"1\"><motion sequenceName=\"Attack_01_A\"/></level></skill>";
    const string Target = "<skill id=\"42\"><basic mainType=\"1\"><kinds immediateActive=\"1\"/></basic>"
        + "<level value=\"1\"><motion sequenceName=\"Attack_02_A\"/></level></skill>";
    const string Costly = "<skill id=\"45\"><basic mainType=\"1\"/><level value=\"1\" sp=\"500\"><motion sequenceName=\"Big\"/></level></skill>";

    static SkillTables Skills() => new(JobTable.Empty, new SkillDataTable(() => new[]
    {
        XDocument.Parse("<ms2>" + Changing(40, 1, 42) + Changing(41, 2, 42) + Changing(43, 0, 42) + Changing(44, 1, 45)
            + Target + Costly + "</ms2>"),
    }, Levels));

    static AdditionalLevelData Level(uint id, ushort level) => new() { Level = level };

    AdditionalBuffList buffs;
    SkillTables t;
    SlotWorld w;
    CastWorld c;
    long castIds;
    int castIdCalls;

    [SetUp]
    public void SetUp()
    {
        buffs = new AdditionalBuffList(Level);
        t = Skills();
        castIds = 1000;
        castIdCalls = 0;
        w = new SlotWorld
        {
            Tables = t, Jobs = new JobSystem(t), Level = 10, Hp = 100, Sp = 100, Ep = 100, Field = new SlotField(),
            HasEffect = (id, level, count, compare) => buffs.Has(id, level, count, compare),
        };
        c = new CastWorld();
    }

    void Buff900() => buffs.Add(new BuffPacket(BuffPacket.Add, 5, 1, 5, 0, new BuffState(0, 1000, 900, 1, 1, 1), 0));

    CastRecord Running(uint id)
    {
        var lv = t.GetSkillData(id).GetLevelData(1);
        return new CastRecord { Skill = lv.Skill, Level = lv, CastId = 1 };
    }

    long NextId() { castIdCalls++; return ++castIds; }

    [Test]
    public void AutoChangeLoads()
    {
        Assert.That(t.GetSkillData(40).Change.AutoChange, Is.EqualTo(1));
        Assert.That(t.GetSkillData(41).Change.AutoChange, Is.EqualTo(2));
        Assert.That(t.GetSkillData(43).Change.AutoChange, Is.EqualTo(0));
    }

    [Test]
    public void TheTextKeysOpenAndCloseTheWindow()
    {
        Assert.That(SkillFollow.ComboKeys.Classify("STARTautochangeskill", null), Is.EqualTo(SkillFollow.ComboKey.StartAutoChange));
        Assert.That(SkillFollow.ComboKeys.Classify("endAutoChangeSkill", null), Is.EqualTo(SkillFollow.ComboKey.EndAutoChange));
        var keys = new SkillFollow.ComboKeys();
        keys.Add(0.2f, SkillFollow.ComboKey.StartAutoChange);
        keys.Add(0.5f, SkillFollow.ComboKey.EndAutoChange);
        var win = default(SkillFollow.Window);
        keys.Advance(ref win, -1, 0.3, true);
        Assert.That((win.AutoChangeOn, win.AutoChangeEnd), Is.EqualTo((true, false)));
        var before = win;
        keys.Advance(ref win, 0.3, 0.6, true);
        Assert.That((win.AutoChangeOn, win.AutoChangeEnd), Is.EqualTo((false, true)));
        Assert.That(win.Same(before), Is.False, "the flags take part in the window compare");
    }

    [Test]
    public void TheKindPicksTheWindow()
    {
        var on = new SkillFollow.Window { AutoChangeOn = true };
        var end = new SkillFollow.Window { AutoChangeEnd = true };
        Assert.That(SkillFollow.AutoChangeWanted(Running(40), 0, on), Is.True);
        Assert.That(SkillFollow.AutoChangeWanted(Running(40), 1, on), Is.True);
        Assert.That(SkillFollow.AutoChangeWanted(Running(40), 2, on), Is.False, "kind 2 asks only an autoChange 2 skill");
        Assert.That(SkillFollow.AutoChangeWanted(Running(40), 0, end), Is.False, "autoChange 1 needs m_b11c");
        Assert.That(SkillFollow.AutoChangeWanted(Running(41), 0, end), Is.True);
        Assert.That(SkillFollow.AutoChangeWanted(Running(41), 2, on), Is.False, "autoChange 2 needs m_b11d");
        Assert.That(SkillFollow.AutoChangeWanted(Running(43), 0, on), Is.False, "no autoChange");
    }

    [Test]
    public void TheChangedSkillsRecordSetsTheFlagsAndIsReused()
    {
        var poll = new SkillCast.AutoChangePoll();
        var run = Running(41);
        var win = new SkillFollow.Window { AutoChangeEnd = true };
        Assert.That(poll.Get(w, c, run, 2, ref win, NextId, () => 7000), Is.Null, "no effect: the change gives the same skill");
        Assert.That(win.AutoChanged, Is.False);
        Buff900();
        var r = poll.Get(w, c, run, 2, ref win, NextId, () => 7000);
        Assert.That(r, Is.Not.Null);
        Assert.That((r.Skill.Id, r.Level.Level, r.Key, r.CastId, r.Tick), Is.EqualTo((42u, (ushort)1, 0, 1001L, 7000)));
        Assert.That((win.AutoChangeEnd, win.AutoChanged), Is.EqualTo((false, true)), "m_b11d cleared, m_b11e set");
        win.AutoChangeEnd = true;
        Assert.That(poll.Get(w, c, run, 0, ref win, NextId, () => 7000), Is.SameAs(r), "the same running record, skill and level");
        Assert.That(castIdCalls, Is.EqualTo(1));
        Assert.That(win.AutoChangeEnd, Is.False);
        poll.Clear();
        Assert.That(poll.Get(w, c, run, 0, ref win, NextId, () => 7000), Is.Null, "m_b11d is down again");
    }

    [Test]
    public void ARefusedChangeIsNotAskedAgainInTheSameWindow()
    {
        Buff900();
        var poll = new SkillCast.AutoChangePoll();
        var run = Running(44);
        var win = new SkillFollow.Window { AutoChangeOn = true };
        Assert.That(poll.Get(w, c, run, 0, ref win, NextId, () => 1), Is.Null, "45 costs 500 sp");
        Assert.That(poll.LastWhy, Is.Not.Zero);
        Assert.That(win.AutoChanged, Is.False);
        Assert.That(poll.Get(w, c, run, 0, ref win, NextId, () => 1), Is.Null);
        Assert.That(castIdCalls, Is.EqualTo(1), "one request for the window");
        w.Sp = 1000;
        win.InputCombo = true;
        Assert.That(poll.Get(w, c, run, 0, ref win, NextId, () => 1)?.Skill.Id, Is.EqualTo(45u), "asked again once the window changed");
    }

    [Test]
    public void APollFrameAllocatesNothing()
    {
        Buff900();
        var poll = new SkillCast.AutoChangePoll();
        var run = Running(40);
        var win = new SkillFollow.Window { AutoChangeOn = true };
        Func<long> id = NextId;
        Func<int> tick = () => 1;
        poll.Get(w, c, run, 0, ref win, id, tick);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) poll.Get(w, c, run, 0, ref win, id, tick);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    [Test]
    public void TheEndStepTakesAnImmediateChangeOfAnotherSkill()
    {
        var run = Running(41);
        var to = Running(42);
        Assert.That(SkillFollow.AutoCastsAtEnd(run, to), Is.True);
        Assert.That(SkillFollow.AutoCastsAtEnd(run, null), Is.False);
        Assert.That(SkillFollow.AutoCastsAtEnd(run, Running(45)), Is.False, "immediateActive 0");
        Assert.That(SkillFollow.AutoCastsAtEnd(to, to), Is.False, "the running skill again, not key-down");
    }

    [Test]
    public void TheChainVetoesTheRunningSkillsOwnChangeUntilTheAutoChange()
    {
        Buff900();
        var run = Running(40);
        var next = Running(42);
        var win = default(SkillFollow.Window);
        Assert.That(SkillCast.AutoChangeVetoes(w, run, next, win), Is.True, "40 changes to 42 itself");
        Assert.That(SkillCast.AutoChangeVetoes(w, run, Running(45), win), Is.False, "another skill");
        Assert.That(SkillCast.AutoChangeVetoes(w, Running(43), next, win), Is.False, "no autoChange");
        win.AutoChanged = true;
        Assert.That(SkillCast.AutoChangeVetoes(w, run, next, win), Is.False, "m_b11e set");
    }
}
