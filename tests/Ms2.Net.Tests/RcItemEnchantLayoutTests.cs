using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>ItemEnchant: mode 6 passes; GMS2 mode 5 has 10-byte options and the Ophelia tail, modes 10/11 have the GMS2
/// item body and a 10-byte option list. Layouts: the GMS2 packet layout notes §B10.</summary>
public class RcItemEnchantLayoutTests {
    // gms2 recording: packet 1060, 13 bytes
    static readonly byte[] Mode6 = RcPkt.Hex(
        "067277D2C37F18B42B00000000");
    // gms2 recording: packet 992, 109 bytes
    static readonly byte[] Mode5Ophelia = RcPkt.Hex(
        "050100E7A010799697B32B020000000065000000440100000000000064000000080000000000020000001B0000000000",
        "6F12833C1C00000000006F12833C0000C842000000000000000000000000000000000000000000000000000000000000",
        "00000100000000000000000000");
    // gms2 recording: packet 1022, 84 bytes
    static readonly byte[] Mode5Peachy = RcPkt.Hex(
        "0502002A002A007F18B42B0300000000650000002203000000000000660000000A000000000000006400000064000000",
        "0000020000001B00000000000AD7A33C1C00000000000AD7A33C00000000000000000000");
    // gms2 recording: packet 1785, 121 bytes
    static readonly byte[] Mode5Ophelia2 = RcPkt.Hex(
        "050100E7A010799697B32B0300000000650000009B030000000000006600000003000000000000006400000017000000",
        "0000020000001B0000000000A69B443D1C0000000000A69B443D00004842000000000000000000000000000000000000",
        "00000000000000000000000000000100000000000000000000");
    // gms2 recording: packet 1023, 371 bytes
    static readonly byte[] Mode10 = RcPkt.Hex(
        "0AE7A010799697B32B0100000000000000FFFFFFFFF6E3045E0000000000000000000000000000000000000000000000",
        "000000000000000000000000000001000000000000000000000000000000000000FFFFFFFF0000000000030011001000",
        "0000000000001B0014050000000000001C00350600000000000000000000000001001C00B40000000000000000000000",
        "00000200010012000000000000001F00000000008FC2F53C000000000000000000000000000000000000000000000000",
        "000000000000000000000000000000000000000000000000000000000000010000000000000000000000000000000001",
        "000000000000000100000000021B000000000000006F12833C1C000000000000006F12833C0000000000000000000000",
        "0018000000010000000000000000000101420618F203132A2A06002A002A002A002A002A002A00000000000000000000",
        "0000000000000000000000020000001B00000000006F12833C1C00000000006F12833C");
    // gms2 recording: packet 1565, 367 bytes
    static readonly byte[] Mode11 = RcPkt.Hex(
        "0BE7A010799697B32B0100000000000000FFFFFFFFF6E3045E0000000000000000000000000000000000000000000000",
        "000000000000000000000000000001000000000000000000000000000000000000FFFFFFFF0000000000030011001000",
        "0000000000001B0014050000000000001C00350600000000000000000000000001001C00B40000000000000000000000",
        "00000200010012000000000000001F00000000008FC2F53C000000000000000000000000000000000000000000000000",
        "000000000000000000000000000000000000000000000000000000000000070000000000000000000000000000000008",
        "000000010000000101000000021B00000000000000BD74133E1C00000000000000BD74133E0000000000000000000000",
        "0018000000010000000000000000000101420618F203132A2A06002A002A002A002A002A002A00000000000000000000",
        "0000000000000000000000B9E0630200000000000000000000000001000000");

