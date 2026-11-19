using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIBlackMarketDialog and its item pages (BlackMarketDialogController): the buy, cancel and register gates and
/// their sends, run on the movie uiblackmarketdialog (needs MS2_DATA). Texts are ids, so no language's strings.</summary>
[NonParallelizable]
public class BlackMarketDialogTests
{
    [Test]
    public void TheHelpersAreTheClients()
    {
        Assert.That(BlackMarketDialogController.SortCode("level", true), Is.EqualTo(0xb));
        Assert.That(BlackMarketDialogController.SortCode("price", false), Is.EqualTo(0x16));
        Assert.That(BlackMarketDialogController.SortCode("x", true), Is.EqualTo(0));
        Assert.That(BlackMarketDialogController.JobMask(30, false), Is.EqualTo(8));
        Assert.That(BlackMarketDialogController.JobMask(200, false), Is.EqualTo(0xfff));
        Assert.That(BlackMarketDialogController.MinorCategory(30000), Is.EqualTo(99999));
        Assert.That(BlackMarketDialogController.MinorCategory(1200), Is.EqualTo(1299));
        Assert.That(BlackMarketDialogController.MinorCategory(1205), Is.EqualTo(1205));
        Assert.That(BlackMarketDialogController.OptionKey(1, true, 7), Is.EqualTo(11007));
        Assert.That(BlackMarketDialogController.OptionKey(2, false, 123), Is.EqualTo(20123));
        Assert.That(BlackMarketDialogController.ParseInt64(" -12x"), Is.EqualTo(-12));
        Assert.That(BlackMarketDialogController.ParseInt64("abc"), Is.EqualTo(0));
        Assert.That(BlackMarketDialogController.StateName(4), Is.EqualTo("soldout"));
    }

    // ---- the movie ----

    static PackSet packs;
    static MovieSource src;
    static GameTables tables;
    static ItemDataTable items;

    sealed class Rig
    {
        public readonly BlackMarketBook Book = new();
        public readonly List<IOutPacket> Sent = new();
        public readonly List<MessageBoxData> Boxes = new();
        public readonly List<string> Shown = new();
        public BlackMarketDialogController C;
        public UiHost H;
        public long Mesos = 1_000_000;
        public ItemData Data;

        public Rig()
        {
            if (packs == null)
            {
                var d = Environment.GetEnvironmentVariable("MS2_DATA");
                if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
                packs = new PackSet(d);
                src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
                tables = new GameTables(packs, "en");
                items = new ItemDataTable(packs);
            }
            for (int id = 20000001; id < 20000500 && Data == null; id++)
                if (items.Get(id) is { SlotMax: > 1 } sd) Data = sd;
            if (Data == null) Assert.Ignore("no stackable item found");
            Book.Send = Sent.Add;
            Book.Common = id => $"#{id:x}";
            Book.CommonKey = k => "$" + k;
            Book.ItemName = id => $"item{id}";
            Book.ItemData = items;
            Book.Mesos = () => Mesos;
            C = new BlackMarketDialogController(Book)
            {
                ShowBox = Boxes.Add, ShowText = t => Shown.Add("box " + t), Notice = t => Shown.Add("notice " + t),
                ShowTerms = (bit, title, text, cb) => Boxes.Add(new MessageBoxData(0) { Msg = "terms", Callback = cb }),
                AgreeTerms = _ => { }, ServerTime = () => 50, OwnAccountId = () => 77,
                Table = tables.BlackMarket,   // the categories the client has loaded before initData (blackmarkettable.xml)
            };
            Book.Dialog = () => C;
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            H = new UiHost(p, C, new UiModel(tables));
            Assert.That(H.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
            foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
            Assert.That(rt.Errors, Is.Empty, "the dialog's init logs no AS3 error");
        }

        public BlackMarketProduct Product(long id, int count = 1, long seller = 5, long expire = 100) => new()
        {
            Id = id, Count = count, UnitPrice = 1000, ListedTime = 10, ExpireTime = expire, ItemId = Data.Id,
            SellerAccountId = seller,
            Item = new EquipItem { Id = Data.Id, Uid = id * 10, Rarity = 1, Amount = count },
        };

        public void Search(params BlackMarketProduct[] ps)
        {
            Book.OnBlackMarket(new BlackMarketMessage.SearchResult(ps));
            C.OnCallback("onChangeTab", new object[] { 0 });
        }

        public void Answer(int index, int result) => Boxes[index].Callback(result, Boxes[index]);
        public IEnumerable<BlackMarketBuyRequest> Buys => Sent.OfType<BlackMarketBuyRequest>();
    }

