using System;
using System.Text.Json.Nodes;
using Maple2.PacketLib.Tools;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Shop;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and handler-effect tests for Shop (KMS 0x51, GMS2 0x52;
/// UIShopDialog::OnShopPacket @0x141af8710, uishopdialog.cpp:1524 -> ShopController.OnShopPacket).
/// </summary>
public class ShopRecordedTests
{
    // kms2: 0x51 cmd 0, 45 bytes (ShopInfo: npc 11004040, shop 230, 10 products, tabShop 22, icon "shopetc")
    static readonly byte[] KmsCmd0Info = Convert.FromHexString(
        "0088E8A700E60000009160E85F00000000000000000A0016000000000000000001000000070073686F70657463");

    // kms2: 0x51 cmd 2, 9 bytes (ProductUpdate: serial 7647824, bought 10)
    static readonly byte[] KmsCmd2Update = Convert.FromHexString("0250B274000A000000");

    // kms2: 0x51 cmd 4, 15 bytes (PurchaseResult: item 32000001, count 1, price 5000, rank 1, guildStorage false)
    static readonly byte[] KmsCmd4Purchase = Convert.FromHexString("040148E80101000000881300000100");

    // gms2: 0x52 cmd 0, 42 bytes (ShopInfo: npc 11000029, shop 105, 15 products, tabShop 3, icon "shop")
    static readonly byte[] Gms2Cmd0Info = Convert.FromHexString(
        "00DDD8A70069000000EA5FA25E00000000000000000F0003000000000000000001000100040073686F70");

    // gms2: 0x52 cmd 2, 9 bytes (ProductUpdate: serial 51730552, bought 5)
    static readonly byte[] Gms2Cmd2Update = Convert.FromHexString("027858150305000000");

    // gms2: 0x52 cmd 4, 15 bytes (PurchaseResult: item 50200868, count 1, price 1, rank 1, guildStorage false)
    static readonly byte[] Gms2Cmd4Purchase = Convert.FromHexString("042401FE0201000000010000000100");

    // gms2: 0x52 cmd 8, 5 bytes (RepurchaseRemoval: serial 1)
    static readonly byte[] Gms2Cmd8Remove = Convert.FromHexString("0801000000");

    static ShopPacket DecodeAll(byte[] payload, ProtocolProfile profile)
    {
        var r = new ByteReader(payload);
        var p = ShopPacket.Read(r, profile);
        Assert.That(r.Available, Is.EqualTo(0));
        return p;
    }

    [Test]
    public void KmsShopCommandsDecodeAndPopulateShopControllerInfo()
    {
        var p0 = DecodeAll(KmsCmd0Info, ProtocolProfile.Kms2525);
        Assert.That(p0.Command, Is.EqualTo(0));

        var ctrl = new ShopController();
        ctrl.OnShopPacket(p0);

        Assert.That(ctrl.Info.NpcId, Is.EqualTo(11_004_040));
        Assert.That(ctrl.Info.ShopId, Is.EqualTo(230));
        Assert.That(ctrl.Info.ResetTime, Is.EqualTo(1_609_064_593L));
        Assert.That(ctrl.Info.ProductCount, Is.EqualTo(10));
        Assert.That(ctrl.Info.TabShopId, Is.EqualTo(22));
        Assert.That(ctrl.Info.CheckEnableItems, Is.True);
        Assert.That(ctrl.Info.ProbInfo, Is.False);
        Assert.That(ctrl.Info.Icon, Is.EqualTo("shopetc"));
        Assert.That(ctrl.CheckEnableItems, Is.True);

        var p2 = DecodeAll(KmsCmd2Update, ProtocolProfile.Kms2525);
        Assert.That(p2.Command, Is.EqualTo(2));
        Assert.That(p2.ProductUpdate, Is.EqualTo(new ShopProductUpdate(7_647_824, 10)));

        var p4 = DecodeAll(KmsCmd4Purchase, ProtocolProfile.Kms2525);
        Assert.That(p4.Command, Is.EqualTo(4));
        Assert.That(p4.PurchaseResult, Is.EqualTo(new ShopPurchaseResult(32_000_001, 1, 5000u, 1, false)));
    }

    [Test]
    public void Gms2ShopCommandsDecodeAndPopulateShopControllerInfo()
    {
        var p0 = DecodeAll(Gms2Cmd0Info, ProtocolProfile.Gms2);
        Assert.That(p0.Command, Is.EqualTo(0));

        var ctrl = new ShopController();
        ctrl.OnShopPacket(p0);

        Assert.That(ctrl.Info.NpcId, Is.EqualTo(11_000_029));
        Assert.That(ctrl.Info.ShopId, Is.EqualTo(105));
        Assert.That(ctrl.Info.ResetTime, Is.EqualTo(1_587_699_690L));
        Assert.That(ctrl.Info.ProductCount, Is.EqualTo(15));
        Assert.That(ctrl.Info.TabShopId, Is.EqualTo(3));
        Assert.That(ctrl.Info.CheckEnableItems, Is.True);
        Assert.That(ctrl.Info.ProbInfo, Is.True);
        Assert.That(ctrl.Info.Icon, Is.EqualTo("shop"));
        Assert.That(ctrl.CheckEnableItems, Is.True);

        var p2 = DecodeAll(Gms2Cmd2Update, ProtocolProfile.Gms2);
        Assert.That(p2.Command, Is.EqualTo(2));
        Assert.That(p2.ProductUpdate, Is.EqualTo(new ShopProductUpdate(51_730_552, 5)));

        var p4 = DecodeAll(Gms2Cmd4Purchase, ProtocolProfile.Gms2);
        Assert.That(p4.Command, Is.EqualTo(4));
        Assert.That(p4.PurchaseResult, Is.EqualTo(new ShopPurchaseResult(50_200_868, 1, 1u, 1, false)));

        var p8 = DecodeAll(Gms2Cmd8Remove, ProtocolProfile.Gms2);
        Assert.That(p8.Command, Is.EqualTo(8));
        Assert.That(p8.RemoveSerial, Is.EqualTo(1));

        // Seed a repurchase item with serial 1 so cmd 8 proves removal from group 1.
        _ = new UiHost(new Player(new Runtime(), new JsonObject { ["movie"] = "shop" }), ctrl, new UiModel(null));
        ctrl.ReadRepurchaseStart(1);
        ctrl.ReadRepurchaseAdd(new[]
        {
            new ShopProduct { Serial = 1, ItemId = 50_200_868, Rarity = 1, Price = 100, Count = 1 },
        });
        Assert.That(ctrl.Products(1).ContainsKey(1), Is.True);

        ctrl.OnShopPacket(p8);
        Assert.That(ctrl.Products(1).ContainsKey(1), Is.False);
        Assert.That(ctrl.Products(1), Is.Empty);
        Assert.That(ctrl.ShopEnabled, Is.True);
    }
}
