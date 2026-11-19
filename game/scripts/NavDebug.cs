using System.Threading.Tasks;
using Godot;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Navigation;

namespace Ms2Godot;

/// <summary>
/// The field's navmesh: loaded at field load in a worker thread (built on first use and cached under user://navmesh), for
/// the server's position test (ValidPosition) and the debug draw (F9 toggles; --nav starts it on). The mesh comes from
/// Ms2.Navigation. Navmesh units are metres with Y up; the game's world is 100 times that with the same axes, so a vertex
/// is drawn at 100 x its navmesh position.
/// </summary>
public partial class NavDebug : Node3D
{
    PackSet packs; AssetIndex assets; NifSpec spec; FlatLibrary flats; string xblock;
    Task<FieldNavigation> loading;
    readonly System.Threading.CancellationTokenSource cancel = new();
    MeshInstance3D drawn;
    public FieldNavigation Navigation { get; private set; }

    public static NavDebug Start(Node parent, PackSet packs, AssetIndex assets, NifSpec spec, FlatLibrary flats, string xblock, bool on)
    {
        var n = new NavDebug { Name = "NavDebug", packs = packs, assets = assets, spec = spec, flats = flats, xblock = xblock, Visible = false };
        parent.AddChild(n);
        var dir = ProjectSettings.GlobalizePath("user://navmesh");
        var cancel = n.cancel.Token;
        n.loading = Task.Run(() => FieldNavigation.Load(packs, assets, spec, flats, xblock, dir, cancel), cancel);
        if (on) n.Toggle();
        return n;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F9 }) Toggle();
    }

    public void Toggle() => Visible = !Visible;

    // the field is gone: a build not yet in Recast is dropped
    public override void _ExitTree() => cancel.Cancel();

    /// <summary>The load finished: the navmesh, or none (no navigation geometry, or the load failed).</summary>
    public bool Loaded => loading.IsCompleted;

    /// <summary>
    /// kms2525 FieldManager.ValidPosition (FieldManager.cs:373-398): FindNearestPoly around the point with extents (2, 4, 2)
    /// and the crowd's filter 0 (Navigation.cs:28-32: include all flags, exclude DISABLED; FieldNavigation's filter) finds
    /// a polygon. Field units. False while there is no navmesh: the server has none to find a polygon on either.
    /// </summary>
    public bool ValidPosition(Vector3 p)
    {
        if (!loading.IsCompletedSuccessfully || loading.Result == null) return false;
        return loading.Result.ValidPosition(p.X, p.Y, p.Z);
    }

    public override void _Process(double delta)
    {
        if (!Visible || !loading.IsCompleted || drawn != null) return;
        if (loading.IsCanceled) return;
        if (loading.IsFaulted) { GD.PushWarning("navmesh: " + loading.Exception?.GetBaseException().Message); drawn = new MeshInstance3D(); AddChild(drawn); return; }
        Navigation = loading.Result;
        drawn = new MeshInstance3D { Name = "NavMesh" };
        AddChild(drawn);
        if (Navigation == null) { GD.Print($"navmesh {xblock}: no navigation geometry"); return; }
        drawn.Mesh = Build(Navigation.Mesh);
    }

    static ArrayMesh Build(DotRecast.Detour.DtNavMesh nav)
    {
        var fill = new List<Vector3>(); var line = new List<Vector3>();
        for (int t = 0; t < nav.GetMaxTiles(); t++)
        {
            var tile = nav.GetTile(t);
            var d = tile?.data;
            if (d == null) continue;
            foreach (var poly in d.polys)
            {
                if (poly.GetPolyType() != DotRecast.Detour.DtPolyTypes.DT_POLYTYPE_GROUND) continue;
                Vector3 V(int i) => new(d.verts[poly.verts[i] * 3] * 100, d.verts[poly.verts[i] * 3 + 1] * 100 + 6, d.verts[poly.verts[i] * 3 + 2] * 100);
                for (int i = 2; i < poly.vertCount; i++) { fill.Add(V(0)); fill.Add(V(i - 1)); fill.Add(V(i)); }
                for (int i = 0; i < poly.vertCount; i++) { line.Add(V(i)); line.Add(V((i + 1) % poly.vertCount)); }
            }
        }
        var mesh = new ArrayMesh();
        void Surface(List<Vector3> v, Mesh.PrimitiveType type, Color c)
        {
            if (v.Count == 0) return;
            var arr = new Godot.Collections.Array(); arr.Resize((int)Mesh.ArrayType.Max);
            arr[(int)Mesh.ArrayType.Vertex] = v.ToArray();
            mesh.AddSurfaceFromArrays(type, arr);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = c.LinearToSrgb(), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                Transparency = c.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
                NoDepthTest = false,
            });
        }
        Surface(fill, Mesh.PrimitiveType.Triangles, new Color(0.1f, 0.7f, 1f, 0.28f));
        Surface(line, Mesh.PrimitiveType.Lines, new Color(0.9f, 1f, 1f, 0.9f));
        return mesh;
    }
}
