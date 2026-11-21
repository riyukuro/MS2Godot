using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The local player's buff list (AdditionalCliList) and the main player info's buff strip (ASEffectManager).</summary>
public class BuffStripTests
{
    sealed class Strip : IBuffStrip
    {
        public readonly List<string> Calls = new();
        public void AddBuffEffect(AdditionalBuff buff) => Calls.Add("add " + buff.Key);
        public void UpdateBuffEffect(AdditionalBuff buff) => Calls.Add("update " + buff.Key);
        public void RemoveBuffEffect(int key) => Calls.Add("remove " + key);
        public void QueueSlotRefresh(int flags) => Calls.Add("refresh " + flags);
    }

    static AdditionalLevelData Level(uint id, ushort level) =>
        id == 1 ? new AdditionalLevelData { Level = level, IconOnOff = 1, Icon = "a.png" } : null;

    static BuffPacket Add(int key, uint id) => new(BuffPacket.Add, 5, key, 5, 0, new BuffState(0, 1000, id, 1, 1, 1), 0);

    [Test]
    public void AddRemoveAndModifyGoToTheStrip()
    {
        var s = new Strip();
        var list = new AdditionalBuffList(Level) { UpperTargetId = 5, Strip = s };
        list.Apply(Add(10, 1));
        list.Apply(Add(10, 1));      // a key already held: nothing
        list.Apply(Add(11, 2));      // no level: nothing
        list.Apply(new BuffPacket(BuffPacket.Modify, 5, 10, 5, BuffPacket.ModifyState, new BuffState(0, 2000, 1, 1, 2, 1), null));
        list.Apply(new BuffPacket(BuffPacket.Modify, 5, 10, 5, BuffPacket.ModifyValue, null, 9));      // the shield only: no UI
        list.Apply(new BuffPacket(BuffPacket.Modify, 5, 12, 5, BuffPacket.ModifyState, new BuffState(0, 2000, 1, 1, 1, 1), null));   // not kept
        list.Apply(new BuffPacket(BuffPacket.Remove, 5, 10, 5, 0, null, null));
        Assert.That(s.Calls, Is.EqualTo(new[] { "add 10", "refresh 13", "update 10", "update 12", "remove 10", "refresh 13" }));
        Assert.That(list.Buffs, Is.Empty);
    }

