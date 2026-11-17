using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class TimeSyncTests {
    static ByteReader Body(byte cmd, int time) {
        var w = new ByteWriter();
        w.WriteByte(cmd); w.WriteInt(time); w.WriteLong(0); w.WriteInt(0); w.WriteByte(0);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test]
    public void ServerTickFollowsTheTickTheServerSent() {
        var s = new ClientSession(new MapleChannel(new MemoryStream()));
        Assert.That(s.HasServerTick, Is.False);
        Assert.That(s.ApplyTimeSync(Body(1, 5_000_000)), Is.True, "cmd 1 asks for a request back");
        Assert.That(s.HasServerTick);
        Assert.That(s.ServerTick, Is.InRange(5_000_000, 5_000_000 + 200));
        Thread.Sleep(60);
        Assert.That(s.ServerTick - 5_000_000, Is.GreaterThanOrEqualTo(55));
    }

    [Test]
    public void Cmd2AsksForARequestAndSetsNoBase() {
        var s = new ClientSession(new MapleChannel(new MemoryStream()));
        Assert.That(s.ApplyTimeSync(new ByteReader(new byte[] { 2 })), Is.True);
        Assert.That(s.HasServerTick, Is.False);
    }

    [Test]
    public void ProfileDecidesTheInboundName() {
        var ch = new MapleChannel(new MemoryStream());
        Assert.That(Handshake.ClientVersion(ch.Profile), Is.EqualTo(2525u));
    }
}
