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

    // CSceneStateGame::OnRequestLogin @0x14170dc90 (scenestategame.cpp:2054-2064): the game scene state answers the login server's
    // RequestLogin with ResponseKey, as OnRequestKey does; the return runs in that state
    [Test] public async Task ARequestLoginGetsTheKeyAsARequestKeyDoes() {
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
        await s.WriteAsync(srv.Frame(Inbound.RequestLogin));
        var (op, body) = await ReadFrame(s, srv).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(op, Is.EqualTo((ushort) Outbound.ResponseKey));
        var key = ResponseKey.Read(new ByteReader(body));
        Assert.That(key.AccountId, Is.EqualTo(10000000000)); Assert.That(key.Token, Is.EqualTo(0x0506070801020304UL)); Assert.That(key.MachineId, Is.EqualTo(machine));
        await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(0)));
        Assert.That((await ReadFrame(s, srv)).op, Is.EqualTo((ushort) Outbound.ServerEnter));
        await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => w.WriteByte((byte) CharacterListCommand.StartList)));
        await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => { w.WriteByte((byte) CharacterListCommand.EndList); w.WriteByte(0); }));
        Assert.That((await ret.WaitAsync(TimeSpan.FromSeconds(10))).Characters, Is.Empty);
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

    // OnGameToLogin @0x14153bc00 (networkservice.cpp:928-937) stores the packet's ip and port and the return connects there;
    // the host the login was made to stands in only for a packet with no address (0.0.0.0)
    [Test] public void TheReturnGoesToThePacketsAddressNotTheLaunchHost() {
        var m = new GameToLogin(0, new byte[] { 203, 0, 113, 7 }, 20001, 1, 2);
        Assert.That(GameClient.HostOf(m.Address, "127.0.0.1"), Is.EqualTo("203.0.113.7"));
        Assert.That(GameClient.HostOf(m.Address, "example.org"), Is.EqualTo("203.0.113.7"));
        Assert.That(GameClient.HostOf(new GameToLogin(0, new byte[4], 20001, 1, 2).Address, "example.org"), Is.EqualTo("example.org"), "0.0.0.0: the launch host");
        Assert.That(GameClient.HostOf(new GameToLogin(0, new byte[4], 20001, 1, 2).Address, null), Is.EqualTo("127.0.0.1"));
        Assert.That(GameClient.HostOf(new LoginToGame(0, new byte[] { 10, 0, 0, 5 }, 20002, 1, 2000001, "").Address, "127.0.0.1"), Is.EqualTo("10.0.0.5"));
        Assert.That(GameClient.HostOf(new GameToGame(0, 1, 2, new byte[] { 10, 0, 0, 6 }, 20003, 2000001, 0).Address, "127.0.0.1"), Is.EqualTo("10.0.0.6"));
        Assert.That(GameClient.EndpointText("203.0.113.7", 20001), Is.EqualTo("203.0.113.7:20001"));
    }

    static IPAddress NonLoopbackV4() =>
        System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(u => u.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));

    /// <summary>Every connection <see cref="ReturnServer"/> accepted, kept until the fixture ends. Its connection tasks park on
    /// Task.Delay(Timeout.Infinite), which has no timer: nothing else references such a task, so without this a collection frees the
    /// TcpClient and its finalizer closes the connection, and a wait that should time out sees the close instead.</summary>
    static readonly System.Collections.Concurrent.ConcurrentQueue<TcpClient> Held = new();
    [OneTimeTearDown] public void CloseHeld() { while (Held.TryDequeue(out var c)) c.Dispose(); }

    /// <summary>A login server for the return on <paramref name="bind"/>: it waits <paramref name="helloDelayMs"/> before the hello, and
    /// with <paramref name="silent"/> says nothing after it. Each accepted connection's local address (where the client connected) is kept.</summary>
    static (TcpListener l, int port, System.Collections.Concurrent.ConcurrentQueue<IPAddress> to) ReturnServer(IPAddress bind, int helloDelayMs = 0, bool silent = false) {
        var l = new TcpListener(bind, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port;
        var to = new System.Collections.Concurrent.ConcurrentQueue<IPAddress>();
        _ = Task.Run(async () => {
            try {
                while (true) {
                    var c = await l.AcceptTcpClientAsync();
                    Held.Enqueue(c);
                    to.Enqueue(((IPEndPoint) c.Client.LocalEndPoint).Address);
                    _ = Task.Run(async () => {
                        try {
                            var srv = new FakeServer(5, 6); var s = c.GetStream();
                            await Task.Delay(helloDelayMs);
                            await s.WriteAsync(srv.HelloFrame); await ReadFrame(s, srv);
                            if (silent) { await Task.Delay(Timeout.Infinite); return; }
                            await s.WriteAsync(srv.Frame(Inbound.RequestKey)); await ReadFrame(s, srv);
                            await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(0))); await ReadFrame(s, srv);
                            await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => w.WriteByte((byte) CharacterListCommand.StartList)));
                            await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => { w.WriteByte((byte) CharacterListCommand.EndList); w.WriteByte(0); }));
                            await Task.Delay(Timeout.Infinite);
                        } catch (Exception) { }
                    });
                }
            } catch (Exception) { }
        });
        return (l, port, to);
    }

    // the user's case: a login server on a public, non-loopback address, the launch host something else. The connect reaches the packet's address.
    [Test] public async Task AReturnToANonLoopbackAddressConnectsThere() {
        var lan = NonLoopbackV4();
        if (lan == null) Assert.Ignore("no non-loopback IPv4 address on this machine");
        var (l, port, to) = ReturnServer(IPAddress.Any);
        var m = new GameToLogin(0, lan.GetAddressBytes(), (ushort) port, 1, 2);
        var (o, t) = await LoginClient.ReturnWithRetriesAsync(GameClient.HostOf(m.Address, "127.0.0.1"), m.Port, m.Token, 1, null, waitMs: 2000).WaitAsync(TimeSpan.FromSeconds(20));
        using var _ = t;
        Assert.That(o.Characters, Is.Empty);
        Assert.That(to.ToArray(), Is.EqualTo(new[] { lan }), "one connection, to the packet's address");
        l.Stop();
    }

    // a slow hello is not a failed connect: ConnectTo's 10 s bound is the TCP connect, the 15 s wait is armed on the new connection
    // (after its connect), so a hello late by more than the connect wait still ends at the list, on one connection
    [Test] public async Task ALateHelloIsWaitedForOnTheSameConnection() {
        var (l, port, to) = ReturnServer(IPAddress.Loopback, helloDelayMs: 1500);
        var attempts = new List<int>();
        var (o, t) = await LoginClient.ReturnWithRetriesAsync("127.0.0.1", (ushort) port, 1, 1, null, waitMs: 500, onAttempt: attempts.Add).WaitAsync(TimeSpan.FromSeconds(20));
        using var _ = t;
        Assert.That(o.Characters, Is.Empty);
        Assert.That(attempts, Is.EqualTo(new[] { 1 }));
        Assert.That(to.Count, Is.EqualTo(1));
        l.Stop();
    }

    // a hello and then nothing: the timeout names the endpoint it waited on; with no hello at all it says so. Collections run
    // through the wait, as under a loaded suite: the silent server's connection must stay open until the timeout.
    [Test] public async Task AReturnTimeoutNamesTheEndpoint() {
        using var stopGc = new CancellationTokenSource();
        var gc = Task.Run(async () => { while (!stopGc.IsCancellationRequested) { GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(10); } });
        var (l, port, _) = ReturnServer(IPAddress.Loopback, silent: true);
        using (var t = new TcpTransport()) {
            var e = Assert.ThrowsAsync<TimeoutException>(async () => await new LoginClient(t).ReturnAsync("127.0.0.1", (ushort) port, 1, 1, null, timeoutMs: 500));
            Assert.That(e.Message, Is.EqualTo($"login return timed out (127.0.0.1:{port})"));
        }
        l.Stop();
        var (l2, port2, _) = ReturnServer(IPAddress.Loopback, helloDelayMs: Timeout.Infinite);
        using (var t = new TcpTransport()) {
            var e = Assert.ThrowsAsync<TimeoutException>(async () => await new LoginClient(t).ReturnAsync("127.0.0.1", (ushort) port2, 1, 1, null, timeoutMs: 500));
            Assert.That(e.Message, Is.EqualTo($"login return timed out (127.0.0.1:{port2}, no hello)"));
        }
        l2.Stop();
        stopGc.Cancel(); await gc;
    }

    // a retry is a new transport: what the dropped attempt's connection still had queued is never read by the next one
    [Test] public async Task ARetryReadsOnlyItsOwnConnection() {
        var (l, port, accepted, _) = await FlakyServer(dropFirst: 1);
        var (o, t) = await LoginClient.ReturnWithRetriesAsync("127.0.0.1", (ushort) port, 1, 1, null, waitMs: 100).WaitAsync(TimeSpan.FromSeconds(20));
        using var _ = t;
        Assert.That(accepted(), Is.EqualTo(2));
        Assert.That(t.IsClosed, Is.False);
        Assert.That(t.Reconnection.IsReconnecting, Is.False, "the return's connection is not in game: a drop closes it, no reconnect hides it");
        l.Stop();
    }

    static async Task<(ushort op, byte[] body)> ReadFrame(NetworkStream s, FakeServer srv) {
        var head = new byte[6]; await s.ReadExactlyAsync(head);
        var frame = new byte[6 + BitConverter.ToInt32(head, 2)]; head.CopyTo(frame, 0);
        await s.ReadExactlyAsync(frame.AsMemory(6));
        return srv.Read(frame);
    }
}
