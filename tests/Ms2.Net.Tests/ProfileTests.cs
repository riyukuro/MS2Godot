using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class ProfileTests {
    [Test]
    public void UserMoveByPortalDecodes() {
        var w = new ByteWriter();
        w.WriteInt(10000009); w.WriteFloat(150); w.WriteFloat(-300); w.WriteFloat(175); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(90); w.WriteBool(false);
        var m = UserMoveByPortal.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(m, Is.EqualTo(new UserMoveByPortal(10000009, new Vector3(150, -300, 175), new Vector3(0, 0, 90), false)));
    }

    [Test]
    public void KmsProfileCarriesTheVersionAndTheEnumNumbers() {
        var p = ProtocolProfile.Kms2525;
        Assert.That(p.Version, Is.EqualTo(2525u));
        Assert.That(p.Number(Outbound.UserSync), Is.EqualTo((ushort) 0x12));
        Assert.That(p.Number(Inbound.UserMoveByPortal), Is.EqualTo((ushort) 0x5F));
        Assert.That(p.TryInbound(0x14, out var op) && op == Inbound.SyncNumber);
    }

    [Test]
    public async Task ChannelSendsThroughTheProfileNumbers() {
        var srv = new FakeServer(1, 2);
        var io = new MemoryStream(); io.Write(srv.HelloFrame); io.Position = 0;
        var ch = new Wire.MapleChannel(io);
        await ch.ReadHandshakeAsync();
        var map = Enum.GetValues<Outbound>().ToDictionary(o => o, o => (ushort) o);
        map[Outbound.UserSync] = 0x77;
        ch.Profile = new ProtocolProfile("test", 2525, Enum.GetValues<Inbound>().ToDictionary(o => o, o => (ushort) o), map);
        long start = io.Length;
        await ch.SendAsync(Outbound.UserSync, w => w.WriteByte(1));
        var all = io.ToArray().AsSpan((int) start).ToArray();
        Assert.That(srv.Read(all).op, Is.EqualTo((ushort) 0x77));
    }
}