    [Test] public void Mode6ReadsTheChargeChanged() {
        var r = new ByteReader(Mode6);
        var m = ItemEnchantMessage.Read(r, _ => null) as ItemEnchantMessage.ChargeChanged;
        Assert.That(m, Is.Not.Null);
        Assert.That((m.Uid, m.Charge), Is.EqualTo((3149168976464082802L, 0)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void Gms2Mode5ReadsTheStageItem() {
        var a = ReadStage(Mode5Ophelia, exp: 1, uid: 3149027236994130151L, ingredients: 2, rate: 100f, failStack: 1);
        Assert.That(a);
        var b = ReadStage(Mode5Peachy, exp: 2, uid: 3149168973181485098L, ingredients: 3, rate: 100f, failStack: 0);
        Assert.That(b);
        var c = ReadStage(Mode5Ophelia2, exp: 1, uid: 3149027236994130151L, ingredients: 3, rate: 50f, failStack: 1);
        Assert.That(c);
    }

    static bool ReadStage(byte[] bytes, int exp, long uid, int ingredients, float rate, byte failStack) {
        var r = new ByteReader(bytes);
        var m = (ItemEnchantMessage) RcPkt.CallAny("ItemEnchantMessage", new[] { "Read" }, new[] {
            new object[] { r, (Func<long, EquipItem>) (_ => null), ProtocolProfile.Gms2 },
            new object[] { r, (Func<long, EquipItem>) (_ => null) },
        });
        var info = m as ItemEnchantMessage.Info;
        Assert.That(info, Is.Not.Null);
        Assert.That((info.ExpMode, info.Uid), Is.EqualTo(((short) exp, uid)));
        Assert.That(info.Ingredients.Count, Is.EqualTo(ingredients));
        Assert.That(info.Ingredients[0], Is.EqualTo(new EnchantIngredient(0, 101, exp == 1 ? (ingredients == 2 ? 324 : 923) : 802)));
        Assert.That(info.Options[27], Is.EqualTo(0));
        Assert.That(info.Options[28], Is.EqualTo(0));
        Assert.That(info.Rates.Base, Is.EqualTo(rate));
        Assert.That(info.FailStackMode, Is.EqualTo(failStack));
        Assert.That(r.Available, Is.EqualTo(0), "the 10-byte options and the Ophelia tail must be read");
        return true;
    }

    [Test] public void Gms2Mode10ReadsTheItemAndOptions() {
        var r = new ByteReader(Mode10);
        var m = (ItemEnchantMessage) RcPkt.CallAny("ItemEnchantMessage", new[] { "Read" }, new[] {
            new object[] { r, (Func<long, EquipItem>) (_ => new EquipItem { Slot = EquipSlot.Unknown }), ProtocolProfile.Gms2 },
            new object[] { r, (Func<long, EquipItem>) (_ => new EquipItem { Slot = EquipSlot.Unknown }) },
        });
        var ok = m as ItemEnchantMessage.Success;
        Assert.That(ok, Is.Not.Null);
        Assert.That(ok.Uid, Is.EqualTo(3149027236994130151L));
        Assert.That(ok.Item, Is.Not.Null);
        Assert.That(ok.Options.Count, Is.EqualTo(2));
        Assert.That(ok.Options[27], Is.EqualTo(0));
        Assert.That(r.Available, Is.EqualTo(0), "the GMS2 item body and its 10-byte options must be read");
    }

    [Test] public void Gms2Mode11ReadsTheFailureTail() {
        var r = new ByteReader(Mode11);
        var m = (ItemEnchantMessage) RcPkt.CallAny("ItemEnchantMessage", new[] { "Read" }, new[] {
            new object[] { r, (Func<long, EquipItem>) (_ => new EquipItem { Slot = EquipSlot.Unknown }), ProtocolProfile.Gms2 },
            new object[] { r, (Func<long, EquipItem>) (_ => new EquipItem { Slot = EquipSlot.Unknown }) },
        });
        var fail = m as ItemEnchantMessage.Failure;
        Assert.That(fail, Is.Not.Null);
        Assert.That(fail.Uid, Is.EqualTo(3149027236994130151L));
        Assert.That((fail.A, fail.B, fail.C, fail.FailType), Is.EqualTo((40100025, 0, 0L, 1)));
        Assert.That(r.Available, Is.EqualTo(0), "the GMS2 item body and the four-value tail must be read");
    }
}
