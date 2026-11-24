using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// CGameRule's meso revival flags over the GameEventMesoRevival events (gamerule.cpp AddMesoRevivalEvent, RemoveMesoRevivalEvent,
/// FUN_14047fc60, FUN_140477d10) as GameEventManagerCli adds (kinds 0 and 1), removes (2) and updates (3) them.
/// </summary>
public class MesoRevivalRuleTests
{
    static GameEventMessage Events(byte kind, params MesoRevivalEvent[] events) =>
        new(kind, Array.Empty<ShutdownInteractObjectEvent>(), Array.Empty<int>(), false) { MesoRevivalEvents = events };

    static GameEventMessage Removed(params int[] ids) => new(2, Array.Empty<ShutdownInteractObjectEvent>(), ids, false);

    [Test]
    public void NoEventIsNoRule()
    {
        var r = new MesoRevivalRule();
        Assert.That((r.Rule(false), r.Rule(true)), Is.EqualTo((0, 0)));
    }

    /// <summary>m_18 0 sets m_368, which holds for everyone; m_18 set sets m_36c, which holds only with bAll (the PC bang grade).</summary>
    [Test]
    public void TheFlagPicksEveryoneOrThePcBang()
    {
        var r = new MesoRevivalRule();
        r.Apply(Events(1, new MesoRevivalEvent(5, true)));
        Assert.That((r.Rule(false), r.Rule(true)), Is.EqualTo((0, 1)));
        r.Apply(Events(1, new MesoRevivalEvent(6, false)));
        Assert.That((r.Rule(false), r.Rule(true)), Is.EqualTo((1, 1)));
        r.Apply(Removed(6));
        Assert.That((r.Rule(false), r.Rule(true)), Is.EqualTo((0, 1)));
        r.Apply(Removed(5, 99));
        Assert.That((r.Rule(false), r.Rule(true)), Is.EqualTo((0, 0)));
    }

    /// <summary>Add skips an id already held (gameeventmanagercli.cpp:211-220); Update (:236-242) replaces it.</summary>
    [Test]
    public void AddKeepsTheHeldEventAndUpdateReplacesIt()
    {
        var r = new MesoRevivalRule();
        r.Apply(Events(0, new MesoRevivalEvent(5, true)));
        r.Apply(Events(1, new MesoRevivalEvent(5, false)));
        Assert.That(r.Rule(false), Is.EqualTo(0), "the second add of id 5 is skipped");
        r.Apply(Events(3, new MesoRevivalEvent(5, false)));
        Assert.That(r.Rule(false), Is.EqualTo(1));
        r.Apply(Events(3, new MesoRevivalEvent(5, true)));
        Assert.That((r.Rule(false), r.Rule(true)), Is.EqualTo((0, 1)));
    }

    /// <summary>GameEventManagerCli::Clear on a scene change removes every event.</summary>
    [Test]
    public void ClearRemovesEveryEvent()
    {
        var r = new MesoRevivalRule();
        r.Apply(Events(1, new MesoRevivalEvent(5, false), new MesoRevivalEvent(6, true)));
        r.Clear();
        Assert.That((r.Rule(false), r.Rule(true)), Is.EqualTo((0, 0)));
    }
}
