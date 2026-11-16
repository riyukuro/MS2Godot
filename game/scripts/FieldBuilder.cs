using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// Builds a field from its entity set: one MultiMesh per (model, mesh) for the visible NIF entities, and static
/// colliders: each NIF's own PhysX meshes, the generated box for cubes without them, and PhysXWhitebox boxes. Everything sits under a "World" node that rotates MS2 Z-up to Godot Y-up,
/// in file units.
/// </summary>
public sealed class FieldBuilder
{
    public const string MapArchive = "Resource/Model/Map";
    const float DefaultMaterialColor = 0.7058824f;     // the flat default (= the NIF material's ambient / diffuse)
    const float ChunkSize = 1500f;                     // collider bodies per 10 x 10 cubes

    readonly PackSet packs;
    readonly AssetIndex assets;
    readonly ModelBuilder builder;

    public int Instances, MultiMeshes, Boxes, Meshes, Whiteboxes, Missing;

    sealed class ModelEntry
    {
        public ModelData Data;
        public List<(ModelMesh mesh, Transform3D local)> Meshes = new();
        public Aabb Bounds;              // union of the mesh bounds in model space
        public List<Shape3D> Collision = new();   // cooked PhysX meshes of the NIF, model space
    }

    readonly Dictionary<string, ModelEntry> models = new(StringComparer.OrdinalIgnoreCase);

    public FieldBuilder(PackSet packs, AssetIndex assets, ModelBuilder builder)
    {
        this.packs = packs; this.assets = assets; this.builder = builder;
        builder.TextureArchives = new[] { MapArchive, ModelBuilder.TextureArchive };
    }

    /// <summary>MS2 (x, y, z) -> Godot (x, z, -y).</summary>
    public static Vector3 ToGodotPoint(Vec3 p) => new((float)p.X, (float)p.Z, (float)-p.Y);

    public static Transform3D ZupToYup => new(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero);

