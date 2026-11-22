using Ms2.Avm2.Flash;
using Ms2.Avm2;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;
using Maple2.PacketLib.Tools;

namespace Ms2.Ui.Tests;

/// <summary>The item colour dialog (uiitemcoloringdialog, UIItemColoringDialog @0x1419ac680 and friends; needs MS2_DATA): the page gate,
/// the result boxes of mode 8, the basket, the pay check and the 0x16 send, the flow and the talk open. Texts are not compared.</summary>
public class ItemColoringDialogTests
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

    sealed class Cust : IItemColoringCustomize
    {
        public bool HasEditItem { get; set; } = true;
        public long EditItemUid { get; set; } = 900;
        public int EditItemId { get; set; } = 11100000;
        public bool HasChanges { get; set; }
        public bool IsEditItemColorChanged { get; set; }
        public List<string> Calls = new();
        public void SelectUid(long uid) => Calls.Add("select" + uid);
        public void RestoreUid(long uid) => Calls.Add("restore" + uid);
        public void ApplyPalette(uint palette, uint color) => Calls.Add("palette");
        public void SetColors(int palette, int color) => Calls.Add("colors");
        public void ResetEdit() => Calls.Add("reset");
        public void RestoreAll() => Calls.Add("restoreAll");
        public BeautyColorItem GetModifiedAndSelect(long uid) => new(false, false, uid, 11100000, _ => { });
        public bool TryGetEditSlotColor(out uint palette, out uint color) { palette = color = 0; return false; }
    }

    sealed class Rig
    {
        public ItemColoringDialogController C = new();
        public Cust Cu = new();
        public List<IOutPacket> Sent = new();
        public List<MessageBoxData> Boxes = new();
        public int NotEnoughCalls, Closed;
        public long Money = 100_000;

        public Rig(bool load = true)
        {
            Packs();
            var model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            C.Customize = Cu;
            C.Send = Sent.Add;
            C.ShowData = Boxes.Add;
            C.NotEnough = _ => NotEnoughCalls++;
            C.FeePriceText = (f, a) => "p" + a;
            C.MoneyLeft = (f, price) => Money - price;
            C.ItemName = id => "item" + id;
            C.Closed += () => Closed++;
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            var host = new UiHost(p, C, model);
            if (load) { Assert.That(host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors)); for (int i = 0; i < 10; i++) p.Tick(); }
        }

        public static BeautyColoringPacket Open(int category = 5, int kind = 3, int shopId = 7) => new(1)
        {
            Kind = kind,
            Shop = new BeautyShopData(shopId, category, 0, false, 0, 0, false, new BeautyPayment(1, 0, 5000, ""), new BeautyPayment(1, 0, 700, "")),
        };
    }

    [Test] public void TheShopMustBeCategory5AndKind3() {
        var r = new Rig();
        r.C.OnPacket(Rig.Open(category: 1));
        Assert.That(r.C.BuildPage(), Is.False);
        r.C.OnPacket(Rig.Open(kind: 2));
        Assert.That(r.C.BuildPage(), Is.False);
        r.C.OnPacket(Rig.Open(shopId: 0));
        Assert.That(r.C.BuildPage(), Is.False);
        r.C.OnPacket(Rig.Open());
        Assert.That(r.C.BuildPage(), Is.True);
        Assert.That(r.C.GetPricePerItem(), Is.EqualTo(700u));
    }

    [Test] public void Mode8NotEnoughShowsTheFeeMessageAndNoBox() {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        r.C.OnPacket(new BeautyColoringPacket(8) { Result = BeautyResult.NotEnough });
        Assert.That((r.NotEnoughCalls, r.Boxes.Count), Is.EqualTo((1, 0)));
        Assert.That(r.Cu.Calls, Does.Contain("restoreAll"));
    }

    [Test] public void Mode8OtherResultShowsABoxAndLeavesWithTheEditRestored() {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        r.C.AddPendingRequests(1);
        r.C.OnPacket(new BeautyColoringPacket(8) { Result = 5 });
        Assert.That(r.Boxes.Count, Is.EqualTo(1));
        Assert.That((r.C.Pending, r.C.Dyeing), Is.EqualTo((0, false)));
        Assert.That(r.Cu.Calls, Does.Contain("restoreAll"));
    }

    [Test] public void Mode8OkOnlyShowsTheBoxAndKeepsTheEdit() {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        r.C.OnPacket(new BeautyColoringPacket(8) { Result = 0 });
        Assert.That(r.Boxes.Count, Is.EqualTo(1));
        Assert.That(r.Cu.Calls, Does.Not.Contain("restoreAll"));
    }

    [Test] public void OnlyTheEditItemOfTheAskedIdJoinsTheBasket() {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        Assert.That(r.C.AddCurrentItemToBasket(1), Is.Null);
        Assert.That(r.C.Basket, Is.Empty);
        Assert.That(r.C.AddCurrentItemToBasket(11100000), Is.Not.Null);
        Assert.That(r.C.Basket.Count, Is.EqualTo(1));
        Assert.That(r.Cu.Calls, Does.Contain("reset"));
        Assert.That(r.C.GetTotalPrice(), Is.EqualTo(700));
        Assert.That(r.C.RemoveBasketItem(11100000), Is.Not.Null);
        Assert.That(r.C.Basket, Is.Empty);
    }

    [Test] public void AnEmptyBasketSendsNothing() {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        Assert.That(r.C.TryPay(), Is.True);
        Assert.That(r.Sent, Is.Empty);
    }

    [Test] public void ABasketThatCannotBePaidSendsNothingAndShowsNotEnough() {
        var r = new Rig { Money = 100 };
        r.C.OnPacket(Rig.Open());
        r.C.AddCurrentItemToBasket(11100000);
        Assert.That(r.C.TryPay(), Is.False);
        Assert.That((r.Sent.Count, r.NotEnoughCalls), Is.EqualTo((0, 1)));
    }

    [Test] public void APaidBasketSendsMode0x16WithItsItems() {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        r.C.AddCurrentItemToBasket(11100000);
        Assert.That(r.C.TryPay(), Is.True);
        Assert.That(r.Sent.Count, Is.EqualTo(1));
        var req = (BeautyColoringRequest)r.Sent[0];
        Assert.That(req.Items.Length, Is.EqualTo(1));
        Assert.That(Bytes(req)[0], Is.EqualTo(0x16));
        Assert.That(r.C.Dyeing, Is.True);
    }

    [Test] public void TheSaveAnswerStaysUnsentWithoutAnAnsweredBill() {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        r.C.AddCurrentItemToBasket(11100000);
        Assert.That(r.C.OpenBill(), Is.False, "no bill dialog is set");
        Assert.That(r.Sent, Is.Empty);
    }

    [Test] public void TheFlowStaysShutWithoutACustomize() {
        Packs();
        var flow = new ItemColoringFlow { CreateController = () => new ItemColoringDialogController() };
        Assert.That(flow.Open(), Is.False);
        Assert.That(flow.IsOpen, Is.False);
    }

    [Test] public void TheRouterGivesTheBodyToTheOpenColoringDialog() {
        Packs();
        var cu = new Cust();
        ItemColoringDialogController made = null;
        var flow = new ItemColoringFlow { CreateCustomize = () => cu, CreateController = () => made = new ItemColoringDialogController() };
        Assert.That(flow.Open(), Is.True);
        var w = new ByteWriter(); w.WriteByte(1); w.WriteByte(3);
        w.WriteInt(11); w.WriteInt(5); w.WriteInt(5); w.WriteByte(1); w.WriteInt(6); w.WriteInt(7); w.WriteBool(false);
        for (int i = 0; i < 2; i++) { w.WriteByte(1); w.WriteInt(0); w.WriteInt(700); w.WriteShort(0); }
        BeautyPacketRouter.Route(w.Buffer[..w.Length], new BeautyShopFlow(), new BeautyStyleFlow(), _ => { }, flow);
        Assert.That((made.Kind, made.Shop.ShopId), Is.EqualTo((3, 11)));
    }

    [Test] public void TheTalkCommandOpensTheItemColourDialog() {
        Packs();
        var flow = new ItemColoringFlow { CreateCustomize = () => new Cust(), CreateController = () => new ItemColoringDialogController() };
        int ended = 0;
        var talk = new BeautyTalkCommand { Coloring = flow, TalkNpcId = () => 5, EndTalk = () => ended++, Send = _ => { } };
        Assert.That(talk.Run(BeautyTalkCommand.ShopDialog, "itemColor"), Is.True);
        Assert.That((flow.IsOpen, ended), Is.EqualTo((true, 1)));
    }

    [Test] public void OnChangeItemSkipsTheFirstStringThenReadsUidAndIndex() {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        Assert.That(r.C.OnCallback("OnChangeItem", new object[] { "skipped", "777", 3 }), Is.True);
        Assert.That((r.C.SelectedIndex, r.Cu.Calls), Is.EqualTo((3, new List<string> { "restore900", "select777" })));
    }

    [Test] public void OnChangeItemNeedsThreeArguments() {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        r.C.OnCallback("OnChangeItem", new object[] { "777", 3 });
        Assert.That((r.C.SelectedIndex, r.Cu.Calls), Is.EqualTo((-1, new List<string>())));
    }

    [Test] public void ADoubleClickPaysOnceAndTheMovieWaitsForTheAnswer() {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        r.C.AddCurrentItemToBasket(11100000);
        Assert.That(r.C.InputLocked, Is.False);
        r.C.TryPay();
        r.C.TryPay();
        r.C.SendBasket();
        Assert.That((r.Sent.Count, r.C.Pending, r.C.InputLocked), Is.EqualTo((1, 1, true)));
        r.C.OnDyeApplied();
        Assert.That(r.C.InputLocked, Is.False);
    }

    [Test] public void LeavingClosesTheFlow() {
        var r = new Rig();
        var flow = new ItemColoringFlow { CreateCustomize = () => r.Cu, CreateController = () => r.C };
        Assert.That(flow.Open(), Is.True);
        r.C.Leave();
        Assert.That((r.C.Closing, flow.IsOpen, r.Closed), Is.EqualTo((true, false, 1)));
    }

    [Test] public void TheItemEventAfterTheSendEndsThePendingAndUnlocksTheMovie() {
        var r = new Rig();
        r.C.OnPacket(Rig.Open());
        r.C.AddCurrentItemToBasket(11100000);
        r.C.TryPay();
        Assert.That((r.C.Pending, r.C.InputLocked), Is.EqualTo((1, true)));
        r.C.OnItemDyed(11100000);
        Assert.That((r.C.Pending, r.C.InputLocked, r.C.Basket.Count), Is.EqualTo((0, false, 0)));
    }
}
