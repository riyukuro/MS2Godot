using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Buddy (KMS 0x61, GMS2 0x63): CBuddyManager::OnBuddy (buddymanager.cpp:95), handled by
/// <see cref="BuddyBook.Apply"/> (protocol 15 clear, protocol 1 list load, protocol 8 update, protocol 14 online/offline notice).
/// </summary>
public class BuddyRecordedTests {
    // gms2: 0x63 protocol 15 (Clear), 1 byte
    static readonly byte[] Gms2Clear = Convert.FromHexString("0F");
    // gms2: 0x63 protocol 1 (1-entry list), 232 bytes (strings masked with '*', entry/character/account ids masked with 0x11/0x22/0x33)
    static readonly byte[] Gms2List = Convert.FromHexString(
        "010100000011111111111111112222222222222222333333333333333305002A002A002A002A002A0000000000000000" +
        "005A000000850300005000000000000000000000A0B0455E38002A002A002A002A002A002A002A002A002A002A002A00" +
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A00" +
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A000000000000000000" +
        "0000000000000000000004002A002A002A002A008901695E00000000A50200008701000031020000");
    // gms2: 0x63 protocol 8 (entry update, online=true), 226 bytes (strings masked with '*', ids masked with 0x11/0x22/0x33)
    static readonly byte[] Gms2Update = Convert.FromHexString(
        "0811111111111111112222222222222222333333333333333308002A002A002A002A002A002A002A002A000000000000" +
        "000000000000002D01000052000000000100000000001B3CC75C38002A002A002A002A002A002A002A002A002A002A00" +
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A00" +
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A0000000000" +
        "000000000000000000000000000035D27A5E00000000080300003902000073030000");
    // gms2: 0x63 protocol 14 (came online, flag 0), 28 bytes (name masked with '*', entry id masked with 0x11)
    static readonly byte[] Gms2Online = Convert.FromHexString("0E00111111111111111108002A002A002A002A002A002A002A002A00");

    // kms2: 0x61 protocol 15 (Clear), 1 byte
    static readonly byte[] KmsClear = Convert.FromHexString("0F");
    // kms2: 0x61 protocol 1 (1-entry list), 238 bytes (strings masked with '*', ids masked with 0x11/0x22/0x33)
    static readonly byte[] KmsList = Convert.FromHexString(
        "010100000011111111111111112222222222222222333333333333333306002A002A002A002A002A002A000000000000" +
        "0000000A000000640000000F000000000000000000005247876038002A002A002A002A002A002A002A002A002A002A00" +
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A00" +
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A0000000000" +
        "00000000000000000000000006002A002A002A002A002A002A00C07B8B6100000000030000000E0000000A000000");
    // kms2: 0x61 protocol 14 (went offline, flag 1), 24 bytes (name masked with '*', entry id masked with 0x11)
    static readonly byte[] KmsOffline = Convert.FromHexString("0E01111111111111111106002A002A002A002A002A002A00");

    static BuddyBook NewBook(out List<(BuddyEventKind Kind, long Id)> events, out List<(string Text, int Kind)> chats, out Func<int> menuCounts) {
        var ev = new List<(BuddyEventKind, long)>();
        var ch = new List<(string, int)>();
        int menus = 0;
        var book = new BuddyBook {
            Common = id => id switch { 0x608 => "{0} online", 0x609 => "{0} offline", _ => "" },
        };
        book.BuddyEvent += (k, id, _) => ev.Add((k, id));
        book.ChatLine += (text, kind) => ch.Add((text, kind));
        book.MenuCount += () => menus++;
        events = ev; chats = ch; menuCounts = () => menus;
        return book;
    }