    ModelEntry Model(string llid)
    {
        if (models.TryGetValue(llid, out var me)) return me;
        me = null;
        var (archive, entry) = assets.Resolve(llid);
        if (archive != null && packs.Exists(archive, entry))
        {
            try
            {
                var nif = NifFile.Load(builder.Spec, packs.Read(archive, entry));
                var md = ModelData.Load(nif);
                me = new ModelEntry { Data = md };
                foreach (var cm in PhysXCollision.Load(nif))
                {
                    var faces = new Vector3[cm.Triangles.Length];
                    for (int i = 0; i < faces.Length; i++)
                    {
                        int v = cm.Triangles[i] * 3;
                        faces[i] = new Vector3(cm.Vertices[v], cm.Vertices[v + 1], cm.Vertices[v + 2]);
                    }
                    if (faces.Length >= 3) me.Collision.Add(new ConcavePolygonShape3D { Data = faces, BackfaceCollision = true });
                }
                bool first = true;
                foreach (var m in md.Meshes)
                {
                    var world = m.Skinned ? Xform.Identity
                              : (m.ParentBone >= 0 ? md.Bones[m.ParentBone].World : Xform.Identity).Compose(m.Local);
                    var local = ModelBuilder.ToGodot(md.RootLocal.Compose(world));
                    me.Meshes.Add((m, local));
                    for (int i = 0; i < m.Positions.Length; i += 3)
                    {
                        var p = local * new Vector3(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]);
                        if (first) { me.Bounds = new Aabb(p, Vector3.Zero); first = false; }
                        else me.Bounds = me.Bounds.Expand(p);
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PushWarning($"model {archive}:{entry}: {ex.Message}");
                me = null;
            }
        }
        if (me == null) Missing++;
        models[llid] = me;
        return me;
    }

    public Node3D Build(FieldMap map)
    {
        var world = new Node3D { Name = "World", Transform = ZupToYup };
        var visuals = new Node3D { Name = "Visuals" };
        var colliders = new Node3D { Name = "Colliders" };
        world.AddChild(visuals);
        world.AddChild(colliders);

        var groups = new Dictionary<string, List<FieldEntity>>(StringComparer.OrdinalIgnoreCase);
        var chunks = new Dictionary<(int, int), StaticBody3D>();

        StaticBody3D Chunk(Vec3 p)
        {
            var key = ((int)Math.Floor(p.X / ChunkSize), (int)Math.Floor(p.Y / ChunkSize));
            if (!chunks.TryGetValue(key, out var body))
            {
                body = new StaticBody3D { Name = $"Chunk_{key.Item1}_{key.Item2}" };
                colliders.AddChild(body);
                chunks[key] = body;
            }
            return body;
        }

        foreach (var e in map.Entities)
        {
            var xf = ModelBuilder.ToGodot(e.Transform);

            if (e.ModelName is "PhysXWhitebox" or "MS2PhysXWhitebox")
            {
                var d = e.Vector("ShapeDimensions");
                if (d.X > 0 && d.Y > 0 && d.Z > 0)
                {
                    // PhysXWhiteboxService: a box centred on the entity, half extents = ShapeDimensions / 2
                    var cs = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3((float)d.X, (float)d.Y, (float)d.Z) }, Transform = xf };
                    Chunk(e.Position).AddChild(cs);
                    Whiteboxes++;
                }
                continue;
            }

            var llid = e.Get("NifAsset");
            if (llid == null || !llid.StartsWith("urn:llid:")) continue;
            var me = Model(llid);
            if (me == null) continue;

            if (e.Bool("IsVisible", true))
            {
                if (!groups.TryGetValue(llid, out var list)) groups[llid] = list = new List<FieldEntity>();
                list.Add(e);
            }

            if (e.Bool("DisableCollision")) continue;
            if (me.Collision.Count > 0)
            {
                // the NIF's own PhysX meshes (deform pieces, stairs, water surface, props)
                foreach (var shp in me.Collision)
                    Chunk(e.Position).AddChild(new CollisionShape3D { Shape = shp, Transform = xf });
                Meshes++;
            }
            else if (e.HasMixin("MS2CubeProp") && e.Bool("GeneratePhysX"))
            {
                // CubeMapInformationService::GeneratePhysX: a box of GeneratePhysXDimension (150 cube when unset)
                // standing on the entity position, turned by the Z rotation only; scale is ignored
                var d = e.Vector("GeneratePhysXDimension");
                var size = d.X == 0 && d.Y == 0 && d.Z == 0 ? new Vector3(150, 150, 150) : new Vector3((float)d.X, (float)d.Y, (float)d.Z);
                var p = e.Position;
                Chunk(e.Position).AddChild(new CollisionShape3D
                {
                    Shape = new BoxShape3D { Size = size },
                    Transform = new Transform3D(new Basis(Vector3.Back, Mathf.DegToRad((float)e.RotationDeg.Z)),
                                                new Vector3((float)p.X, (float)p.Y, (float)p.Z + size.Z / 2)),
                });
                Boxes++;
            }
        }

        foreach (var (llid, ents) in groups)
        {
            var me = models[llid];
            int mi = 0;
            foreach (var (mesh, local) in me.Meshes)
            {
                var mm = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true,
                    Mesh = builder.BuildMesh(mesh, skin: false, instanceColor: true),
                    InstanceCount = ents.Count,
                };
                for (int i = 0; i < ents.Count; i++)
                {
                    mm.SetInstanceTransform(i, ModelBuilder.ToGodot(ents[i].Transform) * local);
                    var c = ents[i].Numbers("MaterialColor");
                    var col = c != null && c.Length >= 3
                        ? new Color((float)c[0], (float)c[1], (float)c[2])
                        : new Color(DefaultMaterialColor, DefaultMaterialColor, DefaultMaterialColor);
                    mm.SetInstanceColor(i, col);
                }
                visuals.AddChild(new MultiMeshInstance3D { Name = $"{ents[0].ModelName}{mi++}", Multimesh = mm });
                MultiMeshes++;
                Instances += ents.Count;
            }
        }
        return world;
    }
}
