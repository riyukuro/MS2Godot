using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;
using Maple2.PacketLib.Tools;

namespace Ms2.Ui.Tests;

/// <summary>The beauty shop dialog (uibeautyshopdialog, UIBeautyShopDialog @0x1418c80b0 and friends; needs MS2_DATA): the state of
/// mode 0, the gate of the other modes, the result boxes of mode 8, the fee per page, the buy and the close question. Texts are not
/// compared, so no language's strings.</summary>
public class BeautyShopDialogTests
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

    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    sealed class Cust : IBeautyCustomize
    {
        public int EditItemId { get; set; }
        public bool HasChanges { get; set; }
        public bool HasStyleChanges { get; set; }
        public bool IsEditItemAdded { get; set; }
        public bool IsEditItemColorChanged { get; set; }
        public bool IsDragging { get; set; }
        public List<string> Calls = new();
        public void UpdateEditItems() => Calls.Add("update");
        public void CommitSlot(int s) => Calls.Add("commit" + s);
        public void RestoreSlot(int s) => Calls.Add("restore" + s);
    }

    static readonly BeautyPayment Pay = new(1, 0, 5000, "");
    static readonly BeautyPayment Recolor = new(1, 0, 700, "");

    sealed class Rig
    {
        public BeautyShopDialogController C = new();
        public Cust Cu = new();
        public List<IOutPacket> Sent = new();
        public List<MessageBoxData> Boxes = new();
        public List<string> Notices = new();
        public List<string> Setfee = new();
        public int Left, Closed, NotEnoughCalls;
        public long Wallet = 6000;
        public UiHost Host;

        public Rig(bool load = true)
        {
            Packs();
            var model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            C.Customize = Cu;
            C.Send = Sent.Add;
            C.ShowData = Boxes.Add;
            C.NoticeText += Notices.Add;
            C.Left += () => Left++;
            C.Closed += () => Closed++;
            C.CanPay = (_, amount) => amount <= Wallet;   // the amount decides: a wrong price passed on shows
            C.CommitSendsPorted = true; C.StyleSelectPorted = true;
            C.NotEnough = _ => NotEnoughCalls++;
            C.FeePriceText = (f, a) => "p" + a;
            C.SlotOfKind = k => 40 + k;
            if (!load) { Model = model; return; }
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            Host = new UiHost(p, C, model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
            Model = model;
        }
        public UiModel Model;

        public static BeautyShopPacket Open(int page = 1, int shopId = 7) => new(0)
        {
            Kind = page, SlotCount = 3, SavedStyles = 1, Context = 0x1122334455L,
            Shop = new BeautyShopData(shopId, 1, 0, false, 0, 0, false, Pay, Recolor),
            Items = new[] { new BeautyShopItem(100, 0, 0, 0, 0, new BeautyPayment(1, 0, 9000, "")) },
        };
    }

    /// <summary>The shop's worn slot is FUN_140f01070 by default (kind 1 -> slot 1); unset, Leave restored slot 0.</summary>
    [Test]
    public void LeaveRestoresTheSlotOfTheShopKind()
    {
        Packs();
        var cu = new Cust();
        var c = new BeautyShopDialogController { Customize = cu };    // the default SlotOfKind, none set by a host
        c.OnPacket(Rig.Open(1));
        c.Leave();
        Assert.That(cu.Calls, Is.EqualTo(new[] { "restore1" }));
    }

    [Test]
    public void ModeZeroStoresTheShopAndBuildsThePageOnLoad()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(2));
        Assert.That(r.C.Page, Is.EqualTo(2));
        Assert.That(r.C.SaleEventId, Is.EqualTo(7));
        Assert.That(r.C.SavedStyles, Is.EqualTo(3), "slot count byte");
        Assert.That(r.C.StyleCount, Is.EqualTo(1), "saved style count byte");
        Assert.That(r.C.Context, Is.EqualTo(0x1122334455L));
        Assert.That(r.C.Items, Has.Count.EqualTo(1));
        Assert.That(r.C.PageBuilt, Is.True);
        Assert.That(r.Host.Child("m_save"), Is.Not.Null);
    }

    [Test]
    public void ModeZeroBeforeTheMovieLoadsBuildsAtLoad()
    {
        var r = new Rig(false);
        r.C.OnPacket(Rig.Open());
        Assert.That(r.C.PageBuilt, Is.False);
        Assert.That(r.C.Shop, Is.Not.Null);
    }

    [Test]
    public void AClosedDialogIgnoresTheGatedModes()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        r.C.Close();
        Assert.That(r.Closed, Is.EqualTo(1));
        r.C.Waiting = true;
        int style = r.C.StyleCount;
        r.C.OnPacket(new BeautyShopPacket(0x10));
        r.C.OnPacket(new BeautyShopPacket(0x11));
        r.C.OnPacket(new BeautyShopPacket(0xc) { CouponId = 0 });
        r.C.OnPacket(new BeautyShopPacket(8) { Result = 7 });
        Assert.That(r.C.StyleCount, Is.EqualTo(style));
        Assert.That(r.Notices, Is.Empty);
        Assert.That(r.Boxes, Is.Empty);
        Assert.That(r.C.Waiting, Is.True, "m_2d8 is only reset after the gate");
    }

    [Test]
    public void StyleSavedModesCountAndNotify()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        r.C.OnPacket(new BeautyShopPacket(0x10));
        Assert.That(r.C.StyleCount, Is.EqualTo(2));
        Assert.That(r.Notices, Has.Count.EqualTo(1));
        r.C.OnPacket(new BeautyShopPacket(0x11));
        Assert.That(r.C.StyleCount, Is.EqualTo(3));
        Assert.That(r.Notices, Has.Count.EqualTo(2));
    }

    [Test]
    public void ModeEightBoxesAndNotEnough()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        r.C.Waiting = true;
        r.C.OnPacket(new BeautyShopPacket(8) { Result = BeautyResult.Silent });
        Assert.That(r.Boxes, Is.Empty);
        Assert.That(r.C.Waiting, Is.False);
        r.C.OnPacket(new BeautyShopPacket(8) { Result = BeautyResult.NotEnough });
        Assert.That(r.NotEnoughCalls, Is.EqualTo(1));
        Assert.That(r.Boxes, Is.Empty);
        r.C.OnPacket(new BeautyShopPacket(8) { Result = BeautyResult.ColorError });
        r.C.OnPacket(new BeautyShopPacket(8) { Result = BeautyResult.SlotMax });
        Assert.That(r.Boxes, Has.Count.EqualTo(2));
        Assert.That(r.Left, Is.EqualTo(0));
        r.C.OnPacket(new BeautyShopPacket(8) { Result = BeautyResult.MessageBoxOk });
        r.C.OnPacket(new BeautyShopPacket(8) { Result = 0x33 });
        Assert.That(r.Boxes, Has.Count.EqualTo(4));
        r.Boxes[2].Callback(0, null);
        r.Boxes[3].Callback(0, null);
        Assert.That(r.Left, Is.EqualTo(2), "ok leaves the page");
    }

    [Test]
    public void ResultZeroHitsTheDefaultBox()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        r.C.Waiting = true;
        // @0x1418c80b0 mode 8 has no case 0: the default box (0x272); m_2d8 is reset first, as for every gated mode
        r.C.OnPacket(new BeautyShopPacket(8) { Result = 0 });
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        Assert.That(r.C.Waiting, Is.False);
    }

    [Test]
    public void FeeFollowsThePage()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(1));
        Assert.That(r.C.GetFee(out uint p0), Is.SameAs(Pay)); Assert.That(p0, Is.EqualTo(0u), "nothing changed");
        r.Cu.IsEditItemAdded = true; r.Cu.EditItemId = 100;
        Assert.That(r.C.GetFee(out uint p1).Amount, Is.EqualTo(9000)); Assert.That(p1, Is.EqualTo(9000u), "the row's own price");
        r.Cu.IsEditItemAdded = false; r.Cu.IsEditItemColorChanged = true;
        Assert.That(r.C.GetFee(out uint p2), Is.SameAs(Recolor)); Assert.That(p2, Is.EqualTo(700u));
        r.C.Page = BeautyShopDialogController.PageSpecial;
        Assert.That(r.C.GetFee(out uint p3), Is.SameAs(Pay)); Assert.That(p3, Is.EqualTo(5000u));
    }

    [Test]
    public void BuyWithoutMoneySendsNothing()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(BeautyShopDialogController.PageSpecial));
        r.Wallet = 0;
        Assert.That(r.C.Buy(), Is.False);
        Assert.That(r.NotEnoughCalls, Is.EqualTo(1));
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.C.Waiting, Is.False);
        Assert.That(r.Cu.Calls, Is.Empty);
    }

    [Test]
    public void SpecialBuySendsModeSevenAfterRestoring()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(BeautyShopDialogController.PageSpecial));
        Assert.That(r.C.Buy(), Is.True);
        Assert.That(r.Sent.Select(Bytes).Single(), Is.EqualTo(Bytes(new BeautyBuyRequest(7, false, 0x1122334455L))));
        Assert.That(r.Cu.Calls, Is.EqualTo(new[] { "restore41" }));
        Assert.That(r.C.Waiting, Is.True);
    }

    [Test]
    public void OtherPagesCommitTheSlotAndSendNothing()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(1));
        r.Cu.IsEditItemColorChanged = true;
        Assert.That(r.C.Buy(), Is.True);
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Cu.Calls, Is.EqualTo(new[] { "commit41" }));
    }

    /// <summary>The fee is checked at the price after the sale (uibeautyshopdialog.cpp:1835): 5000 is over the wallet of 4000, half of it is not.</summary>
    [Test]
    public void TheWalletIsCheckedAgainstThePriceAfterTheSale()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(BeautyShopDialogController.PageSpecial));
        r.Wallet = 4000;
        Assert.That(r.C.Buy(), Is.False, "the full price is over the wallet");
        r.C.Sales.Apply(new GameEventMessage(0, System.Array.Empty<ShutdownInteractObjectEvent>(), System.Array.Empty<int>(), false)
            { BeautySaleEvents = new[] { new BeautyShopSaleEvent(1, 7, 5000, 0) } });
        r.Sent.Clear();
        Assert.That(r.C.Buy(), Is.True, "half the price is within it");
        Assert.That(r.Sent, Has.Count.EqualTo(1));
    }

    /// <summary>After the send the shop root's input is off (FUN_1412761e0(m_30, 0), uibeautyshopdialog.cpp:1860) until the answer
    /// (:1885): a Save and OK before it send no second buy.</summary>
    [Test]
    public void AfterABuyTheShopTakesNoInputUntilTheAnswer()
    {
        var r = new Rig();
        var opened = new List<BillData>();
        r.C.OpenBill = opened.Add;
        r.C.OnPacket(Rig.Open(BeautyShopDialogController.PageSpecial));
        Assert.That(r.C.OnCallback("OnClickSave", System.Array.Empty<object>()), Is.True);
        opened.Single().Answer(0, false);
        Assert.That(r.Sent, Has.Count.EqualTo(1));
        Assert.That(r.C.InputOff, Is.True);
        Assert.That(r.C.OnCallback("OnClickSave", System.Array.Empty<object>()), Is.False, "no callback reaches the shop");
        r.Cu.IsEditItemColorChanged = true;
        Assert.That(r.C.OnCallback("OnClickSave", System.Array.Empty<object>()), Is.False);
        Assert.That(opened, Has.Count.EqualTo(1), "no second bill opens");
        Assert.That(r.Sent, Has.Count.EqualTo(1), "no second buy goes out");
        r.C.OnPacket(new BeautyShopPacket(8) { Result = 0 });
        Assert.That(r.C.InputOff, Is.True, "a result of 0 keeps it off");
        r.C.OnPacket(new BeautyShopPacket(8) { Result = 0x33 });
        Assert.That(r.C.InputOff, Is.False, "an answer other than 0 turns it on");
    }

    /// <summary>The item event listeners (vfunc_21/24/25, uibeautyshopdialog.cpp:896-925) run FUN_1418ca240 (:1868): a successful buy
    /// (result 0) unlocks through them; vfunc_21 keeps the new item on the special page only (m_2c8).</summary>
    [Test]
    public void TheItemEventTurnsTheInputBackOnAfterABuy()
    {
        var r = new Rig();
        var opened = new List<BillData>();
        r.C.OpenBill = opened.Add;
        r.C.OnPacket(Rig.Open(BeautyShopDialogController.PageSpecial));
        r.C.OnCallback("OnClickSave", System.Array.Empty<object>());
        opened.Single().Answer(0, false);
        r.C.OnPacket(new BeautyShopPacket(8) { Result = 0 });
        Assert.That(r.C.InputOff, Is.True);
        r.Cu.Calls.Clear();
        r.C.OnItemEvent(501, ownEquip: true);
        Assert.That(r.C.InputOff, Is.False, "the shop takes input again");
        Assert.That(r.C.Waiting, Is.False);
        Assert.That(r.Cu.Calls, Is.Not.Empty, "the slot is restored");
        Assert.That(r.C.NewItemId, Is.EqualTo(501));
        r.C.OnItemEvent(777);
        Assert.That(r.C.NewItemId, Is.EqualTo(501), "vfunc_24 does not keep the item");
    }

    /// <summary>FUN_1418ca240 (:1868) without a Customize (m_1b8 == 0) clears the wait and returns: the input stays off, nothing is
    /// restored; vfunc_21 still keeps the item on the special page. Without the movie (m_30 == 0) nothing happens at all.</summary>
    [Test]
    public void TheItemEventWithoutACustomizeOrAMovieDoesLess()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(BeautyShopDialogController.PageSpecial));
        r.C.InputOff = true; r.C.Waiting = true;
        r.C.Customize = null;
        r.C.OnItemEvent(501, ownEquip: true);
        Assert.That(r.C.Waiting, Is.False);
        Assert.That(r.C.InputOff, Is.True, "no restore, no input on");
        Assert.That(r.C.NewItemId, Is.EqualTo(501));
        var n = new Rig(load: false);
        n.C.InputOff = true; n.C.Waiting = true;
        n.C.OnItemEvent(501, ownEquip: true);
        Assert.That((n.C.InputOff, n.C.Waiting, n.C.NewItemId), Is.EqualTo((true, true, 0)), "no movie attached");
    }

    /// <summary>An item event before any buy leaves the input on; m_2b8 stays when there is no local player (a null answer).</summary>
    [Test]
    public void TheKeptWornItemStaysWithoutALocalPlayer()
    {
        var r = new Rig();
        var bills = new List<BillData>();
        r.C.OpenBill = bills.Add;
        r.C.OnPacket(Rig.Open(BeautyShopDialogController.PageSpecial));
        r.C.OnItemEvent();
        Assert.That(r.C.InputOff, Is.False);
        r.C.OldItemId = 400;
        r.C.WornItemId = _ => null;
        r.C.OnCallback("OnClickSave", System.Array.Empty<object>());
        bills.Single().Answer(0, false);
        Assert.That(r.C.OldItemId, Is.EqualTo(400), "no local player: unchanged");
    }

    [Test]
    public void TheItemIsKeptOnlyOnTheSpecialPage()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(1));
        r.C.OnItemEvent(501, ownEquip: true);
        Assert.That(r.C.NewItemId, Is.EqualTo(0));
    }

    /// <summary>Mode 0xb opens the style select only when both ids are the kept ones (:1541-1548).</summary>
    [Test]
    public void TheStyleAnswerOpensOnlyForTheKeptItems()
    {
        var r = new Rig();
        var opened = new List<int>();
        r.C.ItemInfo = id => { opened.Add(id); return new List<(string, object)> { ("iconPath", "x") }; };
        r.C.WornItemId = _ => 400;
        var bills = new List<BillData>();
        r.C.OpenBill = bills.Add;
        r.C.OnPacket(Rig.Open(BeautyShopDialogController.PageSpecial));
        r.C.OnCallback("OnClickSave", System.Array.Empty<object>());
        bills.Single().Answer(0, false);
        Assert.That(r.C.OldItemId, Is.EqualTo(400), "m_2b8 is kept with the send");
        r.C.OnItemEvent(501, ownEquip: true);
        r.C.OnPacket(new BeautyShopPacket(0xb) { OldItemId = 400, NewItemId = 999 });
        Assert.That(opened, Is.Empty, "the new id differs");
        r.C.OnPacket(new BeautyShopPacket(0xb) { OldItemId = 401, NewItemId = 501 });
        Assert.That(opened, Is.Empty, "the old id differs");
        r.C.OnPacket(new BeautyShopPacket(0xb) { OldItemId = 400, NewItemId = 501 });
        Assert.That(opened, Is.EqualTo(new[] { 400, 501 }));
        Assert.That(r.C.StyleSelectOpen, Is.True, "m_2b5 = 1");
    }

    /// <summary>The style select rows (:2019-2054): the old row's itemname is rewritten only when the shop has a return coupon; the coupon
    /// line is the item icon with vspace -6 (FUN_1418fe870 writes the offset it is handed), added only with item data for the coupon.</summary>
    [Test]
    public void TheStyleSelectRowsCarryTheCouponLineOnlyWithACoupon()
    {
        var r = new Rig();
        r.C.ItemName = id => "n" + id;
        r.C.ItemInfo = id => new List<(string, object)> { ("itemname", "raw"), ("itemid", id.ToString()) };
        var open = Rig.Open(BeautyShopDialogController.PageSpecial);
        r.C.OnPacket(open);
        var (cur, nw) = r.C.StyleSelectRows(400, 501);
        Assert.That(cur.Single(m => m.Name == "itemname").Value, Is.EqualTo("raw"), "no return coupon: the row keeps WriteItemInfo's name");
        Assert.That((string)nw.Single(m => m.Name == "itemname").Value, Is.Not.EqualTo("raw"));
        Assert.That(nw.Single(m => m.Name == "itemid").Value, Is.EqualTo("501"));

        open.Shop = open.Shop with { ReturnCouponId = 9 };
        r.C.OnPacket(open);
        (cur, _) = r.C.StyleSelectRows(400, 501);
        string plain = (string)cur.Single(m => m.Name == "itemname").Value;
        Assert.That(plain, Is.Not.EqualTo("raw"));
        Assert.That(plain, Does.Not.Contain("export_common_icon_item_9"), "no item data for the coupon: no coupon line");

        r.C.HasItemData = id => id == 9;
        (cur, _) = r.C.StyleSelectRows(400, 501);
        string withLine = (string)cur.Single(m => m.Name == "itemname").Value;
        Assert.That(withLine, Does.Contain("export_common_icon_item_9"));
        Assert.That(withLine, Does.Contain("vspace='-6'"));
        Assert.That(withLine, Does.StartWith(plain));
    }

    /// <summary>OnClickRandomSelect (:828): slot -1 asks the 0x272 box (the first button leaves); another slot restores the worn slot and,
    /// on the special page, sends mode 0xc with it; the dialog is closed after. onChangeSelectedIndexItem (:839) previews the kept item.</summary>
    [Test]
    public void TheStyleSelectCallbacksSendTheSlotAndPreviewTheKeptItem()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(BeautyShopDialogController.PageSpecial));
        r.C.OldItemId = 400; r.C.NewItemId = 501;
        var eq = new List<int>();
        r.C.EquipPreview = eq.Add;
        r.C.StyleSelectOpen = true;
        r.C.OnCallback("onChangeSelectedIndexItem", new object[] { 0 });
        r.C.OnCallback("onChangeSelectedIndexItem", new object[] { 1 });
        r.C.OnCallback("onChangeSelectedIndexItem", new object[] { -1 });
        Assert.That(eq, Is.EqualTo(new[] { 400, 501 }));
        r.C.OnCallback("OnClickRandomSelect", new object[] { -1 });
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        Assert.That(r.C.StyleSelectOpen, Is.False);
        r.Cu.Calls.Clear();
        r.C.OnCallback("OnClickRandomSelect", new object[] { 1 });
        Assert.That(r.Cu.Calls, Is.EqualTo(new[] { "restore41" }));
        Assert.That(r.Sent.Single(), Is.EqualTo(new BeautySlotRequest(1)));
        r.Sent.Clear();
        r.C.OnPacket(Rig.Open(1));
        r.C.OnCallback("OnClickRandomSelect", new object[] { 1 });
        Assert.That(r.Sent, Is.Empty, "mode 0xc only on the special page");
    }

    [Test]
    public void UnportedCommitSendsAndTheUnportedStyleAnswerDoNothing()
    {
        var r = new Rig();
        r.C.CommitSendsPorted = false;
        r.C.OnPacket(Rig.Open(1));
        r.Cu.IsEditItemColorChanged = true;
        Assert.That(r.C.Buy(), Is.False);
        Assert.That(r.Cu.Calls, Is.Empty, "no local commit without its send");
        Assert.That(r.C.InputOff, Is.False);
        r.C.StyleSelectPorted = false;
        r.C.OnPacket(Rig.Open(BeautyShopDialogController.PageSpecial));
        Assert.That(r.C.Buy(), Is.False);
        Assert.That(r.Sent, Is.Empty, "no mode-7 send without the 0xb answer");
        Assert.That(r.Cu.Calls, Is.Empty);
    }

    [Test]
    public void BuyWithoutACustomizeDoesNothing()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(2));
        r.C.Customize = null;
        Assert.That(r.C.Buy(), Is.False);
        Assert.That(r.Sent, Is.Empty);
    }

    [Test]
    public void CloseAsksOnlyWhenChanged()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(1));
        r.C.RequestClose();
        Assert.That(r.Left, Is.EqualTo(1));
        Assert.That(r.Boxes, Is.Empty);
        r.Cu.HasChanges = true;
        r.C.RequestClose();
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        Assert.That(r.Left, Is.EqualTo(1), "asked, not left");
        r.Boxes[0].Callback(1, null);
        Assert.That(r.Left, Is.EqualTo(1));
        r.Boxes[0].Callback(0, null);
        Assert.That(r.Left, Is.EqualTo(2));
    }

    [Test]
    public void TheSpecialPageLeavesWithoutAsking()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(2));
        r.Cu.HasChanges = true;
        r.C.RequestClose();
        Assert.That(r.Boxes, Is.Empty);
        Assert.That(r.Left, Is.EqualTo(1));
    }

    [Test]
    public void TheBillOfAnUnchangedPageIsFree()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(1));
        var b = r.C.BuildBill();
        Assert.That(b.Total, Is.EqualTo(0u));
        r.Cu.IsEditItemColorChanged = true;
        Assert.That(r.C.BuildBill().Total, Is.EqualTo(700u));
        r.C.FeePriceText = null;
        Assert.That(r.C.BuildBill(), Is.Null, "without the price text nothing is guessed");
    }

    static BeautyShopPacket OpenWithCoupon(int page, int coupon) => new(0)
    {
        Kind = page, SlotCount = 3, SavedStyles = 1, Context = 1,
        Shop = new BeautyShopData(7, 1, coupon, false, 0, 0, false, Pay, Recolor),
        Items = new[] { new BeautyShopItem(100, 0, 0, 0, 0, new BeautyPayment(1, 0, 9000, "")) },
    };

    [Test]
    public void SaveHandsTheBillToTheBillDialog()
    {
        var r = new Rig();
        var opened = new List<BillData>();
        r.C.OpenBill = opened.Add;
        r.C.OnPacket(Rig.Open(2));
        r.Cu.IsEditItemColorChanged = true;
        r.C.ClickSave();
        var d = opened.Single();
        Assert.That((d.ShopKind, d.FeeType), Is.EqualTo((1, 1)));
        Assert.That(d.Rows, Has.Count.EqualTo(1));
        Assert.That(d.CheckText, Is.Empty, "no display coupon: the normal mode");
        Assert.That(d.Warning, Is.Empty);
        d.Answer(0, false);
        Assert.That(r.Sent, Has.Count.EqualTo(1), "the dialog's answer reaches the buy");
    }

    [Test]
    public void SaveOffersTheCouponLineOnlyForAShopWithADisplayCoupon()
    {
        var r = new Rig();
        var opened = new List<BillData>();
        r.C.OpenBill = opened.Add;
        r.C.OnPacket(OpenWithCoupon(1, 5));
        r.Cu.IsEditItemColorChanged = true;
        r.C.ClickSave();
        var d = opened.Single();
        Assert.That(d.CheckText, Is.Not.Empty, "check box mode");
        Assert.That(d.Checked, Is.False, "no sale coupon in the bag");
    }

    [Test]
    public void SaveOpensNothingWithoutTheDialogOrTheRows()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(1));
        r.C.ClickSave();   // no OpenBill: nothing happens
        var opened = new List<BillData>();
        r.C.OpenBill = opened.Add;
        r.C.Customize = null;     // BuildBillRows @0x1418ca510 has no rows without a Customize
        r.C.ClickSave();
        Assert.That(opened, Is.Empty, "no Customize, nothing opens");
        r.C.Customize = r.Cu;
        r.C.Page = 0;             // nor on page 0
        r.C.ClickSave();
        Assert.That(opened, Is.Empty, "page 0, nothing opens");
    }

    /// <summary>OnClickSave: the sale coupon is offered only while the fee after the sale is not 0.</summary>
    [Test]
    public void ADiscountedFeeOfZeroClearsTheCoupon()
    {
        foreach (bool free in new[] { false, true })
        {
            var r = new Rig();
            var opened = new List<BillData>();
            r.C.OpenBill = opened.Add;
            r.C.HasItemWithTag = t => t == 9;
            r.C.OnPacket(new BeautyShopPacket(0)
            {
                Kind = 1, SlotCount = 3, SavedStyles = 1, Context = 1,
                Shop = new BeautyShopData(7, 1, 5, false, 0, 9, false, Pay, Recolor),
                Items = new[] { new BeautyShopItem(100, 0, 0, 0, 0, new BeautyPayment(1, 0, 9000, "")) },
            });
            if (free) r.C.Sales.Apply(new GameEventMessage(0, System.Array.Empty<ShutdownInteractObjectEvent>(), System.Array.Empty<int>(), false)
                { BeautySaleEvents = new[] { new BeautyShopSaleEvent(1, 7, 10000, 0) } });
            r.Cu.IsEditItemColorChanged = true;
            r.C.ClickSave();
            Assert.That(opened.Single().Checked, Is.EqualTo(!free), free ? "a free fee offers no coupon" : "the coupon is offered");
        }
    }

    /// <summary>The bill is modal (UIModalDialog vfunc_8 @0x141a55380, uimodaldialog.cpp:32): behind it the shop takes no callback.</summary>
    [Test]
    public void BehindAnOpenBillTheShopTakesNoCallback()
    {
        var r = new Rig();
        var opened = new List<BillData>();
        r.C.OpenBill = opened.Add;
        bool open = true;
        r.C.BillIsOpen = () => open;
        r.C.OnPacket(Rig.Open(2));
        r.Cu.IsEditItemColorChanged = true;
        Assert.That(r.C.OnCallback("OnClickSave", System.Array.Empty<object>()), Is.False);
        Assert.That(opened, Is.Empty, "the save does nothing behind the bill");
        Assert.That(r.C.OnCallback("OnClickClose", System.Array.Empty<object>()), Is.False);
        Assert.That(r.C.IsClosed, Is.False);
        open = false;
        Assert.That(r.C.OnCallback("OnClickSave", System.Array.Empty<object>()), Is.True);
        Assert.That(opened, Has.Count.EqualTo(1), "with the bill gone the shop takes it again");
    }

    [Test]
    public void TheBillClosesWhenTheShopCloses()
    {
        var r = new Rig();
        int closed = 0;
        r.C.CloseBill = () => closed++;
        r.C.OnPacket(Rig.Open(2));
        r.C.Close();
        Assert.That(closed, Is.EqualTo(1));
    }

    [Test]
    public void ASaleEventUpdatesTheOpenBill()
    {
        var r = new Rig();
        var prices = new List<uint>();
        r.C.UpdateBill = prices.Add;
        r.C.OnPacket(Rig.Open(1));
        r.Cu.IsEditItemColorChanged = true;
        r.C.OnSaleEvent();
        Assert.That(prices, Has.Count.EqualTo(1));
    }

    [Test]
    public void ABillAnswerOfZeroBuysAndOtherAnswersDoNot()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(2));
        r.C.OnBillAnswer(1, false);
        Assert.That(r.Sent, Is.Empty);
        r.C.OnBillAnswer(0, true);
        Assert.That(r.Sent.Select(Bytes).Single(), Is.EqualTo(Bytes(new BeautyBuyRequest(7, true, 0x1122334455L))));
    }

    [Test]
    public void AGatedModeBeforeTheMovieLoadsDoesNothing()
    {
        var r = new Rig(false);
        r.C.OnPacket(Rig.Open());
        Assert.DoesNotThrow(() => r.C.OnPacket(new BeautyShopPacket(8) { Result = 0x33 }));
        Assert.DoesNotThrow(() => r.C.OnPacket(new BeautyShopPacket(0x10)));
        Assert.DoesNotThrow(() => r.C.OnPacket(new BeautyShopPacket(0xc)));
        Assert.That(r.Boxes, Is.Empty);
    }

    // ---- the host side: BeautyShopFlow, what HudScene.Beauty drives --------------------------------------------------------

    sealed class FlowRig
    {
        public BeautyShopFlow Flow = new();
        public List<BeautyShopDialogController> Shown = new();
        public List<string> Logs = new();
        public int Exits;
        public Cust Cu;
        public int Unloads;
        public Player Player;

        // a controller on its real movie, as MovieDialog attaches it
        BeautyShopDialogController Make()
        {
            Packs();
            var c = new BeautyShopDialogController();
            var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            var host = new UiHost(p, c, new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true });
            Assert.That(host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            Player = p;
            c.Unloaded += () => Unloads++;
            return c;
        }
        public void Tick(int n) { for (int i = 0; i < n; i++) Player.Tick(); }
        public FlowRig(bool withCustomize)
        {
            Cu = withCustomize ? new Cust() : null;
            Flow.CreateCustomize = () => Cu;
            Flow.CreateController = Make;
            Flow.Show = Shown.Add;
            Flow.ExitSubScene = () => Exits++;
            Flow.Log = Logs.Add;
        }
    }

    [Test]
    public void WithoutACustomizeTheDialogDoesNotOpen()
    {
        var f = new FlowRig(false);
        Assert.That(f.Flow.Open(), Is.False);
        Assert.That(f.Shown, Is.Empty);
        Assert.That(f.Flow.IsOpen, Is.False);
        f.Flow.OnPacket(Rig.Open());
        Assert.That(f.Logs, Has.Count.EqualTo(2));
    }

    [Test]
    public void LeavingClosesTheDialogAndItOpensAgain()
    {
        var f = new FlowRig(true);
        Assert.That(f.Flow.Open(), Is.True);
        Assert.That(f.Flow.IsOpen, Is.True);
        Assert.That(f.Flow.Open(), Is.False, "already open");
        var c = f.Flow.Current;
        Assert.That(c.Customize, Is.SameAs(f.Cu));
        c.Leave();
        f.Tick(120);
        Assert.That(f.Unloads, Is.EqualTo(1), "the movie is handed back to the host");
        Assert.That(f.Exits, Is.EqualTo(1));
        Assert.That(f.Flow.Current, Is.Null);
        Assert.That(f.Flow.IsOpen, Is.False);
        Assert.That(f.Flow.Open(), Is.True, "a second open after leaving");
        Assert.That(f.Shown, Has.Count.EqualTo(2));
        Assert.That(f.Cu.Calls, Is.EqualTo(new[] { "restore0" }), "FUN_1418c9cd0 restores the worn slot first");
    }

    [Test]
    public void PacketsReachTheDialogOnlyWhileOpen()
    {
        var f = new FlowRig(true);
        f.Flow.OnPacket(Rig.Open());
        Assert.That(f.Logs, Has.Count.EqualTo(1));
        f.Flow.Open();
        f.Flow.OnPacket(Rig.Open(2));
        Assert.That(f.Flow.Current.Page, Is.EqualTo(2));
    }

    [Test]
    public void TheOpenRequestGoesOutOnlyWhenTheDialogOpens()
    {
        var f = new FlowRig(false);
        var sent = new List<long>();
        f.Flow.SendOpenRequest = sent.Add;
        Assert.That(f.Flow.OpenFor(new BeautyReq(1, 77)), Is.False, "no customize: the dialog stays shut");
        Assert.That(sent, Is.Empty, "no request for a dialog that never opened");

        var g = new FlowRig(true);
        g.Flow.SendOpenRequest = sent.Add;
        Assert.That(g.Flow.OpenFor(new BeautyReq(1, 78)), Is.True);
        Assert.That(sent, Is.EqualTo(new long[] { 78 }));
        Assert.That(g.Flow.OpenFor(new BeautyReq(1, 79)), Is.False, "already open");
        Assert.That(sent, Is.EqualTo(new long[] { 78 }), "no second request while open");
    }

    [Test]
    public void ASceneTeardownRestoresTheSlotWithoutTheHostHook()
    {
        var f = new FlowRig(true);
        Assert.That(f.Flow.Open(), Is.True);
        f.Flow.Shutdown();
        Assert.That(f.Cu.Calls, Does.Contain("restore0"), "FUN_1418c9cd0 restores the worn slot");
        Assert.That(f.Exits, Is.EqualTo(0), "no camera / sub scene hook while the scene goes away");
        Assert.That(f.Flow.IsOpen, Is.False);
        f.Flow.Shutdown();
    }

    [Test]
    public void TheHudSceneUnsubscribesTheBeautyWatchersWithTheScene()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MS2Godot.sln"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null);
        string beauty = File.ReadAllText(Path.Combine(dir.FullName, "game", "ui", "HudScene.Beauty.cs"));
        string hud = File.ReadAllText(Path.Combine(dir.FullName, "game", "ui", "HudScene.cs"));
        Assert.That(beauty, Does.Contain("live.BeautyRequested -="));
        Assert.That(beauty, Does.Contain("live.BeautyPacketReceived -="));
        Assert.That(beauty, Does.Contain("beautyFlow.Shutdown()"), "the teardown runs the close path, not a bare Close");
        Assert.That(beauty, Does.Contain("beautyStyleFlow.Shutdown()"), "the style dialog goes with the scene too");
        Assert.That(hud, Does.Contain("unwatchBeauty?.Invoke()"), "the scene's teardown runs it");
        Assert.That(beauty, Does.Not.Contain("beautyWatched"), "no once-forever flag that survives a rebuilt scene");
    }

    /// <summary>The ctor's SetFlag(0, 1) (uibeautyshopdialog.cpp:512) and vfunc_13 (:588): the shop is always on, so the field
    /// Esc's open-dialog test and close-all pass it; Esc asks RequestClose (the ask when changed, the sub-scene exit through
    /// Left) and is taken, also while the style select is open, which keeps the shop.</summary>
    [Test]
    public void EscAsksTheCloseAndTheFieldWalksPassTheShop()
    {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(1));
        UiController[] registry = { r.C };
        Assert.That(r.C.AlwaysOn, Is.True);
        Assert.That(DialogGates.AnyClosable(registry), Is.False, "always on: no closable dialog for the field Esc");
        DialogGates.CloseAll(registry);
        Assert.That(r.C.IsClosed, Is.False, "CloseAllDialogs passes an always-on dialog");

        r.Cu.HasChanges = true;
        Assert.That(DialogGates.EscapeKey(registry), Is.True, "the shop takes Esc");
        Assert.That(r.Boxes, Has.Count.EqualTo(1), "changed: the close ask (common 0x271)");
        Assert.That(r.Left, Is.EqualTo(0));
        Assert.That(r.C.IsClosed, Is.False, "asked, not closed");

        r.C.StyleSelectOpen = true;
        Assert.That(DialogGates.EscapeKey(registry), Is.True, "taken while the style select is open (m_2b5)");
        Assert.That(r.Boxes, Has.Count.EqualTo(1), "no second ask");

        r.C.StyleSelectOpen = false; r.Cu.HasChanges = false;
        Assert.That(DialogGates.EscapeKey(registry), Is.True);
        Assert.That(r.Left, Is.EqualTo(1), "unchanged: it leaves (RequestClose -> Left)");
    }
}
