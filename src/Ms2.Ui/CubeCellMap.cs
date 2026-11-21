using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// CubeMapInformationService's cubes by cell key (f98, cubemapinformationservice_3.cpp), with each cube's item: the map's
/// cubes (FUN_1410c1330 @0x1410c1330, :3984-4016) and the cubes the ResponseCube place answers create
/// (<see cref="Place"/>, <see cref="Replace"/>).
/// <para>A placed cube enters f98 only when its entity is made (FUN_1410cf5c0 @0x1410cf5c0, :5643-5716, through
/// FUN_1410c06b0 @0x1410c06b0, :5077-5088); ours makes no entity, so it enters at once, as if CreateCube's entity
/// (EntityWorldManager FUN_1411ce7c0) was made. STILL-OPEN: the removal timers (f1f8) that HasCubeAtKey and GetCubeByKey
/// skip, and the cells of the map's 0xe34a6afd entities (f2e0) that send a place to FUN_1410bca70 instead.</para>
/// </summary>
public sealed class CubeCellMap : ICubeCells
{
    readonly Dictionary<int, CubeItemInfo> cubes = new();

    /// <summary>A map cube's item: FUN_1410cf160 (@0x1410cf160, :4045) passes no CubeItemInfo, so FUN_1410c1330's
    /// CopyFrom(0) leaves it cleared (item id 0, no look; ugcmapoptions.cpp:103-127, CopyFrom and Clear). Shared, read only.</summary>
    static readonly CubeItemInfo MapCubeItem = new();

    public int Count => cubes.Count;

    /// <summary>FUN_1410c1330: a map cube at the key; a key already held refuses it (false).</summary>
    public bool AddMapCube(int key) => cubes.TryAdd(key, MapCubeItem);

    /// <summary>HasCubeAtKey (@0x1410b7ed0, :480-487): a cube is at the key.</summary>
    public bool HasCubeAtKey(int key) => cubes.ContainsKey(key);

    /// <summary>GetCubeByKey (@0x1410b8b00, :617-624)->item.</summary>
    public bool TryCubeItemAtKey(int key, out CubeItemInfo item) => cubes.TryGetValue(key, out item);

    /// <summary>
    /// CSceneStateGame::OnResponseCube 0xa and 0xf with error 0 (scenestategame.cpp:9984-10011, :10102-10121): the answer's
    /// cell (FUN_1408a0400, then FUN_1408a03c0 in the service) to <see cref="Place"/> or <see cref="Replace"/>. An error
    /// answer changes no cube. True when a cube was added.
    /// </summary>
    public bool Apply(ResponseCubePlace m, Func<int, bool> itemExists)
    {
        if (m == null || m.Error != 0) return false;
        var (x, y, z) = CubeCell.Unpack(m.Cell);
        int key = CubeCell.Key(x, y, z);
        if (m.Mode == ResponseCubePlace.ModePlace) return Place(key, m.Item, itemExists);
        if (m.Mode == ResponseCubePlace.ModeReplace) return Replace(key, m.Item, itemExists);
        return false;
    }

    /// <summary>
    /// FUN_1410c4850 (@0x1410c4850, :2706-2726), ResponseCube 0xa with error 0: nothing without an item; else, when the
    /// item data table has the item (FUN_1410b77e0 @0x1410b77e0, :1674-1700), CreateCube (:2599-2703), which refuses a key
    /// f98 already holds and an item without data. True when a cube was added.
    /// </summary>
    public bool Place(int key, CubeItemInfo item, Func<int, bool> itemExists)
    {
        if (item == null) return false;
        if (!itemExists(item.ItemId)) return false;
        return Create(key, item, itemExists);
    }

    /// <summary>
    /// FUN_1410c4e20 (@0x1410c4e20, :2750-2777), ResponseCube 0xf with error 0: nothing without an item or without a cube
    /// at the cell (GetCubeAtPoint; the client logs "Not Exist ReplaceCube"); else the cube is removed (FUN_1410c1910 with
    /// 1, :5190-5216: f98 erase) and, when the item data table has the new item, CreateCube adds it. True when a cube was
    /// added.
    /// </summary>
    public bool Replace(int key, CubeItemInfo item, Func<int, bool> itemExists)
    {
        if (item == null) return false;
        if (!cubes.ContainsKey(key)) return false;
        cubes.Remove(key);
        if (!itemExists(item.ItemId)) return false;
        return Create(key, item, itemExists);
    }

    /// <summary>CreateCube's checks (:2604-2617), then the cube with its item (spCube->item->CopyFrom(pInfo)).</summary>
    bool Create(int key, CubeItemInfo item, Func<int, bool> itemExists)
    {
        if (cubes.ContainsKey(key)) return false;
        if (!itemExists(item.ItemId)) return false;
        cubes[key] = item;
        return true;
    }
}
