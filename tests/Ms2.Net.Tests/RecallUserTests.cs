using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// RecallUser: CSceneStateGame::OnRecallUser @0x141731c10 (scenestategame.cpp:4733) and CNetworkService::SendRecallUserReply /
/// SendRecallUserText (networkservice.cpp:522-528). No recording of this packet exists in either profile (the census of s2c 0x8C
/// and 0x8E finds none), so the bytes are built from the client's reads.
/// </summary>
public class RecallUserTests
{
    static byte[] Concat(params byte[][] parts) => parts.SelectMany(b => b).ToArray();
    static byte[] Str(string s) => Concat(BitConverter.GetBytes((ushort)s.Length), System.Text.Encoding.Unicode.GetBytes(s));
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static RecallUserPacket Read(params byte[][] parts) => RecallUserPacket.Read(new ByteReader(Concat(parts)));

    [Test]
    public void NumbersInBothProfiles()
    {
        Assert.That((ushort)Inbound.RecallUser, Is.EqualTo(0x8C));
        Assert.That((ushort)Outbound.RecallUser, Is.EqualTo(0x5D));
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.RecallUser), Is.EqualTo((ushort)0x8C));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.RecallUser), Is.EqualTo((ushort)0x8E));
        Assert.That(ProtocolProfile.Kms2525.Number(Outbound.RecallUser), Is.EqualTo((ushort)0x5D));
        Assert.That(ProtocolProfile.Gms2.Number(Outbound.RecallUser), Is.EqualTo((ushort)0x5E));
    }

    [Test]
    public void Mode4IsTheInvite()
    {
        // u8 mode 4, u8 type, wide name, i32 invite id
        var p = Read(new byte[] { 4, 9 }, Str("Caller"), BitConverter.GetBytes(0x1234));
        Assert.That(p, Is.EqualTo(new RecallUserPacket.Invite(9, "Caller", 0x1234)));
        Assert.That(p.Who, Is.EqualTo("Caller"));
        Assert.That(p.NoticeStringId, Is.EqualTo(0), "the invite is a box, not a chat line");
    }

    [Test]
    public void Mode5To7AreChatLinesByResult()
    {
        Assert.That(Read(new byte[] { 5 }, Str("A"), new byte[] { 0 }), Is.EqualTo(new RecallUserPacket.Progress("A", 0)));
        var progress = new[] { (0, 0x8fb), (1, 0x8fc), (2, 0x8fb), (3, 0x8fc), (4, 0), (255, 0) };
        foreach (var (result, id) in progress)
            Assert.That(Read(new byte[] { 5 }, Str("A"), new[] { (byte)result }).NoticeStringId, Is.EqualTo(id), $"mode 5 result {result}");
        var recalled = Read(new byte[] { 6 }, Str("B"));
        Assert.That((recalled, recalled.NoticeStringId, recalled.Who), Is.EqualTo((new RecallUserPacket.Recalled("B"), 0x8fd, "B")));
        var failure = new[] { (2, 0x8ff), (4, 0x901), (6, 0x8fe), (0, 0x900), (1, 0x900), (3, 0x900), (7, 0x900) };
        foreach (var (result, id) in failure)
            Assert.That(Read(new byte[] { 7 }, Str("C"), new[] { (byte)result }).NoticeStringId, Is.EqualTo(id), $"mode 7 result {result}");
    }

    [Test]
    public void AnotherModeReadsNothing()
    {
        var r = new ByteReader(new byte[] { 3, 1, 2, 3 });
        Assert.That(RecallUserPacket.Read(r), Is.EqualTo(new RecallUserPacket.Other(3)));
        Assert.That(r.Available, Is.EqualTo(3), "the client reads no further");
    }

    [Test]
    public void TheSendsAreTheClientsOwn()
    {
        // SendRecallUserReply: u8 4, i32 pending id, u8 accepted; SendRecallUserText: u8 7, wide name, u8 flag
        var yes = new RecallUserReply(0x1234, true);
        Assert.That(yes.Op, Is.EqualTo(Outbound.RecallUser));
        Assert.That(Bytes(yes), Is.EqualTo(Concat(new byte[] { 4 }, BitConverter.GetBytes(0x1234), new byte[] { 1 })));
        Assert.That(Bytes(new RecallUserReply(-5, false)), Is.EqualTo(Concat(new byte[] { 4 }, BitConverter.GetBytes(-5), new byte[] { 0 })));
        var busy = new RecallUserText("Caller", 4);
        Assert.That(busy.Op, Is.EqualTo(Outbound.RecallUser));
        Assert.That(Bytes(busy), Is.EqualTo(Concat(new byte[] { 7 }, Str("Caller"), new byte[] { 4 })));
    }
}
