using System.Net;
using System.Net.Sockets;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>GameToGame 0x10 (CNetworkService::OnGameToGame, networkservice.cpp:942) and the next server's key check with its tokens.</summary>
public class GameToGameTests {
    static byte[] Body(Action<ByteWriter> write) {
        var w = new ByteWriter(); write(w);
        return w.Buffer[..w.Length];
    }

    static byte[] OkBody(int token1, int token2, byte[] ip, ushort port, int field, byte kind) => Body(w => {
        w.WriteByte(0); w.WriteInt(token1); w.WriteInt(token2); w.WriteBytes(ip); w.WriteShort((short) port); w.WriteInt(field); w.WriteByte(kind);
    });

    [Test] public void ReadsTheFieldsInTheClientsOrder() {
        var m = GameToGame.Read(new ByteReader(OkBody(0x11223344, unchecked((int) 0xAABBCCDD), new byte[] { 10, 0, 0, 7 }, 21003, 2000331, 0x11)));
        Assert.That(m.Ok, Is.True);
        Assert.That(m.Token1, Is.EqualTo(0x11223344)); Assert.That(m.Token2, Is.EqualTo(unchecked((int) 0xAABBCCDD)));
        Assert.That(m.Address, Is.EqualTo(IPAddress.Parse("10.0.0.7")));
        Assert.That(m.Port, Is.EqualTo(21003)); Assert.That(m.FieldId, Is.EqualTo(2000331)); Assert.That(m.Kind, Is.EqualTo(0x11));
        // ResponseKey writes token1 then token2 (WriteLoginEnter): the u64 with token1 as its low half
        Assert.That(m.Token, Is.EqualTo(0xAABBCCDD11223344UL));
    }

    [Test] public void AnErrorReadsOnlyTheResult() {
        var m = GameToGame.Read(new ByteReader(Body(w => w.WriteByte(0x2c))));
        Assert.That(m.Ok, Is.False); Assert.That(m.Error, Is.EqualTo(0x2c));
        Assert.That(m.Ip, Is.Null); Assert.That(m.FieldId, Is.EqualTo(0));
    }

    [Test] public void TrailingBytesAfterAnOkMoveAreRefused() {
        var body = OkBody(1, 2, new byte[] { 127, 0, 0, 1 }, 1, 3, 0).Append((byte) 9).ToArray();
        Assert.Throws<PacketLayoutException>(() => GameToGame.Read(new ByteReader(body)));
    }

    [Test] public async Task TheNextServerGetsTheGameToGameTokens() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = l.AcceptTcpClientAsync();
        var go = GameToGame.Read(new ByteReader(OkBody(0x01020304, 0x05060708, new byte[] { 127, 0, 0, 1 }, (ushort) port, 2000331, 0)));
        var machine = Enumerable.Range(1, 16).Select(i => (byte) i).ToArray();
        var enter = GameClient.EnterAsync(go, 10000000000, machine);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv);   // ResponseVersion
        await s.WriteAsync(srv.Frame(Inbound.RequestKey));
        var (op, body) = await ReadFrame(s, srv);
        Assert.That(op, Is.EqualTo((ushort) Outbound.ResponseKey));
        var key = ResponseKey.Read(new ByteReader(body));
        Assert.That(key.AccountId, Is.EqualTo(10000000000)); Assert.That(key.MachineId, Is.EqualTo(machine));
        Assert.That(BitConverter.ToInt32(body, 8), Is.EqualTo(0x01020304)); Assert.That(BitConverter.ToInt32(body, 12), Is.EqualTo(0x05060708));
        await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(0)));
        await s.WriteAsync(srv.Frame(Inbound.RequestFieldEnter, w => {
            w.WriteByte(0); w.WriteInt(2000331); w.WriteByte(0); w.WriteByte(0); w.WriteInt(0); w.WriteInt(0);
            w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(90); w.WriteInt(0);
        }));
        var (e, game) = await enter.WaitAsync(TimeSpan.FromSeconds(10));
        using (game) Assert.That(e.Field.MapId, Is.EqualTo(2000331));
        l.Stop();
    }

    [Test] public void AnErrorMoveConnectsNowhere() {
        var go = GameToGame.Read(new ByteReader(Body(w => w.WriteByte(1))));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await GameClient.EnterAsync(go, 1, new byte[16]));
    }

    static async Task<(ushort op, byte[] body)> ReadFrame(NetworkStream s, FakeServer srv) {
        var head = new byte[6]; await s.ReadExactlyAsync(head);
        var frame = new byte[6 + BitConverter.ToInt32(head, 2)]; head.CopyTo(frame, 0);
        await s.ReadExactlyAsync(frame.AsMemory(6));
        return srv.Read(frame);
    }
}
