using System.Globalization;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The select item box dialog (uiselectitemboxdialog, UI id 0x4eff) over the real movie (needs MS2_DATA): the list it
/// fills from the item's drop box, the picks the OK handler turns into the indices OpenItemBoxContents gets, and the
/// refused cases opening nothing. No language's strings are compared.</summary>
public class SelectItemBoxTests
{
    static PackSet packs;
    static MovieSource src;

    static void Packs()
    {
        if (packs != null) return;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = new PackSet(d);
        src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
    }

    static int FirstBox(PackSet p, int minRewards = 1)
    {
        GameTables tables = null;
        if (p.Exists("Xml", "table/individualitemdrop_final.xml"))
        {
            foreach (var b in GameStrings.Parse(p.Read("Xml", "table/individualitemdrop_final.xml")).Root.Elements("dropBox"))
                if (FeatureElements.IsAvailable(p, b) && b.Elements("group").Any(g => FeatureElements.IsAvailable(p, g)
                    && g.Elements("v").Any(v => FeatureElements.IsAvailable(p, v))))
                {
                    int id = ItemData.Atoi((string)b.Attribute("dropBoxID") ?? "0");
                    if (id != 0 && (minRewards <= 1 || SelectItemBoxController.BoxRewards(tables ??= new GameTables(p, "en"), id, 1, 0).Count >= minRewards))
                        return id;
                }
            return 0;
        }
        tables = new GameTables(p, "en");
        foreach (int id in tables.IndividualDrops.BoxIds)
            if (SelectItemBoxController.BoxRewards(tables, id, 1, 0).Count >= minRewards)
                return id;
        return 0;
    }

    sealed class Rig : IDisposable
    {
        public SelectItemBoxController C = new() { ItemUid = 77 };
        public UiModel Model;
        public UiHost Host;
        public Item Box;
        public List<(Item, string)> Opened = new();
        public List<string> Notices = new();
        public List<uint> Picks = new();
        public int Closes;
        readonly List<string> savedArgs;
        readonly ItemData data;

        public Rig(int selectCount, int boxId, bool player = true)
        {
            Packs();
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = player };
            int id = new[] { 20000001, 20000002, 30000001, 11300001, 12000001 }.FirstOrDefault(i => !string.IsNullOrEmpty(Model.Tables.ItemName(i)));
            if (id == 0) Assert.Ignore("no known item");
            Box = Item.Create(Model.Tables.Items, id, 1);
            Box.Uid = 77;
            data = Box.Data;
            savedArgs = data.FunctionArgs.ToList();
            data.FunctionArgs.Clear();
            data.FunctionArgs.Add(selectCount.ToString(CultureInfo.InvariantCulture));
            data.FunctionArgs.Add(boxId.ToString(CultureInfo.InvariantCulture));
            C.FindItem = u => u == 77 ? Box : null;
            C.HaveCount = _ => 1;
            C.GetBoxItems = b =>
            {
                return SelectItemBoxController.BoxRewards(Model.Tables, b, 1, 0);
            };
            C.OpenContents = (i, s) => Opened.Add((i, s));
            C.NoticeKey += Notices.Add;
            C.SelectedOverride = () => Picks;
            C.Closing += () => Closes++;
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            Host = new UiHost(p, C, Model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }

        public void Dispose()
        {
            data.FunctionArgs.Clear();
            data.FunctionArgs.AddRange(savedArgs);
        }
    }

    [Test]
    public void TheListIsFilledFromTheBoxAndPaddedToEighteenRows()
    {
        Packs();
        int box = FirstBox(packs);
        Assert.That(box, Is.Not.EqualTo(0), "no individual drop box in this data");
        using var r = new Rig(1, box);
        Assert.That(r.C.State.SelectCount, Is.EqualTo(1));
        Assert.That(r.C.State.Rewards, Is.Not.Empty);
        var list = r.Host.Child("m_selectItemList");
        Assert.That(list, Is.Not.Null);
        var dp = r.Host.Get(list, "dataProvider");
        int len = (int)Rt.ToUint32(r.Host.Get(dp, "length"));
        Assert.That(len, Is.GreaterThanOrEqualTo(SelectItemBoxState.MinRows));
        Assert.That(len, Is.GreaterThanOrEqualTo(r.C.State.Rewards.Count));
    }

