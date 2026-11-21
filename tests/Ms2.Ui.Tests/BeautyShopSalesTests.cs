using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CGameRule's beauty shop sale lists (gamerule.cpp:SetBeautyShopSaleEvent .. FUN_140476d50) as the game events fill them.</summary>
public class BeautyShopSalesTests
{
    static GameEventMessage Msg(byte kind, params BeautyShopSaleEvent[] e) =>
        new(kind, Array.Empty<ShutdownInteractObjectEvent>(), Array.Empty<int>(), false) { BeautySaleEvents = e };
    static GameEventMessage Removed(params int[] ids) => new(2, Array.Empty<ShutdownInteractObjectEvent>(), ids, false);

    [Test]
    public void NoEventMeansNoSaleAndTheFullPrice()
    {
        var s = new BeautyShopSales();
        Assert.That(s.SaleIndex(7, 50), Is.EqualTo(BeautyShopSales.NoSale));
        Assert.That(s.DiscountedPrice(7, 1000, 50), Is.EqualTo(1000u));
    }

    [Test]
    public void TheFlagPicksTheListAndTheBetterRateWins()
    {
        // m_d8 == 0 goes to list 1, else list 0; the better rate wins and its list is the index
        var s = new BeautyShopSales();
        s.Apply(Msg(0, new BeautyShopSaleEvent(1, 7, 2000, 0), new BeautyShopSaleEvent(2, 7, 3000, 1)));
        Assert.That(s.SaleIndex(7, 50), Is.EqualTo(0));
        Assert.That(s.DiscountedPrice(7, 1000, 50), Is.EqualTo(700u));
        Assert.That(s.SaleIndex(8, 50), Is.EqualTo(BeautyShopSales.NoSale));
    }

    [Test]
    public void ListZeroIsSkippedAtLevelZeroAndATieKeepsTheFirstList()
    {
        var s = new BeautyShopSales();
        s.Apply(Msg(0, new BeautyShopSaleEvent(1, 7, 2000, 0), new BeautyShopSaleEvent(2, 7, 3000, 1)));
        Assert.That(s.SaleIndex(7, 0), Is.EqualTo(1));
        var t = new BeautyShopSales();
        t.Apply(Msg(0, new BeautyShopSaleEvent(1, 7, 2000, 0), new BeautyShopSaleEvent(2, 7, 2000, 1)));
        Assert.That(t.SaleIndex(7, 50), Is.EqualTo(0));
    }

    [Test]
    public void RemoveAndUpdateAndAddFollowTheManager()
    {
        var s = new BeautyShopSales();
        s.Apply(Msg(0, new BeautyShopSaleEvent(1, 7, 2000, 0)));
        s.Apply(Msg(1, new BeautyShopSaleEvent(1, 7, 9000, 0)));      // an id already held is not added again
        Assert.That(s.DiscountedPrice(7, 1000, 50), Is.EqualTo(800u));
        s.Apply(Msg(3, new BeautyShopSaleEvent(1, 7, 5000, 1)));      // update: the old entry out, the new one in
        Assert.That((s.SaleIndex(7, 50), s.DiscountedPrice(7, 1000, 50)), Is.EqualTo((0, 500u)));
        s.Apply(Removed(1));
        Assert.That(s.SaleIndex(7, 50), Is.EqualTo(BeautyShopSales.NoSale));
        s.Apply(Msg(0, new BeautyShopSaleEvent(2, 7, 1000, 0)));
        s.Clear();
        Assert.That(s.SaleIndex(7, 50), Is.EqualTo(BeautyShopSales.NoSale));
    }
}
