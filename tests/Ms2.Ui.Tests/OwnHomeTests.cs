using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>MyPC's UserUGCMapInfo as ResponseCube 0x14 fills it (scenestategame.cpp:10199-10244).</summary>
public class OwnHomeTests
{
    static ResponseCubeHomeInfo Info(int owner, long home, int plot = 3, byte open = 0) => new(owner, plot, 0, 0, "", home, 0, 0, open);

    [Test]
    public void AnotherObjectsInfoChangesNothing()
    {
        var h = new OwnHome();
        Assert.That(h.Apply(Info(8, 5), 7, out _), Is.False);
        Assert.That(h.HasInfo, Is.False);
        Assert.That(h.HasNoHome, Is.True);
    }

    [Test]
    public void TheLocalInfoIsStoredAndZeroHomeMeansNoHome()
    {
        var h = new OwnHome();
        Assert.That(h.Apply(Info(7, 0), 7, out var changed), Is.True);
        Assert.That(h.HasInfo && h.HasNoHome && !changed, Is.True);
        h.Apply(Info(7, 5, plot: 9, open: 2), 7, out changed);
        Assert.That(h.HasNoHome, Is.False);
        Assert.That(h.PlotId, Is.EqualTo(9));
        Assert.That(h.OpenType, Is.EqualTo(2));
        Assert.That(changed, Is.False, "the first info with a home is not a change: the player had none");
    }

    [Test]
    public void AChangedHomeIdIsReportedOnlyWhenThePlayerHadAHome()
    {
        var h = new OwnHome();
        h.Apply(Info(7, 5), 7, out _);
        h.Apply(Info(7, 5), 7, out var same);
        Assert.That(same, Is.False);
        h.Apply(Info(7, 6), 7, out var moved);
        Assert.That(moved, Is.True);
    }
}
