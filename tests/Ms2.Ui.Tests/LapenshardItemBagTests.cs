using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The lapenshard window's item bag panel and equip by drop or right-click (needs MS2_DATA). The item ids come from the
/// data's itemLapenshardUpgrade rows, so no id or text is assumed.</summary>
public class LapenshardItemBagTests
{
    static PackSet packs;
    static MovieSource src;
    static List<(int Id, int Mask, int Group)> rows;
    static readonly List<string> seen = new();

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

    /// <summary>The table's usable single-kind lapenshards (a row, item data, any-job and level 1 items).</summary>
    static List<(int Id, int Mask, int Group)> Rows(GameTables t)
    {
        if (rows != null) return rows;
        rows = new();
        seen.Clear();
        var lp = TablePath.Resolve(Packs(), "itemlapenshardupgrade.xml");
        var root = GameStrings.Parse(Packs().Read("Xml", lp)).Root;
        foreach (var e in root.Elements("key"))
        {
            if (!FeatureElements.IsAvailable(Packs(), e)) continue;
            int id = ItemData.Atoi((string)e.Attribute("ItemId") ?? "0");
            int g = ItemData.Atoi((string)e.Attribute("LapenGroupID") ?? "0");
            var m = t.Lapenshards.Mask(id) ?? 0;
            var d = t.Items.Get(id);
            seen.Add($"{id}:g{g}:m{m}:{(d == null ? "nodata" : $"t{d.Type}:job{d.IsJobAllowed(10)}:lv{d.LevelLimit}")}");
            if (id == 0 || g == 0 || d == null || !d.IsLapenshard || m == 0) continue;
            if (!d.IsJobAllowed(10) || !d.IsLevelAllowed(99)) continue;
            rows.Add((id, (int)m, g));
        }
        return rows;
    }

    sealed class Rig
    {
        public UiHost Host; public LapenshardSystemController C; public LapenshardEquipTabPage Page;
        public List<IOutPacket> Sent = new(); public List<string> Boxes = new(); public List<string> Notices = new(); public List<int> Codes = new();
        public List<Item> Bag = new(); public UiModel Model;
    }

