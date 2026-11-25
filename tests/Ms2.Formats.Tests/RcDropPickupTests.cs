using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The drop pickup's widening range and the special-item rules (loot-drops tasks 6 and 12). FUN_141191a10 raises a resting drop's
/// m_f27c to the distance from its origin to where it is now plus 10, and FUN_141190120 tests against the larger of that and m_f21c
/// (dropitem.cpp:1190-1216, test :789-827); its one caller is the summoned pet's item pick (worldservice_1.cpp:6347). A currency or
/// special drop is requested without FUN_141190900's bag-full refusal (dropitem.cpp:954-958), and the SP and EP orbs take the
/// DropSPEPBall constants (LoadSpepBallConstants, :269-270, :1093-1102).
/// </summary>
public class RcDropPickupTests
{
    [Test]
    public void ARestingDropWidensItsRangeFromTheOrigin()
    {
        float range = DropPickup.WidenedRange(310f, 100f);
        Assert.That(range, Is.EqualTo(320f), "distance from the origin plus 10");
        Assert.That(DropPickup.Eligible(DropMotion.Rest, 5000, 0, 0, 0, 310f, range, true), Is.True,
            "a drop that came to rest 310 from its origin is in range at 310");
        Assert.That(DropPickup.Eligible(DropMotion.Rest, 5000, 0, 0, 0, 321f, range, true), Is.False);
        Assert.That(DropPickup.WidenedRange(20f, 100f), Is.EqualTo(100f), "the fixed range still wins when the drift is small");
    }

    /// <summary>
    /// FUN_141190900 (dropitem.cpp:954-958) sends a currency or special id with no bag test, so no bag-full refusal comes first.
    /// The get key's FUN_141190640 (:906-911) has no item test: with no room, no stack and the key held on, no item is requested,
    /// an orb included, and a fresh press sends it.
    /// </summary>
    [Test]
    public void ASpecialItemNeedsNoBagSlot()
    {
        Assert.That(DropPickup.BagFull(0x55d4a8a, false, false), Is.False, "the EP orb is sent without the bag-full refusal");
        Assert.That(DropPickup.BagFull(0x55d4a89, false, false), Is.False, "the SP orb");
        Assert.That(DropPickup.BagFull(30000001, false, false), Is.True, "an ordinary item on a full bag");
        Assert.That(DropPickup.BagFull(30000001, true, false), Is.False);
        Assert.That(DropPickup.BagFull(30000001, false, true), Is.False);
        Assert.That(DropPickup.BagAllows(false, false, false), Is.False, "the key's test has no special-id case");
        Assert.That(DropPickup.BagAllows(true, false, false), Is.True);
        Assert.That(DropPickup.BagAllows(false, true, false), Is.True);
        Assert.That(DropPickup.BagAllows(false, false, true), Is.True, "a fresh key press");
    }

    /// <summary>
    /// LoadSpepBallConstants replaces m_f218, m_f21c, m_f224, m_f228, m_f22c and m_f230 for 0x55d4a89 and 0x55d4a8a only; the money
    /// range and the refused hop stay the item ones, and 0x55d4a88 / 0x55d4a93 (special, but not orbs) keep the item set.
    /// </summary>
    [Test]
    public void TheSpAndEpOrbsTakeTheSpepBallConstants()
    {
        var c = new ClientConstants(new Dictionary<string, string>
        {
            ["DropItemTargetZPos"] = "50", ["DropItemMaxLength"] = "150", ["DropMoneyMaxLength"] = "300", ["DropItemPickUpVel"] = "600",
            ["DropItemPickUpGravity"] = "-1000", ["DropItemPickUpCompleteRotateTime"] = "0.5", ["DropItemPickUpCompleteRotateVel"] = "3",
            ["DropItemPickFailTime"] = "0.3", ["DropItemPickFailHeight"] = "40",
            ["DropSPEPBallTargetZPos"] = "90", ["DropSPEPBallMaxLength"] = "400", ["DropSPEPBallPickUpVel"] = "900",
            ["DropSPEPBallPickUpGravity"] = "-2000", ["DropSPEPBallPickUpCompleteRotateTime"] = "0.25", ["DropSPEPBallPickUpCompleteRotateVel"] = "6",
        });
        var k = DropConstants.From(c);
        foreach (int id in new[] { 0x55d4a89, 0x55d4a8a })
        {
            var b = k.ForItem(id);
            Assert.That((b.TargetZ, b.MaxLength, b.PickUpVel, b.PickUpGravity, b.RotateTime, b.RotateVel),
                Is.EqualTo((90f, 400f, 900f, -2000f, 0.25f, 6f)), $"0x{id:x}");
            Assert.That((b.MoneyMaxLength, b.FailTime, b.FailHeight), Is.EqualTo((300f, 0.3f, 40f)), $"0x{id:x} keeps the rest");
        }
        foreach (int id in new[] { 30000001, 0x55d4a88, 0x55d4a93, 0x55d4a81 })
            Assert.That(k.ForItem(id), Is.SameAs(k), $"{id} keeps the item set");
        Assert.That((k.TargetZ, k.MaxLength, k.PickUpVel), Is.EqualTo((50f, 150f, 600f)), "the item set is unchanged");
    }

    /// <summary>The get key reaches an orb as far as its own m_f21c (FUN_141190320 with the SPEP length), any other item its own.</summary>
    [Test]
    public void TheKeyReachesAnOrbAsFarAsItsOwnRange()
    {
        var p = new PickupPlanner();
        p.Candidates.Add(new PickupCandidate { ObjectId = 1, State = DropMotion.Rest, Distance = 300f, Owner = true, Range = 400f });
        p.Plan(5000, 0, 150, 300, 0x80);
        Assert.That(p.Nearest, Is.EqualTo(0), "an orb 300 away with a 400 range");
        p.Clear();
        p.Candidates.Add(new PickupCandidate { ObjectId = 2, State = DropMotion.Rest, Distance = 300f, Owner = true, Range = 150f });
        p.Plan(5000, 0, 150, 300, 0x80);
        Assert.That(p.Nearest, Is.EqualTo(-1), "an item 300 away with a 150 range");
        Assert.That(p.Hop, Is.EqualTo(-1));
    }
}
