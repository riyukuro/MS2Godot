using System.Net;
using System.Net.Sockets;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The login server return after GameToLogin (CNetworkService::OnGameToLogin @0x14153bc00): the key check with the packet's tokens, ServerEnter, the character list. The packets themselves are in QuitPacketTests.</summary>
public class LoginReturnTests {
    static byte[] Body(Action<ByteWriter> write) { var w = new ByteWriter(); write(w); return w.Buffer[..w.Length]; }

    [Test] public async Task TheLoginServerGetsTheTokensThenServerEnterThenListsTheCharacters() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = l.AcceptTcpClientAsync();
        var machine = Enumerable.Range(1, 16).Select(i => (byte) i).ToArray();
        using var t = new TcpTransport();
        var ret = new LoginClient(t).ReturnAsync("127.0.0.1", (ushort) port, 0x0506070801020304UL, 10000000000, machine);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv);   // ResponseVersion
        await s.WriteAsync(srv.Frame(Inbound.RequestKey));
        var (op, body) = await ReadFrame(s, srv);
        Assert.That(op, Is.EqualTo((ushort) Outbound.ResponseKey));
        var key = ResponseKey.Read(new ByteReader(body));
        Assert.That(key.AccountId, Is.EqualTo(10000000000)); Assert.That(key.Token, Is.EqualTo(0x0506070801020304UL)); Assert.That(key.MachineId, Is.EqualTo(machine));
        await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(0)));
        // OnMoveResult for the login target sends SendServerEnter
        Assert.That((await ReadFrame(s, srv)).op, Is.EqualTo((ushort) Outbound.ServerEnter));
        await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => w.WriteByte((byte) CharacterListCommand.StartList)));
        await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => { w.WriteByte((byte) CharacterListCommand.EndList); w.WriteByte(0); }));
        var o = await ret.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(o.Characters, Is.Empty);
        l.Stop();
    }

    [Test] public async Task ARefusedKeyThrowsTheMoveError() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = l.AcceptTcpClientAsync();
        using var t = new TcpTransport();
        var ret = new LoginClient(t).ReturnAsync("127.0.0.1", (ushort) port, 1, 1, null);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.RequestKey));
        await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(9)));
        var e = Assert.ThrowsAsync<MoveRefusedException>(async () => await ret.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.That(e.Error, Is.EqualTo(9));
        // no ServerEnter follows the refusal
        Assert.ThrowsAsync<TimeoutException>(async () => await ReadFrame(s, srv).WaitAsync(TimeSpan.FromMilliseconds(400)), "nothing is sent after a refused key");
        l.Stop();
    }

    [Test] public async Task TheSceneSwitchesAtMoveResultBeforeServerEnterAndTheList() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = l.AcceptTcpClientAsync();
        var events = new List<string>();
        using var t = new TcpTransport();
        var ret = new LoginClient(t).ReturnAsync("127.0.0.1", (ushort) port, 1, 1, null, onMoveResult: () => events.Add("switch"));
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame); await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.RequestKey)); await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(0)));
        Assert.That((await ReadFrame(s, srv)).op, Is.EqualTo((ushort) Outbound.ServerEnter));
        Assert.That(events, Is.EqualTo(new[] { "switch" }), "switched before any character list arrived");
        await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => w.WriteByte((byte) CharacterListCommand.StartList)));
        await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => { w.WriteByte((byte) CharacterListCommand.EndList); w.WriteByte(0); }));
        await ret.WaitAsync(TimeSpan.FromSeconds(10));
        l.Stop();
    }

    [Test] public async Task ARefusedKeySwitchesNothing() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = l.AcceptTcpClientAsync();
        int switched = 0;
        using var t = new TcpTransport();
        var ret = new LoginClient(t).ReturnAsync("127.0.0.1", (ushort) port, 1, 1, null, onMoveResult: () => switched++);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame); await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.RequestKey)); await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(9)));
        Assert.ThrowsAsync<MoveRefusedException>(async () => await ret.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.That(switched, Is.EqualTo(0));
        l.Stop();
    }

    /// <summary>A server that takes <paramref name="dropFirst"/> connections and closes each at once, then serves the return.</summary>
    static async Task<(TcpListener l, int port, Func<int> accepted, Task serve)> FlakyServer(int dropFirst) {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port, count = 0;
        var serve = Task.Run(async () => {
            try {
                while (true) {
                    var c = await l.AcceptTcpClientAsync();
                    if (Interlocked.Increment(ref count) <= dropFirst) { c.Close(); continue; }
                    var srv = new FakeServer(5, 6); var s = c.GetStream();
                    await s.WriteAsync(srv.HelloFrame); await ReadFrame(s, srv);
                    await s.WriteAsync(srv.Frame(Inbound.RequestKey)); await ReadFrame(s, srv);
                    await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(0))); await ReadFrame(s, srv);
                    await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => w.WriteByte((byte) CharacterListCommand.StartList)));
                    await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => { w.WriteByte((byte) CharacterListCommand.EndList); w.WriteByte(0); }));
                }
            } catch (Exception) { }
        });
        await Task.CompletedTask;
        return (l, port, () => Volatile.Read(ref count), serve);
    }

    [Test] public async Task AConnectThatFailsIsTriedAgainUpToSixTimes() {
        var (l, port, accepted, _) = await FlakyServer(dropFirst: 5);
        int switched = 0; var attempts = new List<int>();
        var (o, t) = await LoginClient.ReturnWithRetriesAsync("127.0.0.1", (ushort) port, 1, 1, null, () => switched++, waitMs: 100, onAttempt: attempts.Add);
        using var _ = t;
        Assert.That(attempts, Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
        Assert.That(accepted(), Is.EqualTo(6));
        Assert.That(switched, Is.EqualTo(1), "the scene switch comes once, at the good MoveResult");
        Assert.That(o.Characters, Is.Empty);
        l.Stop();
    }

    [Test] public async Task ASixthFailureGivesUpWithoutASeventhAttempt() {
        var (l, port, accepted, _) = await FlakyServer(dropFirst: 99);
        int switched = 0;
        var e = Assert.ThrowsAsync<ReconnectGaveUpException>(async () => await LoginClient.ReturnWithRetriesAsync("127.0.0.1", (ushort) port, 1, 1, null, () => switched++, waitMs: 100));
        Assert.That(e.Attempts, Is.EqualTo(6));
        await Task.Delay(300);
        Assert.That(accepted(), Is.EqualTo(6), "ConnectTo six times, the retry counter is checked at most 5 (scenestategame.cpp:1332)");
        Assert.That(switched, Is.EqualTo(0));
        l.Stop();
    }

    static async Task<(ushort op, byte[] body)> ReadFrame(NetworkStream s, FakeServer srv) {
        var head = new byte[6]; await s.ReadExactlyAsync(head);
        var frame = new byte[6 + BitConverter.ToInt32(head, 2)]; head.CopyTo(frame, 0);
        await s.ReadExactlyAsync(frame.AsMemory(6));
        return srv.Read(frame);
    }
}
