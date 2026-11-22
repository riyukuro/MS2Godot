using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The style dialog's own reader of inbound 0x31 (UIBeautyShopStyleDialog::FUN_140f05a80, uibeautyshopstyledialog.cpp:1205).</summary>
public class BeautyStylePacketTests
{
    static BeautyStylePacket Read(ByteWriter w) => BeautyStylePacket.Read(new ByteReader(w.Buffer[..w.Length]));
    static void Payment(ByteWriter w, byte type, int amount) { w.WriteByte(type); w.WriteInt(0); w.WriteInt(amount); w.WriteShort(0); }

    [Test] public void Mode2ReadsTheShopModeAndTheShop() {
        var w = new ByteWriter(); w.WriteByte(2); w.WriteByte(4);
        w.WriteInt(11); w.WriteInt(3); w.WriteInt(5); w.WriteByte(1); w.WriteInt(6); w.WriteInt(7); w.WriteBool(false);
        Payment(w, 1, 900); Payment(w, 1, 90);
        var p = Read(w);
        Assert.That((p.Mode, p.ShopMode, p.Shop.ShopId, p.Shop.Category, p.Shop.Payment.Amount, p.Shop.RecolorPayment.Amount), Is.EqualTo((2, 4, 11, 3, 900L, 90L)));
    }

    [Test] public void CountAndExtendModesReadTheirFields() {
        var w = new ByteWriter(); w.WriteByte(0xe); w.WriteShort(5);
        Assert.That(Read(w).ExpectedStyles, Is.EqualTo(5));
        w = new ByteWriter(); w.WriteByte(0x14); w.WriteByte(2); w.WriteShort(9);
        var e = Read(w);
        Assert.That((e.ExtendType, e.ExtendedSlots), Is.EqualTo((2, 9)));
        w = new ByteWriter(); w.WriteByte(0xd);
        Assert.That(Read(w).Mode, Is.EqualTo(0xd));
        w = new ByteWriter(); w.WriteByte(0x15);
        Assert.That(Read(w).Mode, Is.EqualTo(0x15));
    }

    [Test] public void Mode0xfReadsEachSavedStyleWithItsBody() {
        var w = new ByteWriter(); w.WriteByte(0xf); w.WriteShort(2);
        for (int i = 0; i < 2; i++) {
            w.WriteInt(10200000 + i); w.WriteLong(100 + i); w.WriteInt(i); w.WriteLong(5000 + i);
            ItemReader.WriteEmptyBody(w, 1);
        }
        var p = Read(w);
        Assert.That(p.Styles.Length, Is.EqualTo(2));
        Assert.That((p.Styles[1].StyleSlot, p.Styles[1].Item.Id, p.Styles[1].Item.Uid, p.Styles[1].Item.CreationTime), Is.EqualTo((1, 10200001, 101L, 5001L)));
    }

    [Test] public void SaveRemoveAndResultModesReadTheirFields() {
        var w = new ByteWriter(); w.WriteByte(0x10); w.WriteLong(7); w.WriteLong(8); w.WriteByte(3); w.WriteLong(99);
        var s = Read(w);
        Assert.That((s.Uid, s.NewUid, s.Slot, s.Time), Is.EqualTo((7L, 8L, 3, 99L)));
        w = new ByteWriter(); w.WriteByte(0x12); w.WriteLong(7);
        Assert.That(Read(w).Uid, Is.EqualTo(7L));
        w = new ByteWriter(); w.WriteByte(8); w.WriteInt(0x18);
        Assert.That(Read(w).Result, Is.EqualTo(0x18));
    }
}
