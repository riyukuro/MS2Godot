using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot.Movement;

/// <summary>
/// CubeMapInformationService's cube map for a field: every MS2CubeProp entity is a cube, keyed by its position
/// divided by 150 and truncated (FUN_1408a13d0 / FUN_1408a1430). A cube fills 150 x 150 around its position and 150
/// up from it. StateClimb and PC::FUN_14165b500 only climb walls made of cubes.
/// <para>Every lookup reads <see cref="Cells"/>, which also holds the cubes the ResponseCube place answers add (with
/// CreateCube's entity position, the cell times 150), so the climb lookups see them too.</para>
/// </summary>
public sealed class CubeGrid : global::Ms2.Ui.ICubeCells
{
    /// <summary>CubeMapInformationService's cubes by packed cell key (MakeCellKey) with their items: the map's, then the live ones.</summary>
    public readonly global::Ms2.Ui.CubeCellMap Cells = new();

    public int Count => Cells.EntityCount;
    public IEnumerable<KeyValuePair<(int X, int Y, int Z), System.Numerics.Vector3>> All => Cells.Entities;

    public static CubeGrid From(FieldMap map)
    {
        var g = new CubeGrid();
        foreach (var e in map.Entities)
            if (e.HasMixin("MS2CubeProp"))
            {
                var p = new Vector3((float)e.Position.X, (float)e.Position.Y, (float)e.Position.Z);
                // FUN_1410c1330: a cell that already holds a cube refuses the next one (the first cube placed is kept)
                g.Cells.AddMapCube(Key(p), new System.Numerics.Vector3(p.X, p.Y, p.Z));
            }
        return g;
    }

    public static (int, int, int) Key(Vector3 ms2) => ((int)(ms2.X / MoveConfig.CubeSize), (int)(ms2.Y / MoveConfig.CubeSize), (int)(ms2.Z / MoveConfig.CubeSize));

    public static Vector3 Ms2(Vector3 godot) => new(godot.X, -godot.Z, godot.Y);

    /// <summary>The cube whose box holds a point just inside a hit surface (Godot space), as its key; the stand-in for
    /// GetCubeByEntity on the ray's hit entity (<see cref="global::Ms2.Ui.CubeCellMap.CubeAt"/>).</summary>
    public (int X, int Y, int Z)? CubeAt(Vector3 godotPoint)
    {
        var p = Ms2(godotPoint);
        return Cells.CubeAt(new System.Numerics.Vector3(p.X, p.Y, p.Z));
    }

    /// <summary>FUN_1410b9080: the cube at a packed offset (x, y, z signed bytes) from another, or null.</summary>
    public (int X, int Y, int Z)? Neighbour((int X, int Y, int Z) key, int dx, int dy, int dz) => Cells.Neighbour(key, dx, dy, dz);

    /// <summary>HasCubeAtKey (@0x1410b7ed0, cubemapinformationservice_3.cpp:480-487) on <see cref="Cells"/>.</summary>
    public bool HasCubeAtKey(int key) => Cells.HasCubeAtKey(key);

    /// <summary>GetCubeByKey (@0x1410b8b00)->item on <see cref="Cells"/>.</summary>
    public bool TryCubeItemAtKey(int key, out global::Ms2.Net.Packets.CubeItemInfo item) => Cells.TryCubeItemAtKey(key, out item);

    /// <summary>The cube's entity position (MS2 axes).</summary>
    public Vector3 Position((int X, int Y, int Z) key)
    {
        var p = Cells.EntityPosition(key);
        return new Vector3(p.X, p.Y, p.Z);
    }
}
