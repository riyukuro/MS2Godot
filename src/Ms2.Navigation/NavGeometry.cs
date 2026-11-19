// Adapted from the server fork's navmesh mapper (Maple2.File.Ingest NavMeshMapper, AGPL-3.0-or-later): which entities
// make navigation geometry (doesMakeTOK), the generated cube, the fluid areas and the underside-face duplicates.
using DotRecast.Recast;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Navigation;

/// <summary>Triangle soup of one field in navmesh space (Y up, 1 unit = 1 m), with a per-triangle area.</summary>
public sealed class NavGeometry
{
    public readonly List<float> Verts = new();
    public readonly List<int> Tris = new();
    public readonly List<int> Areas = new();
    public int Entities, Shapes;

    public bool Empty => Verts.Count == 0 || Tris.Count == 0;

    // the 150-unit generated cube: x/y +-75, z 0..150 (CubeMapInformationService::GeneratePhysX)
    static readonly int[] CubeIdx =
    {
        1, 4, 5, 4, 6, 7, 7, 5, 4, 2, 5, 7, 6, 4, 1, 3, 7, 6, 5, 2, 1, 7, 3, 2, 2, 3, 0, 1, 0, 6, 6, 0, 3, 0, 1, 2,
    };

    static (double x, double y, double z)[] CubeCorners(double sx, double sy, double sz, double oz) => new[]
    {
        (-sx / 2, sy / 2, oz), (-sx / 2, -sy / 2, oz), (-sx / 2, -sy / 2, oz + sz), (-sx / 2, sy / 2, oz + sz),
        (sx / 2, -sy / 2, oz), (sx / 2, -sy / 2, oz + sz), (sx / 2, sy / 2, oz), (sx / 2, sy / 2, oz + sz),
    };

    void AddBox(Xform xf, double sx, double sy, double sz, double oz, int area)
    {
        int b = Verts.Count / 3;
        foreach (var c in CubeCorners(sx, sy, sz, oz))
        {
            var p = xf.Apply(new Vec3(c.x, c.y, c.z));
            var n = NavConfig.ToNav(p.X, p.Y, p.Z);
            Verts.Add(n.X); Verts.Add(n.Y); Verts.Add(n.Z);
        }
        foreach (int i in CubeIdx) Tris.Add(b + i);
        for (int i = 0; i < CubeIdx.Length / 3; i++) Areas.Add(area);
        Shapes++;
    }

    static readonly int Water = DotRecast.Recast.Toolset.Builder.SampleAreaModifications.SAMPLE_POLYAREA_TYPE_WATER;

    void AddMesh(Xform xf, CollisionMesh cm, bool fluid)
    {
        int b = Verts.Count / 3;
        int nv = cm.Vertices.Length / 3;
        var nav = new (float x, float y, float z)[nv];
        for (int i = 0; i < nv; i++)
        {
            var p = xf.Apply(new Vec3(cm.Vertices[i * 3], cm.Vertices[i * 3 + 1], cm.Vertices[i * 3 + 2]));
            var n = NavConfig.ToNav(p.X, p.Y, p.Z);
            nav[i] = (n.X, n.Y, n.Z);
            Verts.Add(n.X); Verts.Add(n.Y); Verts.Add(n.Z);
        }
        var extra = new List<(float x, float y, float z)>();
        for (int t = 0; t < cm.Triangles.Length; t += 3)
        {
            int a = cm.Triangles[t], bb = cm.Triangles[t + 1], c = cm.Triangles[t + 2];
            Tris.Add(b + a); Tris.Add(b + bb); Tris.Add(b + c);
            Areas.Add(fluid ? Water : 0);
            // faces that look down get a copy 0.125 higher, so the top of a block with another block on it is not walkable
            var v0 = nav[a]; var v1 = nav[bb]; var v2 = nav[c];
            float ux = v1.x - v0.x, uy = v1.y - v0.y, uz = v1.z - v0.z, vx = v2.x - v0.x, vy = v2.y - v0.y, vz = v2.z - v0.z;
            float ny = uz * vx - ux * vz;
            float len = MathF.Sqrt((uy * vz - uz * vy) * (uy * vz - uz * vy) + ny * ny + (ux * vy - uy * vx) * (ux * vy - uy * vx));
            if (len > 0 && ny / len < 0)
            {
                extra.Add((v0.x, v0.y + 0.125f, v0.z)); extra.Add((v1.x, v1.y + 0.125f, v1.z)); extra.Add((v2.x, v2.y + 0.125f, v2.z));
            }
        }
        int eb = Verts.Count / 3;
        foreach (var e in extra) { Verts.Add(e.x); Verts.Add(e.y); Verts.Add(e.z); }
        for (int i = 0; i < extra.Count; i += 3)
        {
            Tris.Add(eb + i); Tris.Add(eb + i + 1); Tris.Add(eb + i + 2);
            Areas.Add(fluid ? Water : 0);
        }
        Shapes++;
    }

