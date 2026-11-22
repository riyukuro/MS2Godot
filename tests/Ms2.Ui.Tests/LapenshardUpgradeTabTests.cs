using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Maple2.PacketLib.Tools;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UILapenshardUpgradeTab's state machine: sends of modes 3, 4, 5 and the inbound kinds 4, 5, 6.</summary>
public class LapenshardUpgradeTabTests
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
        public LapenshardSystemController C; public LapenshardUpgradeTabPage Page; public GameTables Tables;
        public List<IOutPacket> Sent = new(); public List<string> Notices = new(); public List<int> Ids = new(); public List<int> Events = new();
        public int Meso; public long Have; public bool Battle;
    }

    static Rig Load(Func<GameTables, (int, int)[]> worn = null, bool player = true)
    {
        var tables = new GameTables(Packs(), "en");
        var model = new UiModel(tables) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f), HasLocalPlayer = player };
        model.Player.Level = 99; model.Player.JobGroup = 10;
        model.Player.Lapenshards.Apply(new LapenshardMessage.Worn(worn?.Invoke(tables) ?? Array.Empty<(int, int)>()));
        var r = new Rig { Tables = tables };
        var c = new LapenshardSystemController { Text = k => k, Send = p => { r.Sent.Add(p); return true; }, InBattle = () => r.Battle, ShowBox = _ => { } };
        c.NoticeText += r.Notices.Add;
        c.AddTab(new LapenshardEquipTabPage());
        r.Page = new LapenshardUpgradeTabPage { Mesos = () => r.Have, CountItems = (_, _) => 1000, CountByTag = _ => 1000 };
        r.Page.SystemNoticeId += r.Ids.Add;
        r.Page.PanelEvent += r.Events.Add;
        c.AddTab(r.Page);
        r.C = c;
        var (rt, p) = src.Open(c.Movie, x => x.Budget = 40_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        r.Page.Show(null, new object());
        return r;
    }

    /// <summary>A table lapenshard that has an upgrade and needs ingredients.</summary>
    static Item Source(GameTables t, long uid = 77)
    {
        foreach (var id in t.Lapenshards.ItemIds().OrderBy(i => i))
        {
            var row = t.Lapenshards.Get(id);
            if (row.Next == 0 || row.MinCount <= 0) continue;
            var it = Item.Create(t.Items, id, 0);
            if (it == null) continue;
            it.Uid = uid;
            return it;
        }
        Assert.Ignore("no upgradable lapenshard row");
        return null;
    }

    [Test]
    public void SelectingASourceSendsTheModeThreeHeadAndOpensTheSelectState()
    {
        var r = Load();
        var s = Source(r.Tables);
        Assert.That(r.Page.SendMode3(s), Is.True);
        Assert.That(r.Sent, Is.EqualTo(new IOutPacket[] { new LapenshardPreviewRequest(77, s.ItemId, 0) }));
        Assert.That((r.Page.State, r.Page.Source, r.Page.IngredientTotal), Is.EqualTo((1, s, 0)));
        Assert.That(r.Events, Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void NothingIsSelectedInBattleOrWithoutAPlayerOrAUidlessItemNotWorn()
    {
        var r = Load();
        r.Battle = true;
        Assert.That(r.Page.SendMode3(Source(r.Tables)), Is.False);
        Assert.That((r.Sent, r.Page.State, r.Page.Source), Is.EqualTo((new List<IOutPacket>(), 0, (Item)null)));
        r.Battle = false;
        var uidless = Source(r.Tables, 0);
        Assert.That(r.Page.SendMode3(uidless), Is.False, "the slot lookup finds nothing");
        Assert.That(r.Sent, Is.Empty);
        Assert.That((r.Page.State, r.Page.Source), Is.EqualTo((1, uidless)), "the source and state are set before the lookup");
        Assert.That(Load(player: false).Page.SendMode3(Source(r.Tables)), Is.False);
    }

    [Test]
    public void AWornItemWithoutAUidIsFoundByItsSlot()
    {
        var s0 = Source(new GameTables(Packs(), "en"), 0);
        var r = Load(_ => new[] { (4, s0.ItemId) });
        Assert.That(r.Page.SendMode3(s0), Is.True);
        Assert.That(r.Sent, Is.EqualTo(new IOutPacket[] { new LapenshardPreviewRequest(0, s0.ItemId, 4) }));
    }

    [Test]
    public void ChangingAnIngredientMovesTheTotalAndSendsModeFour()
    {
        var r = Load();
        var s = Source(r.Tables);
        r.Page.SendMode3(s);
        r.Sent.Clear(); r.Events.Clear();
        r.Page.ChangeIngredientCount(500, 3);
        r.Page.ChangeIngredientCount(501, 2);
        r.Page.ChangeIngredientCount(500, -3);
        Assert.That(r.Page.Ingredients, Is.EqualTo(new Dictionary<long, int> { [501] = 2 }), "an entry at zero is removed");
        Assert.That(r.Page.IngredientTotal, Is.EqualTo(2));
        Assert.That(r.Events, Is.EqualTo(new[] { 4, 4, 4 }));
        Assert.That(r.Sent, Is.EqualTo(new IOutPacket[]
        {
            new LapenshardAddFodder(77, s.ItemId, 0, 3), new LapenshardAddFodder(77, s.ItemId, 0, 5), new LapenshardAddFodder(77, s.ItemId, 0, 2),
        }));
    }

    [Test]
    public void TheUpgradeNeedsEnoughIngredientsAndMeso()
    {
        var r = Load();
        var s = Source(r.Tables);
        var row = r.Tables.Lapenshards.Get(s.ItemId);
        Assert.That(r.Page.SendMode5(), Is.False, "state 0");
        r.Page.SendMode3(s);
        r.Sent.Clear();
        r.Have = row.Meso;
        Assert.That(r.Page.SendMode5(), Is.False, "below the group's min count");
        Assert.That(r.Notices, Is.EqualTo(new[] { "s_lapenshard_tabname_upgrade_lack_ingredient" }));
        r.Page.ChangeIngredientCount(9, row.MinCount);
        r.Sent.Clear(); r.Notices.Clear();
        if (row.Meso > 0)
        {
            r.Have = row.Meso - 1;
            Assert.That(r.Page.SendMode5(), Is.False);
            Assert.That(r.Ids, Is.EqualTo(new[] { 0x34 }));
            Assert.That(r.Sent, Is.Empty);
            r.Have = row.Meso;
        }
        r.Page.CountItems = (_, _) => 0; r.Page.CountByTag = _ => 0;
        bool needsStock = row.Ingredients.Any(i => i.ItemId != 0 && i.Count > 0);
        Assert.That(r.Page.SendMode5(), Is.EqualTo(!needsStock));
        if (needsStock) { Assert.That(r.Notices, Is.EqualTo(new[] { "s_lapenshard_tabname_upgrade_lack_ingredient" })); Assert.That(r.Sent, Is.Empty); }
        r.Page.CountItems = (_, _) => 1000; r.Page.CountByTag = _ => 1000;
        r.Page.UpdateSaleRate();
        r.Sent.Clear();
        Assert.That(r.Page.SendMode5(), Is.True);
        Assert.That(r.Sent, Has.Count.EqualTo(1));
        Assert.That(r.Sent[0], Is.EqualTo(new LapenshardUpgrade(77, s.ItemId, 0, new[] { (9L, row.MinCount) }, 0)).Using<IOutPacket>(PacketEq));
        Assert.That(r.Page.State, Is.EqualTo(2));
    }

    static bool PacketEq(IOutPacket a, IOutPacket b)
    {
        byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
        return Bytes(a).SequenceEqual(Bytes(b));
    }

    static Rig Requested(out Item s)
    {
        var r = Load();
        s = Source(r.Tables);
        var row = r.Tables.Lapenshards.Get(s.ItemId);
        r.Page.SendMode3(s);
        r.Page.ChangeIngredientCount(9, row.MinCount);
        r.Have = row.Meso;
        Assert.That(r.Page.SendMode5(), Is.True);
        r.Events.Clear();
        return r;
    }

    [Test]
    public void ASecondUpgradeRequestWhileOneIsPendingSendsNothing()
    {
        var r = Requested(out _);
        Assert.That(r.Page.State, Is.EqualTo(2));
        r.Sent.Clear();
        Assert.That(r.Page.SendMode5(), Is.False, "state 2 blocks a second mode 5 (uilapenshardsystemdialog.cpp:2143)");
        r.Page.OnCallback("OnAction", new object[] { 1 });
        Assert.That(r.Sent, Is.Empty);
    }

    [Test]
    public void TheTagCountSkipsItemsThePlayerCannotUse()
    {
        Packs();
        var tables = new GameTables(Packs(), "en");
        var row = tables.Lapenshards.ItemIds().Select(i => tables.Lapenshards.Get(i)).SelectMany(w => w.Ingredients)
            .First(i => i.ItemId != 0 && Ms2.Ui.Items.ItemTags.ByName(tables.Items.Get(i.ItemId)?.StringTag) != 0);
        int tag = Ms2.Ui.Items.ItemTags.ByName(tables.Items.Get(row.ItemId).StringTag);
        var bag = new[] { new Ms2.Net.Packets.EquipItem { Id = row.ItemId, Uid = 1, Amount = 7 }, new Ms2.Net.Packets.EquipItem { Id = row.ItemId, Uid = 2, Amount = 5 } };
        Assert.That(LapenshardUpgradeTabPage.TagCounter(() => bag, tables.Items, _ => 0)(tag), Is.EqualTo(12));
        Assert.That(LapenshardUpgradeTabPage.TagCounter(() => bag, tables.Items, it => it.Record.Uid == 2 ? 1 : 0)(tag), Is.EqualTo(7), "an unusable item is not counted (inventory.cpp:845)");
        Assert.That(LapenshardUpgradeTabPage.TagCounter(() => null, tables.Items, _ => 0)(tag), Is.EqualTo(0));
    }

    [Test]
    public void ThePreviewRateIsKept()
    {
        var r = Load();
        r.Page.OnPacket(new LapenshardMessage.Preview(4500));
        Assert.That(r.Page.SuccessRate, Is.EqualTo(4500));
        Assert.That(r.Events, Is.EqualTo(new[] { 5 }));
    }

    [Test]
    public void TheResultOfTheSourceEndsTheRequestAndFallsThroughToEventNine()
    {
        // the ingredient panel answers events 6 and 7 with vfunc_4, which sends the tab event 0xd (ahead of the spy here)
        var r = Requested(out var s);
        r.Page.OnPacket(new LapenshardMessage.UpgradeResult(77, s.ItemId + 1, 0, true));
        Assert.That((r.Page.State, r.Events), Is.EqualTo((2, new List<int>())), "another item's result is ignored");
        r.Page.OnPacket(new LapenshardMessage.UpgradeResult(77, s.ItemId, 0, true));
        Assert.That((r.Page.State, r.Events), Is.EqualTo((3, new List<int> { 0xd, 6, 9 })));
        var f = Requested(out var s2);
        f.Page.OnPacket(new LapenshardMessage.UpgradeResult(77, s2.ItemId, 0, false));
        Assert.That(f.Events, Is.EqualTo(new[] { 0xd, 7, 9 }));
    }

    [Test]
    public void AResultOutsideTheRequestedStateIsIgnoredAndKindSixIsEventNine()
    {
        var r = Load();
        var s = Source(r.Tables);
        r.Page.SendMode3(s);
        r.Events.Clear();
        r.Page.OnPacket(new LapenshardMessage.UpgradeResult(77, s.ItemId, 0, true));
        Assert.That((r.Page.State, r.Events), Is.EqualTo((1, new List<int>())));
        r.Page.OnPacket(new LapenshardMessage.UpgradeDone());
        Assert.That(r.Events, Is.EqualTo(new[] { 9 }));
    }

    [Test]
    public void TheCallbacksResetOrFinishAndEventTwoClearsTheStartStates()
    {
        var r = Requested(out _);
        Assert.That(r.Page.OnCallback("OnProcessingComplete", Array.Empty<object>()), Is.False);
        Assert.That((r.Page.State, r.Events), Is.EqualTo((4, new List<int> { 0xc })));
        r.Events.Clear();
        Assert.That(r.Page.OnCallback("OnAction", new object[] { 3u }), Is.True);
        Assert.That((r.Page.State, r.Page.Source, r.Events), Is.EqualTo((0, (Item)null, new List<int> { 8 })));
        var x = Load();
        x.Page.SendMode3(Source(x.Tables));
        x.Page.ChangeIngredientCount(1, 2);
        x.Page.Notify(2);
        Assert.That((x.Page.State, x.Page.IngredientTotal, x.Page.Source), Is.EqualTo((0, 0, (Item)null)));
        x.Battle = true;
        Assert.That(x.Page.OnCallback("OnAction", new object[] { 1u }), Is.True, "the use check refuses, the callback is handled");
    }
}
