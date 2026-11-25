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

    // kms2: 0x11B mode 1, 9 bytes (equip slot 1 -> item 0x02719C86)
    static readonly byte[] KmsEquipped = Convert.FromHexString("0101000000869C7102");

    // kms2: 0x11B mode 2, 5 bytes (unequip slot 1)
    static readonly byte[] KmsUnequipped = Convert.FromHexString("0201000000");

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

    static void VerifyWornEquipUnequip(byte[] wornBytes, byte[] equipBytes, byte[] unequipBytes)
    {
        var worn = (LapenshardMessage.Worn)DecodeAll(wornBytes);
        Assert.That(worn.Entries.Count, Is.EqualTo(6));

        var data = new LapenshardData();
        Assert.That(data.Apply(worn), Is.True);
        Assert.That(data.Count, Is.EqualTo(worn.Entries.Count));
        foreach (var (slot, itemId) in worn.Entries)
            Assert.That(data.GetItemId(slot), Is.EqualTo(itemId));

        var eq = (LapenshardMessage.Equipped)DecodeAll(equipBytes);
        int replacedId = data.GetItemId(eq.Slot);
        Assert.That(data.Apply(eq), Is.True);
        Assert.That(data.GetItemId(eq.Slot), Is.EqualTo(eq.ItemId));

        // Client rule: EquipLapenshard counts the new id up without decrementing the replaced id.
        var (_, countsAfterEquip) = data.Replay(_ => true);
        Assert.That(countsAfterEquip[eq.ItemId], Is.EqualTo(1));
        Assert.That(countsAfterEquip[replacedId], Is.EqualTo(1));

        var un = (LapenshardMessage.Unequipped)DecodeAll(unequipBytes);
        Assert.That(un.Slot, Is.EqualTo(eq.Slot));
        Assert.That(data.Apply(un), Is.True);
        Assert.That(data.Count, Is.EqualTo(worn.Entries.Count - 1));
        Assert.That(data.GetItemId(un.Slot), Is.EqualTo(0));
        var (slotsAfterUnequip, countsAfterUnequip) = data.Replay(_ => true);
        Assert.That(slotsAfterUnequip.ContainsKey(un.Slot), Is.False);
        Assert.That(countsAfterUnequip.ContainsKey(eq.ItemId), Is.False);
    }

    [Test]
    public void KmsLapenshardWornEquipAndUnequipUpdateState()
        => VerifyWornEquipUnequip(KmsWorn, KmsEquipped, KmsUnequipped);

    [Test]
    public void Gms2LapenshardWornEquipAndUnequipUpdateState()
        => VerifyWornEquipUnequip(Gms2Worn, Gms2Equipped, Gms2Unequipped);
}
