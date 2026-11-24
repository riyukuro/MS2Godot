using System.Net;
using System.Net.Sockets;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// ServerList (0x0B) and RequestServerList (0x14) session tests:
/// UICharacterListDialog::vfunc_79 @0x1418ea860 (uicharacterlistdialog.cpp:592-603) and
/// UIServerListDialog::ReceiveServerList @0x141ad1150 (uiserverlistdialog.cpp:310-343).
/// </summary>
public class ServerListSessionTests
{
    static async Task<(ushort op, byte[] body)> ReadFrame(NetworkStream s, FakeServer srv)
    {
        var head = new byte[6];
        await s.ReadExactlyAsync(head);
        var frame = new byte[6 + BitConverter.ToInt32(head, 2)];
        head.CopyTo(frame, 0);
        await s.ReadExactlyAsync(frame.AsMemory(6));
        return srv.Read(frame);
    }

    [Test]
    public async Task SelectAsyncSendsRequestServerListOnSuccessAndSkipsItOnRefusal()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try
        {
            var srv = new FakeServer(1, 2);
            var accept = l.AcceptTcpClientAsync();
            using var t = new TcpTransport();
            await t.ConnectAsync("127.0.0.1", ((IPEndPoint)l.LocalEndpoint).Port);
            using var client = await accept;
            var s = client.GetStream();
            await s.WriteAsync(srv.HelloFrame);
            await ReadFrame(s, srv);   // ResponseVersion

            var selectTask = GameClient.SelectAsync(t, 20000000001L, 3);
            var (op1, _) = await ReadFrame(s, srv);
            Assert.That(op1, Is.EqualTo((ushort)Outbound.CharacterManagement));

            await s.WriteAsync(srv.Frame(Inbound.LoginToGame, w =>
            {
                w.WriteByte(0);
                w.WriteBytes(new byte[] { 127, 0, 0, 1 });
                w.WriteShort(21001);
                w.WriteLong(0x0102030405060708L);
                w.WriteInt(2000001);
            }));

            var go = await selectTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(go.Ok, Is.True);

            var (op2, body2) = await ReadFrame(s, srv).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(op2, Is.EqualTo((ushort)Outbound.RequestServerList));
            Assert.That(body2, Is.Empty);
        }
        finally
        {
            l.Stop();
        }

        var l2 = new TcpListener(IPAddress.Loopback, 0);
        l2.Start();
        try
        {
            var srv = new FakeServer(3, 4);
            var accept = l2.AcceptTcpClientAsync();
            using var t = new TcpTransport();
            await t.ConnectAsync("127.0.0.1", ((IPEndPoint)l2.LocalEndpoint).Port);
            using var client = await accept;
            var s = client.GetStream();
            await s.WriteAsync(srv.HelloFrame);
            await ReadFrame(s, srv);   // ResponseVersion

            var selectTask = GameClient.SelectAsync(t, 20000000001L, 1);
            await ReadFrame(s, srv);   // CharacterManagement
            await s.WriteAsync(srv.Frame(Inbound.LoginToGame, w =>
            {
                w.WriteByte(0x19);
                w.WriteUnicodeString("Maintenance");
            }));

            var refused = await selectTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(refused.Ok, Is.False);
            Assert.That(s.DataAvailable, Is.False);
        }
        finally
        {
            l2.Stop();
        }
    }

    [Test]
    public void ServerListModesHandleEmptyRefusalAndRejectTrailingBytes()
    {
        // KMS empty list (count == 0 -> CSceneStateLogin::OnPacket shows 0x20e)
        var wKmsEmpty = new ByteWriter();
        wKmsEmpty.WriteString("live");
        wKmsEmpty.WriteByte(0);
        var kmsEmpty = ServerList.Read(new ByteReader(wKmsEmpty.ToArray()), ProtocolProfile.Kms2525);
        Assert.That(kmsEmpty.Usable, Is.False);
        Assert.That(kmsEmpty.EnvName, Is.EqualTo("live"));

        wKmsEmpty.WriteByte(0xFF);
        Assert.Throws<PacketLayoutException>(() =>
            ServerList.Read(new ByteReader(wKmsEmpty.ToArray()), ProtocolProfile.Kms2525));

        // GMS2 command 0 (error -> empty list) and unknown command 2
        var wGmsErr = new ByteWriter();
        wGmsErr.WriteByte(0);
        var gmsErr = ServerList.Read(new ByteReader(wGmsErr.ToArray()), ProtocolProfile.Gms2);
        Assert.That(gmsErr.Usable, Is.False);

        wGmsErr.WriteByte(0xFF);
        Assert.Throws<PacketLayoutException>(() =>
            ServerList.Read(new ByteReader(wGmsErr.ToArray()), ProtocolProfile.Gms2));

        var wGmsBad = new ByteWriter();
        wGmsBad.WriteByte(2);
        Assert.Throws<InvalidDataException>(() =>
            ServerList.Read(new ByteReader(wGmsBad.ToArray()), ProtocolProfile.Gms2));
    }
}
