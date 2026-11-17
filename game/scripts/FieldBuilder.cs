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

    public int Instances, MultiMeshes, Boxes, Meshes, Whiteboxes, Missing, OwnShells;

    /// <summary>
    /// How far behind its colour an own-group shell twin sorts (game units): Godot sorts the alpha list by priority, then by
    /// instance depth, with an unstable sort, so the twin has to be strictly deeper to be drawn right before its colour.
    /// </summary>
    const float OwnShellSortOffset = 0.05f;

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

        // key: the llid, plus the shell group and, for an own-group shell, the instancing batch (see ShellOf / BatchKey)
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
                var key = BatchKey(llid, e);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<FieldEntity>();
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

        foreach (var (key, ents) in groups)
        {
            var me = models[key.Split('\n')[0]];
            int mi = 0;
            // shells (FUN_1413fa4c0): the shared group draws through ModelBuilder.MakeFluidShell; an own group draws each
            // blended mesh's depth-only shell as a twin instance sorted just behind its colour, so it comes right before it
            var shell = ShellOf(ents[0]);
            foreach (var (mesh, local) in me.Meshes)
            {
                var mm = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true,
                    Mesh = builder.BuildMesh(mesh, skin: false, instanceColor: true),
                    InstanceCount = ents.Count,
                };
                if (shell == Shell.Shared && mesh.AlphaBlendOn) ModelBuilder.MakeFluidShell((ArrayMesh)mm.Mesh);
                if (shell == Shell.Own && mesh.AlphaBlendOn && ModelBuilder.ShellMaterial((ArrayMesh)mm.Mesh) is { } depthOnly)
                {
                    visuals.AddChild(new MultiMeshInstance3D { Name = $"{ents[0].ModelName}{mi}_shell", Multimesh = mm, MaterialOverride = depthOnly, SortingOffset = -OwnShellSortOffset, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                    OwnShells++;
                }
                for (int i = 0; i < ents.Count; i++)
                {
                    mm.SetInstanceTransform(i, ModelBuilder.ToGodot(ents[i].Transform) * local);
                    mm.SetInstanceColor(i, InstanceColor(ents[i]));
                }
                // the sun's shadow: a MultiMesh is culled as a whole, and its box spans the field, so as one caster every copy is drawn
                // again in every shadow cascade. The shadow casters are per-cell twins (tight boxes, culled per cascade); the visible
                // instances stay one MultiMesh per mesh. A blended mesh (water, glass, effects) and the glow twin cast none.
                visuals.AddChild(new MultiMeshInstance3D { Name = $"{ents[0].ModelName}{mi++}", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                if (!mesh.AlphaBlendOn) AddShadowCells(visuals, ents[0].ModelName, mesh, mm.Mesh, ents, local);
                if (builder.GlowMaterial(mesh) is { } gm)
                    visuals.AddChild(new MultiMeshInstance3D { Name = $"{ents[0].ModelName}{mi}_glow", Multimesh = mm, Layers = GlowPass.Bit, MaterialOverride = gm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                MultiMeshes++;
                Instances += ents.Count;
            }
        }
        FlushShadowCells(visuals);
        return world;
    }

    /// <summary>Edge of a shadow caster cell, game units.</summary>
    const float ShadowCell = 3000f;
    public int ShadowCasters;

    /// <summary>Opaque shadow casters baked per cell: positions and indices only, one mesh (one draw) per cell.</summary>
    readonly Dictionary<(int, int, int), (List<Vector3> v, List<int> i)> shadowBake = new();

    static (int, int, int) CellKey(FieldEntity e) =>
        ((int)Math.Floor(e.Position.X / ShadowCell), (int)Math.Floor(e.Position.Y / ShadowCell), (int)Math.Floor(e.Position.Z / ShadowCell));

    void AddShadowCells(Node parent, string name, ModelMesh src, Mesh mesh, List<FieldEntity> ents, Transform3D local)
    {
        if (!src.AlphaTestOn && !src.AlphaTest)
        {
            // no cut-outs: the shadow needs the triangles only, so every model of a cell shares one baked mesh
            var arrays = mesh.SurfaceGetArrays(0);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            foreach (var e in ents)
            {
                var key = CellKey(e);
                if (!shadowBake.TryGetValue(key, out var bake)) shadowBake[key] = bake = (new List<Vector3>(), new List<int>());
                var xf = ModelBuilder.ToGodot(e.Transform) * local;
                int baseIndex = bake.v.Count;
                foreach (var v in verts) bake.v.Add(xf * v);
                foreach (var i in idx) bake.i.Add(baseIndex + i);
            }
            return;
        }
        foreach (var cell in ents.GroupBy(CellKey))
        {
            var list = cell.ToList();
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = list.Count };
            for (int i = 0; i < list.Count; i++) mm.SetInstanceTransform(i, ModelBuilder.ToGodot(list[i].Transform) * local);
            parent.AddChild(new MultiMeshInstance3D { Name = name + "_shadow", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly });
            ShadowCasters++;
        }
    }

    void FlushShadowCells(Node parent)
    {
        foreach (var (key, bake) in shadowBake)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = bake.v.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = bake.i.ToArray();
            var am = new ArrayMesh();
            am.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            parent.AddChild(new MeshInstance3D { Name = $"shadow_{key.Item1}_{key.Item2}_{key.Item3}", Mesh = am, CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly });
            ShadowCasters++;
        }
        shadowBake.Clear();
    }

    /// <summary>
    /// The entity's MaterialColor (ambient = diffuse) as the instance colour; without one (effect meshes such as
    /// portals) alpha 0, and the shader keeps the NIF material's own ambient and diffuse.
    /// </summary>
    static Color InstanceColor(FieldEntity e)
    {
        var c = e.Numbers("MaterialColor");
        return c != null && c.Length >= 3 ? new Color((float)c[0], (float)c[1], (float)c[2]) : new Color(0, 0, 0, 0);
    }

    enum Shell { None, Shared, Own }

    /// <summary>
    /// The alpha shell group of a placed entity. Only hardware-instanced entities get one: HardwareInstancingService
    /// instances an entity with UseInstancing, MeshInstancing::Init reads its UseShellRender, and RefreshRootAndSource then
    /// runs FUN_1413fa4c0 on the batch's source entity: z write on, AlphaShellComposite::MarkScene with group 1 (one
    /// composite shared by every such batch) when FUN_1413fa390 holds, else a group of its own. FUN_1413fa390 is
    /// IsHighPrioAlphaObject || CubeType == "Fluid" on the source, which CreateEntity makes from the model alone, so it
    /// reads the flat defaults, not the placed entity's overrides.
    /// </summary>
    static Shell ShellOf(FieldEntity e)
    {
        if (!e.Bool("UseInstancing") || !e.Bool("UseShellRender")) return Shell.None;
        var flat = e.Flat?.Props;
        bool highPrio = flat != null && flat.TryGetValue("IsHighPrioAlphaObject", out var h) && (h.Equals("True", StringComparison.OrdinalIgnoreCase) || h == "1");
        bool fluid = flat != null && flat.TryGetValue("CubeType", out var c) && c == "Fluid";
        return highPrio || fluid ? Shell.Shared : Shell.Own;
    }

    /// <summary>
    /// The draw group of a placed entity: its llid, split by shell group; an own-group shell further by the instancing batch.
    /// Unk_MeshInstancingConfig::Equals matches a shell batch (UseShellRender) on the asset, IsCastingShadow,
    /// IsReceivingShadow and MaterialColor, and not on the instancing cell. Its nearby-light list (FUN_1413d7160) splits
    /// batches too; that is not modelled here.
    /// </summary>
    static string BatchKey(string llid, FieldEntity e) => ShellOf(e) switch
    {
        Shell.Shared => llid + "\n1",
        Shell.Own => $"{llid}\n2\n{e.Get("MaterialColor")}\n{e.Bool("IsCastingShadow", true)}\n{e.Bool("IsReceivingShadow", true)}",
        _ => llid,
    };
}
