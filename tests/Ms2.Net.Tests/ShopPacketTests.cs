using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Inbound 0x51 (Shop) commands 0 and 1 in the order UIShopDialog::ReadShopInfo and CNpcShopProductCli::ReadPacket read them.</summary>
public class ShopPacketTests {
    static ByteReader Reader(ByteWriter w) => new(w.Buffer.AsSpan(0, w.Length).ToArray());

    [Test] public void ShopInfoReadsTheHeaderInTheClientsOrder() {
        // written by hand, field by field as ReadShopInfo reads them
        var w = new ByteWriter();
        w.WriteByte(0);
        w.WriteInt(11000004); w.WriteInt(106); w.WriteLong(1_700_000_000); w.WriteInt(3600); w.WriteShort(12); w.WriteInt(107);
        w.WriteByte(1); w.WriteBool(false); w.WriteBool(true); w.WriteBool(false); w.WriteByte(1); w.WriteBool(true); w.WriteBool(true);
        w.WriteBool(false); w.WriteBool(true); w.WriteString("shop_icon");
        w.WriteByte(0); w.WriteByte(2); w.WriteInt(90000001); w.WriteInt(300); w.WriteBool(true); w.WriteInt(5); w.WriteByte(1);
        w.WriteBool(false); w.WriteBool(true);
        var r = Reader(w);
        var p = ShopPacket.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        var s = p.Info;
        Assert.That((p.Command, s.NpcId, s.ShopId, s.ResetTime, s.ResetInterval, s.ProductCount, s.TabShopId),
            Is.EqualTo((0, 11000004, 106, 1_700_000_000L, 3600, 12, 107)));
        Assert.That((s.OpenInventory, s.SellOnly, s.ResetEnable, s.Shuffle, s.ShopType, s.CheckEnableItems, s.ComparesItems, s.ProbInfo, s.HasNewTab),
            Is.EqualTo((true, false, true, false, 1, true, true, false, true)));
        Assert.That(s.Icon, Is.EqualTo("shop_icon"));
        Assert.That((s.ResetCurrency, s.ResetTooltipCurrency, s.ResetCurrencyItem, s.ResetPrice, s.ResetLimited, s.ResetCount, s.ResetType, s.HideReset, s.ResetByAccount),
            Is.EqualTo((0, 2, 90000001, 300, true, 5, 1, false, true)));
        // the writer gives the same bytes
        Assert.That(ShopPacket.WriteInfo(s), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
    }

    [Test] public void ShopInfoWithoutResetHidesTheResetButton() {
        var info = new ShopInfo { NpcId = 1, ShopId = 2, ResetEnable = false, HideReset = false, Icon = "" };
        var r = new ByteReader(ShopPacket.WriteInfo(info));
        var s = ShopPacket.Read(r).Info;
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That((s.ResetEnable, s.HideReset, s.ResetPrice), Is.EqualTo((false, true, 0)));
    }

    [Test] public void ProductsReadEveryMemberAndTheItem() {
        var a = new ShopProduct {
            Serial = 3, ItemId = 13200167, Currency = 0, Price = 1200, OriginalPrice = 1500, Rarity = 2, Count = 1, Limit = 3, Bought = 2,
            GuildTrophy = 7, Categories = "Weapons,Armor", Achieve = 21, AchieveGrade = 1, ChampionshipRank = 4, ChampionshipScore = 600,
            GuildNpc = 2, GuildNpcLevel = 3, PremiumSlot = true, Bundle = 1, Label = 4, SaleTag = 1, IconTag = "meso", Alliance = 5,
            FameGrade = 6, MainItem = true,
            Period = new ShopBuyPeriod { HasTime = true, Start = 10, End = 20, HasPart = true, PartTimes = { (3600, 7200) }, Days = { 1, 3 } },
            Life = new ShopItemLife(2, 0, 99),
        };
        var b = new ShopProduct { Serial = 4, ItemId = 20000001, Currency = 1, CurrencyItemId = 30000001, CurrencyItemTag = 8, Price = 10, Rarity = 1,
            Count = 50, Limit = 10, Bought = 20, Bundle = 10, Life = new ShopItemLife(3, 17, 0) };
        var r = new ByteReader(ShopPacket.WriteProducts(new[] { a, b }));
        var p = ShopPacket.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(p.Command, Is.EqualTo(1));
        var x = p.Products[0];
        Assert.That(x with { Item = null, Period = null }, Is.EqualTo(a with { Item = null, Period = null }));
        Assert.That((x.Period.Start, x.Period.End, x.Period.PartTimes, x.Period.Days), Is.EqualTo((10L, 20L, a.Period.PartTimes, a.Period.Days)));
        Assert.That((x.Item.Id, x.Item.Rarity, x.Item.Amount), Is.EqualTo((13200167, 2, 1)));
        // one bought of 3: 1 left, not sold out
        Assert.That((x.BuyCount, x.Remaining, x.SoldOut), Is.EqualTo((2, 1, false)));
        var y = p.Products[1];
        Assert.That((y.CurrencyItemId, y.CurrencyItemTag, y.Life), Is.EqualTo((30000001, 8, (ShopItemLife?) new ShopItemLife(3, 17, 0))));
        // SetBuyItemCount: 20 items bought in bundles of 10 are 2 purchases; 8 of 10 left
        Assert.That((y.BuyCount, y.Remaining, y.SoldOut), Is.EqualTo((2, 8, false)));
    }

    [Test] public void ALimitedProductWithNoneLeftIsSoldOut() {
        Assert.That(new ShopProduct { Limit = 1, Bought = 1 }.SoldOut, Is.True);
        Assert.That(new ShopProduct { Limit = 0, Bought = 5 }.SoldOut, Is.False);
    }

    [Test] public void OtherCommandsCarryOnlyTheCommand() {
        var w = new ByteWriter(); w.WriteByte(15); w.WriteByte(3);
        var p = ShopPacket.Read(Reader(w));
        Assert.That((p.Command, p.Info, p.Products), Is.EqualTo((15, (ShopInfo) null, (IReadOnlyList<ShopProduct>) null)));
    }
}