    [Test]
    public void ABuyIsSentOnceAndTheDialogStaysLockedUntilTheAnswer()
    {
        var r = new Rig();
        r.Search(r.Product(1));
        r.C.OnCallback("OnClickBuy", new object[] { "1", "sale" });
        Assert.That(r.Boxes, Has.Count.EqualTo(1), "one listed: the confirm at once");
        r.Answer(0, 0);
        Assert.That(r.Buys.Select(b => (b.Id, b.Count)), Is.EqualTo(new[] { (1L, 1) }));
        Assert.That(r.C.Locked, Is.True, "BuyProduct locks the root");
        r.C.OnCallback("OnClickBuy", new object[] { "1", "sale" });            // the double click's second
        Assert.That(r.Boxes, Has.Count.EqualTo(1), "locked: a second click on Buy shows no confirm");
        Assert.That(r.Buys.Count(), Is.EqualTo(1));
        r.C.ReplyReceived(5);
        Assert.That(r.C.Locked, Is.False, "the answer of mode 5 unlocks");
        r.C.OnCallback("OnClickBuy", new object[] { "1", "sale" });
        Assert.That(r.Boxes, Has.Count.EqualTo(2), "unlocked: Buy takes clicks again");
        Assert.That(r.Book.SearchProducts[0].Item.Count, Is.EqualTo(1u), "nothing local changes before the answer");
    }

    [Test]
    public void RejectedBuysSendNothing()
    {
        var r = new Rig();
        r.Search(r.Product(1), r.Product(2, seller: 77), r.Product(3, expire: 10));
        r.C.OnCallback("OnClickBuy", new object[] { "2", "sale" });           // own product
        r.C.OnCallback("OnClickBuy", new object[] { "3", "sale" });           // expired
        r.C.OnCallback("OnClickBuy", new object[] { "9", "sale" });           // unknown
        r.Mesos = 999;
        r.C.OnCallback("OnClickBuy", new object[] { "1", "sale" });           // a unit costs 1000
        Assert.That(r.Boxes, Is.Empty);
        Assert.That(r.Shown, Has.Some.EqualTo("notice #34"));
        r.Mesos = 1000;
        r.C.OnCallback("OnClickBuy", new object[] { "1", "sale" });
        r.Mesos = 999;                                                         // spent before the yes: BuyProduct's re-check
        r.Answer(0, 0);
        r.Mesos = 1000;
        r.C.OnCallback("OnClickBuy", new object[] { "1", "sale" });
        r.Answer(1, 1);                                                        // no
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.C.Locked, Is.False, "a rejected buy does not lock");
        r.Book.ActionBlocked = _ => true;
        r.C.OnCallback("OnClickBuy", new object[] { "1", "sale" });
        Assert.That(r.Boxes, Has.Count.EqualTo(2), "a blocked action shows no box");
    }

    [Test]
    public void ACountBoxBuysTheCountOfItsTag()
    {
        var r = new Rig();
        r.Search(r.Product(1, count: 5));
        r.C.OnCallback("OnClickBuy", new object[] { "1", "sale" });
        Assert.That(r.Boxes.Single().ArgLong(0, 0), Is.EqualTo(1L));
        r.Boxes[0].ClickedTag = "0,3";
        r.Answer(0, 0);
        r.Answer(1, 0);
        Assert.That(r.Buys.Select(b => (b.Id, b.Count)), Is.EqualTo(new[] { (1L, 3) }));
    }

    [Test]
    public void AnErrorAnswerUnlocksThroughItsType()
    {
        // vfunc_79 @0x1418cf920 takes the error's type (mode 0) as the action the pages wait for.
        var r = new Rig();
        r.Search(r.Product(1));
        r.C.OnCallback("OnClickBuy", new object[] { "1", "sale" });
        r.Answer(0, 0);
        Assert.That(r.C.Locked, Is.True);
        r.Book.OnBlackMarket(new BlackMarketMessage.Error(3, 5, 0, 0, 0));
        Assert.That(r.C.Locked, Is.True, "an error of the cancel's type does not answer the buy");
        r.Book.OnBlackMarket(new BlackMarketMessage.Error(5, 5, 0, 0, 0));
        Assert.That(r.C.Locked, Is.False, "an error of type 5 answers the buy");
        Assert.That(r.Book.SearchProducts[0].Item.Count, Is.EqualTo(1u), "an error changes nothing local");

        r.Book.OnBlackMarket(new BlackMarketMessage.OwnList(new[] { r.Product(4, seller: 77) }));
        r.C.OnCallback("onChangeTab", new object[] { 1 });
        r.C.OnCallback("OnClickCancelSell", new object[] { "4" });
        r.Answer(r.Boxes.Count - 1, 0);
        Assert.That(r.C.Locked, Is.True);
        r.Book.OnBlackMarket(new BlackMarketMessage.Error(3, 5, 0, 0, 0));
        Assert.That(r.C.Locked, Is.False, "an error of type 3 answers the cancel");
        Assert.That(r.Book.OwnProducts, Has.Count.EqualTo(1), "an error removes nothing");
    }

