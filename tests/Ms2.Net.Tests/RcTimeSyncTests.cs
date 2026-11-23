using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>ResponseTimeSync (0x11): KMS command 1 is tick, server time, zone and flag (18 B) and passes today; command 2
/// is the one-byte request. GMS2 mode 0 leaves the trailing i32 sequence, mode 2 leaves the 17-byte body and mode 3 is not
/// read; the 22-byte KMS body leaves the same sequence. Layouts: the GMS2 packet layout notes §B1.</summary>
public class RcTimeSyncTests {
    // gms2 recording: packet 2246, 22 bytes
    static readonly byte[] Gms2Cmd0 = RcPkt.Hex(
        "00CB89E006B049A25E0000000000000000004F330000");
    // gms2 recording: packet 2332, 18 bytes
    static readonly byte[] Gms2Cmd2 = RcPkt.Hex(
        "0249A3E006B749A25E000000000000000000");
    // gms2 recording: packet 19, 9 bytes
    static readonly byte[] Gms2Cmd3 = RcPkt.Hex(
        "03B049A25E00000000");
    // gms2 recording: packet 2241, 22 bytes
    static readonly byte[] Gms2Cmd0b = RcPkt.Hex(
        "0073DA633EDBF6BA5E00000000000000000019000000");
    // gms2 recording: packet 2305, 18 bytes
    static readonly byte[] Gms2Cmd2b = RcPkt.Hex(
        "0202E6633EDEF6BA5E000000000000000000");
    // gms2 recording: packet 19, 9 bytes
    static readonly byte[] Gms2Cmd3b = RcPkt.Hex(
        "03FB70A25E00000000");
    // gms2 recording: packet 18, 18 bytes
    static readonly byte[] Gms2Cmd1 = RcPkt.Hex(
        "019088E006B049A25E000000000000000000");
    // kms2 recording: packet 558, 22 bytes
    static readonly byte[] KmsCmd0 = RcPkt.Hex(
        "00581788729839F666000000007081FFFF00D0370000");
    // kms2 recording: packet 25, 18 bytes
    static readonly byte[] KmsCmd1 = RcPkt.Hex(
        "01CB1588729739F666000000007081FFFF00");
    [Test] public void Gms2Command1ReadsTheTickAndZone() {
        var session = new ClientSession(new MapleChannel(new MemoryStream()));
        var r = new ByteReader(Gms2Cmd1);
        try {
            MsTime.SetTimeZone(0, 0);
            Assert.That(session.ApplyTimeSync(r), Is.True);
            Assert.That(session.ServerTime, Is.EqualTo(1587694000));
            Assert.That(MsTime.ZoneSeconds, Is.EqualTo(0));
            Assert.That(r.Available, Is.EqualTo(0));
        } finally { MsTime.SetTimeZone(0, 0); }
    }

    [Test] public void KmsCommand1ReadsTheTickAndZone() {
        var session = new ClientSession(new MapleChannel(new MemoryStream()));
        var r = new ByteReader(KmsCmd1);
        try {
            MsTime.SetTimeZone(0, 0);
            Assert.That(session.ApplyTimeSync(r), Is.True);
            Assert.That(session.ServerTime, Is.EqualTo(1727412631));
            Assert.That(MsTime.ZoneSeconds, Is.EqualTo(-32400));
            Assert.That(r.Available, Is.EqualTo(0));
        } finally { MsTime.SetTimeZone(0, 0); }
    }

    [Test] public void Gms2Command0ReadsTheLatencySequence() {
        foreach (var (bytes, serverTime) in new[] { (Gms2Cmd0, 1587694000L), (Gms2Cmd0b, 1589311195L) }) {
            var session = new ClientSession(new MapleChannel(new MemoryStream()));
            var r = new ByteReader(bytes);
            try {
                MsTime.SetTimeZone(0, 0);
                session.ApplyTimeSync(r);
                Assert.That(session.ServerTime, Is.EqualTo(serverTime));
                Assert.That(r.Available, Is.EqualTo(0), "the trailing i32 latency sequence is left unread");
            } finally { MsTime.SetTimeZone(0, 0); }
        }
    }

    [Test] public void KmsCommand0ReadsTheLatencySequence() {
        var session = new ClientSession(new MapleChannel(new MemoryStream()));
        var r = new ByteReader(KmsCmd0);
        try {
            MsTime.SetTimeZone(0, 0);
            session.ApplyTimeSync(r);
            Assert.That(session.ServerTime, Is.EqualTo(1727412632));
            Assert.That(MsTime.ZoneSeconds, Is.EqualTo(-32400));
            Assert.That(r.Available, Is.EqualTo(0), "the trailing i32 latency sequence is left unread");
        } finally { MsTime.SetTimeZone(0, 0); }
    }

    [Test] public void Gms2Command2IgnoresTheBody() {
        foreach (var bytes in new[] { Gms2Cmd2, Gms2Cmd2b }) {
            var session = new ClientSession(new MapleChannel(new MemoryStream()));
            var r = new ByteReader(bytes);
            Assert.That(session.ApplyTimeSync(r), Is.True);
            Assert.That(session.HasServerTick, Is.False);
            Assert.That(r.Available, Is.EqualTo(0), "cmd 2 reads nothing and must ignore the 17-byte body");
        }
    }

    [Test] public void Gms2Command3ReadsTheServerTime() {
        foreach (var (bytes, serverTime) in new[] { (Gms2Cmd3, 1587694000L), (Gms2Cmd3b, 1587704059L) }) {
            var session = new ClientSession(new MapleChannel(new MemoryStream()));
            var r = new ByteReader(bytes);
            long before = session.ServerTime;
            session.ApplyTimeSync(r);
            Assert.That(session.SetTime, Is.EqualTo(serverTime), "cmd 3 sets m_tA0, not the server clock (FUN_141473530)");
            Assert.That(session.ServerTime, Is.EqualTo(before));
            Assert.That(r.Available, Is.EqualTo(0), "cmd 3 carries an i64 time");
        }
    }
}
