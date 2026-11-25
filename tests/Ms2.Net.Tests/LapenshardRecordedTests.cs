using System;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and handler-effect tests for Lapenshard (KMS 0x11B, GMS2 0x11D;
/// CSceneStateGame::OnLapenshard @0x14173d2e0, scenestategame.cpp:8743 -> LapenshardData.Apply).
/// </summary>
public class LapenshardRecordedTests
{
    // kms2: 0x11B mode 0, 53 bytes (6 worn lapenshard slots)
    static readonly byte[] KmsWorn = Convert.FromHexString(
        "0006000000" +
        "010000007A9C7102" +
        "02000000859C7102" +
        "0300000008DF8002" +
        "04000000E4DF8002" +
        "05000000F9209002" +
        "0600000006219002");

    // kms2: 0x11B mode 1, 9 bytes (equip slot 3 -> item 0x0280DF0B)
    static readonly byte[] KmsEquipped = Convert.FromHexString("01030000000BDF8002");

    // kms2: 0x11B mode 2, 5 bytes (unequip slot 3)
    static readonly byte[] KmsUnequipped = Convert.FromHexString("0203000000");

    // gms2: 0x11D mode 0, 53 bytes (6 worn lapenshard slots)
    static readonly byte[] Gms2Worn = Convert.FromHexString(
        "0006000000" +
        "010000007A9C7102" +
        "02000000849C7102" +
        "0300000008E18002" +
        "0400000052DF8002" +
        "05000000F9209002" +
        "0600000005219002");

    // gms2: 0x11D mode 1, 9 bytes (equip slot 5 -> item 0x029020CB)
    static readonly byte[] Gms2Equipped = Convert.FromHexString("0105000000CB209002");

    // gms2: 0x11D mode 2, 5 bytes (unequip slot 5)
    static readonly byte[] Gms2Unequipped = Convert.FromHexString("0205000000");

    static LapenshardMessage DecodeAll(byte[] payload)
    {
        var r = new ByteReader(payload);
        var msg = LapenshardMessage.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        return msg;
    }

    static void VerifyWornEquipUnequip(
        byte[] wornBytes,
        byte[] equipBytes,
        byte[] unequipBytes,
        (int Slot, int ItemId)[] expectedWorn,
        int targetSlot,
        int replacedItemId,
        int equippedItemId)
    {
        var worn = (LapenshardMessage.Worn)DecodeAll(wornBytes);
        Assert.That(worn.Entries, Is.EqualTo(expectedWorn));

        var data = new LapenshardData();
        Assert.That(data.Apply(worn), Is.True);
        Assert.That(data.Count, Is.EqualTo(6));
        foreach (var (slot, itemId) in expectedWorn)
            Assert.That(data.GetItemId(slot), Is.EqualTo(itemId));

        var eq = (LapenshardMessage.Equipped)DecodeAll(equipBytes);
        Assert.That(eq, Is.EqualTo(new LapenshardMessage.Equipped(targetSlot, equippedItemId)));
        Assert.That(data.Apply(eq), Is.True);
        Assert.That(data.GetItemId(targetSlot), Is.EqualTo(equippedItemId));

        // Client rule: EquipLapenshard counts the new id up without decrementing the replaced id.
        var (_, countsAfterEquip) = data.Replay(_ => true);
        Assert.That(countsAfterEquip[equippedItemId], Is.EqualTo(1));
        Assert.That(countsAfterEquip[replacedItemId], Is.EqualTo(1));

        var un = (LapenshardMessage.Unequipped)DecodeAll(unequipBytes);
        Assert.That(un, Is.EqualTo(new LapenshardMessage.Unequipped(targetSlot)));
        Assert.That(data.Apply(un), Is.True);
        Assert.That(data.Count, Is.EqualTo(5));
        Assert.That(data.GetItemId(targetSlot), Is.EqualTo(0));
        var (slotsAfterUnequip, countsAfterUnequip) = data.Replay(_ => true);
        Assert.That(slotsAfterUnequip.ContainsKey(targetSlot), Is.False);
        Assert.That(countsAfterUnequip.ContainsKey(equippedItemId), Is.False);
    }

    [Test]
    public void KmsLapenshardWornEquipAndUnequipUpdateState()
        => VerifyWornEquipUnequip(
            KmsWorn, KmsEquipped, KmsUnequipped,
            expectedWorn: new[]
            {
                (1, 0x02719C7A), (2, 0x02719C85), (3, 0x0280DF08),
                (4, 0x0280DFE4), (5, 0x029020F9), (6, 0x02902106),
            },
            targetSlot: 3, replacedItemId: 0x0280DF08, equippedItemId: 0x0280DF0B);

    [Test]
    public void Gms2LapenshardWornEquipAndUnequipUpdateState()
        => VerifyWornEquipUnequip(
            Gms2Worn, Gms2Equipped, Gms2Unequipped,
            expectedWorn: new[]
            {
                (1, 0x02719C7A), (2, 0x02719C84), (3, 0x0280E108),
                (4, 0x0280DF52), (5, 0x029020F9), (6, 0x02902105),
            },
            targetSlot: 5, replacedItemId: 0x029020F9, equippedItemId: 0x029020CB);
}
