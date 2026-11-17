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
    const float ChunkSize = 1500f;                     // collider bodies per 10 x 10 cubes

    readonly PackSet packs;
    readonly AssetIndex assets;
    readonly ModelBuilder builder;

    public int Instances, MultiMeshes, Boxes, Meshes, Whiteboxes, Missing;

    /// <summary>Optional: true for a model drawn elsewhere per entity (world/FieldProps: self-animating NIFs); its visible
    /// entities go to <see cref="DrawnElsewhere"/> instead of a MultiMesh. Collision stays here.</summary>
    public Func<string, NifFile, bool> DrawsInstance;
    public readonly List<FieldEntity> DrawnElsewhere = new();
    /// <summary>Visible EffectMesh entities (portals, fountains' effect models): playing effects, drawn by world/FieldEffects.</summary>
    public readonly List<(FieldEntity entity, string archive, string entry)> EffectEntities = new();

    sealed class ModelEntry
    {
        public ModelData Data;
        public List<(ModelMesh mesh, Transform3D local)> Meshes = new();
        public Aabb Bounds;              // union of the mesh bounds in model space
        public List<Shape3D> Collision = new();   // cooked PhysX meshes of the NIF, model space
        public bool Elsewhere;           // DrawsInstance took it
    }

    readonly Dictionary<string, ModelEntry> models = new(StringComparer.OrdinalIgnoreCase);

    public FieldBuilder(PackSet packs, AssetIndex assets, ModelBuilder builder)
    {
        this.packs = packs; this.assets = assets; this.builder = builder;
        builder.TextureArchives = new[] { MapArchive, ModelBuilder.TextureArchive, "Resource/Model/Effect" };   // effect meshes (fountains, torches) use Effect.m2d textures
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
                me = new ModelEntry { Data = md, Elsewhere = DrawsInstance?.Invoke(llid, nif) == true };
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

        // colliders of entities with a PropCollisionGroup (8 = water) go to their own bodies, tagged ms2_group
        var groupChunks = new Dictionary<(int, int, int), StaticBody3D>();
        StaticBody3D Chunk(Vec3 p, int group = 0)
        {
            var key = ((int)Math.Floor(p.X / ChunkSize), (int)Math.Floor(p.Y / ChunkSize));
            if (group != 0)
            {
                if (!groupChunks.TryGetValue((key.Item1, key.Item2, group), out var gb))
                {
                    gb = new StaticBody3D { Name = $"Chunk_{key.Item1}_{key.Item2}_g{group}" };
                    gb.SetMeta("ms2_group", group);
                    colliders.AddChild(gb);
                    groupChunks[(key.Item1, key.Item2, group)] = gb;
                }
                return gb;
            }
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

            if (e.Bool("IsVisible", true) && e.HasMixin("EffectMesh") && assets.Resolve(llid) is (string ea, string ee) && ea == "Resource/Model/Effect") { EffectEntities.Add((e, ea, ee)); }
            else if (e.Bool("IsVisible", true) && me.Elsewhere) DrawnElsewhere.Add(e);
            else if (e.Bool("IsVisible", true))
            {
                if (!groups.TryGetValue(llid, out var list)) groups[llid] = list = new List<FieldEntity>();
                list.Add(e);
            }

            if (e.Bool("DisableCollision")) continue;
            int group = (int)e.Number("PropCollisionGroup");
            if (me.Collision.Count > 0)
            {
                // the NIF's own PhysX meshes (deform pieces, stairs, water surface, props)
                foreach (var shp in me.Collision)
                    Chunk(e.Position, group).AddChild(new CollisionShape3D { Shape = shp, Transform = xf });
                Meshes++;
            }
            else if (e.HasMixin("MS2CubeProp") && e.Bool("GeneratePhysX"))
            {
                // CubeMapInformationService::GeneratePhysX: a box of GeneratePhysXDimension (150 cube when unset)
                // standing on the entity position, turned by the Z rotation only; scale is ignored
                var d = e.Vector("GeneratePhysXDimension");
                var size = d.X == 0 && d.Y == 0 && d.Z == 0 ? new Vector3(150, 150, 150) : new Vector3((float)d.X, (float)d.Y, (float)d.Z);
                var p = e.Position;
                Chunk(e.Position, group).AddChild(new CollisionShape3D
                {
                    Shape = new BoxShape3D { Size = size },
                    Transform = new Transform3D(new Basis(Vector3.Back, Mathf.DegToRad((float)e.RotationDeg.Z)),
                                                new Vector3((float)p.X, (float)p.Y, (float)p.Z + size.Z / 2)),
                });
                Boxes++;
            }
        }

        // fluid cubes (water, ...) are blended without depth writes, so the faces between two of them would show;
        // their groups become one merged mesh without the faces that touch another fluid cube
        var fluidCells = new HashSet<Vector3I>();
        foreach (var ents in groups.Values)
            foreach (var e in ents)
                if (IsFluidCube(e)) fluidCells.Add(CellOf(e));

        foreach (var (llid, ents) in groups)
        {
            var me = models[llid];
            int mi = 0;
            if (ents.All(IsFluidCube))
            {
                foreach (var (mesh, local) in me.Meshes)
                {
                    visuals.AddChild(new MeshInstance3D
                    {
                        Name = $"{ents[0].ModelName}{mi++}",
                        Mesh = FluidMesh(builder.BuildMesh(mesh, skin: false, instanceColor: true), ents, local, fluidCells),
                    });
                    Instances += ents.Count;
                }
                continue;
            }
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
                    mm.SetInstanceColor(i, InstanceColor(ents[i]));
                }
                visuals.AddChild(new MultiMeshInstance3D { Name = $"{ents[0].ModelName}{mi++}", Multimesh = mm });
                if (builder.GlowMaterial(mesh) is { } gm)
                    visuals.AddChild(new MultiMeshInstance3D { Name = $"{ents[0].ModelName}{mi}_glow", Multimesh = mm, Layers = GlowPass.Bit, MaterialOverride = gm });
                MultiMeshes++;
                Instances += ents.Count;
            }
        }
        return world;
    }

    const float CubeSize = 150f;

    /// <summary>
    /// The entity's MaterialColor (ambient = diffuse) as the instance colour; without one (effect meshes such as
    /// portals) alpha 0, and the shader keeps the NIF material's own ambient and diffuse.
    /// </summary>
    static Color InstanceColor(FieldEntity e)
    {
        var c = e.Numbers("MaterialColor");
        return c != null && c.Length >= 3 ? new Color((float)c[0], (float)c[1], (float)c[2]) : new Color(0, 0, 0, 0);
    }

    static bool IsFluidCube(FieldEntity e) =>
        e.HasMixin("MS2CubeProp") && e.ModelName.Contains("fluid", StringComparison.OrdinalIgnoreCase);

    static Vector3I CellOf(FieldEntity e)
    {
        var p = e.Position;
        return new((int)Math.Round(p.X / CubeSize), (int)Math.Round(p.Y / CubeSize), (int)Math.Round(p.Z / CubeSize));
    }

    /// <summary>
    /// A triangle is hidden when each cube face its centroid lies on (within a few units, so the bevel strips along
    /// the edges count for both faces) has a fluid cube behind it. d is the centroid relative to the cube centre.
    /// </summary>
    static bool Hidden(Vector3 d, Vector3I cell, HashSet<Vector3I> cells)
    {
        const float onFace = CubeSize / 2 - 6;
        bool any = false;
        for (int axis = 0; axis < 3; axis++)
        {
            if (Mathf.Abs(d[axis]) < onFace) continue;
            var step = Vector3I.Zero;
            step[axis] = Math.Sign(d[axis]);
            if (!cells.Contains(cell + step)) return false;
            any = true;
        }
        return any;
    }

    /// <summary>
    /// The entities' copies of a cube mesh baked into one mesh (Z-up field space), dropping each triangle that faces a
    /// neighbouring fluid cell (see Hidden; the cube centre is the entity position + half a cube up). The material
    /// colour goes to COLOR, as the MultiMesh instance colour would.
    /// </summary>
    static ArrayMesh FluidMesh(ArrayMesh src, List<FieldEntity> ents, Transform3D local, HashSet<Vector3I> cells)
    {
        var a = src.SurfaceGetArrays(0);
        var v = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var n = a[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? null : a[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uv = a[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil ? null : a[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var uv2 = a[(int)Mesh.ArrayType.TexUV2].VariantType == Variant.Type.Nil ? null : a[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        var c0 = a[(int)Mesh.ArrayType.Custom0].VariantType == Variant.Type.Nil ? null : a[(int)Mesh.ArrayType.Custom0].AsFloat32Array();
        var idx = a[(int)Mesh.ArrayType.Index].AsInt32Array();

        var ov = new List<Vector3>(); var on = new List<Vector3>(); var ouv = new List<Vector2>(); var ouv2 = new List<Vector2>();
        var oc0 = new List<float>(); var ocol = new List<Color>();
        foreach (var e in ents)
        {
            var xf = ModelBuilder.ToGodot(e.Transform) * local;
            var centre = ModelBuilder.ToGodot(e.Transform).Origin + new Vector3(0, 0, CubeSize / 2);
            var cell = CellOf(e);
            var col = InstanceColor(e);
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                var d = (xf * v[idx[t]] + xf * v[idx[t + 1]] + xf * v[idx[t + 2]]) / 3 - centre;
                if (Hidden(d, cell, cells)) continue;
                for (int k = 0; k < 3; k++)
                {
                    int i = idx[t + k];
                    ov.Add(xf * v[i]);
                    if (n != null) on.Add((xf.Basis * n[i]).Normalized());
                    if (uv != null) ouv.Add(uv[i]);
                    if (uv2 != null) ouv2.Add(uv2[i]);
                    if (c0 != null) for (int j = 0; j < 4; j++) oc0.Add(c0[i * 4 + j]);
                    ocol.Add(col);
                }
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = ov.ToArray();
        if (n != null) arrays[(int)Mesh.ArrayType.Normal] = on.ToArray();
        if (uv != null) arrays[(int)Mesh.ArrayType.TexUV] = ouv.ToArray();
        if (uv2 != null) arrays[(int)Mesh.ArrayType.TexUV2] = ouv2.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = ocol.ToArray();
        ulong format = 0;
        if (c0 != null)
        {
            arrays[(int)Mesh.ArrayType.Custom0] = oc0.ToArray();
            format |= (ulong)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift;
        }
        var mesh = new ArrayMesh();
        if (ov.Count > 0)
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, (Mesh.ArrayFormat)format);
            mesh.SurfaceSetMaterial(0, src.SurfaceGetMaterial(0));
        }
        return mesh;
    }
}
