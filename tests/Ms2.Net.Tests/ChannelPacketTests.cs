using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Channel 0x3B (WorldService::FUN_141bc80c0, worldservice_1.cpp:4936) and DynamicChannel 0xA7 (CNetworkService::OnDynamicChannel, networkservice.cpp:977).</summary>
public class ChannelPacketTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }
    static byte[] Bytes(Ms2.Net.Wire.IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test] public void ChannelWritesOneInt16() {
        Assert.That((ushort) Outbound.Channel, Is.EqualTo(0x3B));
        Assert.That(new ChannelRequest(3).Op, Is.EqualTo(Outbound.Channel));
        Assert.That(Bytes(new ChannelRequest(3)), Is.EqualTo(new byte[] { 3, 0 }));
        Assert.That(Bytes(new ChannelRequest((short) 0x0102)), Is.EqualTo(new byte[] { 2, 1 }));
    }

    [Test] public void DynamicChannelKind1ReadsTheChannelList() {
        Assert.That((ushort) Inbound.DynamicChannel, Is.EqualTo(0xA7));
        var r = R(w => { w.WriteByte(1); w.WriteShort(3); w.WriteShort(1); w.WriteShort(2); w.WriteShort(unchecked((short) 40000)); });
        var m = (DynamicChannelMessage.Channels) DynamicChannelMessage.Read(r);
        Assert.That(m.Tabs, Is.EqualTo(new ushort[] { 1, 2, 40000 }));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void DynamicChannelKind0ReadsEightValues() {
        var r = R(w => { w.WriteByte(0); for (int i = 0; i < 8; i++) w.WriteShort((short) (i * 3)); });
        var m = (DynamicChannelMessage.ClassTable) DynamicChannelMessage.Read(r);
        Assert.That(m.Values, Is.EqualTo(new ushort[] { 0, 3, 6, 9, 12, 15, 18, 21 }));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void DynamicChannelOtherKindReadsNothing() {
        var r = R(w => { w.WriteByte(2); w.WriteShort(9); });
        Assert.That(DynamicChannelMessage.Read(r), Is.Null);
    }
}
