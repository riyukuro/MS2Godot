using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>PersonalInfoAgreement 0xC0 (CPersonalInfoAgreement::SendAgreement, gameoptionstruct.cpp:3445-3452).</summary>
public class PersonalInfoAgreementTests {
    [Test] public void WritesGroupThenFlagsUnderOpcode0xC0() {
        Assert.That((ushort)Outbound.PersonalInfoAgreement, Is.EqualTo(0x00C0));
        var p = new PersonalInfoAgreement(2, 0x0A);
        Assert.That(p.Op, Is.EqualTo(Outbound.PersonalInfoAgreement));
        var w = new ByteWriter();
        p.Write(w);
        var bytes = w.ToArray();
        Assert.That(bytes, Is.EqualTo(new byte[] { 2, 0, 0, 0, 0x0A, 0, 0, 0 }));
        Assert.That(PersonalInfoAgreement.Read(new ByteReader(bytes)), Is.EqualTo(p));
    }
}

/// <summary>MyPC::SendHouseSetting (@0x141530720): RequestCube 0x37, u8 0x18, u8 kind, string, u8 flag.</summary>
public class HouseSettingPacketTests {
    static byte[] Bytes(Ms2.Net.Wire.IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer[..w.Length]; }

    [Test] public void PasswordKindWritesTheSixDigitsAndAZeroFlag() {
        var p = new HouseSettingRequest(1, "123456", false);
        Assert.That(p.Op, Is.EqualTo(Outbound.RequestCube));
        var w = new ByteWriter(); w.WriteByte(0x18); w.WriteByte(1); w.WriteUnicodeString("123456"); w.WriteByte(0);
        Assert.That(Bytes(p), Is.EqualTo(w.Buffer[..w.Length]));
    }

    [Test] public void HomeOptionRowPacketsAreMode42AndMode43() {
        Assert.That(Bytes(new HomeOptionRowSwitchRequest(3, true)), Is.EqualTo(new byte[] { 0x2a, 3, 1 }));
        Assert.That(Bytes(new HomeOptionRowLevelRequest(8, 4)), Is.EqualTo(new byte[] { 0x2b, 8, 4 }));
        Assert.That(new HomeOptionRowLevelRequest(0, 0).Op, Is.EqualTo(Outbound.RequestCube));
    }

    [Test] public void KickUsersIsModeOnly0x31() {
        var p = new HomeKickUsersRequest();
        Assert.That(p.Op, Is.EqualTo(Outbound.RequestCube));
        Assert.That(Bytes(p), Is.EqualTo(new byte[] { 0x31 }));
    }

    [Test] public void PublicKindWritesAnEmptyStringAndAOneFlag() {
        var b = Bytes(new HouseSettingRequest(0, "", true));
        Assert.That(b, Is.EqualTo(new byte[] { 0x18, 0, 0, 0, 1 }));
    }

    [Test] public void SameOpcodeAsTheOtherCubeRequestsInBothProfiles() {
        Assert.That((ushort) Outbound.RequestCube, Is.EqualTo(0x37));
    }
}
