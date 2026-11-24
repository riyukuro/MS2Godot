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

    // Each server's ServerList writer, byte for byte: the KMS fork's ServerListPacket.Load/Error (type name, count, the rows)
    // and upstream's for GMS2 (command byte, one row with 4 and 100 in the character and traffic places).
    static byte[] Write(ServerList list, ProtocolProfile profile)
    {
        var w = new ByteWriter();
        if (profile.IsGms2)
        {
            if (list.Servers.Count == 0) { w.WriteByte(0); return w.ToArray(); }
            w.WriteByte(1);
        }
        else
        {
            w.WriteString(list.EnvName);
            w.WriteByte((byte)list.Servers.Count);
        }
        foreach (var s in list.Servers)
        {
            w.WriteInt(s.ServerId); w.WriteUnicodeString(s.Name); w.WriteByte(profile.IsGms2 ? (byte)4 : s.Characters);
            w.Write<ushort>((ushort)s.Addresses.Length);
            foreach (var (a, port) in s.Addresses) { w.WriteUnicodeString(a); w.Write<ushort>(port); }
            w.WriteInt(s.TrafficPercent);
            w.Write<ushort>((ushort)s.Channels.Length);
            foreach (var c in s.Channels) w.WriteShort((short)c);
        }
        return w.ToArray();
    }

    static ServerListChannelEntry Row(int id, string name, byte chars, int traffic, ushort[] channels, params (string, ushort)[] addresses) =>
        new(id, name, chars, addresses, traffic, channels);

    // Every list shape the two servers send (no server, the one server row, several addresses and many channels, no channels), and for KMS
    // the client's several rows (ReceiveServerList reads count rows, uiserverlistdialog.cpp:314-339): each read back whole on its profile.
    static IEnumerable<(string name, ServerList list, bool gms2)> Shapes()
    {
        foreach (bool gms2 in new[] { false, true })
        {
            int traffic = gms2 ? 100 : 25;
            byte chars = gms2 ? (byte)0 : (byte)3;   // GMS2's place holds 4, which the reader drops
            string env = gms2 ? "" : "live";
            yield return ("empty", new ServerList(env, new()), gms2);
            yield return ("one row", new ServerList(env, new() { Row(1, "Paperwood", chars, traffic, new ushort[] { 1, 2 }, ("127.0.0.1", 20001)) }), gms2);
            yield return ("addresses and channels", new ServerList(env, new() { Row(1, "\u00c9toile", chars, traffic, Enumerable.Range(1, 40).Select(c => (ushort)c).ToArray(),
                ("10.0.0.5", 20001), ("10.0.0.6", 20002), ("::1", 65535)) }), gms2);
            yield return ("no channels", new ServerList(env, new() { Row(1, "Paperwood", chars, traffic, Array.Empty<ushort>(), ("127.0.0.1", 20001)) }), gms2);
            if (!gms2)
                yield return ("several rows", new ServerList("dev", new() {
                    Row(3, "C", 0, 95, new ushort[] { 5, 1, 65535 }),
                    Row(1, "A", 255, 0, new ushort[] { 1 }, ("127.0.0.1", 1)),
                    Row(2, "B", 7, 50, new ushort[] { 2, 2 }, ("127.0.0.2", 2)) }), gms2);
        }
    }

    [Test]
    public void ServerListReadsEveryShapeOnBothProfiles()
    {
        int n = 0;
        foreach (var (name, list, gms2) in Shapes())
        {
            var profile = gms2 ? ProtocolProfile.Gms2 : ProtocolProfile.Kms2525;
            var read = ServerList.Read(new ByteReader(Write(list, profile)), profile);
            string at = (gms2 ? "GMS2 " : "KMS ") + name;
            Assert.That(read.EnvName, Is.EqualTo(list.EnvName), at);
            Assert.That(read.Usable, Is.EqualTo(list.Servers.Count > 0), at);
            Assert.That(read.Servers.Count, Is.EqualTo(list.Servers.Count), at);
            for (int i = 0; i < list.Servers.Count; i++)
            {
                var (want, got) = (list.Servers[i], read.Servers[i]);
                Assert.That((got.ServerId, got.Name, got.Characters, got.TrafficPercent), Is.EqualTo((want.ServerId, want.Name, want.Characters, want.TrafficPercent)), at);
                Assert.That(got.Addresses, Is.EqualTo(want.Addresses), at);
                Assert.That(got.Channels, Is.EqualTo(want.Channels), at);
            }
            n++;
        }
        Assert.That(n, Is.EqualTo(9));
    }

    // UILoginDialog::SendLoginRequest (uilogindialog.cpp:204-246): type 1 with login type 0 on the first connection, ServerList back and the
    // connection dropped; UIServerListDialog::ConnectToServer (uiserverlistdialog.cpp:502-517) then connects to the listed address, and
    // SendLoginRequest (:577-601) sends type 2 with the channel as the login type.
    [TestCase(false)]
    [TestCase(true)]
    public async Task TheServerListLoginThenTheListedServersCharacterLogin(bool gms2)
    {
        var profile = gms2 ? ProtocolProfile.Gms2 : ProtocolProfile.Kms2525;
        var l1 = new TcpListener(IPAddress.Loopback, 0); l1.Start();
        var l2 = new TcpListener(IPAddress.Loopback, 0); l2.Start();
        try
        {
            ushort listedPort = (ushort)((IPEndPoint)l2.LocalEndpoint).Port;
            var srv1 = new FakeServer(5, 6, gms2);
            var accept1 = l1.AcceptTcpClientAsync();
            using var t1 = new TcpTransport();
            var listTask = new LoginClient(t1).ServerListAsync("127.0.0.1", ((IPEndPoint)l1.LocalEndpoint).Port, "userKR", "pw");
            using (var c1 = await accept1)
            {
                var s1 = c1.GetStream();
                await s1.WriteAsync(srv1.HelloFrame);
                Assert.That((await ReadFrame(s1, srv1)).op, Is.EqualTo((ushort)profile.Number(Outbound.ResponseVersion)));
                await s1.WriteAsync(srv1.Frame(Inbound.RequestLogin));
                var (op, body) = await ReadFrame(s1, srv1);
                Assert.That(op, Is.EqualTo((ushort)profile.Number(Outbound.ResponseLogin)));
                var first = ResponseLogin.Read(new ByteReader(body), profile);
                Assert.That((first.Type, first.LoginType, first.User, first.Password), Is.EqualTo((LoginClient.ServerListType, (short)0, "userKR", "pw")));
                var listed = new ServerList(gms2 ? "" : "live", new() { Row(1, "Paperwood", 0, gms2 ? 100 : 0, new ushort[] { 2, 1 }, ("127.0.0.1", listedPort)) });
                var bytes = Write(listed, profile);
                await s1.WriteAsync(srv1.Frame(Inbound.ServerList, w => w.WriteBytes(bytes)));
            }   // the server drops the connection after the list
            var answer = await listTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(answer.Refusal, Is.Null);
            Assert.That(answer.List.Servers.Single().Addresses.Single(), Is.EqualTo(("127.0.0.1", listedPort)));

            var srv2 = new FakeServer(7, 8, gms2);
            var accept2 = l2.AcceptTcpClientAsync();
            using var t2 = new TcpTransport();
            var (host, port) = answer.List.Servers[0].Addresses[0];
            var loginTask = new LoginClient(t2) { Channel = 1 }.LoginAsync(host, port, "userKR", "pw");
            using var c2 = await accept2;
            var s2 = c2.GetStream();
            await s2.WriteAsync(srv2.HelloFrame);
            await ReadFrame(s2, srv2);   // ResponseVersion
            await s2.WriteAsync(srv2.Frame(Inbound.RequestLogin));
            var (op2, body2) = await ReadFrame(s2, srv2);
            Assert.That(op2, Is.EqualTo((ushort)profile.Number(Outbound.ResponseLogin)));
            var second = ResponseLogin.Read(new ByteReader(body2), profile);
            Assert.That((second.Type, second.LoginType), Is.EqualTo((LoginClient.CharacterListType, (short)1)));
            await s2.WriteAsync(srv2.Frame(Inbound.LoginResult, w => { w.WriteByte(0); w.WriteInt(0); w.WriteUnicodeString(""); w.WriteLong(42); w.WriteLong(0); w.WriteInt(0); w.WriteByte(0); w.WriteByte(0); }));
            await s2.WriteAsync(srv2.Frame(Inbound.CharacterList, w => w.WriteByte((byte)CharacterListCommand.StartList)));
            await s2.WriteAsync(srv2.Frame(Inbound.CharacterList, w => { w.WriteByte((byte)CharacterListCommand.List); w.WriteByte(0); }));
            await s2.WriteAsync(srv2.Frame(Inbound.CharacterList, w => { w.WriteByte((byte)CharacterListCommand.EndList); w.WriteByte(0); }));
            var o = await loginTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(o.Success);
            Assert.That(o.Result.AccountId, Is.EqualTo(42));
        }
        finally { l1.Stop(); l2.Stop(); }
    }

    // a refused type 1 login comes back as the refusal; a connection dropped before any list is an error
    [Test]
    public async Task TheServerListLoginRefusedOrDropped()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        try
        {
            var srv = new FakeServer(9, 10);
            var accept = l.AcceptTcpClientAsync();
            using var t = new TcpTransport();
            var task = new LoginClient(t).ServerListAsync("127.0.0.1", ((IPEndPoint)l.LocalEndpoint).Port, "userKR", "bad");
            using (var c = await accept)
            {
                var s = c.GetStream();
                await s.WriteAsync(srv.HelloFrame);
                await ReadFrame(s, srv);
                await s.WriteAsync(srv.Frame(Inbound.RequestLogin));
                await ReadFrame(s, srv);
                await s.WriteAsync(srv.Frame(Inbound.LoginResult, w => { w.WriteByte(1); w.WriteInt(0); w.WriteUnicodeString("bad"); w.WriteLong(42); w.WriteLong(0); w.WriteInt(0); w.WriteByte(0); w.WriteByte(0); }));
                var a = await task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.That(a.List, Is.Null);
                Assert.That(a.Refusal.Success, Is.False);
            }

            var srv2 = new FakeServer(11, 12);
            var accept2 = l.AcceptTcpClientAsync();
            using var t2 = new TcpTransport();
            var dropped = new LoginClient(t2).ServerListAsync("127.0.0.1", ((IPEndPoint)l.LocalEndpoint).Port, "userKR", "pw");
            using (var c2 = await accept2)
            {
                var s2 = c2.GetStream();
                await s2.WriteAsync(srv2.HelloFrame);
                await ReadFrame(s2, srv2);
            }
            Assert.ThrowsAsync<IOException>(async () => await dropped.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { l.Stop(); }
    }
}
