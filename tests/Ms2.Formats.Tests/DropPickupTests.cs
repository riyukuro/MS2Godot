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

    static PickupPlanner Planner(params PickupCandidate[] c) { var p = new PickupPlanner(); p.Candidates.AddRange(c); return p; }
    static PickupCandidate C(int id, float dist, bool meso = false, bool owner = true, uint fade = 0, int state = DropMotion.Rest) =>
        new() { ObjectId = id, Distance = dist, Meso = meso, Owner = owner, FadeUntil = fade, State = state };

    [Test] public void AMesoInBothRangesIsSentOnceAndNotRequestedAsTheNearestDrop() {
        var p = Planner(C(1, 10, meso: true));
        p.Plan(5000, 0, 100, 300, 0x80);
        Assert.That(p.Mesos, Is.EqualTo(new[] { 0 }));
        Assert.That(p.Nearest, Is.EqualTo(-1), "the meso was stamped, so the item pass refuses it (HasPickupDelayElapsed)");
        Assert.That(p.Candidates[0].LastRequest, Is.EqualTo(5000u));
    }

    [Test] public void AnItemBesideAMesoIsStillTheNearest() {
        var p = Planner(C(1, 10, meso: true), C(2, 40));
        p.Plan(5000, 0, 100, 300, 0x80);
        Assert.That((p.Mesos.Count, p.Nearest), Is.EqualTo((1, 1)));
    }

    [Test] public void MesosBeyondTheItemRangeAreSentAndTheCountIsCapped() {
        var p = Planner(C(1, 200, meso: true), C(2, 250, meso: true), C(3, 280, meso: true));
        p.Plan(5000, 0, 100, 300, 2);
        Assert.That(p.Mesos, Is.EqualTo(new[] { 0, 1 }));
    }

    [Test] public void NothingIsPlannedWhileTheSharedTimerRuns() {
        var p = Planner(C(1, 10, meso: true), C(2, 10));
        p.Plan(5000, 5001, 100, 300, 0x80);
        Assert.That((p.Mesos.Count, p.Nearest, p.Hop), Is.EqualTo((0, -1, -1)));
    }

    [Test] public void NoPickupWhileDeadOrOnAMountThatDisablesIt() {
        Assert.That(DropPickup.PickupAllowed(false, false), Is.True);
        Assert.That(DropPickup.PickupAllowed(true, false), Is.False, "dead (mypc.cpp:4555, :4712)");
        Assert.That(DropPickup.PickupAllowed(false, true), Is.False, "riding a mount with disablePickUp (mypc.cpp:4716-4721)");
    }

    [Test] public void APetTrapDropIsUnpickableUntilTheDelayEnds() {
        Assert.That(DropPickup.FadeUntil(1000, 5, true, true, 3000), Is.EqualTo(4000u), "a pet item with the trap flag (SetState 1)");
        Assert.That(DropPickup.FadeUntil(1000, 0xc80, false, true, 3000), Is.EqualTo(4000u), "a drop of kind 0xc80 that is no pet item (held until the delay ends)");
        Assert.That(DropPickup.FadeUntil(1000, 5, false, true, 3000), Is.EqualTo(0u), "any other drop");
        Assert.That(DropPickup.FadeUntil(1000, 0xc80, true, false, 3000), Is.EqualTo(0u), "no trap flag");
        var p = Planner(C(1, 10, fade: 4000));
        p.Plan(3999, 0, 100, 300, 0x80);
        Assert.That(p.Nearest, Is.EqualTo(-1));
        p.Plan(4000, 0, 100, 300, 0x80);
        Assert.That(p.Nearest, Is.EqualTo(0));
    }

    [Test] public void WithNoDropToRequestTheNearestOneIgnoringTheOwnerRuleHops() {
        var p = Planner(C(1, 30, owner: false), C(2, 10, owner: false), C(3, 500, owner: true));
        p.Plan(5000, 0, 100, 300, 0x80);
        Assert.That((p.Nearest, p.Hop), Is.EqualTo((-1, 1)));
        p = Planner(C(1, 30, owner: false), C(2, 40, owner: true));
        p.Plan(5000, 0, 100, 300, 0x80);
        Assert.That((p.Nearest, p.Hop), Is.EqualTo((1, -1)), "a drop to request: no hop");
        p = Planner(C(1, 500, owner: false));
        p.Plan(5000, 0, 100, 300, 0x80);
        Assert.That((p.Nearest, p.Hop), Is.EqualTo((-1, -1)), "out of range");
    }

    [Test] public void EveryCreatedDropRestartsTheSharedTimerAtTenMilliseconds() => Assert.That(DropPickup.AfterCreateMs, Is.EqualTo(10u));
}