    static Rig Load(Func<GameTables, (int, int)[]> worn, Func<GameTables, IEnumerable<Item>> inventory, bool battle = false, Func<Item, bool> canUse = null, Func<GameTables, Func<Item, bool>> canUseFor = null)
    {
        var tables = new GameTables(Packs(), "en");
        var model = new UiModel(tables) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f), HasLocalPlayer = true };
        model.Player.Level = 99; model.Player.JobGroup = 10;
        model.Player.Lapenshards.Apply(new LapenshardMessage.Worn(worn(tables)));
        var r = new Rig { Model = model, Bag = inventory(tables).ToList() };
        var c = new LapenshardSystemController
        {
            Text = k => k,
            Send = p => { r.Sent.Add(p); return true; },
            InBattle = () => battle,
            CanUseItem = canUseFor?.Invoke(tables) ?? canUse ?? (_ => true),
            ShowBox = b => r.Boxes.Add(b.Msg),
            InventoryItems = cat => cat == 0xd ? r.Bag : Array.Empty<Item>(),
        };
        c.NoticeText += r.Notices.Add;
        c.NoticeCode += r.Codes.Add;
        r.C = c;
        r.Page = new LapenshardEquipTabPage();
        c.AddTab(r.Page);
        var (rt, p) = src.Open(c.Movie, x => x.Budget = 40_000_000);
        p.StageW = 1280; p.StageH = 720;
        r.Host = new UiHost(p, c, model);
        Assert.That(r.Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        return r;
    }

    static Item Make(GameTables t, int id, long uid)
    {
        var it = Item.Create(t.Items, id, 0);
        it.Uid = uid;
        return it;
    }

    static (int Id, int Mask, int Group) Pick(GameTables t, int mask, int notGroup = -1, int notId = -1)
    {
        var rs = Rows(t);
        foreach (var r in rs) if (r.Mask == mask && r.Group != notGroup && r.Id != notId) return r;
        Assert.Fail($"no row mask {mask}; usable {rs.Count}; rows seen {seen.Count}: {string.Join(" ", seen.Take(12))}");
        return default;
    }

    [Test]
    public void TheBagListsTheUsableLapenshardsOfTheInventoryByName()
    {
        var r = Load(_ => Array.Empty<(int, int)>(), t =>
        {
            var a = Pick(t, 1); var b = Pick(t, 2);
            return new[] { Make(t, a.Id, 11), Make(t, b.Id, 12), Make(t, a.Id, 13) };
        }, canUse: it => it.Uid != 13);
        Assert.That(r.Page.BagItems.Select(i => i.Uid), Is.EqualTo(new long[] { 11, 12 }));
        Assert.That(r.Page.GetBagSlotBindings(LapenshardEquipTabPage.ItemBag),
            Is.EqualTo(new[] { "LapenshardItemSlotBag.0", "LapenshardItemSlotBag.1" }));
        Assert.That(r.Page.GetBinding("LapenshardItemSlotBag.0"), Is.Not.Null);
        Assert.That(r.Page.GetBinding("LapenshardItemSlotBag.2"), Is.Null);
    }

    [Test]
    public void ADropOnASlotOfTheKindSendsTheEquip()
    {
        var r = Load(_ => Array.Empty<(int, int)>(), t => new[] { Make(t, Pick(t, 2).Id, 21) });
        r.C.SlotDroppedOn("LapenshardEquipSlotBag.2", "LapenshardItemSlotBag.0", r.C);        // slot 3 is blue
        Assert.That(r.Sent, Is.EqualTo(new IOutPacket[] { new LapenshardEquip(3, 21) }));
    }

    [Test]
    public void ADropOnAnotherKindShowsTheNoticeAndSendsNothing()
    {
        var r = Load(_ => Array.Empty<(int, int)>(), t => new[] { Make(t, Pick(t, 2).Id, 21) });
        r.C.SlotDroppedOn("LapenshardEquipSlotBag.0", "LapenshardItemSlotBag.0", r.C);        // slot 1 is red
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Codes, Is.EqualTo(new[] { 0xd3 }));
    }

    [Test]
    public void ADropOnABagSlotOrFromAnEquipSlotSendsNothing()
    {
        var r = Load(t => new[] { (1, Pick(t, 1).Id) }, t => new[] { Make(t, Pick(t, 1).Id, 21) });
        r.C.SlotDroppedOn("LapenshardItemSlotBag.0", "LapenshardItemSlotBag.0", r.C);
        r.C.SlotDroppedOn("LapenshardEquipSlotBag.1", "LapenshardEquipSlotBag.0", r.C);
        Assert.That(r.Sent, Is.Empty);
    }

    [Test]
    public void TheSameItemOrAnotherGroupWornRefusesWithTheNotice()
    {
        // slot 1 holds the very item: a worn lapenshard of the group, itemId == current
        var same = Load(t => new[] { (1, Pick(t, 1).Id) }, t => new[] { Make(t, Pick(t, 1).Id, 21) });
        same.C.SlotDroppedOn("LapenshardEquipSlotBag.0", "LapenshardItemSlotBag.0", same.C);
        Assert.That(same.Sent, Is.Empty);
        Assert.That(same.Notices, Is.EqualTo(new[] { "s_msg_shortcutkey_invalid_already_same_lapenshard" }));
        // slot 2 holds the group, the target slot 1 holds another group
        var other = Load(t => { var a = Pick(t, 1); return new[] { (1, Pick(t, 1, a.Group).Id), (2, a.Id) }; },
            t => new[] { Make(t, Pick(t, 1).Id, 22) });
        other.C.SlotDroppedOn("LapenshardEquipSlotBag.0", "LapenshardItemSlotBag.0", other.C);
        Assert.That(other.Sent, Is.Empty);
        Assert.That(other.Notices, Has.Count.EqualTo(1));
    }

    [Test]
    public void ASameGroupItemOfAnotherLevelReplacesTheWornOne()
    {
        var pair = new Func<GameTables, ((int Id, int Mask, int Group) A, (int Id, int Mask, int Group) B)>(t =>
        {
            var rs = Rows(t);
            foreach (var a in rs) foreach (var b in rs) if (a.Mask == 1 && b.Mask == 1 && a.Group == b.Group && a.Id != b.Id) return (a, b);
            Assert.Ignore("the data has no two red lapenshards of one group");
            return default;
        });
        var r = Load(t => new[] { (1, pair(t).A.Id) }, t => new[] { Make(t, pair(t).B.Id, 31) });
        r.C.SlotDroppedOn("LapenshardEquipSlotBag.0", "LapenshardItemSlotBag.0", r.C);
        Assert.That(r.Sent, Is.EqualTo(new IOutPacket[] { new LapenshardEquip(1, 31) }));
    }

    [Test]
    public void ARightClickTakesTheEmptySlotOfTheKindElseTheFirst()
    {
        var r = Load(t => new[] { (1, Pick(t, 1).Id) }, t => new[] { Make(t, Pick(t, 1, Pick(t, 1).Group).Id, 41) });
        Assert.That(r.C.OnCallback("notifySlotUse", new object[] { "LapenshardItemSlotBag.0", 1 }), Is.True);
        Assert.That(r.Sent, Is.EqualTo(new IOutPacket[] { new LapenshardEquip(2, 41) }));          // slot 1 is taken, 2 is empty
        var full = Load(t => new[] { (1, Pick(t, 1).Id), (2, Pick(t, 1).Id) }, t => new[] { Make(t, Pick(t, 1).Id, 42) });
        full.C.OnCallback("notifySlotUse", new object[] { "LapenshardItemSlotBag.0", 1 });
        Assert.That(full.Sent, Is.Empty, "both red slots hold the same item: the same-group notice");
        Assert.That(full.Notices, Has.Count.EqualTo(1));
    }

    [Test]
    public void ALeftClickOrAnEquipSlotRightClickSendsNothing()
    {
        var r = Load(_ => Array.Empty<(int, int)>(), t => new[] { Make(t, Pick(t, 1).Id, 41) });
        r.C.OnCallback("notifySlotUse", new object[] { "LapenshardItemSlotBag.0", 0 });
        r.C.OnCallback("notifySlotUse", new object[] { "LapenshardEquipSlotBag.0", 1 });
        Assert.That(r.Sent, Is.Empty);
    }

    [Test]
    public void InBattleTheEquipShowsTheBoxAndSendsNothing()
    {
        var r = Load(_ => Array.Empty<(int, int)>(), t => new[] { Make(t, Pick(t, 1).Id, 41) }, battle: true);
        r.C.OnCallback("notifySlotUse", new object[] { "LapenshardItemSlotBag.0", 1 });
        r.C.SlotDroppedOn("LapenshardEquipSlotBag.0", "LapenshardItemSlotBag.0", r.C);
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Boxes, Has.Count.EqualTo(2));
    }

    [Test]
    public void DraggingABagSlotLightsItsKindAndTheReleaseClearsIt()
    {
        var r = Load(_ => Array.Empty<(int, int)>(), t => new[] { Make(t, Pick(t, 4).Id, 51) });
        r.C.SlotDragStarted("LapenshardItemSlotBag.0");
        Assert.That(r.C.SelectedKind, Is.EqualTo(4));
        r.C.SlotDragDone("LapenshardItemSlotBag.0");
        Assert.That(r.C.SelectedKind, Is.EqualTo(0));
    }

    static Func<GameTables, Func<Item, bool>> HudFeed(long now) => t =>
        LapenshardSystemController.CanUseFrom(t.Items, () => new PcItemUse.Pc { ServerTime = () => now });

    static Item MakeRecord(GameTables t, int id, long uid, long expiry)
    {
        var it = Make(t, id, uid);
        it.Record = new EquipItem { Id = id, Uid = uid, ExpiryTime = expiry };
        return it;
    }

    [Test]
    public void AnExpiredLapenshardIsNotListedAndADropOrRightClickSendsNothing()
    {
        // HudFeed is the factory the HUD hands to the controller (the window's CanUseItem)
        var r = Load(_ => Array.Empty<(int, int)>(), t => new[] { MakeRecord(t, Pick(t, 1).Id, 61, expiry: 500), MakeRecord(t, Pick(t, 2).Id, 62, expiry: 0) },
            canUseFor: HudFeed(1000));
        Assert.That(r.Page.BagItems.Select(i => i.Uid), Is.EqualTo(new long[] { 62 }));
        r.C.SlotDroppedOn("LapenshardEquipSlotBag.0", "LapenshardItemSlotBag.1", r.C);
        r.C.OnCallback("notifySlotUse", new object[] { "LapenshardItemSlotBag.1", 1 });
        Assert.That(r.Sent, Is.Empty);
        var live = Load(_ => Array.Empty<(int, int)>(), t => new[] { MakeRecord(t, Pick(t, 1).Id, 63, expiry: 5000) }, canUseFor: HudFeed(1000));
        Assert.That(live.Page.BagItems.Select(i => i.Uid), Is.EqualTo(new long[] { 63 }), "an unexpired item lists");
    }

    [Test]
    public void AReplacedIdIsNeverCountedDownSoItsGroupStaysWorn()
    {
        // EquipLapenshard counts an id up and never counts a replaced id down (mypc.cpp:4195)
        var pair = new Func<GameTables, ((int Id, int Mask, int Group) A, (int Id, int Mask, int Group) A2, (int Id, int Mask, int Group) B)>(t =>
        {
            var rs = Rows(t);
            foreach (var a in rs) foreach (var a2 in rs) if (a.Mask == 1 && a2.Mask == 1 && a.Group == a2.Group && a.Id != a2.Id)
                foreach (var b in rs) if (b.Mask == 1 && b.Group != a.Group) return (a, a2, b);
            Assert.Ignore("the data has no two red lapenshards of one group and a third of another");
            return default;
        });
        var r = Load(t => new[] { (1, pair(t).A.Id) }, t => new[] { Make(t, pair(t).A2.Id, 71) });
        r.Model.Player.Lapenshards.Apply(new LapenshardMessage.Equipped(1, pair(r.Model.Tables).B.Id));
        r.C.SlotDroppedOn("LapenshardEquipSlotBag.1", "LapenshardItemSlotBag.0", r.C);
        Assert.That(r.Sent, Is.Empty, "the replaced id stays in the count map: its group still counts as worn");
        Assert.That(r.Notices, Has.Count.EqualTo(1));
    }

    [Test]
    public void AWornIdWithoutATableRowIsDropped()
    {
        var r = Load(t => new[] { (1, 1), (2, Pick(t, 1).Id) }, t => Array.Empty<Item>());
        Assert.That(r.C.WornItemId(1), Is.EqualTo(0));
        Assert.That(r.C.WornItemId(2), Is.Not.EqualTo(0));
    }

    [Test]
    public void AMultiBitMaskItemDropsOnAWornSlotOfTheSameMask()
    {
        var pair = new Func<GameTables, ((int Id, int Mask, int Group) A, (int Id, int Mask, int Group) B)>(t =>
        {
            var rs = Rows(t);
            foreach (var a in rs) foreach (var b in rs) if (a.Mask == 7 && b.Mask == 7 && a.Group != b.Group) return (a, b);
            Assert.Ignore("the data has no two mask-7 lapenshards of different groups");
            return default;
        });
        var r = Load(t => new[] { (1, pair(t).A.Id) }, t => new[] { Make(t, pair(t).B.Id, 81) });
        r.C.SlotDroppedOn("LapenshardEquipSlotBag.0", "LapenshardItemSlotBag.0", r.C);
        Assert.That(r.Codes, Is.Empty);
        Assert.That(r.Sent, Is.EqualTo(new IOutPacket[] { new LapenshardEquip(1, 81) }));
    }

    [Test]
    public void AnItemAboveThePlayersLevelIsRefusedWithoutANotice()
    {
        var r = Load(_ => Array.Empty<(int, int)>(), t =>
        {
            var hi = Rows(t).FirstOrDefault(x => x.Mask == 1 && t.Items.Get(x.Id).LevelLimit > 1);
            if (hi.Id == 0) Assert.Ignore("the data has no red lapenshard above level 1");
            return new[] { Make(t, hi.Id, 91) };
        });
        r.Model.Player.Level = 1;
        r.C.SlotDroppedOn("LapenshardEquipSlotBag.0", "LapenshardItemSlotBag.0", r.C);
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Notices, Is.Empty);
        Assert.That(r.Codes, Is.Empty);
    }
}
