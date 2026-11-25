using Maple2.PacketLib.Tools;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// FieldRemoveItem (0x2C on KMS2 and GMS2): CSceneStateGame::OnFieldRemoveItem (scenestategame.cpp:2402), handled by
/// <see cref="DropPickup.KeepsOnRemove"/> (removes resting/unpicked drops, keeps drops already flying to their picker).
/// </summary>
public class FieldRemoveItemRecordedTests {
    // gms2: paired 0x2D item pickup (9 bytes) and 0x2C remove (4 bytes) for object 2510027
    static readonly byte[] Gms2Pickup = Convert.FromHexString("01CB4C2600F24B2600");
    static readonly byte[] Gms2Remove = Convert.FromHexString("CB4C2600");

    // kms2: paired 0x2D item pickup (9 bytes) and 0x2C remove (4 bytes) for object 3639352
    static readonly byte[] KmsPickup = Convert.FromHexString("01388837000B833700");
    static readonly byte[] KmsRemove = Convert.FromHexString("38883700");

    [Test] public void Gms2RemoveDecodesAndFollowsDropKeepRule() {
        var rp = new ByteReader(Gms2Pickup);
        var pick = FieldPickupItem.Read(rp, id => (id == 2510027, false, false));
        Assert.That(rp.Available, Is.EqualTo(0));

        var rr = new ByteReader(Gms2Remove);
        var rem = FieldRemoveItem.Read(rr);
        Assert.That(rem.ObjectId, Is.EqualTo(2510027));
        Assert.That(rem.ObjectId, Is.EqualTo(pick.ObjectId));
        Assert.That(rr.Available, Is.EqualTo(0));

        // OnRemovePacket removes a resting drop, but keeps a drop already picked up and flying (state != Rest)
        Assert.That(DropPickup.KeepsOnRemove(pickerId: 0, DropMotion.Rest), Is.False);
        Assert.That(DropPickup.KeepsOnRemove(pick.PickerId, DropMotion.Rest), Is.False);
        Assert.That(DropPickup.KeepsOnRemove(pick.PickerId, DropMotion.FlyToPicker), Is.True);
    }

    [Test] public void KmsRemoveDecodesAndFollowsDropKeepRule() {
        var rp = new ByteReader(KmsPickup);
        var pick = FieldPickupItem.Read(rp, id => (id == 3639352, false, false));
        Assert.That(rp.Available, Is.EqualTo(0));

        var rr = new ByteReader(KmsRemove);
        var rem = FieldRemoveItem.Read(rr);
        Assert.That(rem.ObjectId, Is.EqualTo(3639352));
        Assert.That(rem.ObjectId, Is.EqualTo(pick.ObjectId));
        Assert.That(rr.Available, Is.EqualTo(0));

        Assert.That(DropPickup.KeepsOnRemove(pickerId: 0, DropMotion.Rest), Is.False);
        Assert.That(DropPickup.KeepsOnRemove(pick.PickerId, DropMotion.Rest), Is.False);
        Assert.That(DropPickup.KeepsOnRemove(pick.PickerId, DropMotion.FlyToPicker), Is.True);
    }
}
