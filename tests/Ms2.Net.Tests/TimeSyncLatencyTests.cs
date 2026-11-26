using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Unk_NetworkLatency (networkservice.cpp:1229-1258) and its use by the time sync commands 0 and 1 (:756-779).</summary>
public class TimeSyncLatencyTests {
    MsTime.State savedZone;
    [SetUp] public void SaveZone() => savedZone = MsTime.Save();
    [TearDown] public void RestoreZone() => MsTime.Restore(savedZone);

    [Test]
    public void TheStatisticsCoverTheLastFiveRoundTripsBySequence() {
        var l = new TimeSyncLatency();
        Assert.That((l.Minimum, l.Maximum, l.Average), Is.EqualTo((0, 0, 0)));
        l.Sent(1, 1000); l.Received(1, 1030);
        Assert.That((l.Minimum, l.Maximum, l.Average), Is.EqualTo((30, 30, 30)));
        // round trips 30, 10, 50, 20, 40, 60 for sequences 1..6: the sixth drops sequence 1's sample
        uint[] trips = { 10, 50, 20, 40, 60 };
        for (uint i = 0; i < trips.Length; i++) { l.Sent(i + 2, 2000 + i * 100); l.Received(i + 2, 2000 + i * 100 + trips[i]); }
        Assert.That((l.Minimum, l.Maximum, l.Average), Is.EqualTo((10, 60, (10 + 50 + 20 + 40 + 60) / 5)));
    }

    [Test]
    public void AnAnswerWithNoRequestChangesNothingAndResetClears() {
        var l = new TimeSyncLatency();
        l.Sent(3, 500); l.Received(3, 520);
        l.Received(9, 900);                              // never sent: start 0, the client returns
        l.Received(3, 900);                              // already taken
        Assert.That((l.Minimum, l.Maximum, l.Average), Is.EqualTo((20, 20, 20)));
        l.Sent(4, 600);
        l.Reset();
        Assert.That((l.Minimum, l.Maximum, l.Average), Is.EqualTo((0, 0, 0)));
        l.Received(4, 700);                              // Reset also forgot the request
        Assert.That(l.Average, Is.EqualTo(0));
    }

    static ByteReader Sync(byte cmd, int sequence) {
        var w = new ByteWriter();
        w.WriteByte(cmd); w.WriteInt(0); w.WriteLong(0); w.WriteInt(0); w.WriteByte(0);
        if (cmd == 0) w.WriteInt(sequence);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test]
    public void ACommandZeroAnswerGivesTheRequestsRoundTripAndCommandOneResetsIt() {
        var s = new ClientSession(new MapleChannel(new MemoryStream()));
        Thread.Sleep(5);                                 // a request sent at tick 0 counts as never sent, as in the client
        var req = s.NextTimeSyncRequest();
        Thread.Sleep(40);
        Assert.That(s.ApplyTimeSync(Sync(0, req.Sequence)), Is.False);
        Assert.That(s.Latency.Average, Is.InRange(35, 2000));
        Assert.That(s.NextTimeSyncRequest().Sequence, Is.EqualTo(req.Sequence + 1));
        Assert.That(s.ApplyTimeSync(Sync(1, 0)), Is.True);
        Assert.That(s.Latency.Average, Is.EqualTo(0));
    }
}
