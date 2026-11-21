using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class ChatStickerPacketTests
{
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
