using System.Collections.Generic;
using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>MyPC::GetActiveCubeCraft (@0x141530290, mypc.cpp:4516) and Unk_PCCubeCraft::ConfirmPlacement (@0x1410aad50,
/// cubecraft_2.cpp:936-1063).</summary>
public class ConfirmPlacementTests
{
    const int ItemId = 50100001;

    sealed class Cells : ICubeCells
    {
        public readonly HashSet<int> Keys = new();
        public bool HasCubeAtKey(int key) => Keys.Contains(key);
        public readonly Dictionary<int, CubeItemInfo> Items = new();
        public bool TryCubeItemAtKey(int key, out CubeItemInfo item) => Items.TryGetValue(key, out item);
    }

    sealed class Owner : ICubeCraftOwner
    {
        public bool IsLocal { get; set; } = true;
        public Vector3 Point { get; set; } = new(1000f, 2000f, 300f);
        public float RotZ { get; set; }
        public float TargetYaw { get; set; }
        public int StateId { get; set; } = 1;
        public Cells Cells = new();
        public bool NoMap;
        public ICubeCells Map;
        public ICubeCells Cubes => NoMap ? null : Map ?? Cells;
    }

    static CubeCraftState Craft(int cubeMode, int craftMode, Owner owner = null, CubeItemInfo item = null)
    {
        var c = new CubeCraftState { Owner = owner };
        c.NewPc();
        c.ModelAttached();
        c.Set(cubeMode, craftMode, item ?? new CubeItemInfo { ItemId = ItemId, InstanceId = 77, Count = 1 }, dead: false);
        return c;
    }

    static ItemData Plain = new();
    static CubeCraftState.PlacementStop Confirm(CubeCraftState c, out CubePlaceRequest send, ItemData data = null) =>
        c.ConfirmPlacement(false, id => id == ItemId ? data ?? Plain : null, out send);

    [Test]
    public void TheActiveCraftNeedsALivePcInCubeMode()
    {
        var c = Craft(2, 2, new Owner());
        Assert.That(c.Active(false), Is.SameAs(c.Current));
        Assert.That(c.Active(true), Is.Null);
        Assert.That(c.ConfirmPlacement(true, _ => Plain, out var s1), Is.EqualTo(CubeCraftState.PlacementStop.NoCraft));
        Assert.That(new CubeCraftState().ConfirmPlacement(false, _ => Plain, out var s2), Is.EqualTo(CubeCraftState.PlacementStop.NoCraft));
        Assert.That(s1, Is.Null);
        Assert.That(s2, Is.Null);
    }

    [Test]
    public void CubeModeOneStopsAtTheConstructionGuideBeforeTheNotice()
    {
        Assert.That(Confirm(Craft(1, 1, new Owner()), out var s1), Is.EqualTo(CubeCraftState.PlacementStop.NoConstructionGuide));
        Assert.That(Confirm(Craft(1, 2, new Owner()), out var s2), Is.EqualTo(CubeCraftState.PlacementStop.NoConstructionGuide));
        Assert.That(s1, Is.Null);
        Assert.That(s2, Is.Null);
    }

    [Test]
    public void CraftModeOneGivesTheBuildNotice()
    {
        Assert.That(Confirm(Craft(2, 1, new Owner()), out var s), Is.EqualTo(CubeCraftState.PlacementStop.BuildNotice));
        Assert.That(s, Is.Null);
        Assert.That(CubeCraftState.ModeOneNotice, Is.EqualTo(0x39));
    }

    [Test]
    public void WithoutALocalOwnerThereIsNoGuide()
    {
        Assert.That(Confirm(Craft(2, 2), out var s), Is.EqualTo(CubeCraftState.PlacementStop.NoGuide));
        Assert.That(Confirm(Craft(2, 2, new Owner { IsLocal = false }), out _), Is.EqualTo(CubeCraftState.PlacementStop.NoGuide));
        Assert.That(s, Is.Null);
    }

    [Test]
    public void AnUnknownItemStopsBeforeTheModeCheck()
    {
        var c = Craft(2, 0, new Owner(), new CubeItemInfo { ItemId = ItemId + 1 });
        Assert.That(Confirm(c, out var s), Is.EqualTo(CubeCraftState.PlacementStop.NoItemData));
        Assert.That(s, Is.Null);
    }

    [Test]
    public void CraftModeZeroStopsAtTheOpenInventoryCheck()
    {
        Assert.That(Confirm(Craft(2, 0, new Owner()), out var s), Is.EqualTo(CubeCraftState.PlacementStop.InventoryCheckOpen));
        Assert.That(s, Is.Null);
    }

    [Test]
    public void CraftModeZeroAsksTheLockCheckOnceTheUnlockIsKnown()
    {
        var c = Craft(2, 0, new Owner());
        c.ConstructionUnlocked = false;
        Assert.That(Confirm(c, out _), Is.EqualTo(CubeCraftState.PlacementStop.InventoryCheckOpen), "no lock check yet");
        c.ConstructionUnlocked = null;
        c.Locks = new CubeLock
        {
            Housing = new Ms2.Formats.World.HousingTable(System.Xml.Linq.XDocument.Parse($"<ms2><housing id=\"{ItemId}\" interiorLevel=\"3\"/></ms2>")),
            IsFeatureEnabled = id => id == CubeLock.HousingLevelFeature,
        };
        Assert.That(Confirm(c, out _), Is.EqualTo(CubeCraftState.PlacementStop.InventoryCheckOpen), "no unlock yet");
        c.ConstructionUnlocked = false;
        Assert.That(Confirm(c, out var s), Is.EqualTo(CubeCraftState.PlacementStop.Locked));
        Assert.That(s, Is.Null);
        Assert.That(CubeCraftState.NoticeOf(CubeCraftState.PlacementStop.Locked), Is.EqualTo(0x58));
        c.ConstructionUnlocked = true;
        Assert.That(Confirm(c, out _), Is.Not.EqualTo(CubeCraftState.PlacementStop.Locked));
        Assert.That(Confirm(c, out _), Is.Not.EqualTo(CubeCraftState.PlacementStop.InventoryCheckOpen));
    }

    [Test]
    public void ALockWhoseTablesDidNotLoadIsNoneSoALockedItemIsNotSent()
    {
        var housing = new Ms2.Formats.World.HousingTable(System.Xml.Linq.XDocument.Parse($"<ms2><housing id=\"{ItemId}\" interiorLevel=\"3\"/></ms2>"));
        var interiors = new Ms2.Formats.World.CubeInteriorTable(System.Xml.Linq.XDocument.Parse("<ms2/>"));
        var c = Craft(2, 0, new Owner());
        c.ConstructionUnlocked = false;
        c.Locks = CubeLock.WithTables(() => housing, () => interiors);
        Assert.That(c.Locks, Is.Not.Null);
        c.Locks.IsFeatureEnabled = id => id == CubeLock.HousingLevelFeature;
        Assert.That(Confirm(c, out _), Is.EqualTo(CubeCraftState.PlacementStop.Locked), "the tables loaded");

        int failed = 0;
        Assert.That(CubeLock.WithTables(() => throw new System.IO.IOException(), () => interiors, _ => failed++), Is.Null);
        Assert.That(CubeLock.WithTables(() => housing, () => throw new System.IO.IOException(), _ => failed++), Is.Null);
        Assert.That(CubeLock.WithTables(() => null, () => interiors), Is.Null);
        Assert.That(failed, Is.EqualTo(2));

        c.Locks = CubeLock.WithTables(() => throw new System.IO.IOException(), () => interiors);
        Assert.That(Confirm(c, out var s), Is.EqualTo(CubeCraftState.PlacementStop.InventoryCheckOpen));
        Assert.That(s, Is.Null);
    }

    [Test]
    public void CraftModeTwoSkipsTheLockCheck()
    {
        var c = Craft(2, 2, new Owner());
        c.ConstructionUnlocked = false;
        c.Locks = new CubeLock
        {
            Housing = new Ms2.Formats.World.HousingTable(System.Xml.Linq.XDocument.Parse($"<ms2><housing id=\"{ItemId}\" interiorLevel=\"3\"/></ms2>")),
            IsFeatureEnabled = _ => true,
        };
        Assert.That(Confirm(c, out _), Is.Not.EqualTo(CubeCraftState.PlacementStop.Locked));
    }

    [Test]
    public void ATextureCubeNeedsTheUploadedLook()
    {
        var mesh = new ItemData { UccMesh = "m" };
        Assert.That(Confirm(Craft(2, 2, new Owner()), out var s, mesh), Is.EqualTo(CubeCraftState.PlacementStop.NoTextureLook));
        Assert.That(s, Is.Null);
        Assert.That(CubeCraftState.TextureNotice, Is.EqualTo(0x62));
        var noUid = new CubeItemInfo { ItemId = ItemId, Look = new UgcLook() };
        Assert.That(Confirm(Craft(2, 2, new Owner(), noUid), out _, mesh), Is.EqualTo(CubeCraftState.PlacementStop.NoTextureLook));
        var looked = new CubeItemInfo { ItemId = ItemId, Look = new UgcLook { Uid = 9 } };
        Assert.That(Confirm(Craft(2, 2, new Owner(), looked), out var sent, mesh), Is.EqualTo(CubeCraftState.PlacementStop.Send));
        Assert.That(sent.Item.Look, Is.SameAs(looked.Look));
    }

    [Test]
    public void AnAttachableItemTakesTheOpenAttachPath()
    {
        var attach = new ItemData { InstallAttachable = 1 };
        Assert.That(Confirm(Craft(2, 2, new Owner()), out var s, attach), Is.EqualTo(CubeCraftState.PlacementStop.AttachableOpen));
        Assert.That(s, Is.Null);
    }

    [Test]
    public void OutsideCraftModeTwoAnAttachableItemIsPlacedLikeAnyOther()
    {
        var c = Craft(2, 0, new Owner());
        c.ConstructionUnlocked = true;
        c.Locks = new CubeLock();
        Assert.That(Confirm(c, out var s, new ItemData { InstallAttachable = 1 }), Is.EqualTo(CubeCraftState.PlacementStop.Send));
        Assert.That(s, Is.Not.Null);
    }

    [Test]
    public void ACubeWhoseItemTheMapDoesNotKnowTakesTheOpenBranch()
    {
        var o = new Owner();
        var c = Craft(2, 2, o);
        o.Cells.Keys.Add(CubeCell.Key(c.Current.Cell));
        Assert.That(Confirm(c, out var s), Is.EqualTo(CubeCraftState.PlacementStop.CubeInCellOpen));
        Assert.That(s, Is.Null);
        Assert.That(Confirm(Craft(2, 2, new Owner { NoMap = true }), out _), Is.EqualTo(CubeCraftState.PlacementStop.NoCubeMap));
    }

    static (CubeCraftState c, int key) WithCubeInCell(int craftMode, CubeItemInfo inCell, CubeItemInfo held = null, float target = 0f)
    {
        var o = new Owner { TargetYaw = target };
        var c = Craft(2, craftMode, o, held);
        c.ConstructionUnlocked = true;
        c.Locks = new CubeLock();
        int key = CubeCell.Key(c.Current.Cell);
        o.Cells.Keys.Add(key);
        o.Cells.Items[key] = inCell;
        return (c, key);
    }

    [Test]
    public void ALoadedHouseCubeTakesTheReplaceBranch()
    {
        // OnLoadCubes mode 0 (scenestategame.cpp:10857-10897) fills the cell map through FUN_1410c4850; ConfirmPlacement
        // (cubecraft_2.cpp:990-1031) over that cube sends a replace (0xf), not a place
        var map = new CubeCellMap();
        var o = new Owner { Map = map };
        var c = Craft(2, 2, o, new CubeItemInfo { ItemId = ItemId, InstanceId = 5, Count = 1 });
        c.ConstructionUnlocked = true;
        c.Locks = new CubeLock();
        int key = CubeCell.Key(c.Current.Cell);
        var load = new LoadCubes(0, new[] { new ResponseCubePlace(ResponseCubePlace.ModePlace, 0, 0, key, Item: new CubeItemInfo { ItemId = 50100002, Count = 1 }) });
        Assert.That(map.Load(load, _ => true), Is.EqualTo(1));
        Assert.That(Confirm(c, out var sent), Is.EqualTo(CubeCraftState.PlacementStop.Send));
        Assert.That((sent.Mode, sent.CellKey), Is.EqualTo((CubePlaceRequest.ReplaceMode, key)));
    }

    [Test]
    public void ACubeFromAPlaceAnswerTakesTheReplaceAndSameItemBranches()
    {
        // OnResponseCube 0xa (scenestategame.cpp:9984-10011) through FUN_1410c4850 into the cell map; ConfirmPlacement's
        // cell checks (cubecraft_2.cpp:990-1031) then see the cube: another item is a 0xf, the same one the held stop
        var map = new CubeCellMap();
        var o = new Owner { Map = map };
        var c = Craft(2, 2, o, new CubeItemInfo { ItemId = ItemId, InstanceId = 5, Count = 1 });
        c.ConstructionUnlocked = true;
        c.Locks = new CubeLock();
        int key = CubeCell.Key(c.Current.Cell);
        Assert.That(Confirm(c, out var before), Is.EqualTo(CubeCraftState.PlacementStop.Send));
        Assert.That(before.Mode, Is.EqualTo(CubePlaceRequest.PlaceMode));

        var placed = new ResponseCubePlace(ResponseCubePlace.ModePlace, 0, 1, key, Item: new CubeItemInfo { ItemId = 50100002, Count = 1 });
        Assert.That(map.Apply(placed, _ => true), Is.True);
        Assert.That(Confirm(c, out var replace), Is.EqualTo(CubeCraftState.PlacementStop.Send));
        Assert.That((replace.Mode, replace.CellKey), Is.EqualTo((CubePlaceRequest.ReplaceMode, key)));

        var replaced = placed with { Mode = ResponseCubePlace.ModeReplace, Item = new CubeItemInfo { ItemId = ItemId, Count = 1 } };
        Assert.That(map.Apply(replaced, _ => true), Is.True);
        Assert.That(Confirm(c, out var same), Is.EqualTo(CubeCraftState.PlacementStop.SameItemHeld));
        Assert.That(same, Is.Null);
    }

    [Test]
    public void AnotherItemInTheCellSendsRequestCube0xfWithTheSnappedYawNotNormalized()
    {
        // cubecraft_2.cpp:1007-1031: a map cube (item id 0) under the held item; target -100 snaps to -90, not 270
        var held = new CubeItemInfo { ItemId = ItemId, InstanceId = 0x1122334455L, Count = 3 };
        var (c, key) = WithCubeInCell(2, new CubeItemInfo(), held, -100f);
        Assert.That(Confirm(c, out var send), Is.EqualTo(CubeCraftState.PlacementStop.Send));
        Assert.That((send.Mode, send.CellKey, send.Yaw), Is.EqualTo((CubePlaceRequest.ReplaceMode, key, -90f)));
        Assert.That(send.Item, Is.Not.SameAs(held));

        var w = new ByteWriter();
        send.Write(w);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        Assert.That(r.ReadByte(), Is.EqualTo(0xf));
        Assert.That(r.ReadInt(), Is.EqualTo(key));
        Assert.That(r.ReadInt(), Is.EqualTo(ItemId));
        Assert.That(r.ReadLong(), Is.EqualTo(0x1122334455L));
        Assert.That(r.ReadLong(), Is.EqualTo(3L));
        Assert.That(r.ReadByte(), Is.EqualTo(0));
        Assert.That(r.ReadFloat(), Is.EqualTo(-90f));
        Assert.That(r.Available, Is.Zero);
    }

    [Test]
    public void TheSameItemIdWithAnotherLookIsReplaced()
    {
        var held = new CubeItemInfo { ItemId = ItemId, Count = 1, Look = new UgcLook { Uid = 5 } };
        var (c, _) = WithCubeInCell(2, new CubeItemInfo { ItemId = ItemId, Count = 1, Look = new UgcLook { Uid = 6 } }, held);
        Assert.That(Confirm(c, out var send), Is.EqualTo(CubeCraftState.PlacementStop.Send));
        Assert.That(send.Mode, Is.EqualTo(CubePlaceRequest.ReplaceMode));
    }

    [Test]
    public void TheSameItemInCraftModeTwoStopsWithTheHeldNotice()
    {
        var (c, _) = WithCubeInCell(2, new CubeItemInfo { ItemId = ItemId, InstanceId = 9, Count = 1 });
        Assert.That(Confirm(c, out var send), Is.EqualTo(CubeCraftState.PlacementStop.SameItemHeld));
        Assert.That(send, Is.Null);
        Assert.That(CubeCraftState.NoticeOf(CubeCraftState.PlacementStop.SameItemHeld), Is.Zero);
    }

    [Test]
    public void TheSameItemOutsideCraftModeTwoStopsWithNotice0x35()
    {
        var look = new UgcLook { Uid = 5 };
        var (c, _) = WithCubeInCell(0, new CubeItemInfo { ItemId = ItemId, Count = 1, Look = look },
            new CubeItemInfo { ItemId = ItemId, Count = 1, Look = new UgcLook { Uid = 5 } });
        Assert.That(Confirm(c, out var send), Is.EqualTo(CubeCraftState.PlacementStop.SameItem));
        Assert.That(send, Is.Null);
        Assert.That(CubeCraftState.NoticeOf(CubeCraftState.PlacementStop.SameItem), Is.EqualTo(0x35));
    }

    [TestCase(-90f, -90f)]
    [TestCase(270f, -90f)]
    [TestCase(-135f, -180f)]
    [TestCase(135f, 180f)]
    [TestCase(45f, 90f)]
    [TestCase(-45f, -90f)]
    [TestCase(0f, 0f)]
    public void TheSnappedYawKeepsItsSign(float guide, float sent)
    {
        Assert.That(CubeCraftState.SnappedYaw(guide), Is.EqualTo(sent));
    }

    [Test]
    public void AFreeCellSendsRequestCubeTenWithTheCellItemAndYaw()
    {
        var o = new Owner { TargetYaw = 100f };
        var item = new CubeItemInfo { ItemId = ItemId, InstanceId = 0x1122334455L, Count = 3 };
        var c = Craft(2, 2, o, item);
        Assert.That(c.Current.GuideYaw, Is.EqualTo(180f));
        Assert.That(Confirm(c, out var send), Is.EqualTo(CubeCraftState.PlacementStop.Send));
        Assert.That(send.CellKey, Is.EqualTo(CubeCell.Key(c.Current.Cell)));
        Assert.That(send.Yaw, Is.EqualTo(180f));
        Assert.That(send.Mode, Is.EqualTo(CubePlaceRequest.PlaceMode));
        Assert.That(send.Item, Is.Not.SameAs(item));
        Assert.That((send.Item.ItemId, send.Item.InstanceId, send.Item.Count), Is.EqualTo((item.ItemId, item.InstanceId, item.Count)));

        var w = new ByteWriter();
        send.Write(w);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        Assert.That(r.ReadByte(), Is.EqualTo(10));
        Assert.That(r.ReadInt(), Is.EqualTo(send.CellKey));
        Assert.That(r.ReadInt(), Is.EqualTo(ItemId));
        Assert.That(r.ReadLong(), Is.EqualTo(0x1122334455L));
        Assert.That(r.ReadLong(), Is.EqualTo(3L));
        Assert.That(r.ReadByte(), Is.EqualTo(0));
        Assert.That(r.ReadFloat(), Is.EqualTo(180f));
        Assert.That(r.Available, Is.Zero);
    }

    [TestCase(-150f, 270f)]
    [TestCase(-100f, 270f)]
    [TestCase(-30f, 0f)]
    [TestCase(30f, 90f)]
    [TestCase(150f, 180f)]
    public void ANegativeTargetYawSendsTheYawOfItsZeroTo360Angle(float target, float sent)
    {
        var c = Craft(2, 2, new Owner { TargetYaw = target });
        Assert.That(Confirm(c, out var send), Is.EqualTo(CubeCraftState.PlacementStop.Send));
        Assert.That(send.Yaw, Is.EqualTo(sent));
    }

    [TestCase(0f, 0f)]
    [TestCase(90f, 90f)]
    [TestCase(180f, 180f)]
    [TestCase(270f, 270f)]
    [TestCase(360f, 0f)]
    [TestCase(-90f, 270f)]
    [TestCase(-180f, 180f)]
    [TestCase(45f, 90f)]
    [TestCase(-45f, 270f)]
    public void ThePlacementYawSnapsAwayFromZeroThenNormalizes(float guide, float sent)
    {
        Assert.That(CubeCraftState.PlacementYaw(guide), Is.EqualTo(sent));
    }

    [TestCase(-90f, 270f)]
    [TestCase(450.7f, 90f)]
    [TestCase(-0.5f, 0f)]
    [TestCase(-360f, 0f)]
    public void NormalizeDegreesTruncatesThenWraps(float d, float n)
    {
        Assert.That(CubeCraftState.NormalizeDegrees(d), Is.EqualTo(n));
    }

    static CubeItemInfo Cube => new() { ItemId = ItemId, InstanceId = 77, Count = 1 };

    [Test]
    public void EnteringTheCubeModeSetsTheUnlockAndLeavingClearsIt()
    {
        // MyPC::vfunc_135 (mypc.cpp:1364-1397): on an IsInCubeMode change with craft mode != 2
        bool? onEnter = true;
        var c = new CubeCraftState { Owner = new Owner(), ConstructionUnlockOnEnter = () => onEnter };
        c.NewPc();
        Assert.That(c.ConstructionUnlocked, Is.False, "MyPC ctor: m_b15d4 0");
        c.Set(1, 0, Cube, dead: false);
        Assert.That(c.ConstructionUnlocked, Is.True);
        onEnter = null;
        c.Set(2, 0, Cube, dead: false);
        Assert.That(c.ConstructionUnlocked, Is.True, "still in cube mode: no change");
        c.Set(0, 0, null, dead: false);
        Assert.That(c.ConstructionUnlocked, Is.False);
        c.Set(1, 0, Cube, dead: false);
        Assert.That(c.ConstructionUnlocked, Is.Null, "an unknown answer stays unknown");
    }

    [Test]
    public void CraftModeTwoAndADeadPcLeaveTheUnlockAlone()
    {
        var c = new CubeCraftState { Owner = new Owner(), ConstructionUnlockOnEnter = () => true };
        c.NewPc();
        c.Set(2, 2, Cube, dead: false);
        Assert.That(c.ConstructionUnlocked, Is.False, "b == 2");
        c.Set(0, 2, null, dead: false);
        c.Set(1, 0, Cube, dead: true);
        Assert.That(c.InCubeMode, Is.False);
        Assert.That(c.ConstructionUnlocked, Is.False, "PC::vfunc_135 does nothing on a dead pc: no mode change");
    }

    [Test]
    public void TheUnlockIsTrueOnAnyKnownReasonElseUnknownWhileTheMapTestIs()
    {
        // mypc.cpp:1380-1383: (a) || (b) || (c); (a) is unported (null)
        Assert.That(CubeCraftState.ConstructionUnlock(null, false, false), Is.Null);
        Assert.That(CubeCraftState.ConstructionUnlock(null, true, false), Is.True);
        Assert.That(CubeCraftState.ConstructionUnlock(null, false, true), Is.True);
        Assert.That(CubeCraftState.ConstructionUnlock(false, false, false), Is.False);
        Assert.That(CubeCraftState.ConstructionUnlock(true, false, false), Is.True);
    }

    [Test]
    public void AnIgnoredLockOnEnterLetsCraftModeZeroPassTheLockCheck()
    {
        var events = new ConstructionEvents();
        events.Apply(new GameEventMessage(0, System.Array.Empty<ShutdownInteractObjectEvent>(), System.Array.Empty<int>(), false)
            { ConstructionEvents = new object[] { new IgnoreConstructionLockEvent(1, new[] { 42L }) } });
        var c = new CubeCraftState
        {
            Owner = new Owner(),
            ConstructionUnlockOnEnter = () => CubeCraftState.ConstructionUnlock(null, false, events.IsIgnoredConstructionLock(42)),
            Locks = new CubeLock
            {
                Housing = new Ms2.Formats.World.HousingTable(System.Xml.Linq.XDocument.Parse($"<ms2><housing id=\"{ItemId}\" interiorLevel=\"3\"/></ms2>")),
                IsFeatureEnabled = id => id == CubeLock.HousingLevelFeature,
            },
        };
        c.NewPc();
        c.ModelAttached();
        c.Set(2, 0, Cube, dead: false);
        Assert.That(c.ConstructionUnlocked, Is.True);
        var stop = Confirm(c, out _);
        Assert.That(stop, Is.Not.EqualTo(CubeCraftState.PlacementStop.Locked));
        Assert.That(stop, Is.Not.EqualTo(CubeCraftState.PlacementStop.InventoryCheckOpen));
    }
    static ResponseCubePlace Answer(byte mode, byte error, int character) => new(mode, error, character);

    [Test]
    public void APlaceAnswerForThePcHidesTheGuideAndPlaysThePlaceSound()
    {
        foreach (byte mode in new[] { ResponseCubePlace.ModePlace, ResponseCubePlace.ModeReplace })
        {
            var c = Craft(2, 0, new Owner());
            Assert.That(c.Current.GuideVisible, Is.True);
            Assert.That(c.OnPlaceAnswer(Answer(mode, 0, 9), 8, dead: false), Is.EqualTo(CubeCraftState.PlaceAnswer.None));
            Assert.That(c.OnPlaceAnswer(Answer(mode, 0, 9), -1, dead: false), Is.EqualTo(CubeCraftState.PlaceAnswer.None));
            Assert.That(c.Current.GuideVisible, Is.True);
            Assert.That(c.OnPlaceAnswer(Answer(mode, 0, 9), 9, dead: false), Is.EqualTo(CubeCraftState.PlaceAnswer.Placed));
            Assert.That(c.Current.GuideVisible, Is.False);
        }
        // only 0xa needs a character other than 0 (:9986 against :10107)
        Assert.That(Craft(2, 0, new Owner()).OnPlaceAnswer(Answer(ResponseCubePlace.ModePlace, 0, 0), 0, false), Is.EqualTo(CubeCraftState.PlaceAnswer.None));
        Assert.That(Craft(2, 0, new Owner()).OnPlaceAnswer(Answer(ResponseCubePlace.ModeReplace, 0, 0), 0, false), Is.EqualTo(CubeCraftState.PlaceAnswer.Placed));
        // no active craft while dead: the sound only; craft mode 1 keeps its guide (SetGuideVisible, cubecraft_2.cpp:660)
        var dead = Craft(2, 0, new Owner());
        Assert.That(dead.OnPlaceAnswer(Answer(ResponseCubePlace.ModePlace, 0, 9), 9, dead: true), Is.EqualTo(CubeCraftState.PlaceAnswer.Placed));
        Assert.That(dead.Current.GuideVisible, Is.True);
        var modeOne = Craft(2, 1, new Owner());
        modeOne.OnPlaceAnswer(Answer(ResponseCubePlace.ModePlace, 0, 9), 9, dead: false);
        Assert.That(modeOne.Current.GuideVisible, Is.True);
    }

    [Test]
    public void ARefusedPlaceAnswerGivesTheLiftNoticeOrTheBuildNotice()
    {
        foreach (byte mode in new[] { ResponseCubePlace.ModePlace, ResponseCubePlace.ModeReplace })
        {
            var lift = Craft(2, 2, new Owner());
            Assert.That(lift.OnPlaceAnswer(Answer(mode, 0x35, 9), 9, false), Is.EqualTo(CubeCraftState.PlaceAnswer.SameItemHeld));
            Assert.That(lift.Current.GuideVisible, Is.True);
            Assert.That(Craft(2, 0, new Owner()).OnPlaceAnswer(Answer(mode, 0x35, 9), 9, false), Is.EqualTo(CubeCraftState.PlaceAnswer.BuildNotice));
            Assert.That(Craft(2, 0, new Owner()).OnPlaceAnswer(Answer(mode, 0x35, 9), 8, false), Is.EqualTo(CubeCraftState.PlaceAnswer.None));
            Assert.That(Craft(2, 2, new Owner()).OnPlaceAnswer(Answer(mode, 0x35, 9), -1, false), Is.EqualTo(CubeCraftState.PlaceAnswer.None));
        }
        Assert.That(new CubeCraftState().OnPlaceAnswer(Answer(ResponseCubePlace.ModeReplace, 0x35, 9), 9, false), Is.EqualTo(CubeCraftState.PlaceAnswer.BuildNotice));
    }
}
