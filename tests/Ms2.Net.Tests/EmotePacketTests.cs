using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class EmotePacketTests
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
        // commonfiles/opcodes.h:39 RecvOp::Emote = 0x001F, :325 SendOp::Emote = 0x0013
        Assert.That((ushort)Inbound.Emote, Is.EqualTo(0x001F));
        Assert.That((ushort)Outbound.Emote, Is.EqualTo(0x0013));
    }

    [Test]
    public void ListModeReadsEveryEntry()
    {
        // OnEmote mode 0 (scenestategame.cpp:2540-2553): count; i32 id, i32 level, bool, i64, i64, bool
        var r = Reader(w =>
        {
            w.WriteByte(0); w.WriteInt(2);
            w.WriteInt(90200011); w.WriteInt(1); w.WriteBool(true); w.WriteLong(0); w.WriteLong(5); w.WriteBool(false);
            w.WriteInt(90200022); w.WriteInt(0x10002); w.WriteBool(false); w.WriteLong(1700000000); w.WriteLong(7); w.WriteBool(true);
        });
        var m = EmoteMessage.Read(r) as EmoteMessage.List;
        Assert.That(m, Is.Not.Null);
        Assert.That(m.Entries, Has.Count.EqualTo(2));
        Assert.That(m.Entries[0], Is.EqualTo(new EmoteEntry(90200011, 1, true, 0, 5, false)));
        // the level is read as i32 and kept as u16 (unsigned short nLevel = ReadInt32())
        Assert.That(m.Entries[1], Is.EqualTo(new EmoteEntry(90200022, 2, false, 1700000000, 7, true)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void Gms2ListEntriesAreSixteenBytes()
    {
        // GMS2 (recordings, upstream EmotePacket.Load): count; i32 id, i32 level, i64 expiry
        byte[] Frame() { var w = new ByteWriter(); w.WriteByte(0); w.WriteInt(2); w.WriteInt(90200011); w.WriteInt(1); w.WriteLong(0); w.WriteInt(90200022); w.WriteInt(3); w.WriteLong(1700000000); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
        var r = new ByteReader(Frame());
        var m = EmoteMessage.Read(r, ProtocolProfile.Gms2) as EmoteMessage.List;
        Assert.That(m.Entries, Has.Count.EqualTo(2));
        Assert.That(m.Entries[0], Is.EqualTo(new EmoteEntry(90200011, 1, false, 0, 0, false)));
        Assert.That(m.Entries[1], Is.EqualTo(new EmoteEntry(90200022, 3, false, 1700000000, 0, false)));
        Assert.That(r.Available, Is.EqualTo(0));
        // the KMS reader runs past the end of the same frame
        Assert.Catch<Exception>(() => EmoteMessage.Read(new ByteReader(Frame())));
    }

    [Test]
    public void Gms2LearnIsOneSixteenByteEntry()
    {
        var w = new ByteWriter(); w.WriteByte(1); w.WriteInt(90200033); w.WriteInt(2); w.WriteLong(99);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        Assert.That((EmoteMessage.Read(r, ProtocolProfile.Gms2) as EmoteMessage.Learn).Entry, Is.EqualTo(new EmoteEntry(90200033, 2, false, 99, 0, false)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void EveryRecordedGms2EmotePacketDecodes()
    {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.Emote));
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, data) in all)
        {
            var r = new ByteReader(data);
            Assert.That(EmoteMessage.Read(r, ProtocolProfile.Gms2), Is.Not.Null, file);
            Assert.That(r.Available, Is.EqualTo(0), file);
        }
    }

    [Test]
    public void LearnModeHasNoFlags()
    {
        // OnEmote mode 1 (:2555-2561): i32 id, i32 level, i64, i64; AddEmotion(.., false, false, 1)
        var r = Reader(w => { w.WriteByte(1); w.WriteInt(90200033); w.WriteInt(3); w.WriteLong(99); w.WriteLong(42); });
        var m = EmoteMessage.Read(r) as EmoteMessage.Learn;
        Assert.That(m, Is.Not.Null);
        Assert.That(m.Entry, Is.EqualTo(new EmoteEntry(90200033, 3, false, 99, 42, false)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void ResultModeReadsOneByteAndOtherModesAreNull()
    {
        // OnEmote mode 3 (:2563-2569): u8 result
        var m = EmoteMessage.Read(Reader(w => { w.WriteByte(3); w.WriteByte(1); })) as EmoteMessage.Result;
        Assert.That(m, Is.Not.Null);
        Assert.That(m.Code, Is.EqualTo(1));
        Assert.That(EmoteMessage.Read(Reader(w => { w.WriteByte(2); w.WriteInt(5); })), Is.Null);
    }

    [Test]
    public void LearnRequestWritesModeAndItemUid()
    {
        // Unk_Inventory::SendEmotion (@0x140a65bb0, inventory.cpp:1622-1624): Encode1(1), Encode8(item uid)
        var req = new EmoteLearnRequest(0x0102030405060708);
        Assert.That(req.Op, Is.EqualTo(Outbound.Emote));
        Assert.That(BytesOf(req), Is.EqualTo(new byte[] { 1, 8, 7, 6, 5, 4, 3, 2, 1 }));
    }

    [Test]
    public void UseRequestWritesSkillIdAndParam()
    {
        // StateSkill (stateskill.cpp:1906-1909): Encode1(2), Encode4(skill id), WriteUnicodeStringA(param)
        var req = new EmoteUseRequest(0x01020304, "ab");
        Assert.That(req.Op, Is.EqualTo(Outbound.Emote));
        Assert.That(BytesOf(req), Is.EqualTo(new byte[] { 2, 4, 3, 2, 1, 2, 0, (byte)'a', 0, (byte)'b', 0 }));
    }

    [Test]
    public void FavoriteRequestWritesIdLevelFlag()
    {
        // onFavorite (uidynamicactiondialog.cpp:369-373): u8 4, i32 id, i32 level, bool
        var req = new EmoteFavoriteRequest(0x01020304, 2, true);
        Assert.That(req.Op, Is.EqualTo(Outbound.Emote));
        Assert.That(BytesOf(req), Is.EqualTo(new byte[] { 4, 4, 3, 2, 1, 2, 0, 0, 0, 1 }));
    }
}
