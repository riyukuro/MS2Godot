using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The GroupChat packet (0x8B, CGroupChatManager::OnGroupChat, CGroupChat / CGroupChatMember::ReadFromPacket) and the
/// manager's sends (GroupChat 0x4C).</summary>
public class GroupChatPacketTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }
    static byte[] Str(string s) { var w = new ByteWriter(); w.WriteUnicodeString(s); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static byte[] Cat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();
    static readonly byte[] Oid7 = { 7, 0, 0, 0 };

    internal static GroupChatMember Member(string name, int state, long id) =>
        new() { State = state, User = new PartyUser { Name = name, CharacterId = id, AccountId = id + 100 } };

    [Test]
    public void TheRequestsWriteTheirModeAndFields() {
        Assert.That(new GroupChatCreateRequest("").Op, Is.EqualTo(Outbound.GroupChat));
        Assert.That((int)Outbound.GroupChat, Is.EqualTo(0x4C));
        Assert.That((int)Inbound.GroupChat, Is.EqualTo(0x8B));
        Assert.That(Bytes(new GroupChatCreateRequest("")), Is.EqualTo(Cat(new byte[] { 1 }, Str(""))));
        Assert.That(Bytes(new GroupChatInviteRequest("Ann", 7)), Is.EqualTo(Cat(new byte[] { 2 }, Str("Ann"), Oid7)));
        Assert.That(Bytes(new GroupChatLeaveRequest(7, 1)), Is.EqualTo(Cat(new byte[] { 4 }, Oid7, new byte[] { 1 })));
        Assert.That(Bytes(new GroupChatTalkRequest("hi", 7)), Is.EqualTo(Cat(new byte[] { 10 }, Str("hi"), Oid7)));
    }

    [Test]
    public void AChatReadsItsMembersByName() {
        // CGroupChat::ReadFromPacket: i32 oid, u8 count, members (u8 state, the user record for states 1 and 2)
        var r = R(w => {
            w.WriteByte(0); w.WriteInt(7); w.WriteByte(3);
            Member("Zed", 1, 2).Write(w);
            Member("Amy", 2, 3).Write(w);
            w.WriteByte(0);                                  // state 0: no record
        });
        var m = (GroupChatAdd)GroupChatMessage.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(m.Chat.Oid, Is.EqualTo(7));
        // the CRBMap walks by name; state 0's empty name sorts first
        Assert.That(m.Chat.Members.Select(x => x.Name), Is.EqualTo(new[] { "", "Amy", "Zed" }));
        Assert.That(m.Chat.JoinedCount, Is.EqualTo(1));
        Assert.That(m.Chat.JoinedMembers.Single().User.CharacterId, Is.EqualTo(2));
        Assert.That(m.Chat.OtherMembers.Select(x => x.Name), Is.EqualTo(new[] { "", "Amy" }));
        Assert.That(m.Chat.HasMemberById(3), Is.True);
        Assert.That(m.Chat.HasMemberByName("amy"), Is.False);
    }

    [Test]
    public void AChatOfMoreThanTwentyMembersFailsToRead() {
        // AddMember fails at 20 members, and ReadFromPacket with it
        var r = R(w => {
            w.WriteByte(0); w.WriteInt(7); w.WriteByte(21);
            for (int i = 0; i < 21; i++) Member("m" + i.ToString("00"), 1, i).Write(w);
        });
        Assert.That(((GroupChatAdd)GroupChatMessage.Read(r)).Chat, Is.Null);
    }

    [Test]
    public void TheModesReadTheirBodies() {
        GroupChatMessage Read(Action<ByteWriter> f) { var r = R(f); var m = GroupChatMessage.Read(r); Assert.That(r.Available, Is.EqualTo(0), m.ToString()); return m; }
        Assert.That(Read(w => { w.WriteByte(1); w.WriteInt(7); }), Is.EqualTo(new GroupChatCreated(7)));
        Assert.That(Read(w => { w.WriteByte(2); w.WriteUnicodeString("a"); w.WriteUnicodeString("b"); w.WriteInt(7); }), Is.EqualTo(new GroupChatInviteSent("a", "b", 7)));
        Assert.That(Read(w => { w.WriteByte(3); w.WriteUnicodeString("a"); w.WriteUnicodeString("b"); w.WriteInt(7); }), Is.EqualTo(new GroupChatInvited("a", "b", 7)));
        Assert.That(Read(w => { w.WriteByte(4); w.WriteInt(7); }), Is.EqualTo(new GroupChatRemove(7)));
        var add = (GroupChatMemberAdd)Read(w => { w.WriteByte(6); w.WriteInt(7); w.WriteUnicodeString("a"); Member("b", 1, 9).Write(w); });
        Assert.That((add.Oid, add.Name, add.Member.Name, add.Member.State), Is.EqualTo((7, "a", "b", 1)));
        Assert.That(Read(w => { w.WriteByte(7); w.WriteInt(7); w.WriteByte(1); w.WriteUnicodeString("a"); }), Is.EqualTo(new GroupChatMemberRemove(7, 1, "a")));
        Assert.That(Read(w => { w.WriteByte(8); w.WriteInt(7); w.WriteUnicodeString("a"); }), Is.EqualTo(new GroupChatMemberOnline(7, "a")));
        Assert.That(Read(w => { w.WriteByte(9); w.WriteInt(7); w.WriteUnicodeString("a"); }), Is.EqualTo(new GroupChatMemberOffline(7, "a")));
        Assert.That(Read(w => { w.WriteByte(10); w.WriteInt(7); w.WriteUnicodeString("a"); w.WriteUnicodeString("hi"); }), Is.EqualTo(new GroupChatTalk(7, "a", "hi")));
        var links = (GroupChatItemLinks)Read(w => { w.WriteByte(0xb); w.WriteInt(7); w.WriteUnicodeString("a"); w.WriteInt(0); });
        Assert.That((links.Oid, links.Name, links.Links.Items.Count), Is.EqualTo((7, "a", 0)));
        Assert.That(Read(w => { w.WriteByte(0xc); w.WriteInt(7); w.WriteUnicodeString("a"); w.WriteUnicodeString("p"); }), Is.EqualTo(new GroupChatMemberPicture(7, "a", "p")));
        Assert.That(Read(w => { w.WriteByte(0xd); w.WriteByte(1); w.WriteInt(10); w.WriteUnicodeString("a"); w.WriteUnicodeString("b"); }), Is.EqualTo(new GroupChatResult(1, 10, "a", "b")));
        Assert.That(Read(w => w.WriteByte(5)), Is.EqualTo(new GroupChatOtherMode(5)));
    }
}
