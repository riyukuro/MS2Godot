using Ms2.Formats.Options;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>What MyPC::SendHouseSetting (mypc.cpp:4385-4396) does to the agreement: group 1 takes 8, 0x10 or 0x20 for kind 0, 1, 2 and that mask is the group sent.</summary>
public class HouseSettingAgreementTests
{
    [TestCase(0, 8)]
    [TestCase(1, 0x10)]
    [TestCase(2, 0x20)]
    public void EachKindSetsItsFlagAndSendsItAsTheGroup(int kind, int mask)
    {
        var a = new PersonalInfoAgreement { Flags = 0x38 | 2 };   // an old house answer and the group 0 agreement
        Assert.That(HouseSettingAgreement.Apply(a, kind), Is.EqualTo(mask));
        Assert.That(a.Flags, Is.EqualTo((uint) (2 | mask)), "group 1 replaced, group 0 kept");
    }

    [Test]
    public void AnUnknownKindChangesNothing()
    {
        var a = new PersonalInfoAgreement { Flags = 0x12 };
        Assert.That(HouseSettingAgreement.Apply(a, 3), Is.EqualTo(0));
        Assert.That(a.Flags, Is.EqualTo(0x12u));
    }
}