    [Test]
    public void AHiddenRowKeepsItsPlaceInTheList()
    {
        Packs();
        var tables = new GameTables(packs, "en");
        int box = tables.IndividualDrops.BoxIds.FirstOrDefault(id =>
        {
            var all = new List<ItemDropEntry>();
            tables.IndividualDrops.GetBoxItems(tables.Items, id, 1, 0, false, all);
            int hidden = all.FindIndex(e => e.Tooltip == 2);
            return hidden >= 0 && hidden < all.Count - 1;
        });
        Assume.That(box, Is.Not.EqualTo(0), "no box with a hidden row before another row");
        var rows = SelectItemBoxController.BoxRewards(tables, box, 1, 0);
        var full = new List<ItemDropEntry>();
        tables.IndividualDrops.GetBoxItems(tables.Items, box, 1, 0, false, full);
        Assert.That(rows, Is.EqualTo(full), "bSkipHidden is false: no later row shifts up, the index sent picks the right reward");
        Assert.That(rows.Any(e => e.Tooltip == 2));
    }

    [Test]
    public void NoBoxOrNoSelectCountLeavesTheListEmpty()
    {
        using var r = new Rig(0, 1);
        Assert.That(r.C.State.Rewards, Is.Empty);
        using var r2 = new Rig(1, 0);
        Assert.That(r2.C.State.Rewards, Is.Empty);
        using var r3 = new Rig(1, 1, player: false);
        Assert.That(r3.C.State.Rewards, Is.Empty);
    }

    [Test]
    public void OkOpensWithTheJoinedIndicesOnlyWhenAllArePicked()
    {
        Packs();
        int box = FirstBox(packs, minRewards: 3);
        Assert.That(box, Is.Not.EqualTo(0), "no box with at least 3 rewards");
        using var r = new Rig(2, box);
        Assert.That(r.C.State.Rewards.Count, Is.GreaterThan(2));
        r.Picks.Add(1);
        r.C.OnOk();
        Assert.That(r.Opened, Is.Empty, "one pick of two");
        r.Picks.Add(2);
        r.C.OnOk();
        Assert.That(r.Opened.Single().Item2, Is.EqualTo("1,2"));
        Assert.That(r.Closes, Is.EqualTo(1), "the dialog closed itself");
    }

    [Test]
    public void APickPastTheRewardsIsTheWrongNoticeAndOpensNothing()
    {
        Packs();
        int box = FirstBox(packs);
        Assert.That(box, Is.Not.EqualTo(0), "no individual drop box in this data");
        using var r = new Rig(1, box);
        r.Picks.Add((uint)r.C.State.Rewards.Count);
        r.C.OnOk();
        Assert.That(r.Opened, Is.Empty);
        Assert.That(r.Notices, Is.EqualTo(new[] { "s_selectItemBox_select_wrong" }));
        Assert.That(r.Closes, Is.EqualTo(0));
    }

    [Test]
    public void StateRulesNeedNoMovie()
    {
        var s = new SelectItemBoxState { SelectCount = 2 };
        s.Rewards.Add(new ItemDropEntry(1, 1, 1, 0, 0));
        s.Rewards.Add(new ItemDropEntry(2, 1, 1, 0, 0));
        Assert.That(s.Ok(new uint[] { 0 }, out _), Is.EqualTo(SelectItemBoxState.OkKind.Ignored));
        Assert.That(s.Ok(new uint[] { 0, 2 }, out _), Is.EqualTo(SelectItemBoxState.OkKind.Wrong));
        Assert.That(s.Ok(new uint[] { 1, 0 }, out var idx), Is.EqualTo(SelectItemBoxState.OkKind.Open));
        Assert.That(idx, Is.EqualTo("1,0"));
        Assert.That(s.TooMany(3), Is.True);
        Assert.That(s.TooMany(2), Is.False);
    }
}
