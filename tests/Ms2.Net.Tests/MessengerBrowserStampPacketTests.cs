using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class MessengerBrowserStampPacketTests
{
    [Test]
    public void OpcodeMatchesTheClientAndUpstreamTables()
    {
        // commonfiles/opcodes.h:297 RecvOp::MessengerBrowserStamp = 0x012C; upstream SendOp.MessengerBrowserStamp = 0x012E
        Assert.That((ushort)Inbound.MessengerBrowserStamp, Is.EqualTo(0x012C));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.MessengerBrowserStamp), Is.EqualTo((ushort)0x012E));
    }

    [Test]
    public void TheTypesAreUnsignedShortsAfterTheCount()
    {
        // OnMessengerBrowserStamp (scenestategame.cpp:6842-6855): u32 count, then (unsigned short)ReadInt16 each
        var w = new ByteWriter();
        w.WriteInt(3); w.WriteShort(0xb); w.WriteShort(0x10); w.WriteShort(unchecked((short)0xFFFF));
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        Assert.That(MessengerBrowserStamp.Read(r).HiddenTypes, Is.EqualTo(new[] { 0xb, 0x10, 0xFFFF }));
        Assert.That(r.Available, Is.EqualTo(0));
        var none = new ByteWriter();
        none.WriteInt(0);
        var r0 = new ByteReader(none.Buffer.AsSpan(0, none.Length).ToArray());
        Assert.That(MessengerBrowserStamp.Read(r0).HiddenTypes, Is.Empty);
        Assert.That(r0.Available, Is.EqualTo(0));
    }
}
