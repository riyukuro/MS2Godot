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
        var list = new AdditionalBuffList(Level) { SelfObjectId = 5, Strip = s };
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
    public void SortOrderAndRemainTicks()
    {
        uint now = 400;
        var list = new AdditionalBuffList(Level) { SelfObjectId = 5, ServerTick = () => now };
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
        var list = new AdditionalBuffList(tables.Additional.GetLevel) { SelfObjectId = 1, Strip = c };
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
