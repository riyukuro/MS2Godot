using System.Numerics;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CubeMapInformationService's cell map with the ResponseCube place answers: FUN_1410c4850 (@0x1410c4850) and
/// FUN_1410c4e20 (@0x1410c4e20, cubemapinformationservice_3.cpp:2706-2777).</summary>
public class CubeCellMapTests
{
    const int Known = 50100001, OtherKnown = 50100002, Unknown = 50199999;
    static bool Exists(int id) => id is Known or OtherKnown;
    static CubeItemInfo Item(int id, long uid = 0) => new() { ItemId = id, InstanceId = uid, Count = 1 };

    [Test]
    public void APlacedCubeIsInItsCellWithItsItem()
    {
        var m = new CubeCellMap();
        int key = CubeCell.Key(3, -2, 1);
        var item = Item(Known, 7);
        Assert.That(m.HasCubeAtKey(key), Is.False);
        Assert.That(m.Place(key, item, Exists), Is.True);
        Assert.That(m.HasCubeAtKey(key), Is.True);
        Assert.That(m.TryCubeItemAtKey(key, out var got), Is.True);
        Assert.That(got.ItemId, Is.EqualTo(Known));
        Assert.That(got.InstanceId, Is.EqualTo(7L));
        Assert.That(m.HasCubeAtKey(CubeCell.Key(3, -2, 2)), Is.False);
    }

    [Test]
    public void APlaceOnAHeldCellKeepsTheFirstCube()
    {
        // CreateCube (:2605-2607): f98 already holding the key refuses the cube
        var m = new CubeCellMap();
        int key = CubeCell.Key(1, 1, 0);
        Assert.That(m.AddMapCube(key), Is.True);
        Assert.That(m.Place(key, Item(Known), Exists), Is.False);
        Assert.That(m.TryCubeItemAtKey(key, out var got), Is.True);
        Assert.That(got.ItemId, Is.Zero);
        Assert.That(m.Count, Is.EqualTo(1));
    }

    [Test]
    public void APlaceWithoutItemDataOrItemAddsNothing()
    {
        // FUN_1410b77e0 (:1676-1678) false without the item's data; FUN_1410c4850 (:2709) returns without an item
        var m = new CubeCellMap();
        int key = CubeCell.Key(0, 0, 0);
        Assert.That(m.Place(key, Item(Unknown), Exists), Is.False);
        Assert.That(m.Place(key, null, Exists), Is.False);
        Assert.That(m.HasCubeAtKey(key), Is.False);
        Assert.That(m.Count, Is.Zero);
    }

    [Test]
    public void AReplaceSwapsTheCellsItem()
    {
        var m = new CubeCellMap();
        int key = CubeCell.Key(2, 0, 1);
        m.AddMapCube(key);
        Assert.That(m.Replace(key, Item(Known, 4), Exists), Is.True);
        Assert.That(m.TryCubeItemAtKey(key, out var a), Is.True);
        Assert.That((a.ItemId, a.InstanceId), Is.EqualTo((Known, 4L)));
        Assert.That(m.Replace(key, Item(OtherKnown, 5), Exists), Is.True);
        Assert.That(m.TryCubeItemAtKey(key, out var b), Is.True);
        Assert.That(b.ItemId, Is.EqualTo(OtherKnown));
        Assert.That(m.Count, Is.EqualTo(1));
    }

    [Test]
    public void AReplaceOnAnEmptyCellAddsNothing()
    {
        // FUN_1410c4e20 (:2775-2776): no cube at the point, only the "Not Exist ReplaceCube" log
        var m = new CubeCellMap();
        int key = CubeCell.Key(5, 5, 5);
        Assert.That(m.Replace(key, Item(Known), Exists), Is.False);
        Assert.That(m.HasCubeAtKey(key), Is.False);
    }

    [Test]
    public void AReplaceWithAnItemWithoutDataStillRemovesTheOldCube()
    {
        // FUN_1410c1910 (:2763) runs before FUN_1410b77e0 (:2765)
        var m = new CubeCellMap();
        int key = CubeCell.Key(4, 1, 0);
        m.Place(key, Item(Known), Exists);
        Assert.That(m.Replace(key, Item(Unknown), Exists), Is.False);
        Assert.That(m.HasCubeAtKey(key), Is.False);
        Assert.That(m.Count, Is.Zero);
    }

    [Test]
    public void TheMapKeepsTheFirstMapCubeOfAKey()
    {
        var m = new CubeCellMap();
        Assert.That(m.AddMapCube(9), Is.True);
        Assert.That(m.AddMapCube(9), Is.False);
        Assert.That(m.Count, Is.EqualTo(1));
    }

