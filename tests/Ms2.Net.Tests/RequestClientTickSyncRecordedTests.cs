using System;
using System.Collections.Generic;
using System.IO;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and ClientSession dispatch tests for RequestClientTickSync (KMS 0x13, GMS2 0x13;
/// CNetworkService::OnRequestClientTickSync -> ClientSession.RunAsync -> ResponseClientTickSync).
/// </summary>
public class RequestClientTickSyncRecordedTests
{
    // kms2: 0x13, 4 bytes (tick 1921521103)
    static readonly byte[] KmsTickSync = Convert.FromHexString("CF158872");

    // gms2: 0x13, 4 bytes (tick 115378328)
    static readonly byte[] Gms2TickSync = Convert.FromHexString("9888E006");

    sealed class DuplexStream : Stream
    {
        readonly Stream input;
        public readonly MemoryStream Written = new();
        public DuplexStream(Stream input) { this.input = input; }
        public override int Read(byte[] b, int o, int c) => input.Read(b, o, c);
        public override void Write(byte[] b, int o, int c) => Written.Write(b, o, c);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
    }

    static void VerifySessionAnswersTickSync(byte[] payload, bool gms2, int expectedTick)
    {
        var profile = gms2 ? ProtocolProfile.Gms2 : ProtocolProfile.Kms2525;
        var srv = new FakeServer(0x01020304, 0x0A0B0C0D, gms2: gms2);
        var inbound = new MemoryStream();
        inbound.Write(srv.HelloFrame);
        inbound.Write(srv.Frame(Inbound.RequestClientTickSync, w => w.WriteBytes(payload)));
        inbound.Position = 0;

        var io = new DuplexStream(inbound);
        var session = new ClientSession(new MapleChannel(io));
        int elapsedBefore = session.ElapsedMs;
        Assert.ThrowsAsync<EndOfStreamException>(() => session.RunAsync());
        int elapsedAfter = session.ElapsedMs;

        Assert.That(session.OpcodeCounts[0x13], Is.EqualTo(1));

        var all = io.Written.ToArray();
        int off = 0;
        var sent = new List<(ushort Op, byte[] Body)>();
        while (off < all.Length)
        {
            int len = BitConverter.ToInt32(all, off + 2) + 6;
            sent.Add(srv.Read(all[off..(off + len)]));
            off += len;
        }

        Assert.That(sent.Count, Is.EqualTo(2));
        Assert.That(sent[1].Op, Is.EqualTo(profile.Number(Outbound.ResponseClientTickSync)));

        var reply = ResponseClientTickSync.Read(new ByteReader(sent[1].Body));
        Assert.That(reply.ServerTick, Is.EqualTo(expectedTick));
        Assert.That(reply.ClientTick, Is.InRange(elapsedBefore, elapsedAfter));
    }

    [Test]
    public void KmsSessionEchoesRecordedClientTickSync()
        => VerifySessionAnswersTickSync(KmsTickSync, gms2: false, expectedTick: 1_921_521_103);

    [Test]
    public void Gms2SessionEchoesRecordedClientTickSync()
        => VerifySessionAnswersTickSync(Gms2TickSync, gms2: true, expectedTick: 115_378_328);
}
