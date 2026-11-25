using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet dispatch tests for RequestKey (KMS 0x04, GMS2 0x04;
/// CNetworkService::OnRequestKey @0x14153b010, networkservice.cpp:859 -> GameClient.EnterEndpointAsync, ClientSession.RunAsync).
/// Every recorded KMS2 and GMS2 0x04 packet carries an empty 0-byte payload.
/// </summary>
public class RequestKeyRecordedTests
{
    sealed class DuplexStream : Stream
    {
        readonly Stream input;
        public readonly MemoryStream Written = new();
        public DuplexStream(Stream input) { this.input = input; }
        public override int Read(byte[] b, int o, int c) => input.Read(b, o, c);
        public override void Write(byte[] b, int o, int c) => Written.Write(b, o, c);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
    }

    sealed class StubReconnectHost : IReconnectHost
    {
        public readonly List<ReconnectRequest> Sent = new();
        public uint Now => 1_000;
        public bool InGame => true;
        public bool HasConnection => true;
        public bool HasNetworkService => true;
        public bool SocketDisconnected => false;
        public bool Closing => false;
        public string SocketHost => "127.0.0.1";
        public int SocketPort => 21001;
        public int SocketSequence => 17;
        public bool IsConnected => true;
        public bool Connect(string host, int port) => true;
        public void Close() { }
        public void SendReconnectRequest(ReconnectRequest request) => Sent.Add(request);
        public void ShowProgress(int attempt, int attempts) { }
        public void HideProgress() { }
        public void Notice(string text) { }
        public void PostAttempt(int code) { }
        public void PostGaveUp(int code) { }
        public void EndSession() { }
    }

    static async Task<(ushort Op, byte[] Body)> ReadFrame(NetworkStream s, FakeServer srv)
    {
        var head = new byte[6];
        await s.ReadExactlyAsync(head);
        var frame = new byte[6 + BitConverter.ToInt32(head, 2)];
        head.CopyTo(frame, 0);
        await s.ReadExactlyAsync(frame.AsMemory(6));
        return srv.Read(frame);
    }

    static async Task VerifyRequestKeyAnswersResponseKeyAndReconnect(bool gms2, long accountId, ulong token)
    {
        var profile = gms2 ? ProtocolProfile.Gms2 : ProtocolProfile.Kms2525;

        // 1. GameClient entry answers empty RequestKey with ResponseKey.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var srv = new FakeServer(0x10203040, 0x50607080, gms2: gms2);
            var accept = listener.AcceptTcpClientAsync();
            var machine = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
            var enter = GameClient.EnterEndpointAsync("127.0.0.1", (ushort)port, token, accountId, machine);

            using var peer = await accept;
            var s = peer.GetStream();
            await s.WriteAsync(srv.HelloFrame);
            await ReadFrame(s, srv); // ResponseVersion
            await s.WriteAsync(srv.Frame(Inbound.RequestKey));

            var (op, body) = await ReadFrame(s, srv);
            Assert.That(op, Is.EqualTo(profile.Number(Outbound.ResponseKey)));
            var key = ResponseKey.Read(new ByteReader(body));
            Assert.That(key.AccountId, Is.EqualTo(accountId));
            Assert.That(key.Token, Is.EqualTo(token));
            Assert.That(key.MachineId, Is.EqualTo(machine));

            await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(0)));
            await s.WriteAsync(srv.Frame(Inbound.RequestFieldEnter, w =>
            {
                w.WriteByte(0); w.WriteInt(2_000_001); w.WriteByte(0); w.WriteByte(0); w.WriteInt(0); w.WriteInt(0);
                w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteInt(0);
            }));
            var (_, game) = await enter.WaitAsync(TimeSpan.FromSeconds(10));
            game.Dispose();
        }
        finally
        {
            listener.Stop();
        }

        // 2. ClientSession while reconnecting routes RequestKey to ReconnectionManager.SendRequest and suppresses PacketReceived.
        var host = new StubReconnectHost();
        var mgr = new ReconnectionManager(host);
        mgr.SetIdentity(accountId, 777L, "test-machine");
        mgr.Begin(ReconnectionManager.ErrorSocketClosed);
        Assert.That(mgr.IsReconnecting, Is.True);

        var rsrv = new FakeServer(0x0A0B0C0D, 0x01020304, gms2: gms2);
        var inbound = new MemoryStream();
        inbound.Write(rsrv.HelloFrame);
        inbound.Write(rsrv.Frame(Inbound.RequestKey));
        inbound.Position = 0;

        var received = new List<RawPacket>();
        var session = new ClientSession(new MapleChannel(new DuplexStream(inbound))) { Manager = mgr };
        session.PacketReceived += p => received.Add(p);
        Assert.ThrowsAsync<EndOfStreamException>(() => session.RunAsync());

        Assert.That(session.OpcodeCounts[0x04], Is.EqualTo(1));
        Assert.That(received, Is.Empty);
        Assert.That(host.Sent, Has.Count.EqualTo(1));
        Assert.That(host.Sent[0].Account, Is.EqualTo(accountId));
        Assert.That(host.Sent[0].Session, Is.EqualTo(777L));
        Assert.That(host.Sent[0].MachineId, Is.EqualTo("test-machine"));
    }

    [Test]
    public Task KmsRequestKeyTriggersResponseKeyAndReconnectRequest()
        => VerifyRequestKeyAnswersResponseKeyAndReconnect(gms2: false, accountId: 1001L, token: 0x0102030405060708UL);

    [Test]
    public Task Gms2RequestKeyTriggersResponseKeyAndReconnectRequest()
        => VerifyRequestKeyAnswersResponseKeyAndReconnect(gms2: true, accountId: 2002L, token: 0x0807060504030201UL);
}
