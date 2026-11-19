using System.Net;
using System.Net.Sockets;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Select, migration and field-enter steps against in-process fake servers, plus an opt-in live run.</summary>
public class GameClientTests {
    static async Task<(ushort op, byte[] body)> ReadFrame(NetworkStream s, FakeServer srv) {
        var head = new byte[6]; await s.ReadExactlyAsync(head);
        var frame = new byte[6 + BitConverter.ToInt32(head, 2)]; head.CopyTo(frame, 0);
        await s.ReadExactlyAsync(frame.AsMemory(6));
        return srv.Read(frame);
    }

    static byte[] LoginToGameFrame(FakeServer srv, byte[] ip, ushort port, ulong token, int map) => srv.Frame(Inbound.LoginToGame, w => {
        w.WriteByte(0); w.WriteBytes(ip); w.WriteShort((short) port); w.WriteLong((long) token); w.WriteInt(map);
    });

    [Test] public async Task SelectSendsCommandZeroAndReadsTheEndpoint() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var srv = new FakeServer(1, 2);
        var accept = l.AcceptTcpClientAsync();
        using var t = new TcpTransport();
        await t.ConnectAsync("127.0.0.1", ((IPEndPoint) l.LocalEndpoint).Port);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv); // ResponseVersion
        var sel = GameClient.SelectAsync(t, 20000000000, 1);
        var (op, body) = await ReadFrame(s, srv);
        Assert.That(op, Is.EqualTo((ushort) Outbound.CharacterManagement));
        var r = new ByteReader(body);
        Assert.That(r.ReadByte(), Is.EqualTo(GameClient.SelectCommand)); Assert.That(r.ReadLong(), Is.EqualTo(20000000000)); Assert.That(r.ReadShort(), Is.EqualTo(1));
        await s.WriteAsync(LoginToGameFrame(srv, new byte[] { 127, 0, 0, 1 }, 20003, 0x1122334455667788UL, 2000001));
        var go = await sel.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(go.Ok); Assert.That(go.Port, Is.EqualTo(20003)); Assert.That(go.Token, Is.EqualTo(0x1122334455667788UL)); Assert.That(go.MapId, Is.EqualTo(2000001));
        l.Stop();
    }

    [Test] public async Task SelectRefusedThrows() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var srv = new FakeServer(1, 2);
        var accept = l.AcceptTcpClientAsync();
        using var t = new TcpTransport();
        await t.ConnectAsync("127.0.0.1", ((IPEndPoint) l.LocalEndpoint).Port);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv);
        var sel = GameClient.SelectAsync(t, 1);
        await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(7)));
        Assert.ThrowsAsync<InvalidDataException>(async () => await sel.WaitAsync(TimeSpan.FromSeconds(10)));
        l.Stop();
    }

    [Test] public async Task KeyExchangeAndEntryUpToFieldEnter() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = l.AcceptTcpClientAsync();
        var go = new LoginToGame(0, new byte[] { 127, 0, 0, 1 }, (ushort) port, 0xAABBCCDD11223344UL, 2000001, "");
        var machine = Enumerable.Range(1, 16).Select(i => (byte) i).ToArray();
        var enter = GameClient.EnterAsync(go, 10000000000, machine);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        Assert.That((await ReadFrame(s, srv)).op, Is.EqualTo((ushort) Outbound.ResponseVersion));
        await s.WriteAsync(srv.Frame(Inbound.RequestKey));
        var (op, body) = await ReadFrame(s, srv);
        Assert.That(op, Is.EqualTo((ushort) Outbound.ResponseKey));
        var key = ResponseKey.Read(new ByteReader(body));
        Assert.That(key.AccountId, Is.EqualTo(10000000000)); Assert.That(key.Token, Is.EqualTo(0xAABBCCDD11223344UL)); Assert.That(key.MachineId, Is.EqualTo(machine));
        // the client writes the token as two i32, low half first: the same bytes
        Assert.That(BitConverter.ToInt32(body, 8), Is.EqualTo(0x11223344)); Assert.That(BitConverter.ToInt32(body, 12), Is.EqualTo(unchecked((int) 0xAABBCCDD)));
        await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(0)));
        await s.WriteAsync(srv.Frame(Inbound.ServerEnter, w => {
            w.WriteInt(7); w.WriteLong(20000000000); w.WriteShort(1); w.WriteLong(100); w.WriteLong(50); w.WriteLong(9);
            for (int i = 0; i < 14; i++) w.WriteLong(i);
            w.WriteUnicodeString("pic"); w.WriteByte(0);
            w.WriteShort(1); w.WriteInt(2000001); w.WriteShort(0);
            w.WriteLong(0); w.WriteUnicodeString(""); w.WriteUnicodeString("u1"); w.WriteUnicodeString("u2"); w.WriteUnicodeString("u3");
        }));
        await s.WriteAsync(srv.Frame(Inbound.SyncNumber, w => w.WriteByte(0)));
        await s.WriteAsync(srv.Frame(Inbound.RequestFieldEnter, w => {
            w.WriteByte(0); w.WriteInt(2000001); w.WriteByte(0); w.WriteByte(0); w.WriteInt(0); w.WriteInt(0);
            w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(90); w.WriteInt(55);
        }));
        var (e, game) = await enter.WaitAsync(TimeSpan.FromSeconds(10));
        using (game) {
            Assert.That(e.Enter.CharacterId, Is.EqualTo(20000000000)); Assert.That(e.Enter.UnlockedMaps, Is.EqualTo(new[] { 2000001 }));
            Assert.That(e.Sync.Number, Is.EqualTo(0));
            Assert.That(e.Field.MapId, Is.EqualTo(2000001)); Assert.That(e.Field.Position, Is.EqualTo(new System.Numerics.Vector3(1, 2, 3))); Assert.That(e.Field.Portal, Is.EqualTo(55));
        }
        l.Stop();
    }

    /// <summary>The login's adventure level loads (Prestige kinds 0 and 7, GameSession.EnterServer) come between ServerEnter
    /// and RequestFieldEnter: the entry keeps them, and anything else of that window, in arrival order, with ServerEnter's
    /// place, so the replay runs the ServerEnter step (the missions' clear) before them.</summary>
    [Test] public async Task EntryKeepsThePacketsBeforeTheFieldInOrder() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = l.AcceptTcpClientAsync();
        var go = new LoginToGame(0, new byte[] { 127, 0, 0, 1 }, (ushort) port, 1, 2000001, "");
        var enter = GameClient.EnterAsync(go, 10000000000, new byte[16]);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.RequestKey));
        await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(0)));
        await s.WriteAsync(srv.Frame(Inbound.Achieve, w => w.WriteByte(9)));          // before ServerEnter
        await s.WriteAsync(srv.Frame(Inbound.ServerEnter, w => {
            w.WriteInt(7); w.WriteLong(20000000000); w.WriteShort(1); w.WriteLong(100); w.WriteLong(50); w.WriteLong(9);
            for (int i = 0; i < 14; i++) w.WriteLong(i);
            w.WriteUnicodeString("pic"); w.WriteByte(0);
            w.WriteShort(1); w.WriteInt(2000001); w.WriteShort(0);
            w.WriteLong(0); w.WriteUnicodeString(""); w.WriteUnicodeString("u1"); w.WriteUnicodeString("u2"); w.WriteUnicodeString("u3");
        }));
        await s.WriteAsync(srv.Frame(Inbound.SyncNumber, w => w.WriteByte(0)));
        await s.WriteAsync(srv.Frame(Inbound.Prestige, w => { w.WriteByte(0); w.WriteLong(300); w.WriteInt(4); w.WriteLong(900); w.WriteInt(2); w.WriteInt(1); w.WriteInt(3); }));
        await s.WriteAsync(srv.Frame(Inbound.Prestige, w => { w.WriteByte(7); w.WriteInt(1); w.WriteLong(11); w.WriteLong(2); w.WriteByte(0); }));
        await s.WriteAsync(srv.Frame(Inbound.KeyTable, w => w.WriteByte(0)));
        await s.WriteAsync(srv.Frame(Inbound.RequestFieldEnter, w => {
            w.WriteByte(0); w.WriteInt(2000001); w.WriteByte(0); w.WriteByte(0); w.WriteInt(0); w.WriteInt(0);
            w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(90); w.WriteInt(55);
        }));
        var (e, game) = await enter.WaitAsync(TimeSpan.FromSeconds(10));
        using (game) {
            var seen = new List<string>();
            e.Replay(p => {
                var op = game.OpOf(p);
                seen.Add(op == Inbound.Prestige ? "Prestige " + PrestigeMessage.Read(p.Reader()).GetType().Name : op.ToString());
            }, () => seen.Add("ServerEnter"));
            Assert.That(seen, Is.EqualTo(new[] { "Achieve", "ServerEnter", "SyncNumber", "Prestige Loaded", "Prestige MissionList", "KeyTable" }));
            Assert.That(e.EarlyBeforeEnter, Is.EqualTo(1));
        }
        l.Stop();
    }

    [Test] public async Task GlobalFactorIsRequestedAtServerEnterAndTheAnswerKept() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = l.AcceptTcpClientAsync();
        var go = new LoginToGame(0, new byte[] { 127, 0, 0, 1 }, (ushort) port, 0xAABBCCDD11223344UL, 2000001, "");
        var machine = Enumerable.Range(1, 16).Select(i => (byte) i).ToArray();
        var enter = GameClient.EnterAsync(go, 10000000000, machine, requestGlobalFactor: true);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        Assert.That((await ReadFrame(s, srv)).op, Is.EqualTo((ushort) Outbound.ResponseVersion));
        await s.WriteAsync(srv.Frame(Inbound.RequestKey));
        var (op, body) = await ReadFrame(s, srv);
        Assert.That(op, Is.EqualTo((ushort) Outbound.ResponseKey));
        var key = ResponseKey.Read(new ByteReader(body));
        Assert.That(key.AccountId, Is.EqualTo(10000000000)); Assert.That(key.Token, Is.EqualTo(0xAABBCCDD11223344UL)); Assert.That(key.MachineId, Is.EqualTo(machine));
        // the client writes the token as two i32, low half first: the same bytes
        Assert.That(BitConverter.ToInt32(body, 8), Is.EqualTo(0x11223344)); Assert.That(BitConverter.ToInt32(body, 12), Is.EqualTo(unchecked((int) 0xAABBCCDD)));
        await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(0)));
        await s.WriteAsync(srv.Frame(Inbound.ServerEnter, w => {
            w.WriteInt(7); w.WriteLong(20000000000); w.WriteShort(1); w.WriteLong(100); w.WriteLong(50); w.WriteLong(9);
            for (int i = 0; i < 14; i++) w.WriteLong(i);
            w.WriteUnicodeString("pic"); w.WriteByte(0);
            w.WriteShort(1); w.WriteInt(2000001); w.WriteShort(0);
            w.WriteLong(0); w.WriteUnicodeString(""); w.WriteUnicodeString("u1"); w.WriteUnicodeString("u2"); w.WriteUnicodeString("u3");
        }));
        var (gop, gbody) = await ReadFrame(s, srv);   // OnServerEnter sends SendOp::GlobalFactor (0x82), no payload
        Assert.That(gop, Is.EqualTo((ushort) Outbound.GlobalFactor)); Assert.That(gbody, Is.Empty);
        await s.WriteAsync(srv.Frame(Inbound.GlobalFactor, w => { for (int t = 0; t < 6; t++) { w.WriteByte((byte) t); for (int i = 1; i <= 99; i++) w.WriteFloat(1.5f); } }));
        await s.WriteAsync(srv.Frame(Inbound.SyncNumber, w => w.WriteByte(0)));
        await s.WriteAsync(srv.Frame(Inbound.RequestFieldEnter, w => {
            w.WriteByte(0); w.WriteInt(2000001); w.WriteByte(0); w.WriteByte(0); w.WriteInt(0); w.WriteInt(0);
            w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(90); w.WriteInt(55);
        }));
        var (e, game) = await enter.WaitAsync(TimeSpan.FromSeconds(10));
        using (game) {
            int at = e.Early.ToList().FindIndex(p => p.Opcode == (ushort) Inbound.GlobalFactor);   // in the replay, after ServerEnter
            Assert.That(at, Is.GreaterThanOrEqualTo(e.EarlyBeforeEnter));
            var f = e.Early[at];
            var g = new GlobalFactor(); var r = f.Reader();
            Assert.That(g.Apply(r), Is.True); Assert.That(r.Available, Is.EqualTo(0)); Assert.That(g.ExpRate(5, 2), Is.EqualTo(1.5f));
        }
        l.Stop();
    }

    [Test] public async Task GameServerRefusesTheKey() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var srv = new FakeServer(5, 6);
        var accept = l.AcceptTcpClientAsync();
        var go = new LoginToGame(0, new byte[] { 127, 0, 0, 1 }, (ushort) ((IPEndPoint) l.LocalEndpoint).Port, 1, 1, "");
        var enter = GameClient.EnterAsync(go, 1, new byte[16]);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(3)));
        Assert.ThrowsAsync<InvalidDataException>(async () => await enter.WaitAsync(TimeSpan.FromSeconds(10)));
        l.Stop();
    }

    [Test] public void ServerEnterRequestWire() {
        var w = new ByteWriter(); new ServerEnterRequest(3).Write(w);
        Assert.That(new ServerEnterRequest().Op, Is.EqualTo(Outbound.ServerEnter)); Assert.That((ushort) Outbound.ServerEnter, Is.EqualTo(0xB0));
        Assert.That(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()).ReadInt(), Is.EqualTo(3));
    }

    /// <summary>Opt-in: MS2_LIVE_LOGIN=host:port, MS2_LIVE_ACCOUNT=user:password (without the region code), MS2_LIVE_EXPECT=characterId,mapId[,x,y,z]: id and map from the server database, the position only when known.</summary>
    [Test] public async Task LiveServerReachesFieldEnter() {
        var addr = Environment.GetEnvironmentVariable("MS2_LIVE_LOGIN"); var acct = Environment.GetEnvironmentVariable("MS2_LIVE_ACCOUNT");
        if (string.IsNullOrEmpty(addr) || string.IsNullOrEmpty(acct)) Assert.Ignore("MS2_LIVE_LOGIN / MS2_LIVE_ACCOUNT not set");
        var hp = addr.Split(':'); var up = acct.Split(':', 2);
        var machine = new byte[16];
        using var t = new TcpTransport();
        var o = await new LoginClient(t).LoginAsync(hp[0], int.Parse(hp[1]), up[0] + "KR", up[1], machine);
        Assert.That(o.Success, Is.True);
        var ch = o.Characters[0];
        var go = await GameClient.SelectAsync(t, ch.User.CharacterId);
        Assert.That(go.Ok, Is.True, go.Message);
        var (e, game) = await GameClient.EnterAsync(go, o.Result.AccountId, machine, hp[0]);
        using (game) {
            TestContext.Out.WriteLine($"map {e.Field.MapId} pos {e.Field.Position} portal {e.Field.Portal} char {e.Enter.CharacterId} ch {e.Enter.Channel}");
            Assert.That(e.Enter.CharacterId, Is.EqualTo(ch.User.CharacterId));
            var ex = Environment.GetEnvironmentVariable("MS2_LIVE_EXPECT")?.Split(',');
            if (ex != null) {
                Assert.That(long.Parse(ex[0]), Is.EqualTo(ch.User.CharacterId));
                Assert.That(e.Field.MapId, Is.EqualTo(int.Parse(ex[1])));
                if (ex.Length < 5) return; // the character row has no position: the spawn comes from the map
                var p = e.Field.Position;
                Assert.That(p.X, Is.EqualTo(float.Parse(ex[2], System.Globalization.CultureInfo.InvariantCulture)).Within(1));
                Assert.That(p.Y, Is.EqualTo(float.Parse(ex[3], System.Globalization.CultureInfo.InvariantCulture)).Within(1));
                Assert.That(p.Z, Is.EqualTo(float.Parse(ex[4], System.Globalization.CultureInfo.InvariantCulture)).Within(30));
            }
        }
    }
}
