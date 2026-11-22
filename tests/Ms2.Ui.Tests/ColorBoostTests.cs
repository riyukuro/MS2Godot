using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>An actor's colour boosts: CColorBoostManager::UpdateBoosts and CColorBoostData::FUN_14109b770.</summary>
public class ColorBoostTests
{
    [Test]
    public void ABoostFadesFromItsScaleToOneOverItsDuration()
    {
        var b = new ColorBoost();
        b.Add(0f, 2f, 0.1f, 0.1f);
        Assert.That((b.Update(0f), b.Scale), Is.EqualTo((true, 3f)));
        Assert.That(b.Update(0.05f), Is.True);
        Assert.That(b.Scale, Is.EqualTo(2f).Within(1e-5f), "half its time left: half its share");
        // past its duration it is removed and the scale goes back to 1, once
        Assert.That((b.Update(0.2f), b.Scale, b.Count), Is.EqualTo((true, 1f, 0)));
        Assert.That(b.Update(0.3f), Is.False);
    }

    [Test]
    public void ALaterBoostIsCappedByItsIndex()
    {
        // the second's share: min(3 + 2, 1 + (1 + 1 * 0.1) * 2) - 3
        var b = new ColorBoost();
        b.Add(0f, 2f, 0.1f, 0.1f);
        b.Add(0f, 2f, 0.1f, 0.1f);
        b.Update(0f);
        Assert.That(b.Scale, Is.EqualTo(3.2f).Within(1e-5f));
    }

    [Test]
    public void TheIndexCountsTheBoostsRemovedOnTheSameUpdate()
    {
        // the first has ended; the third is still index 2: min(3 + 2, 1 + (1 + 2 * 0.1) * 2) - 3
        var b = new ColorBoost();
        b.Add(0f, 2f, 0.1f, 0f);
        b.Add(0.15f, 2f, 0.1f, 0.1f);
        b.Add(0.15f, 2f, 0.1f, 0.1f);
        b.Update(0.15f);
        Assert.That(b.Count, Is.EqualTo(2));
        Assert.That(b.Scale, Is.EqualTo(3.4f).Within(1e-5f));
    }

    [Test]
    public void AShareIsFixedOnItsFirstUpdate()
    {
        // at 0.5 the first adds 2 * 0.5; the second its fixed 0.2 * 0.5, not min(2 + 2, 3.2) - 2 again
        var b = new ColorBoost();
        b.Add(0f, 2f, 1f, 0.1f);
        b.Add(0f, 2f, 1f, 0.1f);
        b.Update(0f);
        b.Update(0.5f);
        Assert.That(b.Scale, Is.EqualTo(2.1f).Within(1e-5f));
    }

    [Test]
    public void AnUnchangedScaleIsNotWrittenAgain()
    {
        var b = new ColorBoost();
        Assert.That(b.Update(0f), Is.False, "no boost: 1, as at the start");
        b.Add(0f, 0f, 1f, 0.1f);
        Assert.That(b.Update(0.5f), Is.False, "a boost of scale 0 changes nothing");
    }

    [Test]
    public void AFrameAllocatesNothing()
    {
        var b = new ColorBoost();
        b.Add(0f, 2f, 0.1f, 0.1f);
        b.Update(0f);
        b.Update(1f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 1000; f++)
        {
            if (b.Count < 5) b.Add(f * 0.01f, 2f, 0.1f, 0.1f);
            b.Update(f * 0.01f);
        }
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0L));
    }
}
