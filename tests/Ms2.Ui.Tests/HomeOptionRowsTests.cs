using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CubeMapInformationService's option rows (cubemapinformationservice_3.cpp:1076-1085) and CanChange (:1186).</summary>
public class HomeOptionRowsTests
{
    [Test]
    public void ARowNeverSentIsOffAtLevelZero()
    {
        var r = new HomeOptionRows();
        Assert.That(r.RowOn(3), Is.False);
        Assert.That(r.RowLevel(3), Is.EqualTo(0));
    }

    [Test]
    public void TheSwitchAndTheLevelAreKeptPerRow()
    {
        var r = new HomeOptionRows();
        r.Apply(new ResponseCubeRowSwitch(2, true));
        r.Apply(new ResponseCubeRowLevel(2, 3));
        r.Apply(new ResponseCubeRowSwitch(5, true));
        r.Apply(new ResponseCubeRowSwitch(5, false));
        Assert.That(r.RowOn(2), Is.True);
        Assert.That(r.RowLevel(2), Is.EqualTo(3));
        Assert.That(r.RowOn(5), Is.False);
        Assert.That(r.RowLevel(5), Is.EqualTo(0));
    }

    [Test]
    public void TheResetPutsRowsZeroToEightBackOff()
    {
        var r = new HomeOptionRows();
        r.Apply(new ResponseCubeRowSwitch(0, true));
        r.Apply(new ResponseCubeRowLevel(8, 2));
        r.Reset();
        Assert.That(r.RowOn(0), Is.False);
        Assert.That(r.RowLevel(8), Is.EqualTo(0));
    }

    [TestCase(true, false, 10L, 10L, true)]
    [TestCase(false, false, 10L, 10L, false)]
    [TestCase(true, true, 10L, 10L, false)]
    [TestCase(true, false, 11L, 10L, false)]
    [TestCase(true, false, 0L, 0L, true)]
    public void CanChangeNeedsAHomeFieldF330NotOneAndTheServiceOwnerIsMe(bool home, bool f330, long owner, long me, bool want)
        => Assert.That(HomeOptionRows.CanChange(home, f330, owner, me), Is.EqualTo(want));
}
