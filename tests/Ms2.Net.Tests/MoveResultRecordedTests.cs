using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and GameClient handler tests for MoveResult (KMS 0x0D, GMS2 0x0D;
/// CNetworkService::OnMoveResult @0x14153b410, networkservice.cpp:899 -> MoveResult.Read, GameClient.EnterEndpointAsync).
/// </summary>
public class MoveResultRecordedTests
{
    static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(10);

    // Both KMS2 0x0D and GMS2 0x0D captures carry this single 0x00 byte (MoveResult error 0 = ok).
    static readonly byte[] RecordedMoveOk = Convert.FromHexString("00");

    // Hand-built 1-byte non-zero error body to verify the rejection path (no failed MoveResult capture exists).
    static readonly byte[] SyntheticMoveRefused = Convert.FromHexString("01");

    static async Task<(ushort Op, byte[] Body)> ReadFrame(NetworkStream s, FakeServer srv, CancellationToken ct)
    {
        var head = new byte[6];
        await s.ReadExactlyAsync(head, ct);
        var frame = new byte[6 + BitConverter.ToInt32(head, 2)];
        head.CopyTo(frame, 0);
        await s.ReadExactlyAsync(frame.AsMemory(6), ct);
        return srv.Read(frame);
    }

    static async Task Observe(Task<(GameEntry entry, TcpTransport game)> enter)
    {
        if (enter == null) return;
        try
        {
            var (_, game) = await enter.WaitAsync(TimeSpan.FromSeconds(2));
            game.Dispose();
        }
        catch
        {
            // Faulted or cancelled enter task observed on cleanup so it never escapes as unobserved.
        }
    }

    static async Task VerifyMoveResultAcceptsEntry(bool gms2, int mapId)
    {
        var r = new ByteReader(RecordedMoveOk);
        var ok = MoveResult.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(ok.Error, Is.EqualTo((byte)0));

        using var cts = new CancellationTokenSource(IoTimeout);
        var ct = cts.Token;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Task<(GameEntry entry, TcpTransport game)> enter = null;
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var srv = new FakeServer(0x11223344, 0x55667788, gms2: gms2);
            var accept = listener.AcceptTcpClientAsync(ct).AsTask();
            var machine = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
            enter = GameClient.EnterEndpointAsync("127.0.0.1", (ushort)port, 0x0102030405060708UL, 99, machine, ct: ct);

            using var peer = await accept.WaitAsync(IoTimeout);
            var s = peer.GetStream();
            await s.WriteAsync(srv.HelloFrame, ct);
            await ReadFrame(s, srv, ct); // ResponseVersion
            await s.WriteAsync(srv.Frame(Inbound.RequestKey), ct);
            await ReadFrame(s, srv, ct); // ResponseKey
            await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteBytes(RecordedMoveOk)), ct);
            await s.WriteAsync(srv.Frame(Inbound.RequestFieldEnter, w =>
            {
                w.WriteByte(0); w.WriteInt(mapId); w.WriteByte(0); w.WriteByte(0); w.WriteInt(0); w.WriteInt(0);
                w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteInt(0);
            }), ct);

            var (entry, game) = await enter.WaitAsync(IoTimeout);
            enter = null;
            using (game) Assert.That(entry.Field.MapId, Is.EqualTo(mapId));
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            await Observe(enter);
        }
    }

    static async Task VerifyMoveResultErrorThrows(bool gms2)
    {
        using var cts = new CancellationTokenSource(IoTimeout);
        var ct = cts.Token;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Task<(GameEntry entry, TcpTransport game)> enter = null;
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var srv = new FakeServer(0x22334455, 0x66778899, gms2: gms2);
            var accept = listener.AcceptTcpClientAsync(ct).AsTask();
            var machine = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
            enter = GameClient.EnterEndpointAsync("127.0.0.1", (ushort)port, 0x0102030405060708UL, 99, machine, ct: ct);

            using var peer = await accept.WaitAsync(IoTimeout);
            var s = peer.GetStream();
            await s.WriteAsync(srv.HelloFrame, ct);
            await ReadFrame(s, srv, ct); // ResponseVersion
            await s.WriteAsync(srv.Frame(Inbound.RequestKey), ct);
            await ReadFrame(s, srv, ct); // ResponseKey
            await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteBytes(SyntheticMoveRefused)), ct);

            var ex = Assert.ThrowsAsync<MoveRefusedException>(async () => await enter.WaitAsync(IoTimeout));
            enter = null;
            Assert.That(ex.Error, Is.EqualTo((byte)1));
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            await Observe(enter);
        }
    }

    [Test]
    public async Task KmsMoveResultAcceptsOkAndRefusesNonZeroError()
    {
        await VerifyMoveResultAcceptsEntry(gms2: false, mapId: 2_000_001);
        await VerifyMoveResultErrorThrows(gms2: false);
    }

    [Test]
    public async Task Gms2MoveResultAcceptsOkAndRefusesNonZeroError()
    {
        await VerifyMoveResultAcceptsEntry(gms2: true, mapId: 2_000_062);
        await VerifyMoveResultErrorThrows(gms2: true);
    }
}
