using System;
using System.IO;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and ClientSession dispatch tests for Gvg (KMS 0x6D, GMS2 0x6F;
/// CNetworkService::OnGvg @0x14153a8f0, networkservice.cpp:811-832 -> ClientSession -> ReconnectionManager.SetOptions).
/// </summary>
public class GvgRecordedTests
{
    // kms2: 0x6D, 83 bytes (4 entries: LaunchFlag, GroupState, DungeonRates, ReconnectionOptions)
    static readonly byte[] KmsGvgFourGroups = Convert.FromHexString(
        "04000000010000000022000000000000000000020000002A0000000000803F0000803F" +
        "2B00000001000000809698002D000000050000000100000000000000010000000F000000020000000000000001000000");

    // kms2: 0x6D, 12 bytes (1 entry: ContentShutdown [0x3A])
    static readonly byte[] KmsGvgShutdown = Convert.FromHexString("010000000700000001003A00");

    // gms2: 0x6F, 83 bytes (4 entries: LaunchFlag, GroupState, DungeonRates, ReconnectionOptions)
    static readonly byte[] Gms2GvgFourGroups = Convert.FromHexString(
        "04000000010000000022000000370000000000020000002A0000000000803F0000803F" +
        "2B00000001000000809698002D000000050000000100000000000000010000000F000000020000000000000001000000");

    // gms2: 0x6F, 16 bytes (1 entry: ContentShutdown [0x15, 0x3C, 0x60])
    static readonly byte[] Gms2GvgShutdown = Convert.FromHexString("0100000007000000030015003C006000");

    sealed class SessionDuplexStream : Stream
    {
        readonly Stream input;
        readonly MemoryStream written = new();
        public SessionDuplexStream(Stream input) { this.input = input; }
        public override int Read(byte[] b, int o, int c) => input.Read(b, o, c);
        public override void Write(byte[] b, int o, int c) => written.Write(b, o, c);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
    }

    static (ClientSession Session, ReconnectionManager Manager) RunGvgThroughSession(byte[] payload, bool gms2)
    {
        var srv = new FakeServer(0x01020304, 0x0A0B0C0D, gms2: gms2);
        var inbound = new MemoryStream();
        inbound.Write(srv.HelloFrame);
        inbound.Write(srv.Frame(Inbound.Gvg, w => w.WriteBytes(payload)));
        inbound.Position = 0;

        var host = new RecordedReconnectHost();
        var mgr = new ReconnectionManager(host);
        mgr.OnPacket(new ReconnectNotice(ReconnectNotice.KindContext, new ReconnectionContext(1, "token", 21001), 0));

        var session = new ClientSession(new MapleChannel(new SessionDuplexStream(inbound))) { Manager = mgr };
        Assert.ThrowsAsync<EndOfStreamException>(() => session.RunAsync());
        return (session, mgr);
    }

    static void VerifyFourGroupGvg(byte[] payload, bool gms2, ushort wireOpcode, short expectedGroupStateValue0)
    {
        var entries = GvgOptions.ReadAll(new ByteReader(payload));
        Assert.That(entries.Count, Is.EqualTo(4));
        Assert.That(((GvgEntry.LaunchFlag)entries[0]).Enabled, Is.False);
        Assert.That(((GvgEntry.GroupState)entries[1]).Value0, Is.EqualTo(expectedGroupStateValue0));
        Assert.That(((GvgEntry.GroupState)entries[1]).Value2, Is.EqualTo(2));
        Assert.That(((GvgEntry.DungeonRates)entries[2]).RateA, Is.EqualTo(1f));
        Assert.That(((GvgEntry.DungeonRates)entries[2]).RateB, Is.EqualTo(1f));

        var (session, mgr) = RunGvgThroughSession(payload, gms2);
        Assert.That(session.OpcodeCounts[wireOpcode], Is.EqualTo(1));
        Assert.That(mgr.Options[ReconnectionOptions.Enabled], Is.EqualTo(1));
        Assert.That(mgr.Options[ReconnectionOptions.BufferLimit], Is.EqualTo(10_000_000));
        Assert.That(mgr.Options[ReconnectionOptions.GiveUpSeconds], Is.EqualTo(45));
        Assert.That(mgr.Options[ReconnectionOptions.MaxAttempts], Is.EqualTo(15));
        Assert.That(mgr.Options[ReconnectionOptions.RetryDelaySeconds], Is.EqualTo(2));
        Assert.That(mgr.ShouldReconnect(ReconnectionManager.ErrorSocketClosed), Is.True);
    }

    [Test]
    public void KmsGvgSessionDispatchAppliesReconnectionOptionsAndIgnoresShutdownOnly()
    {
        VerifyFourGroupGvg(KmsGvgFourGroups, gms2: false, wireOpcode: 0x6D, expectedGroupStateValue0: 0);

        var shutdown = GvgOptions.ReadAll(new ByteReader(KmsGvgShutdown));
        Assert.That(shutdown.Count, Is.EqualTo(1));
        Assert.That(((GvgEntry.ContentShutdown)shutdown[0]).DisabledContents, Is.EqualTo(new ushort[] { 0x3A }));

        var (session, mgr) = RunGvgThroughSession(KmsGvgShutdown, gms2: false);
        Assert.That(session.OpcodeCounts[0x6D], Is.EqualTo(1));
        Assert.That(mgr.Options[ReconnectionOptions.Enabled], Is.EqualTo(0));
        Assert.That(mgr.ShouldReconnect(ReconnectionManager.ErrorSocketClosed), Is.False);
    }

    [Test]
    public void Gms2GvgSessionDispatchAppliesReconnectionOptionsAndIgnoresShutdownOnly()
    {
        VerifyFourGroupGvg(Gms2GvgFourGroups, gms2: true, wireOpcode: 0x6F, expectedGroupStateValue0: 55);

        var shutdown = GvgOptions.ReadAll(new ByteReader(Gms2GvgShutdown));
        Assert.That(shutdown.Count, Is.EqualTo(1));
        Assert.That(((GvgEntry.ContentShutdown)shutdown[0]).DisabledContents, Is.EqualTo(new ushort[] { 0x15, 0x3C, 0x60 }));

        var (session, mgr) = RunGvgThroughSession(Gms2GvgShutdown, gms2: true);
        Assert.That(session.OpcodeCounts[0x6F], Is.EqualTo(1));
        Assert.That(mgr.Options[ReconnectionOptions.Enabled], Is.EqualTo(0));
        Assert.That(mgr.ShouldReconnect(ReconnectionManager.ErrorSocketClosed), Is.False);
    }
}