/// <summary>FUN_141190900's bag test, OnPickedUp's toast and guide tests, the item id kinds and OnFieldRemoveItem's fade.</summary>
public class DropPickupRulesTests {
    [Test] public void TheIdKindsAreTheClientsLists() {
        Assert.That(new[] { 90000001, 90000002, 90000003 }.All(DropPickup.IsMesoId), Is.True);
        Assert.That(new[] { 90000008, 90000009, 90000010, 90000019 }.All(DropPickup.IsSpecialId), Is.True, "FUN_14053f900");
        Assert.That(new[] { 90000004, 90000011, 90000015, 90000016 }.All(DropPickup.IsMeratId), Is.True, "FUN_14053f940");
        Assert.That(DropPickup.IsTokenId(90000013), Is.True);
        Assert.That(DropPickup.IsTokenId(90000012), Is.False);
        Assert.That(DropPickup.IsCurrencyId(90000013) && DropPickup.IsCurrencyId(90000001) && DropPickup.IsCurrencyId(90000004), Is.True);
        Assert.That(DropPickup.IsCurrencyId(90000008), Is.False, "the EP ball is special, not currency");
        Assert.That(DropPickup.IsCurrencyId(20000001), Is.False);
    }

    [Test] public void TheBagFullRefusalNeedsNoRoomNoStackAndAnOrdinaryItem() {
        Assert.That(DropPickup.BagFull(20000001, tabHasRoom: false, canStack: false), Is.True);
        Assert.That(DropPickup.BagFull(20000001, tabHasRoom: true, canStack: false), Is.False);
        Assert.That(DropPickup.BagFull(20000001, tabHasRoom: false, canStack: true), Is.False);
        Assert.That(DropPickup.BagFull(90000010, false, false), Is.False, "an EP ball is sent with no bag test");
        Assert.That(DropPickup.BagFull(90000013, false, false), Is.False, "a token is currency");
        Assert.That(DropPickup.BagFull(90000004, false, false), Is.False, "a merat is currency");
        Assert.That((DropPickup.BagFullNotice, DropPickup.TooFarNotice, DropPickup.BagFullSound), Is.EqualTo((0x43, 0x45, "system_notice_error_warningBag")));
    }

    [Test] public void TheToastIsForGradedItemsThatAreNoCurrencyNorSpecial() {
        Assert.That(DropPickup.Toasts(20000001, 1, false), Is.True);
        Assert.That(DropPickup.Toasts(20000001, 0, false), Is.False, "rarity 0");
        Assert.That(DropPickup.Toasts(90000001, 1, true), Is.False, "a meso");
        Assert.That(DropPickup.Toasts(90000013, 4, false), Is.False, "a token");
        Assert.That(DropPickup.Toasts(90000004, 4, false), Is.False, "a merat");
        Assert.That(DropPickup.Toasts(90000010, 1, false), Is.False, "the EP ball (FUN_141abfa10 returns)");
    }

    [Test] public void TheGuideTriggerSkipsAnExpiredItemUnlessPetOrMaid() {
        Assert.That(DropPickup.GuideCounts(0, 1000, false, false), Is.True, "no expiry");
        Assert.That(DropPickup.GuideCounts(2000, 1000, false, false), Is.True, "not yet expired");
        Assert.That(DropPickup.GuideCounts(1000, 1000, false, false), Is.False, "IsExpired: expiry <= now");
        Assert.That(DropPickup.GuideCounts(500, 1000, true, false), Is.True, "a pet");
        Assert.That(DropPickup.GuideCounts(500, 1000, false, true), Is.True, "a maid id");
    }

    [Test] public void ARemovedDropFadesOutOverHalfASecond() {
        Assert.That(DropPickup.RemoveAlpha(0), Is.EqualTo(1f));
        Assert.That(DropPickup.RemoveAlpha(0.25f), Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(DropPickup.RemoveAlpha(0.5f), Is.EqualTo(0f));
        Assert.That(DropPickup.RemoveAlpha(2f), Is.EqualTo(0f));
    }
}
