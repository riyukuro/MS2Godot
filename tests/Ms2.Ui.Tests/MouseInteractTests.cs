using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The mouse controller's click picks (<see cref="MouseInteract"/>, mouseinteract.cpp and unit_140ad1860_mousecontroller.cpp):
/// the try order and cursor kinds, the standing sphere every pick but the npc one tests, the portal and interact object finds,
/// and the model keys of the taxi and ugc banner picks against the data's flat library.
/// </summary>
public class MouseInteractTests
{
    static readonly Vec3 Up = new(0, 0, 1);

    [Test]
    public void ThePicksGoInTheControllersOrderWithTheirCursorKinds()
    {
        Assert.That(MouseInteract.Order, Is.EqualTo(new[]
        {
            MousePick.TalkCancel, MousePick.Taxi, MousePick.UgcBanner, MousePick.WeaponObject, MousePick.Portal, MousePick.Object,
            MousePick.Npc,
        }));
        Assert.That(MouseInteract.Order.Select(MouseInteract.CursorKind), Is.EqualTo(new[] { 6, 4, 5, 7, 2, 3, 1 }));
        Assert.That(MouseInteract.CursorKind(MousePick.None), Is.EqualTo(0));
    }

    [Test]
    public void TheSphereStandsOnThePointAndIsAsTallAsTheSize()
    {
        // a ray straight down onto the base point hits; one passing just outside the radius misses
        var basePoint = new Vec3(100, 200, 50);
        Assert.That(MouseInteract.HitsStanding(new Vec3(100, 200, 1000), new Vec3(0, 0, -1), basePoint, 150), Is.True);
        Assert.That(MouseInteract.HitsStanding(new Vec3(100 + 74, 200, 1000), new Vec3(0, 0, -1), basePoint, 150), Is.True);
        Assert.That(MouseInteract.HitsStanding(new Vec3(100 + 76, 200, 1000), new Vec3(0, 0, -1), basePoint, 150), Is.False);
        // a level ray at the centre's height (base + 75) hits; one above the top (base + 151) misses
        Assert.That(MouseInteract.HitsStanding(new Vec3(-1000, 200, 125), new Vec3(1, 0, 0), basePoint, 150), Is.True);
        Assert.That(MouseInteract.HitsStanding(new Vec3(-1000, 200, 201), new Vec3(1, 0, 0), basePoint, 150), Is.False);
        // pointing away from it misses
        Assert.That(MouseInteract.HitsStanding(new Vec3(100, 200, 1000), Up, basePoint, 150), Is.False);
    }

    static LivePortal Portal(int id, bool glow = true, int? kind = null, double height = 250, bool entity = true) => new()
    {
        Id = id, Glow = glow, Kind = kind, HasEntity = entity, Position = new Vec3(0, 0, 0), Dimension = new Vec3(200, 200, height),
        TargetMap = 1000 + id,
    };

    [Test]
    public void APortalPicksByIdWhenEnabledAndNotKindOne()
    {
        var down = new Vec3(0, 0, -1);
        var from = new Vec3(0, 0, 2000);
        // the lowest id of the hit ones, not the list order (the field object map is keyed by id)
        Assert.That(MouseInteract.FindPortal(new[] { Portal(7), Portal(3), Portal(5) }, from, down)?.Id, Is.EqualTo(3));
        // not enabled (0x14969c28), kind 1 (0x1b8b1161) and no entity are passed over
        Assert.That(MouseInteract.FindPortal(new[] { Portal(1, glow: false), Portal(2, kind: 1), Portal(3, entity: false), Portal(4, kind: 0) },
            from, down)?.Id, Is.EqualTo(4));
        // the sphere is as tall as the portal's dimension Z: a level ray at 240 hits a 250 tall portal, not a 200 tall one
        var level = new Vec3(1, 0, 0);
        Assert.That(MouseInteract.FindPortal(new[] { Portal(1, height: 250) }, new Vec3(-1000, 0, 240), level), Is.Not.Null);
        Assert.That(MouseInteract.FindPortal(new[] { Portal(1, height: 200) }, new Vec3(-1000, 0, 240), level), Is.Null);
    }

    static LiveInteractObject Obj(string name, double z, byte kind = 1) => new()
    {
        Name = name, Kind = kind, Position = new Vec3(0, 0, 0), Row = new InteractObjectRow { ShapeDimension = new Vec3(150, 150, z) },
    };

