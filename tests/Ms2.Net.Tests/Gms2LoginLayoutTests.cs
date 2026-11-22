using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The login-path packets of the GMS2 profile, built field by field from the Maple2 upstream server's layouts
/// (LoginHandler.cs, LoginResultPacket.cs, ServerListPacket.cs, ResponseVersionHandler.cs) and read or written through the profile.
/// The KMS2 layouts stay in the other packet tests.
/// </summary>
public class Gms2LoginLayoutTests {
    static readonly ProtocolProfile Gms = ProtocolProfile.Gms2;

    static byte[] Bytes(ByteWriter w) => w.ToArray();

    [Test]
    public void ResponseVersionIsU32U16AndOneLocaleByte() {
        var w = new ByteWriter();
        ResponseVersion.For(Gms).Write(w, Gms);
        var expect = new ByteWriter();
        expect.WriteInt(12); expect.WriteShort(47); expect.WriteByte(2);   // upstream ResponseVersionHandler: version, 47, Locale.NA
        Assert.That(Bytes(w), Is.EqualTo(Bytes(expect)));
        Assert.That(ResponseVersion.Read(new ByteReader(Bytes(w)), Gms), Is.EqualTo(new ResponseVersion(12, 47, 2)));
    }

    [Test]
    public void ResponseVersionForKmsIsTheEightByteForm() {
        var w = new ByteWriter();
        ResponseVersion.For(ProtocolProfile.Kms2525).Write(w, ProtocolProfile.Kms2525);
        var old = new ByteWriter();
        new ResponseVersion(2525).Write(old);
        Assert.That(Bytes(w), Is.EqualTo(Bytes(old)));
        Assert.That(Bytes(w).Length, Is.EqualTo(8));
    }

    [Test]
    public void ResponseLoginEndsAtTheMachineId() {
        var id = Enumerable.Range(1, 16).Select(i => (byte) i).ToArray();
        var w = new ByteWriter();
        new ResponseLogin(2, "user", "pass", 1, id, 7, 9, "en").Write(w, Gms);
        var expect = new ByteWriter();   // upstream LoginHandler.Handle: command, user, pass, short, Guid
        expect.WriteByte(2); expect.WriteUnicodeString("user"); expect.WriteUnicodeString("pass"); expect.WriteShort(1); expect.WriteBytes(id);
        Assert.That(Bytes(w), Is.EqualTo(Bytes(expect)));
        var back = ResponseLogin.Read(new ByteReader(Bytes(w)), Gms);
        Assert.That(back.User, Is.EqualTo("user"));
        Assert.That(back.Password, Is.EqualTo("pass"));
        Assert.That(back.MachineId, Is.EqualTo(id));
    }

    [Test]
    public void ResponseLoginWithTicketIsTheRecordedForm() {
        var id = Enumerable.Range(1, 16).Select(i => (byte) i).ToArray();
        var pk = new ResponseLogin(2, "user", "pass", 1, id, 7, 9, "EN") { Ticket = "MST_0123456789ab", AccountId = 5, LocalIp = new byte[] { 192, 168, 1, 7 }, Nonce = 0x11223344, Trailer = 2 };
        var w = new ByteWriter(); pk.Write(w, Gms);
        var e = new ByteWriter();
        e.WriteByte(2); e.WriteLong(5); e.WriteShort(16); e.WriteBytes(System.Text.Encoding.ASCII.GetBytes("MST_0123456789ab")); e.WriteShort(1); e.WriteBytes(new byte[] { 192, 168, 1, 7 });
        e.WriteBytes(id); e.WriteInt(0); e.WriteInt(0x11223344); e.WriteShort(2); e.WriteBytes(System.Text.Encoding.ASCII.GetBytes("EN")); e.WriteByte(2);
        Assert.That(Bytes(w), Is.EqualTo(Bytes(e)));
        Assert.That(Bytes(w).Length, Is.EqualTo(62));
        var back = ResponseLogin.ReadRecorded(new ByteReader(Bytes(w)));
        Assert.That(back.Ticket, Is.EqualTo("MST_0123456789ab"));
        Assert.That(back.Nonce, Is.EqualTo(0x11223344));
        Assert.That(back.MachineId, Is.EqualTo(id));
        // a ticket on a KMS profile leaves the KMS login as it was: fixed bytes, written by hand
        var k = new ByteWriter(); pk.Write(k, ProtocolProfile.Kms2525);
        var ke = new ByteWriter();
        ke.WriteByte(2); ke.WriteUnicodeString("user"); ke.WriteUnicodeString("pass"); ke.WriteShort(1); ke.WriteBytes(id); ke.WriteInt(7); ke.WriteInt(9); ke.WriteString("EN");
        Assert.That(Bytes(k), Is.EqualTo(Bytes(ke)));
    }

    [Test]
    public void TicketAsciiMustBeAscii() {
        var id = new byte[16];
        var w = new ByteWriter();
        Assert.Throws<ArgumentException>(() => new ResponseLogin(2, null, null, 1, id, 0, 0, "EN") { Ticket = "MST_\u00e9" }.Write(w, Gms));
        Assert.Throws<ArgumentException>(() => new ResponseLogin(2, null, null, 1, id, 0, 0, "EN") { Ticket = "MST_x", LocalIp = new byte[3] }.Write(w, Gms));
    }

    [Test]
    public void MachineIdFileKeepsOneNonZeroIdPerInstall() {
        var path = Path.Combine(Path.GetTempPath(), "ms2-mid-" + Guid.NewGuid().ToString("N"), "machine-id");
        try {
            var a = MachineIdFile.LoadOrCreate(path, new byte[16]);
            Assert.That(a.Any(x => x != 0));
            Assert.That(MachineIdFile.LoadOrCreate(path, new byte[16]), Is.EqualTo(a));
        } finally { try { Directory.Delete(Path.GetDirectoryName(path), true); } catch { } }
    }

