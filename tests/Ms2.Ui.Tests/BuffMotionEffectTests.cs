using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>AdditionalCli's motion effects (additionalcli_1.cpp) through AdditionalBuffList: start, keep, restart, stop, invoke
/// and the overlap keep effects, on a recording effect service.</summary>
public class BuffMotionEffectTests
{
    sealed class Service : IBuffMotionEffects
    {
        public readonly List<string> Calls = new();
        public bool Shown = true, Fail;
        int next = 1;
        public int Keep(string effect, int ownerId, int casterId)
        {
            if (Fail) { Calls.Add($"keep {effect} failed"); return 0; }
            Calls.Add($"keep {effect} on {ownerId} by {casterId} -> {next}");
            return next++;
        }
        public void Insert(string effect, int ownerId, int casterId) => Calls.Add($"insert {effect} on {ownerId} by {casterId}");
        public void Stop(int id) => Calls.Add($"stop {id}");
        public bool OwnerShown(int ownerId) => Shown;
    }

    /// <summary>A service that records nothing, for the allocation guard.</summary>
    sealed class Quiet : IBuffMotionEffects
    {
        public bool Fail;
        int next = 1;
        public int Keep(string effect, int ownerId, int casterId) => Fail ? 0 : next++;
        public void Insert(string effect, int ownerId, int casterId) { }
        public void Stop(int id) { }
        public bool OwnerShown(int ownerId) => true;
    }

    static readonly AdditionalLevelData Plain = new() { Level = 1, StartEffect = "start.xml", KeepEffect = "keep.xml", StopEffect = "stop.xml", InvokeEffect = "invoke.xml", InvokeEvent = 1 };
    static readonly AdditionalLevelData Toggle = new() { Level = 1, KeepCondition = 99, StartEffect = "start.xml", KeepEffect = "keep.xml", StopEffect = "stop.xml", InvokeEffect = "invoke.xml" };
    static readonly AdditionalLevelData Stacks = new()
    {
        Level = 1, StopEffect = "stop.xml",
        OverlapKeep = new OverlapKeepEffects(
            new (uint, string)[] { (1, "s1.xml"), (3, "s3.xml") },
            new (uint, string)[] { (1, "k1.xml"), (3, "k3.xml") },
            new (uint, string)[] { (1, "e1.xml"), (3, "e3.xml") }),
    };

    static AdditionalLevelData Level(uint id, ushort level) => id switch { 1 => Plain, 2 => Toggle, 3 => Stacks, _ => null };

    static BuffPacket Add(int key, uint id, int count = 1, byte enabled = 1, int caster = 7) =>
        new(BuffPacket.Add, 5, key, caster, 0, new BuffState(0, 1000, id, 1, count, enabled), 0);
    static BuffPacket Modify(int key, uint id, int count, byte enabled, int caster = 7) =>
        new(BuffPacket.Modify, 5, key, caster, BuffPacket.ModifyState, new BuffState(0, 2000, id, 1, count, enabled), null);
    static BuffPacket Remove(int key, int caster) => new(BuffPacket.Remove, 5, key, caster, 0, null, null);

    static (AdditionalBuffList, Service) List()
    {
        var fx = new Service();
        return (new AdditionalBuffList(Level) { Effects = fx }, fx);
    }

    [Test]
    public void AddPlaysTheStartEffectThenTheUpdateKeepsTheKeepEffectOnce()
    {
        var (list, fx) = List();
        list.Apply(Add(10, 1));
        list.UpdateEffects();
        list.UpdateEffects();
        // Remove with the packet's caster: the keep effect stopped, the stop effect by that caster
        list.Apply(Remove(10, 9));
        Assert.That(fx.Calls, Is.EqualTo(new[]
        {
            "insert start.xml on 5 by 7", "keep keep.xml on 5 by 7 -> 1", "stop 1", "insert stop.xml on 5 by 9",
        }));
    }

    [Test]
    public void TheEntryListPlaysNoStartEffect()
    {
        var (list, fx) = List();
        list.Apply(Add(10, 1) with { FromList = true });
        list.UpdateEffects();
        Assert.That(fx.Calls, Is.EqualTo(new[] { "keep keep.xml on 5 by 7 -> 1" }));
    }

    [Test]
    public void AnOwnerNotShownGetsNoStartEffectAndAFailedKeepIsTriedEachFrame()
    {
        var (list, fx) = List();
        fx.Shown = false;
        fx.Fail = true;
        list.Apply(Add(10, 1));
        list.UpdateEffects();
        fx.Fail = false;
        list.UpdateEffects();
        list.UpdateEffects();
        Assert.That(fx.Calls, Is.EqualTo(new[] { "keep keep.xml failed", "keep keep.xml on 5 by 7 -> 1" }));
    }

