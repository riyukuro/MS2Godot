using System.Net;
using System.Net.Sockets;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Reconnect, FinalizeReconnect and Gvg session behaviour (gamesocket.cpp:315-321, 405-418; networkservice.cpp:811-889;
/// reconnectionmanagercli.cpp:174, 243-332; windowservice.cpp:558-578; unit_14067c020_machineidcodec.cpp:139).
/// </summary>
public class ReconnectSessionTests {
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

    static async Task WaitFor(Func<bool> done) {
        for (int i = 0; i < 400 && !done(); i++) await Task.Delay(10);
        Assert.That(done(), Is.True, "timed out");
    }

    /// <summary>
    /// OnRequestKey's ResponseKey resets CGameSocket::m_sequence to 0 (gamesocket.cpp:406); BeginReconnect snapshots
    /// GetSocketSequence = m_sequence (inbound frames since that reset, NOT m_out's outbound sequence) and sends it in
    /// ReconnectRequest (reconnectionmanagercli.cpp:174, 250).
    /// </summary>
    [Test] public async Task SocketSequenceSentInReconnectRequestIsInboundCountSinceResponseKey() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint) listener.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = listener.AcceptTcpClientAsync();
        using var t = new TcpTransport { InGame = () => true };
        var idBytes = Enumerable.Range(0, 16).Select(i => (byte) (i * 0x11)).ToArray();
        t.Reconnection.SetIdentity(11, 22, MachineIdText.Format(idBytes));
        await t.ConnectAsync("127.0.0.1", port);
        var first = await accept;
        var s = first.GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv);   // ResponseVersion
        await s.WriteAsync(srv.Frame(Inbound.Gvg, w => {
            w.WriteInt(1); w.WriteInt(43);
            var o = new int[ReconnectionOptions.Count];
            o[ReconnectionOptions.Enabled] = 1; o[ReconnectionOptions.BufferLimit] = 100000; o[ReconnectionOptions.GiveUpSeconds] = 30;
            o[ReconnectionOptions.MaxAttempts] = 5; o[ReconnectionOptions.RetryDelaySeconds] = 1;
            foreach (var v in o) w.WriteInt(v);
        }));
        await s.WriteAsync(srv.Frame(Inbound.RequestKey));
        await WaitFor(() => t.Incoming.Count >= 2);   // both Gvg and RequestKey read before ResponseKey resets ReceivedCount
        await t.SendAsync(new ResponseKey(11, 22, idBytes));
        await ReadFrame(s, srv);   // ResponseKey resets m_sequence = 0 and m_out

        // 3 inbound packets after ResponseKey: Reconnect context + two SyncNumber packets
        await s.WriteAsync(srv.Frame(Inbound.Reconnect, w => { w.WriteByte(ReconnectNotice.KindContext); w.WriteInt(1); w.WriteString("127.0.0.1"); w.WriteInt(port); }));
        await s.WriteAsync(srv.Frame(Inbound.SyncNumber, w => w.WriteByte(1)));
        await s.WriteAsync(srv.Frame(Inbound.SyncNumber, w => w.WriteByte(2)));
        await WaitFor(() => t.Reconnection.Context.Mode == 1 && t.Session.ReceivedCount == 3);

        // 5 outbound packets (must NOT pollute SocketSequence)
        for (byte m = 1; m <= 5; m++) { await t.SendAsync(new RequestQuit(m)); await ReadFrame(s, srv); }

        var srv2 = new FakeServer(5, 6);
        var accept2 = listener.AcceptTcpClientAsync();
        first.Close();
        var s2 = (await accept2).GetStream();
        await s2.WriteAsync(srv2.HelloFrame);
        await ReadFrame(s2, srv2);   // ResponseVersion
        await s2.WriteAsync(srv2.Frame(Inbound.RequestKey));
        var (op, reqBody) = await ReadFrame(s2, srv2);
        Assert.That(op, Is.EqualTo((ushort) Outbound.Reconnect));
        var r = new ByteReader(reqBody);
        Assert.That(r.ReadByte(), Is.EqualTo(1));
        Assert.That(r.ReadLong(), Is.EqualTo(11));
        Assert.That(r.ReadLong(), Is.EqualTo(22));
        Assert.That(r.ReadInt(), Is.EqualTo(1), "first attempt");
        Assert.That(r.ReadInt(), Is.EqualTo(3), "m_sequence had 3 inbound packets after ResponseKey, not 5 outbound");
        Assert.That(r.ReadUnicodeString(), Is.EqualTo("00-11-22-33-44-55-66-77-88-99-AA-BB-CC-DD-EE-FF"));
        listener.Stop();
    }

    /// <summary>
    /// CGameSocket::ProcessPacket (gamesocket.cpp:320-321): op 7 (ReconnectUnk) sets m_sequence = ReadInt32(), and op 8
    /// (FinalizeReconnect) sets m_sequence to the first ReadInt32(), so the next Pong carries the restored sequence + 1.
    /// </summary>
    [Test] public async Task ReconnectUnkAndFinalizeReconnectRestoreReceivedCountForNextPong() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint) listener.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = listener.AcceptTcpClientAsync();
        using var t = new TcpTransport();
        await t.ConnectAsync("127.0.0.1", port);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv);   // ResponseVersion

        await s.WriteAsync(srv.Frame(Inbound.ReconnectUnk, w => w.WriteInt(77)));
        await s.WriteAsync(srv.Frame(Inbound.Ping, w => w.WriteInt(0)));
        var (op1, body1) = await ReadFrame(s, srv);
        Assert.That(op1, Is.EqualTo((ushort) Outbound.Pong));
        Assert.That(Pong.Read(new ByteReader(body1)).ReceivedCount, Is.EqualTo(78));

        await s.WriteAsync(srv.Frame(Inbound.FinalizeReconnect, w => { w.WriteInt(120); w.WriteInt(0); }));
        Assert.That((await ReadFrame(s, srv)).op, Is.EqualTo((ushort) Outbound.ReconnectUnk));
        Assert.That((await ReadFrame(s, srv)).op, Is.EqualTo((ushort) Outbound.FinalizeReconnect));
        await s.WriteAsync(srv.Frame(Inbound.Ping, w => w.WriteInt(0)));
        var (op2, body2) = await ReadFrame(s, srv);
        Assert.That(op2, Is.EqualTo((ushort) Outbound.Pong));
        Assert.That(Pong.Read(new ByteReader(body2)).ReceivedCount, Is.EqualTo(121));
        listener.Stop();
    }

    /// <summary>
    /// CNetworkService::OnGvg (networkservice.cpp:811-832): kind 7 (CContentShutdown::Decode: u16 count + count x u16,
    /// contentshutdown.cpp:26) and kind 42 (DungeonRecordManagerCli::OnRates: two floats, dungeonrecordcli.cpp:466) precede
    /// kind 43 without aborting the walk, and ReadAll decodes all six client kinds with ExpectEnd.
    /// </summary>
    [Test] public void GvgFindsReconnectionOptionsAfterContentShutdownAndDungeonRates() {
        byte[] raw = Body(w => {
            w.WriteInt(6);
            w.WriteInt(1); w.WriteBool(true);
            w.WriteInt(7); w.WriteShort(2); w.WriteShort(5); w.WriteShort(19);
            w.WriteInt(25); w.WriteInt(1); w.WriteInt(2); w.WriteInt(8192); w.WriteInt(8192);
            w.WriteInt(34); w.WriteShort(3); w.WriteInt(4); w.WriteInt(5);
            w.WriteInt(42); w.WriteFloat(1.5f); w.WriteFloat(2.0f);
            w.WriteInt(43);
            for (int i = 0; i < ReconnectionOptions.Count; i++) w.WriteInt(i == ReconnectionOptions.Enabled ? 1 : i == ReconnectionOptions.MaxAttempts ? 6 : 0);
        });
        var ro = GvgOptions.FindReconnection(new ByteReader(raw));
        Assert.That(ro, Is.Not.Null);
        Assert.That(ro[ReconnectionOptions.Enabled], Is.EqualTo(1));
        Assert.That(ro[ReconnectionOptions.MaxAttempts], Is.EqualTo(6));

        var all = GvgOptions.ReadAll(new ByteReader(raw));
        Assert.That(all.Select(e => e.Kind), Is.EqualTo(new[] { 1, 7, 25, 34, 42, 43 }));
        Assert.That(((GvgEntry.ContentShutdown) all[1]).DisabledContents, Is.EqualTo(new ushort[] { 5, 19 }));
        Assert.That(((GvgEntry.DungeonRates) all[4]).RateA, Is.EqualTo(1.5f));
        Assert.Throws<PacketLayoutException>(() => GvgOptions.ReadAll(new ByteReader(raw.Append((byte) 0).ToArray())));
    }

    /// <summary>
    /// windowservice.cpp:558-578 (uMsg == 0x46b): when AttemptReconnect fails to connect, OnReconnectFailed(200) is called;
    /// when the scene left the game before 0x46b runs, OnReconnectFailed(0xcc) is called without connecting.
    /// </summary>
    [Test] public async Task FailedConnectAttemptReportsCode200AndLeavingGameReportsCodeCc() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint) listener.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = listener.AcceptTcpClientAsync();
        bool inGame = true;
        int gaveUpCode = 0;
        using var t = new TcpTransport { InGame = () => inGame };
        t.ReconnectGaveUp += c => gaveUpCode = c;
        await t.ConnectAsync("127.0.0.1", port);
        var first = await accept;
        var s = first.GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.Gvg, w => {
            w.WriteInt(1); w.WriteInt(43);
            var o = new int[ReconnectionOptions.Count];
            o[ReconnectionOptions.Enabled] = 1; o[ReconnectionOptions.GiveUpSeconds] = 30;
            o[ReconnectionOptions.MaxAttempts] = 5; o[ReconnectionOptions.RetryDelaySeconds] = 1;
            foreach (var v in o) w.WriteInt(v);
        }));
        await s.WriteAsync(srv.Frame(Inbound.Reconnect, w => { w.WriteByte(ReconnectNotice.KindContext); w.WriteInt(1); w.WriteString("127.0.0.1"); w.WriteInt(port); }));
        await WaitFor(() => t.Reconnection.Context.Mode == 1);

        listener.Stop();   // next connect fails immediately
        first.Close();
        await WaitFor(() => t.Reconnection.Attempt >= 1 && t.Reconnection.Pending);
        Assert.That(t.Reconnection.LastError, Is.EqualTo(ReconnectionManager.ErrorConnectFailed));

        inGame = false;    // scene left game before the next pending retry
        await WaitFor(() => t.Reconnection.LastError == ReconnectionManager.ErrorNotInGame);
        Assert.That(t.Reconnection.Attempt, Is.EqualTo(1), "not-in-game fails with 0xcc without running TryConnect");

        // fatal / disabled ends the reconnect and raises ReconnectGaveUp with the initial drop code (0x2745)
        t.Reconnection.Disable();
        await WaitFor(() => gaveUpCode == ReconnectionManager.ErrorSocketClosed);
        Assert.That(t.IsClosed && t.ReconnectEnded, "the close is the gave-up's: its 0x465 code went out with ReconnectGaveUp");
    }

    [Test] public void MachineIdTextFormatsSixteenBytesAsUppercaseDashedHex() {
        var bytes = new byte[] { 0x00, 0x0A, 0x1B, 0x2C, 0x3D, 0x4E, 0x5F, 0x60, 0x71, 0x82, 0x93, 0xA4, 0xB5, 0xC6, 0xD7, 0xFF };
        Assert.That(MachineIdText.Format(bytes), Is.EqualTo("00-0A-1B-2C-3D-4E-5F-60-71-82-93-A4-B5-C6-D7-FF"));
        Assert.Throws<ArgumentException>(() => MachineIdText.Format(new byte[15]));
    }
}
