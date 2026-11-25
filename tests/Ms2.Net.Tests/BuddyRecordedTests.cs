using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Buddy (KMS 0x61, GMS2 0x63): CBuddyManager::OnBuddy (buddymanager.cpp:95), handled by
/// <see cref="BuddyBook.Apply"/> (protocol 15 clear, protocol 1 list load, protocol 8 update, protocol 14 online/offline).
/// </summary>
public class BuddyRecordedTests {
    // gms2 recording: 0x63 protocol 15 (Clear), packet 8, 1 byte (extracted with extract.py)
    static readonly byte[] Gms2Clear = Convert.FromHexString("0F");
    // gms2 recording: 0x63 protocol 1 (1-entry list), packet 8, 232 bytes
    // (extracted with extract.py; strings masked with '*', entry/character/account ids masked with 0x11/0x22/0x33)
    static readonly byte[] Gms2List = Convert.FromHexString(
        "010100000011111111111111112222222222222222333333333333333305002A002A002A002A002A0000000000000000" +
        "005A000000850300005000000000000000000000A0B0455E38002A002A002A002A002A002A002A002A002A002A002A00" +
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A00" +
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A000000000000000000" +
        "0000000000000000000004002A002A002A002A008901695E00000000A50200008701000031020000");
    // gms2 recording: 0x63 protocol 8 (entry update, online=true), packet 4269, 226 bytes
    // (extracted with extract.py; strings masked with '*', entry/character/account ids masked with 0x11/0x22/0x33)
    static readonly byte[] Gms2Update = Convert.FromHexString(
        "0811111111111111112222222222222222333333333333333308002A002A002A002A002A002A002A002A000000000000" +
        "000000000000002D01000052000000000100000000001B3CC75C38002A002A002A002A002A002A002A002A002A002A00" +
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A00" +
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A0000000000" +
        "000000000000000000000000000035D27A5E00000000080300003902000073030000");
    // gms2 recording: 0x63 protocol 14 (came online, flag 0), packet 4270, 28 bytes
    // (extracted with extract.py; name masked with '*', entry id masked with 0x11)
    static readonly byte[] Gms2Online = Convert.FromHexString("0E00111111111111111108002A002A002A002A002A002A002A002A00");

    // kms2 recording: 0x61 protocol 15 (Clear), packet 11, 1 byte (extracted with extract.py)
    static readonly byte[] KmsClear = Convert.FromHexString("0F");
    // kms2 recording: 0x61 protocol 1 (1-entry list), packet 14, 238 bytes
    // (extracted with extract.py; strings masked with '*', entry/character/account ids masked with 0x11/0x22/0x33)
    static readonly byte[] KmsList = Convert.FromHexString(
        "010100000011111111111111112222222222222222333333333333333306002A002A002A002A002A002A000000000000" +
        "0000000A000000640000000F000000000000000000005247876038002A002A002A002A002A002A002A002A002A002A00" +
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A00" +
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A0000000000" +
        "00000000000000000000000006002A002A002A002A002A002A00C07B8B6100000000030000000E0000000A000000");
    // kms2 recording: 0x61 protocol 14 (went offline, flag 1), packet 1307, 24 bytes
    // (extracted with extract.py; name masked with '*', entry id masked with 0x11)
    static readonly byte[] KmsOffline = Convert.FromHexString("0E01111111111111111106002A002A002A002A002A002A00");

    [Test] public void Gms2BuddyListUpdateOnlineAndClearApplyToBuddyBook() {
        var book = new BuddyBook();
        var events = new List<(BuddyEventKind Kind, long Id)>();
        book.BuddyEvent += (k, id, _) => events.Add((k, id));

        var r1 = new ByteReader(Gms2List);
        var list = BuddyMessage.Read(r1);
        Assert.That((list.Protocol, list.Entries.Count), Is.EqualTo(((byte)1, 1)));
        Assert.That((list.Entries[0].Id, list.Entries[0].JobGroup, list.Entries[0].JobCode, list.Entries[0].Level),
            Is.EqualTo((0x1111111111111111L, 90, 901, 80)));
        Assert.That(list.Entries[0].Trophy, Is.EqualTo((677, 391, 561)));
        Assert.That(r1.Available, Is.EqualTo(0));
        book.Apply(list);
        Assert.That(book.FindById(0x1111111111111111L)?.Level, Is.EqualTo(80));
        Assert.That(book.FindById(0x1111111111111111L)?.Online, Is.False);

        var r8 = new ByteReader(Gms2Update);
        var upd = BuddyMessage.Read(r8);
        Assert.That((upd.Protocol, upd.Entries.Count, upd.Entries[0].Level, upd.Entries[0].Online),
            Is.EqualTo(((byte)8, 1, 82, true)));
        Assert.That(r8.Available, Is.EqualTo(0));
        book.Apply(upd);
        Assert.That((book.FindById(0x1111111111111111L).Level, book.FindById(0x1111111111111111L).Online), Is.EqualTo((82, true)));
        Assert.That(events.Any(e => e.Kind == BuddyEventKind.Updated && e.Id == 0x1111111111111111L), Is.True);

        var r14 = new ByteReader(Gms2Online);
        var on = BuddyMessage.Read(r14);
        Assert.That((on.Protocol, on.Flag, on.Id, on.Name.Length), Is.EqualTo(((byte)14, (byte)0, 0x1111111111111111L, 8)));
        Assert.That(r14.Available, Is.EqualTo(0));
        book.Apply(on);

        var r15 = new ByteReader(Gms2Clear);
        var clear = BuddyMessage.Read(r15);
        Assert.That(clear.Protocol, Is.EqualTo((byte)15));
        Assert.That(r15.Available, Is.EqualTo(0));
        book.Apply(clear);
        Assert.That(book.FindById(0x1111111111111111L), Is.Null);
    }

    [Test] public void KmsBuddyListOfflineAndClearApplyToBuddyBook() {
        var book = new BuddyBook();

        var r1 = new ByteReader(KmsList);
        var list = BuddyMessage.Read(r1);
        Assert.That((list.Protocol, list.Entries.Count), Is.EqualTo(((byte)1, 1)));
        Assert.That((list.Entries[0].Id, list.Entries[0].JobGroup, list.Entries[0].JobCode, list.Entries[0].Level),
            Is.EqualTo((0x1111111111111111L, 10, 100, 15)));
        Assert.That(list.Entries[0].Trophy, Is.EqualTo((3, 14, 10)));
        Assert.That(r1.Available, Is.EqualTo(0));
        book.Apply(list);
        Assert.That(book.FindById(0x1111111111111111L)?.Level, Is.EqualTo(15));

        var r14 = new ByteReader(KmsOffline);
        var off = BuddyMessage.Read(r14);
        Assert.That((off.Protocol, off.Flag, off.Id, off.Name.Length), Is.EqualTo(((byte)14, (byte)1, 0x1111111111111111L, 6)));
        Assert.That(r14.Available, Is.EqualTo(0));
        book.Apply(off);

        var r15 = new ByteReader(KmsClear);
        var clear = BuddyMessage.Read(r15);
        Assert.That(clear.Protocol, Is.EqualTo((byte)15));
        Assert.That(r15.Available, Is.EqualTo(0));
        book.Apply(clear);
        Assert.That(book.FindById(0x1111111111111111L), Is.Null);
    }
}
