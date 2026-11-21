using System.Numerics;
using Ms2.Ui.Skills;

namespace Ms2.Ui;

/// <summary>The cube map's cell math (CubeMapInformationService, cubemapinformationservice_3.cpp; ugcmapdata_1.cpp).</summary>
public static class CubeCell
{
    /// <summary>The cube edge.</summary>
    public const float Size = 150f;

    /// <summary>FUN_1408a1430 (@0x1408a1430, ugcmapdata_1.cpp:275-279): each axis divided by 150 and truncated.</summary>
    public static (int X, int Y, int Z) Of(Vector3 p) => ((int)(p.X / Size), (int)(p.Y / Size), (int)(p.Z / Size));

    /// <summary>MakeCellKey (ugcmapdata_1.cpp:113): the low byte of each axis, x | y &lt;&lt; 8 | z &lt;&lt; 16.</summary>
    public static int Key(int x, int y, int z) => (x & 0xff) + ((y & 0xff) << 8) + ((z & 0xff) << 16);

    /// <summary>FUN_1408a13d0 (@0x1408a13d0, ugcmapdata_1.cpp:272): the cell key of a point.</summary>
    public static int Key(Vector3 p)
    {
        var c = Of(p);
        return Key(c.X, c.Y, c.Z);
    }

    /// <summary>GetCellPosition (@0x1410b7a20, cubemapinformationservice_3.cpp:431-438): the cell's corner, each axis the
    /// cell times 150.</summary>
    public static Vector3 Position(Vector3 p)
    {
        var c = Of(p);
        return new Vector3(c.X * Size, c.Y * Size, c.Z * Size);
    }

    /// <summary>
    /// GetCellInFrontOfActor (@0x1410b7ad0, :440-443) through FUN_1410b7ba0 (@0x1410b7ba0, :445-459): the point
    /// <paramref name="snap"/> cells ahead along the facing (FUN_140a01250's forward), moved by half a cell on x and y and
    /// by <paramref name="dist"/> on z; a negative x or y goes one more cell down, so the truncation lands on the cell
    /// below.
    /// </summary>
    public static Vector3 InFrontOf(Vector3 from, float rotZDegrees, float dist, int snap)
    {
        SkillHit.Axes(rotZDegrees, out var forward, out _);
        var p = from + forward * (float)snap * Size;
        p.X += 75f;
        p.Y += 75f;
        p.Z += dist;
        if (p.X < 0f) p.X -= Size;
        if (p.Y < 0f) p.Y -= Size;
        return p;
    }
}

/// <summary>The cube map's cubes, by cell key (CubeMapInformationService::HasCubeAtKey @0x1410b7ed0).</summary>
public interface ICubeCells
{
    bool HasCubeAtKey(int key);

    /// <summary>GetCubeByKey (@0x1410b8b00, cubemapinformationservice_3.cpp:617-624)->item: the item of the cube at the key.
    /// False when the map does not know it (the caller's branch on it stays STILL-OPEN).</summary>
    bool TryCubeItemAtKey(int key, out Ms2.Net.Packets.CubeItemInfo item)
    {
        item = null;
        return false;
    }
}

/// <summary>PutDownLiftableAction (key 0xc, actionkeyobject.cpp:1970-2015), in the cube-mode action map.</summary>
public static class PutDownLiftable
{
    /// <summary>
    /// vfunc_1 (@0x14095aa70, :1972-1997): applies while the pc's craft holds a lift (PC::FUN_141659690, pc.cpp:1854) and
    /// it stands in state 1; else when the cell in front of it (one cell ahead, GetCellInFrontOfActor with 0 and 1) has no
    /// cube (HasCubeAtPoint, :475) and is the cell of a liftable target box (GetLiftableTargetBoxes, in the map's order).
    /// <paramref name="from"/> and <paramref name="rotZDegrees"/> are the ridee's when riding (PCBase::FUN_1416767d0,
    /// GetRideeAwareRotation).
    /// </summary>
    public static bool Poll(CubeCraftState craft, int stateId, Vector3 from, float rotZDegrees, ICubeCells cubes,
        LiftableTargetBoxes targets)
    {
        if (craft.Current is { HoldsLift: true } && stateId == 1) return true;
        var cell = CubeCell.Position(CubeCell.InFrontOf(from, rotZDegrees, 0f, 1));
        if (cubes != null && cubes.HasCubeAtKey(CubeCell.Key(cell))) return false;
        var all = targets.All;
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i].Position;
            if (CubeCell.Position(new Vector3((float)p.X, (float)p.Y, (float)p.Z)) == cell) return true;
        }
        return false;
    }
}
