using Maple2.PacketLib.Tools;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// FieldRemoveItem (0x2C on KMS2 and GMS2): CSceneStateGame::OnFieldRemoveItem, handled by
/// <see cref="DropPickup.KeepsOnRemove"/> (removes resting/unpicked drops, keeps drops already flying to their picker).
/// </summary>
public class FieldRemoveItemRecordedTests {
    // gms2 recording: 0x2C, packet 3009, 4 bytes (extracted with extract.py): object id 2510027
    static readonly byte[] Gms2 = Convert.FromHexString("CB4C2600");
    // kms2 recording: 0x2C, packet 1994, 4 bytes (extracted with extract.py): object id 3639352
    static readonly byte[] Kms = Convert.FromHexString("38883700");

    [Test] public void Gms2RemoveDecodesAndFollowsDropKeepRule() {
        var r = new ByteReader(Gms2);
        var m = FieldRemoveItem.Read(r);
        Assert.That(m.ObjectId, Is.EqualTo(2510027));
        Assert.That(r.Available, Is.EqualTo(0));

        // OnRemovePacket removes a resting drop, but keeps a drop already picked up and flying (state != Rest)
        Assert.That(DropPickup.KeepsOnRemove(pickerId: 0, DropMotion.Rest), Is.False);
        Assert.That(DropPickup.KeepsOnRemove(pickerId: 2509810, DropMotion.Rest), Is.False);
        Assert.That(DropPickup.KeepsOnRemove(pickerId: 2509810, DropMotion.FlyToPicker), Is.True);
    }

    [Test] public void KmsRemoveDecodesAndFollowsDropKeepRule() {
        var r = new ByteReader(Kms);
        var m = FieldRemoveItem.Read(r);
        Assert.That(m.ObjectId, Is.EqualTo(3639352));
        Assert.That(r.Available, Is.EqualTo(0));

        Assert.That(DropPickup.KeepsOnRemove(pickerId: 0, DropMotion.Rest), Is.False);
        Assert.That(DropPickup.KeepsOnRemove(pickerId: 3638027, DropMotion.FlyToPicker), Is.True);
    }
}
