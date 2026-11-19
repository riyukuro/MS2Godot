using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>RecvOp 0x1d, a user's chat line (ChatMessage) and the wedding hall block after a type 0x19 line.</summary>
public class ChatTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    static void Head(ByteWriter w, bool common, int type) {
        w.WriteLong(11); w.WriteLong(22); w.WriteUnicodeString("name"); w.WriteBool(common);
        if (common) w.WriteInt(0x35); else w.WriteUnicodeString("hi");
        w.WriteInt(type); w.WriteBool(true); w.WriteInt(1);
    }

    [Test]
    public void NormalLine() {
        var m = ChatMessage.Read(R(w => { Head(w, false, 0); w.WriteByte(1); }));
        Assert.That((m.AccountId, m.CharacterId, m.Name, m.Common, m.Text, m.Type, m.Gm, m.Gender, m.ShowImage),
            Is.EqualTo((11L, 22L, "name", false, "hi", 0, true, 1, (byte)1)));
    }

    [Test]
    public void CommonText() {
        var m = ChatMessage.Read(R(w => { Head(w, true, 0xd); w.WriteByte(0); }));
        Assert.That((m.Common, m.CommonId, m.Text, m.Type), Is.EqualTo((true, 0x35, "", 0xd)));
    }

    [Test]
    public void TypeBlocks() {
        var whisper = ChatMessage.Read(R(w => { Head(w, false, 3); w.WriteUnicodeString("win"); w.WriteByte(0); }));
        Assert.That(whisper.WhisperWindow, Is.EqualTo("win"));
        var club = ChatMessage.Read(R(w => { Head(w, false, 0x14); w.WriteLong(99); w.WriteByte(0); }));
        Assert.That(club.ClubId, Is.EqualTo(99));
        var super = ChatMessage.Read(R(w => { Head(w, false, 0x10); w.WriteInt(20300000); w.WriteByte(1); }));
        Assert.That((super.ItemId, super.ShowImage), Is.EqualTo((20300000, (byte)1)));
    }

    [Test]
    public void WeddingHallAfterTheLine() {
        var r = R(w => {
            Head(w, false, 0x19); w.WriteInt(5); w.WriteByte(0);
            w.WriteLong(1); w.WriteInt(2); w.WriteInt(3); w.WriteLong(4); w.WriteUnicodeString("a"); w.WriteUnicodeString("b");
        });
        var m = ChatMessage.Read(r);
        Assert.That(m.ItemId, Is.EqualTo(5));
        Assert.That(WeddingChatHall.Read(r), Is.EqualTo(new WeddingChatHall(1, 2, 3, 4, "a", "b")));
    }

    [Test]
    public void UserChatRequestBytes() {
        // FUN_1401fa700: kind, message, target, value
        var w = new ByteWriter();
        var p = new UserChatRequest(4, "hi", "Bob", 7);
        p.Write(w);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        Assert.That(p.Op, Is.EqualTo(Outbound.UserChat));
        Assert.That((int)Outbound.UserChat, Is.EqualTo(0x11));
        Assert.That((r.ReadInt(), r.ReadUnicodeString(), r.ReadUnicodeString(), r.ReadLong()), Is.EqualTo((4, "hi", "Bob", 7L)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
