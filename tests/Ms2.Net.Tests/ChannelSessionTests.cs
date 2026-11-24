using System.Net;
using System.Net.Sockets;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// DynamicChannel 0xA7 (CNetworkService::OnDynamicChannel @0x14153c4b0, networkservice.cpp:977-989) and channel-move
/// endpoint entry (GameClient.EnterEndpointAsync for RestoreServerAddress, networkservice.cpp:582).
/// </summary>
public class ChannelSessionTests {
    static byte[] Body(Action<ByteWriter> write) {
        var w = new ByteWriter();
        write(w);
        return w.Buffer[..w.Length];
    }

    static async Task<(ushort op, byte[] body)> ReadFrame(NetworkStream s, FakeServer srv) {
        var head = new byte[6];
        await s.ReadExactlyAsync(head);
        var frame = new byte[6 + BitConverter.ToInt32(head, 2)];
        head.CopyTo(frame, 0);
        await s.ReadExactlyAsync(frame.AsMemory(6));
        return srv.Read(frame);
    }

    [Test] public void DynamicChannelMode1RejectsTrailingBytes() {
        var raw = Body(w => { w.WriteByte(1); w.WriteShort(2); w.WriteShort(1); w.WriteShort(2); w.WriteByte(0xFF); });
        Assert.Throws<PacketLayoutException>(() => DynamicChannelMessage.Read(new ByteReader(raw)));
    }

    [Test] public void DynamicChannelMode0RejectsTrailingBytes() {
        var raw = Body(w => { w.WriteByte(0); for (int i = 0; i < 8; i++) w.WriteShort((short) i); w.WriteByte(0xFF); });
        Assert.Throws<PacketLayoutException>(() => DynamicChannelMessage.Read(new ByteReader(raw)));
    }

    [Test] public async Task EnterEndpointAsyncConnectsToRestoredServerAddressWithMoveTokens() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint) listener.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = listener.AcceptTcpClientAsync();
        var machine = Enumerable.Range(1, 16).Select(i => (byte) i).ToArray();
        var enter = GameClient.EnterEndpointAsync("127.0.0.1", (ushort) port, 0x0506070801020304UL, 42, machine);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv);   // ResponseVersion
        await s.WriteAsync(srv.Frame(Inbound.RequestKey));
        var (op, body) = await ReadFrame(s, srv);
        Assert.That(op, Is.EqualTo((ushort) Outbound.ResponseKey));
        var key = ResponseKey.Read(new ByteReader(body));
        Assert.That((key.AccountId, key.Token), Is.EqualTo((42L, 0x0506070801020304UL)));
        await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(0)));
        await s.WriteAsync(srv.Frame(Inbound.RequestFieldEnter, w => {
            w.WriteByte(0); w.WriteInt(2000001); w.WriteByte(0); w.WriteByte(0); w.WriteInt(0); w.WriteInt(0);
            w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteInt(0);
        }));
        var (e, game) = await enter.WaitAsync(TimeSpan.FromSeconds(10));
        using (game) Assert.That(e.Field.MapId, Is.EqualTo(2000001));
        listener.Stop();
    }
}
