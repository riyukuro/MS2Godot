using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>FieldAddItem / FieldRemoveItem as CSceneStateGame::OnFieldAddItem (scenestategame.cpp:9576) and OnFieldRemoveItem (:2402) read them.</summary>
public class DropPacketTests {
    static ByteWriter Head(int obj, int item, int amount, byte ownerType) {
        var w = new ByteWriter();
        w.WriteInt(obj); w.WriteInt(item); w.WriteInt(amount); w.WriteLong(0x1122334455L);
        w.WriteFloat(10f); w.WriteFloat(-20f); w.WriteFloat(30f);
        w.WriteInt(777); w.WriteByte(ownerType); w.WriteInt(3); w.WriteShort(5); w.WriteBool(true); w.WriteBool(false);
        return w;
    }

    static ByteReader Reader(ByteWriter w) => new(w.Buffer.AsSpan(0, w.Length).ToArray());

    [Test] public void AnItemDropCarriesItsRecord() {
        var w = Head(41, 20000001, 4, 2);
        ItemReader.WriteEmptyBody(w, 4);
        w.WriteInt(-999);
        var r = Reader(w);
        var d = FieldAddItem.Read(r);
        Assert.That((d.ObjectId, d.ItemId, d.Amount, d.OwnerUid, d.SourceObjectId, d.OwnerType, d.Rarity, d.DropKind, d.FixedThrow, d.PetTrap),
            Is.EqualTo((41, 20000001, 4, 0x1122334455L, 777, (byte) 2, 3, (short) 5, true, false)));
        Assert.That((d.Position.X, d.Position.Y, d.Position.Z), Is.EqualTo((10f, -20f, 30f)));
        Assert.That(d.IsMeso, Is.False);
        Assert.That(d.Item, Is.Not.Null);
        Assert.That(d.Item.Amount, Is.EqualTo(4));
        Assert.That(r.ReadInt(), Is.EqualTo(-999), "the record is read whole: the next field follows it");
    }

    [Test] public void AMesoDropHasNoRecord() {
        foreach (int id in new[] { 90000001, 90000002, 90000003 }) {
            var w = Head(9, id, 1234, 2);
            w.WriteInt(-999);
            var r = Reader(w);
            var d = FieldAddItem.Read(r);
            Assert.That(d.IsMeso, Is.True, id.ToString());
            Assert.That(d.Item, Is.Null);
            Assert.That(d.Amount, Is.EqualTo(1234), "the amount is the meso value");
            Assert.That(r.ReadInt(), Is.EqualTo(-999), "nothing after the flags is read for a meso");
        }
    }

    [Test] public void OnlyTheThreeMesoIdsAreMeso() {
        Assert.That(FieldAddItem.IsMesoId(90000000), Is.False);
        Assert.That(FieldAddItem.IsMesoId(90000004), Is.False);
        Assert.That(FieldAddItem.IsMesoId(0), Is.False);
    }

    [Test] public void OnlyTheOwnerListOrAnyoneMayPickUp() {
        var d = new FieldAddItem { OwnerUid = 7 };
        d.OwnerType = 2; Assert.That(d.IsPickupAllowed(99), Is.True, "type 2 is anyone's");
        d.OwnerType = 0; Assert.That((d.IsPickupAllowed(7), d.IsPickupAllowed(8)), Is.EqualTo((true, false)));
        d.OwnerType = 1; Assert.That((d.IsPickupAllowed(7), d.IsPickupAllowed(8)), Is.EqualTo((true, false)));
        d.OwnerType = 3; Assert.That(d.IsPickupAllowed(7), Is.False, "an unknown type is nobody's");
    }

    [Test] public void ATruncatedDropIsRefused() {
        var w = Head(41, 90000001, 5, 2);
        var bytes = w.Buffer.AsSpan(0, w.Length - 1).ToArray();
        Assert.Catch<Exception>(() => FieldAddItem.Read(new ByteReader(bytes)), "a missing last flag byte is a short packet");
    }

    [Test] public void ARemoveNamesTheObject() {
        var w = new ByteWriter(); w.WriteInt(41);
        Assert.That(FieldRemoveItem.Read(Reader(w)).ObjectId, Is.EqualTo(41));
        Assert.Catch<Exception>(() => FieldRemoveItem.Read(new ByteReader(new byte[3])));
    }

    [Test] public void TheOpcodesAreTheClients() {
        Assert.That((int) Inbound.FieldAddItem, Is.EqualTo(0x2B));
        Assert.That((int) Inbound.FieldRemoveItem, Is.EqualTo(0x2C));
        Assert.That((int) Inbound.FieldPickupItem, Is.EqualTo(0x2D));
    }
}
