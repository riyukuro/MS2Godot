using System.Net;
using System.Net.Sockets;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>LoginClient against an in-process fake login server on loopback, plus an opt-in run against a local test login server.</summary>
public class LoginClientTests {
    static async Task<(NetworkStream s, TcpListener l, FakeServer srv, TcpTransport t, Task<LoginOutcome> login)> Start(string user = "userKR") {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var srv = new FakeServer(0x01020304, 0x0A0B0C0D);
        var accept = l.AcceptTcpClientAsync();
        var t = new TcpTransport();
        var login = new LoginClient(t).LoginAsync("127.0.0.1", ((IPEndPoint) l.LocalEndpoint).Port, user, "pw");
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        return (s, l, srv, t, login);
    }

    static async Task<(ushort op, byte[] body)> ReadFrame(NetworkStream s, FakeServer srv) {
        var head = new byte[6]; await s.ReadExactlyAsync(head);
        var frame = new byte[6 + BitConverter.ToInt32(head, 2)]; head.CopyTo(frame, 0);
        await s.ReadExactlyAsync(frame.AsMemory(6));
        return srv.Read(frame);
    }

    static byte[] LoginResultFrame(FakeServer srv, byte state) => srv.Frame(Inbound.LoginResult, w => {
        w.WriteByte(state); w.WriteInt(0); w.WriteUnicodeString(state == 0 ? "" : "bad"); w.WriteLong(42); w.WriteLong(0); w.WriteInt(0); w.WriteByte(0); w.WriteByte(0);
    });

