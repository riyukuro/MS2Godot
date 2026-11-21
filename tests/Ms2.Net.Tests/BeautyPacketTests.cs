using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Beauty shop packets (OnBeautyShopPacket, the SendOp::Beauty senders).</summary>
public class BeautyPacketTests
{
    static void Payment(ByteWriter w, byte type, int item, int amount, string tag) {
        w.WriteByte(type); w.WriteInt(item); w.WriteInt(amount); w.WriteShort((short) tag.Length); foreach (char c in tag) w.WriteByte((byte) c);
    }

    [Test] public void ShopModeReadsTheShopAndItsRows() {
        var w = new ByteWriter();
        w.WriteByte(0); w.WriteByte(2);
        w.WriteInt(11); w.WriteInt(3); w.WriteInt(20300001); w.WriteByte(1); w.WriteInt(20300002); w.WriteInt(5); w.WriteBool(true);
        Payment(w, 1, 0, 1000, "mesos"); Payment(w, 4, 0, 0, "");
        w.WriteByte(7); w.WriteByte(3); w.WriteLong(99); w.WriteShort(2);
        for (int i = 0; i < 2; i++) { w.WriteInt(10200000 + i); w.WriteByte(1); w.WriteShort(10); w.WriteInt(0); w.WriteByte(0); Payment(w, 1, 0, 500 + i, "x"); }
        var p = BeautyShopPacket.Read(new ByteReader(w.Buffer[..w.Length]));
        Assert.That((p.Mode, p.Kind, p.SlotCount, p.SavedStyles, p.Context), Is.EqualTo((0, 2, 7, 3, 99L)));
        Assert.That((p.Shop.ShopId, p.Shop.Category, p.Shop.DisplayCouponId, p.Shop.Flag24, p.Shop.ReturnCouponId, p.Shop.Value2C, p.Shop.Flag30),
            Is.EqualTo((11, 3, 20300001, true, 20300002, 5, true)));
        Assert.That((p.Shop.Payment.Amount, p.Shop.Payment.IconTag, p.Shop.RecolorPayment.Type), Is.EqualTo((1000L, "mesos", 4)));
        Assert.That(p.Items.Length, Is.EqualTo(2));
        Assert.That((p.Items[1].ItemId, p.Items[1].SaleTag, p.Items[1].RequireLevel, p.Items[1].Payment.Amount), Is.EqualTo((10200001, 1, 10, 501L)));
    }

    [Test] public void SmallModesReadTheirFields() {
        var w = new ByteWriter(); w.WriteByte(8); w.WriteInt(0x13);
        Assert.That(BeautyShopPacket.Read(new ByteReader(w.Buffer[..w.Length])).Result, Is.EqualTo(BeautyResult.SlotMax));
        w = new ByteWriter(); w.WriteByte(9); w.WriteInt(5); w.WriteInt(2);
        Assert.That(BeautyShopPacket.Read(new ByteReader(w.Buffer[..w.Length])).ShowsItemNotice, Is.True);
        w = new ByteWriter(); w.WriteByte(9); w.WriteInt(5); w.WriteInt(0);
        Assert.That(BeautyShopPacket.Read(new ByteReader(w.Buffer[..w.Length])).ShowsItemNotice, Is.False);
        w = new ByteWriter(); w.WriteByte(0xb); w.WriteInt(1); w.WriteInt(2);
        var b = BeautyShopPacket.Read(new ByteReader(w.Buffer[..w.Length]));
        Assert.That((b.OldItemId, b.NewItemId), Is.EqualTo((1, 2)));
        w = new ByteWriter(); w.WriteByte(0xc); w.WriteInt(7); w.WriteByte(1);
        var c = BeautyShopPacket.Read(new ByteReader(w.Buffer[..w.Length]));
        Assert.That((c.CouponId, c.Mailed), Is.EqualTo((7, true)));
        Assert.That(BeautyShopPacket.Read(new ByteReader(new byte[] { 0x10 })).Mode, Is.EqualTo(0x10));
    }

    static byte[] Bytes(BeautyRequest r) { var w = new ByteWriter(); r.Write(w); return w.Buffer[..w.Length]; }

    [Test] public void RequestsWriteTheirModeAndBody() {
        Assert.That(Bytes(new BeautyEnterRequest(3, 2)), Is.EqualTo(new byte[] { 0, 3, 0, 0, 0, 2 }));
        Assert.That(Bytes(new BeautyBuyRequest(9, true, 1)), Is.EqualTo(new byte[] { 7, 9, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.That(Bytes(new BeautyListRequest(3)), Is.EqualTo(new byte[] { 10, 3, 0 }));
        Assert.That(Bytes(new BeautySlotRequest(4)), Is.EqualTo(new byte[] { 0xc, 4 }));
        Assert.That(Bytes(new BeautyColorsRequest(1, 2, 3, 0)), Is.EqualTo(new byte[] { 6, 1, 2, 0, 0, 0, 3, 0, 0, 0, 0 }));
        Assert.That(Bytes(new BeautyRemovedRequest(5)), Is.EqualTo(new byte[] { 4, 5, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.That(Bytes(new BeautySaveStyleRequest(1, false))[0], Is.EqualTo(0x10));
        Assert.That(Bytes(new BeautySaveStyleRequest(1, true))[0], Is.EqualTo(0x11));
        Assert.That(Bytes(new BeautyDeleteStyleRequest(1, 6)), Is.EqualTo(new byte[] { 0x12, 1, 0, 0, 0, 0, 0, 0, 0, 6 }));
        Assert.That(Bytes(new BeautyExtendStylesRequest()), Is.EqualTo(new byte[] { 0x13 }));
        Assert.That(Bytes(new BeautyApplyStyleRequest(1, 2)), Is.EqualTo(new byte[] { 0x15, 1, 0, 0, 0, 0, 0, 0, 0, 2 }));
    }

    [Test] public void AddedAndModifiedAppendTheItemBody() {
        var a = Bytes(new BeautyAddedRequest(1, 0, 7, 8, w => w.WriteByte(0xAA)));
        Assert.That(a, Is.EqualTo(new byte[] { 3, 1, 0, 7, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0xAA }));
        var m = Bytes(new BeautyModifiedRequest(1, 1, 2, w => w.WriteByte(0xBB)));
        Assert.That(m, Is.EqualTo(new byte[] { 5, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0, 0xBB }));
    }

    [Test] public void ColoringWritesCountThenPresenceAndItem() {
        var b = Bytes(new BeautyColoringRequest(new[] { new BeautyColorItem(true, false, 9, 4, w => w.WriteByte(0xCC)) }));
        Assert.That(b, Is.EqualTo(new byte[] { 0x16, 1, 1, 1, 0, 0, 0,0,0,0, 0,0,0,0,0,0,0,0, 9,0,0,0,0,0,0,0, 4,0,0,0, 0xCC }));
    }
}
