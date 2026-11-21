using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Field-enter packets of the GMS2 profile, built from upstream ServerEnterPacket.cs and FieldEnterPacket.cs.</summary>
public class Gms2FieldEnterLayoutTests {
    static byte[] ServerEnterBytes(bool gms2) {
        var w = new ByteWriter();
        w.WriteInt(7); w.WriteLong(99); w.WriteShort(3); w.WriteLong(10); w.WriteLong(11); w.WriteLong(12);
        for (int i = 0; i < 14; i++) w.WriteLong(100 + i);
        w.WriteUnicodeString("pic"); w.WriteByte(0);
        if (gms2) w.WriteByte(1);
        w.WriteShort(2); w.WriteInt(1); w.WriteInt(2);
        w.WriteShort(1); w.WriteInt(5);
        w.WriteLong(0);
        if (gms2) { foreach (var s in new[] { "", "http://x", "", "^pat", "" }) w.WriteUnicodeString(s); }
        else { foreach (var s in new[] { "g", "u1", "u2", "u3" }) w.WriteUnicodeString(s); }
        return w.ToArray();
    }

    [Test]
    public void ServerEnterGms2HasExtraByteAndFiveStrings() {
        var e = ServerEnter.Read(new ByteReader(ServerEnterBytes(true)), ProtocolProfile.Gms2);
        Assert.That(e.ObjectId, Is.EqualTo(7));
        Assert.That(e.Gms2Flag, Is.EqualTo(1));
        Assert.That(e.UnlockedMaps, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(e.UnlockedTaxis, Is.EqualTo(new[] { 5 }));
        Assert.That(e.Gms2Strings, Is.EqualTo(new[] { "", "http://x", "", "^pat", "" }));
    }

    [Test]
    public void ServerEnterKmsFormUnchanged() {
        var e = ServerEnter.Read(new ByteReader(ServerEnterBytes(false)), ProtocolProfile.Kms2525);
        Assert.That(e.Url3, Is.EqualTo("u3"));
        Assert.Catch<Exception>(() => ServerEnter.Read(new ByteReader(ServerEnterBytes(false)), ProtocolProfile.Gms2), "KMS bytes are not a GMS2 frame");
    }

    [Test]
    public void RequestFieldEnterSameUnderGms2() {
        var w = new ByteWriter();
        w.WriteByte(0); w.WriteInt(2000001); w.WriteByte(0); w.WriteByte(1); w.WriteInt(5); w.WriteInt(0);
        for (int i = 0; i < 6; i++) w.WriteFloat(i); w.WriteInt(1);
        var f = RequestFieldEnter.Read(new ByteReader(w.ToArray()));
        Assert.That(f.MapId, Is.EqualTo(2000001));
        Assert.That(f.Portal, Is.EqualTo(1));
    }
}
