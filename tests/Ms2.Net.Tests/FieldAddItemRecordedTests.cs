using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>FieldAddItem (0x2B) meso drops: item id 90000001 is one of the three meso ids, so no item record follows and the
/// drop is a meso drop. The GMS2 body uses the receiver bool and the GMS2 reader's owner-type mapping; the KMS2 body carries
/// the i64 owner uid unconditionally.</summary>
public class FieldAddItemRecordedTests {
    // gms2 recording: packet 2917, 50 bytes (names masked with '*' by the masking pass; account/character id bytes replaced with 0x11).
    static readonly byte[] Gms2 = new byte[] {
        0xC6, 0x4C, 0x26, 0x00, 0x81, 0x4A, 0x5D, 0x05, 0x1C, 0x00, 0x00, 0x00, 0x01, 0x11, 0x11, 0x11,
        0x11, 0x11, 0x11, 0x11, 0x11, 0x00, 0x20, 0x7D, 0x45, 0x00, 0x80, 0x3B, 0xC4, 0x00, 0x40, 0x4E,
        0x45, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x21, 0x00,
        0x00, 0x00,
    };
    // kms2 recording: packet 7710, 45 bytes (names masked with '*' by the masking pass; account/character id bytes replaced with 0x11).
    static readonly byte[] Kms = new byte[] {
        0x6A, 0xD6, 0x36, 0x00, 0x81, 0x4A, 0x5D, 0x05, 0x5C, 0x00, 0x00, 0x00, 0x11, 0x11, 0x11, 0x11,
        0x11, 0x11, 0x11, 0x11, 0x00, 0xD0, 0x3A, 0xC5, 0x00, 0x10, 0x34, 0xC5, 0x00, 0xE0, 0x95, 0x44,
        0x4C, 0x53, 0x36, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x15, 0x00, 0x00, 0x00,
    };

    [Test] public void Gms2MesoDropReadsTheWholeBody() {
        var r = new ByteReader(Gms2);
        var d = FieldAddItem.Read(r, ProtocolProfile.Gms2);
        Assert.That((d.ObjectId, d.ItemId, d.Amount), Is.EqualTo((2510022, 90000001, 28)));
        Assert.That(d.OwnerUid, Is.EqualTo(0x1111111111111111L), "the recorded owner id is masked");
        Assert.That((d.Position.X, d.Position.Y, d.Position.Z), Is.EqualTo((4050f, -750f, 3300f)));
        Assert.That((d.SourceObjectId, d.OwnerType, d.Rarity, d.DropKind), Is.EqualTo((0, (byte) 0, 1, (ushort) 33)));
        Assert.That((d.FixedThrow, d.PetTrap), Is.EqualTo((false, false)));
        Assert.That(d.Item, Is.Null);
        Assert.That(d.IsMeso, Is.True);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsMesoDropReadsTheWholeBody() {
        var r = new ByteReader(Kms);
        var d = FieldAddItem.Read(r);
        Assert.That((d.ObjectId, d.ItemId, d.Amount), Is.EqualTo((3593834, 90000001, 92)));
        Assert.That(d.OwnerUid, Is.EqualTo(0x1111111111111111L), "the recorded owner id is masked");
        Assert.That((d.Position.X, d.Position.Y, d.Position.Z), Is.EqualTo((-2989f, -2881f, 1199f)));
        Assert.That((d.SourceObjectId, d.OwnerType, d.Rarity, d.DropKind), Is.EqualTo((3560268, (byte) 0, 1, (ushort) 21)));
        Assert.That(d.Item, Is.Null);
        Assert.That(d.IsMeso, Is.True);
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
