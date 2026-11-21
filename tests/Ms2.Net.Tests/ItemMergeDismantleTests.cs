using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Layouts from OnItemFusion (scenestategame.cpp:5906), UIItemMergeOptionDialog (:799-1020),
/// CItemMergeOptionProxy::Read (itemmergeoptiontable.cpp:245) and CItemBreakManager (itembreakmanager.cpp:321-451).</summary>
public class ItemMergeDismantleTests {
    static ByteReader Reader(ByteWriter w) => new(w.Buffer.AsSpan(0, w.Length).ToArray());
    static byte[] WriteOut(IOutPacket p) {
        var w = new ByteWriter();
        p.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    [Test] public void OpcodesMatchTheClientTable() {
        Assert.That(((ushort)Inbound.ItemMerge, (ushort)Inbound.ItemDismantle, (ushort)Outbound.ItemMerge, (ushort)Outbound.ItemDismantle),
            Is.EqualTo(((ushort)0x74, (ushort)0x96, (ushort)0x3F, (ushort)0x5A)));
    }

    [Test] public void MergeInfoModeReadsSubModes() {
        var w = new ByteWriter(); w.WriteByte(0); w.WriteByte(0);
        var r = Reader(w); var p = ItemMergePacket.Read(r);
        Assert.That((p.Mode, p.Sub, r.Available), Is.EqualTo(((byte)0, (byte)0, 0)));
        w = new ByteWriter(); w.WriteByte(0); w.WriteByte(2); w.WriteInt(11000001);
        r = Reader(w); p = ItemMergePacket.Read(r);
        Assert.That((p.Sub, p.ItemId, r.Available), Is.EqualTo(((byte)2, 11000001, 0)));
        w = new ByteWriter(); w.WriteByte(0); w.WriteByte(1); w.WriteInt(5); w.WriteLong(777); w.WriteInt(1); w.WriteInt(2); w.WriteInt(3);
        r = Reader(w); p = ItemMergePacket.Read(r);
        Assert.That((p.ItemId, p.Uid, p.Normal, p.Special, p.Value, r.Available), Is.EqualTo((5, 777L, 1, 2, 3, 0)));
    }

    [Test] public void MergeMaterialListAndChoiceModes() {
        var w = new ByteWriter(); w.WriteByte(1); w.WriteInt(2); w.WriteLong(10); w.WriteLong(11);
        var r = Reader(w); var p = ItemMergePacket.Read(r);
        Assert.That((p.Materials, r.Available), Is.EqualTo((new long[] { 10, 11 }, 0)));
        w = new ByteWriter(); w.WriteByte(4); w.WriteInt(7); w.WriteInt(8); w.WriteInt(9);
        r = Reader(w); p = ItemMergePacket.Read(r);
        Assert.That((p.Normal, p.Special, p.Value, r.Available), Is.EqualTo((7, 8, 9, 0)));
    }

    [Test] public void MergeOptionProxyReadsInTheClientsOrder() {
        var w = new ByteWriter(); w.WriteByte(2);
        w.WriteLong(5000);
        w.WriteByte(2); w.WriteInt(1); w.WriteInt(2); w.WriteInt(3); w.WriteInt(4); w.WriteInt(5); w.WriteInt(6);
        w.WriteInt(2); w.WriteInt(100); w.WriteInt(200);
        w.WriteInt(1); w.WriteShort(9); w.WriteInt(10); w.WriteInt(20);
        w.WriteInt(1); w.WriteShort(8); w.WriteInt(30); w.WriteInt(40);
        var r = Reader(w); var p = ItemMergePacket.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        var x = p.Proxy;
        Assert.That((x.Meso, x.Ingredients.Length, x.Ingredients[1], x.Values), Is.EqualTo((5000L, 2, new MergeIngredient(4, 5, 6), new[] { 100, 200 })));
        Assert.That((x.Normal[0], x.Special[0]), Is.EqualTo((new MergeOptionRange(9, 10, 20), new MergeOptionRange(8, 30, 40))));
    }

    [Test] public void MergeResultModesReadTheErrorAndUid() {
        var w = new ByteWriter(); w.WriteByte(5); w.WriteShort(0); w.WriteLong(99);
        var r = Reader(w); var p = ItemMergePacket.Read(r);
        Assert.That((p.Error, p.Uid), Is.EqualTo(((ushort)0, 99L)));
        w = new ByteWriter(); w.WriteByte(5); w.WriteShort(3);
        r = Reader(w); p = ItemMergePacket.Read(r);
        Assert.That((p.Error, r.Available), Is.EqualTo(((ushort)3, 0)));
        w = new ByteWriter(); w.WriteByte(6); w.WriteLong(42);
        p = ItemMergePacket.Read(Reader(w));
        Assert.That(p.Uid, Is.EqualTo(42L));
    }

    [Test] public void MergeRequestsEncodeLikeTheDialogs() {
        Assert.That(WriteOut(ItemMergeRequest.Info()), Is.EqualTo(new byte[] { 0 }));
        var w = new ByteWriter(); w.WriteByte(1); w.WriteLong(5);
        Assert.That(WriteOut(ItemMergeRequest.Selection(5)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        foreach (var (req, mode) in new[] { (ItemMergeRequest.Preview(5, 6), (byte)2), (ItemMergeRequest.Merge(5, 6), (byte)3),
                     (ItemMergeRequest.Revert(5, 6), (byte)6), (ItemMergeRequest.Material(5, 6), (byte)7) }) {
            w = new ByteWriter(); w.WriteByte(mode); w.WriteLong(5); w.WriteLong(6);
            Assert.That(WriteOut(req), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()), $"mode {mode}");
            Assert.That(req.Op, Is.EqualTo(Outbound.ItemMerge));
        }
        Assert.That(WriteOut(ItemMergeRequest.ChoiceOf(2)), Is.EqualTo(new byte[] { 4, 2 }));
    }

    [Test] public void DismantleSlotModesRead() {
        var w = new ByteWriter(); w.WriteByte(1); w.WriteLong(900); w.WriteShort(4); w.WriteInt(3);
        var r = Reader(w); var p = ItemDismantlePacket.Read(r);
        Assert.That((p.Slots[0], r.Available), Is.EqualTo((new DismantleSlot(4, 900, 3), 0)));
        w = new ByteWriter(); w.WriteByte(2); w.WriteLong(900);
        Assert.That(ItemDismantlePacket.Read(Reader(w)).Uid, Is.EqualTo(900L));
        w = new ByteWriter(); w.WriteByte(3); w.WriteByte(2);
        Assert.That(ItemDismantlePacket.Read(Reader(w)).Type, Is.EqualTo((byte)2));
        w = new ByteWriter(); w.WriteByte(4); w.WriteInt(1);
        Assert.That(ItemDismantlePacket.Read(Reader(w)).Result, Is.EqualTo(1));
    }

    [Test] public void DismantlePreviewAndListModesRead() {
        var w = new ByteWriter(); w.WriteByte(5); w.WriteInt(2);
        w.WriteInt(20000001); w.WriteInt(1); w.WriteInt(3); w.WriteInt(20000002); w.WriteInt(2); w.WriteInt(4);
        var r = Reader(w); var p = ItemDismantlePacket.Read(r);
        Assert.That((p.Previews[1], r.Available), Is.EqualTo((new DismantlePreview(20000002, 2, 4), 0)));
        w = new ByteWriter(); w.WriteByte(6); w.WriteInt(2);
        w.WriteShort(0); w.WriteLong(11); w.WriteInt(1); w.WriteShort(5); w.WriteLong(12); w.WriteInt(9);
        r = Reader(w); p = ItemDismantlePacket.Read(r);
        Assert.That((p.Slots[1], r.Available), Is.EqualTo((new DismantleSlot(5, 12, 9), 0)));
    }

    [Test] public void DismantleRequestsEncodeLikeTheManager() {
        Assert.That(WriteOut(ItemDismantleRequest.SetMode(2)), Is.EqualTo(new byte[] { 0, 2 }));
        var w = new ByteWriter(); w.WriteByte(1); w.WriteInt(7); w.WriteLong(900); w.WriteInt(3);
        Assert.That(WriteOut(ItemDismantleRequest.AddItem(900, 3, 7)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        w = new ByteWriter(); w.WriteByte(2); w.WriteLong(900);
        Assert.That(WriteOut(ItemDismantleRequest.RemoveItem(900)), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(WriteOut(ItemDismantleRequest.Start()), Is.EqualTo(new byte[] { 3 }));
        Assert.That(WriteOut(ItemDismantleRequest.Options(1, 0)), Is.EqualTo(new byte[] { 6, 1, 0 }));
        Assert.That(ItemDismantleRequest.Start().Op, Is.EqualTo(Outbound.ItemDismantle));
    }
}
