using System.Net;
using System.Net.Sockets;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Loopback only: the test plays the server on 127.0.0.1 with an ephemeral port.</summary>
public class TcpTransportTests {
    static async Task<(TcpTransport t, NetworkStream srvStream, TcpListener l, FakeServer srv)> Start() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var srv = new FakeServer(0x01020304, 0x0A0B0C0D);
        var accept = l.AcceptTcpClientAsync();
        var t = new TcpTransport();
        await t.ConnectAsync("127.0.0.1", ((IPEndPoint) l.LocalEndpoint).Port);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        return (t, s, l, srv);
    }

    static async Task<(ushort op, byte[] body)> ReadFrame(NetworkStream s, FakeServer srv) {
        var head = new byte[6]; await s.ReadExactlyAsync(head);
        var frame = new byte[6 + BitConverter.ToInt32(head, 2)]; head.CopyTo(frame, 0);
        await s.ReadExactlyAsync(frame.AsMemory(6));
        return srv.Read(frame);
    }

    static async Task<RawPacket> Dequeue(TcpTransport t) {
        for (int i = 0; i < 500; i++) { if (t.Incoming.TryDequeue(out var p)) return p; await Task.Delay(10); }
        throw new TimeoutException("no packet");
    }

    [Test] public async Task HandshakeAndPacketsBothWays() {
        var (t, s, l, srv) = await Start();
        using (t) {
            var (op, body) = await ReadFrame(s, srv);                       // the session answers the hello
            Assert.That(op, Is.EqualTo((ushort) Outbound.ResponseVersion));
            Assert.That(ResponseVersion.Read(new Maple2.PacketLib.Tools.ByteReader(body)).Version, Is.EqualTo(2525u));
            Assert.That(t.Channel.Hello.Version, Is.EqualTo(2525u));

            await s.WriteAsync(srv.Frame(Inbound.RequestLogin));            // server to client
            Assert.That((await Dequeue(t)).Opcode, Is.EqualTo((ushort) Inbound.RequestLogin));

            await t.SendAsync(new ResponseHeartbeat(3, 4));                 // client to server
            (op, body) = await ReadFrame(s, srv);
            Assert.That(op, Is.EqualTo((ushort) Outbound.ResponseHeartbeat));
            Assert.That(ResponseHeartbeat.Read(new Maple2.PacketLib.Tools.ByteReader(body)), Is.EqualTo(new ResponseHeartbeat(3, 4)));
        }
        l.Stop();
    }

    [Test] public async Task KeepaliveAnsweredWhileNothingDrains() {
        var (t, s, l, srv) = await Start();
        using (t) {
            await ReadFrame(s, srv);                                        // ResponseVersion
            await s.WriteAsync(srv.Frame(Inbound.RequestHeartbeat, w => w.WriteInt(777)));
            await s.WriteAsync(srv.Frame(Inbound.Ping, w => w.WriteInt(5)));
            var hb = await ReadFrame(s, srv);                               // answered without anyone touching Incoming
            Assert.That(hb.op, Is.EqualTo((ushort) Outbound.ResponseHeartbeat));
            Assert.That(ResponseHeartbeat.Read(new Maple2.PacketLib.Tools.ByteReader(hb.body)).ServerTick, Is.EqualTo(777));
            var pong = await ReadFrame(s, srv);
            Assert.That(pong.op, Is.EqualTo((ushort) Outbound.Pong));
            Assert.That(Pong.Read(new Maple2.PacketLib.Tools.ByteReader(pong.body)).ReceivedCount, Is.EqualTo(3)); // hello, heartbeat, ping
        }
        l.Stop();
    }

    [Test] public async Task ServerCloseEndsTheLoop() {
        var (t, s, l, srv) = await Start();
        using (t) {
            await ReadFrame(s, srv);
            s.Close();
            for (int i = 0; i < 500 && !t.IsClosed; i++) await Task.Delay(10);
            Assert.That(t.IsClosed, Is.True);
            Assert.That(t.Failure, Is.InstanceOf<EndOfStreamException>());
        }
        l.Stop();
    }

    [Test] public async Task DisposeCancelsCleanly() {
        var (t, s, l, srv) = await Start();
        await ReadFrame(s, srv);
        t.Dispose();
        Assert.That(t.IsClosed, Is.True);
        Assert.That(t.Failure, Is.Null);
        t.Dispose();                                                        // twice is fine
        l.Stop();
    }
}
