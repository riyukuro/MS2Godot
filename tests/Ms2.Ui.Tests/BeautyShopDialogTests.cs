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
        r.C.FeePriceText = null;
        r.C.ClickSave();
        Assert.That(opened, Is.Empty, "no rows, nothing opens");
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
}