    [Test]
    public void ResponseLoginTicketFormMatchesRecordedLogins() {
        string dir = Environment.GetEnvironmentVariable("MS2_SNIFFS");
        if (string.IsNullOrEmpty(dir)) Assert.Ignore("MS2_SNIFFS needed");
        int n = 0;
        foreach (var path in Directory.GetFiles(Path.Combine(dir, "gms2-sniffs"), "*.msb", SearchOption.AllDirectories)) {
            var f = MsbFile.Read(path);
            foreach (var p in f.Packets) {
                if (p.ServerToClient || p.Opcode != (ushort) Outbound.ResponseLogin) continue;
                var r = ResponseLogin.ReadRecorded(new ByteReader(p.Data));   // throws unless the layout ends exactly
                var w = new ByteWriter(); r.Write(w, Gms);
                Assert.That(Bytes(w), Is.EqualTo(p.Data), Path.GetFileName(path));
                Assert.That(r.Ticket, Does.StartWith("MST_"));
                n++;
            }
        }
        Assert.That(n, Is.GreaterThan(0));
    }

    [Test]
    public void ResponseLoginForKmsKeepsTheTrailingFields() {
        var id = new byte[16];
        var rec = new ResponseLogin(2, "u", "p", 0, id, 1, 2, "en");
        var a = new ByteWriter(); rec.Write(a, ProtocolProfile.Kms2525);
        var b = new ByteWriter(); rec.Write(b);
        Assert.That(Bytes(a), Is.EqualTo(Bytes(b)));
    }

    [Test]
    public void LoginResultSuccessFromUpstreamReadsWithItsTail() {
        var w = new ByteWriter();   // LoginResultPacket.Success
        w.WriteByte(0); w.WriteInt(0); w.WriteUnicodeString(""); w.WriteLong(123456789L); w.WriteLong(1700000000L); w.WriteInt(0);
        w.WriteByte(0); w.WriteByte(0); w.WriteInt(0); w.WriteLong(0); w.WriteInt(2);
        var p = LoginResult.Read(new ByteReader(Bytes(w)));
        Assert.That(p.Success, Is.True);
        Assert.That(p.AccountId, Is.EqualTo(123456789L));
        Assert.That(p.SyncTime, Is.EqualTo(1700000000L));
        Assert.That(p.Tail.Length, Is.EqualTo(4 + 8 + 4));
    }

    [Test]
    public void LoginResultRestrictedFromUpstreamKeepsTheBanTimes() {
        var w = new ByteWriter();   // LoginResultPacket.Restricted
        w.WriteByte(1); w.WriteInt(0); w.WriteUnicodeString("reason"); w.WriteLong(5); w.WriteLong(10); w.WriteInt(0);
        w.WriteByte(0); w.WriteByte(2); w.WriteInt(0); w.WriteLong(0); w.WriteLong(111); w.WriteLong(222); w.WriteInt(0);
        var p = LoginResult.Read(new ByteReader(Bytes(w)));
        Assert.That(p.Success, Is.False);
        Assert.That(p.Message, Is.EqualTo("reason"));
        Assert.That(p.BlockType, Is.EqualTo(2));
    }

    static ByteWriter UpstreamServerList(string name, (string Ip, ushort Port)[] ips, short[] channels) {
        var w = new ByteWriter();   // ServerListPacket.Load
        w.WriteByte(1); w.WriteInt(1); w.WriteUnicodeString(name); w.WriteByte(4);
        w.WriteShort((short) ips.Length);
        foreach (var (ip, port) in ips) { w.WriteUnicodeString(ip); w.WriteShort((short) port); }
        w.WriteInt(100);
        w.WriteShort((short) channels.Length);
        foreach (var c in channels) w.WriteShort(c);
        return w;
    }

    [Test]
    public void ServerListLoadIsOneServerWithItsChannels() {
        var w = UpstreamServerList("Paperwood", new[] { ("1.2.3.4", (ushort) 1235) }, new short[] { 1, 2, 3 });
        var list = ServerList.Read(new ByteReader(Bytes(w)), Gms);
        Assert.That(list.Usable, Is.True);
        Assert.That(list.Servers, Has.Count.EqualTo(1));
        var s = list.Servers[0];
        Assert.That(s.Name, Is.EqualTo("Paperwood"));
        Assert.That(s.Addresses, Is.EqualTo(new[] { ("1.2.3.4", (ushort) 1235) }));
        Assert.That(s.TrafficPercent, Is.EqualTo(100));
        Assert.That(s.Channels, Is.EqualTo(new ushort[] { 1, 2, 3 }));
    }

    [Test]
    public void ServerListErrorIsAnEmptyList() {
        var w = new ByteWriter(); w.WriteByte(0);   // ServerListPacket.Error
        var list = ServerList.Read(new ByteReader(Bytes(w)), Gms);
        Assert.That(list.Usable, Is.False);
    }

    [Test]
    public void ServerListForKmsStillReadsTheKmsLayout() {
        var w = new ByteWriter();
        w.WriteString("env"); w.WriteByte(1);
        w.WriteInt(1); w.WriteUnicodeString("S"); w.WriteByte(0); w.WriteShort(1); w.WriteUnicodeString("9.9.9.9"); w.WriteShort(1); w.WriteInt(5); w.WriteShort(1); w.WriteShort(1);
        var list = ServerList.Read(new ByteReader(Bytes(w)), ProtocolProfile.Kms2525);
        Assert.That(list.EnvName, Is.EqualTo("env"));
        Assert.That(list.Servers[0].Name, Is.EqualTo("S"));
    }
}
