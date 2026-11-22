using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The upgrade tab's three panels on the real movie: the source bag, the ingredient bag and the upgrade slot panel.</summary>
public class LapenshardUpgradePanelTests
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

    sealed class Rig
    {
        public UiHost Host; public Action Tick; public LapenshardSystemController C; public LapenshardUpgradeTabPage Page; public GameTables Tables;
        public List<IOutPacket> Sent = new(); public List<Item> Bag = new();
    }

    /// <summary>A source row that has an upgrade and a minimum count, and its group's first item as the fodder.</summary>
    static (int Source, int Fodder, LapenshardTable.Row Row) Rows(GameTables t)
    {
        foreach (var id in t.Lapenshards.ItemIds().OrderBy(i => i))
        {
            var row = t.Lapenshards.Get(id);
            if (row.Next == 0 || row.MinCount <= 0 || t.Items.Get(id) is not { IsLapenshard: true }) continue;
            int first = t.Lapenshards.FirstItem(row.Group);
            if (first == 0 || t.Items.Get(first) is not { IsLapenshard: true } || t.Lapenshards.Level(first) >= 2) continue;
            return (id, first, row);
        }
        Assert.Ignore("no upgradable lapenshard row with a level 1 fodder");
        return default;
    }

    static Item Make(GameTables t, int id, long uid, uint count)
    {
        var it = Item.Create(t.Items, id, 0);
        it.Uid = uid; it.Count = count;
        return it;
    }

    static Rig Load(Func<GameTables, IEnumerable<Item>> inventory, Func<GameTables, (int, int)[]> worn = null)
    {
        var tables = new GameTables(Packs(), "en");
        var model = new UiModel(tables) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f), HasLocalPlayer = true };
        model.Player.Level = 99; model.Player.JobGroup = 10;
        model.Player.Lapenshards.Apply(new LapenshardMessage.Worn(worn?.Invoke(tables) ?? Array.Empty<(int, int)>()));
        var r = new Rig { Tables = tables, Bag = inventory(tables).ToList() };
        var c = new LapenshardSystemController
        {
            Text = k => k, Send = p => { r.Sent.Add(p); return true; }, ShowBox = _ => { },
            InventoryItems = cat => cat == 0xd ? r.Bag : Array.Empty<Item>(),
        };
        c.AddTab(new LapenshardEquipTabPage());
        r.Page = new LapenshardUpgradeTabPage { CountItems = (_, _) => 1000, CountByTag = _ => 1000 };
        c.AddTab(r.Page);
        r.C = c;
        var (rt, p) = src.Open(c.Movie, x => x.Budget = 40_000_000);
        p.StageW = 1280; p.StageH = 720;
        r.Host = new UiHost(p, c, model); r.Tick = () => p.Tick();
        Assert.That(r.Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        r.Host.Set(r.Host.Child("m_buttonBar"), "selectedIndex", 1);
        for (int i = 0; i < 30; i++) p.Tick();
        Assert.That(c.Page, Is.SameAs(r.Page), "the upgrade tab is the current page");
        return r;
    }

    [Test]
    public void TheSourceBagListsTheUpgradableLapenshardsThenTheWornOnes()
    {
        var r = Load(t => { var x = Rows(t); return new[] { Make(t, x.Source, 11, 1), Make(t, x.Fodder, 12, 3) }; }, t => new[] { (1, Rows(t).Source) });
        var ids = r.Page.SourceItems.Select(i => (i.ItemId, i.Uid, i.IsEquipped)).ToList();
        var x = Rows(r.Tables);
        Assert.That(ids.Last(), Is.EqualTo((x.Source, 0L, true)), "the worn one comes last, uid 0");
        Assert.That(ids.Count(i => !i.IsEquipped), Is.EqualTo(r.Page.SourceItems.Count(i => i.Uid != 0)));
        Assert.That(r.Page.GetBagSlotBindings(LapenshardUpgradeTabPage.SourceBag).Count(), Is.EqualTo(ids.Count));
        Assert.That(r.Page.GetBinding(LapenshardUpgradeTabPage.SourceBag + ".0"), Is.Not.Null);
        Assert.That(r.Page.GetBinding(LapenshardUpgradeTabPage.SourceBag + "." + ids.Count), Is.Null);
    }

    [Test]
    public void SelectingASourceFillsTheIngredientBagAndTheSlotPanel()
    {
        var r = Load(t => { var x = Rows(t); return new[] { Make(t, x.Source, 11, 1), Make(t, x.Fodder, 12, 3) }; });
        var x = Rows(r.Tables);
        int idx = r.Page.SourceItems.ToList().FindIndex(i => i.Uid == 11);
        Assert.That(r.C.OnCallback("OnSelectSourceItem", new object[] { idx }), Is.False.Or.True);
        Assert.That((r.Page.State, r.Page.Source?.Uid), Is.EqualTo((1, (long?)11)));
        Assert.That(r.Page.IngredientItems.Select(i => i.Uid), Is.All.Not.EqualTo(11L), "the source itself is excluded");
        Assert.That(r.Page.IngredientItems.Any(i => i.Uid == 12), Is.True);
        Assert.That(r.Page.GetBinding(LapenshardUpgradeTabPage.SourceSlot), Is.Not.Null);
        Assert.That(r.Page.GetBinding(LapenshardUpgradeTabPage.ResultSlot), Is.Not.Null);
        Assert.That(r.Page.SlotItems[0].ItemId, Is.EqualTo(x.Fodder), "the first slot is the group's first item at the minimum count");
        Assert.That(r.Page.SlotItems[0].Count, Is.EqualTo((uint)x.Row.MinCount));
        Assert.That(r.Page.GetBinding(LapenshardUpgradeTabPage.SlotBag + ".0"), Is.Not.Null);
        // the label frames are played (vfunc_13 is gotoAndPlay) and the names sit in the body's children
        var h = r.Host;
        var body = h.Get(h.Get(h.Child("m_body"), "m_upgradePage"), "m_body");
        Assert.That(body, Is.Not.Null);
        for (int i = 0; i < 40; i++) r.Tick();
        Assert.That(h.Get(body, "currentLabel"), Is.EqualTo("select"));
        Assert.That(h.Get(h.Get(h.Get(body, "m_sourceItemName"), "textField"), "text")?.ToString(), Does.Not.EqualTo("itemName").And.Not.Empty);
    }

    [Test]
    public void SelectingAnIngredientTakesUpToTheNeedAndSelectingItAgainGivesItBack()
    {
        var r = Load(t => { var x = Rows(t); return new[] { Make(t, x.Source, 11, 1), Make(t, x.Fodder, 12, (uint)x.Row.MinCount + 5) }; });
        var x = Rows(r.Tables);
        r.C.OnCallback("OnSelectSourceItem", new object[] { r.Page.SourceItems.ToList().FindIndex(i => i.Uid == 11) });
        int fi = r.Page.IngredientItems.ToList().FindIndex(i => i.Uid == 12);
        r.Sent.Clear();
        r.C.OnCallback("OnSelectIngredientItem", new object[] { fi });
        Assert.That(r.Page.IngredientTotal, Is.EqualTo(x.Row.MinCount), "taken up to the need");
        Assert.That(r.Page.GetIngredientCount(12), Is.EqualTo(x.Row.MinCount));
        Assert.That(r.Page.IngredientItems[fi].HasFlags(2), Is.True);
        Assert.That(r.Page.IngredientItems[fi].Count, Is.EqualTo(5u));
        Assert.That(r.Sent.Last(), Is.EqualTo(new LapenshardAddFodder(11, x.Source, 0, x.Row.MinCount)));
        r.C.OnCallback("OnSelectIngredientItem", new object[] { fi });
        Assert.That((r.Page.IngredientTotal, r.Page.IngredientItems[fi].HasFlags(2), r.Page.IngredientItems[fi].Count), Is.EqualTo((0, false, (uint)x.Row.MinCount + 5)));
    }

    [Test]
    public void SelectionCallbacksAreIgnoredOutsideTheirStates()
    {
        var r = Load(t => { var x = Rows(t); return new[] { Make(t, x.Source, 11, 1), Make(t, x.Fodder, 12, 9) }; });
        r.C.OnCallback("OnSelectIngredientItem", new object[] { 0 });
        Assert.That((r.Page.IngredientTotal, r.Page.State), Is.EqualTo((0, 0)), "the ingredient bag works only in state 1");
        int si = r.Page.SourceItems.ToList().FindIndex(i => i.Uid == 11);
        r.C.OnCallback("OnSelectSourceItem", new object[] { si });
        r.Sent.Clear();
        r.Page.OnCallback("OnProcessingComplete", Array.Empty<object>());       // state 4
        r.C.OnCallback("OnSelectSourceItem", new object[] { si });
        Assert.That(r.Sent.OfType<LapenshardPreviewRequest>(), Is.Empty);
    }
}