    [Test] public void Gms2BuddyListUpdateOnlineAndClearApplyToBuddyBook() {
        var book = NewBook(out var events, out var chats, out var menuCounts);

        var r1 = new ByteReader(Gms2List);
        var list = BuddyMessage.Read(r1);
        Assert.That((list.Protocol, list.Entries.Count), Is.EqualTo(((byte)1, 1)));
        Assert.That((list.Entries[0].Id, list.Entries[0].JobGroup, list.Entries[0].JobCode, list.Entries[0].Level),
            Is.EqualTo((0x1111111111111111L, 90, 901, 80)));
        Assert.That(list.Entries[0].Trophy, Is.EqualTo((677, 391, 561)));
        Assert.That(r1.Available, Is.EqualTo(0));
        book.Apply(list);
        Assert.That((book.Count, book.OnlineCount), Is.EqualTo((1, 0)));
        Assert.That(book.FindById(list.Entries[0].Id)?.Online, Is.False);

        var r8 = new ByteReader(Gms2Update);
        var upd = BuddyMessage.Read(r8);
        Assert.That((upd.Protocol, upd.Entries.Count, upd.Entries[0].Level, upd.Entries[0].Online),
            Is.EqualTo(((byte)8, 1, 82, true)));
        Assert.That(r8.Available, Is.EqualTo(0));
        book.Apply(upd);
        Assert.That((book.FindById(upd.Entries[0].Id).Level, book.FindById(upd.Entries[0].Id).Online, book.OnlineCount),
            Is.EqualTo((82, true, 1)));
        Assert.That(events, Is.EqualTo(new[] { (BuddyEventKind.Updated, upd.Entries[0].Id) }));

        // OnBuddy protocol 14 posts the chat status line (0x608 online) without mutating the entry or firing BuddyEvent
        var r14 = new ByteReader(Gms2Online);
        var on = BuddyMessage.Read(r14);
        Assert.That((on.Protocol, on.Flag, on.Id, on.Name.Length), Is.EqualTo(((byte)14, (byte)0, 0x1111111111111111L, 8)));
        Assert.That(r14.Available, Is.EqualTo(0));
        int eventsBefore = events.Count;
        book.Apply(on);
        Assert.That(book.FindById(on.Id)?.Online, Is.True);
        Assert.That(chats, Is.EqualTo(new[] { (on.Name + " online", 12) }));
        Assert.That(events.Count, Is.EqualTo(eventsBefore));

        var r15 = new ByteReader(Gms2Clear);
        var clear = BuddyMessage.Read(r15);
        Assert.That(clear.Protocol, Is.EqualTo((byte)15));
        Assert.That(r15.Available, Is.EqualTo(0));
        book.Apply(clear);
        Assert.That((book.Count, book.OnlineCount, menuCounts()), Is.EqualTo((0, 0, 4)));
        Assert.That(book.Buddies, Is.Empty);
        Assert.That(book.FindById(on.Id), Is.Null);
    }

    [Test] public void KmsBuddyListOfflineAndClearApplyToBuddyBook() {
        var book = NewBook(out var events, out var chats, out var menuCounts);

        var r1 = new ByteReader(KmsList);
        var list = BuddyMessage.Read(r1);
        Assert.That((list.Protocol, list.Entries.Count), Is.EqualTo(((byte)1, 1)));
        Assert.That((list.Entries[0].Id, list.Entries[0].JobGroup, list.Entries[0].JobCode, list.Entries[0].Level),
            Is.EqualTo((0x1111111111111111L, 10, 100, 15)));
        Assert.That(list.Entries[0].Trophy, Is.EqualTo((3, 14, 10)));
        Assert.That(r1.Available, Is.EqualTo(0));
        book.Apply(list);
        Assert.That((book.Count, book.OnlineCount), Is.EqualTo((1, 0)));
        Assert.That(book.FindById(list.Entries[0].Id)?.Online, Is.False);

        // OnBuddy protocol 14 flag 1 posts the offline chat line (0x609) without firing BuddyEvent
        var r14 = new ByteReader(KmsOffline);
        var off = BuddyMessage.Read(r14);
        Assert.That((off.Protocol, off.Flag, off.Id, off.Name.Length), Is.EqualTo(((byte)14, (byte)1, 0x1111111111111111L, 6)));
        Assert.That(r14.Available, Is.EqualTo(0));
        book.Apply(off);
        Assert.That(book.FindById(off.Id)?.Online, Is.False);
        Assert.That(chats, Is.EqualTo(new[] { (off.Name + " offline", 12) }));
        Assert.That(events, Is.Empty);

        var r15 = new ByteReader(KmsClear);
        var clear = BuddyMessage.Read(r15);
        Assert.That(clear.Protocol, Is.EqualTo((byte)15));
        Assert.That(r15.Available, Is.EqualTo(0));
        book.Apply(clear);
        Assert.That((book.Count, book.OnlineCount, menuCounts()), Is.EqualTo((0, 0, 3)));
        Assert.That(book.Buddies, Is.Empty);
        Assert.That(book.FindById(off.Id), Is.Null);
    }
}
