using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The lapenshard window's equip page: six worn slots over the real movie (needs MS2_DATA).</summary>
public class LapenshardEquipTabTests
{
    static PackSet packs;
    static MovieSource src;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        }
        return packs;
    }

    static (UiHost, LapenshardSystemController, List<IOutPacket> sent, List<string> boxes) Load(bool battle = false, bool fieldLimit = false)
    {
        var model = new UiModel(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f), HasLocalPlayer = true };
        model.Player.Lapenshards.Apply(new LapenshardMessage.Worn(new[] { (1, 70000001), (4, 70000002) }));
        var sent = new List<IOutPacket>();
        var boxes = new List<string>();
        var c = new LapenshardSystemController
        {
            Text = k => k,
            Send = p => { sent.Add(p); return true; },
            InBattle = () => battle,
            FieldLimitsLapenshard = () => fieldLimit,
            ShowBox = b => boxes.Add(b.Msg),
        };
        c.AddTab(new LapenshardEquipTabPage());
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 40_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        return (h, c, sent, boxes);
    }

    [Test]
    public void EverySlotIsBoundToTheWornBagAndFlaggedByWhatIsWorn()
    {
        var (h, c, _, _) = Load();
        var page = (LapenshardEquipTabPage)c.Page;
        Assert.That(page.GetBagSlotBindings(LapenshardEquipTabPage.Bag), Is.EqualTo(new[]
            { "LapenshardEquipSlotBag.0", "LapenshardEquipSlotBag.1", "LapenshardEquipSlotBag.2", "LapenshardEquipSlotBag.3", "LapenshardEquipSlotBag.4", "LapenshardEquipSlotBag.5" }));
        Assert.That(page.Root, Is.Not.Null);
        for (int i = 1; i <= 6; i++)
        {
            var slot = h.Get(page.Root, $"m_equipSlot{i}");
            Assert.That(slot, Is.Not.Null, $"m_equipSlot{i}");
            Assert.That(Rt.ToString(h.Get(h.Get(slot, "m_slot"), "binding")), Is.EqualTo($"LapenshardEquipSlotBag.{i - 1}"));
            Assert.That(Rt.ToBoolean(h.Get(slot, "m_equip")), Is.EqualTo(i is 1 or 4), $"isEquip {i}");
        }
    }

    [Test]
    public void KindsAreRedBlueGreenInPairs()
    {
        Assert.That(Enumerable.Range(1, 6).Select(LapenshardEquipTabPage.KindOfSlot), Is.EqualTo(new[] { 1, 1, 2, 2, 4, 4 }));
    }

    [Test]
    public void UnmountSendsModeTwoOnlyForAWornSlot()
    {
        var (_, c, sent, boxes) = Load();
        c.OnCallback("OnUnmountEquipSlot", new object[] { 1u });
        c.OnCallback("OnUnmountEquipSlot", new object[] { 2u });
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new LapenshardUnequip(1) }));
        Assert.That(boxes, Is.Empty);
    }

    [Test]
    public void UnmountInBattleShowsTheBoxAndSendsNothing()
    {
        var (_, c, sent, boxes) = Load(battle: true);
        c.OnCallback("OnUnmountEquipSlot", new object[] { 1u });
        Assert.That(sent, Is.Empty);
        Assert.That(boxes, Is.EqualTo(new[] { "s_lapenshard_disable_battle" }));
    }

    [Test]
    public void UnmountOnALimitedFieldShowsTheFieldBoxAndSendsNothing()
    {
        var (_, c, sent, boxes) = Load(fieldLimit: true);
        c.OnCallback("OnUnmountEquipSlot", new object[] { 1u });
        Assert.That(sent, Is.Empty);
        Assert.That(boxes, Is.EqualTo(new[] { "s_lapenshard_disable_field" }));
    }

    [Test]
    public void AnEquippedPacketRebuildsThePanel()
    {
        var (h, c, _, _) = Load();
        c.Model.Player.Lapenshards.Apply(new LapenshardMessage.Equipped(2, 70000003));
        c.OnPacket(new LapenshardMessage.Equipped(2, 70000003));
        var slot = h.Get(((LapenshardEquipTabPage)c.Page).Root, "m_equipSlot2");
        Assert.That(Rt.ToBoolean(h.Get(slot, "m_equip")), Is.True);
    }
}