    /// <summary>Collects the geometry of every entity that has doesMakeTOK set.</summary>
    public static NavGeometry Build(PackSet packs, AssetIndex assets, NifSpec spec, FieldMap map)
    {
        var g = new NavGeometry();
        var nifs = new Dictionary<string, List<CollisionMesh>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in map.Entities)
        {
            if (!string.Equals(e.Get("doesMakeTOK"), "true", StringComparison.OrdinalIgnoreCase)) continue;
            g.Entities++;
            var xf = e.Transform;
            bool fluid = e.Get("CubeType") == "Fluid";
            if (e.ModelName is "PhysXWhitebox" or "MS2PhysXWhitebox")
            {
                var d = e.Vector("ShapeDimensions");
                if (d.X > 0 && d.Y > 0 && d.Z > 0) g.AddBox(xf, d.X, d.Y, d.Z, -d.Z / 2, 0);
                continue;
            }
            bool generate = e.Bool("GeneratePhysX");
            // FUN_1410cf160 (cubemapinformationservice_3.cpp:4026-4052) runs FUN_1410c5570, the GeneratePhysX box, for an
            // MS2CubeProp and for any other MS2PhysXProp (0xc9cd2193; MS2CubeProp contains it), but not for an MS2SalePost
            // (0xf01a7608), which it tests first
            if (generate && !e.HasMixin("MS2SalePost") && (e.HasMixin("MS2CubeProp") || e.HasMixin("MS2PhysXProp")))
            {
                var d = e.Vector("GeneratePhysXDimension");
                if (d.X == 0 && d.Y == 0 && d.Z == 0) d = new Vec3(150, 150, 150);
                g.AddBox(xf, d.X, d.Y, d.Z, 0, fluid ? Water : 0);
            }
            // PropService::HandleSceneGraphAddedMessage (PropService.cpp:460-465, exe 0x142062560) returns before
            // AddPhysXPropToPropData for an entity whose GeneratePhysX (0x38141815) is set, whatever its model: such an
            // entity gets no NIF PhysX prop, so its only geometry is the box above. A water volume without GeneratePhysX
            // keeps its NIF meshes
            if (generate) continue;
            var llid = e.Get("NifAsset");
            if (llid == null || !llid.StartsWith("urn:llid:")) continue;
            if (!nifs.TryGetValue(llid, out var meshes))
            {
                meshes = new List<CollisionMesh>();
                var (archive, entry) = assets.Resolve(llid);
                if (archive != null && packs.Exists(archive, entry))
                {
                    try { meshes = PhysXCollision.Load(NifFile.Load(spec, packs.Read(archive, entry))); }
                    catch { meshes = new List<CollisionMesh>(); }
                }
                nifs[llid] = meshes;
            }
            foreach (var cm in meshes) g.AddMesh(xf, cm, fluid);
        }
        return g;
    }
}
