using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>A party member's counters (CPartyMember::counters, Unk_LockedIntMap): modes 2, 9 and 0xe keep them (partymanager.cpp:1003, :1121, :1234).</summary>
public class PartyMemberCountersTests {
    static PartyUser User(long id) => new() { AccountId = id + 100, CharacterId = id, Name = "n" + id, MapId = 2000001, Level = 30, Channel = 1 };
    static PartyMessage Read(byte[] body) { var r = new ByteReader(body); var m = PartyMessage.Read(r); Assert.That(r.Available, Is.Zero); return m; }
    static IReadOnlyDictionary<int, int> Map(params (int, int)[] e) => e.ToDictionary(x => x.Item1, x => x.Item2);

    [Test]
    public void AJoinKeepsTheMembersCounters()
    {
        var join = (PartyJoin)Read(new PartyJoin(User(2)) { Counters = Map((21001001, 10), (21001002, 3)) }.Write());
        var book = new PartyBook();
        book.Apply(new PartyInfo(false, 1, 1, new[] { new PartyInfoMember(false, User(1)) }, false, 0), 1, false);
        book.Apply(join, 1, true);
        Assert.That(book.Find(2).Counter(21001001), Is.EqualTo(10));
        Assert.That(book.Find(2).Counter(21001002), Is.EqualTo(3));
        Assert.That(book.Find(2).Counter(5), Is.Zero, "a key the map lacks is 0 (Unk_LockedIntMap::Get)");
    }

    [Test]
    public void ThePartyInfoKeepsEachMembersCounters()
    {
        var info = (PartyInfo)Read(new PartyInfo(false, 9, 1, new[] {
            new PartyInfoMember(false, User(1)) { Counters = Map((7, 1)) }, new PartyInfoMember(true, User(2)) { Counters = Map((7, 12), (8, 2)) } }, false, 0).Write());
        var book = new PartyBook();
        book.Apply(info, 1, false);
        Assert.That(book.Find(1).Counter(7), Is.EqualTo(1));
        Assert.That(book.Find(2).Counter(7), Is.EqualTo(12));
        Assert.That(book.Find(2).Counter(8), Is.EqualTo(2));
    }

    [Test]
    public void ACountersPacketReplacesTheWholeMap()
    {
        var book = new PartyBook();
        book.Apply(new PartyInfo(false, 1, 1, new[] { new PartyInfoMember(false, User(1)) { Counters = Map((7, 12), (8, 2)) }, new PartyInfoMember(false, User(2)) }, false, 0), 1, false);
        var m = (PartyMemberCounters)Read(new PartyMemberCounters(1) { Counters = Map((9, 4)) }.Write());
        book.Apply(m, 1, true);
        Assert.That(book.Find(1).Counter(7), Is.Zero, "SetCounters assigns the map (Unk_LockedIntMap::Assign)");
        Assert.That(book.Find(1).Counter(9), Is.EqualTo(4));
        book.Apply(new PartyMemberCounters(99) { Counters = Map((9, 4)) }, 1, true);   // no such member: nothing
        Assert.That(book.Find(2).Counter(9), Is.Zero);
    }

    [Test]
    public void ALaterEntryOfAKeyReplacesTheEarlier()
    {
        var body = new ByteWriter();
        body.WriteByte(0xe); body.WriteLong(1); body.WriteInt(2); body.WriteInt(5); body.WriteByte(1); body.WriteInt(5); body.WriteByte(11);
        var m = (PartyMemberCounters)Read(body.Buffer.AsSpan(0, body.Length).ToArray());
        Assert.That(m.Counters, Has.Count.EqualTo(1));
        Assert.That(m.Counters[5], Is.EqualTo(11));
    }
}
