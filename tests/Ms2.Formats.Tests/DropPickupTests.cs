using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The pickup tests of CDropItem (FUN_1411903c0, HasPickupDelayElapsed, FUN_141190640, FUN_1411900d0), with the clock passed in.</summary>
public class DropPickupTests {
    static bool Ok(int state = DropMotion.Rest, uint now = 5000, uint global = 0, uint last = 0, uint fade = 0, float dist = 10, float range = 100, bool owner = true)
        => DropPickup.Eligible(state, now, global, last, fade, dist, range, owner);

    [Test] public void ARestingDropInRangeIsEligible() => Assert.That(Ok(), Is.True);

    [Test] public void EachRefusalHolds() {
        Assert.That(Ok(global: 5001), Is.False, "the shared timer is still running");
        Assert.That(Ok(state: DropMotion.FlyOut), Is.False, "not resting");
        Assert.That(Ok(state: DropMotion.FlyToPicker), Is.False, "already flying to a picker");
        Assert.That(Ok(last: 4500), Is.False, "asked less than 1000 ms ago");
        Assert.That(Ok(fade: 5001), Is.False, "still fading");
        Assert.That(Ok(dist: 100.5f), Is.False, "out of range");
        Assert.That(Ok(owner: false), Is.False, "someone else's drop");
    }

    [Test] public void TheBoundariesAreTheClientsComparisons() {
        Assert.That(Ok(global: 5000), Is.True);
        Assert.That(Ok(last: 4000), Is.True);
        Assert.That(Ok(last: 4001), Is.False);
        Assert.That(Ok(dist: 100), Is.True);
    }

    [Test] public void RangeIsPerKind() {
        var k = new DropConstants { MaxLength = 150, MoneyMaxLength = 300 };
        Assert.That(DropPickup.Range(k, true), Is.EqualTo(k.MoneyMaxLength));
        Assert.That(DropPickup.Range(k, false), Is.EqualTo(k.MaxLength));
    }

    [Test] public void TheBagRule() {
        Assert.That(DropPickup.BagAllows(false, false, false), Is.False);
        Assert.That(DropPickup.BagAllows(true, false, false), Is.True);
        Assert.That(DropPickup.BagAllows(false, true, false), Is.True);
        Assert.That(DropPickup.BagAllows(false, false, true), Is.True);
    }

    [Test] public void ARemoveKeepsOnlyADropBeingPickedUp() {
        Assert.That(DropPickup.KeepsOnRemove(77, DropMotion.FlyToPicker), Is.True);
        Assert.That(DropPickup.KeepsOnRemove(77, DropMotion.Rest), Is.False);
        Assert.That(DropPickup.KeepsOnRemove(0, DropMotion.FlyToPicker), Is.False);
    }
}
