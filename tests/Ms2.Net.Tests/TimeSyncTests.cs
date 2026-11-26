using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class TimeSyncTests {
    // ApplyTimeSync sets the process-wide CMsTime zone; each test puts it back as it found it, so the order of tests does not matter.
    MsTime.State savedZone;
    [SetUp] public void SaveZone() => savedZone = MsTime.Save();
    [TearDown] public void RestoreZone() => MsTime.Restore(savedZone);

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
    public void ServerTimeFollowsTheTimeTheServerSent() {
        var s = new ClientSession(new MapleChannel(new MemoryStream()));
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(s.ServerTime, Is.InRange(now, now + 2), "before a sync: the local time");
        var w = new ByteWriter();
        w.WriteByte(1); w.WriteInt(0); w.WriteLong(1_000_000_000); w.WriteInt(0); w.WriteByte(0);
        s.ApplyTimeSync(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(s.ServerTime, Is.InRange(1_000_000_000L, 1_000_000_002L));
        w = new ByteWriter();
        w.WriteByte(2);
        s.ApplyTimeSync(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(s.ServerTime, Is.InRange(1_000_000_000L, 1_000_000_002L), "kind 2 sets no base");
    }

    [Test]
    public void Cmd2AsksForARequestAndSetsNoBase() {
        var s = new ClientSession(new MapleChannel(new MemoryStream()));
        Assert.That(s.ApplyTimeSync(new ByteReader(new byte[] { 2 })), Is.True);
        Assert.That(s.HasServerTick, Is.False);
    }

    [Test]
    public void TimeSyncSetsTheDateZone() {
        // kind 0 as the server writes it: tick, time, zone seconds (KST -32400), daylight flag, the request's sequence
        var w = new ByteWriter();
        w.WriteByte(0); w.WriteInt(1234); w.WriteLong(1_795_391_000); w.WriteInt(-32400); w.WriteByte(0); w.WriteInt(7);
        var s = new ClientSession(new MapleChannel(new MemoryStream()));
        // 2026-11-17 23:30 UTC: 11-17 with no zone, 11-18 in KST (GetLocalTm: gmtime(t - CalcZoneOffset))
        long t = new DateTimeOffset(2026, 11, 17, 23, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        MsTime.SetTimeZone(0, 0);
        Assert.That(MsTime.LocalTm(t).Day, Is.EqualTo(17));
        Assert.That(s.ApplyTimeSync(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray())), Is.False);
        Assert.That(MsTime.ZoneSeconds, Is.EqualTo(-32400));
        Assert.That(MsTime.LocalTm(t), Is.EqualTo(new DateTime(2026, 11, 18, 8, 30, 0)));
    }

    [Test]
    public void RestorePutsBackTheZoneIncludingNoneSet() {
        MsTime.Restore(new MsTime.State(false, 0, 0));
        var w = new ByteWriter();
        w.WriteByte(1); w.WriteInt(0); w.WriteLong(1_000_000_000); w.WriteInt(-32400); w.WriteByte(1);
        var before = MsTime.Save();
        new ClientSession(new MapleChannel(new MemoryStream())).ApplyTimeSync(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(MsTime.Save(), Is.EqualTo(new MsTime.State(true, -32400, 1)));
        MsTime.Restore(before);
        Assert.That((MsTime.ZoneSet, MsTime.ZoneSeconds, MsTime.DstHours), Is.EqualTo((false, 0, 0)));
    }

    [Test]
    public void ProfileDecidesTheInboundName() {
        var ch = new MapleChannel(new MemoryStream());
        Assert.That(Handshake.ClientVersion(ch.Profile), Is.EqualTo(2525u));
    }
}
