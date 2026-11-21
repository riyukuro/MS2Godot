using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class InteractObjectShutdownsTests
{
    static ShutdownInteractObjectEvent Ev(int id, params (uint, int)[] pairs) => new(id, pairs);
    static GameEventMessage Msg(byte kind, params ShutdownInteractObjectEvent[] e) => new(kind, e, Array.Empty<int>(), false);
    static GameEventMessage Removed(params int[] ids) => new(2, Array.Empty<ShutdownInteractObjectEvent>(), ids, false);

    [Test]
    public void PairCountedOncePerEvent()
    {
        // CShutdownEventInteractObject (shopeventmanager.cpp:254-292): AddShutdown counts up, RemoveShutdown counts down
        var s = new InteractObjectShutdowns();
        s.Apply(Msg(0, Ev(1, (100, 5), (100, 5)), Ev(2, (100, 5), (100, 6))));
        Assert.That(s.IsShutdown(100, 5) && s.IsShutdown(100, 6), Is.True);
        s.Apply(Removed(2));
        Assert.That(s.IsShutdown(100, 5), Is.True);    // event 1 still holds it (its duplicate pair counted once)
        Assert.That(s.IsShutdown(100, 6), Is.False);
        s.Apply(Removed(1));
        Assert.That(s.IsShutdown(100, 5), Is.False);
    }

    [Test]
    public void AddSkipsAnIdAlreadyHeld()
    {
        // GameEventManagerCli::Add (:211-220): an existing id is not added (nor started) again
        var s = new InteractObjectShutdowns();
        s.Apply(Msg(1, Ev(1, (100, 5))));
        s.Apply(Msg(1, Ev(1, (100, 9))));
        Assert.That(s.IsShutdown(100, 9), Is.False);
        s.Apply(Removed(1));
        Assert.That(s.IsShutdown(100, 5), Is.False);
    }

    [Test]
    public void UpdateStopsTheOldEvent()
    {
        // Update (:236-242) -> vfunc_7 (gameeventbase.cpp:202-207): old vfunc_6, then OnStart
        var s = new InteractObjectShutdowns();
        s.Apply(Msg(0, Ev(1, (100, 5))));
        s.Apply(Msg(3, Ev(1, (100, 6))));
        Assert.That(s.IsShutdown(100, 5), Is.False);
        Assert.That(s.IsShutdown(100, 6), Is.True);
        s.Clear();
        Assert.That(s.IsShutdown(100, 6), Is.False);
    }

    [Test]
    public void FieldIdCompareIsUnsigned()
    {
        var s = new InteractObjectShutdowns();
        s.Apply(Msg(0, Ev(1, (0x80000001u, 5))));
        Assert.That(s.IsShutdown(unchecked((int)0x80000001u), 5), Is.True);
    }
}
