using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot.Movement;

/// <summary>
/// CubeMapInformationService's cube map for a field: every MS2CubeProp entity is a cube, keyed by its position
/// divided by 150 and truncated (FUN_1408a13d0 / FUN_1408a1430). A cube fills 150 x 150 around its position and 150
/// up from it. StateClimb and PC::FUN_14165b500 only climb walls made of cubes.
/// </summary>
public sealed class CubeGrid : global::Ms2.Ui.ICubeCells
{
    readonly Dictionary<(int, int, int), Vector3> cubes = new();   // key -> entity position (MS2 axes)
    readonly HashSet<int> keys = new();                              // the packed cell keys (MakeCellKey)

    public int Count => cubes.Count;
    public IEnumerable<KeyValuePair<(int, int, int), Vector3>> All => cubes;

    public static CubeGrid From(FieldMap map)
    {
        var g = new CubeGrid();
        foreach (var e in map.Entities)
            if (e.HasMixin("MS2CubeProp"))
            {
                var p = new Vector3((float)e.Position.X, (float)e.Position.Y, (float)e.Position.Z);
                // FUN_1410c1330: a cell that already holds a cube refuses the next one (the first cube placed is kept)
                var k = Key(p);
                if (g.cubes.TryAdd(k, p)) g.keys.Add(global::Ms2.Ui.CubeCell.Key(k.Item1, k.Item2, k.Item3));
            }
        return g;
    }

    public static (int, int, int) Key(Vector3 ms2) => ((int)(ms2.X / MoveConfig.CubeSize), (int)(ms2.Y / MoveConfig.CubeSize), (int)(ms2.Z / MoveConfig.CubeSize));

    public static Vector3 Ms2(Vector3 godot) => new(godot.X, -godot.Z, godot.Y);

    /// <summary>The cube whose box holds a point just inside a hit surface (Godot space), as its key; the stand-in for
    /// GetCubeByEntity on the ray's hit entity.</summary>
    public (int, int, int)? CubeAt(Vector3 godotPoint)
    {
        var p = Ms2(godotPoint);
        var k = Key(p);
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            var key = (k.Item1 + dx, k.Item2 + dy, k.Item3 + dz);
            if (!cubes.TryGetValue(key, out var c)) continue;
            float h = MoveConfig.CubeSize / 2;
            if (Mathf.Abs(p.X - c.X) <= h && Mathf.Abs(p.Y - c.Y) <= h && p.Z >= c.Z && p.Z <= c.Z + MoveConfig.CubeSize) return key;
        }
        return null;
    }

    /// <summary>FUN_1410b9080: the cube at a packed offset (x, y, z signed bytes) from another, or null.</summary>
    public (int, int, int)? Neighbour((int, int, int) key, int dx, int dy, int dz)
    {
        var n = (key.Item1 + dx, key.Item2 + dy, key.Item3 + dz);
        return cubes.ContainsKey(n) ? n : null;
    }

    /// <summary>HasCubeAtKey (@0x1410b7ed0, cubemapinformationservice_3.cpp:480-487): a cube is at the packed cell key.
    /// STILL-OPEN: the cubes whose removal timer runs (f1f8) count as none there; cubes placed live are not added.</summary>
    public bool HasCubeAtKey(int key) => keys.Contains(key);

    /// <summary>A map cube's item: FUN_1410cf160 (cubemapinformationservice_3.cpp:4045) makes it with no CubeItemInfo, so
    /// FUN_1410c1330's CopyFrom(0) leaves it cleared (item id 0, no look; ugcmapoptions.cpp:103-127, CopyFrom and Clear). Shared, read only.</summary>
    static readonly global::Ms2.Net.Packets.CubeItemInfo MapCubeItem = new();

    /// <summary>GetCubeByKey's item for a map cube (<see cref="MapCubeItem"/>).</summary>
    public bool TryCubeItemAtKey(int key, out global::Ms2.Net.Packets.CubeItemInfo item)
    {
        item = keys.Contains(key) ? MapCubeItem : null;
        return item != null;
    }

    /// <summary>The cube's entity position (MS2 axes).</summary>
    public Vector3 Position((int, int, int) key) => cubes[key];
}
