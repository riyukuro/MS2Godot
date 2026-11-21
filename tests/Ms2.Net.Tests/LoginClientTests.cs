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

    [Test] public async Task AnEmptyIdIsSentAsTyped() {
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

    [Test] public async Task ClosedBeforeListThrows() {
        var (s, l, srv, t, login) = await Start();
        using (t) {
            await ReadFrame(s, srv);
            s.Socket.Close();
            Assert.ThrowsAsync<IOException>(async () => await login.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        l.Stop();
    }

    /// <summary>Opt-in: MS2_LIVE_LOGIN=host:port and MS2_LIVE_ACCOUNT=user:password (user without the region code; "KR" is appended like the client does).
    /// Optional MS2_LIVE_EXPECT=name1,name2 lists the character names the server's game db holds for the account.</summary>
    [Test] public async Task LiveServerCharacterList() {
        var addr = Environment.GetEnvironmentVariable("MS2_LIVE_LOGIN"); var acct = Environment.GetEnvironmentVariable("MS2_LIVE_ACCOUNT");
        if (string.IsNullOrEmpty(addr) || string.IsNullOrEmpty(acct)) Assert.Ignore("MS2_LIVE_LOGIN / MS2_LIVE_ACCOUNT not set");
        var hp = addr.Split(':'); var up = acct.Split(':', 2);
        Assert.That(int.Parse(hp[1]), Is.Not.EqualTo(20001), "never the shared server");
        using var t = new TcpTransport();
        var o = await new LoginClient(t).LoginAsync(hp[0], int.Parse(hp[1]), up[0] + "KR", up[1]);
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
