using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
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
        var w = UpstreamServerList("Paperwood", new[] { ("1.2.3.4", (ushort) 20002) }, new short[] { 1, 2, 3 });
        var list = ServerList.Read(new ByteReader(Bytes(w)), Gms);
        Assert.That(list.Usable, Is.True);
        Assert.That(list.Servers, Has.Count.EqualTo(1));
        var s = list.Servers[0];
        Assert.That(s.Name, Is.EqualTo("Paperwood"));
        Assert.That(s.Addresses, Is.EqualTo(new[] { ("1.2.3.4", (ushort) 20002) }));
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