    // GMS2 hello (version 12): the ticket form carries "EN", the login type that goes with command 2 (1), the socket's local address and a non-zero nonce
    [Test] public async Task LoginClientSendsTheRecordedTicketLoginOnGms2() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var srv = new FakeServer(0x01020304, 0x0A0B0C0D, gms2: true);
        var accept = l.AcceptTcpClientAsync();
        var t = new TcpTransport();
        var id = Enumerable.Range(1, 16).Select(i => (byte) i).ToArray();
        var login = new LoginClient(t) { Ticket = "MST_0123456789ab" }.LoginAsync("127.0.0.1", ((IPEndPoint) l.LocalEndpoint).Port, "userKR", "pw", id);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        using (t) {
            await ReadFrame(s, srv);   // ResponseVersion
            await s.WriteAsync(srv.Frame(Inbound.RequestLogin));
            var (op, body) = await ReadFrame(s, srv);
            Assert.That(op, Is.EqualTo((ushort) ProtocolProfile.Gms2.Number(Outbound.ResponseLogin)));
            Assert.That(body.Length, Is.EqualTo(62));
            var r = ResponseLogin.ReadRecorded(new ByteReader(body));
            Assert.That(r.Language, Is.EqualTo("EN"));
            Assert.That(r.Type, Is.EqualTo(2));
            Assert.That(r.LoginType, Is.EqualTo(1));
            Assert.That(r.Ticket, Is.EqualTo("MST_0123456789ab"));
            Assert.That(r.LocalIp, Is.EqualTo(new byte[] { 127, 0, 0, 1 }));
            Assert.That(r.MachineId, Is.EqualTo(id));
            Assert.That(r.Nonce, Is.Not.EqualTo(0));
            await s.WriteAsync(LoginResultFrame(srv, 1));
            Assert.That((await login).Success, Is.False);
            l.Stop();
        }
    }

    // a ticket on a KMS hello leaves the KMS login as it was: unicode id and password, language "en"
    [Test] public async Task LoginClientIgnoresTheTicketOnKms() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var srv = new FakeServer(0x01020304, 0x0A0B0C0D);
        var accept = l.AcceptTcpClientAsync();
        var t = new TcpTransport();
        var login = new LoginClient(t) { Ticket = "MST_0123456789ab" }.LoginAsync("127.0.0.1", ((IPEndPoint) l.LocalEndpoint).Port, "userKR", "pw");
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        using (t) {
            await ReadFrame(s, srv);
            await s.WriteAsync(srv.Frame(Inbound.RequestLogin));
            var (_, body) = await ReadFrame(s, srv);
            var r = ResponseLogin.Read(new ByteReader(body));
            Assert.That((r.User, r.Password, r.LoginType, r.Language), Is.EqualTo(("userKR", "pw", (short) 0, "en")));
            await s.WriteAsync(LoginResultFrame(srv, 1));
            Assert.That((await login).Success, Is.False);
            l.Stop();
        }
    }

    [Test] public async Task LoginClientSendsAnEmptyIdAsTyped() {
        var (s, l, srv, t, login) = await Start("");
        using (t) {
            Assert.That((await ReadFrame(s, srv)).op, Is.EqualTo((ushort) Outbound.ResponseVersion));
            await s.WriteAsync(srv.Frame(Inbound.RequestLogin));
            var (op, body) = await ReadFrame(s, srv);
            Assert.That(op, Is.EqualTo((ushort) Outbound.ResponseLogin));
            Assert.That(ResponseLogin.Read(new ByteReader(body)).User, Is.EqualTo(""));
            await s.WriteAsync(LoginResultFrame(srv, 1));
            Assert.That((await login).Success, Is.False);
            l.Stop();
        }
    }

    [Test] public async Task LoginToEmptyCharacterList() {
        var (s, l, srv, t, login) = await Start();
        using (t) {
            Assert.That((await ReadFrame(s, srv)).op, Is.EqualTo((ushort) Outbound.ResponseVersion));
            await s.WriteAsync(srv.Frame(Inbound.RequestLogin));
            var (op, body) = await ReadFrame(s, srv);
            Assert.That(op, Is.EqualTo((ushort) Outbound.ResponseLogin));
            var rl = ResponseLogin.Read(new ByteReader(body));
            Assert.That(rl.Type, Is.EqualTo(LoginClient.CharacterListType));
            Assert.That(rl.User, Is.EqualTo("userKR")); Assert.That(rl.Password, Is.EqualTo("pw"));
            await s.WriteAsync(LoginResultFrame(srv, 0));
            await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => w.WriteByte((byte) CharacterListCommand.StartList)));
            await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => { w.WriteByte((byte) CharacterListCommand.List); w.WriteByte(0); }));
            await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => { w.WriteByte((byte) CharacterListCommand.EndList); w.WriteByte(0); }));
            var o = await login.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(o.Success); Assert.That(o.Result.AccountId, Is.EqualTo(42)); Assert.That(o.Characters, Is.Empty);
        }
        l.Stop();
    }

    // the EndList flag (OnCharacterListPacket mode 4, m_1e8) comes back with the list; the forced rename (CharacterManagement 5)
    // is answered with CharacterList 7 and its code, or with the list again
    [Test] public async Task TheListsEndFlagAndTheRenameAnswers() {
        var (s, l, srv, t, login) = await Start();
        using (t) {
            await ReadFrame(s, srv);
            await s.WriteAsync(srv.Frame(Inbound.RequestLogin));
            await ReadFrame(s, srv);
            await s.WriteAsync(LoginResultFrame(srv, 0));
            await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => w.WriteByte((byte) CharacterListCommand.StartList)));
            await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => { w.WriteByte((byte) CharacterListCommand.List); w.WriteByte(0); }));
            await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => { w.WriteByte((byte) CharacterListCommand.EndList); w.WriteByte(1); }));
            var o = await login.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(o.CheckNames, Is.True, "EndList 1: the start checks the names");

            var client = new LoginClient(t);
            var refused = client.RenameAsync(20000000042, "Taken");
            var (op, body) = await ReadFrame(s, srv);
            Assert.That(op, Is.EqualTo((ushort) Outbound.CharacterManagement));
            Assert.That(RenameCharacter.Read(new ByteReader(body)), Is.EqualTo(new RenameCharacter(20000000042, "Taken")));
            await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => { w.WriteByte((byte) CharacterListCommand.NameChanged); w.WriteInt(2); w.WriteLong(20000000042); w.WriteUnicodeString("Taken"); }));
            var a = await refused.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(a.List, Is.Null);
            Assert.That((a.NameChanged.Error, a.NameChanged.CharacterId, a.NameChanged.Name), Is.EqualTo((2, 20000000042L, "Taken")));

            var taken = client.RenameAsync(20000000042, "Fine");
            await ReadFrame(s, srv);
            await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => w.WriteByte((byte) CharacterListCommand.StartList)));
            await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => { w.WriteByte((byte) CharacterListCommand.List); w.WriteByte(0); }));
            await s.WriteAsync(srv.Frame((Inbound) 0x0C, w => { w.WriteByte((byte) CharacterListCommand.EndList); w.WriteByte(0); }));
            a = await taken.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(a.NameChanged, Is.Null);
            Assert.That(a.List.CheckNames, Is.False, "the list again, its flag off once no name is due");
        }
        l.Stop();
    }

    [Test] public async Task RejectedLoginReturnsTheError() {
        var (s, l, srv, t, login) = await Start();
        using (t) {
            await ReadFrame(s, srv);
            await s.WriteAsync(srv.Frame(Inbound.RequestLogin));
            await ReadFrame(s, srv);
            await s.WriteAsync(LoginResultFrame(srv, 3));
            var o = await login.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(o.Success, Is.False); Assert.That(o.Result.State, Is.EqualTo(3)); Assert.That(o.Result.Message, Is.EqualTo("bad"));
        }
        l.Stop();
    }

    /// <summary>
    /// A refusal (state 4, the account still logged in), then the next try: the client connects again for it (the login
    /// dialog's OnEndPopdown -> ConnectTo, uilogindialog.cpp:190-201; LiveSession.LoginAsync makes a new transport), and the
    /// new connection sends a second ResponseLogin, which the server accepts.
    /// </summary>
    [Test] public async Task ARefusedLoginIsTriedAgainOnANewConnection() {
        var (s, l, srv, t, login) = await Start();
        using (t) {
            await ReadFrame(s, srv);
            await s.WriteAsync(srv.Frame(Inbound.RequestLogin));
            Assert.That((await ReadFrame(s, srv)).op, Is.EqualTo((ushort) Outbound.ResponseLogin));
            await s.WriteAsync(LoginResultFrame(srv, 4));
            var o = await login.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(o.Success, Is.False); Assert.That(o.Result.State, Is.EqualTo(4));
            s.Socket.Close();   // the server drops the refused connection
        }
        l.Stop();
        var (s2, l2, srv2, t2, login2) = await Start();
        using (t2) {
            await ReadFrame(s2, srv2);
            await s2.WriteAsync(srv2.Frame(Inbound.RequestLogin));
            var (op, body) = await ReadFrame(s2, srv2);
            Assert.That(op, Is.EqualTo((ushort) Outbound.ResponseLogin), "the second try sends a second login");
            Assert.That(ResponseLogin.Read(new ByteReader(body)).User, Is.EqualTo("userKR"));
            await s2.WriteAsync(LoginResultFrame(srv2, 0));
            await s2.WriteAsync(srv2.Frame((Inbound) 0x0C, w => w.WriteByte((byte) CharacterListCommand.StartList)));
            await s2.WriteAsync(srv2.Frame((Inbound) 0x0C, w => { w.WriteByte((byte) CharacterListCommand.EndList); w.WriteByte(0); }));
            var o2 = await login2.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(o2.Success, "the second try logs in");
        }
        l2.Stop();
    }

    [Test] public async Task ClosedBeforeListThrows() {
        var (s, l, srv, t, login) = await Start();
        using (t) {
            await ReadFrame(s, srv);
            s.Socket.Close();
            Assert.ThrowsAsync<IOException>(async () => await login.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        l.Stop();
    }

    /// <summary>Opt-in: MS2_LIVE_ARGS="--ip=host --port=n" (the launcher's arguments; a test run has no command line of its own) and MS2_LIVE_ACCOUNT=user:password (user without the region code; "KR" is appended like the client does).
    /// Optional MS2_LIVE_PORT=n (the test server's port; the test fails when --port differs). Optional MS2_LIVE_EXPECT=name1,name2 lists the character names the server's game db holds for the account.</summary>
    [Test] public async Task LiveServerCharacterList() {
        var addr = LauncherArgs.Parse((Environment.GetEnvironmentVariable("MS2_LIVE_ARGS") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)); var acct = Environment.GetEnvironmentVariable("MS2_LIVE_ACCOUNT");
        if (!addr.HasAddress || string.IsNullOrEmpty(acct)) Assert.Ignore("MS2_LIVE_ARGS / MS2_LIVE_ACCOUNT not set");
        var up = acct.Split(':', 2);
        if (int.TryParse(Environment.GetEnvironmentVariable("MS2_LIVE_PORT"), out int expected)) Assert.That(addr.Port, Is.EqualTo(expected), "the port MS2_LIVE_PORT names, the test server's");   // set it so a stray --port never reaches another server
        using var t = new TcpTransport();
        var o = await new LoginClient(t).LoginAsync(addr.Host, addr.Port, up[0] + "KR", up[1]);
        Assert.That(o.Success, Is.True, o.Result.Message);
        TestContext.Out.WriteLine($"account {o.Result.AccountId}: " + string.Join(", ", o.Characters.Select(c => $"{c.User.CharacterId}:{c.User.Name}")));
        var expect = Environment.GetEnvironmentVariable("MS2_LIVE_EXPECT");
        if (expect != null) {
            var names = expect.Split(',', StringSplitOptions.RemoveEmptyEntries);
            Assert.That(o.Characters.Count, Is.EqualTo(names.Length));
            Assert.That(o.Characters.Select(c => c.User.Name).OrderBy(x => x), Is.EqualTo(names.OrderBy(x => x)));
        }
    }
}
