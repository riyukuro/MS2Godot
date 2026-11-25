using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// WorldService's idle timer (ResetIdleTimer @0x141bca010 worldservice_1.cpp:732, CheckIdleTimeout @0x141bca090 :746,
/// SendSyncWorld @0x141bca110 :762, SetIdleInterval @0x141bca170 :771) on an injected clock, and the SyncWorld request it sends
/// (SendOp::SyncWorld: KMS 0xA5, GMS2 0xA6, one bool).
/// </summary>
public class WorldIdleTimerTests
{
    sealed class Rig
    {
        public uint Now = 1000;
        public readonly List<bool> Sent = new();
        public readonly WorldIdleTimer Timer;
        public Rig() { Timer = new WorldIdleTimer(() => Now, Sent.Add); }
    }

    [Test]
    public void WithNoIntervalNothingIsSentAndNothingStarts()
    {
        var r = new Rig();
        r.Timer.ResetIdleTimer();
        Assert.That(r.Timer.DeadlineTick, Is.EqualTo(0u), "no interval: no deadline");
        r.Now += 10_000_000;
        r.Timer.CheckIdleTimeout();
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Timer.Idle, Is.False);
    }

    [Test]
    public void AnIntervalAloneStartsNoDeadlineUntilAReset()
    {
        // CheckIdleTimeout returns while m_a74 is 0 (worldservice_1.cpp:750): the first reset (game start) starts it
        var r = new Rig();
        r.Timer.SetIdleInterval(120000);
        r.Now += 500_000;
        r.Timer.CheckIdleTimeout();
        Assert.That(r.Sent, Is.Empty);
        r.Timer.ResetIdleTimer();
        Assert.That(r.Timer.DeadlineTick, Is.EqualTo(r.Now + 120000));
    }

    [Test]
    public void TheDeadlinePassingSendsIdleOnceAndAResetSendsBack()
    {
        var r = new Rig();
        r.Timer.SetIdleInterval(120000);
        r.Timer.ResetIdleTimer();
        r.Now += 119_999;
        r.Timer.CheckIdleTimeout();
        Assert.That(r.Sent, Is.Empty, "one ms before the deadline");
        r.Now += 1;
        r.Timer.CheckIdleTimeout();
        Assert.That(r.Sent, Is.EqualTo(new[] { true }), ">= the deadline");
        Assert.That(r.Timer.Idle, Is.True);
        r.Now += 50_000;
        r.Timer.CheckIdleTimeout();
        Assert.That(r.Sent, Is.EqualTo(new[] { true }), "once idle, the check sends nothing more");
        r.Timer.ResetIdleTimer();
        Assert.That(r.Sent, Is.EqualTo(new[] { true, false }));
        Assert.That(r.Timer.Idle, Is.False);
        Assert.That(r.Timer.DeadlineTick, Is.EqualTo(r.Now + 120000));
        r.Timer.ResetIdleTimer();
        Assert.That(r.Sent, Has.Count.EqualTo(2), "a reset that was not idle sends nothing");
    }

    [Test]
    public void InputKeepsTheTimerFromRunningOut()
    {
        var r = new Rig();
        r.Timer.SetIdleInterval(1000);
        r.Timer.ResetIdleTimer();
        for (int i = 0; i < 20; i++) { r.Now += 900; r.Timer.ResetIdleTimer(); r.Timer.CheckIdleTimeout(); }
        Assert.That(r.Sent, Is.Empty);
    }

    [Test]
    public void SwitchingTheIntervalOffWhileIdleSendsBack()
    {
        var r = new Rig();
        r.Timer.SetIdleInterval(1000);
        r.Timer.ResetIdleTimer();
        r.Now += 1000;
        r.Timer.CheckIdleTimeout();
        Assert.That(r.Sent, Is.EqualTo(new[] { true }));
        r.Timer.SetIdleInterval(0);
        Assert.That(r.Sent, Is.EqualTo(new[] { true, false }));
        Assert.That(r.Timer.Idle, Is.False);
        // a changed (non-zero) interval while idle sends nothing and leaves the flag
        var q = new Rig();
        q.Timer.SetIdleInterval(1000); q.Timer.ResetIdleTimer(); q.Now += 1000; q.Timer.CheckIdleTimeout();
        q.Timer.SetIdleInterval(5000);
        Assert.That(q.Sent, Is.EqualTo(new[] { true }));
        Assert.That(q.Timer.Idle, Is.True);
    }

    [Test]
    public void TheTickIsA32BitCountAndTheDeadlineWrapsWithIt()
    {
        // GetTickCountMs2 is a 32-bit count: the deadline wraps, as the client's (unsigned) compare does
        var r = new Rig { Now = uint.MaxValue - 100 };
        r.Timer.SetIdleInterval(1000);
        r.Timer.ResetIdleTimer();
        Assert.That(r.Timer.DeadlineTick, Is.EqualTo(unchecked(r.Now + 1000)));
        Assert.That(r.Timer.DeadlineTick, Is.EqualTo(899u));
    }

    static byte[] Body(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.ToArray(); }

    [Test]
    public void SyncWorldIsOneBoolAtTheProfilesOutboundNumber()
    {
        Assert.That(Body(new SyncWorldRequest(true)), Is.EqualTo(new byte[] { 1 }));
        Assert.That(Body(new SyncWorldRequest(false)), Is.EqualTo(new byte[] { 0 }));
        Assert.That(new SyncWorldRequest(true).Op, Is.EqualTo(Outbound.SyncWorld));
        Assert.That(ProtocolProfile.Kms2525.Number(Outbound.SyncWorld), Is.EqualTo((ushort)0xA5));
        Assert.That(ProtocolProfile.Gms2.Number(Outbound.SyncWorld), Is.EqualTo((ushort)0xA6));
    }

    [Test]
    public void EveryRecordedGms2SyncWorldRequestIsOurLayout()
    {
        int n = 0;
        foreach (var (file, data) in Gms2Sniffs.Outbound("gms2", ProtocolProfile.Gms2.Number(Outbound.SyncWorld)))
        {
            Assert.That(data, Has.Length.EqualTo(1), file);
            Assert.That(data[0], Is.LessThanOrEqualTo((byte)1), file);
            Assert.That(Body(new SyncWorldRequest(data[0] == 1)), Is.EqualTo(data), file);
            n++;
        }
        Assert.That(n, Is.GreaterThan(0), "no GMS2 SyncWorld requests recorded");
    }
}