    [Test]
    public void AnObjectPicksInListOrderWhenUsableWithAShapeHeight()
    {
        var down = new Vec3(0, 0, -1);
        var from = new Vec3(0, 0, 2000);
        var list = new[] { Obj("a", 0), Obj("b", 150), Obj("c", 150) };
        // a shape of height 0 is passed over (IsNearlyEqual(fSize, 0)); then the list order
        Assert.That(MouseInteract.FindObject(list, _ => true, from, down)?.Name, Is.EqualTo("b"));
        // one the player may not use (vfunc_4) is passed over
        Assert.That(MouseInteract.FindObject(list, o => o.Name != "b", from, down)?.Name, Is.EqualTo("c"));
        Assert.That(MouseInteract.FindObject(list, _ => false, from, down), Is.Null);
    }

    [Test]
    public void TheObjectPickWaitsForItsKindsAction()
    {
        Assert.That(MouseInteract.ObjectActionKey(InteractObjectMessage.KindBillBoard), Is.EqualTo(0x18));
        Assert.That(MouseInteract.ObjectActionKey(InteractObjectMessage.KindGathering), Is.EqualTo(4));
        Assert.That(MouseInteract.ObjectActionKey(1), Is.EqualTo(0x12));
        Assert.That(MouseInteract.ObjectActionKey(InteractObjectMessage.KindWatchTower), Is.EqualTo(0x12));
        // MyPC::FUN_141527160(0x19): states 1, 0xb, 0xf, 0x21, 0x3a
        foreach (int s in new[] { 1, 0xb, 0xf, 0x21, 0x3a }) Assert.That(MouseInteract.ObjectStateAllows(s), Is.True, $"state {s}");
        foreach (int s in new[] { -1, 0, 2, 6, 0x10, 0x19, 0x1a }) Assert.That(MouseInteract.ObjectStateAllows(s), Is.False, $"state {s}");
    }

    [Test]
    public void TheModelPickTakesTheFirstHitPosition()
    {
        var at = new List<Vec3> { new(5000, 0, 0), new(0, 0, 0), new(0, 0, 10) };
        Assert.That(MouseInteract.FindModel(at, MouseInteract.TaxiPickSize, new Vec3(0, 0, 2000), new Vec3(0, 0, -1)), Is.EqualTo(1));
        Assert.That(MouseInteract.FindModel(at, MouseInteract.TaxiPickSize, new Vec3(0, 3000, 2000), new Vec3(0, 0, -1)), Is.EqualTo(-1));
        Assert.That(MouseInteract.FindModel(null, 1, new Vec3(0, 0, 0), Up), Is.EqualTo(-1));
    }

    [Test]
    public void TheHoverRangePassesOverFarHits()
    {
        Assert.That(MouseInteract.InHoverRange(new Vec3(0, 0, 0), new Vec3(2000, 0, 0), MouseInteract.DefaultLimitDistance), Is.True);
        Assert.That(MouseInteract.InHoverRange(new Vec3(0, 0, 0), new Vec3(1500, 1500, 0), MouseInteract.DefaultLimitDistance), Is.False);
    }

