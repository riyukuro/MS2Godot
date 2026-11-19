using System.Net;
using System.Net.Sockets;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// A field move in game: after the login's entry the server sends RequestFieldEnter again (a portal, /warp, a dungeon), and
/// the client takes it the same way (CSceneStateGame::OnRequestFieldEnter): the field changes and ResponseFieldEnter goes
/// out once, with the new entry's portal.
/// </summary>
public class FieldMoveTests {
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

    static Action<ByteWriter> FieldEnter(int map, int portal, byte type = 0) => w => {
        w.WriteByte(0); w.WriteInt(map); w.WriteByte(type); w.WriteByte(0); w.WriteInt(0); w.WriteInt(0);
        w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(90); w.WriteInt(portal);
    };

    /// <summary>The login up to its RequestFieldEnter (map 2000001, portal 55), as GameClientTests' entry.</summary>
    static async Task<(NetworkStream s, TcpListener l, FakeServer srv, GameEntry e, TcpTransport game)> Enter() {
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
        await s.WriteAsync(srv.Frame(Inbound.ServerEnter, w => {
            w.WriteInt(7); w.WriteLong(20000000000); w.WriteShort(1); w.WriteLong(100); w.WriteLong(50); w.WriteLong(9);
            for (int i = 0; i < 14; i++) w.WriteLong(i);
            w.WriteUnicodeString("pic"); w.WriteByte(0);
            w.WriteShort(1); w.WriteInt(2000001); w.WriteShort(0);
            w.WriteLong(0); w.WriteUnicodeString(""); w.WriteUnicodeString("u1"); w.WriteUnicodeString("u2"); w.WriteUnicodeString("u3");
        }));
        await s.WriteAsync(srv.Frame(Inbound.SyncNumber, w => w.WriteByte(0)));
        await s.WriteAsync(srv.Frame(Inbound.RequestFieldEnter, FieldEnter(2000001, 55)));
        var (e, game) = await enter.WaitAsync(TimeSpan.FromSeconds(10));
        return (s, l, srv, e, game);
    }

    /// <summary>What LiveSession does with a game packet that is RequestFieldEnter: the entry takes it.</summary>
    static bool Route(TcpTransport game, RawPacket p, FieldEntry entry)
        => game.Is(p, Inbound.RequestFieldEnter) && entry.Enter(RequestFieldEnter.Read(p.Reader()));

    [Test] public async Task InGameFieldEnterChangesTheFieldAndAnswersOnce() {
        var (s, l, srv, e, game) = await Enter();
        using (game) {
            var entry = new FieldEntry(e.Field);
            // the login's field loads: its one answer
            await game.SendAsync(entry.Answer());
            Assert.That(entry.Answer(), Is.Null);
            var (op, body) = await ReadFrame(s, srv);
            Assert.That(op, Is.EqualTo((ushort) Outbound.ResponseFieldEnter));
            Assert.That(ResponseFieldEnter.Read(new ByteReader(body)).Portal, Is.EqualTo(55));

            // in game: the server moves the character to another map
            await s.WriteAsync(srv.Frame(Inbound.RequestFieldEnter, FieldEnter(2000062, 77)));
            Assert.That(Route(game, await Dequeue(game), entry), Is.True);
            Assert.That(entry.Current.MapId, Is.EqualTo(2000062));
            Assert.That(entry.Entries, Is.EqualTo(2));
            // the new field loads (the flow and the field both report it): one answer goes out
            foreach (var _ in new[] { 1, 2 }) { var a = entry.Answer(); if (a != null) await game.SendAsync(a); }
            await game.SendAsync(new GlobalFactorRequest());      // a marker: the next frame after the answer
            (op, body) = await ReadFrame(s, srv);
            Assert.That(op, Is.EqualTo((ushort) Outbound.ResponseFieldEnter));
            Assert.That(ResponseFieldEnter.Read(new ByteReader(body)).Portal, Is.EqualTo(77));
            (op, _) = await ReadFrame(s, srv);
            Assert.That(op, Is.EqualTo((ushort) Outbound.GlobalFactor), "ResponseFieldEnter went out more than once");
        }
        l.Stop();
    }

    [Test] public async Task InGameFieldEnterErrorKeepsTheField() {
        var (s, l, srv, e, game) = await Enter();
        using (game) {
            var entry = new FieldEntry(e.Field);
            entry.Answer();
            await s.WriteAsync(srv.Frame(Inbound.RequestFieldEnter, w => w.WriteByte(0x1d)));
            var p = await Dequeue(game);
            var f = RequestFieldEnter.Read(p.Reader());
            Assert.That(f.Error, Is.EqualTo(0x1d));
            Assert.That(entry.Enter(f), Is.False);
            Assert.That(entry.Current.MapId, Is.EqualTo(2000001));
            Assert.That(entry.Answer(), Is.Null, "an error is not answered");
        }
        l.Stop();
    }

    [Test] public void PortalMoveRequestLayout() {
        var w = new ByteWriter();
        new RequestMoveFieldPortal(2000001, 3).Write(w);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        Assert.That(r.ReadByte(), Is.EqualTo(0));
        Assert.That(r.ReadInt(), Is.EqualTo(2000001)); Assert.That(r.ReadInt(), Is.EqualTo(3));
        Assert.That(r.ReadInt(), Is.EqualTo(0)); Assert.That(r.ReadInt(), Is.EqualTo(0));
        Assert.That(r.ReadUnicodeString(), Is.EqualTo("")); Assert.That(r.ReadUnicodeString(), Is.EqualTo(""));
        Assert.That(w.Length, Is.EqualTo(1 + 16 + 4));
        Assert.That(new RequestMoveFieldPortal(1, 2).Op, Is.EqualTo(Outbound.RequestMoveField));
        // the password portal's password is the second string (+0x18)
        w = new ByteWriter();
        new RequestMoveFieldPortal(1, 2, Password: "pw").Write(w);
        r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        r.ReadByte(); for (int i = 0; i < 4; i++) r.ReadInt();
        Assert.That(r.ReadUnicodeString(), Is.EqualTo("")); Assert.That(r.ReadUnicodeString(), Is.EqualTo("pw"));
    }
}