    [Test]
    public void TheUnpackedCellOfAnAnswerGivesTheSameKey()
    {
        // FUN_1408a0400 (ugcmapdata_1.cpp:119-123) signed bytes, then FUN_1408a03c0 / MakeCellKey (:113-117): the low three bytes
        int packed = CubeCell.Key(-3, 4, -1);
        var (x, y, z) = CubeCell.Unpack(packed);
        Assert.That((x, y, z), Is.EqualTo((-3, 4, -1)));
        Assert.That(CubeCell.Key(x, y, z), Is.EqualTo(packed));
        var (hx, hy, hz) = CubeCell.Unpack(unchecked((int)0xff000000) | packed);
        Assert.That(CubeCell.Key(hx, hy, hz), Is.EqualTo(packed));
    }

    [Test]
    public void AnAnswerAddsItsCubeOnlyWithoutError()
    {
        // OnResponseCube 0xa / 0xf (scenestategame.cpp:9984-10011, :10102-10121): error 0 places or replaces, an error
        // answer (:10012, :10122) changes no cube; the answer's cell may carry a high byte (FUN_1408a0400 reads three)
        var m = new CubeCellMap();
        int key = CubeCell.Key(-1, 2, 0);
        var item = Item(Known);
        Assert.That(m.Apply(new ResponseCubePlace(ResponseCubePlace.ModePlace, 3, 1, key, Item: item), Exists), Is.False);
        Assert.That(m.HasCubeAtKey(key), Is.False);
        Assert.That(m.Apply(new ResponseCubePlace(ResponseCubePlace.ModeReplace, 0, 1, key, Item: item), Exists), Is.False);
        Assert.That(m.Apply(new ResponseCubePlace(ResponseCubePlace.ModePlace, 0, 1, unchecked((int)0x7f000000) | key, Item: item), Exists), Is.True);
        Assert.That(m.TryCubeItemAtKey(key, out var got) && got.ItemId == Known, Is.True);
        Assert.That(m.Apply(new ResponseCubePlace(ResponseCubePlace.ModeReplace, 0, 1, key, Item: Item(OtherKnown)), Exists), Is.True);
        Assert.That(m.TryCubeItemAtKey(key, out got) && got.ItemId == OtherKnown, Is.True);
        Assert.That(m.Apply(null, Exists), Is.False);
    }

    [Test]
    public void APlacedCubeIsFoundByTheClimbLookups()
    {
        // CreateCube's entity at ptCell (FUN_1408a15b0, ugcmapdata_1.cpp:293-297; cubemapinformationservice_3.cpp:2608-2609):
        // the cell times 150; its box is 150 x 150 around that and 150 up
        var m = new CubeCellMap();
        Assert.That(m.Place(CubeCell.Key(2, -1, 1), Item(Known), Exists), Is.True);
        Assert.That(m.EntityPosition((2, -1, 1)), Is.EqualTo(new Vector3(300, -150, 150)));
        Assert.That(m.CubeAt(new Vector3(300 + 74, -150 - 74, 150 + 10)), Is.EqualTo(((int, int, int)?)(2, -1, 1)));
        Assert.That(m.CubeAt(new Vector3(300 + 76, -150, 160)), Is.Null);
        Assert.That(m.EntityCount, Is.EqualTo(1));
    }

    [Test]
    public void TheNeighbourLookupSeesMapAndPlacedCubes()
    {
        var m = new CubeCellMap();
        Assert.That(m.AddMapCube((0, 0, 0), new Vector3(0, 0, 0)), Is.True);
        Assert.That(m.AddMapCube((0, 0, 0), new Vector3(10, 0, 0)), Is.False);
        Assert.That(m.HasCubeAtKey(CubeCell.Key(0, 0, 0)), Is.True);
        Assert.That(m.Neighbour((0, 0, 0), 0, 0, 1), Is.Null);
        Assert.That(m.Place(CubeCell.Key(0, 0, 1), Item(Known), Exists), Is.True);
        Assert.That(m.Neighbour((0, 0, 0), 0, 0, 1), Is.EqualTo(((int, int, int)?)(0, 0, 1)));
        Assert.That(m.Neighbour((0, 0, 1), 0, 0, -1), Is.EqualTo(((int, int, int)?)(0, 0, 0)));
    }

    [Test]
    public void AReplaceWithoutItemDataTakesTheCubeFromTheClimbLookups()
    {
        // FUN_1410c1910 (:5190-5216) erases the cube with its entity before FUN_1410b77e0 (:2765) refuses the new item
        var m = new CubeCellMap();
        m.AddMapCube((1, 1, 0), new Vector3(150, 150, 0));
        Assert.That(m.Replace(CubeCell.Key(1, 1, 0), Item(Unknown), Exists), Is.False);
        Assert.That(m.CubeAt(new Vector3(150, 150, 10)), Is.Null);
        Assert.That(m.Neighbour((1, 1, 1), 0, 0, -1), Is.Null);
        Assert.That(m.EntityCount, Is.Zero);
    }