    [Test]
    public void PvpOnOffIsSetByAnEnabledEffectWhoseLevelHasIt()
    {
        // <Basic pvpOnOff> (AdditionalLevelData::Load, additionalactionadditional.cpp:1066) summed by
        // AdditionalCliList::FUN_140972a00 (additionalcli_2.cpp:1066-1082): enabled, not suspended, the level's flag
        var pvp = AdditionalLevelData.Load(System.Xml.Linq.XElement.Parse("<level level=\"1\"><Basic pvpOnOff=\"1\"/></level>"), null);
        Assert.That(pvp.PvpOnOff, Is.True);
        Assert.That(AdditionalLevelData.Load(System.Xml.Linq.XElement.Parse("<level level=\"1\"><Basic/></level>"), null).PvpOnOff, Is.False);
        var list = new AdditionalBuffList((id, lv) => id == 7 ? pvp : Level(id, lv));
        list.Apply(Add(10, 1));
        Assert.That(list.IsPvpOnOff, Is.False, "an effect without it");
        list.Apply(new BuffPacket(BuffPacket.Add, 5, 11, 5, 0, new BuffState(0, 1000, 7, 1, 1, 0), 0));
        Assert.That(list.IsPvpOnOff, Is.False, "not enabled");
        list.Apply(new BuffPacket(BuffPacket.Modify, 5, 11, 5, BuffPacket.ModifyState, new BuffState(0, 1000, 7, 1, 1, 1), null));
        Assert.That(list.IsPvpOnOff, Is.True);
        list.Buffs.Single(b => b.Key == 11).Suspended = true;
        Assert.That(list.IsPvpOnOff, Is.False, "suspended");
        list.Buffs.Single(b => b.Key == 11).Suspended = false;
        list.Apply(new BuffPacket(BuffPacket.Remove, 5, 11, 5, 0, null, null));
        Assert.That(list.IsPvpOnOff, Is.False, "removed");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = list.IsPvpOnOff;
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(512));
    }

    [Test]
    public void ForceApplyTargetAndHideAreSummedOverTheEnabledEffects()
    {
        // <Basic forceApplyTarget> and <Motion hide> (AdditionalLevelData::Load, additionalactionadditional.cpp:1074, :1100)
        // summed by AdditionalCliList::FUN_140972a00 (additionalcli_2.cpp:1072, :1096): enabled, not suspended; the bits OR'd
        var a = AdditionalLevelData.Load(System.Xml.Linq.XElement.Parse("<level level=\"1\"><Basic forceApplyTarget=\"4\"/><Motion hide=\"1\"/></level>"), null);
        var b = AdditionalLevelData.Load(System.Xml.Linq.XElement.Parse("<level level=\"1\"><Basic forceApplyTarget=\"2\"/></level>"), null);
        Assert.That((a.ForceApplyTarget, a.Hide, b.ForceApplyTarget, b.Hide), Is.EqualTo((4, 1, 2, 0)));
        var list = new AdditionalBuffList((id, lv) => id == 7 ? a : id == 8 ? b : Level(id, lv));
        list.Apply(Add(10, 1));
        Assert.That((list.ForceApplyTarget, list.IsHidden), Is.EqualTo((0, false)), "effects without them");
        list.Apply(new BuffPacket(BuffPacket.Add, 5, 11, 5, 0, new BuffState(0, 1000, 7, 1, 1, 0), 0));
        Assert.That((list.ForceApplyTarget, list.IsHidden), Is.EqualTo((0, false)), "not enabled");
        list.Apply(new BuffPacket(BuffPacket.Modify, 5, 11, 5, BuffPacket.ModifyState, new BuffState(0, 1000, 7, 1, 1, 1), null));
        list.Apply(new BuffPacket(BuffPacket.Add, 5, 12, 5, 0, new BuffState(0, 1000, 8, 1, 1, 1), 0));
        Assert.That((list.ForceApplyTarget, list.IsHidden), Is.EqualTo((6, true)));
        list.Buffs.Single(x => x.Key == 11).Suspended = true;
        Assert.That((list.ForceApplyTarget, list.IsHidden), Is.EqualTo((2, false)), "suspended");
        list.Apply(new BuffPacket(BuffPacket.Remove, 5, 12, 5, 0, null, null));
        Assert.That(list.ForceApplyTarget, Is.EqualTo(0), "removed");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { _ = list.ForceApplyTarget; _ = list.IsHidden; }
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(512));
    }

    [Test]
    public void AGroupCooltimeReduceSumsTheOwnEnabledSpecialEffectsOfTheGroup()
    {
        // <SpecialEffect type probability values> (additionalactionadditional.cpp:1346-1353, CXmlData::ParseValues
        // xmldata.cpp:112) run by AdditionalCliList::FUN_140972380 (additionalcli_2.cpp:956) with kind 4 and
        // CSEReduceSkillGroupCooltime::vfunc_1 (specialeffectfunctionmanager.cpp:26)
        var a = AdditionalLevelData.Load(System.Xml.Linq.XElement.Parse("<level level=\"1\"><SpecialEffect type=\"4\" "
            + "values=\"&lt;v groupID=&quot;90000001&quot; reduceRate=&quot;0.20&quot; /&gt;\"/></level>"), null);
        var b = AdditionalLevelData.Load(System.Xml.Linq.XElement.Parse("<level level=\"1\"><SpecialEffect type=\"4\" probability=\"0.5\" "
            + "values=\"&lt;v groupID=&quot;90000001&quot; reduceRate=&quot;0.1&quot; /&gt;\"/></level>"), null);
        var c = AdditionalLevelData.Load(System.Xml.Linq.XElement.Parse("<level level=\"1\"><SpecialEffect type=\"4\" values=\"&lt;v\"/></level>"), null);
        Assert.That((a.SpecialEffectType, a.SpecialEffectProbability, a.SpecialEffectValues["reduceRate"]), Is.EqualTo((4, 1f, "0.20")));
        Assert.That((b.SpecialEffectProbability, c.SpecialEffectValues), Is.EqualTo((0.5f, (Dictionary<string, string>)null)), "a values string that does not parse stores nothing");
        float roll = 0.75f;
        var list = new AdditionalBuffList((id, lv) => id == 7 ? a : id == 8 ? b : Level(id, lv)) { Roll = () => roll };
        list.Apply(new BuffPacket(BuffPacket.Add, 5, 11, 5, 0, new BuffState(0, 1000, 7, 1, 1, 1), 0));
        list.Apply(new BuffPacket(BuffPacket.Add, 5, 12, 5, 0, new BuffState(0, 1000, 8, 1, 1, 1), 0));
        Assert.That(list.ReduceSkillGroupCooltime(90000001), Is.EqualTo(0f), "GetOwnerMyPC not set");
        list.OwnerIsMyPc = true;
        Assert.That(list.ReduceSkillGroupCooltime(90000001), Is.EqualTo(0.2f).Within(1e-6f), "0.5 under the roll 0.75: skipped");
        Assert.That(list.ReduceSkillGroupCooltime(90000002), Is.EqualTo(0f), "another group");
        roll = 0.25f;
        Assert.That(list.ReduceSkillGroupCooltime(90000001), Is.EqualTo(0.3f).Within(1e-6f));
        list.Buffs.Single(x => x.Key == 11).Suspended = true;
        Assert.That(list.ReduceSkillGroupCooltime(90000001), Is.EqualTo(0.3f).Within(1e-6f), "suspension is not tested");
        list.Apply(new BuffPacket(BuffPacket.Modify, 5, 12, 5, BuffPacket.ModifyState, new BuffState(0, 1000, 8, 1, 1, 0), null));
        Assert.That(list.ReduceSkillGroupCooltime(90000001), Is.EqualTo(0.2f).Within(1e-6f), "not enabled");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = list.ReduceSkillGroupCooltime(90000001);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(512));
    }

    [Test]
    public void TheSkillStatOptionsSumTheOwnEnabledInvokeEffectsOfTheSkillOrItsGroup()
    {
        // <InvokeEffect> (additionalactionadditional.cpp:1335-1344) collected by AdditionalCliList::CollectInvokeEffectStats
        // (additionalcli_2.cpp:851) and read by CEquipment::FUN_1411e13e0 / FUN_1411e1250 (equipment.cpp:751, :770)
        var bySkill = AdditionalLevelData.Load(System.Xml.Linq.XElement.Parse(
            "<level level=\"1\"><InvokeEffect skillID=\"100\" types=\"1002,1001\" values=\"2,0\" rates=\"0,0.1\"/></level>"), null);
        var byGroup = AdditionalLevelData.Load(System.Xml.Linq.XElement.Parse(
            "<level level=\"1\"><InvokeEffect skillGroupID=\"9\" types=\"1001\" values=\"0\" rates=\"0.05\"/></level>"), null);
        var byEffect = AdditionalLevelData.Load(System.Xml.Linq.XElement.Parse(
            "<level level=\"1\"><InvokeEffect effectID=\"5\" skillGroupID=\"9\" types=\"1001\" values=\"0\" rates=\"0.5\"/></level>"), null);
        Assert.That((bySkill.InvokeSkillId, bySkill.InvokeTypes, bySkill.InvokeValues, bySkill.InvokeRates, bySkill.HasInvokeEffect),
            Is.EqualTo((100u, new[] { 1002, 1001 }, new[] { 2, 0 }, new[] { 0f, 0.1f }, true)));
        var list = new AdditionalBuffList((id, lv) => id == 7 ? bySkill : id == 8 ? byGroup : id == 9 ? byEffect : Level(id, lv));
        list.Apply(new BuffPacket(BuffPacket.Add, 5, 11, 5, 0, new BuffState(0, 1000, 7, 1, 2, 1), 0));
        list.Apply(new BuffPacket(BuffPacket.Add, 5, 12, 5, 0, new BuffState(0, 1000, 8, 1, 1, 1), 0));
        list.Apply(new BuffPacket(BuffPacket.Add, 5, 13, 5, 0, new BuffState(0, 1000, 9, 1, 1, 1), 0));
        Assert.That(list.SkillInvokeOption(0x3ea, 100, 9, out var a), Is.EqualTo(4), "2 x overlap 2");
        Assert.That(a, Is.EqualTo(0f));
        Assert.That(list.SkillInvokeOption(0x3e9, 100, 9, out a), Is.EqualTo(0));
        Assert.That(a, Is.EqualTo(0.25f).Within(1e-6f), "0.1 x 2 + the group's 0.05; an effectID keeps its group out");
        Assert.That(list.SkillInvokeOption(0x3e9, 101, 0, out a), Is.EqualTo(0));
        Assert.That(a, Is.EqualTo(0f), "another skill outside the group");
        Assert.That(list.SkillInvokeOption(0x3, 100, 9, out a), Is.EqualTo(0), "an effect option is no skill option");
        list.Apply(new BuffPacket(BuffPacket.Modify, 5, 11, 5, BuffPacket.ModifyState, new BuffState(0, 1000, 7, 1, 2, 0), null));
        Assert.That(list.SkillInvokeOption(0x3ea, 100, 9, out _), Is.EqualTo(0), "not enabled");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) list.SkillInvokeOption(0x3e9, 100, 9, out _);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(512));
    }

    [Test]
    public void SortOrderAndRemainTicks()
    {
        uint now = 400;
        var list = new AdditionalBuffList(Level) { UpperTargetId = 5, ServerTick = () => now };
        list.Apply(Add(10, 1));
        var b = list.Buffs.Single();
        Assert.That(b.SortOrder, Is.EqualTo(2));         // cast by its owner
        Assert.That(b.RemainTicks, Is.EqualTo(600u));
        now = 1200;
        Assert.That(b.RemainTicks, Is.EqualTo(0u));
    }

    [Test]
    public void SnipeShowsAboveTheSkillSlots()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        var src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        var tables = new GameTables(packs, "en");
        var m = new UiModel(tables) { Keys = new KeyTable(packs, "en"), HasLocalPlayer = true };
        UiFixture.Apply(m);
        var c = new MainPlayerInfoController();
        var list = new AdditionalBuffList(tables.Additional.GetLevel) { UpperTargetId = 1, Strip = c };
        c.IsUpperTargetId = list.IsUpperTargetId;
        list.Apply(new BuffPacket(BuffPacket.Add, 1, 1, 1, 0, new BuffState(0, 0, 10500111, 1, 1, 1), 0));     // before the load: pending
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True);
        uint tick = 0;
        c.TickMs = () => tick;
        for (int i = 0; i < 30; i++) { tick += 33; c.UpdateEffects(); p.Tick(); }
        var mgr = h.Child("m_effectManager") as AsContainer;
        Assert.That(mgr, Is.Not.Null);
        var buffs = mgr.Children[^2] as AsContainer;
        Assert.That(buffs, Is.Not.Null);
        Assert.That(buffs.Children, Has.Count.EqualTo(1));
        var icon = buffs.Children[0];
        Assert.That(icon.visible, Is.True);
        Assert.That((Rt.ToNumber(h.Get(buffs, "x")), Rt.ToNumber(h.Get(buffs, "y"))), Is.EqualTo((-395.0, -12.0)));
        Assert.That(Rt.ToNumber(h.Get(h.Get(icon, "m_buffBorder"), "currentFrame")), Is.GreaterThan(0));
        Assert.That(Rt.ToNumber(h.Get(h.Get(icon, "m_cooldown"), "currentFrame")), Is.EqualTo(0x1e));       // keep condition 99: no ring
        Assert.That(rt.Errors, Is.Empty);
        var tip = BuffTooltip.Build(tables, tables.Additional.GetLevel(10500111, 1), 5, 0);
        TestContext.WriteLine(tip["text"]);
        Assert.That((string)tip["text"], Does.StartWith("<FONT color='#00AEFF'>"));
        Assert.That(tip["maxWidth"], Is.EqualTo(0x198));
    }
}
