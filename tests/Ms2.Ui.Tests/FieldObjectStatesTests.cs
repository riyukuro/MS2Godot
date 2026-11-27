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
        // Init(entity, false, kind) (liftableobject.cpp:167-175): m_bC1 = 1 (:171), BindEntity's ToLower (:186-191); SetState(0)
        // runs before BindEntity, with no entity yet, so it does not SetActive (:295)
        var l = new LiftableObjects();
        var lift = l.AddFieldLift("ABCdef", 7);
        Assert.That((lift.Name, lift.Kind, lift.Enabled, lift.State, lift.Active), Is.EqualTo(("abcdef", 7, false, 0, true)));
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
    public void AStateChangeOfAFieldLiftSetsItsActiveFlagAndSignalsIt()
    {
        // SetState (liftableobject.cpp:292-310): with an entity, states 0 and 2 SetActive(1), state 1 SetActive(0), others
        // nothing; a lift without an entity (mode 3) never
        var l = new LiftableObjects();
        l.AddFieldLift("a", 1);
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeAdd, new[] { new LiftableEntry("4_655105", false, 1, 0) }));
        var seen = new List<(string, bool)>();
        l.ActiveSet = (n, on) => seen.Add((n, on));
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeUpdate, new[] { new LiftableEntry("a", true, 1, 1) }));
        Assert.That(l.Find("a").Active, Is.False, "lifted");
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeUpdate, new[] { new LiftableEntry("a", true, 1, 3) }));
        Assert.That(l.Find("a").Active, Is.False, "state 3 leaves it");
        l.Apply(List(new LiftableEntry("a", true, 1, 0), new LiftableEntry("4_655105", false, 1, 1)));
        Assert.That(l.Find("a").Active, Is.True, "back");
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeUpdate, new[] { new LiftableEntry("4_655105", false, 1, 2) }));
        Assert.That(l.Find("4_655105").Active, Is.True, "no entity: Init's flag stays");
        Assert.That(seen, Is.EqualTo(new[] { ("a", false), ("a", true) }));
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
        Assert.That(a.CanLift, Is.False, "disabled (the field load leaves the lift disabled)");
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
    public void TheKeySendsLiftableForThePolledLiftAfterTheRideOffAndNothingWithoutOne()
    {
        // vfunc_2 (actionkeyobject.cpp:1297-1315): FindLiftByName(m_str18); riding, SendRequestRideMode1(0) must return 1
        var l = new LiftableObjects();
        var a = Liftable(l, "a", 0);
        int asked = 0;
        Assert.That(l.Use(() => { asked++; return true; }), Is.Null, "nothing polled");
        Assert.That(asked, Is.Zero, "the lift is found before the ride off");
        l.Poll(new Vec3(0, 0, 0));
        Assert.That(l.Use(() => { asked++; return false; }), Is.Null, "a refused ride off (a battle mount) sends nothing");
        Assert.That((asked, a.Highlighted), Is.EqualTo((1, true)));
        Assert.That(l.Use(() => { asked++; return true; }), Is.EqualTo(new LiftableRequest("a")), "the ride off sent, then the lift");
        Assert.That((asked, a.Highlighted), Is.EqualTo((2, false)), "SetHighlight(0) before the send");
        l.Poll(new Vec3(0, 0, 0));
        Assert.That(l.Use(null), Is.EqualTo(new LiftableRequest("a")), "on foot");
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeRemove, new LiftableEntry[0], "a"));
        Assert.That(l.Use(null), Is.Null, "the polled lift is gone");
    }

    [Test]
    public void ThePollAllocatesNothing()
    {
        var l = new LiftableObjects();
        for (int i = 0; i < 20; i++) Liftable(l, "l" + i, i * 400);
        var at = new Vec3(4000, 0, 0);
        long bytes = Alloc.Least(() => { for (int i = 0; i < 100; i++) { l.Poll(at); l.Poll(new Vec3(0, 0, 0)); } });
        Assert.That(bytes, Is.EqualTo(0));
    }
    static RequestFieldEnter FieldEnterPacket(int map, byte error = 0) => new(error, map, 0, 0, 0, 0, default, default, 0);

    static FieldEntity LiftEntity(string id)
    {
        var raw = new XEntity { Id = id, ModelName = "MS2Liftable", Name = id, Props = { ["ItemStackCount"] = "1" } };
        return new FieldEntity { Raw = raw, Flat = new FlatModel { Name = "MS2Liftable" } };
    }

    /// <summary>A field entered and loaded: its lift attached, a breakable marker, no hold.</summary>
    static (Ms2.Net.FieldEntry, BreakableObjects, LiftableObjects, LiftableTargetBoxes) LoadedField(int map)
    {
        var entry = new Ms2.Net.FieldEntry();
        var b = new BreakableObjects();
        var l = new LiftableObjects();
        var t = new LiftableTargetBoxes();
        Assert.That(FieldEnter.Take(entry, FieldEnterPacket(map), b, l, t), Is.EqualTo(FieldEnterResult.NewField));
        entry.FieldLoaded(_ => { });
        b.AttachField(new List<FieldEntity>());
        b.AddMarker("m");
        l.AttachField(new List<FieldEntity> { LiftEntity("lift1") });
        t.AttachField(new List<FieldEntity> { TargetBox("box1", "1") });
        Assert.That((b.Holding, l.Holding, l.Count, t.Count), Is.EqualTo((false, false, 1, 1)));
        return (entry, b, l, t);
    }

    [Test]
    public void TheLoginsFieldEnterHoldsTheUpdatesForItsEntities()
    {
        // the login's RequestFieldEnter takes the same OnRequestFieldEnter path (scenestategame.cpp:2335): the field load
        // attaches its breakables and lifts, so the updates before it wait
        var b = new BreakableObjects();
        var l = new LiftableObjects();
        Assert.That(FieldEnter.Take(new Ms2.Net.FieldEntry(), FieldEnterPacket(2000001), b, l, new LiftableTargetBoxes()), Is.EqualTo(FieldEnterResult.NewField));
        Assert.That((b.Holding, l.Holding), Is.EqualTo((true, true)));
    }

    [Test]
    public void ARefusedFieldEnterKeepsTheFieldsBreakablesAndLifts()
    {
        // a non-zero error only shows the message box; LoadMap does not run, so the field and its objects stay
        var (entry, b, l, t) = LoadedField(2000001);
        Assert.That(FieldEnter.Take(entry, FieldEnterPacket(2000062, 5), b, l, t), Is.EqualTo(FieldEnterResult.Refused));
        Assert.That(entry.Current.MapId, Is.EqualTo(2000001));
        Assert.That((b.Holding, b.Find("m") != null, l.Holding, l.Find("lift1") != null, t.Count), Is.EqualTo((false, true, false, true, 1)));
        // the next update applies to the field's lift at once
        l.Apply(List(new LiftableEntry("lift1", true, 3, 0)));
        Assert.That((l.Find("lift1").Enabled, l.Find("lift1").Kind), Is.EqualTo((true, 3)));
    }

    [Test]
    public void TheLoadingMapEnteredAgainKeepsItsObjectsAndANewMapResetsThem()
    {
        // LoadMap returns at once for the map that is still loading (worldservice_1.cpp:3296-3297); another map tears the
        // field down: markers and entity lifts go (:3526-3533), and the updates wait for the new field
        var (entry, b, l, t) = LoadedField(2000001);
        Assert.That(FieldEnter.Take(entry, FieldEnterPacket(2000062), b, l, t), Is.EqualTo(FieldEnterResult.NewField));
        b.AttachField(new List<FieldEntity>());
        b.AddMarker("m");
        l.AttachField(new List<FieldEntity> { LiftEntity("lift1") });
        Assert.That((b.Holding, b.Find("m") != null, l.Holding, l.Count), Is.EqualTo((false, true, false, 1)));
        // 2000062 still loading: entered again, its objects stay
        Assert.That(FieldEnter.Take(entry, FieldEnterPacket(2000062), b, l, t), Is.EqualTo(FieldEnterResult.LoadingAlready));
        Assert.That((b.Holding, b.Find("m") != null, l.Holding, l.Count), Is.EqualTo((false, true, false, 1)));
        // another map while loading tears it down
        Assert.That(FieldEnter.Take(entry, FieldEnterPacket(2000001), b, l, t), Is.EqualTo(FieldEnterResult.NewField));
        Assert.That((b.Holding, b.Find("m") != null, l.Holding, l.Count, t.Count), Is.EqualTo((true, false, true, 0, 0)));
        // the same map entered again once its load is done is a new field load
        entry.FieldLoaded(_ => { });
        b.AttachField(new List<FieldEntity>());
        l.AttachField(new List<FieldEntity> { LiftEntity("lift1") });
        Assert.That(FieldEnter.Take(entry, FieldEnterPacket(2000001), b, l, t), Is.EqualTo(FieldEnterResult.NewField));
        Assert.That((b.Holding, l.Holding, l.Count), Is.EqualTo((true, true, 0)));
    }

    [Test]
    public void AGameToGameDuringTheLoadMakesTheNextEntryOfThatMapAFreshLoad()
    {
        // the GameToGame teardown (FUN_141bbc580) sets the world state to 6 (worldservice_1.cpp:9975): the next server's
        // entry for the map that was loading passes LoadMap's check and tears the field down; the old load sends nothing
        var (entry, b, l, t) = LoadedField(2000001);
        Assert.That(FieldEnter.Take(entry, FieldEnterPacket(2000062), b, l, t), Is.EqualTo(FieldEnterResult.NewField));
        b.AttachField(new List<FieldEntity>());
        b.AddMarker("m");
        l.AttachField(new List<FieldEntity> { LiftEntity("lift1") });
        entry.TornDown();
        Assert.That((entry.Loading, entry.Loaded), Is.EqualTo((false, false)));
        int sent = 0;
        Assert.That(entry.FieldLoaded(_ => sent++), Is.False, "the torn-down load has no field-loaded sends");
        Assert.That(sent, Is.EqualTo(0));
        Assert.That(FieldEnter.Take(entry, FieldEnterPacket(2000062), b, l, t), Is.EqualTo(FieldEnterResult.NewField));
        Assert.That((entry.LoadingAlready, entry.Loading, entry.Answered), Is.EqualTo((false, true, false)));
        Assert.That((b.Holding, b.Find("m") != null, l.Holding, l.Count), Is.EqualTo((true, false, true, 0)));
        Assert.That(entry.FieldLoaded(_ => sent++), Is.True);
        Assert.That(sent, Is.EqualTo(2));
    }

    /// <summary>A moving NIF breakable with the props of the gratings in 52000120: 150 up in 1 s, a 1 s start wait, a 5 s
    /// end wait, round type 2.</summary>
    internal static FieldEntity Platform(string id, string round = "2", string dir = "0, 0, 1", string dist = "150",
        string dur = "1", string waitS = "1", string waitE = "5", string offset = null)
    {
        var raw = new XEntity { Id = id, ModelName = "MS2BreakableNIF", Name = id };
        raw.Props["Position"] = "100, 200, 300";
        raw.Props["Rotation"] = "0, 0, 270";
        raw.Props["moveDirection"] = dir;
        raw.Props["moveDistance"] = dist;
        raw.Props["moveDuration"] = dur;
        raw.Props["moveStopDurationS"] = waitS;
        raw.Props["moveStopDurationE"] = waitE;
        raw.Props["moveRoundType"] = round;
        if (offset != null) raw.Props["moveTimeOffset"] = offset;
        var flat = new FlatModel { Name = "MS2BreakableNIF" };
        flat.Mixins.Add("MS2Breakable");
        return new FieldEntity { Raw = raw, Flat = flat };
    }

    static (BreakableObjects, BreakableObjects.Marker) OnePlatform(FieldEntity e)
    {
        var b = new BreakableObjects();
        b.AttachField(new[] { e });
        return (b, b.Find(e.Raw.Id));
    }

    [Test]
    public void APlatformRunsTheStartWaitTheWayUpTheEndWaitAndTheWayBack()
    {
        // CBreakableObject::Update (breakableobject.cpp:376-423): wait0 1000, go 2000, wait1 7000, back 8000 = the cycle
        var (b, m) = OnePlatform(Platform("p"));
        Assert.That(b.Moving, Is.EqualTo(new[] { m }));
        var seen = new List<(int t, double z, double rz, int phase)>();
        foreach (int t in new[] { 0, 500, 1000, 1500, 2000, 3000, 7000, 7500, 8000, 9500 })
        {
            b.Update(t, t);
            seen.Add((t, m.Position.Z, m.Rotation.Z, m.Phase));
        }
        Assert.That(seen, Is.EqualTo(new[]
        {
            (0, 300.0, 270.0, 1), (500, 300.0, 270.0, 1), (1000, 300.0, 270.0, 1), (1500, 375.0, 270.0, 2),
            (2000, 450.0, 270.0, 2), (3000, 450.0, 450.0, 3), (7000, 450.0, 450.0, 3), (7500, 375.0, 450.0, 3),
            (8000, 300.0, 270.0, 1), (9500, 375.0, 270.0, 2),
        }));
        Assert.That((m.Position.X, m.Position.Y, m.PosBase.Z, m.RotBase.Z), Is.EqualTo((100.0, 200.0, 300.0, 270.0)));
    }

    [Test]
    public void TheCycleStartsAtTheServerTickAndThenRunsOnTheLocalClock()
    {
        // m_nPhaseTime = GetServerTickAt(now) at the first frame and at each phase change; the time adds now - m_nPhaseStart
        var (b, m) = OnePlatform(Platform("p"));
        b.Update(100000, 1500);
        Assert.That(m.Position.Z, Is.EqualTo(375.0));
        b.Update(100250, 0);   // the server tick is read only at a phase change
        Assert.That(m.Position.Z, Is.EqualTo(412.5));
        b.Update(100700, 15500);   // 2200: the end wait begins, its phase time is this tick
        Assert.That((m.Position.Z, m.Phase), Is.EqualTo((450.0, 3)));
        b.Update(100800, 0);   // (15500 + 100) % 8000 = 7600: the way back, 400 ms from the base
        Assert.That((m.Position.Z, m.Phase), Is.EqualTo((360.0, 3)));
    }

    [Test]
    public void TheVelocityIsTheUnitizedStepAndZeroWhileItWaits()
    {
        var (b, m) = OnePlatform(Platform("p"));
        b.Update(1200, 1200);
        b.Update(1300, 1300);
        Assert.That(m.Velocity, Is.EqualTo(new Vec3(0, 0, 1)));
        b.Update(3000, 3000);
        b.Update(3100, 3100);
        Assert.That(m.Velocity, Is.EqualTo(default(Vec3)));
        b.Update(7500, 7500);
        b.Update(7600, 7600);
        Assert.That(m.Velocity, Is.EqualTo(new Vec3(0, 0, -1)));
    }

    [Test]
    public void SyncBOffsetsTheCycleAndSyncAZeroHoldsItAtSyncC()
    {
        // FUN_140adeaa0 stores the packet's a, b, c; the time adds m_nSyncB; with m_nSyncA 0 the place is m_nSyncC % cycle once
        // the time passes m_nSyncC
        var (b, m) = OnePlatform(Platform("p"));
        b.Apply(new BreakableUpdate(1, new[] { new BreakableEntry("p", 2, 1, 1500, 0) }));
        b.Update(0, 0);
        Assert.That(m.Position.Z, Is.EqualTo(375.0));

        (b, m) = OnePlatform(Platform("q"));
        b.Apply(new BreakableUpdate(1, new[] { new BreakableEntry("q", 2, 0, 0, 1250) }));
        b.Update(1100, 1100);
        Assert.That(m.Position.Z, Is.EqualTo(315.0).Within(1e-3));
        b.Update(5000, 5000);
        Assert.That(m.Position.Z, Is.EqualTo(337.5));
        b.Update(9000, 9000);
        Assert.That(m.Position.Z, Is.EqualTo(337.5));
    }

    [Test]
    public void AFieldMarkerStartsWithSyncAOneSoItRunsWithoutAPacket()
    {
        var (b, m) = OnePlatform(Platform("p"));
        Assert.That(m.SyncA, Is.EqualTo(1));
        b.Update(5000, 5000);
        Assert.That(m.Position.Z, Is.EqualTo(450.0));
    }

    [Test]
    public void RoundTypeOneEndsAtTheEndWaitAndJumpsBack()
    {
        var (b, m) = OnePlatform(Platform("p", round: "1"));
        b.Update(6999, 6999);
        Assert.That((m.Position.Z, m.Rotation.Z), Is.EqualTo((450.0, 270.0)));
        b.Update(7000, 7000);
        Assert.That((m.Position.Z, m.Phase), Is.EqualTo((300.0, 1)));
    }

    [Test]
    public void OnlyRoundTypeTwoTurnsAndTheWayBackFromAnotherPhaseIsPhaseFour()
    {
        var (b, m) = OnePlatform(Platform("p", round: "0"));
        b.Update(7500, 7500);
        Assert.That((m.Position.Z, m.Rotation.Z, m.Phase), Is.EqualTo((375.0, 270.0, 4)));
        b.Update(7600, 7600);
        Assert.That(m.Phase, Is.EqualTo(4));

        (b, m) = OnePlatform(Platform("q"));
        b.Update(6900, 6900);
        b.Update(7500, 7500);
        Assert.That((m.Rotation.Z, m.Phase), Is.EqualTo((450.0, 3)));
    }

    [Test]
    public void TheMoveTimeOffsetAddsToTheCycle()
    {
        var (b, m) = OnePlatform(Platform("p", offset: "0.5"));
        b.Update(1000, 1000);
        Assert.That(m.Position.Z, Is.EqualTo(375.0));
    }

    [Test]
    public void OnlyStatesTwoFiveAndSixMove()
    {
        foreach (var (state, moves) in new[] { (0, false), (1, false), (2, true), (3, false), (4, false), (5, true), (6, true) })
        {
            var (b, m) = OnePlatform(Platform("p"));
            b.Apply(new BreakableUpdate(1, new[] { new BreakableEntry("p", (byte)state, 1, 0, 0) }));
            b.Update(1500, 1500);
            Assert.That(m.Position.Z, Is.EqualTo(moves ? 375.0 : 300.0), $"state {state}");
        }
    }

    [Test]
    public void MovedFiresOnlyWhenThePlaceOrTurnChanges()
    {
        var (b, m) = OnePlatform(Platform("p"));
        int moved = 0;
        b.Moved = x => { Assert.That(x, Is.SameAs(m)); moved++; };
        b.Update(100, 100);   // the base: nothing changed
        b.Update(1500, 1500);
        b.Update(1600, 1600);
        b.Update(2500, 2500);   // the end wait: the top and the turn
        b.Update(2600, 2600);
        b.Update(3600, 3600);
        Assert.That(moved, Is.EqualTo(3));
    }

    [Test]
    public void AMarkerWithoutAMoveTimeOrDistanceIsNotInTheMovingList()
    {
        var b = new BreakableObjects();
        b.AttachField(new[] { Platform("a", dur: "0"), Platform("b", dist: "0"), Platform("c"), LiftEntityAsBreakable("d") });
        Assert.That(b.Moving.Select(m => m.Name), Is.EqualTo(new[] { "c" }));
        b.AttachField(new[] { Platform("c", dur: "0") });   // the same name replaces it
        Assert.That(b.Moving, Is.Empty);
        b.AttachField(new[] { Platform("c") });
        b.ClearMarkers();
        Assert.That(b.Moving, Is.Empty);
    }

    static FieldEntity LiftEntityAsBreakable(string id)
    {
        var flat = new FlatModel { Name = "MS2BreakableNIF" };
        flat.Mixins.Add("MS2Breakable");
        return new FieldEntity { Raw = new XEntity { Id = id, ModelName = "MS2BreakableNIF", Name = id }, Flat = flat };
    }

    [Test]
    public void TheMoveCycleAllocatesNothing()
    {
        var b = new BreakableObjects();
        b.AttachField(new[] { Platform("a"), Platform("b", round: "1"), Platform("c", offset: "2") });
        int n = 0;
        b.Moved = _ => n++;
        int t = 0;
        long bytes = Alloc.Least(() => { for (int i = 0; i < 200; i++) { t += 37; b.Update(t, t); } });
        Assert.That(bytes, Is.EqualTo(0));
        Assert.That(n, Is.GreaterThan(0));
    }

    internal static FieldEntity TargetBox(string id, string target, string pos = "0, 0, 0", string rot = "0, 0, 0")
    {
        var raw = new XEntity { Id = id, ModelName = "MS2LiftableTargetBox", Name = id };
        if (pos != null) raw.Props["Position"] = pos;
        if (rot != null) raw.Props["Rotation"] = rot;
        if (target != null) raw.Props["liftableTarget"] = target;
        return new FieldEntity { Raw = raw, Flat = new FlatModel { Name = "MS2LiftableTargetBox" } };
    }

    [Test]
    public void TheFieldsTargetBoxesAreAttachedNewestFirstAmongEqualIds()
    {
        // worldservice_1.cpp:6788-6801: every "MS2LiftableTargetBox" entity goes into the CRBMultiMap m_540 by its id; ATL's
        // InsertImpl walks left on <= 0, so a box goes before the boxes with its id. The field's boxes all share one id.
        var map = Field("02000041_bf");
        var want = map.Entities.Where(e => e.HasMixin("MS2LiftableTargetBox")).ToList();
        Assert.That(want.Count, Is.GreaterThan(1));
        Assert.That(want.Select(e => e.Number("liftableTarget")).Distinct().Count(), Is.EqualTo(1));
        var t = new LiftableTargetBoxes();
        Assert.That(t.AttachField(map.Entities), Is.EqualTo(want.Count));
        want.Reverse();
        Assert.That(t.All.Select(b => b.Entity), Is.EqualTo(want));
        foreach (var b in t.All)
        {
            Assert.That(b.Id, Is.EqualTo((int)b.Entity.Number("liftableTarget")));
            Assert.That(b.Position, Is.EqualTo(b.Entity.Position));
            Assert.That(b.Rotation, Is.EqualTo(b.Entity.Vector("Rotation")));
        }
    }

    [Test]
    public void TargetBoxesAreOrderedByIdAndABoxWithoutAPropertyIsDropped()
    {
        // CLiftableTarget::Init (@0x1414482a0, liftabletarget.cpp:84-96) fails without Position, Rotation or liftableTarget,
        // and the load deletes the box (worldservice_1.cpp:6796)
        var t = new LiftableTargetBoxes();
        var a = TargetBox("a", "5", "150, 300, 450", "0, 0, 90");
        var b = TargetBox("b", "2");
        var c = TargetBox("c", "5");
        var d = TargetBox("d", "-1");
        var other = new FieldEntity { Raw = new XEntity { Id = "o", ModelName = "MS2Liftable", Name = "o", Props = { ["liftableTarget"] = "0" } }, Flat = new FlatModel { Name = "MS2Liftable" } };
        var list = new List<FieldEntity> { a, TargetBox("np", "1", pos: null), b, TargetBox("nr", "1", rot: null), c, TargetBox("ni", null), other, d };
        Assert.That(t.AttachField(list), Is.EqualTo(4));
        Assert.That(t.All.Select(x => x.Entity.Raw.Id), Is.EqualTo(new[] { "d", "b", "c", "a" }));
        Assert.That((t.All[3].Id, t.All[3].Position, t.All[3].Rotation), Is.EqualTo((5, new Vec3(150, 300, 450), new Vec3(0, 0, 90))));
        // a new field load replaces the boxes; the field reset empties them (worldservice_1.cpp:10062)
        Assert.That(t.AttachField(new List<FieldEntity> { b }), Is.EqualTo(1));
        t.Clear();
        Assert.That(t.Count, Is.Zero);
        Assert.That(t.AttachField(null), Is.Zero);
    }
    [Test]
    public void ABoxIsMarkedOnlyWhileTheCraftCarriesTheItemAnOpenItemMoveQuestWantsThere()
    {
        // CLiftableTarget::FUN_1414483e0 (liftabletarget.cpp:99-134): in cube mode, a quest in progress and not complete
        // whose item_move (0x9c) condition names the box as its one target id and the craft's item in its code
        var t = new LiftableTargetBoxes();
        t.AttachField(new List<FieldEntity> { TargetBox("a", "404", pos: "100, 200, 300", rot: "0, 0, 90"), TargetBox("b", "405") });
        var data = new Ms2.Quest.QuestData { Id = 7 };
        data.Conditions.Add(new Ms2.Quest.QuestCondition { Type = 0x9c, Goal = 1, Target = Ms2.Quest.ConditionParam.Parse("404"), Code = Ms2.Quest.ConditionParam.Parse("30000856") });
        var other = new Ms2.Quest.QuestData { Id = 8 };   // two target ids: GetIdCount != 1, never a match
        other.Conditions.Add(new Ms2.Quest.QuestCondition { Type = 0x9c, Goal = 1, Target = Ms2.Quest.ConditionParam.Parse("404,405"), Code = Ms2.Quest.ConditionParam.Parse("30000856") });
        Ms2.Quest.QuestData Quest(int id) => id == 7 ? data : id == 8 ? other : null;
        var open = new QuestRecord { Id = 7, State = 1, Progress = new long[] { 0 } };
        var quests = new List<QuestRecord> { open, new QuestRecord { Id = 8, State = 1, Progress = new long[] { 0 } } };
        var log = new List<(int, bool)>();
        t.MarkerSet = (b, on) => log.Add((b.Id, on));

        t.RefreshMarkers(true, 30000856, quests, Quest);
        Assert.That(log, Is.EqualTo(new[] { (404, true) }));
        t.RefreshMarkers(true, 30000856, quests, Quest);     // CreateEffect returns while the effect runs
        Assert.That(log, Has.Count.EqualTo(1));
        t.RefreshMarkers(true, 30000441, quests, Quest);     // another item
        Assert.That(log[^1], Is.EqualTo((404, false)));
        t.RefreshMarkers(true, 30000856, quests, Quest);
        t.RefreshMarkers(false, 30000856, quests, Quest);    // out of the cube mode
        Assert.That(log[^1], Is.EqualTo((404, false)));
        open.Progress[0] = 1;                                // complete: IsComplete skips it
        t.RefreshMarkers(true, 30000856, quests, Quest);
        Assert.That(log, Has.Count.EqualTo(4));
        open.Progress[0] = 0; open.State = 2;                // rewarded: not in progress
        t.RefreshMarkers(true, 30000856, quests, Quest);
        Assert.That(log, Has.Count.EqualTo(4));
        // the marker's place: the box raised by 1, the box rotation's degrees, scale 1 (liftabletarget.cpp:140-144)
        var at = LiftableTargetBoxes.MarkerAt(t.All[0]);
        Assert.That(at.T, Is.EqualTo(new Vec3(100, 200, 301)));
        Assert.That(at.R, Is.EqualTo(DummyWorld.EulerXyz(0, 0, 90)));
        Assert.That(at.S, Is.EqualTo(1));
        // CConditionParam::Matches: ranges hold both ends, then the ids
        var p = Ms2.Quest.ConditionParam.Parse("10-20, 30");
        Assert.That(new[] { 9L, 10, 20, 21, 30, 31 }.Select(p.Matches), Is.EqualTo(new[] { false, true, true, false, true, false }));
    }
}