    [Test]
    public void KeepCondition99FollowsTheEnabledFlag()
    {
        var (list, fx) = List();
        list.Apply(Add(10, 2, enabled: 0));   // disabled: no start effect, no keep
        list.UpdateEffects();
        list.PlayInvokeEffectsByEvent(0);       // only enabled effects
        list.PlayInvokeEffect(10);              // PlayInvokeEffect's own guard
        Assert.That(fx.Calls, Is.Empty);
        list.Apply(Modify(10, 2, 1, 1));       // enabled: RestartKeepEffect, then FUN_14096c4c0's start effect
        list.UpdateEffects();
        list.Apply(Modify(10, 2, 1, 0));       // disabled: StopEffects with its caster; no start effect
        list.UpdateEffects();
        Assert.That(fx.Calls, Is.EqualTo(new[]
        {
            "keep keep.xml on 5 by 7 -> 1", "insert start.xml on 5 by 7", "stop 1", "insert stop.xml on 5 by 7",
        }));
    }

    [Test]
    public void InvokeEffectsPlayOnTheirEvent()
    {
        var (list, fx) = List();
        list.Apply(Add(10, 1) with { FromList = true });
        list.Apply(Add(11, 2) with { FromList = true });   // invoke event 0
        list.PlayInvokeEffectsByEvent(1);
        list.PlayInvokeEffect(11);
        list.PlayInvokeEffect(12);    // no such key
        Assert.That(fx.Calls, Is.EqualTo(new[] { "insert invoke.xml on 5 by 7", "insert invoke.xml on 5 by 7" }));
    }

    [Test]
    public void OverlapKeepEffectsFollowTheCount()
    {
        var (list, fx) = List();
        list.Apply(Add(10, 3) with { FromList = true });
        list.UpdateEffects();                  // count 1: start 1, keep 1
        fx.Calls.Add("--");
        list.Apply(Modify(10, 3, 2, 1));       // count 2: no start row, no keep row of 2: the keep stays
        fx.Calls.Add("--");
        list.Apply(Modify(10, 3, 3, 1));       // count 3: start 3; keep 3 replaces keep 1 with the end of 2 (row 1)
        fx.Calls.Add("--");
        list.Apply(Remove(10, 7));             // RemoveKeepEffect: the end of the count (row 3), then the stop effect
        Assert.That(fx.Calls, Is.EqualTo(new[]
        {
            "insert s1.xml on 5 by 7", "keep k1.xml on 5 by 7 -> 1", "--",
            "--",
            "insert s3.xml on 5 by 7", "stop 1", "insert e1.xml on 5 by 7", "keep k3.xml on 5 by 7 -> 2", "--",
            "stop 2", "insert e3.xml on 5 by 7", "insert stop.xml on 5 by 7",
        }));
    }

    [Test]
    public void ClearAllRemovesTheKeepEffectsAndForgetEffectsDropsTheIds()
    {
        var (list, fx) = List();
        list.Apply(Add(10, 1) with { FromList = true });
        list.Apply(Add(11, 1) with { FromList = true });
        list.UpdateEffects();
        list.ForgetEffects();                   // the field went: kept anew, nothing stopped
        list.UpdateEffects();
        list.ClearAll();
        Assert.That(fx.Calls, Is.EqualTo(new[]
        {
            "keep keep.xml on 5 by 7 -> 1", "keep keep.xml on 5 by 7 -> 2",
            "keep keep.xml on 5 by 7 -> 3", "keep keep.xml on 5 by 7 -> 4",
            "stop 3", "stop 4",
        }));
    }

    [Test]
    public void OverlapRowsAreLookedUpAsTheClientMaps()
    {
        var rows = new (uint, string)[] { (1, "a"), (3, "b"), (6, "c") };
        Assert.That(OverlapKeepEffects.Exact(rows, 3), Is.EqualTo("b"));
        Assert.That(OverlapKeepEffects.Exact(rows, 4), Is.Null);
        // upper_bound(count + 1) on a std::greater map: the largest count not above it
        Assert.That(new uint[] { 0, 1, 2, 3, 5, 6, 99 }.Select(c => OverlapKeepEffects.Floor(rows, c)), Is.EqualTo(new string[] { null, "a", "a", "b", "b", "c", "c" }));
        var e = System.Xml.Linq.XElement.Parse("<OverlapKeepEffectProperty><start><overlap effect='s'/></start>"
            + "<keep><overlap count='3' effect='k3'/><overlap count='1' effect='k1'/><overlap count='3' effect='later'/></keep></OverlapKeepEffectProperty>");
        var o = OverlapKeepEffects.Load(e);
        Assert.That(o.Start, Is.EqualTo(new (uint, string)[] { (1, "s") }));
        Assert.That(o.Keep, Is.EqualTo(new (uint, string)[] { (1, "k1"), (3, "k3") }), "sorted, the first row of a count kept");
        Assert.That(o.End, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TheFrameUpdateDoesNotAllocate(bool failing)
    {
        var fx = new Quiet { Fail = failing };
        var list = new AdditionalBuffList(Level) { Effects = fx };
        for (int i = 0; i < 40; i++) list.Apply(Add(100 + i, (uint)(1 + i % 3)) with { FromList = true });
        for (int i = 0; i < 10; i++) list.UpdateEffects();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) list.UpdateEffects();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(bytes, Is.EqualTo(0), "bytes over 1000 updates of 40 effects");
    }
}
