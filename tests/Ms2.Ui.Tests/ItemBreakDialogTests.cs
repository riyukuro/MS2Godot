using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;
using Maple2.PacketLib.Tools;

namespace Ms2.Ui.Tests;

/// <summary>The item break dialogs (uiitembreakdialog, uiitembreakdonationdialog) over the manager (needs MS2_DATA): the movie's
/// children, the sends on load / close / break, the rarity confirms, the result and preview rows, the slot paths.</summary>
public class ItemBreakDialogTests
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

    static string Hex(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return Convert.ToHexString(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    sealed class Rig
    {
        public ItemBreakDialogController C;
        public ItemBreakManager M = new();
        public List<IOutPacket> Sent = new();
        public List<MessageBoxData> Boxes = new();
        public List<string> Sounds = new();
        public Dictionary<long, Item> Inv = new();
        public UiModel Model;
        public UiHost Host;

        public Rig(bool donation = false, bool flash = false)
        {
            Packs();
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            M.Send = Sent.Add;
            M.Find = u => Inv.GetValueOrDefault(u);
            M.ShowBox = Boxes.Add;
            C = new ItemBreakDialogController(donation) { Mgr = M, ShowData = Boxes.Add };
            C.Sound += Sounds.Add;
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            if (flash)
            {
                // the client's Flash table (korFlash, usmSubtitleText, stringMovie of the string folder); a missing key shows itself
                var table = GameStrings.FlashTable(packs, "en", LobbyFeatures.LocaleOf(packs));
                p.Translate = k => table.TryGetValue(k, out var t) ? t : k;
            }
            else p.Translate = k => k.TrimStart('$');
            Host = new UiHost(p, C, Model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }

        public Item Add(long uid, int rarity = 3)
        {
            int id = new[] { 20000001, 20000002, 30000001, 11300001, 12000001 }.FirstOrDefault(i => !string.IsNullOrEmpty(Model.Tables.ItemName(i)));
            if (id == 0) Assert.Ignore("no known item");
            var it = Item.Create(Model.Tables.Items, id, rarity);
            it.Uid = uid; it.Rarity = rarity;
            it.Record = new EquipItem { Id = id, Uid = uid };
            Inv[uid] = it;
            return it;
        }

        public void Put(long uid, int slot, int amount = 1)
        {
            M.OnPacket(new ItemDismantlePacket(1) { Uid = uid, Slots = new[] { new DismantleSlot((ushort)slot, uid, amount) } });
        }
    }

    /// <summary>FUN_1419a1430 (uiinventorydialog.cpp:2307) sets "bagId" on the slot list the m_itemBag wrapper holds as "instance". Set on the
    /// wrapper (the old code) nothing asked GameDataModel.getBagSlotBindings, the list made no tiles and every staged item drew in no slot.</summary>
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(6)]
    [TestCase(9)]
    public void TheStagedItemsGridHasATileBoundToEverySlotForEachDismantleInput(int type)
    {
        var r = new Rig();
        r.C.OnCallback("onbundletype", new object[] { type });
        r.Add(500);
        r.Put(500, 0);
        for (int i = 0; i < 30; i++) r.Host.Player.Tick();
        var list = r.Host.Get(r.Host.Child("m_itemBag"), "instance");
        Assert.That(Rt.ToString(r.Host.Get(list, "bagId")), Is.EqualTo("ItemBreakDialog"));
        Assert.That(Rt.ToNumber(r.Host.Get(r.Host.Get(list, "dataProvider"), "length")), Is.EqualTo(ItemBreakManager.Slots));
        var tiles = r.Host.Invoke(list, "getChildAt", 1);
        Assert.That(Rt.ToNumber(r.Host.Get(tiles, "numChildren")), Is.EqualTo(ItemBreakManager.Slots), "one tile per slot");
        Assert.That(r.Host.BindingNames, Does.Contain("ItemBreakDialog.0"), "the staged slot's tile asked for its binding");
        Assert.That(r.C.GetBinding("ItemBreakDialog.0"), Is.Not.Null);
        Assert.That(r.C.GetBinding("ItemBreakDialog.1"), Is.Null, "an empty slot has no binding");
    }

    [Test]
    public void LoadSendsModeOneAndBindsTheBag()
    {
        var r = new Rig();
        Assert.That(r.M.DialogOpen, Is.True);
        Assert.That(r.M.Type, Is.EqualTo(1));
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.SetMode(1))));
        Assert.That(Rt.ToString(r.Host.Get(r.Host.Child("instance", r.Host.Child("m_itemBag")), "bagId")), Is.EqualTo("ItemBreakDialog"));
        Assert.That(r.C.GetBagSlotBindings("ItemBreakDialog").Count(), Is.EqualTo(100));
        Assert.That(r.Host.Child("m_break"), Is.Not.Null);
    }

    [Test]
    public void DonationLoadSendsModeTwoUnderItsOwnName()
    {
        var r = new Rig(true);
        Assert.That(r.M.Type, Is.EqualTo(2));
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.SetMode(2))));
        Assert.That(Rt.ToString(r.Host.Get(r.Host.Child("instance", r.Host.Child("m_itemBag")), "bagId")), Is.EqualTo("ItemBreakDonationDialog"));
    }

    [Test]
    public void InventoryButtonsAreRefreshedWhenTheDialogIsUnloadedNotWhenTheCloseStarts()
    {
        // vfunc_26 (@0x1419a1830) is the unload, after the close tween: the inventory's open-dialog test (vfunc_55) then sees it closed
        var r = new Rig();
        var closedAtEvent = new List<bool>();
        r.C.InventoryButtons += () => closedAtEvent.Add(r.C.IsClosed);
        r.C.Close();
        for (int i = 0; i < 300 && !r.C.IsClosed; i++) r.Host.Player.Tick();
        Assert.That(r.C.IsClosed, Is.True);
        Assert.That(closedAtEvent, Is.EqualTo(new[] { true }));
    }

    [Test]
    public void CloseSendsTheResetAndClosesTheManager()
    {
        var r = new Rig();
        r.Sent.Clear();
        r.C.Close();
        Assert.That(r.M.DialogOpen, Is.False);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.SetMode(0))));
    }

    [Test]
    public void ListedItemIsBoundAndRemoveSendsModeTwo()
    {
        var r = new Rig();
        var it = r.Add(5);
        r.Put(5, 3);
        Assert.That(r.C.GetBinding("ItemBreakDialog.3"), Is.Not.Null);
        Assert.That(r.C.GetBinding("ItemBreakDialog.4"), Is.Null);
        Assert.That(r.C.BreakSlotUid("ItemBreakDialog.3"), Is.EqualTo(5));
        r.Sent.Clear();
        r.C.RemoveFromSlot(3);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.RemoveItem(5))));
    }

    [Test]
    public void ABreakSlotsRightClickTakesItsItemOutAndItsShiftClickLinksIt()
    {
        // NotifyDragSlot::SlotUse button 1 -> ItemBreakSlot::vfunc_14 (RequestRemoveItem, mode 2); the preview key: raised;
        // button 0: pushed again; SlotShiftClick -> vfunc_15 (the item linked)
        foreach (bool donation in new[] { false, true })
        {
            var r = new Rig(donation);
            r.Add(5); r.Put(5, 3);
            r.Sent.Clear();
            bool preview = false;
            var raised = new List<string>(); r.C.Button += raised.Add;
            r.C.ActionKeyDown = k => preview && k == 0x49406;
            var linked = new List<long>(); r.C.LinkItem = i => linked.Add(i.Uid);

            Assert.That(r.C.OnCallback("notifySlotUse", new object[] { r.C.SlotName(3), 1 }), Is.True);
            Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.RemoveItem(5))), $"donation {donation}");
            r.C.OnCallback("notifySlotUse", new object[] { r.C.SlotName(3), 0 });
            r.C.OnCallback("notifySlotUse", new object[] { r.C.SlotName(4), 1 });
            Assert.That(r.Sent.Count, Is.EqualTo(1), "button 0 and an empty slot send nothing");
            preview = true;
            r.C.OnCallback("notifySlotUse", new object[] { r.C.SlotName(3), 1 });
            Assert.That((r.Sent.Count, raised.Single()), Is.EqualTo((1, "notifySlotUse")), "the preview key: the preview, raised");

            Assert.That(r.C.OnCallback("notifySlotShiftClick", new object[] { r.C.SlotName(3) }), Is.True);
            r.C.OnCallback("notifySlotShiftClick", new object[] { r.C.SlotName(4) });
            Assert.That(linked, Is.EqualTo(new[] { 5L }));
            Assert.That(r.C.OnCallback("notifySlotUse", new object[] { "Other.0", 1 }), Is.False);
        }
    }

    [Test]
    public void ABreakSlotShowsNoItemOrTooltipWithoutTheLocalPlayer()
    {
        // ItemBreakSlot::vfunc_8 / vfunc_0 (itembreakslot.cpp:139, :121): no local player, 0
        var r = new Rig();
        r.Add(5); r.Put(5, 3);
        Assert.That(r.C.GetBinding(r.C.SlotName(3)), Is.Not.Null);
        Assert.That(r.C.GetSlotTooltip(r.C.SlotName(3)), Is.Not.Empty);
        r.Model.HasLocalPlayer = false;
        Assert.That(r.C.GetBinding(r.C.SlotName(3)), Is.Null);
        Assert.That(r.C.GetSlotTooltip(r.C.SlotName(3)), Is.Empty);
    }

    [Test]
    public void RightClickAddOnlyWhenIdle()
    {
        var r = new Rig();
        var it = r.Add(5);
        r.Sent.Clear();
        r.C.AddFromInventory(it);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.AddItem(5, 1, -1))));
        r.Sent.Clear();
        r.C.StartBreak();
        r.C.AddFromInventory(r.Add(6));
        Assert.That(r.Sent, Is.Empty, "no add while a break runs");
    }

    [Test]
    public void BreakWithNothingListedShowsTheNoticeBoxAndSendsNothing()
    {
        var r = new Rig();
        r.Sent.Clear();
        r.C.ClickBreak();
        Assert.That(r.Boxes.Single().Type, Is.EqualTo(ItemBreakDialogController.NoItemsBoxType));
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.C.InProgress, Is.False);
    }

    [Test]
    public void CommonRarityStartsAtOnceAndOnSendBreakSendsModeThree()
    {
        var r = new Rig();
        r.Add(5, 3); r.Put(5, 0);
        r.Sent.Clear();
        r.C.ClickBreak();
        Assert.That(r.Boxes, Is.Empty);
        Assert.That(r.C.InProgress, Is.True);
        Assert.That(r.Sounds, Does.Contain("Extract_Progress"));
        Assert.That(r.C.OnCallback("onSendBreak", Array.Empty<object>()), Is.True);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.Start())));
    }

    [Test]
    public void RarityFourAsksYesNoAndYesStarts()
    {
        var r = new Rig();
        r.Add(5, 4); r.Put(5, 0);
        r.C.ClickBreak();
        var box = r.Boxes.Single();
        Assert.That(box.Type, Is.EqualTo(ItemBreakDialogController.CommonBoxType));
        Assert.That(r.C.InProgress, Is.False);
        box.Callback(1, box);
        Assert.That(r.C.InProgress, Is.False, "no starts the break not");
        box.Callback(0, box);
        Assert.That(r.C.InProgress, Is.True);
    }

    [TestCase(5)]
    [TestCase(6)]
    public void RarityFiveAndSixNeedTheCheckbox(int rarity)
    {
        var r = new Rig();
        r.Add(5, rarity); r.Put(5, 0);
        r.C.ClickBreak();
        var box = r.Boxes.Single();
        Assert.That(box.Type, Is.EqualTo(ItemBreakDialogController.LegendaryBoxType));
        Assert.That(box.MustCheck, Is.True);
        Assert.That(box.EnterAllowed, Is.False);
        Assert.That(r.C.InProgress, Is.False);
        box.Callback(0, box);
        Assert.That(r.C.InProgress, Is.True);
    }

    [Test]
    public void ResultRowsBindTheRewardsSkipZeroAmountAndEndTheBreak()
    {
        var r = new Rig();
        r.Add(5); r.Put(5, 0);
        r.C.StartBreak();
        int id = new[] { 20000001, 20000002, 30000001, 11300001, 12000001 }.First(i => !string.IsNullOrEmpty(r.Model.Tables.ItemName(i)));
        r.M.OnPacket(new ItemDismantlePacket(3) { Type = 1, Rewards = new[] { new DismantleReward(id, 4), new DismantleReward(id + 1, 0) } });
        Assert.That(r.C.InProgress, Is.False);
        Assert.That(r.Sounds, Does.Contain("Extract_End"));
        Assert.That(r.M.HasBreakItems, Is.False, "mode 3 clears the list");
        Assert.That(r.Model.ItemSlots.Keys.Where(k => k.StartsWith("ItemBreakDialog_")).Count(), Is.EqualTo(1));
        r.C.Close();
        Assert.That(r.Model.ItemSlots.Keys.Where(k => k.StartsWith("ItemBreakDialog_")), Is.Empty, "close releases the bindings");
    }

    [Test]
    public void PreviewSkipsEmptyRangesAndBindsPerItem()
    {
        var r = new Rig();
        int id = new[] { 20000001, 20000002, 30000001, 11300001, 12000001 }.First(i => !string.IsNullOrEmpty(r.Model.Tables.ItemName(i)));
        r.M.OnPacket(new ItemDismantlePacket(5) { Previews = new[] { new DismantlePreview(id, 1, 3), new DismantlePreview(id + 1, 0, 0) } });
        Assert.That(r.Model.ItemSlots.Keys.Where(k => k.StartsWith("ItemBreakDialog_preview_")).Count(), Is.EqualTo(1));
    }

    [Test]
    public void BundleRegisterSendsTheTypeAndRankOnlyWhenIdle()
    {
        var r = new Rig();
        r.C.OnCallback("OnBundleType", new object[] { 6 });
        r.C.OnCallback("OnBundleRank", new object[] { 4 });
        r.Sent.Clear();
        r.C.OnCallback("OnBundleRegister", Array.Empty<object>());
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.Options(6, 4))));
        r.C.StartBreak();
        r.Sent.Clear();
        r.C.OnCallback("OnBundleRegister", Array.Empty<object>());
        Assert.That(r.Sent, Is.Empty);
    }

    [Test]
    public void DonationDoesNotTakeTheBundleCallbacks()
    {
        var r = new Rig(true);
        Assert.That(r.C.OnCallback("OnBundleRegister", Array.Empty<object>()), Is.False);
    }

    /// <summary>UIItemBreakDialog::vfunc_85 (uiinventorydialog.cpp:2663): the row is scrolled to as it is (SetScrollPosition(nRow)) when it is
    /// above the top row or below the 3 shown, else the position stays; a reset goes to max(last row - 2, 0).</summary>
    [Test]
    public void ScrollTargetFollowsTheClientRule()
    {
        Assert.That(ItemBreakDialogController.ScrollTarget(true, 0, 44, 0), Is.EqualTo(6));
        Assert.That(ItemBreakDialogController.ScrollTarget(true, 0, 4, 0), Is.EqualTo(0));
        Assert.That(ItemBreakDialogController.ScrollTarget(false, 25, 99, 0), Is.EqualTo(5), "below the 3 rows: the row itself");
        Assert.That(ItemBreakDialogController.ScrollTarget(false, 5, 99, 3), Is.EqualTo(1), "above the top");
        Assert.That(ItemBreakDialogController.ScrollTarget(false, 12, 99, 1), Is.EqualTo(1), "row 2 is in rows 1..3: unchanged");
        Assert.That(ItemBreakDialogController.ScrollTarget(false, 14, 99, 0), Is.EqualTo(0), "row 2 is the last of rows 0..2: unchanged");
        Assert.That(ItemBreakDialogController.ScrollTarget(false, 15, 99, 0), Is.EqualTo(3), "row 3 is the first below rows 0..2");
        Assert.That(ItemBreakDialogController.ScrollTarget(false, 990, 44, 0), Is.EqualTo(8), "clamped to the last row");
    }

    int Scroll(Rig r) => Rt.ToInt32(r.Host.Get(r.Host.Get(r.Host.Child("m_itemBag"), "instance"), "scrollPosition"));

    /// <summary>A staged item below the 3 rows the bag shows scrolls the bag to its row (vfunc_85(false, index) from OnBagSlotChanged);
    /// one in the shown rows leaves it; one above scrolls back; the bag shows the top rows again after a break (RefreshActiveDialog).</summary>
    [Test]
    public void TheBagScrollsToAStagedItemOutsideItsRowsAndBackAfterABreak()
    {
        var r = new Rig();
        r.Add(500); r.Add(501); r.Add(502);
        Assert.That(Scroll(r), Is.EqualTo(0));
        r.Put(500, 27);     // row 5
        for (int i = 0; i < 5; i++) r.Host.Player.Tick();
        Assert.That(Scroll(r), Is.EqualTo(5), "below the shown rows");
        r.Put(501, 26);     // row 5 again, shown
        Assert.That(Scroll(r), Is.EqualTo(5), "shown: unchanged");
        r.Put(502, 2);      // row 0, above
        for (int i = 0; i < 5; i++) r.Host.Player.Tick();
        Assert.That(Scroll(r), Is.EqualTo(0), "above the top row");
        r.Put(500, 52);     // row 10
        r.M.OnPacket(new ItemDismantlePacket(3) { Type = 1 });
        for (int i = 0; i < 5; i++) r.Host.Player.Tick();
        Assert.That(Scroll(r), Is.EqualTo(0), "after the break nothing is staged: the top rows");
        Assert.That(r.M.HasBreakItems, Is.False);
    }

    [Test]
    public void DroppedOnInventoryReplacesOrRemoves()
    {
        var r = new Rig();
        var a = r.Add(5); a.Slot = 7; r.Put(5, 2);
        var b = r.Add(6);
        r.Sent.Clear();
        r.C.DroppedOnInventory(5, b);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.AddItem(6, 1, 7))));
        r.Sent.Clear();
        r.C.DroppedOnInventory(5, null);
        Assert.That(r.Sent.Select(Hex).Single(), Is.EqualTo(Hex(ItemDismantleRequest.RemoveItem(5))));
    }

    [Test]
    public void IdleTickAllocatesNothing()
    {
        // the controller's per-frame Tick (vfunc_15, HudScene.cs): RemoveMissingItems over the listed items, no Item built
        var r = new Rig();
        r.Add(5); r.Put(5, 0);
        for (int i = 0; i < 5000; i++) r.C.Tick();     // past the tiered JIT's call counting
        Assert.That(Alloc.Least(() => { for (int i = 0; i < 100_000; i++) r.C.Tick(); }), Is.LessThan(512));
    }

    /// <summary>The rank dropdown's rows: the rank's name and GetStringByKey("s_itembreak_rank_or_below") (uiinventorydialog.cpp:2609),
    /// the common table's text; no row shows a "$s_" key.</summary>
    [Test]
    public void RankRowsTakeTheOrBelowTextFromTheCommonTable()
    {
        var r = new Rig(flash: true);
        string below = r.Model.Tables.Common("s_itembreak_rank_or_below");
        Assert.That(below, Is.Not.Null.And.Not.Empty, "the data's common row");
        var dp = r.Host.Get(r.Host.Child("m_bundleRankDropdown"), "dataProvider");
        int n = (int)Rt.ToNumber(r.Host.Get(dp, "length"));
        var labels = Enumerable.Range(0, n).Select(i => Rt.ToString(r.Host.Get(r.Host.Invoke(dp, "requestItemAt", i), "label"))).ToList();
        TestContext.WriteLine(string.Join(" | ", labels));
        var want = Enumerable.Range(1, 6).Select(k => r.Model.Tables.Common("s_word_rank_" + k.ToString("00")) + " " + below);
        Assert.That(labels, Is.EqualTo(want));
        Assert.That(labels.Any(l => l.Contains("s_itembreak")), Is.False);
    }

    /// <summary>The result and preview rows' text: FUN_14028dfa0 of the common table's "s_itembreak_preview_static" /
    /// "s_itembreak_preview_random" (uiinventorydialog.cpp:2736, :2878-2880).</summary>
    [Test]
    public void ResultAndPreviewRowsFormatTheCommonTablesText()
    {
        var r = new Rig(flash: true);
        int id = new[] { 20000001, 20000002, 30000001, 11300001, 12000001 }.First(i => !string.IsNullOrEmpty(r.Model.Tables.ItemName(i)));
        string name = r.Model.Tables.ItemName(id);
        string fixedText = r.Model.Tables.Common("s_itembreak_preview_static"), random = r.Model.Tables.Common("s_itembreak_preview_random");
        Assert.That((fixedText, random), Is.Not.EqualTo((null as string, null as string)));
        Assert.That(r.C.RowText(id, 3, 3), Is.EqualTo(ClientFormat.Format(fixedText ?? "", name, 3u)));
        Assert.That(r.C.RowText(id, 1, 4), Is.EqualTo(ClientFormat.Format(random ?? "", name, 1u, 4u)));
        Assert.That(r.C.RowText(id, 1, 4), Does.Not.Contain("s_itembreak"));
    }

    /// <summary>The dialog's and the manager's sounds are the client's system sound keys (s_strExtractProgress "Extract_Progress",
    /// s_strExtractEnd "Extract_End", uiinventorydialog.cpp:475-476; "Extract_ItemChoice"), each a row of table/systemsound.xml.</summary>
    [Test]
    public void SoundsAreSystemSoundKeys()
    {
        var r = new Rig();
        r.Add(5); r.Put(5, 0);
        r.C.StartBreak();
        r.C.EndBreak();
        var keys = System.Xml.Linq.XDocument.Load(new MemoryStream(packs.Read("Xml", "table/systemsound.xml"))).Root.Elements()
            .Select(e => (string)e.Attribute("key")).Where(k => k != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.That(r.Sounds, Is.SupersetOf(new[] { "Extract_Progress", "Extract_End" }));
        foreach (var k in r.Sounds.Append("Extract_ItemChoice").Append(InventoryController.GenderWarningSound))
            Assert.That(keys, Does.Contain(k), k);
    }
}
