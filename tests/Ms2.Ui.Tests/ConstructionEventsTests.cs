using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CGameRule's construction sets (gamerule.cpp:2428-2541) as GameEventManagerCli fills them.</summary>
public class ConstructionEventsTests
{
    static MassiveConstructionEvent Massive(int id, uint[] fields, int channel, params long[] accounts) => new(id, fields, channel, accounts);
    static IgnoreConstructionLockEvent Ignore(int id, params long[] accounts) => new(id, accounts);
    static GameEventMessage Msg(byte kind, params object[] e) =>
        new(kind, Array.Empty<ShutdownInteractObjectEvent>(), Array.Empty<int>(), false) { ConstructionEvents = e };
    static GameEventMessage Removed(params int[] ids) => new(2, Array.Empty<ShutdownInteractObjectEvent>(), ids, false);

    [Test]
    public void AMassiveConstructionHoldsItsFieldsOnItsChannel()
    {
        var c = new ConstructionEvents();
        Assert.That(c.IsMassiveConstruction(100, 1, 9), Is.False);
        c.Apply(Msg(0, Massive(1, new[] { 100u, 101u }, 1)));
        Assert.That(c.IsMassiveConstruction(100, 1, 9) && c.IsMassiveConstruction(101, 1, 9), Is.True);
        Assert.That(c.IsMassiveConstruction(100, 2, 9), Is.False, "another channel");
        Assert.That(c.IsMassiveConstruction(102, 1, 9), Is.False, "another field");
    }

    [Test]
    public void AnAccountListLimitsItToThoseAccounts()
    {
        // FUN_14047ddd0 (:2462-2475): a non-empty m_cb0 without the account is false before the field test
        var c = new ConstructionEvents();
        c.Apply(Msg(0, Massive(1, new[] { 100u }, 1, 7L, 8L)));
        Assert.That(c.IsMassiveConstruction(100, 1, 7), Is.True);
        Assert.That(c.IsMassiveConstruction(100, 1, 9), Is.False);
    }

    [Test]
    public void RemovingOneMassiveEventClearsEveryOnesSets()
    {
        // vfunc_6 (gameevent.cpp:2961-2965) clears the whole of m_ca0 and m_cb0
        var c = new ConstructionEvents();
        c.Apply(Msg(0, Massive(1, new[] { 100u }, 1), Massive(2, new[] { 200u }, 1, 7L)));
        Assert.That(c.IsMassiveConstruction(200, 1, 7), Is.True);
        c.Apply(Removed(1));
        Assert.That(c.IsMassiveConstruction(200, 1, 7), Is.False);
        Assert.That(c.IsMassiveConstruction(100, 1, 7), Is.False);
    }

    [Test]
    public void AddSkipsAnIdAlreadyHeld()
    {
        // GameEventManagerCli::Add (:211-220): no second OnStart for a held id
        var c = new ConstructionEvents();
        c.Apply(Msg(1, Ignore(1, 5L)));
        c.Apply(Msg(1, Ignore(1, 6L)));
        Assert.That(c.IsIgnoredConstructionLock(5), Is.True);
        Assert.That(c.IsIgnoredConstructionLock(6), Is.False);
    }

    [Test]
    public void UpdateStopsThePreviousEventThenStartsTheNewOne()
    {
        // Update (:236-242) -> vfunc_7 (gameeventbase.cpp:202-208)
        var c = new ConstructionEvents();
        c.Apply(Msg(0, Ignore(1, 5L), Ignore(2, 6L)));
        c.Apply(Msg(3, Ignore(1, 7L)));
        Assert.That(c.IsIgnoredConstructionLock(7), Is.True);
        Assert.That(c.IsIgnoredConstructionLock(5), Is.False);
        Assert.That(c.IsIgnoredConstructionLock(6), Is.False, "ClearIgnoredConstructionLockIds empties the whole list");
        c.Apply(Msg(3, Ignore(3, 8L)));
        Assert.That(c.IsIgnoredConstructionLock(7) && c.IsIgnoredConstructionLock(8), Is.True, "no previous event, nothing stopped");
    }

    [Test]
    public void ClearStopsEveryEvent()
    {
        // GameEventManagerCli::Clear (:98-107)
        var c = new ConstructionEvents();
        c.Apply(Msg(0, Ignore(1, 5L), Massive(2, new[] { 100u }, 1)));
        c.Clear();
        Assert.That(c.IsIgnoredConstructionLock(5), Is.False);
        Assert.That(c.IsMassiveConstruction(100, 1, 0), Is.False);
        c.Apply(Msg(1, Ignore(1, 5L)));
        Assert.That(c.IsIgnoredConstructionLock(5), Is.True, "the ids are free again");
    }

    [Test]
    public void TheIgnoredListFeedsTheCubeLock()
    {
        // MyPC::FUN_1415302e0 (mypc.cpp:4459): in the owner's house, the owner on the list is not locked
        var c = new ConstructionEvents();
        c.Apply(Msg(0, Ignore(1, 42L)));
        var l = new CubeLock { IgnoredConstructionLock = c.IsIgnoredConstructionLock };
        Assert.That(l.IgnoredConstructionLock(42), Is.True);
        Assert.That(l.IgnoredConstructionLock(43), Is.False);
    }
}
