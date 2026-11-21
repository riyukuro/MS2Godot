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
}
