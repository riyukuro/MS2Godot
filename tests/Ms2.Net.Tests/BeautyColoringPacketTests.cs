using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The item colour dialog's reader of inbound 0x31 (UIItemColoringDialog::OnBeautyShopPacket @0x1419b00b0, uiitemcoloringdialog.cpp:937).</summary>
public class BeautyColoringPacketTests
{
    static BeautyColoringPacket Read(ByteWriter w) => BeautyColoringPacket.Read(new ByteReader(w.Buffer[..w.Length]));
    static void Payment(ByteWriter w, byte type, int amount) { w.WriteByte(type); w.WriteInt(0); w.WriteInt(amount); w.WriteShort(0); }

    [Test] public void Mode1ReadsTheKindAndTheShop() {
        var w = new ByteWriter(); w.WriteByte(1); w.WriteByte(3);
        w.WriteInt(11); w.WriteInt(5); w.WriteInt(5); w.WriteByte(1); w.WriteInt(6); w.WriteInt(7); w.WriteBool(false);
        Payment(w, 1, 900); Payment(w, 1, 90);
        var p = Read(w);
        Assert.That((p.Mode, p.Kind, p.Shop.ShopId, p.Shop.Category, p.Shop.RecolorPayment.Amount), Is.EqualTo((1, 3, 11, 5, 90L)));
    }

    [Test] public void Mode8ReadsTheResult() {
        var w = new ByteWriter(); w.WriteByte(8); w.WriteInt(2);
        var p = Read(w);
        Assert.That((p.Mode, p.Result), Is.EqualTo((8, 2)));
    }
}
