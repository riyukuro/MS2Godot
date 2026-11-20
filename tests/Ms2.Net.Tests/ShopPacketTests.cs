using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Inbound 0x51 (Shop) and Outbound 0x28 (SendOp::Shop) layouts from UIShopDialog / CNpcShopProductCli / RequestSellItem.</summary>
public class ShopPacketTests {
    static ByteReader Reader(ByteWriter w) => new(w.Buffer.AsSpan(0, w.Length).ToArray());
    static byte[] WriteOut(IOutPacket p) {
        var w = new ByteWriter();
        p.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

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

    [Test] public void InboundSubCommandsReadInClientOrder() {
        // cmd 2: ReadProductUpdate (uishopdialog.cpp:1740): i32 serial, i32 buyCount
        var w2 = new ByteWriter(); w2.WriteByte(2); w2.WriteInt(42); w2.WriteInt(7);
        var r2 = Reader(w2);
        var p2 = ShopPacket.Read(r2);
        Assert.That(r2.Available, Is.EqualTo(0));
        Assert.That(p2.ProductUpdate, Is.EqualTo(new ShopProductUpdate(42, 7)));
        Assert.That(ShopPacket.WriteProductUpdate(42, 7), Is.EqualTo(w2.Buffer.AsSpan(0, w2.Length).ToArray()));

        // cmd 4: ReadPurchaseResult (uishopdialog.cpp:1834): u32 itemId, i32 count, u32 price, u8 rank, bool guildStorage
        var w4 = new ByteWriter(); w4.WriteByte(4); w4.WriteInt(20000001); w4.WriteInt(5); w4.WriteInt(2500); w4.WriteByte(3); w4.WriteBool(true);
        var r4 = Reader(w4);
        var p4 = ShopPacket.Read(r4);
        Assert.That(r4.Available, Is.EqualTo(0));
        Assert.That(p4.PurchaseResult, Is.EqualTo(new ShopPurchaseResult(20000001, 5, 2500u, 3, true)));
        Assert.That(ShopPacket.WritePurchaseResult(p4.PurchaseResult), Is.EqualTo(w4.Buffer.AsSpan(0, w4.Length).ToArray()));

        // cmd 6: ReadRepurchaseStart (uishopdialog.cpp:1778): u16 count
        var w6 = new ByteWriter(); w6.WriteByte(6); w6.WriteShort(3);
        var r6 = Reader(w6);
        var p6 = ShopPacket.Read(r6);
        Assert.That(r6.Available, Is.EqualTo(0));
        Assert.That((p6.Command, p6.RepurchaseCount), Is.EqualTo((6, 3)));
        Assert.That(ShopPacket.WriteRepurchaseStart(3), Is.EqualTo(w6.Buffer.AsSpan(0, w6.Length).ToArray()));

        // cmd 7: ReadRepurchaseAdd + ReadPacket mode 7 (npcshopproductbase.cpp:191-202): u16 count, [i32 serial, i32 itemId, u8 rarity, i64 price, item]
        var w7 = new ByteWriter(); w7.WriteByte(7); w7.WriteShort(1);
        w7.WriteInt(99); w7.WriteInt(11300001); w7.WriteByte(4); w7.WriteLong(5_000_000_000L);
        ItemReader.WriteEmptyBody(w7, 2);
        var r7 = Reader(w7);
        var p7 = ShopPacket.Read(r7);
        Assert.That(r7.Available, Is.EqualTo(0));
        Assert.That(p7.Products, Has.Count.EqualTo(1));
        Assert.That((p7.Products[0].Serial, p7.Products[0].ItemId, p7.Products[0].Rarity, p7.Products[0].Price, p7.Products[0].Count),
            Is.EqualTo((99, 11300001, 4, 5_000_000_000L, 2)));
        Assert.That(ShopPacket.WriteRepurchaseAdd(p7.Products), Is.EqualTo(w7.Buffer.AsSpan(0, w7.Length).ToArray()));

        // cmd 8: ReadRepurchaseRemoval (uishopdialog.cpp:1819): i32 serial
        var w8 = new ByteWriter(); w8.WriteByte(8); w8.WriteInt(99);
        var r8 = Reader(w8);
        var p8 = ShopPacket.Read(r8);
        Assert.That(r8.Available, Is.EqualTo(0));
        Assert.That((p8.Command, p8.RemoveSerial), Is.EqualTo((8, 99)));
        Assert.That(ShopPacket.WriteRepurchaseRemoval(99), Is.EqualTo(w8.Buffer.AsSpan(0, w8.Length).ToArray()));

        // cmd 9: ReadResetResult (uishopdialog.cpp:1756): bool success, [u32 itemId, u32 count if true]
        var w9Ok = new ByteWriter(); w9Ok.WriteByte(9); w9Ok.WriteBool(true); w9Ok.WriteInt(30000001); w9Ok.WriteInt(4);
        var r9Ok = Reader(w9Ok);
        var p9Ok = ShopPacket.Read(r9Ok);
        Assert.That(r9Ok.Available, Is.EqualTo(0));
        Assert.That(p9Ok.ResetResult, Is.EqualTo(new ShopResetResult(true, 30000001, 4)));
        Assert.That(ShopPacket.WriteResetResult(p9Ok.ResetResult), Is.EqualTo(w9Ok.Buffer.AsSpan(0, w9Ok.Length).ToArray()));

        var w9Fail = new ByteWriter(); w9Fail.WriteByte(9); w9Fail.WriteBool(false);
        var r9Fail = Reader(w9Fail);
        var p9Fail = ShopPacket.Read(r9Fail);
        Assert.That(r9Fail.Available, Is.EqualTo(0));
        Assert.That(p9Fail.ResetResult, Is.EqualTo(new ShopResetResult(false, 0, 0)));
        Assert.That(ShopPacket.WriteResetResult(p9Fail.ResetResult), Is.EqualTo(w9Fail.Buffer.AsSpan(0, w9Fail.Length).ToArray()));

        // cmd 11: ReadMeratStart (uishopdialog.cpp:1710): u16 count
        var w11 = new ByteWriter(); w11.WriteByte(11); w11.WriteShort(6);
        var r11 = Reader(w11);
        var p11 = ShopPacket.Read(r11);
        Assert.That(r11.Available, Is.EqualTo(0));
        Assert.That((p11.Command, p11.MeratCount), Is.EqualTo((11, 6)));

        // cmd 13: ReadNewStart (uishopdialog.cpp:1664): i32 npcId, i32 shopId, i32 tabShopId, u16 total
        var w13 = new ByteWriter(); w13.WriteByte(13); w13.WriteInt(11000004); w13.WriteInt(106); w13.WriteInt(205); w13.WriteShort(12);
        var r13 = Reader(w13);
        var p13 = ShopPacket.Read(r13);
        Assert.That(r13.Available, Is.EqualTo(0));
        Assert.That(p13.NewStart, Is.EqualTo(new ShopNewStart(11000004, 106, 205, 12)));
        Assert.That(ShopPacket.WriteNewStart(p13.NewStart), Is.EqualTo(w13.Buffer.AsSpan(0, w13.Length).ToArray()));

        // cmd 14: ReadNewProducts + CNpcShopItemNew::Read (npcshopdata.cpp:821)
        var np = new ShopNewProduct {
            ItemId = 11300001, Flag = true, Rarity = 3, Categories = "CP", ProductType = 1, ProductKind = 2,
            Period = new ShopBuyPeriod { HasTime = true, Start = 100, End = 200 },
        };
        var w14 = new ByteWriter(); w14.WriteByte(14); w14.WriteByte(1); np.Write(w14);
        var r14 = Reader(w14);
        var p14 = ShopPacket.Read(r14);
        Assert.That(r14.Available, Is.EqualTo(0));
        Assert.That(p14.NewProducts, Has.Count.EqualTo(1));
        Assert.That((p14.NewProducts[0].ItemId, p14.NewProducts[0].Flag, p14.NewProducts[0].Rarity, p14.NewProducts[0].Categories, p14.NewProducts[0].ProductType, p14.NewProducts[0].ProductKind),
            Is.EqualTo((11300001, true, (byte) 3, "CP", (byte) 1, (byte) 2)));
        Assert.That((p14.NewProducts[0].Period.Start, p14.NewProducts[0].Period.End), Is.EqualTo((100L, 200L)));
        Assert.That(ShopPacket.WriteNewProducts(new[] { np }), Is.EqualTo(w14.Buffer.AsSpan(0, w14.Length).ToArray()));

        // cmd 15: ReadShopError (uishopdialog.cpp:1858): i32 error, u8 cmd, i32 value
        var w15 = new ByteWriter(); w15.WriteByte(15); w15.WriteInt(11); w15.WriteByte(4); w15.WriteInt(0);
        var r15 = Reader(w15);
        var p15 = ShopPacket.Read(r15);
        Assert.That(r15.Available, Is.EqualTo(0));
        Assert.That(p15.Error, Is.EqualTo(new ShopError(11, 4, 0)));
        Assert.That(ShopPacket.WriteError(p15.Error), Is.EqualTo(w15.Buffer.AsSpan(0, w15.Length).ToArray()));
    }

    [Test] public void OutboundShopPacketsMatchClientWriters() {
        Assert.That((ushort) Outbound.Shop, Is.EqualTo(0x28));

        // mode 3: RequestRepurchase (uishopdialog.cpp:2826)
        var rep = new ShopRepurchaseRequest(77);
        Assert.That(rep.Op, Is.EqualTo(Outbound.Shop));
        var r3 = new ByteReader(WriteOut(rep));
        Assert.That((r3.ReadByte(), r3.ReadInt(), r3.Available), Is.EqualTo(((byte) 3, 77, 0)));

        // mode 4: SendPurchase (uishopdialog.cpp:2846)
        var buy = new ShopBuyRequest(12, 5, 106);
        Assert.That(buy.Op, Is.EqualTo(Outbound.Shop));
        var r4 = new ByteReader(WriteOut(buy));
        Assert.That((r4.ReadByte(), r4.ReadInt(), r4.ReadInt(), r4.ReadInt(), r4.Available), Is.EqualTo(((byte) 4, 12, 5, 106, 0)));

        // mode 5: RequestSellItem (itemugcmesh.cpp:1097)
        var sell = new ShopSellRequest(0x1122334455667788L, 3);
        Assert.That(sell.Op, Is.EqualTo(Outbound.Shop));
        var r5 = new ByteReader(WriteOut(sell));
        Assert.That((r5.ReadByte(), r5.ReadLong(), r5.ReadInt(), r5.Available), Is.EqualTo(((byte) 5, 0x1122334455667788L, 3, 0)));

        // mode 9: ResetList (uishopdialog.cpp:2550)
        var reset = new ShopResetListRequest(1500);
        Assert.That(reset.Op, Is.EqualTo(Outbound.Shop));
        var r9 = new ByteReader(WriteOut(reset));
        Assert.That((r9.ReadByte(), r9.ReadInt(), r9.Available), Is.EqualTo(((byte) 9, 1500, 0)));

        // mode 10: list timeout / day rollover (uishopdialog.cpp:750, :762)
        var list = new ShopRequestListRequest();
        Assert.That(list.Op, Is.EqualTo(Outbound.Shop));
        Assert.That(WriteOut(list), Is.EqualTo(new byte[] { 10 }));

        // mode 13: FillNewProducts (uishopdialog.cpp:2270)
        var news = new ShopRequestNewItemsRequest();
        Assert.That(news.Op, Is.EqualTo(Outbound.Shop));
        Assert.That(WriteOut(news), Is.EqualTo(new byte[] { 13 }));
    }

    [Test] public void OtherCommandsCarryOnlyTheCommand() {
        var w = new ByteWriter(); w.WriteByte(12); w.WriteByte(3);
        var p = ShopPacket.Read(Reader(w));
        Assert.That((p.Command, p.Info, p.Products), Is.EqualTo((12, (ShopInfo) null, (IReadOnlyList<ShopProduct>) null)));
    }

    [Test] public void MyCharacterApplyStoresSummonedPetFromFieldAddUser() {
        var me = new MyCharacter();
        var pet = new EquipItem { Id = 60000001, Uid = 987654321L, Amount = 1 };
        me.Apply(new FieldAddUser { ObjectId = 1, User = new UserBlock { Name = "Hero" }, HasPet = true, Pet = pet });
        Assert.That(me.Pet, Is.SameAs(pet));
        me.Apply(new FieldAddUser { ObjectId = 1, User = new UserBlock { Name = "Hero" }, HasPet = false });
        Assert.That(me.Pet, Is.Null);
    }
}
