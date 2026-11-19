using Ms2.Formats.Pack;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The black market manager (CBlackMarketMan, blackmarketman.cpp): the sends, the own and search lists on the
/// server's answers and the result notices by string id. Texts are the ids, so no language's strings. The item tests need
/// MS2_DATA.</summary>
public class BlackMarketBookTests
{
    sealed class FakeDialog : IBlackMarketDialog
    {
        public readonly List<string> Calls = new();
        public IReadOnlyList<BlackMarketEntry> Own;
        public BlackMarketEntry Last;
        public void BuildOwnerItemList(IReadOnlyList<BlackMarketEntry> own) { Own = own; Calls.Add($"own {own.Count}"); }
        public void OwnAdded(BlackMarketEntry e) { Last = e; Calls.Add($"added {e.Id}"); }
        public void OwnRemoved(BlackMarketEntry e, int reason) { Last = e; Calls.Add($"removed {e.Id} {reason}"); }
        public void BuildItemList(int first, int end, int tail) => Calls.Add($"list {first} {end} {tail}");
        public void Bought(BlackMarketEntry e, int count) { Last = e; Calls.Add($"bought {e.Id} {count}"); }
        public void ReferencePrice(int itemId, int rarity, long price) => Calls.Add($"price {itemId} {rarity} {price}");
        public void SearchNotice(int type, int result, long value) => Calls.Add($"notice {type} {result} {value}");
        public readonly List<int> Replies = new();
        public void ReplyReceived(int action) => Replies.Add(action);
    }

    sealed class Rig
    {
        public readonly BlackMarketBook Book;
        public readonly List<IOutPacket> Sent = new();
        public readonly List<string> Shown = new();
        public FakeDialog Dlg = new();

        public Rig(ItemDataTable items = null)
        {
            Book = new BlackMarketBook
            {
                Send = Sent.Add, Common = id => $"#{id:x}", CommonKey = k => "$" + k, ItemName = id => $"item{id}",
                Dialog = () => Dlg, PropertyProtectionTime = () => 7,
            };
            if (items != null) Book.ItemData = items;
            Book.Notice += (t, type, log) => Shown.Add($"notice {t} {type} {log}");
            Book.Box += t => Shown.Add($"box {t}");
            Book.ChatLine += (t, k) => Shown.Add($"chat {t} {k}");
            Book.Sound += k => Shown.Add($"sound {k}");
            Book.OpenDialog += id => Shown.Add($"open {id:x}");
        }
    }

