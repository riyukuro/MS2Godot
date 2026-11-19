using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The Buddy packet (0x61, CBuddyManager::OnBuddy and CBuddy::ReadFromPacket) and the friend dialogs' sends (Buddy 0x30,
/// EventReward 0x97).</summary>
public class BuddyPacketTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }
    static byte[] Str(string s) { var w = new ByteWriter(); w.WriteUnicodeString(s); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static byte[] Cat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();
    static readonly byte[] Id5 = { 5, 0, 0, 0, 0, 0, 0, 0 };

    [Test]
    public void TheRequestsWriteTheirProtocolAndFields() {
        Assert.That(BuddyRequest.Invite("a", "b").Op, Is.EqualTo(Outbound.Buddy));
        Assert.That(Bytes(BuddyRequest.Invite("Ann", "hi")), Is.EqualTo(Cat(new byte[] { 2 }, Str("Ann"), Str("hi"))));
        Assert.That(Bytes(BuddyRequest.Accept(5)), Is.EqualTo(Cat(new byte[] { 3 }, Id5)));
        Assert.That(Bytes(BuddyRequest.Decline(5)), Is.EqualTo(Cat(new byte[] { 4 }, Id5)));
        Assert.That(Bytes(BuddyRequest.Block(5, "Bo", "m")), Is.EqualTo(Cat(new byte[] { 5 }, Id5, Str("Bo"), Str("m"))));
        Assert.That(Bytes(BuddyRequest.Unblock(5)), Is.EqualTo(Cat(new byte[] { 6 }, Id5)));
        Assert.That(Bytes(BuddyRequest.Remove(5)), Is.EqualTo(Cat(new byte[] { 7 }, Id5)));
        Assert.That(Bytes(BuddyRequest.EditMemo(5, "Bo", "m")), Is.EqualTo(Cat(new byte[] { 10 }, Id5, Str("Bo"), Str("m"))));
        Assert.That(Bytes(BuddyRequest.Cancel(5)), Is.EqualTo(Cat(new byte[] { 0x11 }, Id5)));
        Assert.That(Bytes(new BuddyAutoRejectRequest(true)), Is.EqualTo(new byte[] { 20, 1, 0, 0, 0 }));
        Assert.That(new EventRewardRequest(5, 1).Op, Is.EqualTo(Outbound.EventReward));
        Assert.That(Bytes(new EventRewardRequest(5, 1)), Is.EqualTo(new byte[] { 5, 1 }));
    }

    static void WriteEntry(ByteWriter w, long id, long character, string name, byte received, byte waiting, byte blocked, byte online) {
        w.WriteLong(id); w.WriteLong(character); w.WriteLong(900);
        w.WriteUnicodeString(name); w.WriteUnicodeString("msg");
        w.WriteShort(3); w.WriteInt(2000062); w.WriteInt(2); w.WriteInt(0); w.WriteShort(40);
        w.WriteByte(received); w.WriteByte(waiting); w.WriteByte(blocked); w.WriteByte(online); w.WriteByte(0);
        w.WriteInt(0); w.WriteInt(0);
        w.WriteUnicodeString("pic"); w.WriteUnicodeString("today"); w.WriteUnicodeString("memo");
        w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); w.WriteUnicodeString("");
        w.WriteLong(0);
        w.WriteInt(1); w.WriteInt(2); w.WriteInt(3);
    }

    [Test]
    public void Protocol1ReadsEachEntry() {
        var r = R(w => { w.WriteByte(1); w.WriteInt(2); WriteEntry(w, 10, 100, "Ann", 1, 0, 0, 1); WriteEntry(w, 11, 101, "Bo", 0, 0, 1, 0); });
        var m = BuddyMessage.Read(r);
        Assert.That(r.Available, Is.Zero, "the whole body is read");
        Assert.That(m.Entries.Select(e => (e.Id, e.CharacterId, e.Name, e.Received, e.Blocked, e.Online)),
            Is.EqualTo(new[] { (10L, 100L, "Ann", true, false, true), (11L, 101L, "Bo", false, true, false) }));
        var a = m.Entries[0];
        Assert.That((a.Channel, a.MapId, a.Job, a.Level, a.ProfilePath, a.TodayMemo, a.Memo, a.TrophyTotal),
            Is.EqualTo((3, 2000062, 20, 40, "pic", "today", "memo", 6)), "job code 0 gives the group x 10");
        Assert.That((a.IsFriend, m.Entries[1].IsFriend), Is.EqualTo((false, false)));
    }

    [Test]
    public void TheHeaderProtocolsReadTheirFields() {
        var invite = BuddyMessage.Read(R(w => { w.WriteByte(2); w.WriteByte(4); w.WriteUnicodeString("Ann"); w.WriteUnicodeString("hi"); }));
        Assert.That((invite.Result, invite.Name, invite.Memo), Is.EqualTo((4, "Ann", "hi")));
        var block = BuddyMessage.Read(R(w => { w.WriteByte(5); w.WriteByte(0); w.WriteLong(7); w.WriteUnicodeString("Bo"); w.WriteUnicodeString("m"); }));
        Assert.That((block.Result, block.Id, block.Name, block.Memo), Is.EqualTo((0, 7L, "Bo", "m")));
        var removed = BuddyMessage.Read(R(w => { w.WriteByte(7); w.WriteByte(0); w.WriteLong(7); w.WriteLong(8); w.WriteLong(9); w.WriteUnicodeString("Cy"); }));
        Assert.That((removed.Id, removed.Value30, removed.Value08, removed.Name), Is.EqualTo((7L, 8L, 9L, "Cy")));
        var online = BuddyMessage.Read(R(w => { w.WriteByte(0xe); w.WriteByte(1); w.WriteLong(7); w.WriteUnicodeString("Dee"); }));
        Assert.That((online.Flag, online.Id, online.Name), Is.EqualTo(((byte)1, 7L, "Dee")));
        var r12 = BuddyMessage.Read(R(w => { w.WriteByte(0xc); w.WriteByte(2); w.WriteUnicodeString("E"); }));
        Assert.That((r12.Result, r12.Name), Is.EqualTo((2, "E")));
        var answers = BuddyMessage.Read(R(w => { w.WriteByte(0xd); w.WriteInt(1); w.WriteUnicodeString("F"); w.WriteUnicodeString("approved"); w.WriteLong(3); }));
        Assert.That(answers.Answers.Single(), Is.EqualTo(("F", "approved", 3L)));
        Assert.That(BuddyMessage.Read(R(w => w.WriteByte(0x99))).Protocol, Is.EqualTo(0x99));
    }
}
