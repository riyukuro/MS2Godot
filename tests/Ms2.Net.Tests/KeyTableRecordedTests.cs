using System;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and handler-effect tests for KeyTable (KMS 0x6F, GMS2 0x71;
/// CKeyTable::OnKeyTable @0x141428bb0, keytable.cpp:285 -> ServerKeyTable.Apply).
/// </summary>
public class KeyTableRecordedTests
{
    // kms2: 0x6F mode 0, 2 bytes (useDefault = 1)
    static readonly byte[] KmsMode0Default = Convert.FromHexString("0001");

    // kms2: 0x6F mode 7, 77 bytes (currentPage 0, 3 pages, 3 skill quick slots on page 0: slots 4, 5, 6)
    static readonly byte[] KmsMode7Pages = Convert.FromHexString(
        "070000030003000000" +
        "04000000A137A000000000000000000000000000" +
        "05000000AB37A000000000000000000000000000" +
        "060000003738A000000000000000000000000000" +
        "0000000000000000");

    // gms2: 0x71 mode 7, 97 bytes (currentPage 0, 3 pages, 4 skill quick slots on page 0: slots 4, 5, 6, 7)
    static readonly byte[] Gms2Mode7Pages = Convert.FromHexString(
        "070000030004000000" +
        "040000006B5FA900000000000000000000000000" +
        "05000000895FA900000000000000000000000000" +
        "06000000935FA900000000000000000000000000" +
        "070000009D5FA900000000000000000000000000" +
        "0000000000000000");

    // gms2: 0x71 mode 7, 77 bytes (currentPage 0, 3 pages, 3 skill quick slots on page 0: slots 4, 5, 6)
    static readonly byte[] Gms2Mode7AltPages = Convert.FromHexString(
        "070000030003000000" +
        "040000003F52A600000000000000000000000000" +
        "05000000B752A600000000000000000000000000" +
        "060000005352A600000000000000000000000000" +
        "0000000000000000");

    // gms2: 0x71 mode 9, 1 byte (ignored mode returns 9 without altering pages)
    static readonly byte[] Gms2Mode9Ignored = Convert.FromHexString("09");

    [Test]
    public void KmsKeyTableMode0DefaultAndMode7PopulateQuickSlots()
    {
        var table = new ServerKeyTable();

        var r0 = new ByteReader(KmsMode0Default);
        Assert.That(table.Apply(r0), Is.EqualTo(0));
        Assert.That(r0.Available, Is.EqualTo(0));
        Assert.That(table.Loaded, Is.True);
        Assert.That(table.UsesDefaultKeys, Is.True);
        Assert.That(table.PageNotified, Is.False);
        Assert.That(table.PageCount, Is.EqualTo(0));

        var r7 = new ByteReader(KmsMode7Pages);
        Assert.That(table.Apply(r7), Is.EqualTo(7));
        Assert.That(r7.Available, Is.EqualTo(0));
        Assert.That(table.CurrentPage, Is.EqualTo(0));
        Assert.That(table.PageCount, Is.EqualTo(3));
        Assert.That(table.PageNotified, Is.True);
        Assert.That(table.Entry(0, 4), Is.EqualTo(new QuickSlotEntry(10_500_001, 0, 0)));
        Assert.That(table.Entry(0, 5), Is.EqualTo(new QuickSlotEntry(10_500_011, 0, 0)));
        Assert.That(table.Entry(0, 6), Is.EqualTo(new QuickSlotEntry(10_500_151, 0, 0)));
        Assert.That(table.FindQuickSlotBySkillAndItemUid(10_500_151, 0), Is.EqualTo(6));
    }

    [Test]
    public void Gms2KeyTableMode7PopulatesQuickSlotsAndRespectsLockAndIgnoredMode()
    {
        var table = new ServerKeyTable();

        var r7 = new ByteReader(Gms2Mode7Pages);
        Assert.That(table.Apply(r7), Is.EqualTo(7));
        Assert.That(r7.Available, Is.EqualTo(0));
        Assert.That(table.CurrentPage, Is.EqualTo(0));
        Assert.That(table.PageCount, Is.EqualTo(3));
        Assert.That(table.PageNotified, Is.True);
        Assert.That(table.Entry(0, 4), Is.EqualTo(new QuickSlotEntry(11_100_011, 0, 0)));
        Assert.That(table.Entry(0, 5), Is.EqualTo(new QuickSlotEntry(11_100_041, 0, 0)));
        Assert.That(table.Entry(0, 6), Is.EqualTo(new QuickSlotEntry(11_100_051, 0, 0)));
        Assert.That(table.Entry(0, 7), Is.EqualTo(new QuickSlotEntry(11_100_061, 0, 0)));
        Assert.That(table.FindQuickSlotBySkillAndItemUid(11_100_061, 0), Is.EqualTo(7));

        var r9 = new ByteReader(Gms2Mode9Ignored);
        Assert.That(table.Apply(r9), Is.EqualTo(9));
        Assert.That(r9.Available, Is.EqualTo(0));
        Assert.That(table.PageCount, Is.EqualTo(3));
        Assert.That(table.Entry(0, 7), Is.EqualTo(new QuickSlotEntry(11_100_061, 0, 0)));

        // Locked KeyTable skips mode 7 updates without modifying stored slots.
        table.Locked = true;
        Assert.That(table.Apply(new ByteReader(Gms2Mode7AltPages)), Is.EqualTo(7));
        Assert.That(table.Entry(0, 4), Is.EqualTo(new QuickSlotEntry(11_100_011, 0, 0)));
        Assert.That(table.Entry(0, 7), Is.EqualTo(new QuickSlotEntry(11_100_061, 0, 0)));

        // Once unlocked, the second recorded GMS2 mode 7 packet replaces the page entries.
        table.Locked = false;
        var rAlt = new ByteReader(Gms2Mode7AltPages);
        Assert.That(table.Apply(rAlt), Is.EqualTo(7));
        Assert.That(rAlt.Available, Is.EqualTo(0));
        Assert.That(table.Entry(0, 4), Is.EqualTo(new QuickSlotEntry(10_900_031, 0, 0)));
        Assert.That(table.Entry(0, 5), Is.EqualTo(new QuickSlotEntry(10_900_151, 0, 0)));
        Assert.That(table.Entry(0, 6), Is.EqualTo(new QuickSlotEntry(10_900_051, 0, 0)));
        Assert.That(table.Entry(0, 7).IsEmpty, Is.True);
    }
}