    static PackSet packs;
    static ItemDataTable Items()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
        }
        return new ItemDataTable(packs);
    }

    /// <summary>The first stackable item (slot max above 1) of the first item file.</summary>
    static ItemData Stackable(ItemDataTable t)
    {
        for (int id = 20000001; id < 20000500; id++)
            if (t.Get(id) is { SlotMax: > 1 } d) return d;
        for (int id = 30000001; id < 30000500; id++)
            if (t.Get(id) is { SlotMax: > 1 } d) return d;
        Assert.Ignore("no stackable item found");
        return null;
    }

    static BlackMarketProduct Product(long id, int count = 1, int bought = 0, int itemId = 0, long expire = 100) => new()
    {
        Id = id, Count = count, Bought = bought, UnitPrice = 10, ExpireTime = expire, ItemId = itemId,
        Item = itemId == 0 ? null : new EquipItem { Id = itemId, Uid = id * 10, Rarity = 1, Amount = count },
    };

    static byte[] Bytes(IOutPacket p)
    {
        var w = new Maple2.PacketLib.Tools.ByteWriter();
        p.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    [Test]
    public void TheSendsAreTheClientsModes()
    {
        var r = new Rig();
        r.Book.SendOwnList();
        r.Book.SendRegister(5, 1000, 3);
        r.Book.SendCancel(6);
        r.Book.SendBuy(7, 2);
        r.Book.SendReferencePrice(8, 4);
        Assert.That(r.Sent.Select(p => Bytes(p)[0]), Is.EqualTo(new byte[] { 1, 2, 3, 5, 8 }));
        Assert.That(r.Sent.All(p => p.Op == Outbound.BlackMarket));
        Assert.That(r.Book.OwnProducts, Is.Empty);          // nothing changes before the answer
    }

    [Test]
    public void ASearchIsSentOnlyWhenItCompactsAndPageOneReloads()
    {
        var r = new Rig();
        r.Book.SendSearch(null);
        r.Book.SendSearch(new BlackMarketSearch { OptionSearch = 1, Page = 1 });   // no stat range: Compact fails
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Book.Reload, Is.False);
        r.Book.SendSearch(new BlackMarketSearch { Page = 2 });
        Assert.That(r.Sent, Has.Count.EqualTo(1));
        Assert.That(r.Book.Reload, Is.False);
        r.Book.SendSearch(new BlackMarketSearch { Page = 1 });
        Assert.That(r.Sent, Has.Count.EqualTo(2));
        Assert.That(r.Book.Reload, Is.True);
    }

    [Test]
    public void TheOwnListIsReplacedAndAddedAndRemovedOnTheAnswers()
    {
        var r = new Rig();
        r.Book.OnBlackMarket(new BlackMarketMessage.OwnList(new[] { Product(1), Product(2) }));
        Assert.That(r.Book.OwnProducts.Select(e => e.Id), Is.EqualTo(new[] { 1L, 2L }));
        Assert.That(r.Dlg.Own, Has.Count.EqualTo(2));
        r.Book.OnBlackMarket(new BlackMarketMessage.OwnList(new[] { Product(3) }));
        Assert.That(r.Book.OwnProducts.Select(e => e.Id), Is.EqualTo(new[] { 3L }));

        r.Book.OnBlackMarket(new BlackMarketMessage.OwnAdded(Product(4)));
        r.Book.OnBlackMarket(new BlackMarketMessage.OwnAdded(Product(4, count: 9)));   // known id: replaced, no call
        Assert.That(r.Book.FindOwnProduct(4).Product.Count, Is.EqualTo(9));
        Assert.That(r.Dlg.Calls.Count(c => c.StartsWith("added")), Is.EqualTo(1));

        r.Book.OnBlackMarket(new BlackMarketMessage.OwnRemoved(99, 0));                // unknown id: nothing
        r.Book.OnBlackMarket(new BlackMarketMessage.OwnRemoved(3, 1));
        Assert.That(r.Dlg.Calls.Last(), Is.EqualTo("removed 3 1"));
        Assert.That(r.Book.OwnProducts.Select(e => e.Id), Is.EqualTo(new[] { 4L }));
        Assert.That(r.Dlg.Calls.Count(c => c.StartsWith("removed")), Is.EqualTo(1));
    }

    [Test]
    public void SoldPlaysTheSoundWritesTheChatLineAndAsksTheOwnList()
    {
        var r = new Rig();
        r.Book.OnBlackMarket(new BlackMarketMessage.Sold());
        Assert.That(r.Shown, Is.EqualTo(new[] { "sound BlackMarket_Sell", "chat #925 18" }));
        Assert.That(r.Sent.Single(), Is.TypeOf<BlackMarketOwnListRequest>());
    }

    [Test]
    public void OpenOpensTheDialogOnlyWhenItIsClosed()
    {
        var r = new Rig();
        r.Book.OnBlackMarket(new BlackMarketMessage.Open());
        Assert.That(r.Shown, Is.Empty);
        r.Dlg = null;
        r.Book.OnBlackMarket(new BlackMarketMessage.Open());
        Assert.That(r.Shown, Is.EqualTo(new[] { "open 4e97" }));
    }

    [Test]
    public void StatusAndReferencePriceAreKeptAndPassedOn()
    {
        var r = new Rig();
        r.Book.OnBlackMarket(new BlackMarketMessage.Status(1, 11, 22));
        Assert.That((r.Book.StatusFlag, r.Book.StatusTimeA, r.Book.StatusTimeB), Is.EqualTo(((byte)1, 11L, 22L)));
        r.Book.OnBlackMarket(new BlackMarketMessage.ReferencePrice(5, 2, 300));
        Assert.That(r.Dlg.Calls, Is.EqualTo(new[] { "price 5 2 300" }));
    }

    [TestCase(22, 0, "notice #34 1 True")]
    [TestCase(11, 2, "box #935")]
    [TestCase(12, 1, "notice #92b 1 True")]
    [TestCase(13, 0, "notice #88 1 True")]
    [TestCase(14, 0, "box #935")]
    [TestCase(30, 0, "box #936")]
    [TestCase(25, 0, "box #932")]
    [TestCase(26, 0, "box #931")]
    [TestCase(31, 2, "box #92f")]
    [TestCase(31, 0, "notice #92b 1 True")]
    [TestCase(29, 0, "notice #92d 1 True")]
    [TestCase(23, 3, "notice $s_err_reserved_other 1 True")]
    [TestCase(23, 0, "notice #87 1 True")]
    [TestCase(5, 0, "box #934")]
    [TestCase(32, 0, "box #92c")]
    [TestCase(36, 0, "box #8b7")]
    [TestCase(38, 0, "box #c07")]
    [TestCase(39, 0, "notice $s_blackmarket_error_not_useable 1 True")]
    [TestCase(27, 0, "notice $s_blackmarket_error_buy_expired 1 True")]
    [TestCase(42, 0, "box $s_blackmarket_error_cannot_buy_ownProduct")]
    [TestCase(41, 0, "box $s_blackmarket_error_purchase_count")]
    [TestCase(99, 0, "notice #92b 1 True")]
    public void EachResultShowsTheClientsString(int result, int type, string shown)
    {
        var r = new Rig();
        r.Book.OnBlackMarket(new BlackMarketMessage.Error((byte)type, result, 44, 0, 0));
        Assert.That(r.Shown, Is.EqualTo(new[] { shown }));
        Assert.That(r.Dlg.Calls, Is.EqualTo(new[] { $"notice {type} {result} 44" }));
    }

    [TestCase(24)]
    [TestCase(34)]
    [TestCase(35)]
    [TestCase(40)]
    public void SomeResultsShowNothingButStillReachTheSearchPage(int result)
    {
        var r = new Rig();
        r.Book.OnBlackMarket(new BlackMarketMessage.Error(1, result, 0, 0, 0));
        Assert.That(r.Shown, Is.Empty);
        Assert.That(r.Dlg.Calls, Is.EqualTo(new[] { $"notice 1 {result} 0" }));
    }

    [Test]
    public void TheFormattedResultsTakeTheirArguments()
    {
        var r = new Rig();
        r.Book.Common = id => id switch { 0x92b => "r{0}", 0x933 => "n{0}", 0x688 => "t{0} {1}", 0x5a5 => "w", 0x93d => "a{0}:{1}", 0x93e => "b{0}:{1}", _ => "" };
        foreach (int res in new[] { 12, 21, 37, 43, 44 })
            r.Book.OnBlackMarket(new BlackMarketMessage.Error(0, res, 0, 55, 3));
        Assert.That(r.Shown, Is.EqualTo(new[] { "notice r12 1 True", "box n28", "box t7 w", "box aitem55:3", "box bitem55:3" }));
    }

    [Test]
    public void ASearchResultKeepsOnlyItemsWithDataClampsAndReloadsOnPageOne()
    {
        var items = Items();
        var d = Stackable(items);
        var r = new Rig(items);
        r.Book.OnBlackMarket(new BlackMarketMessage.SearchResult(new[] { Product(1, 5, 2, d.Id), Product(2) }));
        Assert.That(r.Book.SearchProducts.Select(e => e.Id), Is.EqualTo(new[] { 1L }));      // no item data: dropped
        Assert.That(r.Book.SearchProducts[0].Item.Count, Is.EqualTo(Math.Min(3u, d.SlotMax)));
        Assert.That(r.Book.FindSearchProduct(2), Is.Null);
        Assert.That(r.Dlg.Calls.Last(), Is.EqualTo("list 0 0 -1"));

        r.Book.OnBlackMarket(new BlackMarketMessage.SearchResult(new[] { Product(3, 1, 0, d.Id), Product(4, 1, 0, d.Id) }));
        Assert.That(r.Book.SearchProducts.Select(e => e.Id), Is.EqualTo(new[] { 1L, 3L, 4L }));   // the next page appends
        Assert.That(r.Dlg.Calls.Last(), Is.EqualTo("list 1 2 0"));

        r.Book.SendSearch(new BlackMarketSearch { Page = 1 });
        r.Book.OnBlackMarket(new BlackMarketMessage.SearchResult(new[] { Product(5, 1, 0, d.Id) }));
        Assert.That(r.Book.SearchProducts.Select(e => e.Id), Is.EqualTo(new[] { 5L }));
        Assert.That(r.Book.Reload, Is.False);
        Assert.That(r.Dlg.Calls.Last(), Is.EqualTo("list 0 0 -1"));
    }

    [Test]
    public void BoughtAddsTheCountAndMarksSoldOutOrOwn()
    {
        var items = Items();
        var d = Stackable(items);
        var r = new Rig(items);
        r.Book.OnBlackMarket(new BlackMarketMessage.SearchResult(new[] { Product(1, 5, 0, d.Id), Product(2, 5, 0, d.Id), Product(3, 5, 0, d.Id) }));
        r.Book.OnBlackMarket(new BlackMarketMessage.OwnAdded(Product(3, 5, 0, d.Id)));
        r.Dlg.Calls.Clear();

        r.Book.OnBlackMarket(new BlackMarketMessage.Bought(9, 1));                 // not in the search: nothing
        Assert.That(r.Dlg.Calls, Is.Empty);

        r.Book.OnBlackMarket(new BlackMarketMessage.Bought(1, 2));
        var e1 = r.Book.FindSearchProduct(1);
        Assert.That((e1.Product.Bought, e1.Item.Count, e1.StateA), Is.EqualTo((2, Math.Min(3u, d.SlotMax), false)));
        Assert.That(e1.GetState(false, 0), Is.EqualTo(2));
        Assert.That(r.Dlg.Calls, Is.EqualTo(new[] { "list 3 2 2", "bought 1 2" }));

        r.Book.OnBlackMarket(new BlackMarketMessage.Bought(2, 5));
        Assert.That(r.Book.FindSearchProduct(2).StateA, Is.True);                  // none remains
        Assert.That(r.Book.FindSearchProduct(2).GetState(false, 0), Is.EqualTo(4));
        r.Book.OnBlackMarket(new BlackMarketMessage.Bought(3, 1));
        Assert.That(r.Book.FindSearchProduct(3).StateA, Is.True);                  // an own product

        r.Dlg = null;
        r.Book.OnBlackMarket(new BlackMarketMessage.Bought(1, 1));
        Assert.That(r.Book.FindSearchProduct(1).Product.Bought, Is.EqualTo(3));
    }

    [Test]
    public void TheStateIsTheClients()
    {
        var e = new BlackMarketEntry(new BlackMarketProduct { ExpireTime = 100, StateB = 1 }, null);
        Assert.That(e.GetState(true, 50), Is.EqualTo(5));
        Assert.That(e.GetState(false, 50), Is.EqualTo(2));
        Assert.That(e.GetState(false, 100), Is.EqualTo(3));
        e.StateA = true;
        Assert.That(e.GetState(false, 50), Is.EqualTo(4));
        e.Product.StateB = 0;
        Assert.That(e.GetState(true, 200), Is.EqualTo(3));
    }

    // The expected values are the client's precompiled Lua chunk run in Lua 5.1 on the same inputs.
    [TestCase(0L, 0L), TestCase(1L, 1L), TestCase(10L, 1L), TestCase(100L, 1L), TestCase(101L, 2L), TestCase(700L, 7L),
     TestCase(1234567L, 12346L), TestCase(9999999L, 100000L), TestCase(10000001L, 100000L), TestCase(9900000000L, 100000L)]
    public void RegisterDepositIsTheLuaDeposit(long total, long deposit)
    {
        Assert.That(BlackMarketBook.RegisterDeposit(total), Is.EqualTo(deposit));
        Assert.That(BlackMarketBook.RegisterDepositMax, Is.EqualTo(100000));
        Assert.That(BlackMarketBook.RegisterDepositPercent, Is.EqualTo(1));
    }

    [TestCase(1L, 0L), TestCase(19L, 0L), TestCase(20L, 1L), TestCase(999L, 49L), TestCase(12345L, 617L),
     TestCase(9900000000L, 495000000L)]
    public void BuyFeeIsTheLuaFeeWithoutADiscount(long price, long fee)
    {
        var r = new Rig();
        Assert.That(r.Book.BuyFee(Item.Create(new ItemData(), 3), price), Is.EqualTo(fee));
        Assert.That(r.Book.BuyFee(null, price), Is.EqualTo(0));
        Assert.That(r.Book.CostRatePermyriad(), Is.EqualTo(500));
    }

    [Test]
    public void BuyFeeUsesTheDiscountRateAsAFloat()
    {
        var r = new Rig();
        r.Book.FeeDiscountRate = () => 250;
        Assert.That(r.Book.CostRate(), Is.EqualTo(250 / 10000.0f));
        Assert.That(r.Book.CostRatePermyriad(), Is.EqualTo(250));
        Assert.That(r.Book.BuyFee(Item.Create(new ItemData(), 1), 1000), Is.EqualTo((long)(1000f * (250 / 10000.0f))));
        r.Book.FeeDiscountRate = () => 0;
        Assert.That(r.Book.BuyFee(Item.Create(new ItemData(), 1), 1000), Is.EqualTo(0));
    }

    [Test]
    public void CheckRegisterRunsTheClientsChecksInOrderAndSendsNothing()
    {
        var r = new Rig();
        var item = Item.Create(new ItemData(), 1);
        item.Count = 5;
        bool blocked = false;
        r.Book.FindItem = uid => uid == 7 ? item : null;
        r.Book.Mesos = () => 3;
        r.Book.ActionBlocked = id => blocked && id == 0x25;
        long dep;
        Assert.That(r.Book.CheckRegister(0, 100, 1, out dep), Is.EqualTo(BlackMarketBook.RegisterCheck.None));
        Assert.That(r.Book.CheckRegister(8, 100, 1, out dep), Is.EqualTo(BlackMarketBook.RegisterCheck.NoItem));
        Assert.That(r.Book.CheckRegister(7, 100, 6, out dep), Is.EqualTo(BlackMarketBook.RegisterCheck.LackCount));
        blocked = true;
        Assert.That(r.Book.CheckRegister(7, 1, 1, out dep), Is.EqualTo(BlackMarketBook.RegisterCheck.Blocked));
        blocked = false;
        Assert.That(r.Book.CheckRegister(7, 1, 5, out dep), Is.EqualTo(BlackMarketBook.RegisterCheck.PriceRange), "5 < min 10");
        Assert.That(r.Book.CheckRegister(7, 2, 5, out dep), Is.EqualTo(BlackMarketBook.RegisterCheck.Terms), "10 is the min");
        Assert.That(dep, Is.EqualTo(1));
        Assert.That(r.Book.CheckRegister(7, 4950000001, 2, out dep), Is.EqualTo(BlackMarketBook.RegisterCheck.PriceRange));
        Assert.That(r.Book.CheckRegister(7, 101, 2, out dep), Is.EqualTo(BlackMarketBook.RegisterCheck.Terms));
        Assert.That(dep, Is.EqualTo(3), "202 * 0.01 rounds up");
        Assert.That(r.Book.CheckRegister(7, 101, 3, out dep), Is.EqualTo(BlackMarketBook.RegisterCheck.LackDeposit));
        Assert.That(dep, Is.EqualTo(4));
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Shown, Is.Empty);
    }

    [Test]
    public void CheckRegisterStopsAtTheOwnListingLimit()
    {
        var r = new Rig();
        var item = Item.Create(new ItemData(), 1);
        r.Book.FindItem = _ => item;
        r.Book.Mesos = () => 1000;
        r.Book.OnBlackMarket(new BlackMarketMessage.OwnList(Enumerable.Range(1, 0x1b).Select(i => Product(i)).ToArray()));
        Assert.That(r.Book.CheckRegister(7, 10, 1, out _), Is.EqualTo(BlackMarketBook.RegisterCheck.Terms));
        r.Book.OnBlackMarket(new BlackMarketMessage.OwnList(Enumerable.Range(1, 0x1c).Select(i => Product(i)).ToArray()));
        Assert.That(r.Book.CheckRegister(7, 10, 1, out _), Is.EqualTo(BlackMarketBook.RegisterCheck.Full));
    }

    [Test]
    public void ConfirmRegisterSendsOnYesWithoutADiscount()
    {
        var r = new Rig();
        Assert.That(r.Book.ConfirmRegister(1, 7, 10, 1), Is.False);
        Assert.That(r.Sent, Is.Empty);
        r.Book.FeeDiscountRate = () => 100;
        Assert.That(r.Book.ConfirmRegister(0, 7, 10, 1), Is.False, "the discount box sends on its close");
        Assert.That(r.Sent, Is.Empty);
        r.Book.FeeDiscountRate = () => -1;
        Assert.That(r.Book.ConfirmRegister(0, 7, 10, 1), Is.True);
        Assert.That(r.Sent, Has.Count.EqualTo(1));
        Assert.That(r.Sent[0], Is.TypeOf<BlackMarketRegisterRequest>());
    }

    [Test]
    public void SellPriceConstantsReadAsInt64AndClamp()
    {
        Func<string, string> set(string min, string max) => k => k == "BlackMarketSellMinPrice" ? min : k == "BlackMarketSellMaxPrice" ? max : null;
        // read exactly, not through a float (9900000000 as a float is 9900000256)
        Assert.That(GameTables.BlackMarketPrices(set("10", "10000000000"), 10, 9900000000), Is.EqualTo((10L, 10000000000L)));
        Assert.That(GameTables.BlackMarketPrices(set(null, null), 10, 9900000000), Is.EqualTo((10L, 9900000000L)));
        // min below 0 is clamped to 0; max below min is raised to min
        Assert.That(GameTables.BlackMarketPrices(set("-5", "100"), 10, 9900000000), Is.EqualTo((0L, 100L)));
        Assert.That(GameTables.BlackMarketPrices(set("500", "100"), 10, 9900000000), Is.EqualTo((500L, 500L)));
        Assert.That(GameTables.BlackMarketPrices(set("-5", "-9"), 10, 9900000000), Is.EqualTo((0L, 0L)));
    }

    [Test]
    public void EveryReplyReachesTheDialogWithItsAction()
    {
        // vfunc_79 @0x1418cf920: the packet's first byte, or the error's type when the first byte is 0
        var r = new Rig();
        r.Book.OnBlackMarket(new BlackMarketMessage.Bought(9, 1) with { Mode = 5 });
        r.Book.OnBlackMarket(new BlackMarketMessage.Error(3, 7, 0, 0, 0) with { Mode = 0 });
        r.Book.OnBlackMarket(new BlackMarketMessage.OwnList(new[] { Product(1) }) with { Mode = 1 });
        Assert.That(r.Dlg.Replies, Is.EqualTo(new[] { 5, 3, 1 }));
    }
}
