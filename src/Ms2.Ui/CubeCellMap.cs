using System.Numerics;
using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// CubeMapInformationService's cubes by cell key (f98, cubemapinformationservice_3.cpp), with each cube's item: the map's
/// cubes (FUN_1410c1330 @0x1410c1330, :3984-4016) and the cubes the ResponseCube place answers create
/// (<see cref="Place"/>, <see cref="Replace"/>).
/// <para>Each cube's entity position is kept by cell for the climb lookups (<see cref="CubeAt"/>, <see cref="Neighbour"/>,
/// the stand-ins for GetCubeByEntity and FUN_1410b9080): a map cube's is its MS2CubeProp entity's, a placed cube's is
/// CreateCube's ptCell, the cell times 150 on each axis (FUN_1408a15b0 @0x1408a15b0, ugcmapdata_1.cpp:293-297;
/// cubemapinformationservice_3.cpp:2608-2609, made at :2634-2665).</para>
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

    // cell -> entity position (MS2 axes). The map's MS2CubeProp entities and the entities CreateCube makes are separate
    // entities in the client, so a created cube never replaces a map prop's entity at its cell, and removing it leaves the prop's
    readonly Dictionary<(int X, int Y, int Z), Vector3> mapEntities = new();
    readonly Dictionary<(int X, int Y, int Z), Vector3> cubeEntities = new();
    // key -> its entity's cell and table, beside f98: a map cube's cell can lie past the key's signed byte (a cube at x 200
    // has key 0xc8, which unpacks to -56), so the cube leaves with the entity it was added with, not one found from the key.
    // A map prop whose key f98 already holds has no row: it keeps its entity
    readonly Dictionary<int, ((int X, int Y, int Z) Cell, bool Map)> entityCells = new();

    /// <summary>The plot tables LoadCubes modes 1-3 fill, kept with the field's cubes.</summary>
    public readonly CubePlots Plots = new();

    public int Count => cubes.Count;

    /// <summary>The cubes with an entity position (the climb lookups' cubes).</summary>
    public int EntityCount => mapEntities.Count + cubeEntities.Count;
    public IEnumerable<KeyValuePair<(int X, int Y, int Z), Vector3>> Entities => mapEntities.Concat(cubeEntities);

    /// <summary>FUN_1410c1330: a map cube at the key; a key already held refuses it (false).</summary>
    public bool AddMapCube(int key) => cubes.TryAdd(key, MapCubeItem);

    /// <summary>A map MS2CubeProp entity at <paramref name="position"/> in its cell (FUN_1408a1430 of the position): the
    /// first cube of a cell is kept, a later one refused (false).</summary>
    public bool AddMapCube((int X, int Y, int Z) cell, Vector3 position)
    {
        if (!mapEntities.TryAdd(cell, position)) return false;
        int key = CubeCell.Key(cell.X, cell.Y, cell.Z);
        if (AddMapCube(key)) entityCells[key] = (cell, true);
        return true;
    }

    /// <summary>The cube whose box holds <paramref name="ms2Point"/> (a point just inside a hit surface, MS2 axes), as its
    /// cell: the stand-in for GetCubeByEntity on the ray's hit entity. A cube fills 150 x 150 around its position and 150 up
    /// from it.</summary>
    public (int X, int Y, int Z)? CubeAt(Vector3 ms2Point)
    {
        var k = CubeCell.Of(ms2Point);
        const float h = CubeCell.Size / 2;
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            var cell = (k.X + dx, k.Y + dy, k.Z + dz);
            if (mapEntities.TryGetValue(cell, out var c) && Holds(c, ms2Point, h)) return cell;
            if (cubeEntities.TryGetValue(cell, out c) && Holds(c, ms2Point, h)) return cell;
        }
        return null;
    }

    static bool Holds(Vector3 c, Vector3 p, float h)
        => MathF.Abs(p.X - c.X) <= h && MathF.Abs(p.Y - c.Y) <= h && p.Z >= c.Z && p.Z <= c.Z + CubeCell.Size;

    /// <summary>FUN_1410b9080: the cube at a cell offset (x, y, z signed bytes) from another, or null.</summary>
    public (int X, int Y, int Z)? Neighbour((int X, int Y, int Z) cell, int dx, int dy, int dz)
    {
        var n = (cell.X + dx, cell.Y + dy, cell.Z + dz);
        return mapEntities.ContainsKey(n) || cubeEntities.ContainsKey(n) ? n : null;
    }

    /// <summary>The cube's entity position (MS2 axes). STILL-OPEN: a cell with both a map prop and a created cube gives the
    /// map prop's (the client's ray hits one entity; the lookups here go by cell).</summary>
    public Vector3 EntityPosition((int X, int Y, int Z) cell) => mapEntities.TryGetValue(cell, out var p) ? p : cubeEntities[cell];

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
    /// 1, :5190-5216: f98 erase, with its entity) and, when the item data table has the new item, CreateCube adds it. True when a cube was
    /// added.
    /// </summary>
    public bool Replace(int key, CubeItemInfo item, Func<int, bool> itemExists)
    {
        if (item == null) return false;
        if (!cubes.ContainsKey(key)) return false;
        Remove(key);
        if (!itemExists(item.ItemId)) return false;
        return Create(key, item, itemExists);
    }

    /// <summary>
    /// CSceneStateGame::OnResponseCube 0xc with error 0 (scenestategame.cpp:10037-10054): the answer's cell (FUN_1408a0400)
    /// to <see cref="RemoveCube"/>. An error answer removes nothing. True when a cube left.
    /// </summary>
    public bool Apply(ResponseCubeRemove m)
    {
        if (m == null || m.Error != 0) return false;
        var (x, y, z) = CubeCell.Unpack(m.Cell);
        return RemoveCube(CubeCell.Key(x, y, z));
    }

    /// <summary>
    /// FUN_1410c4b50 (@0x1410c4b50, cubemapinformationservice_3.cpp:2728-2748): the cube at the key leaves f98 with its entity
    /// (FUN_1410c1910, :5190-5216; with the keep byte directly, without it through FUN_1410c2610, :1726-1747; both the same here). True when a
    /// cube was at the key.
    /// STILL-OPEN: the housing set and inventory counts a removed cube changes first (FUN_1410bec60, FUN_1410d4cb0 and
    /// FUN_1410d54f0), and FUN_1410c2610's rebuild of a cell the map's f2b8 cells hold (FUN_1410be0c0), which ours does not keep.
    /// </summary>
    public bool RemoveCube(int key)
    {
        if (!cubes.ContainsKey(key)) return false;
        Remove(key);
        return true;
    }

    /// <summary>
    /// CSceneStateGame::OnLoadCubes mode 0 (@0x141728110, scenestategame.cpp:10857-10897): each loaded cube's cell
    /// (FUN_1408a0400, then FUN_1408a15b0 to a world point and HasCubeAtPoint's FUN_1408a13d0 back to its key) first loses a
    /// cube already there (FUN_1410c1910 with 0) unless FUN_1410b7da0 finds the cell among the map's 0xe34a6afd entities
    /// (f2e0, STILL-OPEN here as in <see cref="Place"/>, so never); then FUN_1410c4850 places it as ResponseCube 0xa does
    /// (<see cref="Place"/>). The number of cubes added.
    /// STILL-OPEN: the floor state (FUN_1410c3f50: f1dc and GenerateFloorPhysX).
    /// </summary>
    public int Load(LoadCubes m, Func<int, bool> itemExists)
    {
        if (m == null) return 0;
        int added = 0;
        foreach (var c in m.Cubes)
        {
            var (x, y, z) = CubeCell.Unpack(c.Cell);
            int key = CubeCell.Key(x, y, z);
            if (HasCubeAtKey(key)) Remove(key);
            if (Place(key, c.Item, itemExists)) added++;
        }
        return added;
    }

    /// <summary>FUN_1410c1910 (@0x1410c1910, :5190-5216): the cube at the key leaves f98, with its entity.</summary>
    void Remove(int key)
    {
        cubes.Remove(key);
        if (entityCells.Remove(key, out var e)) (e.Map ? mapEntities : cubeEntities).Remove(e.Cell);
    }

    /// <summary>CreateCube's checks (:2604-2617), then the cube with its item (spCube->item->CopyFrom(pInfo)) and its entity
    /// at ptCell (FUN_1408a15b0, the cell times 150).</summary>
    bool Create(int key, CubeItemInfo item, Func<int, bool> itemExists)
    {
        if (cubes.ContainsKey(key)) return false;
        if (!itemExists(item.ItemId)) return false;
        cubes[key] = item;
        var cell = CubeCell.Unpack(key);
        cubeEntities[cell] = new Vector3(cell.X * CubeCell.Size, cell.Y * CubeCell.Size, cell.Z * CubeCell.Size);
        entityCells[key] = (cell, false);
        return true;
    }
}
