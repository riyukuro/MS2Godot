using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The changeSkill swap: Unk_SkillBlockB0::Load @0x1408034a0 (skillleveldata.cpp:600), AdditionalCliList::FUN_140970020
/// @0x140970020 with the overlap test @0x140975ed0 (additionalcli_2.cpp:142-171, 383), MyPC::FUN_141526c80 @0x141526c80
/// (mypc.cpp:2633) and its use in the compact pick (skillslot.cpp:2482-2490), over hand-made skill rows.
/// </summary>
public class SkillChangeTests
{
    static List<XElement> Levels(XElement e, string name, string attr) => e.Elements(name).ToList();

    // main type 1 is not a book skill kind: learnt at level 1 without a book entry.
    // 20 swaps to 21 under effect 900 at any level with an overlap of 2 or more (sign 5: the count 2 at or below the overlap),
    // to 22 under effect 901 at level 3 with an overlap of 1 (sign padded to 1, equal); 20 costs more sp than the world has.
    const string Swapping = "<skill id=\"20\"><basic mainType=\"1\">"
        + "<changeSkill changeSkillCheckEffectID=\"900,901\" changeSkillCheckEffectLevel=\"0,3\" changeSkillCheckEffectOverlapCount=\"2,1\""
        + " changeSkillID=\"21,22\" changeSkillCheckEffectCompareFunc=\"5\"/></basic><level value=\"1\" sp=\"500\"/></skill>";
    // 21 has a sequence, 20 has none
    const string Cheap21 = "<skill id=\"21\"><basic mainType=\"1\"/><level value=\"1\"><motion sequenceName=\"Attack_01_A\"/></level></skill>";
    const string Cheap22 = "<skill id=\"22\"><basic mainType=\"1\"/><level value=\"1\"/></skill>";
    // the levels list is one short: the whole block is dropped
    const string Broken = "<skill id=\"23\"><basic mainType=\"1\">"
        + "<changeSkill changeSkillCheckEffectID=\"900,901\" changeSkillCheckEffectLevel=\"0\" changeSkillCheckEffectOverlapCount=\"2,1\""
        + " changeSkillID=\"21,22\"/></basic><level value=\"1\"/></skill>";
    // running skills: 30's combo turns 22 into 31; 32 is the same but a charging skill
    const string Combo = "<skill id=\"30\"><basic mainType=\"1\"><combo inputSkill=\"22\" outputSkill=\"31\"/></basic><level value=\"1\"/></skill>";
    const string ChargingCombo = "<skill id=\"32\"><basic mainType=\"1\"><combo inputSkill=\"22\" outputSkill=\"31\" chargingSkill=\"1\"/></basic><level value=\"1\"/></skill>";

    static SkillTables Skills() => new(JobTable.Empty, new SkillDataTable(() => new[]
        { XDocument.Parse("<ms2>" + Swapping + Cheap21 + Cheap22 + Broken + Combo + ChargingCombo + "</ms2>") }, Levels));

    static AdditionalLevelData Level(uint id, ushort level) => new() { Level = level };

    static BuffPacket Buff(int key, uint id, ushort level, int overlap, byte enabled = 1) =>
        new(BuffPacket.Add, 5, key, 5, 0, new BuffState(0, 1000, id, level, overlap, enabled), 0);

    AdditionalBuffList buffs;
    SkillTables t;
    SlotWorld w;
    readonly HashSet<int> features = new();

    [SetUp]
    public void SetUp()
    {
        features.Clear();
        buffs = new AdditionalBuffList(Level);
        t = Skills();
        w = new SlotWorld
        {
            Tables = t, Jobs = new JobSystem(t), Level = 10, Hp = 100, Sp = 100, Ep = 100, Field = new SlotField(),
            HasEffect = (id, level, count, compare) => buffs.Has(id, level, count, compare),
            IsFeatureEnabled = features.Contains,
        };
    }

    (uint, ushort, bool) Change(uint id)
    {
        ushort level = 1;
        bool changed = SkillCast.ChangeSkill(w, ref id, ref level);
        return (id, level, changed);
    }

    [Test]
    public void TheCompareSignsArePaddedWithEqualsAndAShortListDropsTheBlock()
    {
        var ch = t.GetSkillData(20).Change;
        Assert.That(ch.CheckEffectCompareFunc, Is.EqualTo(new[] { 5, 1 }));
        Assert.That(ch.ChangeSkillId, Is.EqualTo(new[] { 21u, 22u }));
        var broken = t.GetSkillData(23).Change;
        Assert.That(broken.CheckEffectId, Is.Empty);
        Assert.That(broken.ChangeSkillId, Is.Empty);
        Assert.That(broken.CheckEffectOverlapCount, Is.Empty);
    }

