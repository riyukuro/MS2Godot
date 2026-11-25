using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The treasure map appraise dialog (UIItemTreasureMapDialog, uiitemtreasuremapdialog, UI id 0x4f5b), needs MS2_DATA: every
/// TreasureMap item of the data, then the dialog over the real movie: the bag of the category's maps, the pick's cost request,
/// the cost answer, the refused appraise (nothing used), the use with "free" or nothing, the result and the way back. No
/// language's strings are compared.
/// </summary>
public class ItemTreasureMapDialogTests
{
    static PackSet packs;
    static MovieSource src;

    static void Packs()
    {
        if (packs != null) return;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = TestData.Packs(d);
        src = TestData.Movies(packs);
    }

    static List<int> MapIds()
    {
        Packs();
        var t = new GameTables(packs, "en");
        return ItemFunctionSweepTests.ItemIds(packs).Where(id => t.Items.Get(id)?.FunctionName == ItemTreasureMapDialogController.Function).ToList();
    }

    [Test]
    public void EveryTreasureMapItemIsAMapOfTheDialog()
    {
        var ids = MapIds();
        TestContext.Out.WriteLine($"TreasureMap items: {ids.Count} [{string.Join(",", ids)}]");
        if (ids.Count == 0) Assert.Ignore("no TreasureMap item in this data");
        var r = new Rig(ids[0]);
        foreach (int id in ids)
            Assert.That(r.C.IsTreasureMap(Item.Create(r.Model.Tables.Items, id, 1)), Is.True, $"item {id}");
        Assert.That(r.C.IsTreasureMap(Item.Create(r.Model.Tables.Items, NotAMap(r.Model), 1)), Is.False);
    }

    static int NotAMap(UiModel m) => ItemFunctionSweepTests.ItemIds(packs).First(id => m.Tables.Items.Get(id) is { } d && d.FunctionName != ItemTreasureMapDialogController.Function);

    sealed class Rig
    {
        public readonly ItemTreasureMapDialogController C = new();
        public readonly UiModel Model;
        public UiHost Host;
        public readonly Item Map, Other;
        public long Meso = 1_000_000;
        public bool UseOk = true;
        public readonly List<Item> Held = new();
        public readonly List<TreasureMapCostRequest> Sent = new();
        public readonly List<(Item Item, string Arg)> Uses = new();
        public readonly List<int> Notices = new();
        public readonly List<string> Sounds = new();
        public readonly List<int> Categories = new();

        public Rig(int mapId, string argument = "")
        {
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            Map = Item.Create(Model.Tables.Items, mapId, 1);
            Map.Uid = 501;
            Other = Item.Create(Model.Tables.Items, NotAMap(Model), 1);
            Other.Uid = 502;
            Held.Add(Other); Held.Add(Map);
            C.CategoryItems = cat => { Categories.Add(cat); return Held.ToList(); };
            C.FindByUid = uid => Held.FirstOrDefault(i => i.Uid == uid);
            C.RequestUse = (item, arg) => { Uses.Add((item, arg)); return UseOk; };
            C.Send = Sent.Add;
            C.Mesos = () => Meso;
            C.PlaySound = Sounds.Add;
            C.CommonNotice = Notices.Add;
            C.SetFree(argument);
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            Host = new UiHost(p, C, Model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }

        object Board => Host.Child("m_board");
        public bool SubmitEnabled => Rt.ToBoolean(Host.Get(Host.Child("m_submit", Board), "enabled"));
        public bool Visible(string name) => Rt.ToBoolean(Host.Get(Host.Child(name, Board), "visible"));
    }

    static int FirstMap()
    {
        var ids = MapIds();
        if (ids.Count == 0) Assert.Ignore("no TreasureMap item in this data");
        return ids[0];
    }

    [Test]
    public void TheBagListsOnlyTheMapsOfCategorySeven()
    {
        var r = new Rig(FirstMap());
        Assert.That(r.Categories, Is.Not.Empty.And.All.EqualTo(ItemTreasureMapDialogController.Category));
        Assert.That(r.C.Items, Is.EqualTo(new[] { r.Map }));
        Assert.That(r.C.BagSlots, Is.EqualTo(new[] { ItemTreasureMapDialogController.Bag + ".0" }));
        Assert.That(r.Model.ItemSlots[ItemTreasureMapDialogController.Bag + ".0"].Item, Is.SameAs(r.Map));
        Assert.That(r.C.State, Is.Zero);
        Assert.That(r.SubmitEnabled, Is.False, "nothing picked yet");
        r.C.OnSubmitClick();
        Assert.That(r.Uses, Is.Empty, "no target: nothing used");
    }