    [Test]
    public void CancelSendsOnlyOnYes()
    {
        var r = new Rig();
        r.Book.OnBlackMarket(new BlackMarketMessage.OwnList(new[] { r.Product(4, seller: 77) }));
        r.C.OnCallback("onChangeTab", new object[] { 1 });
        r.C.OnCallback("OnClickCancelSell", new object[] { "4" });
        r.Answer(0, 1);
        Assert.That(r.Sent, Is.Empty);
        r.C.OnCallback("OnClickCancelSell", new object[] { "4" });
        r.Answer(1, 0);
        Assert.That(r.Sent.OfType<BlackMarketCancelRequest>().Select(p => p.Id), Is.EqualTo(new[] { 4L }));
        Assert.That(r.Book.OwnProducts, Has.Count.EqualTo(1), "removed only on the answer");
        Assert.That(r.C.Locked, Is.True);
        r.C.ReplyReceived(3);
        Assert.That(r.C.Locked, Is.False);
    }

    [Test]
    public void RegisterSendsAfterTheTermsYesOnly()
    {
        var r = new Rig();
        var item = Item.Create(items, r.Data.Id, 1);
        item.Uid = 500;
        item.Count = 4;
        r.Book.FindItem = uid => uid == 500 ? item : null;
        r.C.OnCallback("onChangeTab", new object[] { 1 });
        r.C.OnCallback("OnClickRegister", new object[] { "501", "100", 1 });   // no item
        r.C.OnCallback("OnClickRegister", new object[] { "500", "100", 5 });   // lacks count
        r.C.OnCallback("OnClickRegister", new object[] { "500", "1", 1 });     // below the min price
        Assert.That(r.Shown.Take(2), Is.EqualTo(new[] { "box #935", "box #931" }));
        Assert.That(r.Shown[2], Does.StartWith("box #930"), "below the min price");
        Assert.That(r.Boxes, Is.Empty);
        r.C.OnCallback("OnClickRegister", new object[] { "500", "100", 2 });
        r.Answer(0, 1);
        Assert.That(r.Sent, Is.Empty, "no on the terms");
        r.C.OnCallback("OnClickRegister", new object[] { "500", "100", 2 });
        r.Answer(1, 0);
        Assert.That(r.Sent.OfType<BlackMarketRegisterRequest>().Select(p => (p.ItemUid, p.Price, p.Count)),
            Is.EqualTo(new[] { (500L, 100L, 2) }));
        Assert.That(item.Count, Is.EqualTo(4u), "nothing local changes before the answer");
    }

    [Test]
    public void ASlotDropAsksForItsReferencePriceOnce()
    {
        var r = new Rig();
        var item = Item.Create(items, r.Data.Id, 2);
        item.Uid = 600;
        r.Book.FindItem = uid => uid == 600 ? item : null;
        r.C.SlotItemUid = s => s == "slot" ? 600 : 0;
        r.C.CanRegister = _ => true;
        r.C.OnCallback("onChangeTab", new object[] { 1 });
        var ret = r.H.Obj();
        r.C.OnCallback("OnEnableRegister", new object[] { "slot", ret });
        r.C.OnCallback("OnEnableRegister", new object[] { "slot", r.H.Obj() });
        Assert.That(Rt.ToBoolean(r.H.Get(ret, "success")), Is.True);
        Assert.That(r.Sent.OfType<BlackMarketReferencePriceRequest>().Select(p => (p.ItemId, p.Rarity)),
            Is.EqualTo(new[] { (r.Data.Id, item.Rarity) }), "SelectItem sends mode 8 for a new item only");
        r.C.CanRegister = _ => false;
        r.C.OnCallback("OnClearSlot", Array.Empty<object>());
        r.C.OnCallback("OnEnableRegister", new object[] { "slot", r.H.Obj() });
        Assert.That(r.Sent, Has.Count.EqualTo(1), "a slot the client refuses sends nothing");
    }
}
