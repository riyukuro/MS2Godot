using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;
using Maple2.PacketLib.Tools;

namespace Ms2.Ui.Tests;

/// <summary>The item merge window (uiitemmergeoptiondialog, UI 0x4e68; needs MS2_DATA): phases, picks, CheckMerge's refusals,
/// the warn box, the sends, the close refusal in phases 4 and 5, the bag order and the option order.</summary>
public class ItemMergeOptionDialogTests
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

    static string Hex(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return Convert.ToHexString(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    sealed class Rig
    {
        public ItemMergeOptionDialogController C = new();
        public List<IOutPacket> Sent = new();
        public List<string> Notices = new(), Missing = new(), Chat = new();
        public List<MessageBoxData> Boxes = new();
        public Dictionary<long, Item> Inv = new();
        public List<Item> Bag = new(), Worn = new();
        public long Meso = 1000;
        public Dictionary<int, int> ById = new(), ByTag = new();
        public UiModel Model;
        public UiHost Host;

        public Rig()
        {
            Packs();
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            C.Send = Sent.Add;
            C.Find = u => Inv.GetValueOrDefault(u);
            C.BagItems = () => Bag;
            C.EquippedItems = () => Worn;
            C.Meso = () => Meso;
            C.CountById = i => ById.GetValueOrDefault(i);
            C.CountByTag = i => ByTag.GetValueOrDefault(i);
            C.Notice = Notices.Add;
            C.MissingBox = Missing.Add;
            C.ChatLine = Chat.Add;
            C.ShowData = Boxes.Add;
            C.Text = k => k;
            C.ItemName = id => "item" + id;
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            Host = new UiHost(p, C, Model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }

        public Item Add(long uid, int rarity = 3, bool fusionable = true, bool merged = false, bool worn = false)
        {
            // item data is shared per id, so the fusionable flag needs two ids
            var known = new[] { 20000001, 20000002, 30000001, 11300001, 12000001 }.Where(i => !string.IsNullOrEmpty(Model.Tables.ItemName(i))).ToArray();
            if (known.Length < 2) Assert.Ignore("no two known items");
            int id = fusionable ? known[0] : known[1];
            Model.Tables.Items.Get(known[0]).Fusionable = 1;
            Model.Tables.Items.Get(known[1]).Fusionable = 0;
            var it = Item.Create(Model.Tables.Items, id, rarity);
            it.Uid = uid; it.Rarity = rarity;
            var e = new EquipItem { Id = id, Uid = uid };
            if (merged) e.Options.Stat[4][4] = 10;
            it.Record = e;
            Inv[uid] = it;
            (worn ? Worn : Bag).Add(it);
            return it;
        }

        /// <summary>Ready with an item and a material picked and a proxy asking for meso and one ingredient.</summary>
        public void Picked(long item, long material, long meso = 500, params MergeIngredient[] ing)
        {
            C.SelectItem(item);
            C.SelectMaterial(material);
            C.OnPacket(new ItemMergePacket(2) { Proxy = new MergeOptionProxy(meso, ing, Array.Empty<int>(), Array.Empty<MergeOptionRange>(), Array.Empty<MergeOptionRange>()) });
            Sent.Clear();
        }
    }

    [Test]
    public void LoadSendsInfoAndStartsEmpty()
    {
        var r = new Rig();
        Assert.That(r.C.Phase, Is.EqualTo(0));
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemMergeRequest.Info())));
        Assert.That(r.Host.Child("m_itemBag"), Is.Not.Null);
        Assert.That(r.Host.Child("m_ingredientBag"), Is.Not.Null);
        Assert.That(r.Host.Child("m_merge"), Is.Not.Null);
    }

    [Test]
    public void UserDataAnswersTooltipsByGuidAndIdAndIsMergeable()
    {
        // vfunc_45 (:448): getTooltipByGUID = FUN_1409e0d40 of FindMergeItem, getTooltipByID = FUN_1409e1770(id, rank)
        var r = new Rig();
        var it = r.Add(41);
        var byUid = r.C.GetUserData("getTooltipByGUID", new object[] { "41" });
        Assert.That(byUid["itemName"], Is.EqualTo(r.Model.Tables.ItemName(it.ItemId)));
        Assert.That(r.C.GetUserData("getTooltipByGUID", new object[] { "42" }), Is.Null);
        var byId = r.C.GetUserData("getTooltipByID", new object[] { (uint)it.ItemId, 2u });
        Assert.That(byId["itemName"], Is.EqualTo(r.Model.Tables.ItemName(it.ItemId)));
        Assert.That(byId["itemGrade"], Is.EqualTo(2));
        Assert.That(r.C.GetUserData("getTooltipByID", new object[] { 1u, 2u }), Is.Null);
        Assert.That(r.C.GetUserValue("isMergeable", Array.Empty<object>()), Is.EqualTo(true));
    }

    [Test]
    public void InfoSubZeroIsPhaseOneAndSelectionIsIgnoredBefore()
    {
        var r = new Rig();
        r.Add(1);
        r.Sent.Clear();
        r.C.SelectItem(1);
        Assert.That((r.C.Phase, r.Sent.Count), Is.EqualTo((0, 0)));
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        Assert.That(r.C.Phase, Is.EqualTo(1));
    }

    [Test]
    public void PicksSendSelectionAndPreview()
    {
        var r = new Rig();
        r.Add(1); r.Add(2);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        r.Sent.Clear();
        r.C.SelectItem(1);
        Assert.That(r.C.Phase, Is.EqualTo(2));
        Assert.That(r.C.Special, Is.False);
        r.C.SelectMaterial(2);
        Assert.That(r.C.Phase, Is.EqualTo(3));
        Assert.That(r.Sent.Select(Hex), Is.EqualTo(new[] { Hex(ItemMergeRequest.Selection(1)), Hex(ItemMergeRequest.Preview(1, 2)) }));
    }

    [Test]
    public void PickingAMergedItemMarksItSpecial()
    {
        var r = new Rig();
        r.Add(1, merged: true);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        r.C.SelectItem(1);
        Assert.That(r.C.Special, Is.True);
    }

    [Test]
    public void CheckMergeRefusalsInClientOrder()
    {
        var r = new Rig();
        r.Add(1); r.Add(2);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        Assert.That(r.C.CheckMerge(), Is.EqualTo(ItemMergeOptionDialogController.CheckNoItem));
        r.C.SelectItem(1);
        Assert.That(r.C.CheckMerge(), Is.EqualTo(ItemMergeOptionDialogController.CheckNoMaterial));
        r.Picked(1, 2, 500, new MergeIngredient(900, 0, 3));
        r.Meso = 499;
        Assert.That(r.C.CheckMerge(), Is.EqualTo(ItemMergeOptionDialogController.CheckLackMeso));
        r.Meso = 500;
        Assert.That(r.C.CheckMerge(), Is.EqualTo(ItemMergeOptionDialogController.CheckLackMaterial));
        r.ById[900] = 3;
        Assert.That(r.C.CheckMerge(), Is.EqualTo(ItemMergeOptionDialogController.CheckOk));
    }

    [Test]
    public void IngredientWithATagCountsByTag()
    {
        var r = new Rig();
        r.Add(1); r.Add(2);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        r.Picked(1, 2, 0, new MergeIngredient(900, 7, 2));
        r.ById[900] = 5;
        Assert.That(r.C.CheckMerge(), Is.EqualTo(ItemMergeOptionDialogController.CheckLackMaterial));
        r.ByTag[7] = 2;
        Assert.That(r.C.CheckMerge(), Is.EqualTo(ItemMergeOptionDialogController.CheckOk));
    }

    [Test]
    public void RefusedMergeShowsItsNoticeAndSendsNothing()
    {
        var r = new Rig();
        r.Add(1); r.Add(2);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        r.C.OnCallback("onMerge", Array.Empty<object>());
        r.Picked(1, 2, 500, new MergeIngredient(900, 0, 3));
        r.Meso = 0;
        r.C.ClickMerge();
        r.Meso = 500;
        r.C.ClickMerge();
        Assert.That(r.Notices, Is.EqualTo(new[]
        {
            "s_item_merge_option_error_invalid_mergeitem",
            "s_item_merge_option_error_lack_meso",
            "s_item_merge_option_error_lack_material",
        }));
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.C.Phase, Is.EqualTo(3));
    }

    [Test]
    public void MergeSendsAndEntersPhaseFiveWhichRefusesClose()
    {
        var r = new Rig();
        r.Add(1); r.Add(2);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        r.Picked(1, 2);
        Assert.That(r.C.CanClose, Is.True);
        r.C.ClickMerge();
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemMergeRequest.Merge(1, 2))));
        Assert.That((r.C.Phase, r.C.CanClose), Is.EqualTo((5, false)));
    }

    [Test]
    public void MergedItemAsksFirstAndTheFirstButtonSends()
    {
        var r = new Rig();
        r.Add(1, merged: true); r.Add(2);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        r.Picked(1, 2);
        r.C.ClickMerge();
        Assert.That(r.Sent, Is.Empty);
        Assert.That((r.C.Phase, r.C.CanClose), Is.EqualTo((4, false)));
        var box = r.Boxes.Single();
        Assert.That(box.Type, Is.EqualTo(ItemMergeOptionDialogController.WarnBoxType));
        box.Callback(1, box);
        Assert.That(r.Sent, Is.Empty);
        box.Callback(0, box);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemMergeRequest.Merge(1, 2))));
        Assert.That(r.C.Phase, Is.EqualTo(5));
    }

    [Test]
    public void MaterialLookupIsInventoryOnly()
    {
        var r = new Rig();
        r.Add(1); r.Add(2);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        r.C.FindBag = u => r.Bag.FirstOrDefault(i => i.Uid == u);
        r.C.SelectItem(1);
        r.Bag.RemoveAll(i => i.Uid == 2);                  // only equipped now
        r.C.SelectMaterial(2);
        Assert.That(r.C.CheckMerge(), Is.EqualTo(ItemMergeOptionDialogController.CheckNoMaterial));
    }

    [Test]
    public void OptionChoiceWithAMissingItemKeepsThePhase()
    {
        var r = new Rig();
        r.Add(1); r.Add(2);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        r.Picked(1, 2);
        r.Inv.Remove(1);
        int phase = r.C.Phase;
        r.C.OnPacket(new ItemMergePacket(4) { Normal = 4, Value = 10 });
        Assert.That(r.C.Phase, Is.EqualTo(phase));
    }

    [Test]
    public void SelectingAMissingItemKeepsSpecial()
    {
        var r = new Rig();
        r.Add(1, merged: true);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        r.C.SelectItem(1);
        Assert.That(r.C.Special, Is.True);
        r.C.SelectItem(99);
        Assert.That(r.C.Special, Is.True);
    }

    [Test]
    public void OptionChoiceAnswerIsPhaseFourAndTheChoiceIsSent()
    {
        var r = new Rig();
        r.Add(1); r.Add(2);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        r.Picked(1, 2);
        r.C.OnPacket(new ItemMergePacket(4) { Normal = 4, Value = 10 });
        Assert.That(r.C.Phase, Is.EqualTo(4));
        r.C.OnCallback("onMergeSelectOption", new object[] { true });
        r.C.OnCallback("onMergeSelectOption", new object[] { false });
        Assert.That(r.Sent.Select(Hex), Is.EqualTo(new[] { Hex(ItemMergeRequest.ChoiceOf(1)), Hex(ItemMergeRequest.ChoiceOf(0)) }));
    }

    [Test]
    public void ResultIsPhaseSixAndClearsTheTalk()
    {
        var r = new Rig();
        var it = r.Add(1); r.Add(2);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        r.Picked(1, 2);
        r.C.TalkId = 5;
        r.C.ShowMergeResult(it);
        Assert.That((r.C.Phase, r.C.TalkId, r.C.CanClose), Is.EqualTo((6, 0, true)));
    }

    [Test]
    public void ResultTalkAndDebugCommands()
    {
        var r = new Rig();
        r.Add(1); r.Add(2);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        r.Picked(1, 2);
        r.C.TalkId = 3; r.C.TalkConstant = 9;
        r.C.OnCallback("onShowResult", Array.Empty<object>());
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(new ItemMergeResultTalkRequest(false, 0, 9, 3))));
        r.Sent.Clear();
        r.C.OnCallback("onDebug", Array.Empty<object>());
        Assert.That(r.Sent, Is.Empty);
        r.C.DebugLevelNow = 50;
        r.C.OnCallback("onDebug", Array.Empty<object>());
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemMergeRequest.Material(1, 2))));
    }

    [Test]
    public void ResetDialogRebuildsTheBagAndIsPhaseOne()
    {
        var r = new Rig();
        r.Add(1); r.Add(2);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        r.Picked(1, 2, 500);
        r.C.OnCallback("onResetDialog", Array.Empty<object>());
        Assert.That((r.C.Phase, r.C.Proxy.Meso), Is.EqualTo((1, 0L)));
    }

    [Test]
    public void BagHoldsOnlyFusionableSortedEquippedMergedThenRarity()
    {
        var r = new Rig();
        r.Add(1, rarity: 5);
        r.Add(2, rarity: 3, fusionable: false);
        r.Add(3, rarity: 4, merged: true);
        r.Add(4, rarity: 1, worn: true);
        r.Add(5, rarity: 4);
        r.C.ResetMergeItems();
        Assert.That(r.C.MergeItems.Select(i => i.Uid), Is.EqualTo(new long[] { 4, 3, 1, 5 }));
        Assert.That(r.C.GetBagSlotBindings(r.C.MergeBag).Count(), Is.EqualTo(4));
    }

    [Test]
    public void MaterialBagFollowsTheModeOneUids()
    {
        var r = new Rig();
        r.Add(1); r.Add(2, rarity: 5); r.Add(3, fusionable: false);
        r.C.OnPacket(new ItemMergePacket(1) { Materials = new long[] { 1, 2, 3, 99 } });
        Assert.That(r.C.MaterialItems.Select(i => i.Uid), Is.EqualTo(new long[] { 2, 1 }));
    }

    [Test]
    public void MissingItemAnswersGiveBoxAndChatAndPhaseOne()
    {
        var r = new Rig();
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 2, ItemId = 123 });
        Assert.That((r.Missing.Count, r.Chat.Count, r.C.Phase), Is.EqualTo((1, 1, 1)));
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 1, ItemId = 123, Uid = 77 });
        Assert.That((r.Missing.Count, r.C.Phase), Is.EqualTo((2, 1)));
    }

    [Test]
    public void ErrorCodesMapToTheirNotices()
    {
        var r = new Rig();
        foreach (int e in new[] { 3, 4, 1, 9, 5, 6, 7, 2, 8, 0 }) r.C.ShowMergeError(e);
        Assert.That(r.Notices, Is.EqualTo(new[]
        {
            "s_item_merge_option_error_invalid_mergeitem", "s_item_merge_option_error_invalid_material",
            "s_item_merge_option_error_lack_material", "s_item_merge_option_error_unknown",
            "s_item_merge_option_error_invalid_merge_scroll", "s_item_merge_option_error_invalid_revert_rank",
            "s_item_merge_option_error_invalid_revert_option", "s_item_merge_option_error_lack_meso",
        }));
    }

    [Test]
    public void OptionOrderFollowsTheConstructorTable()
    {
        string Name((int, int) o) => o.ToString();
        Assert.That(ItemMergeOptionDialogController.CompareOptions((4, 0), (7, 0), Name), Is.LessThan(0));
        Assert.That(ItemMergeOptionDialogController.CompareOptions((-1, 26), (0, 0), Name), Is.GreaterThan(0));
        Assert.That(ItemMergeOptionDialogController.CompareOptions((-1, 55), (999, 0), Name), Is.LessThan(0));
        Assert.That(ItemMergeOptionDialogController.CompareOptions((998, 0), (999, 0), Name), Is.LessThan(0));
    }

    [Test]
    public void CloseClearsThePicksAndRaisesClosed()
    {
        var r = new Rig();
        r.Add(1); r.Add(2);
        r.C.OnPacket(new ItemMergePacket(0) { Sub = 0 });
        r.Picked(1, 2);
        int closed = 0; r.C.Closed += () => closed++;
        r.C.Close();
        r.C.Close();
        Assert.That((r.C.ItemUid, r.C.MaterialUid, r.C.Phase, closed), Is.EqualTo((0L, 0L, 0, 1)));
    }
}
