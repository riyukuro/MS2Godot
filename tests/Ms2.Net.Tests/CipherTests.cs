using Maple2.PacketLib.Crypto;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The server half of a connection, built from the same cipher the server fork uses, so the client channel is tested against its mirror.</summary>
public sealed class FakeServer {
    public readonly MapleCipher.Encryptor Send;
    public readonly MapleCipher.Decryptor Recv;
    public readonly byte[] HelloFrame;
    public const uint Version = 2525, Block = 23;

    public FakeServer(uint siv, uint riv) {
        Send = new MapleCipher.Encryptor(Version, siv, Block);
        Recv = new MapleCipher.Decryptor(Version, riv, Block);
        var w = new ByteWriter();
        w.WriteShort((short) Inbound.RequestVersion);
        w.WriteInt((int) Version); w.WriteInt((int) riv); w.WriteInt((int) siv); w.WriteInt((int) Block); w.WriteByte(2);
        var f = Send.WriteHeader(w.Buffer, 0, w.Length); // plain, and it advances the send iv like the real server
        HelloFrame = f.Buffer.AsSpan(0, f.Length).ToArray();
    }

    public byte[] Frame(Inbound op, Action<ByteWriter> body = null) {
        var w = new ByteWriter();
        w.WriteShort((short) op);
        body?.Invoke(w);
        var e = Send.Encrypt(w);
        return e.Buffer.AsSpan(0, e.Length).ToArray();
    }

    public (ushort op, byte[] body) Read(byte[] frame) {
        int n = Recv.TryDecrypt(new System.Buffers.ReadOnlySequence<byte>(frame), out var r);
        Assert.That(n, Is.EqualTo(frame.Length));
        using (r) { ushort op = (ushort) r.ReadShort(); return (op, r.ReadBytes(r.Length - 2)); }
    }
}

public class CipherTests {
    static (MapleChannel ch, MemoryStream io) Connect(FakeServer srv) {
        var io = new MemoryStream();
        io.Write(srv.HelloFrame); io.Position = 0;
        return (new MapleChannel(io), io);
    }

    [Test]
    public async Task HandshakeFieldsAndKeys() {
        var srv = new FakeServer(0x20609700, 0x12345678);
        var (ch, _) = Connect(srv);
        var hs = await ch.ReadHandshakeAsync();
        Assert.That(hs.Version, Is.EqualTo(2525u));
        Assert.That(hs.ServerSendIv, Is.EqualTo(0x20609700u));
        Assert.That(hs.ServerRecvIv, Is.EqualTo(0x12345678u));
        Assert.That(hs.BlockIv, Is.EqualTo(23u));
        Assert.That(hs.PatchType, Is.EqualTo(2));
    }

    [Test]
    public async Task ServerToClientRoundTripManyPackets() {
        var srv = new FakeServer(0xCAFEBABE, 0x0BADF00D);
        var io = new MemoryStream();
        io.Write(srv.HelloFrame);
        var bodies = new List<byte[]>();
        for (int i = 0; i < 50; i++) {
            var body = new byte[i * 7 % 33];
            new Random(i).NextBytes(body);
            bodies.Add(body);
            io.Write(srv.Frame(Inbound.RequestHeartbeat, w => w.WriteBytes(body)));
        }
        io.Position = 0;
        var ch = new MapleChannel(io);
        await ch.ReadHandshakeAsync();
        for (int i = 0; i < 50; i++) {
            var p = await ch.ReadPacketAsync();
            Assert.That(p.Opcode, Is.EqualTo((ushort) Inbound.RequestHeartbeat));
            Assert.That(p.Body, Is.EqualTo(bodies[i]), $"packet {i}");
        }
    }

    [Test]
    public async Task ClientToServerRoundTrip() {
        var srv = new FakeServer(0x11111111, 0x22222222);
        var (ch, io) = Connect(srv);
        await ch.ReadHandshakeAsync();
        long start = io.Length;
        await ch.SendAsync(new ResponseVersion());
        await ch.SendAsync(new ResponseKey(7, 0xFFFFFFFFFFFF0001UL, new byte[16]));
        await ch.SendAsync(Outbound.ResponseHeartbeat, w => { w.WriteInt(5); w.WriteInt(6); });
        var all = io.ToArray().AsSpan((int) start).ToArray();
        int off = 0;
        var got = new List<(ushort op, byte[] body)>();
        while (off < all.Length) {
            int len = BitConverter.ToInt32(all, off + 2) + 6;
            got.Add(srv.Read(all[off..(off + len)]));
            off += len;
        }
        Assert.That(got.Select(g => g.op), Is.EqualTo(new ushort[] { 1, 4, 0x0A }));
        Assert.That(ResponseVersion.Read(new ByteReader(got[0].body)), Is.EqualTo(new ResponseVersion()));
        var key = ResponseKey.Read(new ByteReader(got[1].body));
        Assert.That(key.AccountId, Is.EqualTo(7)); Assert.That(key.Token, Is.EqualTo(0xFFFFFFFFFFFF0001UL));
        Assert.That(ResponseHeartbeat.Read(new ByteReader(got[2].body)), Is.EqualTo(new ResponseHeartbeat(5, 6)));
    }

    [Test]
    public async Task FragmentedDeliveryByteByByte() {
        var srv = new FakeServer(0xAAAA5555, 0x5555AAAA);
        byte[] all = srv.HelloFrame.Concat(srv.Frame(Inbound.MoveResult, w => w.WriteByte(0))).Concat(srv.Frame(Inbound.SyncNumber, w => w.WriteByte(9))).ToArray();
        var ch = new MapleChannel(new OneByteStream(all));
        await ch.ReadHandshakeAsync();
        Assert.That((await ch.ReadPacketAsync()).Opcode, Is.EqualTo((ushort) Inbound.MoveResult));
        var p = await ch.ReadPacketAsync();
        Assert.That(SyncNumber.Read(p.Reader()).Number, Is.EqualTo(9));
    }

    /// <summary>A stream that hands out one byte per read, like the worst TCP segmentation.</summary>
    sealed class OneByteStream : Stream {
        readonly byte[] data; int pos;
        public OneByteStream(byte[] d) { data = d; }
        public override int Read(byte[] buffer, int offset, int count) { if (pos >= data.Length || count == 0) return 0; buffer[offset] = data[pos++]; return 1; }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => pos; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    [Test]
    public void WrongVersionIsRejected() {
        // the receiving side throws when the decoded sequence base is not the version: a desynchronised iv is detected, not decoded
        var srv = new FakeServer(1, 2);
        var ch = new MapleChannel(new MemoryStream());
        ch.SetCiphers(new MapleCipher.Encryptor(2525, 2, 23), new MapleCipher.Decryptor(2525, 12345, 23));
        ch.Feed(srv.Frame(Inbound.MoveResult));
        Assert.Throws<ArgumentException>(() => ch.TryRead(out _));
    }

    [Test]
    public void IvStepMatchesCrt() => Assert.That(Handshake.Step(1), Is.EqualTo(214013u + 2531011u));
}
