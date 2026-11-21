using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The pickup packets: the writers of CDropItem::FUN_141190900 (dropitem.cpp:950) and MyPC::FUN_141530db0 (mypc.cpp:4740), the reader of OnFieldPickupItem (scenestategame.cpp:2420).</summary>
public class DropPickupPacketTests {
    static byte[] Bytes(Ms2.Net.Wire.IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static ByteReader Reader(ByteWriter w) => new(w.Buffer.AsSpan(0, w.Length).ToArray());

    [Test] public void ItemPickupWithoutPickerIsIdAndAFalseFlag() {
        var p = new RequestItemPickup(0x01020304, 0);
        Assert.That(p.Op, Is.EqualTo(Outbound.RequestItemPickup));
        Assert.That(Bytes(p), Is.EqualTo(new byte[] { 4, 3, 2, 1, 0 }));
    }

    [Test] public void ItemPickupWithPickerAppendsThePickerId() {
        Assert.That(Bytes(new RequestItemPickup(1, 0x0A0B0C0D)), Is.EqualTo(new byte[] { 1, 0, 0, 0, 1, 0x0D, 0x0C, 0x0B, 0x0A }));
    }

    [Test] public void MesoPickupIsACountAndTheIds() {
        var p = new RequestMesoPickup(new[] { 5, 6 });
        Assert.That(p.Op, Is.EqualTo(Outbound.RequestMesoPickup));
        Assert.That(Bytes(p), Is.EqualTo(new byte[] { 2, 5, 0, 0, 0, 6, 0, 0, 0 }));
    }

    [Test] public void MesoPickupIsCappedAtTheClientsLimit() {
        var ids = new int[200];
        for (int i = 0; i < ids.Length; i++) ids[i] = i;
        var b = Bytes(new RequestMesoPickup(ids));
        Assert.That(b[0], Is.EqualTo(0x80));
        Assert.That(b.Length, Is.EqualTo(1 + 4 * 0x80), "ids past the cap are not sent");
    }

    [Test] public void APickupNamesThePickerAndReadsNothingElseForAnItem() {
        var w = new ByteWriter(); w.WriteBool(true); w.WriteInt(9); w.WriteInt(77); w.WriteInt(-999);
        var r = Reader(w);
        var d = FieldPickupItem.Read(r, _ => (true, false, false));
        Assert.That((d.Picked, d.DropKnown, d.ObjectId, d.PickerId, d.Meso, d.Ep), Is.EqualTo((true, true, 9, 77, 0L, 0u)));
        Assert.That(r.ReadInt(), Is.EqualTo(-999));
    }

    [Test] public void AMesoPickupCarriesTheAmount() {
        var w = new ByteWriter(); w.WriteBool(true); w.WriteInt(9); w.WriteInt(77); w.WriteLong(123456789012L);
        var d = FieldPickupItem.Read(Reader(w), _ => (true, true, false));
        Assert.That(d.Meso, Is.EqualTo(123456789012L));
    }

    [Test] public void AnEpBallCarriesItsGain() {
        var w = new ByteWriter(); w.WriteBool(true); w.WriteInt(9); w.WriteInt(77); w.WriteInt(42);
        var d = FieldPickupItem.Read(Reader(w), _ => (true, false, true));
        Assert.That(d.Ep, Is.EqualTo(42u));
    }

    [Test] public void ARefusedPickupCarriesAMode() {
        var w = new ByteWriter(); w.WriteBool(false); w.WriteInt(9); w.WriteInt(0x43);
        var d = FieldPickupItem.Read(Reader(w), _ => (true, false, false));
        Assert.That((d.Picked, d.Mode, d.PickerId), Is.EqualTo((false, 0x43, 0)));
    }

    [Test] public void AnUnknownDropIsNotReadFurther() {
        var w = new ByteWriter(); w.WriteBool(true); w.WriteInt(9); w.WriteInt(77); w.WriteInt(-999);
        var r = Reader(w);
        var d = FieldPickupItem.Read(r, _ => (false, false, false));
        Assert.That((d.DropKnown, d.PickerId), Is.EqualTo((false, 0)));
        Assert.That(r.ReadInt(), Is.EqualTo(77), "the rest of the packet is left unread");
    }
}
