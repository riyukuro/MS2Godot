using System.Net;
using System.Net.Sockets;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The resend buffer on a real connection: OnGvg kind 43 turns the capture on (networkservice.cpp:820), every send is numbered and copied, OnPingRequest
/// drops the acknowledged copies (:876), OnRetransmitRequest sends ReconnectUnk, the packets after the acknowledged number and FinalizeReconnect (:884).
/// </summary>
public class ResendOverSocketTests {
    static async Task<(ushort op, byte[] body)> ReadFrame(NetworkStream s, FakeServer srv) {
        var head = new byte[6]; await s.ReadExactlyAsync(head);
        var frame = new byte[6 + BitConverter.ToInt32(head, 2)]; head.CopyTo(frame, 0);
        await s.ReadExactlyAsync(frame.AsMemory(6));
        return srv.Read(frame);
    }

    static async Task<(TcpTransport t, NetworkStream s, FakeServer srv)> Connected(bool capture) {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = l.AcceptTcpClientAsync();
        var t = new TcpTransport();
        await t.ConnectAsync("127.0.0.1", port);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv);   // ResponseVersion
        if (capture) {
            await s.WriteAsync(srv.Frame(Inbound.Gvg, w => {
                w.WriteInt(1); w.WriteInt(43);
                for (int i = 0; i < 11; i++) w.WriteInt(i == 0 ? 1 : i == 1 ? 100000 : 0);   // enabled, buffer limit
            }));
        }
        await s.WriteAsync(srv.Frame(Inbound.RequestKey));
        await t.SendAsync(new ResponseKey(1, 2, new byte[16]));
        await ReadFrame(s, srv);   // ResponseKey
        return (t, s, srv);
    }

    static async Task WaitFor(Func<bool> done) {
        for (int i = 0; i < 400 && !done(); i++) await Task.Delay(10);
        Assert.That(done(), Is.True, "timed out");
    }

    [Test] public async Task AFinalizeAfterSomeAcksResendsOnlyThePacketsAfterTheAckedOne() {
        var (t, s, srv) = await Connected(capture: true);
        using var _ = t;
        await WaitFor(() => t.Channel.Out.CaptureEnabled);
        for (byte m = 10; m < 14; m++) { await t.SendAsync(new RequestQuit(m)); await ReadFrame(s, srv); }
        Assert.That(t.Channel.Out.Sequence, Is.EqualTo(4), "ResponseKey's reset put the count back to 0");
        await s.WriteAsync(srv.Frame(Inbound.Ping, w => w.WriteInt(2)));   // the server saw packets 1 and 2
        Assert.That((await ReadFrame(s, srv)).op, Is.EqualTo((ushort) Outbound.Pong));
        await s.WriteAsync(srv.Frame(Inbound.FinalizeReconnect, w => { w.WriteInt(0); w.WriteInt(2); }));
        var got = new List<(ushort op, byte[] body)>();
        for (int i = 0; i < 4; i++) got.Add(await ReadFrame(s, srv));
        Assert.That(got.Select(g => g.op), Is.EqualTo(new[] { (ushort) Outbound.ReconnectUnk, (ushort) Outbound.RequestQuit, (ushort) Outbound.RequestQuit, (ushort) Outbound.FinalizeReconnect }));
        Assert.That(BitConverter.ToInt32(got[0].body), Is.EqualTo(2));
        Assert.That(got[1].body, Is.EqualTo(new byte[] { 12 })); Assert.That(got[2].body, Is.EqualTo(new byte[] { 13 }), "no duplicate of 10 or 11, which were acked");
        Assert.That(BitConverter.ToInt32(got[3].body), Is.EqualTo(5), "the Pong is numbered too (it is only not kept)");
    }

    [Test] public async Task WithoutTheOptionNothingIsKeptAndNothingIsResent() {
        var (t, s, srv) = await Connected(capture: false);
        using var _ = t;
        await t.SendAsync(new RequestQuit(10)); await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.FinalizeReconnect, w => { w.WriteInt(0); w.WriteInt(0); }));
        Assert.That((await ReadFrame(s, srv)).op, Is.EqualTo((ushort) Outbound.ReconnectUnk));
        Assert.That((await ReadFrame(s, srv)).op, Is.EqualTo((ushort) Outbound.FinalizeReconnect));
    }

    [Test] public async Task ASocketDropMidFieldReconnectsAndResendsOnlyThePacketsAfterTheAckedOne() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = l.AcceptTcpClientAsync();
        var t = new TcpTransport { InGame = () => true };
        using var _ = t;
        await t.ConnectAsync("127.0.0.1", port);
        var first = await accept;
        var s = first.GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.Gvg, w => {
            w.WriteInt(1); w.WriteInt(43);
            var o = new int[ReconnectionOptions.Count];
            o[ReconnectionOptions.Enabled] = 1; o[ReconnectionOptions.BufferLimit] = 100000; o[ReconnectionOptions.GiveUpSeconds] = 30;
            o[ReconnectionOptions.MaxAttempts] = 5; o[ReconnectionOptions.RetryDelaySeconds] = 1;
            foreach (var v in o) w.WriteInt(v);
        }));
        await s.WriteAsync(srv.Frame(Inbound.RequestKey));
        await t.SendAsync(new ResponseKey(1, 2, new byte[16]));
        await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.Reconnect, w => { w.WriteByte(ReconnectNotice.KindContext); w.WriteInt(1); w.WriteString("127.0.0.1"); w.WriteInt(port); }));
        await WaitFor(() => t.Reconnection.Context.Mode == 1 && t.Channel.Out.CaptureEnabled);
        for (byte m = 10; m < 14; m++) { await t.SendAsync(new RequestQuit(m)); await ReadFrame(s, srv); }

        var srv2 = new FakeServer(5, 6);   // a new connection: new cipher state
        var accept2 = l.AcceptTcpClientAsync();
        first.Close();   // the drop
        var s2 = (await accept2).GetStream();
        await s2.WriteAsync(srv2.HelloFrame);
        await ReadFrame(s2, srv2);   // ResponseVersion
        await s2.WriteAsync(srv2.Frame(Inbound.RequestKey));
        var req = await ReadFrame(s2, srv2);
        Assert.That(req.op, Is.EqualTo((ushort) Outbound.Reconnect), "RequestKey while reconnecting is answered with the reconnect request");
        await s2.WriteAsync(srv2.Frame(Inbound.Reconnect, w => { w.WriteByte(ReconnectNotice.KindResult); w.WriteInt(1); }));
        await s2.WriteAsync(srv2.Frame(Inbound.FinalizeReconnect, w => { w.WriteInt(0); w.WriteInt(2); }));   // the server got packets 1 and 2
        var got = new List<(ushort op, byte[] body)>();
        for (int i = 0; i < 4; i++) got.Add(await ReadFrame(s2, srv2));
        Assert.That(got.Select(g => g.op), Is.EqualTo(new[] { (ushort) Outbound.ReconnectUnk, (ushort) Outbound.RequestQuit, (ushort) Outbound.RequestQuit, (ushort) Outbound.FinalizeReconnect }));
        Assert.That(got[1].body, Is.EqualTo(new byte[] { 12 })); Assert.That(got[2].body, Is.EqualTo(new byte[] { 13 }));
        Assert.That(t.Reconnection.IsReconnecting, Is.False, "the good result ended the reconnect");
        Assert.That(t.IsClosed, Is.False);
    }

    /// <summary>CGameSocket::SendPacket (gamesocket.cpp:279-290): a packet sent while the socket is down or while a reconnect runs is held, and goes out once, in order, ahead of the next send.</summary>
    [Test] public async Task PacketsSentDuringEveryReconnectPhaseGoOutOnceInTheClientsOrder() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = l.AcceptTcpClientAsync();
        var t = new TcpTransport { InGame = () => true };
        using var _ = t;
        await t.ConnectAsync("127.0.0.1", port);
        var first = await accept;
        var s = first.GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.Gvg, w => {
            w.WriteInt(1); w.WriteInt(43);
            var o = new int[ReconnectionOptions.Count];
            o[ReconnectionOptions.Enabled] = 1; o[ReconnectionOptions.BufferLimit] = 100000; o[ReconnectionOptions.GiveUpSeconds] = 30;
            o[ReconnectionOptions.MaxAttempts] = 5; o[ReconnectionOptions.RetryDelaySeconds] = 1;
            foreach (var v in o) w.WriteInt(v);
        }));
        await s.WriteAsync(srv.Frame(Inbound.RequestKey));
        await t.SendAsync(new ResponseKey(1, 2, new byte[16]));
        await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.Reconnect, w => { w.WriteByte(ReconnectNotice.KindContext); w.WriteInt(1); w.WriteString("127.0.0.1"); w.WriteInt(port); }));
        await WaitFor(() => t.Reconnection.Context.Mode == 1 && t.Channel.Out.CaptureEnabled);
        for (byte m = 10; m < 12; m++) { await t.SendAsync(new RequestQuit(m)); await ReadFrame(s, srv); }

        var srv2 = new FakeServer(5, 6);
        var accept2 = l.AcceptTcpClientAsync();
        first.Close();
        await WaitFor(() => t.Reconnection.IsReconnecting);
        await t.SendAsync(new RequestQuit(20));   // phase 1: the socket is down
        var s2 = (await accept2).GetStream();
        await t.SendAsync(new RequestQuit(21));   // phase 2: connected, before the handshake
        await s2.WriteAsync(srv2.HelloFrame);
        var quits = new List<byte>();
        (ushort op, byte[] body) f;
        // ResponseVersion and the reconnect request are sends like any other: the packets held so far go out ahead of each (FUN_140208960)
        async Task ReadUntil(Outbound stop) {
            while ((f = await ReadFrame(s2, srv2).WaitAsync(TimeSpan.FromSeconds(5))).op != (ushort) stop) { Assert.That(f.op, Is.EqualTo((ushort) Outbound.RequestQuit)); quits.Add(f.body[0]); }
        }
        await ReadUntil(Outbound.ResponseVersion);
        await s2.WriteAsync(srv2.Frame(Inbound.RequestKey));
        await ReadUntil(Outbound.Reconnect);
        await t.SendAsync(new RequestQuit(22));   // phase 3: after the handshake, before FinalizeReconnect
        await s2.WriteAsync(srv2.Frame(Inbound.Reconnect, w => { w.WriteByte(ReconnectNotice.KindResult); w.WriteInt(1); }));
        await s2.WriteAsync(srv2.Frame(Inbound.FinalizeReconnect, w => { w.WriteInt(0); w.WriteInt(2); }));
        await WaitFor(() => !t.Reconnection.IsReconnecting);

        await t.SendAsync(new RequestQuit(23));   // the reconnect is over: anything still held goes out ahead of this
        do {
            f = await ReadFrame(s2, srv2).WaitAsync(TimeSpan.FromSeconds(5));
            if (f.op == (ushort) Outbound.RequestQuit) quits.Add(f.body[0]);
        } while (!(f.op == (ushort) Outbound.RequestQuit && f.body[0] == 23));
        Assert.That(quits.Where(q => q >= 20), Is.EqualTo(new byte[] { 20, 21, 22, 23 }), "each held packet once, in the order they were sent");
        Assert.That(t.IsClosed, Is.False);
    }

    /// <summary>The heartbeat answer goes through SendPacket too (networkservice.cpp:746): between the reconnect request and its result it is held, so it does not flush the held packets ahead of the retransmit (a duplicate).</summary>
    [Test] public async Task AHeartbeatAnsweredWhileReconnectingDoesNotFlushHeldPacketsAheadOfTheRetransmit() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = l.AcceptTcpClientAsync();
        var t = new TcpTransport { InGame = () => true };
        using var _ = t;
        await t.ConnectAsync("127.0.0.1", port);
        var first = await accept;
        var s = first.GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.Gvg, w => {
            w.WriteInt(1); w.WriteInt(43);
            var o = new int[ReconnectionOptions.Count];
            o[ReconnectionOptions.Enabled] = 1; o[ReconnectionOptions.BufferLimit] = 100000; o[ReconnectionOptions.GiveUpSeconds] = 30;
            o[ReconnectionOptions.MaxAttempts] = 5; o[ReconnectionOptions.RetryDelaySeconds] = 1;
            foreach (var v in o) w.WriteInt(v);
        }));
        await s.WriteAsync(srv.Frame(Inbound.RequestKey));
        await t.SendAsync(new ResponseKey(1, 2, new byte[16]));
        await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.Reconnect, w => { w.WriteByte(ReconnectNotice.KindContext); w.WriteInt(1); w.WriteString("127.0.0.1"); w.WriteInt(port); }));
        await WaitFor(() => t.Reconnection.Context.Mode == 1 && t.Channel.Out.CaptureEnabled);
        for (byte m = 10; m < 12; m++) { await t.SendAsync(new RequestQuit(m)); await ReadFrame(s, srv); }

        var srv2 = new FakeServer(5, 6);
        var accept2 = l.AcceptTcpClientAsync();
        first.Close();
        await WaitFor(() => t.Reconnection.IsReconnecting);
        var s2 = (await accept2).GetStream();
        await s2.WriteAsync(srv2.HelloFrame);
        (ushort op, byte[] body) f;
        while ((f = await ReadFrame(s2, srv2).WaitAsync(TimeSpan.FromSeconds(5))).op != (ushort) Outbound.ResponseVersion) { }
        await s2.WriteAsync(srv2.Frame(Inbound.RequestKey));
        while ((f = await ReadFrame(s2, srv2).WaitAsync(TimeSpan.FromSeconds(5))).op != (ushort) Outbound.Reconnect) { }
        await t.SendAsync(new RequestQuit(20));   // held: the reconnect runs
        await s2.WriteAsync(srv2.Frame(Inbound.RequestHeartbeat, w => w.WriteInt(77)));   // answered inside the window
        // the real condition, not a fixed delay: the client took the heartbeat answer into the hold path (RequestQuit 20 and the answer are both pending)
        await WaitFor(() => t.Channel.Out.PendingCount >= 2);
        Assert.That(t.Channel.Out.PendingCount, Is.EqualTo(2), "the heartbeat answer is held with the quit, not sent ahead");
        await s2.WriteAsync(srv2.Frame(Inbound.Reconnect, w => { w.WriteByte(ReconnectNotice.KindResult); w.WriteInt(1); }));
        await s2.WriteAsync(srv2.Frame(Inbound.FinalizeReconnect, w => { w.WriteInt(0); w.WriteInt(2); }));   // the server got packets 1 and 2
        var ops = new List<(ushort op, byte[] body)>();
        await WaitFor(() => !t.Reconnection.IsReconnecting);
        await t.SendAsync(new RequestQuit(23));
        do { f = await ReadFrame(s2, srv2).WaitAsync(TimeSpan.FromSeconds(5)); ops.Add(f); } while (!(f.op == (ushort) Outbound.RequestQuit && f.body[0] == 23));
        int unk = ops.FindIndex(o => o.op == (ushort) Outbound.ReconnectUnk);
        int q20 = ops.FindIndex(o => o.op == (ushort) Outbound.RequestQuit && o.body[0] == 20);
        Assert.That(unk, Is.GreaterThanOrEqualTo(0));
        Assert.That(q20, Is.GreaterThan(unk), "the held packet is not flushed ahead of the retransmit");
        Assert.That(ops.Count(o => o.op == (ushort) Outbound.RequestQuit && o.body[0] == 20), Is.EqualTo(1), "no duplicate");
    }
}
