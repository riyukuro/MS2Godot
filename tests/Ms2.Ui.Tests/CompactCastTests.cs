using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The skill system's compact auto-cast: CSkillSystem::FUN_141766ec0 @0x141766ec0 (skillslot.cpp:2384-2442), FUN_1417674a0
/// @0x1417674a0 (:2446-2508), FUN_141767990 @0x141767990 (:2512-2526), SetCompactConfigs (:2551-2556) and FUN_141767df0
/// (:2560-2567), over hand-made skill rows and a compact control table.
/// </summary>
public class CompactCastTests
{
    const long ActionA = 0xaae61, ActionB = 0xaae62;

    static List<XElement> Levels(XElement e, string name, string attr) => e.Elements(name).ToList();

    // main type 1 is not a book skill kind: learnt at level 1 without a book entry
    static SkillTables Skills() => new(JobTable.Empty, new SkillDataTable(() => new[] { XDocument.Parse("<ms2>"
        + "<skill id=\"10\"><basic mainType=\"1\"><condition useCoolDownShowUI=\"1\"/></basic><level value=\"1\"/></skill>"
        + "<skill id=\"11\"><basic mainType=\"1\"/><level value=\"1\" sp=\"50\"/></skill>"
        + "<skill id=\"12\"><basic mainType=\"1\"/><level value=\"1\"/></skill>"
        + "<skill id=\"13\"><basic mainType=\"1\"/><level value=\"1\"/></skill>"
        + "</ms2>") }, Levels));

    // job group 0 (JobTable.Empty): 10, 11 twice more, 12; 13 is not in the table
    static readonly SkillCompactControlTable Table = new(XDocument.Parse(
        "<ms2><job code=\"0\"><skill id=\"10\"/><skill id=\"11\" repeatCount=\"2\"/><skill id=\"12\"/></job></ms2>"), _ => true, true);

    static readonly SkillMacroTab NoKey = new("none", 0, new[] { 10, 11, 12 }),
        TabA = new("a", ActionA, new[] { 10, 11, 13 }),
        TabB = new("b", ActionB, new[] { 12 });

    readonly HashSet<long> down = new();
    readonly HashSet<uint> cooling = new();
    readonly List<int> notices = new();
    Func<long, bool> actionDown;
    Action<int> notice;
    SlotWorld w;
    SkillTables t;

    [SetUp]
    public void SetUp()
    {
        down.Clear(); cooling.Clear(); notices.Clear();
        actionDown = down.Contains;
        notice = notices.Add;
        t = Skills();
        w = new SlotWorld
        {
            Tables = t, Jobs = new JobSystem(t), Level = 10, Hp = 100, Sp = 100, Ep = 100, Field = new SlotField(),
            CooldownRemain = (id, _) => cooling.Contains(id) ? 1f : 0f,
        };
    }

    CompactCast Cast()
    {
        var c = new CompactCast();
        c.SetConfigs(new[] { NoKey, TabA, TabB }, true, true, actionDown, 0, w, Table);
        return c;
    }

    void Update(CompactCast c, int job = 0) => c.Update(false, true, actionDown, job, w, Table);
    uint Pick(CompactCast c, bool any = false, uint now = 5000, bool castAllowed = false) => c.Pick(any, castAllowed, true, actionDown, w, now, notice);

    [Test]
    public void TheTabWhoseKeyIsDownFillsTheListsFromTheJobsTicked()
    {
        var c = Cast();
        Assert.That(c.Tab, Is.Null, "no key down");
        down.Add(ActionA);
        Update(c);
        Assert.That(c.Tab, Is.SameAs(TabA), "a tab with key action 0 is skipped");
        Assert.That(c.Skills, Is.EqualTo(new uint[] { 10 }), "useCoolDownShowUI");
        Assert.That(c.Chains, Is.EqualTo(new uint[] { 11, 11, 11 }), "once plus repeatCount; 12 not ticked, 13 not in the table");
        down.Clear();
        Update(c);
        Assert.That(c.Tab, Is.SameAs(TabA), "the tab stays when its key goes up");
        Assert.That(c.Chains.Count, Is.EqualTo(3));
        down.Add(ActionB);
        Update(c);
        Assert.That(c.Tab, Is.SameAs(TabB));
        Assert.That(c.Skills, Is.Empty, "a new tab empties the lists");
        Assert.That(c.Chains, Is.EqualTo(new uint[] { 12 }));
        Update(c, job: 1);
        Assert.That(c.Chains, Is.Empty, "a new job group empties them (job 1 has no rows)");
    }

    [Test]
    public void ThePickTakesTheSkillsListFirstAndSkipsCoolingSkills()
    {
        var c = Cast();
        down.Add(ActionA);
        Update(c);
        Assert.That(Pick(c), Is.EqualTo(10u));
        cooling.Add(10);
        Assert.That(Pick(c), Is.EqualTo(11u), "a cooling skill is skipped");
        cooling.Add(11);
        Assert.That(Pick(c), Is.EqualTo(0u), "both cooling");
        Assert.That(Pick(c, any: true), Is.EqualTo(0u), "with any the cast check refuses them (reasons 2 and 0x23)");   // FUN_14176a530
        cooling.Clear();
        down.Clear();
        Assert.That(Pick(c), Is.EqualTo(0u), "the tab's key up");
    }

