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
        packs = new PackSet(d);
        src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
    }

    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    sealed class Cust : IBeautyCustomize
    {
        public int EditItemId { get; set; }
        public bool HasChanges { get; set; }
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
        public bool Rich = true;
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
            C.CanPay = (_, amount) => Rich;
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
        r.Rich = false;
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
}
