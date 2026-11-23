using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>ResponseTimeSync (0x11) in the KMS2 layout the session reads: command 1 is a tick, the server time, the zone
/// offset and a flag (18 bytes); command 2 is the one-byte "answer me" form. The GMS2 bodies leave bytes unread and are left open.
/// </summary>
public class TimeSyncRecordedTests {
    // kms2 recording: packet 25, 18 bytes (names masked with '*' by the masking pass).
    static readonly byte[] KmsFull = new byte[] {
        0x01, 0xCB, 0x15, 0x88, 0x72, 0x97, 0x39, 0xF6, 0x66, 0x00, 0x00, 0x00, 0x00, 0x70, 0x81, 0xFF,
        0xFF, 0x00,
    };
    // kms2 recording: packet 273, 1 bytes (names masked with '*' by the masking pass).
    static readonly byte[] KmsCmd2 = new byte[] {
        0x02,
    };

    [Test] public void KmsCommand1SetsTheTickAndZone() {
        var session = new ClientSession(new MapleChannel(new MemoryStream()));
        var r = new ByteReader(KmsFull);
        try {
            MsTime.SetTimeZone(0, 0);
            Assert.That(session.ApplyTimeSync(r), Is.True);
            Assert.That(session.HasServerTick, Is.True);
            Assert.That(MsTime.ZoneSeconds, Is.EqualTo(-32400));
            Assert.That(r.Available, Is.EqualTo(0));
        } finally { MsTime.SetTimeZone(0, 0); }
    }

    [Test] public void KmsCommand2AsksForARequestAndSetsNoBase() {
        var session = new ClientSession(new MapleChannel(new MemoryStream()));
        var r = new ByteReader(KmsCmd2);
        Assert.That(session.ApplyTimeSync(r), Is.True);
        Assert.That(session.HasServerTick, Is.False);
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
