using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The ShadowExpedition packet (FieldGameScoreBoardCli::FUN_140f9c300 @0x140f9c300, uishadowexpeditionbossbar.cpp): the op and the three types.</summary>
public class FieldGameScoreBoardPacketTests
{
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    [Test]
    public void TheOpIsTheClientsRecvOpOnBothProfiles()
    {
        Assert.That((ushort)Inbound.ShadowExpedition, Is.EqualTo(0xE1));
        Assert.That(RcLane.Gms2Inbound(Inbound.ShadowExpedition), Is.EqualTo((ushort)0xE3));
    }

    [Test]
    public void TypeZeroOpensWithTheModeTheUnicodeTitleAndThePoints()
    {
        var p = FieldGameScoreBoardPacket.Read(R(w => { w.WriteByte(0); w.WriteByte(1); w.WriteUnicodeString("boss_title"); w.WriteInt(30); w.WriteInt(200); }));
        Assert.That(p, Is.EqualTo(new FieldGameScoreBoardPacket(FieldGameScoreBoardPacket.Open, 1, "boss_title", 30, 200)));
    }

    [Test]
    public void TypeOneSetsTheCurrentPoints()
    {
        var p = FieldGameScoreBoardPacket.Read(R(w => { w.WriteByte(1); w.WriteInt(77); }));
        Assert.That((p.Type, p.Cur), Is.EqualTo((FieldGameScoreBoardPacket.SetCurrent, 77)));
    }

    [Test]
    public void TypeTwoClosesAndReadsNothingMore()
    {
        var p = FieldGameScoreBoardPacket.Read(R(w => { w.WriteByte(2); w.WriteInt(5); }));
        Assert.That(p, Is.EqualTo(new FieldGameScoreBoardPacket(FieldGameScoreBoardPacket.Close, 0, "", 0, 0)));
    }

    [Test]
    public void AnotherTypeReadsNothing()
    {
        var p = FieldGameScoreBoardPacket.Read(R(w => { w.WriteByte(9); w.WriteInt(5); }));
        Assert.That((p.Type, p.Cur, p.Max, p.Title), Is.EqualTo(((byte)9, 0, 0, "")));
    }
}
