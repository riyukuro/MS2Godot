using System;
using System.Collections.Generic;
using System.IO;
using Ms2.Net;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet ClientSession dispatch tests for World (KMS 0xB9, GMS2 0xBB;
/// CSceneStateGame::OnWorld, scenestategame.cpp:3883).
/// Dispatch-only: OnWorld reads no packet fields (sets WorldService m_2bc = 1) and LiveSession discards the body.
/// </summary>
public class WorldRecordedTests
{
    // kms2: 0xB9, 17 bytes (non-empty variant; 64-bit id at bytes 1..9 masked)
    static readonly byte[] KmsWorld17Bytes = Convert.FromHexString("0001000000000011112358E25D00000000");

    sealed class DuplexStream : Stream
    {
        readonly Stream input;
        public DuplexStream(Stream input) { this.input = input; }
        public override int Read(byte[] b, int o, int c) => input.Read(b, o, c);
        public override void Write(byte[] b, int o, int c) { }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
    }

    [Test]
    public void KmsWorldFramesDispatchThroughClientSessionOnOpcode0xB9()
    {
        var srv = new FakeServer(0x01020304, 0x0A0B0C0D, gms2: false);
        var inbound = new MemoryStream();
        inbound.Write(srv.HelloFrame);
        inbound.Write(srv.Frame(Inbound.World));
        inbound.Write(srv.Frame(Inbound.World, w => w.WriteBytes(KmsWorld17Bytes)));
        inbound.Position = 0;

        var received = new List<RawPacket>();
        var session = new ClientSession(new MapleChannel(new DuplexStream(inbound)));
        session.PacketReceived += p => received.Add(p);
        Assert.ThrowsAsync<EndOfStreamException>(() => session.RunAsync());

        Assert.That(session.OpcodeCounts[0xB9], Is.EqualTo(2));
        Assert.That(received, Has.Count.EqualTo(2));
        Assert.That(session.Channel.Profile.Is(received[0].Opcode, Inbound.World), Is.True);
        Assert.That(received[0].Body, Is.Empty);
        Assert.That(session.Channel.Profile.Is(received[1].Opcode, Inbound.World), Is.True);
        Assert.That(received[1].Body, Is.EqualTo(KmsWorld17Bytes));
    }

    [Test]
    public void Gms2WorldEmptyFrameDispatchesThroughClientSessionOnOpcode0xBB()
    {
        var srv = new FakeServer(0x05060708, 0x0D0C0B0A, gms2: true);
        var inbound = new MemoryStream();
        inbound.Write(srv.HelloFrame);
        inbound.Write(srv.Frame(Inbound.World));
        inbound.Position = 0;

        var received = new List<RawPacket>();
        var session = new ClientSession(new MapleChannel(new DuplexStream(inbound)));
        session.PacketReceived += p => received.Add(p);
        Assert.ThrowsAsync<EndOfStreamException>(() => session.RunAsync());

        Assert.That(session.OpcodeCounts[0xBB], Is.EqualTo(1));
        Assert.That(received, Has.Count.EqualTo(1));
        Assert.That(session.Channel.Profile.Is(received[0].Opcode, Inbound.World), Is.True);
        Assert.That(received[0].Body, Is.Empty);
    }
}