    [Test]
    public void TheFirstRefusalAfterTheKeyWentUpIsNoticedOnce()
    {
        var c = Cast();
        down.Add(ActionA);
        Update(c);
        cooling.Add(10);
        w.Sp = 10;   // 11 costs 50 sp: FUN_14176a530's 6
        Assert.That(Pick(c), Is.EqualTo(0u));
        Assert.That(notices, Is.Empty, "the key was not up before");
        down.Clear();
        Pick(c);
        down.Add(ActionA);
        Pick(c);
        Pick(c);
        Assert.That(notices, Is.EqualTo(new[] { 6 }), "once, for the first refused skill");
    }

    [Test]
    public void ACastTakesOneChainEntryAndAnEmptyListRefills()
    {
        var c = Cast();
        down.Add(ActionA);
        Update(c);
        var s11 = t.GetSkillData(11);
        c.Used(true, s11, 1);
        c.Used(true, t.GetSkillData(10), 1);
        Assert.That(c.Chains, Is.EqualTo(new uint[] { 11, 11 }), "one entry by origin id; 10 is not in the chains");
        Assert.That(c.Skills, Is.EqualTo(new uint[] { 10 }));
        c.Used(true, s11, 2);
        Assert.That(c.Chains.Count, Is.EqualTo(2), "no level data at level 2");
        c.Used(false, s11, 1);
        Assert.That(c.Chains.Count, Is.EqualTo(2), "feature off");
        Update(c);
        Assert.That(c.Chains.Count, Is.EqualTo(2), "a list with entries is not refilled");
        c.Used(true, s11, 1);
        c.Used(true, s11, 1);
        Update(c);
        Assert.That(c.Chains, Is.EqualTo(new uint[] { 11, 11, 11 }), "the empty chains list refills at the next update");
        Assert.That(c.Skills, Is.EqualTo(new uint[] { 10 }), "the skills list is not added to again");
    }

    [Test]
    public void TheCapturesEndHoldsThePicksForASecond()
    {
        var c = Cast();
        down.Add(ActionA);
        Update(c);
        c.HotKeyCapture(true, 5000);
        Assert.That(Pick(c, now: 5000), Is.EqualTo(10u), "on does not hold");
        c.HotKeyCapture(false, 5000);
        Assert.That(Pick(c, now: 5999), Is.EqualTo(0u));
        Assert.That(Pick(c, now: 6000), Is.EqualTo(10u));
        Assert.That(c.Pick(false, false, false, actionDown, w, 6000, notice), Is.EqualTo(0u), "feature 0x10 off");
    }

    [Test]
    public void SetConfigsForcesAnUpdate()
    {
        var c = Cast();
        down.Add(ActionA);
        Update(c);
        c.Used(true, t.GetSkillData(11), 1);
        c.SetConfigs(new[] { TabA }, true, true, actionDown, 0, w, Table);
        Assert.That(c.Chains, Is.EqualTo(new uint[] { 11, 11, 11 }), "emptied and refilled");
    }

    [Test]
    public void SkillMacroMode2OnlyReplacesTheTabs()
    {
        // FUN_141767ae0 (skillslot.cpp:2530-2547): m_lstCompact only, no FUN_141766ec0(1)
        var c = Cast();
        down.Add(ActionA);
        Update(c);
        c.Used(true, t.GetSkillData(11), 1);
        c.SetConfigs(new[] { TabA }, false, true, actionDown, 0, w, Table);
        Assert.That(c.Chains, Is.EqualTo(new uint[] { 11, 11 }), "not emptied");
        Assert.That(c.Tab, Is.SameAs(TabA));
        down.Clear();
        down.Add(ActionB);
        Update(c);
        Assert.That(c.Tab, Is.SameAs(TabA), "the next update reads the new tabs (B is gone)");
    }

    [Test]
    public void InsideTheSkillStateACoolingChainSkillIsPicked()
    {
        // m_nCastAllowed 1 (StateSkill::vfunc_8 stateskill.cpp:639-697): a cooling skill without useCoolDownShowUI is not
        // skipped (:2475-2483) and the cast check passes it (:3141-3143), so the chain order holds
        var c = Cast();
        down.Add(ActionA);
        Update(c);
        cooling.Add(10);
        cooling.Add(11);
        Assert.That(Pick(c, castAllowed: true), Is.EqualTo(11u), "the cooling chain skill, not skipped");
        Assert.That(Pick(c), Is.EqualTo(0u), "outside the state it is skipped");
        cooling.Remove(11);
        cooling.Add(12);
        down.Clear();
        down.Add(ActionB);
        Update(c);
        Assert.That(Pick(c, castAllowed: true), Is.EqualTo(12u));
        Assert.That(notices, Is.Empty);
    }

    [Test]
    public void TheContinueHoldPicksTheCoolingSkillEachRound()
    {
        // stateskill.cpp:665: FUN_1417674a0(1) under m_nCastAllowed 1 each round; the skill cools after its first round
        var c = Cast();
        down.Add(ActionA);
        Update(c);
        cooling.Add(10);
        for (int round = 0; round < 4; round++)
        {
            Assert.That(Pick(c, any: true, castAllowed: true), Is.EqualTo(11u), $"round {round}");
            cooling.Add(11);
            Update(c);
        }
        Assert.That(Pick(c, any: true), Is.EqualTo(0u), "outside the state the cast check refuses it (0x23)");
    }

    [Test]
    public void TheUpdateAndThePickAllocateNothing()
    {
        var c = Cast();
        down.Add(ActionA);
        Update(c);
        Assert.That(Alloc.Least(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                Update(c);
                Pick(c);
                Pick(c, any: true);
            }
        }), Is.LessThan(512));
    }
}
