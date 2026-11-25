using System;
using System.Buffers;
using System.IO;
using Maple2.PacketLib.Crypto;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and ClientSession handshake tests for RequestVersion (KMS 0x01, GMS2 0x01;
/// CNetworkService::OnRequestVersion @0x14153ad10, networkservice.cpp:835 -> Handshake.TryParse, MapleChannel.ReadHandshakeAsync, ClientSession.RunAsync).
/// </summary>
public class RequestVersionRecordedTests
{
    // kms2: 0x01, 25 bytes (plain server hello frame: version 2525, patchType 2)
    static readonly byte[] KmsHandshakeFrame = Convert.FromHexString("C4E8130000000100DD0900003F113933C61019E11700000002");

    // gms2: 0x01, 25 bytes (plain server hello frame: version 12, patchType 2)
    static readonly byte[] Gms2HandshakeFrame = Convert.FromHexString("B4A91300000001000C0000009B24095F1D30B8A91700000002");

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

    [Test]
    public void KmsRequestVersionParsesHandshakeAndAnswersResponseVersion()
    {
        Assert.That(Handshake.TryParse(KmsHandshakeFrame, out var hs, out int consumed, ProtocolProfile.Kms2525), Is.True);
        Assert.That(consumed, Is.EqualTo(25));
        Assert.That(hs.Version, Is.EqualTo(2525u));
        Assert.That(hs.ServerRecvIv, Is.EqualTo(0x3339113Fu));
        Assert.That(hs.ServerSendIv, Is.EqualTo(0xE11910C6u));
        Assert.That(hs.BlockIv, Is.EqualTo(23u));
        Assert.That(hs.PatchType, Is.EqualTo((byte)2));

        var io = new DuplexStream(new MemoryStream(KmsHandshakeFrame));
        var channel = new MapleChannel(io);
        var session = new ClientSession(channel);
        Assert.ThrowsAsync<EndOfStreamException>(() => session.RunAsync());

        Assert.That(session.ReceivedCount, Is.EqualTo(1));
        Assert.That(channel.Ready, Is.True);
        Assert.That(channel.Profile, Is.EqualTo(ProtocolProfile.Kms2525));

        var dec = new MapleCipher.Decryptor(hs.Version, hs.ServerRecvIv, hs.BlockIv);
        var written = io.Written.ToArray();
        Assert.That(dec.TryDecrypt(new ReadOnlySequence<byte>(written), out var reader), Is.EqualTo(written.Length));
        using (reader)
        {
            Assert.That((ushort)reader.ReadShort(), Is.EqualTo(ProtocolProfile.Kms2525.Number(Outbound.ResponseVersion)));
            var reply = ResponseVersion.Read(new ByteReader(reader.ReadBytes(reader.Length - 2)), ProtocolProfile.Kms2525);
            Assert.That(reply, Is.EqualTo(new ResponseVersion(2525u, 1, 0)));
        }
    }

    [Test]
    public void Gms2RequestVersionParsesHandshakeAutoSelectsProfileAndAnswersResponseVersion()
    {
        Assert.That(Handshake.TryParse(Gms2HandshakeFrame, out var hs, out int consumed, ProtocolProfile.Gms2), Is.True);
        Assert.That(consumed, Is.EqualTo(25));
        Assert.That(hs.Version, Is.EqualTo(12u));
        Assert.That(hs.ServerRecvIv, Is.EqualTo(0x5F09249Bu));
        Assert.That(hs.ServerSendIv, Is.EqualTo(0xA9B8301Du));
        Assert.That(hs.BlockIv, Is.EqualTo(23u));
        Assert.That(hs.PatchType, Is.EqualTo((byte)2));

        var io = new DuplexStream(new MemoryStream(Gms2HandshakeFrame));
        var channel = new MapleChannel(io);
        var session = new ClientSession(channel);
        Assert.ThrowsAsync<EndOfStreamException>(() => session.RunAsync());

        Assert.That(session.ReceivedCount, Is.EqualTo(1));
        Assert.That(channel.Ready, Is.True);
        Assert.That(channel.Profile, Is.EqualTo(ProtocolProfile.Gms2));
        Assert.That(channel.Hello, Is.EqualTo(hs));

        var dec = new MapleCipher.Decryptor(hs.Version, hs.ServerRecvIv, hs.BlockIv);
        var written = io.Written.ToArray();
        Assert.That(dec.TryDecrypt(new ReadOnlySequence<byte>(written), out var reader), Is.EqualTo(written.Length));
        using (reader)
        {
            Assert.That((ushort)reader.ReadShort(), Is.EqualTo(ProtocolProfile.Gms2.Number(Outbound.ResponseVersion)));
            var reply = ResponseVersion.Read(new ByteReader(reader.ReadBytes(reader.Length - 2)), ProtocolProfile.Gms2);
            Assert.That(reply, Is.EqualTo(new ResponseVersion(12u, 47, 2)));
        }
    }
}