    /// <summary>
    /// The taxi and ugc banner picks find their entities by the model key (WorldService::FUN_141bd0420): 0x85359406 and
    /// 0xc7f9dd65. In the flat library a model's id and each mixin's id are those keys. Over every flat model, the models
    /// that are or take the mixin with id 0xc7f9dd65 are exactly the ones <see cref="FieldEntity.HasMixin"/> finds by the name
    /// MS2UccProp, and the model with id 0x85359406 is MS2TaxiStation. Runs on MS2_DATA and, when set, MS2_GMS2_DATA.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TheTaxiAndUgcBannerModelKeysAreTheNamesTheFieldUses(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} is not set");
        var packs = new PackSet(dir);
        var lib = new FlatLibrary(packs);
        var byKey = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var byName = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        string taxiModel = null;
        int models = 0;
        foreach (var entry in packs.List(XBlock.Archive))
        {
            if (!entry.StartsWith("flat/", StringComparison.OrdinalIgnoreCase) || !entry.EndsWith(".flat", StringComparison.OrdinalIgnoreCase)) continue;
            var root = XDocument.Load(new MemoryStream(packs.Read(XBlock.Archive, entry))).Root;
            if (root == null || root.Name != "model") continue;
            models++;
            string name = (string)root.Attribute("name");
            if ((uint?)(long?)root.Attribute("id") == 0x85359406u) taxiModel = name;
            // ContainsModel: the model itself or one of its mixins
            if ((uint?)(long?)root.Attribute("id") == 0xc7f9dd65u
                || root.Elements("mixin").Any(m => (uint?)(long?)m.Attribute("id") == 0xc7f9dd65u)) byKey.Add(name);
            var e = new FieldEntity { Raw = new XEntity { ModelName = name }, Flat = lib.Get(Path.GetFileNameWithoutExtension(entry)) };
            if (e.HasMixin(MouseInteract.UgcBannerMixin)) byName.Add(name);
        }
        TestContext.Out.WriteLine($"{env}: {models} flat models; ugc banner models ({byKey.Count}): {string.Join(", ", byKey)}");
        Assert.That(models, Is.GreaterThan(100));
        Assert.That(taxiModel, Is.EqualTo("MS2TaxiStation"));
        Assert.That(byKey, Is.Not.Empty);
        Assert.That(byName, Is.EqualTo(byKey));
    }

    [Test]
    public void TheHoverSetsInteractOrTheHandOnlyOverThoseTwo()
    {
        // UpdateHover (:690-698): from the hand or interact, interact over a pick and the hand otherwise
        Assert.That(MouseCursorController.HoverType(MouseCursorController.Hand, 1), Is.EqualTo(MouseCursorController.Interact));
        Assert.That(MouseCursorController.HoverType(MouseCursorController.Interact, 0), Is.EqualTo(MouseCursorController.Hand));
        Assert.That(MouseCursorController.HoverType(MouseCursorController.Hand, 0), Is.EqualTo(MouseCursorController.Hand));
        Assert.That(MouseCursorController.HoverType(MouseCursorController.Interact, 6), Is.EqualTo(MouseCursorController.Interact));
        // any other cursor (sell, drag, text ...) is left alone
        foreach (int other in new[] { 1, 3, 5, 8, 9, 10 })
            Assert.That(MouseCursorController.HoverType(other, 1), Is.Null, $"type {other}");
    }

    /// <summary>
    /// The picks' hints (vfunc_1's strCursor) on the data's English strings: the fixed taxi, ugc banner and talk end ones, the
    /// portal's target map name, the object's action from stringInteractObjectAction, and the npc's action and name (with the
    /// common fallback when actionName has none). Runs on MS2_DATA and, when set, MS2_GMS2_DATA.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TheHintsReadTheMouseInteractStrings(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} is not set");
        var t = GameTables.Shared(new PackSet(dir), "en");
        const string open = "<FONT color='#FFD200' size='16' >";
        Assert.That(MouseInteract.FixedHint(t, MousePick.Taxi), Is.EqualTo($"Action - {open}Call Taxi</FONT>"));
        Assert.That(MouseInteract.FixedHint(t, MousePick.UgcBanner), Does.StartWith("Action - " + open));
        Assert.That(MouseInteract.FixedHint(t, MousePick.TalkCancel), Is.Not.Empty.And.Not.Contains("{"));
        Assert.That(MouseInteract.FixedHint(t, MousePick.Npc), Is.Empty);

        string tria = t.ReservedWords.Lookup("mapname", 2000001);
        Assert.That(tria, Is.Not.Empty);
        Assert.That(MouseInteract.PortalHint(t, 0, 2000001), Is.EqualTo($"Move - {open}{tria}</FONT>"));
        // no target: the portal's action text by type (common 0x632 by default)
        Assert.That(MouseInteract.PortalHint(t, 0, 0), Is.EqualTo(t.Common(0x632)).And.Not.Empty);
        Assert.That(MouseInteract.PortalHint(t, 8, 0), Is.EqualTo(t.Common("s_action_portal_DungeonLobby")));

        Assert.That(MouseInteract.ObjectHint(t, 10000001), Is.EqualTo($"Action - {open}Milk</FONT>"));

        // 11000004 has the action "Trade"; 11000001 has none and takes common 0x62d
        string name4 = t.ReservedWords.Lookup("npcname", 11000004), name1 = t.ReservedWords.Lookup("npcname", 11000001);
        Assert.That(MouseInteract.NpcHint(t, 11000004, false), Is.EqualTo($"Trade - {open}{name4}</FONT>"));
        Assert.That(t.Common(0x62d), Is.Not.Empty);
        Assert.That(MouseInteract.NpcHint(t, 11000001, false), Is.EqualTo($"{t.Common(0x62d)} - {open}{name1}</FONT>"));
        TestContext.Out.WriteLine($"{env}: {MouseInteract.NpcHint(t, 11000001, false)} | {MouseInteract.FixedHint(t, MousePick.TalkCancel)}");
    }
}
