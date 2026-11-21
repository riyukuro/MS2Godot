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
