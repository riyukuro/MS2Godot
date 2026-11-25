using System;
using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and handler-effect tests for Party (KMS 0x53, GMS2 0x54;
/// CPartyManager::OnParty, partymanager.cpp:447 -> PartyBook.Apply).
/// </summary>
public class PartyRecordedTests
{
    const long CharA = 0x1111_1111_1111_1101L;
    const long CharB = 0x2222_2222_2222_2202L;

    // kms2: 0x53 mode 0x0B, 19 bytes (PartyInvite: 6-char name masked with '*', kind 0x5251)
    static readonly byte[] KmsInvite = Convert.FromHexString(
        "0B06002A002A002A002A002A002A0051520000");

    // kms2: 0x53 mode 0x13, 27 bytes (PartyMemberFlags: CharA, AcctB, maxHp 5000, hp 5000, tomb 0; IDs masked)
    static readonly byte[] KmsMemberFlags = Convert.FromHexString(
        "130111111111111111022222222222222288130000881300000000");

    // kms2: 0x53 mode 0x19, 6 bytes (PartyMatchDungeonChange: matchmaking true, dungeonId 21001001)
    static readonly byte[] KmsMatchDungeonChange = Convert.FromHexString("190129734001");

    // kms2: 0x53 mode 0x1E, 5 bytes (PartyDungeonMatchRegister: group 1, registered true, announce true, searchBits 2)
    static readonly byte[] KmsDungeonMatchRegister = Convert.FromHexString("1E01010102");

    // kms2: 0x53 mode 0x2F, 50 bytes (PartyVoteStart Ready: id 1, 2 participants [CharA, CharB], 1 yes [CharB], 0 no; IDs masked)
    static readonly byte[] KmsVoteStart = Convert.FromHexString(
        "2F02010000008907E05F0000000002000000" +
        "01111111111111110222222222222222" +
        "01000000022222222222222200000000");

    // kms2: 0x53 mode 0x30, 10 bytes (PartyVoteReply: CharA votes yes; ID masked)
    static readonly byte[] KmsVoteReply = Convert.FromHexString("30011111111111111101");

    // kms2: 0x53 mode 0x31, 1 byte (PartyVoteEnd; bodyless 1-byte packet on both KMS2 and GMS2)
    static readonly byte[] KmsVoteEnd = Convert.FromHexString("31");

    // gms2: 0x54 mode 0x0B, 31 bytes (PartyInvite: 12-char name masked with '*', kind 0x0F40)
    static readonly byte[] Gms2Invite = Convert.FromHexString(
        "0B0C002A002A002A002A002A002A002A002A002A002A002A002A00400F0000");

    // gms2: 0x54 mode 0x13, 27 bytes (PartyMemberFlags: CharA, AcctB, maxHp 6644, hp 6200, tomb 0; IDs masked)
    static readonly byte[] Gms2MemberFlags = Convert.FromHexString(
        "1301111111111111110222222222222222F4190000381800000000");

    // gms2: 0x54 mode 0x2F, 50 bytes (PartyVoteStart Ready: id 5, 2 participants [CharA, CharB], 1 yes [CharA], 0 no; IDs masked)
    static readonly byte[] Gms2VoteStart = Convert.FromHexString(
        "2F02050000007581AB5E0000000002000000" +
        "01111111111111110222222222222222" +
        "01000000011111111111111100000000");

    // gms2: 0x54 mode 0x30, 10 bytes (PartyVoteReply: CharB votes yes; ID masked)
    static readonly byte[] Gms2VoteReply = Convert.FromHexString("30022222222222222201");

    // gms2: 0x54 mode 0x31, 1 byte (PartyVoteEnd; bodyless 1-byte packet on both KMS2 and GMS2)
    static readonly byte[] Gms2VoteEnd = Convert.FromHexString("31");

    // gms2: 0x54 mode 0x08, 9 bytes (PartyLeaderChange -> CharB; ID masked)
    static readonly byte[] Gms2LeaderChange = Convert.FromHexString("080222222222222222");

    // gms2: 0x54 mode 0x07, 1 byte (PartyDisband)
    static readonly byte[] Gms2Disband = Convert.FromHexString("07");

    static PartyBook SeedTwoMemberParty()
    {
        var book = new PartyBook();
        var info = new PartyInfo(
            Announce: false,
            PartyId: 100,
            LeaderId: CharA,
            Members: new[]
            {
                new PartyInfoMember(false, new PartyUser { CharacterId = CharA, Name = "Alice" }),
                new PartyInfoMember(false, new PartyUser { CharacterId = CharB, Name = "Bob" }),
            },
            Matchmaking: false,
            DungeonId: 0);
        book.Apply(info, CharA, windowOpen: true);
        return book;
    }

    [Test]
    public void KmsPartyModesUpdateInviteFlagsMatchmakingAndReadyVote()
    {
        var book = SeedTwoMemberParty();
        PartyInvite invited = null;
        var updated = new List<(long Id, int Flags)>();
        int startedVoteId = 0, endedVoteId = 0;
        book.Invited += inv => invited = inv;
        book.MemberUpdated += (m, f) => updated.Add((m.CharacterId, f));
        book.VoteStarted += v => startedVoteId = v.Id;
        book.VoteEnded += v => endedVoteId = v.Id;

        var inv = (PartyInvite)PartyMessage.Read(new ByteReader(KmsInvite), ProtocolProfile.Kms2525);
        book.Apply(inv, CharA, windowOpen: true);
        Assert.That(invited, Is.EqualTo(new PartyInvite("******", 0x5251)));
        Assert.That(book.PartyName, Is.EqualTo("******"));
        Assert.That(book.PartyKind, Is.EqualTo(0x5251));

        var flags = (PartyMemberFlags)PartyMessage.Read(new ByteReader(KmsMemberFlags), ProtocolProfile.Kms2525);
        book.Apply(flags, CharA, windowOpen: true);
        var memberA = book.Find(CharA);
        Assert.That(memberA.User.AccountId, Is.EqualTo(CharB));
        Assert.That(memberA.User.MaxHp, Is.EqualTo(5000));
        Assert.That(memberA.User.Hp, Is.EqualTo(5000));
        Assert.That(updated[^1], Is.EqualTo((CharA, 399)));

        var match = (PartyMatchDungeonChange)PartyMessage.Read(new ByteReader(KmsMatchDungeonChange), ProtocolProfile.Kms2525);
        book.Apply(match, CharA, windowOpen: true);
        Assert.That(book.Matchmaking, Is.True);
        Assert.That(book.DungeonId, Is.EqualTo(21_001_001));

        var reg = (PartyDungeonMatchRegister)PartyMessage.Read(new ByteReader(KmsDungeonMatchRegister), ProtocolProfile.Kms2525);
        book.Apply(reg, CharA, windowOpen: true);
        Assert.That(book.SearchBits, Is.EqualTo(2));
        Assert.That(book.IsMatchSearching(1), Is.True);

        var start = (PartyVoteStart)PartyMessage.Read(new ByteReader(KmsVoteStart), ProtocolProfile.Kms2525);
        book.Apply(start, CharA, windowOpen: true);
        Assert.That(startedVoteId, Is.EqualTo(1));
        Assert.That(book.Find(CharA).ReadyState, Is.EqualTo(1));
        Assert.That(book.Find(CharB).ReadyState, Is.EqualTo(2));

        var reply = (PartyVoteReply)PartyMessage.Read(new ByteReader(KmsVoteReply), ProtocolProfile.Kms2525);
        book.Apply(reply, CharA, windowOpen: true);
        Assert.That(book.Find(CharA).ReadyState, Is.EqualTo(2));
        Assert.That(book.Vote.Yes.Contains(CharA), Is.True);

        var end = (PartyVoteEnd)PartyMessage.Read(new ByteReader(KmsVoteEnd), ProtocolProfile.Kms2525);
        book.Apply(end, CharA, windowOpen: true);
        Assert.That(endedVoteId, Is.EqualTo(1));
        Assert.That(book.Vote, Is.Null);
    }

    [Test]
    public void Gms2PartyModesUpdateInviteFlagsVoteLeaderAndDisband()
    {
        var book = SeedTwoMemberParty();
        PartyInvite invited = null;
        (long Old, long New)? chief = null;
        int closed = 0, endedVoteId = 0;
        book.Invited += inv => invited = inv;
        book.ChiefChanged += (oldL, newL) => chief = (oldL.CharacterId, newL.CharacterId);
        book.Closed += () => closed++;
        book.VoteEnded += v => endedVoteId = v.Id;

        var inv = (PartyInvite)PartyMessage.Read(new ByteReader(Gms2Invite), ProtocolProfile.Gms2);
        book.Apply(inv, CharA, windowOpen: true);
        Assert.That(invited, Is.EqualTo(new PartyInvite("************", 0x0F40)));
        Assert.That(book.PartyName, Is.EqualTo("************"));
        Assert.That(book.PartyKind, Is.EqualTo(0x0F40));

        var flags = (PartyMemberFlags)PartyMessage.Read(new ByteReader(Gms2MemberFlags), ProtocolProfile.Gms2);
        book.Apply(flags, CharA, windowOpen: true);
        Assert.That(book.Find(CharA).User.AccountId, Is.EqualTo(CharB));
        Assert.That(book.Find(CharA).User.MaxHp, Is.EqualTo(6644));
        Assert.That(book.Find(CharA).User.Hp, Is.EqualTo(6200));

        var start = (PartyVoteStart)PartyMessage.Read(new ByteReader(Gms2VoteStart), ProtocolProfile.Gms2);
        book.Apply(start, CharA, windowOpen: true);
        Assert.That(book.Vote.Id, Is.EqualTo(5));
        Assert.That(book.Find(CharA).ReadyState, Is.EqualTo(2));
        Assert.That(book.Find(CharB).ReadyState, Is.EqualTo(1));

        var reply = (PartyVoteReply)PartyMessage.Read(new ByteReader(Gms2VoteReply), ProtocolProfile.Gms2);
        book.Apply(reply, CharA, windowOpen: true);
        Assert.That(book.Find(CharB).ReadyState, Is.EqualTo(2));
        Assert.That(book.Vote.Yes.Contains(CharB), Is.True);

        var end = (PartyVoteEnd)PartyMessage.Read(new ByteReader(Gms2VoteEnd), ProtocolProfile.Gms2);
        book.Apply(end, CharA, windowOpen: true);
        Assert.That(endedVoteId, Is.EqualTo(5));
        Assert.That(book.Vote, Is.Null);

        var leader = (PartyLeaderChange)PartyMessage.Read(new ByteReader(Gms2LeaderChange), ProtocolProfile.Gms2);
        book.Apply(leader, CharA, windowOpen: true);
        Assert.That(book.LeaderId, Is.EqualTo(CharB));
        Assert.That(book.IsLeader(CharB), Is.True);
        Assert.That(chief, Is.EqualTo((CharA, CharB)));

        var disband = (PartyDisband)PartyMessage.Read(new ByteReader(Gms2Disband), ProtocolProfile.Gms2);
        book.Apply(disband, CharA, windowOpen: true);
        Assert.That(book.IsInParty, Is.False);
        Assert.That(book.Members, Is.Empty);
        Assert.That(closed, Is.EqualTo(1));
    }
}