    [Test]
    public void ALoadedHouseCubeReplacesTheCubeInItsCell()
    {
        // OnLoadCubes mode 0 (scenestategame.cpp:10892-10897): a cube at the point is removed (FUN_1410c1910 with 0), then
        // FUN_1410c4850 places the loaded one; a loaded item without data (FUN_1410b77e0) leaves the cell empty
        var m = new CubeCellMap();
        m.AddMapCube((1, 0, 0), new Vector3(150, 0, 0));
        m.AddMapCube((2, 0, 0), new Vector3(300, 0, 0));
        var load = new LoadCubes(0, new[]
        {
            new ResponseCubePlace(ResponseCubePlace.ModePlace, 0, 0, CubeCell.Key(1, 0, 0), Item: Item(Known, 11)),
            new ResponseCubePlace(ResponseCubePlace.ModePlace, 0, 0, CubeCell.Key(2, 0, 0), Item: Item(Unknown)),
            new ResponseCubePlace(ResponseCubePlace.ModePlace, 0, 0, unchecked((int)0x7f000000) | CubeCell.Key(-1, 3, 1), Item: Item(OtherKnown)),
        });
        Assert.That(m.Load(load, Exists), Is.EqualTo(2));
        Assert.That(m.TryCubeItemAtKey(CubeCell.Key(1, 0, 0), out var a) && a.ItemId == Known && a.InstanceId == 11, Is.True);
        Assert.That(m.EntityPosition((1, 0, 0)), Is.EqualTo(new Vector3(150, 0, 0)));
        Assert.That(m.HasCubeAtKey(CubeCell.Key(2, 0, 0)), Is.False);
        Assert.That(m.CubeAt(new Vector3(300, 0, 10)), Is.Null);
        Assert.That(m.TryCubeItemAtKey(CubeCell.Key(-1, 3, 1), out var c) && c.ItemId == OtherKnown, Is.True);
        Assert.That(m.Load(null, Exists), Is.Zero);
    }

    [Test]
    public void AMapCubePastASignedByteCellLosesItsEntityOnAReplaceOrALoad()
    {
        // FUN_1410c1910 (:5190-5216) erases the cube with its own entity: a map cube at x 200 has key 0xc8, which unpacks to
        // x -56, so the entity must leave by the cell it was added with
        var m = new CubeCellMap();
        m.AddMapCube((200, 0, 0), new Vector3(30000, 0, 0));
        Assert.That(m.Replace(CubeCell.Key(200, 0, 0), Item(Unknown), Exists), Is.False);
        Assert.That(m.CubeAt(new Vector3(30000, 0, 10)), Is.Null);
        Assert.That(m.EntityCount, Is.Zero);

        m.AddMapCube((0, -200, 0), new Vector3(0, -30000, 0));
        var load = new LoadCubes(0, new[] { new ResponseCubePlace(ResponseCubePlace.ModePlace, 0, 0, CubeCell.Key(0, -200, 0), Item: Item(Unknown)) });
        Assert.That(m.Load(load, Exists), Is.Zero);
        Assert.That(m.CubeAt(new Vector3(0, -30000, 10)), Is.Null);
        Assert.That(m.EntityCount, Is.Zero);
    }

    [Test]
    public void AMapPropWhoseKeyIsTakenKeepsItsEntityThroughACreateAndARemoveAtItsCell()
    {
        // (200, 0, 0) and (-56, 0, 0) share key 0xc8: f98 keeps the first (FUN_1410c1330), the second prop still has its
        // entity. CreateCube's entity at the key's cell (-56, 0, 0) is another entity, and removing it leaves the prop's
        var m = new CubeCellMap();
        Assert.That(m.AddMapCube((200, 0, 0), new Vector3(30000, 0, 0)), Is.True);
        Assert.That(m.AddMapCube((-56, 0, 0), new Vector3(-8400, 0, 0)), Is.True);
        Assert.That((m.Count, m.EntityCount), Is.EqualTo((1, 2)));
        int key = CubeCell.Key(200, 0, 0);
        Assert.That(key, Is.EqualTo(CubeCell.Key(-56, 0, 0)));

        Assert.That(m.Replace(key, Item(Known), Exists), Is.True);      // the (200, 0, 0) prop leaves, a cube is made at (-56, 0, 0)
        Assert.That((m.Count, m.EntityCount), Is.EqualTo((1, 2)));
        Assert.That(m.CubeAt(new Vector3(30000, 0, 10)), Is.Null);
        Assert.That(m.EntityPosition((-56, 0, 0)), Is.EqualTo(new Vector3(-8400, 0, 0)));

        Assert.That(m.Replace(key, Item(Unknown), Exists), Is.False);   // the created cube leaves; nothing new is made
        Assert.That((m.Count, m.EntityCount), Is.EqualTo((0, 1)));
        Assert.That(m.CubeAt(new Vector3(-8400, 0, 10)), Is.EqualTo(((int, int, int)?)(-56, 0, 0)));
        Assert.That(m.EntityPosition((-56, 0, 0)), Is.EqualTo(new Vector3(-8400, 0, 0)));
    }
}
