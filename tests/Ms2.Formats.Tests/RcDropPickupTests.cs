using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The drop pickup's widening range and the special-item bag rule (loot-drops tasks 6 and 12). The client's
/// FUN_141191a10 raises a resting drop's range to the distance from its origin to where it is now plus 10, and the test
/// uses max(that, DropItemMaxLength) (dropitem.cpp:1190-1216, test :789-827); a currency/special drop sends without a bag
/// test (dropitem.cpp:954-958, the EP ball is item 0x55d4a8a). Expected API: DropPickup.WidenedRange(float drift, float
/// maxLength) and the four-argument DropPickup.BagAllows(hasEmptySlot, canStack, force, itemId).
/// </summary>
public class RcDropPickupTests
{
    [Ignore("the widened pickup range is not ported")]
    [Test]
    public void ARestingDropWidensItsRangeFromTheOrigin()
    {
        float range = RcLane.Static<float>("Ms2.Formats.World.DropPickup", "WidenedRange", 310f, 100f);
        Assert.That(range, Is.EqualTo(320f), "distance from the origin plus 10");
        Assert.That(DropPickup.Eligible(DropMotion.Rest, 100, 0, 0, 0, 310f, range, true), Is.True,
            "a drop launched 300 units away is 310 from the origin and inside 320");
        Assert.That(DropPickup.Eligible(DropMotion.Rest, 100, 0, 0, 0, 321f, range, true), Is.False);
        Assert.That(RcLane.Static<float>("Ms2.Formats.World.DropPickup", "WidenedRange", 20f, 100f), Is.EqualTo(100f),
            "the fixed range still wins when the drift is small");
    }

    [Ignore("the special-item bag rule is not ported")]
    [Test]
    public void ASpecialItemNeedsNoBagSlot()
    {
        bool Allows(int itemId) => RcLane.Static<bool>("Ms2.Formats.World.DropPickup", "BagAllows", false, false, false, itemId);
        Assert.That(Allows(0x55d4a8a), Is.True, "the EP ball is sent without a bag test");
        Assert.That(Allows(0x55d4a89), Is.True, "a special currency item");
        Assert.That(Allows(30000001), Is.False, "an ordinary item on a full bag");
        Assert.That(RcLane.Static<bool>("Ms2.Formats.World.DropPickup", "BagAllows", true, false, false, 30000001), Is.True);
        Assert.That(RcLane.Static<bool>("Ms2.Formats.World.DropPickup", "BagAllows", false, true, false, 30000001), Is.True);
        Assert.That(RcLane.Static<bool>("Ms2.Formats.World.DropPickup", "BagAllows", false, false, true, 30000001), Is.True);
    }
}
