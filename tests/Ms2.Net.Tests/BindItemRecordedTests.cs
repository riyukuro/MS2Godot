using System;
using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and handler-effect tests for BindItem (KMS 0x103, GMS2 0x105;
/// CSceneStateGame::OnBindItem @0x14173b420, scenestategame.cpp:8542 -> BindItem.Read, ItemBook.Apply).
/// </summary>
public class BindItemRecordedTests
{
    const long KmsItemUid = 0x3333_0000_0000_0001L;
    const long KmsBindingCharId = 0x1111_0000_0000_0001L;

    const long Gms2ItemUid = 0x4444_0000_0000_0001L;
    const long Gms2BindingCharId = 0x2222_0000_0000_0002L;

    // kms2: 0x103, 207 bytes (item UID, transfer binding character ID, and 5-char binding name masked)
    static readonly byte[] KmsBindItem = Convert.FromHexString(
        "01000000000033330A000000FFFFFFFFA26F185E0000000000000000000000000000000000000000" +
        "000000000000000000000000000000000000000000000000000000000000000000000000FFFFFFFF" +
        "00000000000000000000000000000000000000000000000000000000000000000000000000000000" +
        "00000000000000000000000000010000000000000000000000000000000000080000000000000000" +
        "00000000000101010000000000111105002A002A002A002A002A0000000000000000000000000000" +
        "00000000000000");

    // gms2: 0x105, 258 bytes (item UID, transfer binding character ID, and 4-char binding name masked)
    static readonly byte[] Gms2BindItem = Convert.FromHexString(
        "01000000000044440100000000000000FFFFFFFF7C33795E00000000000000000000000000000000" +
        "000000000000000000000000000000000000000000000000000000002F2FAAFFDB854CFF131360FF" +
        "00000000050000000000000000000000000000000000000000000000000000000000000000000000" +
        "00000000000000000000000000000000000000000000000000000000000000000000000000000000" +
        "00000000000000000000000000000000000000000000000000000100000000000000000000000000" +
        "000000000E000000000000000000000000000101020000000000222204002A002A002A002A000000" +
        "000000000000000000000000000000000000");

    static void VerifyBindItemUpdatesInventory(
        byte[] payload,
        ProtocolProfile profile,
        long uid,
        int itemId,
        EquipSlot slot,
        int expectedAmount,
        long expectedCreationTime,
        long expectedBindingId,
        string expectedBindingName)
    {
        // Unknown UID leaves the body unread and marks Found = false.
        var empty = new ItemBook();
        var missing = BindItem.Read(new ByteReader(payload), empty.Get, _ => null, profile);
        Assert.That(missing.Uid, Is.EqualTo(uid));
        Assert.That(missing.Found, Is.False);
        Assert.That(missing.Item, Is.Null);

        var items = new ItemBook();
        var changedUids = new List<long>();
        items.Changed += id => changedUids.Add(id);
        items.Apply(new ItemMessage.Add(new EquipItem { Id = itemId, Uid = uid, Slot = slot, Rarity = 1, Amount = 1 }));
        changedUids.Clear();

        var r = new ByteReader(payload);
        var bound = BindItem.Read(r, items.Get, _ => null, profile);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(bound.Uid, Is.EqualTo(uid));
        Assert.That(bound.Found, Is.True);
        Assert.That(bound.Worn, Is.False);
        Assert.That(bound.Item.Amount, Is.EqualTo(expectedAmount));
        Assert.That(bound.Item.CreationTime, Is.EqualTo(expectedCreationTime));
        Assert.That(bound.Item.Transfer.HasBinding, Is.True);
        Assert.That(bound.Item.Transfer.BindingId, Is.EqualTo(expectedBindingId));
        Assert.That(bound.Item.Transfer.BindingName, Is.EqualTo(expectedBindingName));

        items.Apply(new ItemMessage.Update(bound.Uid, bound.Item));
        Assert.That(changedUids, Is.EqualTo(new[] { uid }));
        Assert.That(items.Get(uid).Transfer.HasBinding, Is.True);
        Assert.That(items.Get(uid).Transfer.BindingId, Is.EqualTo(expectedBindingId));
        Assert.That(items.Get(uid).Transfer.BindingName, Is.EqualTo(expectedBindingName));
        Assert.That(items.Get(uid).Amount, Is.EqualTo(expectedAmount));
    }

    [Test]
    public void KmsBindItemReadsBodyIntoBagItemAndUpdatesItemBook()
        => VerifyBindItemUpdatesInventory(
            KmsBindItem,
            ProtocolProfile.Kms2525,
            uid: KmsItemUid,
            itemId: 30000001,
            slot: EquipSlot.Unknown,
            expectedAmount: 10,
            expectedCreationTime: 0x5E186FA2L,
            expectedBindingId: KmsBindingCharId,
            expectedBindingName: "*****");

    [Test]
    public void Gms2BindItemReadsBodyIntoBagItemAndUpdatesItemBook()
        => VerifyBindItemUpdatesInventory(
            Gms2BindItem,
            ProtocolProfile.Gms2,
            uid: Gms2ItemUid,
            itemId: 11400636,
            slot: EquipSlot.CL,
            expectedAmount: 1,
            expectedCreationTime: 0x5E79337CL,
            expectedBindingId: Gms2BindingCharId,
            expectedBindingName: "****");
}