    [Test]
    public void TheEffectTestTakesTheLevelAndTheCompareSign()
    {
        buffs.Add(Buff(1, 900, 4, 3));
        buffs.Add(Buff(2, 901, 3, 1, enabled: 0));
        Assert.That(buffs.Has(900, 0, 0, 0), Is.True, "no count: the sign is not read");
        Assert.That(buffs.Has(900, 4, 3, 1), Is.True);
        Assert.That(buffs.Has(900, 3, 3, 1), Is.False, "another level");
        Assert.That(buffs.Has(900, 0, 2, 1), Is.False, "1: equal");
        Assert.That(buffs.Has(900, 0, 4, 2), Is.True, "2: the count above the overlap");
        Assert.That(buffs.Has(900, 0, 3, 2), Is.False);
        Assert.That(buffs.Has(900, 0, 3, 3), Is.True, "3: at or above");
        Assert.That(buffs.Has(900, 0, 2, 4), Is.True, "4: below");
        Assert.That(buffs.Has(900, 0, 3, 4), Is.False);
        Assert.That(buffs.Has(900, 0, 3, 5), Is.True, "5: at or below");
        Assert.That(buffs.Has(900, 0, 4, 5), Is.False);
        Assert.That(buffs.Has(900, 0, 3, 0), Is.False, "any other sign fails");
        Assert.That(buffs.Has(901, 3, 1, 1), Is.False, "not enabled");
    }

    [Test]
    public void AnEffectSwapsTheRequestToItsChangeSkill()
    {
        Assert.That(Change(20), Is.EqualTo((20u, (ushort)1, false)), "no effect");
        buffs.Add(Buff(1, 900, 1, 1));
        Assert.That(Change(20), Is.EqualTo((20u, (ushort)1, false)), "overlap 1 is under the count 2");
        buffs.Add(Buff(2, 901, 3, 1));
        Assert.That(Change(20), Is.EqualTo((22u, (ushort)1, true)), "the second check, sign 1 by padding");
        buffs.Add(Buff(3, 900, 1, 2));
        Assert.That(Change(20), Is.EqualTo((21u, (ushort)1, true)), "the first check that passes wins");
        Assert.That(Change(23), Is.EqualTo((23u, (ushort)1, false)), "a dropped block swaps nothing");
    }

    [Test]
    public void TheOwnRequestTakesTheSequenceFlagOfTheSkillItSwapsTo()
    {
        uint id = 20;
        ushort level = 1;
        Assert.That(SkillCast.OwnSkill(w, ref id, ref level, out bool seq), Is.False);
        Assert.That((id, seq), Is.EqualTo((20u, false)), "20 has no sequence");
        buffs.Add(Buff(3, 900, 1, 2));
        Assert.That(SkillCast.OwnSkill(w, ref id, ref level, out seq), Is.True);
        Assert.That((id, seq), Is.EqualTo((21u, true)), "swapped to 21: its sequence, not 20's");
        var c = new CastWorld();
        var req = new CastRequest(id, level, 1, 0) { HasSequence = seq };
        Assert.That(SkillCast.Check(w, c, t.GetSkillData(21).GetLevelData(1), req), Is.Not.EqualTo(1),
            "the sequence flag agrees with 21's level: no reason 1");
        id = 99;
        Assert.That(SkillCast.OwnSkill(w, ref id, ref level, out seq), Is.False);
        Assert.That(seq, Is.True, "no row: the request's default");
    }

    [Test]
    public void TheRunningCastsComboSwapsBeforeTheEffects()
    {
        buffs.Add(Buff(3, 900, 1, 2));
        w.RunningSkill = () => t.GetSkillData(30);
        Assert.That(Change(20), Is.EqualTo((22u, (ushort)1, true)), "22 is the running skill's combo input");
        w.RunningSkill = () => t.GetSkillData(32);
        Assert.That(Change(20), Is.EqualTo((22u, (ushort)1, true)), "without feature 0xca a charging running skill still counts");
        features.Add(0xca);
        Assert.That(Change(20), Is.EqualTo((21u, (ushort)1, true)), "with 0xca a charging running skill does not: the effect");
        w.RunningSkill = () => t.GetSkillData(21);
        Assert.That(Change(20), Is.EqualTo((21u, (ushort)1, true)), "with 0xca a running skill with no combo output does not");
        w.RunningSkill = () => t.GetSkillData(30);
        Assert.That(Change(20), Is.EqualTo((22u, (ushort)1, true)));
        Assert.That(w.OutputSkill(22), Is.EqualTo(31u), "the output skill is the running skill's");
        w.RunningSkill = () => null;
        Assert.That(w.OutputSkill(22), Is.EqualTo(22u));
    }

    [Test]
    public void TheCompactPickChecksTheSwappedSkill()
    {
        var table = new SkillCompactControlTable(XDocument.Parse("<ms2><job code=\"0\"><skill id=\"20\"/></job></ms2>"), _ => true, true);
        const long action = 0xaae61;
        var down = new HashSet<long> { action };
        var c = new CompactCast();
        c.SetConfigs(new[] { new SkillMacroTab("a", action, new[] { 20 }) }, true, true, down.Contains, 0, w, table);
        Assert.That(c.Pick(false, false, true, down.Contains, w, 5000, null), Is.EqualTo(0u), "20 costs 500 sp");
        buffs.Add(Buff(3, 900, 1, 2));
        Assert.That(c.Pick(false, false, true, down.Contains, w, 5000, null), Is.EqualTo(20u),
            "the check runs on 21, the pick stays 20");
    }

    [Test]
    public void TheSlotReplacementTestsTheOverlapForEquality()
    {
        var slot = new SkillSlotState(w, 20, 0, 0);
        w.HasPc = true;
        buffs.Add(Buff(1, 900, 1, 3));
        Assert.That(slot.Replacement(), Is.Null, "overlap 3 is not the count 2 (the slot passes sign 1)");
        buffs.Add(Buff(2, 900, 1, 2));
        Assert.That(slot.Replacement()?.Id, Is.EqualTo(21u));
    }
}
