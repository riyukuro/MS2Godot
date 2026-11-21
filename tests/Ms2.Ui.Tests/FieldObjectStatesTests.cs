using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class FieldObjectStatesTests
{
    [Test]
    public void BreakableMarkerTakesStateAndSyncValues()
    {
        // OnBreakable (scenestategame.cpp:3682-3686): SetState(state, 0), FUN_140adeaa0(a, b, c)
        var b = new BreakableObjects();
        b.AddMarker("m1");
        var set = new List<(string, int)>();
        b.StateSet = (n, s) => set.Add((n, s));
        b.Apply(new BreakableUpdate(1, new[] { new BreakableEntry("m1", 3, 1, 20, 30) }));
        var m = b.Find("m1");
        Assert.That((m.State, m.SyncA, m.SyncB, m.SyncC), Is.EqualTo((3, 1, 20, 30)));
        Assert.That(set, Is.EqualTo(new[] { ("m1", 3) }));
        Assert.That(b.CubeStates, Is.Empty);
    }

    [Test]
    public void BreakableWithoutAMarkerGoesToTheCubeStatesAndTheLastWins()
    {
        // :3688-3692 -> CubeMapInformationService::FUN_1410caa60 (cubemapinformationservice_3.cpp:1298-1303)
        var b = new BreakableObjects();
        b.Apply(new BreakableUpdate(0, new[] { new BreakableEntry("c", 2, 0, 0, 0), new BreakableEntry("c", 4, 0, 0, 0) }));
        Assert.That(b.CubeStates["c"], Is.EqualTo(4));
        Assert.That(b.Find("c"), Is.Null);
    }

    [Test]
    public void BreakableLookupLowercasesTheNameAndTheCubeListKeepsIt()
    {
        // FindMarkerByName (worldservice_1.cpp:4321-4327) lowercases its copy; the cube list takes strName as read (:3691)
        var b = new BreakableObjects();
        b.AddMarker("abc");
        b.Apply(new BreakableUpdate(0, new[] { new BreakableEntry("ABC", 3, 0, 0, 0), new BreakableEntry("Cube", 1, 0, 0, 0) }));
        Assert.That(b.Find("abc").State, Is.EqualTo(3));
        Assert.That(b.CubeStates.Keys, Is.EqualTo(new[] { "Cube" }));
    }

    static FieldMap Field(string xblock)
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new Ms2.Formats.Pack.PackSet(d);
        return FieldMap.Load(packs, new Ms2.Formats.World.FlatLibrary(packs), xblock);
    }

    [Test]
    public void TheFieldsBreakablesBecomeMarkersUnderTheirLowercasedIdsAtStateTwo()
    {
        // worldservice_1.cpp:6757-6768: every entity of model "MS2Breakable" (0xd3b04f05) through vfunc_1(entity, 2)
        var map = Field("02000007_bf");
        var want = map.Entities.Where(e => e.HasMixin("MS2Breakable")).ToList();
        Assert.That(want, Is.Not.Empty);
        var b = new BreakableObjects();
        Assert.That(b.AttachField(map.Entities), Is.EqualTo(want.Count));
        Assert.That(b.Count, Is.EqualTo(want.Select(e => e.Raw.Id.ToLowerInvariant()).Distinct().Count()));
        foreach (var e in want)
        {
            var m = b.Find(e.Raw.Id);
            Assert.That(m, Is.Not.Null);
            Assert.That((m.Name, m.State, m.Shown), Is.EqualTo((e.Raw.Id.ToLowerInvariant(), 2, true)));
            Assert.That(m.Entity, Is.SameAs(map.Entities.Last(x => string.Equals(x.Raw?.Id, e.Raw.Id, StringComparison.OrdinalIgnoreCase) && x.HasMixin("MS2Breakable"))));
            // FUN_140adf9d0 (breakableobject.cpp:586-590): m_bHittable is "Enabled", false with a zero size
            var size = e.Vector("ShapeDimensions");
            Assert.That(m.Hittable, Is.EqualTo(e.Bool("Enabled") && (size.X != 0 || size.Y != 0 || size.Z != 0)));
            Assert.That(m.Position, Is.EqualTo(e.Position));
        }
        Assert.That(b.Find(map.Entities.First(e => !e.HasMixin("MS2Breakable") && e.Raw?.Id != null).Raw.Id), Is.Null);
    }

    [Test]
    public void ABreakableAtStateFourIsHiddenAndShownAgainByARegen()
    {
        // SetState (breakableobject.cpp:270-343): state 4 -> FUN_140ae07d0(0) (the fade out); 5 -> FUN_140ae07d0(1); 1 leaves it
        var map = Field("02000007_bf");
        var b = new BreakableObjects();
        b.AttachField(map.Entities);
        string name = map.Entities.First(e => e.HasMixin("MS2Breakable")).Raw.Id;
        var set = new List<(string, int)>();
        b.StateSet = (n, s) => set.Add((n, s));
        b.Apply(new BreakableUpdate(1, new[] { new BreakableEntry(name.ToUpperInvariant(), 4, 0, 0, 0) }));
        Assert.That(b.Find(name).Shown, Is.False);
        Assert.That(b.CubeStates, Is.Empty);
        b.Apply(new BreakableUpdate(1, new[] { new BreakableEntry(name, 1, 0, 0, 0) }));
        Assert.That(b.Find(name).Shown, Is.False);
        b.Apply(new BreakableUpdate(1, new[] { new BreakableEntry(name, 5, 0, 0, 0) }));
        Assert.That(b.Find(name).Shown, Is.True);
        Assert.That(set.Select(x => x.Item2), Is.EqualTo(new[] { 4, 1, 5 }));
        Assert.That(set.Select(x => x.Item1).Distinct(), Is.EqualTo(new[] { name.ToLowerInvariant() }));
    }

    [Test]
    public void UpdatesAfterAFieldEnterWaitForTheFieldsMarkers()
    {
        // the client reads OnBreakable after its field load (worldservice_1.cpp:6757-6768) made the markers; a held update
        // that reached the cube list instead would lose the marker's state
        var map = Field("02000007_bf");
        var b = new BreakableObjects();
        b.ClearMarkers();
        string name = map.Entities.First(e => e.HasMixin("MS2Breakable")).Raw.Id;
        b.Apply(new BreakableUpdate(1, new[] { new BreakableEntry(name, 4, 0, 0, 0) }));
        Assert.That(b.CubeStates, Is.Empty);
        b.AttachField(map.Entities);
        Assert.That((b.Find(name).State, b.Find(name).Shown, b.Holding), Is.EqualTo((4, false, false)));
        Assert.That(b.CubeStates, Is.Empty);
    }

    static LiftableUpdate List(params LiftableEntry[] e) => new(LiftableUpdate.ModeList, e);

    [Test]
    public void TheFieldsLiftsAreAttachedWithTheirKindRangeAndPosition()
    {
        // worldservice_1.cpp:6771-6786: every entity of model "MS2Liftable" (0x32767dd), kind = "ItemStackCount" (0x3773e9e0);
        // BindEntity (liftableobject.cpp:180-210): the lowercased DataFileID, range "ActionDistance" (0xf39fa2a1) else 150,
        // position, item id "ItemID" (0x2e687f77); the ctor's m_bC2 is true (:121)
        var map = Field("02000041_bf");
        var want = map.Entities.Where(e => e.HasMixin("MS2Liftable")).ToList();
        Assert.That(want, Is.Not.Empty);
        var l = new LiftableObjects();
        Assert.That(l.AttachField(map.Entities), Is.EqualTo(want.Count));
        Assert.That(l.Count, Is.EqualTo(want.Select(e => e.Raw.Id.ToLowerInvariant()).Distinct().Count()));
        foreach (var e in want)
        {
            var lift = l.Find(e.Raw.Id.ToLowerInvariant());
            Assert.That(lift, Is.Not.Null);
            Assert.That((lift.Kind, lift.Enabled, lift.State, lift.Flag, lift.HasEntity),
                Is.EqualTo(((int)e.Number("ItemStackCount"), false, 0, true, true)));
            Assert.That(lift.Range, Is.EqualTo(e.Has("ActionDistance") ? (float)e.Number("ActionDistance") : 150f));
            Assert.That(lift.Range, Is.GreaterThan(0f));
            Assert.That(lift.Position, Is.EqualTo(e.Position));
            Assert.That(lift.ItemId, Is.EqualTo((int)e.Number("ItemID")));
            Assert.That(lift.ItemId, Is.Not.Zero);
        }
        Assert.That(l.Find(map.Entities.First(e => !e.HasMixin("MS2Liftable") && e.Raw?.Id != null).Raw.Id.ToLowerInvariant()), Is.Null);
    }

    [Test]
    public void LiftUpdatesAfterAFieldEnterWaitForTheFieldsLifts()
    {
        // OnLiftable's list (scenestategame.cpp:11941-11991) runs on the lifts the field load made; read before them it would
        // set nothing and the attached lifts would stay disabled
        var map = Field("02000041_bf");
        var l = new LiftableObjects();
        l.FieldLeft();
        string name = map.Entities.First(e => e.HasMixin("MS2Liftable")).Raw.Id.ToLowerInvariant();
        l.Apply(List(new LiftableEntry(name, true, 3, 0)));
        Assert.That((l.Holding, l.Count), Is.EqualTo((true, 0)));
        l.AttachField(map.Entities);
        var lift = l.Find(name);
        Assert.That((l.Holding, lift.Enabled, lift.Kind, lift.State), Is.EqualTo((false, true, 3, 0)));
        Assert.That(l.Count, Is.EqualTo(1), "the list removes the field lifts it does not name");
    }

    [Test]
    public void FieldLiftIsDisabledAtStateZeroUnderItsLowercasedId()
    {
        // Init(entity, false, kind) (liftableobject.cpp:167-175), BindEntity's ToLower (:186-191); SetState(0) runs before
        // BindEntity, with no entity yet, so it does not SetActive (:295)
        var l = new LiftableObjects();
        var lift = l.AddFieldLift("ABCdef", 7);
        Assert.That((lift.Name, lift.Kind, lift.Enabled, lift.State, lift.Active), Is.EqualTo(("abcdef", 7, false, 0, false)));
    }

    [Test]
    public void ListSetsTheNamedLiftsKindFromTheInt32AndStateFromTheByte()
    {
        // :11964-11971: SetKind(i32), SetLiftEnabled, FUN_141447aa0, SetState(u8); state 1 SetActive(0)
        var l = new LiftableObjects();
        l.AddFieldLift("a", 0);
        l.Apply(List(new LiftableEntry("a", true, 900, 1, "q1", "q2", "q3", "q4", true)));
        var a = l.Find("a");
        Assert.That((a.Kind, a.State, a.Enabled, a.Flag, a.Active), Is.EqualTo((900, 1, true, true, false)));
        Assert.That((a.QuestA, a.QuestB, a.QuestC, a.QuestD), Is.EqualTo(("q1", "q2", "q3", "q4")));
    }

    [Test]
    public void ListRemovesTheFieldLiftsItDoesNotName()
    {
        // :11973-11991: a field lift entity whose name is not in the list is removed; a lift without an entity is not
        var l = new LiftableObjects();
        l.AddFieldLift("keep", 0);
        l.AddFieldLift("drop", 0);
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeAdd, new[] { new LiftableEntry("loose", false, 2, 0) }));
        var removed = new List<string>();
        l.Removed = removed.Add;
        l.Apply(List(new LiftableEntry("keep", true, 1, 0)));
        Assert.That(removed, Is.EqualTo(new[] { "drop" }));
        Assert.That(l.Find("drop"), Is.Null);
        Assert.That(l.Find("keep"), Is.Not.Null);
        Assert.That(l.Find("loose"), Is.Not.Null);
    }

    [Test]
    public void UpdateLeavesQuestStringsAndFlag()
    {
        // case 2 (:11999-12001): SetKind, SetLiftEnabled, SetState only
        var l = new LiftableObjects();
        l.AddFieldLift("a", 0);
        l.Apply(List(new LiftableEntry("a", false, 1, 0, "q1", "q2", "q3", "q4", true)));
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeUpdate, new[] { new LiftableEntry("a", true, 5, 2) }));
        var a = l.Find("a");
        Assert.That((a.Kind, a.Enabled, a.State, a.Flag, a.QuestA), Is.EqualTo((5, true, 2, true, "q1")));
    }

    [Test]
    public void AddedLiftHasNoEntityAndReplacesTheName()
    {
        // FUN_141bc5420 (worldservice_1.cpp:4429-4447): Init(0, true, kind), SetAt(name)
        var l = new LiftableObjects();
        l.AddFieldLift("n", 1);
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeAdd, new[] { new LiftableEntry("n", false, 6, 0, "a", "b", "c", "d", true) }));
        var n = l.Find("n");
        Assert.That((n.HasEntity, n.Kind, n.Enabled, n.State, n.Flag, n.QuestD), Is.EqualTo((false, 6, false, 0, true, "d")));
        Assert.That(l.Count, Is.EqualTo(1));
    }

    [Test]
    public void LeavingTheFieldDropsTheFieldLiftsOnly()
    {
        // the entity removal handler drops its lift (worldservice_1.cpp:3526-3533); a mode 3 lift has no entity
        var l = new LiftableObjects();
        l.AddFieldLift("f", 0);
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeAdd, new[] { new LiftableEntry("x", false, 0, 0) }));
        l.FieldLeft();
        Assert.That(l.Find("f"), Is.Null);
        Assert.That(l.Find("x"), Is.Not.Null);
    }

    [Test]
    public void RemoveTakesAFieldLiftOnly()
    {
        // case 4 -> DestroyLiftByName (worldservice_1.cpp:4451-4456); the list entry goes with the entity (:3526-3533)
        var l = new LiftableObjects();
        l.AddFieldLift("f", 0);
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeAdd, new[] { new LiftableEntry("x", false, 0, 0) }));
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeRemove, new LiftableEntry[0], "f"));
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeRemove, new LiftableEntry[0], "x"));
        Assert.That(l.Find("f"), Is.Null);
        Assert.That(l.Find("x"), Is.Not.Null);
    }

    static LiftableObjects.Lift Liftable(LiftableObjects l, string name, double x, float range = 150f)
    {
        var lift = l.AddFieldLift(name, 1);
        lift.Position = new Vec3(x, 0, 0);
        lift.Range = range;
        lift.Enabled = true;
        return lift;
    }

    [Test]
    public void CanLiftNeedsEnabledAKindAndStateZero()
    {
        // CanLift (liftableobject.cpp:155-164): m_bC0, m_nKind > 0, m_nState == 0
        var l = new LiftableObjects();
        var a = Liftable(l, "a", 0);
        Assert.That(a.CanLift, Is.True);
        a.Enabled = false;
        Assert.That(a.CanLift, Is.False, "disabled (the field load leaves m_bC0 at 0)");
        a.Enabled = true; a.Kind = 0;
        Assert.That(a.CanLift, Is.False, "kind 0");
        a.Kind = 1; a.SetState(1);
        Assert.That(a.CanLift, Is.False, "state 1");
        Assert.That(l.Poll(new Vec3(0, 0, 0)), Is.False, "a lift that can't be lifted is not found");
    }

    [Test]
    public void ThePollFindsTheFirstLiftByNameWithinItsRange()
    {
        // FUN_140957890: the lifts in m_510's name order, the first that CanLift and whose 3D distance <= its range
        var l = new LiftableObjects();
        Liftable(l, "c", 100);
        Liftable(l, "b", 100, range: 50);
        Liftable(l, "a", 0, range: 10);
        Assert.That(l.Poll(new Vec3(100, 0, 0)), Is.True);
        Assert.That(l.Polled, Is.EqualTo("b"), "a is out of its range of 10; b comes before c");
        Assert.That(l.Poll(new Vec3(100, 50, 0)), Is.True);
        Assert.That(l.Polled, Is.EqualTo("b"), "exactly at the range is in reach");
        Assert.That(l.Poll(new Vec3(100, 30, 40.5)), Is.True);
        Assert.That(l.Polled, Is.EqualTo("c"), "the distance is 3D: b is past 50, c within 150");
        Assert.That(l.Poll(new Vec3(1000, 0, 0)), Is.False);
        Assert.That(l.Polled, Is.EqualTo(""));
        Assert.That(l.Poll(new Vec3(0, 0, 0)), Is.True);
        Assert.That(l.Polled, Is.EqualTo("a"));
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeAdd, new[] { new LiftableEntry("0", true, 1, 0) }));
        l.Find("0").Enabled = true;
        Assert.That(l.Poll(new Vec3(0, 0, 0)), Is.True);
        Assert.That(l.Polled, Is.EqualTo("0"), "a lift added later takes its place in the name order");
    }

    [Test]
    public void ThePollMovesTheHighlightAndOnlyOnLiftsWithAnEntity()
    {
        // vfunc_1 (actionkeyobject.cpp:1279-1294): the old lift loses the highlight when another or none is found;
        // SetHighlight does nothing without an entity (liftableobject.cpp:350-351)
        var l = new LiftableObjects();
        var a = Liftable(l, "a", 0);
        var b = Liftable(l, "b", 500);
        l.Poll(new Vec3(0, 0, 0));
        Assert.That((a.Highlighted, b.Highlighted), Is.EqualTo((true, false)));
        l.Poll(new Vec3(0, 0, 0));
        Assert.That(a.Highlighted, Is.True, "the same lift keeps it");
        l.Poll(new Vec3(500, 0, 0));
        Assert.That((a.Highlighted, b.Highlighted), Is.EqualTo((false, true)));
        l.Poll(new Vec3(5000, 0, 0));
        Assert.That(b.Highlighted, Is.False, "none in reach: the old one loses it");
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeAdd, new[] { new LiftableEntry("n", true, 1, 0) }));
        var n = l.Find("n"); n.Enabled = true; n.Position = new Vec3(5000, 0, 0);
        Assert.That(l.Poll(new Vec3(5000, 0, 0)), Is.True);
        Assert.That(n.Highlighted, Is.False, "an added lift has no entity");
    }

    [Test]
    public void TheKeySendsLiftableForThePolledLiftAndNothingWhileRidingOrWithoutOne()
    {
        // vfunc_2 (actionkeyobject.cpp:1297-1315): FindLiftByName(m_str18); riding first needs the ride off (not ported)
        var l = new LiftableObjects();
        var a = Liftable(l, "a", 0);
        Assert.That(l.Use(false, out bool open), Is.Null, "nothing polled");
        Assert.That(open, Is.False);
        l.Poll(new Vec3(0, 0, 0));
        Assert.That(l.Use(true, out open), Is.Null, "riding: the ride off is STILL-OPEN, nothing is sent");
        Assert.That((open, a.Highlighted), Is.EqualTo((true, true)));
        var req = l.Use(false, out open);
        Assert.That(req, Is.EqualTo(new LiftableRequest("a")));
        Assert.That((open, a.Highlighted), Is.EqualTo((false, false)), "SetHighlight(0) before the send");
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeRemove, new LiftableEntry[0], "a"));
        Assert.That(l.Use(false, out open), Is.Null, "the polled lift is gone");
    }

    [Test]
    public void ThePollAllocatesNothing()
    {
        var l = new LiftableObjects();
        for (int i = 0; i < 20; i++) Liftable(l, "l" + i, i * 400);
        var at = new Vec3(4000, 0, 0);
        long bytes = 0;
        for (int round = 0; round < 2; round++)          // measured on the second round: the first pays the JIT tiering
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) { l.Poll(at); l.Poll(new Vec3(0, 0, 0)); }
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.That(bytes, Is.EqualTo(0));
    }
    static RequestFieldEnter FieldEnterPacket(int map, byte error = 0) => new(error, map, 0, 0, 0, 0, default, default, 0);

    static FieldEntity LiftEntity(string id)
    {
        var raw = new XEntity { Id = id, ModelName = "MS2Liftable", Name = id, Props = { ["ItemStackCount"] = "1" } };
        return new FieldEntity { Raw = raw, Flat = new FlatModel { Name = "MS2Liftable" } };
    }

    /// <summary>A field entered and loaded: its lift attached, a breakable marker, no hold.</summary>
    static (Ms2.Net.FieldEntry, BreakableObjects, LiftableObjects) LoadedField(int map)
    {
        var entry = new Ms2.Net.FieldEntry();
        var b = new BreakableObjects();
        var l = new LiftableObjects();
        Assert.That(FieldEnter.Take(entry, FieldEnterPacket(map), false, b, l), Is.EqualTo(FieldEnterResult.NewField));
        b.AttachField(new List<FieldEntity>());
        b.AddMarker("m");
        l.AttachField(new List<FieldEntity> { LiftEntity("lift1") });
        Assert.That((b.Holding, l.Holding, l.Count), Is.EqualTo((false, false, 1)));
        return (entry, b, l);
    }

    [Test]
    public void TheLoginsFieldEnterHoldsTheUpdatesForItsEntities()
    {
        // the login's RequestFieldEnter takes the same OnRequestFieldEnter path (scenestategame.cpp:2335): the field load
        // attaches its breakables and lifts, so the updates before it wait
        var b = new BreakableObjects();
        var l = new LiftableObjects();
        Assert.That(FieldEnter.Take(new Ms2.Net.FieldEntry(), FieldEnterPacket(2000001), false, b, l), Is.EqualTo(FieldEnterResult.NewField));
        Assert.That((b.Holding, l.Holding), Is.EqualTo((true, true)));
    }

    [Test]
    public void ARefusedFieldEnterKeepsTheFieldsBreakablesAndLifts()
    {
        // a non-zero error only shows the message box; LoadMap does not run, so the field and its objects stay
        var (entry, b, l) = LoadedField(2000001);
        Assert.That(FieldEnter.Take(entry, FieldEnterPacket(2000062, 5), false, b, l), Is.EqualTo(FieldEnterResult.Refused));
        Assert.That(entry.Current.MapId, Is.EqualTo(2000001));
        Assert.That((b.Holding, b.Find("m") != null, l.Holding, l.Find("lift1") != null), Is.EqualTo((false, true, false, true)));
        // the next update applies to the field's lift at once
        l.Apply(List(new LiftableEntry("lift1", true, 3, 0)));
        Assert.That((l.Find("lift1").Enabled, l.Find("lift1").Kind), Is.EqualTo((true, 3)));
    }

    [Test]
    public void TheLoadingMapEnteredAgainKeepsItsObjectsAndANewMapResetsThem()
    {
        // LoadMap returns at once for the map that is still loading (worldservice_1.cpp:3295); another map tears the field
        // down: markers and entity lifts go (:3526-3533), and the updates wait for the new field
        var (entry, b, l) = LoadedField(2000001);
        Assert.That(FieldEnter.Take(entry, FieldEnterPacket(2000001), true, b, l), Is.EqualTo(FieldEnterResult.LoadingAlready));
        Assert.That((b.Holding, b.Find("m") != null, l.Holding, l.Count), Is.EqualTo((false, true, false, 1)));
        Assert.That(FieldEnter.Take(entry, FieldEnterPacket(2000062), true, b, l), Is.EqualTo(FieldEnterResult.NewField));
        Assert.That((b.Holding, b.Find("m") != null, l.Holding, l.Count), Is.EqualTo((true, false, true, 0)));
        // the same map entered again once its load is done is a new field load
        b.AttachField(new List<FieldEntity>());
        l.AttachField(new List<FieldEntity> { LiftEntity("lift1") });
        Assert.That(FieldEnter.Take(entry, FieldEnterPacket(2000062), false, b, l), Is.EqualTo(FieldEnterResult.NewField));
        Assert.That((b.Holding, l.Holding, l.Count), Is.EqualTo((true, true, 0)));
    }
}