    [Test]
    public void APickAsksTheCostAndTheAnswerShowsTheMap()
    {
        var r = new Rig(FirstMap(), "FREE");
        Assert.That(r.C.Free, Is.True, "\"free\" without case");
        r.C.SelectRow(0);
        Assert.That(r.Sent, Is.EqualTo(new[] { new TreasureMapCostRequest(r.Map.Uid, true) }));
        Assert.That(r.Uses, Is.Empty, "a pick only asks the cost");
        r.C.OnItemScript(new ItemScriptTreasureMap.Cost(r.Map.Uid, 0));
        Assert.That(r.C.Target, Is.SameAs(r.Map));
        Assert.That(r.Model.ItemSlots[ItemTreasureMapDialogController.Bind].Item, Is.SameAs(r.Map));
        Assert.That(r.SubmitEnabled, Is.True);
        Assert.That(r.Visible("m_desc1") && r.Visible("m_desc2"), Is.True);
        Assert.That(r.Sounds, Is.EqualTo(new[] { ItemTreasureMapDialogController.ChoiceSound }));

        r.C.OnSubmitClick();
        Assert.That(r.Uses, Is.EqualTo(new[] { (r.Map, "free") }), "the free appraise is the map's use with \"free\"");
        Assert.That((r.C.State, r.C.TargetUid, r.C.Target), Is.EqualTo((1, r.Map.Uid, (Item)null)));
        Assert.That(r.SubmitEnabled, Is.False);
        r.C.OnSubmitClick();
        Assert.That(r.Uses, Has.Count.EqualTo(1), "waiting: the button does nothing");
        r.C.SelectRow(0);
        Assert.That(r.Sent, Has.Count.EqualTo(1), "waiting: a pick sends nothing");
    }

    [Test]
    public void ACostTheMesosDoNotCoverIsRefusedWithTheNoticeAndNothingUsed()
    {
        var r = new Rig(FirstMap());
        Assert.That(r.C.Free, Is.False);
        r.Meso = 999;
        r.C.SelectRow(0);
        r.C.OnItemScript(new ItemScriptTreasureMap.Cost(r.Map.Uid, 1000));
        r.C.OnSubmitClick();
        Assert.That(r.Notices, Is.EqualTo(new[] { ItemTreasureMapDialogController.NotEnoughMesoNotice }));
        Assert.That(r.Uses, Is.Empty);
        Assert.That(r.C.State, Is.Zero);
        r.Meso = 1000;
        r.C.OnSubmitClick();
        Assert.That(r.Uses, Is.EqualTo(new[] { (r.Map, "") }), "the paid appraise is the use with no text");

        var no = new Rig(FirstMap());
        no.C.SelectRow(0);
        no.C.OnItemScript(new ItemScriptTreasureMap.Cost(no.Map.Uid, -1));
        Assert.That(no.SubmitEnabled, Is.False, "a negative cost cannot be appraised");
        var other = new Rig(FirstMap());
        other.C.OnItemScript(new ItemScriptTreasureMap.Cost(other.Other.Uid, 0));
        Assert.That(other.C.Target, Is.Null, "a cost for an item that is no map picks nothing");
        other.UseOk = false;
        other.C.OnItemScript(new ItemScriptTreasureMap.Cost(other.Map.Uid, 0));
        other.C.OnSubmitClick();
        Assert.That((other.Uses.Count, other.C.State), Is.EqualTo((1, 0)), "a use not sent keeps the select state");
    }

    [Test]
    public void TheResultShowsTheItemAndBackAsksTheHeldMapsCostAgain()
    {
        var r = new Rig(FirstMap());
        r.C.SelectRow(0);
        r.C.OnItemScript(new ItemScriptTreasureMap.Cost(r.Map.Uid, 0));
        r.C.OnSubmitClick();
        int reward = r.Other.ItemId;
        r.C.OnItemScript(new ItemScriptTreasureMap.Result(true, reward, 2));
        Assert.That(r.C.State, Is.EqualTo(2));
        Assert.That(r.Model.ItemSlots[ItemTreasureMapDialogController.Bind].Item.ItemId, Is.EqualTo(reward));
        Assert.That(r.SubmitEnabled, Is.True, "the back button");
        Assert.That(r.Sounds.Last(), Is.EqualTo(ItemTreasureMapDialogController.StampSound));
        r.C.OnSubmitClick();
        Assert.That(r.C.State, Is.Zero);
        Assert.That(r.Model.ItemSlots.ContainsKey(ItemTreasureMapDialogController.Bind), Is.False, "the shown slot released");
        Assert.That(r.Sent.Last(), Is.EqualTo(new TreasureMapCostRequest(r.Map.Uid, false)), "the map is still held: its cost again");
        Assert.That(r.C.TargetUid, Is.Zero);

        var gone = new Rig(FirstMap());
        gone.C.SelectRow(0);
        gone.C.OnItemScript(new ItemScriptTreasureMap.Cost(gone.Map.Uid, 0));
        gone.C.OnSubmitClick();
        gone.Held.Remove(gone.Map);
        gone.C.OnItemScript(new ItemScriptTreasureMap.Result(true, reward, 1));
        Assert.That(gone.C.Items, Is.Empty, "the bag refreshed without the used map");
        int sent = gone.Sent.Count;
        gone.C.OnSubmitClick();
        Assert.That(gone.Sent, Has.Count.EqualTo(sent), "the map is gone: no cost asked");
    }

    [Test]
    public void TheEndOfTheAppraiseGoesBackAndTheCloseClearsTheFreeFlag()
    {
        var r = new Rig(FirstMap(), "free");
        r.C.SelectRow(0);
        r.C.OnItemScript(new ItemScriptTreasureMap.Cost(r.Map.Uid, 0));
        r.C.OnSubmitClick();
        r.C.OnItemScript(new ItemScriptTreasureMap.Result(false, 0, 0));
        Assert.That(r.C.State, Is.Zero);
        Assert.That(r.SubmitEnabled, Is.False);
        r.C.Close();
        Assert.That((r.C.Free, r.C.TargetUid, r.C.Items.Count), Is.EqualTo((false, 0L, 0)));
        Assert.That(r.Model.ItemSlots.Keys.Any(k => k.StartsWith(ItemTreasureMapDialogController.Name)), Is.False);
    }
}
