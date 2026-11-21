using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class ChatStickerPacketTests
{
    static byte[] BytesOf(Ms2.Net.Wire.IOutPacket p)
    {
        var w = new ByteWriter();
        p.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    static ByteReader Reader(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test]
    public void OpcodesMatchClientTable()
    {
        // commonfiles/opcodes.h:293 RecvOp::ChatStamp = 0x0126, :485 SendOp::ChatSticker = 0x00B7
        Assert.That((ushort)Inbound.ChatStamp, Is.EqualTo(0x0126));
        Assert.That((ushort)Outbound.ChatSticker, Is.EqualTo(0x00B7));
    }

    [Test]
    public void ListReadsFavoritesThenGroups()
    {
        var r = Reader(w =>
        {
            w.WriteByte(0);
            w.WriteShort(2); w.WriteInt(11); w.WriteInt(12);
            w.WriteShort(1); w.WriteInt(5); w.WriteLong(long.MaxValue);
        });
        var m = (ChatStampMessage.List)ChatStampMessage.Read(r);
        Assert.That(m.Favorites, Is.EqualTo(new uint[] { 11, 12 }));
        Assert.That(m.Groups, Is.EqualTo(new[] { new ChatEmoticonGroup(5, long.MaxValue) }));
    }

    [Test]
    public void ExpiredAndAddedRead()
    {
        var e = (ChatStampMessage.Expired)ChatStampMessage.Read(Reader(w =>
        {
            w.WriteByte(1); w.WriteInt(2); w.WriteInt(3); w.WriteInt(4); w.WriteInt(1); w.WriteInt(9);
        }));
        Assert.That(e.Groups, Is.EqualTo(new uint[] { 3, 4 }));
        Assert.That(e.Favorites, Is.EqualTo(new uint[] { 9 }));
        var a = (ChatStampMessage.Added)ChatStampMessage.Read(Reader(w =>
        {
            w.WriteByte(2); w.WriteInt(20300001); w.WriteInt(1); w.WriteInt(7); w.WriteLong(1700000000);
        }));
        Assert.That(a.ItemId, Is.EqualTo(20300001));
        Assert.That(a.Groups, Is.EqualTo(new[] { new ChatEmoticonGroup(7, 1700000000) }));
    }

    [Test]
    public void SmallModesRead()
    {
        var t = (ChatStampMessage.Typed)ChatStampMessage.Read(Reader(w => { w.WriteByte(3); w.WriteInt(8); w.WriteUnicodeString("win"); w.WriteByte(1); }));
        Assert.That((t.Id, t.Window, t.Flag), Is.EqualTo((8u, "win", (byte)1)));
        var g = (ChatStampMessage.GroupSent)ChatStampMessage.Read(Reader(w => { w.WriteByte(4); w.WriteInt(8); w.WriteUnicodeString("grp"); }));
        Assert.That((g.Id, g.Window), Is.EqualTo((8u, "grp")));
        Assert.That(((ChatStampMessage.MovedToEnd)ChatStampMessage.Read(Reader(w => { w.WriteByte(5); w.WriteInt(3); }))).Id, Is.EqualTo(3u));
        Assert.That(((ChatStampMessage.Removed)ChatStampMessage.Read(Reader(w => { w.WriteByte(6); w.WriteInt(3); }))).Id, Is.EqualTo(3u));
        Assert.That(((ChatStampMessage.Result)ChatStampMessage.Read(Reader(w => { w.WriteByte(7); w.WriteByte(2); }))).Code, Is.EqualTo((byte)2));
    }

    [Test]
    public void TrailingBytesAndUnknownType()
    {
        Assert.That(() => ChatStampMessage.Read(Reader(w => { w.WriteByte(6); w.WriteInt(3); w.WriteByte(0); })), Throws.Exception);
        Assert.That(ChatStampMessage.Read(Reader(w => { w.WriteByte(99); })), Is.Null);
    }

    [Test]
    public void RequestsWriteClientLayout()
    {
        Assert.That(BytesOf(new ChatStickerListRequest(new uint[] { 7 })), Is.EqualTo(new byte[] { 1, 1, 0, 0, 0, 7, 0, 0, 0 }));
        Assert.That(BytesOf(new ChatStickerListRequest(Array.Empty<uint>())), Is.EqualTo(new byte[] { 1, 0, 0, 0, 0 }));
        Assert.That(BytesOf(ChatStickerRequest.SetFavorite(5, true)), Is.EqualTo(new byte[] { 5, 5, 0, 0, 0 }));
        Assert.That(BytesOf(ChatStickerRequest.SetFavorite(5, false)), Is.EqualTo(new byte[] { 6, 5, 0, 0, 0 }));
        Assert.That(BytesOf(ChatStickerRequest.Send(5, "a", 2)), Is.EqualTo(new byte[] { 3, 5, 0, 0, 0, 1, 0, (byte)'a', 0, 2 }));
    }

    static ByteReader Bytes(ChatStickerRequest p, out int length)
    {
        var w = new ByteWriter();
        p.Write(w);
        length = w.Length;
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test]
    public void OpcodeMatchesClientTable()
    {
        // commonfiles/opcodes.h:485 SendOp::ChatSticker = 0x00B7
        Assert.That((ushort)Outbound.ChatSticker, Is.EqualTo(0x00B7));
        Assert.That(ChatStickerRequest.Send(1, "w", 0).Op, Is.EqualTo(Outbound.ChatSticker));
    }

    [Test]
    public void SendWritesModeIdWindowAndChatType()
    {
        // SendEmoticon (uimessenger.cpp:1416-1436): Encode1 3, Encode4 id, EncodeStr name, Encode1 chat type
        var r = Bytes(ChatStickerRequest.Send(0x1234, "messengerdialog0.xml", 9), out int length);
        Assert.That(length, Is.EqualTo(1 + 4 + 2 + 2 * "messengerdialog0.xml".Length + 1));
        Assert.That(r.ReadByte(), Is.EqualTo(3));
        Assert.That(r.ReadInt(), Is.EqualTo(0x1234));
        Assert.That(r.ReadUnicodeString(), Is.EqualTo("messengerdialog0.xml"));
        Assert.That(r.ReadByte(), Is.EqualTo(9));
    }

    [TestCase(true, 5)]
    [TestCase(false, 6)]
    public void FavoriteWritesModeAndIdOnly(bool favorite, int mode)
    {
        // SetEmoticonFavorite (uimessenger.cpp:1477-1497): Encode1 5 or 6, Encode4 id
        var r = Bytes(ChatStickerRequest.SetFavorite(77, favorite), out int length);
        Assert.That(length, Is.EqualTo(5));
        Assert.That(r.ReadByte(), Is.EqualTo(mode));
        Assert.That(r.ReadInt(), Is.EqualTo(77));
    }
}
