using System;
using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and handler-effect tests for Guild (KMS 0x8A, GMS2 0x8C;
/// CGuildManager::FUN_1413b6370, guildmanager.cpp:301 -> GuildBook.Apply).
/// </summary>
public class GuildRecordedTests
{
    // kms2: 0x8A type 0x01, 24 bytes (CreateGuild, result 0, 10-char guild name masked with '*')
    static readonly byte[] KmsCreateOk = Convert.FromHexString(
        "01000A002A002A002A002A002A002A002A002A002A002A00");

    // kms2: 0x8A type 0x13, 11 bytes (MemberLeft, 4-char member name masked with '*')
    static readonly byte[] KmsMemberLeft = Convert.FromHexString(
        "1304002A002A002A002A00");

    // kms2: 0x8A type 0x38, 27 bytes (CustomEvent opened: 8-char name masked with '*', eventId 1, time 0x62B2BD80)
    static readonly byte[] KmsCustomEvent = Convert.FromHexString(
        "3808002A002A002A002A002A002A002A002A000100000080BDB262");

    // kms2: 0x8A type 0x02, 2 bytes (Disband, result 0)
    static readonly byte[] KmsDisbandOk = Convert.FromHexString("0200");

    // gms2: 0x8C type 0x39 (57), 21 bytes: the npc upgrade (GMS2 case 57 @0x140aa0810 pairs with KMS 0x3b FUN_1413c3ef0, see GuildModes):
    // 5-char name masked with '*', npc kind 3, level 1
    static readonly byte[] Gms2NpcUpgrade = Convert.FromHexString(
        "3905002A002A002A002A002A000300000001000000");

    // gms2: 0x8C type 0x0F, 1 byte (AttendComplete)
    static readonly byte[] Gms2AttendComplete = Convert.FromHexString("0F");

    // gms2: 0x8C type 0x53 (GMS2 mode 83 -> KMS 0x55 JoinRequestsForGuild), 5 bytes (0 entries)
    static readonly byte[] Gms2GuildRequestsEmpty = Convert.FromHexString("5300000000");

    // gms2: 0x8C type 0x54 (GMS2 mode 84 -> KMS 0x56 MyRequests), 5 bytes (0 entries)
    static readonly byte[] Gms2MyRequestsEmpty = Convert.FromHexString("5400000000");

    static GuildBook InGuildBook(string memberName, int eventId = 1)
    {
        var book = new GuildBook
        {
            Common = id => $"c:{id:X}:{{0}}",
            CommonByKey = key => $"k:{key}",
        };
        var info = new GuildInfo
        {
            Id = 0x1111_0001,
            Name = "TestGuild",
            HouseLevel = 1,
            HouseTheme = 0,
        };
        info.Members.Add(new GuildMember { CharId = 0x2222_0001, Name = memberName, Mode = 3 });
        info.CustomEvents.Add((eventId, 0));
        book.Apply(new GuildMessage(0) { GuildId = info.Id, Info = info });
        return book;
    }

    [Test]
    public void KmsCreateMemberLeftCustomEventAndDisbandMutateGuildBook()
    {
        var create = GuildMessage.Read(new ByteReader(KmsCreateOk), ProtocolProfile.Kms2525);
        Assert.That(create.Type, Is.EqualTo(0x01));
        Assert.That(create.Result, Is.EqualTo(0));
        Assert.That(create.Name, Is.EqualTo("**********"));

        var book = InGuildBook("****");
        var sounds = new List<string>();
        var boxes = new List<string>();
        var chats = new List<(string Text, int Kind)>();
        var removedMembers = new List<string>();
        int closes = 0, infoRefreshes = 0, eventRefreshes = 0;

        book.Sound += s => sounds.Add(s);
        book.Box += b => boxes.Add(b);
        book.ChatLine += (t, k) => chats.Add((t, k));
        book.DialogClose += () => closes++;
        book.GuildInfoRefresh += () => infoRefreshes++;
        book.MemberRemoved += n => removedMembers.Add(n);
        book.EventsRefresh += () => eventRefreshes++;

        book.Apply(create);
        Assert.That(sounds, Is.EqualTo(new[] { "PC_GuildMake" }));
        Assert.That(boxes, Is.EqualTo(new[] { "c:389:**********" }));
        Assert.That(closes, Is.EqualTo(1));

        var left = GuildMessage.Read(new ByteReader(KmsMemberLeft), ProtocolProfile.Kms2525);
        Assert.That(left.Type, Is.EqualTo(0x13));
        Assert.That(left.Name, Is.EqualTo("****"));

        book.Apply(left);
        Assert.That(book.Info.Members, Is.Empty);
        Assert.That(infoRefreshes, Is.EqualTo(1));
        Assert.That(removedMembers, Is.EqualTo(new[] { "****" }));
        Assert.That(chats, Is.EqualTo(new[] { ("c:3A6:****", 0x0D) }));

        var ev = GuildMessage.Read(new ByteReader(KmsCustomEvent), ProtocolProfile.Kms2525);
        Assert.That(ev.Type, Is.EqualTo(0x38));
        Assert.That(ev.EventId, Is.EqualTo(1));
        Assert.That(ev.EventTime, Is.EqualTo(0x62B2BD80));

        book.Apply(ev);
        Assert.That(book.Info.CustomEvents[0], Is.EqualTo((1, 0x62B2BD80)));
        Assert.That(eventRefreshes, Is.EqualTo(1));

        var disband = GuildMessage.Read(new ByteReader(KmsDisbandOk), ProtocolProfile.Kms2525);
        Assert.That(disband.Type, Is.EqualTo(0x02));
        Assert.That(disband.Result, Is.EqualTo(0));

        book.Apply(disband);
        Assert.That(book.InGuild, Is.False);
        Assert.That(book.GuildId, Is.EqualTo(0));
        Assert.That(book.Info, Is.Null);
        Assert.That(closes, Is.EqualTo(2));
        Assert.That(boxes.Count, Is.EqualTo(2));
    }

    [Test]
    public void Gms2NpcUpgradeAttendAndListModesMutateGuildBook()
    {
        var upgrade = GuildMessage.Read(new ByteReader(Gms2NpcUpgrade), ProtocolProfile.Gms2);
        Assert.That(upgrade.Type, Is.EqualTo(0x3b));
        Assert.That(upgrade.Name, Is.EqualTo("*****"));
        Assert.That(upgrade.NpcKind, Is.EqualTo(3));
        Assert.That(upgrade.NpcLevel, Is.EqualTo(1));

        var book = InGuildBook("*****");
        var boxes = new List<string>();
        var chats = new List<(string Text, int Kind)>();
        var searchPages = new List<int>();
        int houseRefreshes = 0, npcRefreshes = 0, guildReqRefreshes = 0, myReqRefreshes = 0;

        book.Box += b => boxes.Add(b);
        book.ChatLine += (t, k) => chats.Add((t, k));
        book.HouseRefresh += () => houseRefreshes++;
        book.NpcRefresh += () => npcRefreshes++;
        book.RequestsForGuildRefresh += () => guildReqRefreshes++;
        book.SearchRefresh += p => searchPages.Add(p);
        book.MyRequestsRefresh += () => myReqRefreshes++;

        book.Apply(upgrade);
        Assert.That(book.Info.Npcs, Does.Contain((3, 1)));
        Assert.That(houseRefreshes, Is.EqualTo(0));
        Assert.That(npcRefreshes, Is.EqualTo(1));

        var attend = GuildMessage.Read(new ByteReader(Gms2AttendComplete), ProtocolProfile.Gms2);
        Assert.That(attend.Type, Is.EqualTo(0x0F));
        book.Apply(attend);
        Assert.That(boxes, Is.EqualTo(new[] { "k:s_guild_attend_complete" }));
        Assert.That(chats, Is.EqualTo(new[] { ("k:s_guild_attend_complete", 0x0D) }));

        // GMS2 mode 83 (0x53) maps to KMS 0x55 JoinRequestsForGuild and replaces RequestsForGuild while in a guild.
        book.Apply(new GuildMessage(0x2D) { NewRequest = new GuildJoinRequestForGuild { Id = 99 } });
        Assert.That(book.RequestsForGuild.Count, Is.EqualTo(1));

        var guildReqs = GuildMessage.Read(new ByteReader(Gms2GuildRequestsEmpty), ProtocolProfile.Gms2);
        Assert.That(guildReqs.Type, Is.EqualTo(0x55));
        Assert.That(guildReqs.GuildRequests, Is.Empty);
        book.Apply(guildReqs);
        Assert.That(book.RequestsForGuild, Is.Empty);
        Assert.That(guildReqRefreshes, Is.EqualTo(2));

        // GMS2 mode 84 (0x54) maps to KMS 0x56 MyRequests when not in a guild.
        book.LeaveGame();
        Assert.That(book.InGuild, Is.False);

        var myReqs = GuildMessage.Read(new ByteReader(Gms2MyRequestsEmpty), ProtocolProfile.Gms2);
        Assert.That(myReqs.Type, Is.EqualTo(0x56));
        Assert.That(myReqs.Requests, Is.Empty);
        book.Apply(myReqs);
        Assert.That(book.MyRequests, Is.Empty);
        Assert.That(searchPages, Is.EqualTo(new[] { 0 }));
        Assert.That(myReqRefreshes, Is.EqualTo(1));
    }
}
