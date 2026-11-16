using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class SessionTests {
    [Test] public async Task AnswersHelloAndKeepalivesAndForwardsTheRest() {
        var srv = new FakeServer(0x01020304, 0x0A0B0C0D);
        var inbound = new MemoryStream();
        inbound.Write(srv.HelloFrame);
        inbound.Write(srv.Frame(Inbound.RequestHeartbeat, w => w.WriteInt(777)));
        inbound.Write(srv.Frame(Inbound.RequestClientTickSync, w => w.WriteInt(888)));
        inbound.Write(srv.Frame(Inbound.Ping, w => w.WriteInt(5)));
        inbound.Write(srv.Frame(Inbound.RequestLogin));
        inbound.Write(srv.Frame((Inbound) 0x3FF, w => w.WriteInt(1))); // unknown opcode: forwarded, not fatal
        inbound.Position = 0;
        var io = new DuplexStream(inbound);
        var session = new ClientSession(new MapleChannel(io));
        var seen = new List<ushort>();
        session.PacketReceived += p => seen.Add(p.Opcode);
        Assert.ThrowsAsync<EndOfStreamException>(() => session.RunAsync());

        Assert.That(seen, Is.EqualTo(new ushort[] { 0x12, 0x13, 0x06, 0x09, 0x3FF }));
        Assert.That(session.OpcodeCounts[0x3FF], Is.EqualTo(1));
        // what the client wrote, decoded by the server half: hello, heartbeat answer, tick answer
        var all = io.Written.ToArray(); int off = 0; var got = new List<(ushort op, byte[] body)>();
        while (off < all.Length) { int len = BitConverter.ToInt32(all, off + 2) + 6; got.Add(srv.Read(all[off..(off + len)])); off += len; }
        Assert.That(got.Select(g => g.op), Is.EqualTo(new ushort[] { 1, 0x0A, 0x0C, 0x06 }));
        Assert.That(ResponseVersion.Read(new Maple2.PacketLib.Tools.ByteReader(got[0].body)).Version, Is.EqualTo(2525u));
        Assert.That(ResponseHeartbeat.Read(new Maple2.PacketLib.Tools.ByteReader(got[1].body)).ServerTick, Is.EqualTo(777));
        Assert.That(ResponseClientTickSync.Read(new Maple2.PacketLib.Tools.ByteReader(got[2].body)).ServerTick, Is.EqualTo(888));
        // hello + heartbeat + tick sync + the ping itself = 4 frames received when the pong is built
        Assert.That(Pong.Read(new Maple2.PacketLib.Tools.ByteReader(got[3].body)).ReceivedCount, Is.EqualTo(4));
    }

    /// <summary>Reads from a prepared stream, collects writes: no socket.</summary>
    sealed class DuplexStream : Stream {
        readonly Stream input; public readonly MemoryStream Written = new();
        public DuplexStream(Stream i) { input = i; }
        public override int Read(byte[] b, int o, int c) => input.Read(b, o, c);
        public override void Write(byte[] b, int o, int c) => Written.Write(b, o, c);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException();
    }
}
