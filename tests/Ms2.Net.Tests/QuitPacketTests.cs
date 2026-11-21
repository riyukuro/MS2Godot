using System.Net;
using System.Net.Sockets;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// RequestQuit 0x0D (CNetworkService::SendRequestQuit / SendRequestQuitAndDisconnect, networkservice.cpp:501-512)
/// and GameToLogin 0x0F (CNetworkService::OnGameToLogin, networkservice.cpp:928-940).
/// </summary>
public class QuitPacketTests {
    static byte[] WriteOut(Ms2.Net.Wire.IOutPacket p) {
        var w = new ByteWriter();
        p.Write(w);
        return w.ToArray();
    }

    [Test] public void RequestQuitWritesMode0AndMode1WithOpcode0x0D() {
        Assert.That((ushort)Outbound.RequestQuit, Is.EqualTo(0x000D));

        // CNetworkService::SendRequestQuit (networkservice.cpp:501-505): mode 0
        var sel = new RequestQuit(RequestQuit.SelectCharacter);
        Assert.That(sel.Op, Is.EqualTo(Outbound.RequestQuit));
        var selBytes = WriteOut(sel);
        Assert.That(selBytes, Is.EqualTo(new byte[] { 0 }));
        Assert.That(RequestQuit.Read(new ByteReader(selBytes)), Is.EqualTo(sel));

        // CNetworkService::SendRequestQuitAndDisconnect (networkservice.cpp:507-512): mode 1
        var disc = new RequestQuit(RequestQuit.Disconnect);
        Assert.That(disc.Op, Is.EqualTo(Outbound.RequestQuit));
        var discBytes = WriteOut(disc);
        Assert.That(discBytes, Is.EqualTo(new byte[] { 1 }));
        Assert.That(RequestQuit.Read(new ByteReader(discBytes)), Is.EqualTo(disc));
    }

    [Test] public void GameToLoginReadsClientFieldOrderAndErrors() {
        Assert.That((ushort)Inbound.GameToLogin, Is.EqualTo(0x000F));

        // CNetworkService::OnGameToLogin (networkservice.cpp:928-934): u8 0, u32 ip, u16 port, i32 token1, i32 token2
        var w = new ByteWriter();
        w.WriteByte(0);
        w.WriteBytes(new byte[] { 127, 0, 0, 1 });
        w.WriteShort(20001);
        w.WriteInt(0x11223344);
        w.WriteInt(unchecked((int)0x55667788));
        var ok = GameToLogin.Read(new ByteReader(w.ToArray()));
        Assert.That(ok.Ok, Is.True);
        Assert.That(ok.Address, Is.EqualTo(IPAddress.Loopback));
        Assert.That(ok.Port, Is.EqualTo(20001));
        Assert.That(ok.Token1, Is.EqualTo(0x11223344));
        Assert.That(ok.Token2, Is.EqualTo(0x55667788));
        Assert.That(ok.Token, Is.EqualTo(0x5566778811223344UL));

        // Trailing byte rejected
        var extra = w.ToArray().Append((byte)0xFF).ToArray();
        Assert.Throws<PacketLayoutException>(() => GameToLogin.Read(new ByteReader(extra)));

        // Non-zero error reads nothing further (:939)
        var err = GameToLogin.Read(new ByteReader(new byte[] { 0x15 }));
        Assert.That(err.Ok, Is.False);
        Assert.That(err.Error, Is.EqualTo(0x15));
        Assert.That(err.Ip, Is.Null);
    }

    [Test] public async Task RequestQuitDisconnectReachesServerBeforeSocketCloses() {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        var srv = new FakeServer(7, 8);
        var accept = l.AcceptTcpClientAsync();
        using var transport = new TcpTransport();
        var connect = transport.ConnectAsync("127.0.0.1", port);
        using var peer = await accept;
        var s = peer.GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await connect;
        // Consume ResponseVersion sent after hello
        var (vOp, _) = await ReadFrame(s, srv);
        Assert.That(vOp, Is.EqualTo((ushort)Outbound.ResponseVersion));

        await transport.SendAsync(new RequestQuit(RequestQuit.Disconnect));
        var (op, body) = await ReadFrame(s, srv);
        Assert.That(op, Is.EqualTo((ushort)Outbound.RequestQuit));
        Assert.That(body, Is.EqualTo(new byte[] { 1 }));
        transport.Dispose();
        Assert.That(transport.IsClosed, Is.True);
        l.Stop();
    }

    static async Task<(ushort op, byte[] body)> ReadFrame(NetworkStream s, FakeServer srv) {
        var head = new byte[6];
        await s.ReadExactlyAsync(head);
        var frame = new byte[6 + BitConverter.ToInt32(head, 2)];
        head.CopyTo(frame, 0);
        await s.ReadExactlyAsync(frame.AsMemory(6));
        return srv.Read(frame);
    }
}
