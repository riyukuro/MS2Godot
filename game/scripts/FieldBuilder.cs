using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// Builds a field from its entity set: one MultiMesh per (model, mesh) for the visible NIF entities, and static
/// colliders: each NIF's own PhysX meshes (none for a GeneratePhysX entity), the generated box of a GeneratePhysX entity, and PhysXWhitebox boxes. Everything sits under a "World" node that rotates MS2 Z-up to Godot Y-up,
/// in file units.
/// </summary>
public sealed class FieldBuilder
{
    public const string MapArchive = "Resource/Model/Map";
    const float ChunkSize = 1500f;                     // collider bodies per 10 x 10 cubes
    /// <summary>Meta on a collision shape of a fall-return cube: the entity's index in the field (see the collision pass).</summary>
    public const string FallReturnMeta = "ms2_fall_return";
    /// <summary>Meta on a collision shape whose entity's PropCollisionGroup is not the group of its body (a GeneratePhysX
    /// cube box); a shape without it has its body's group.</summary>
    public const string KindMeta = "ms2_kind";
    const int GeneratedBoxGroup = 9;

    readonly PackSet packs;
    readonly AssetIndex assets;
    readonly ModelBuilder builder;

    public int Instances, MultiMeshes, Boxes, Meshes, Whiteboxes, Missing, OwnShells, SplitObjects;
    /// <summary>GeneratePhysX entities whose NIF has PhysX meshes the client does not build (their box stands in).</summary>
    public int SkippedNifMeshes;
    /// <summary>The field's &lt;ui instancingGrid&gt; (FieldData.InstancingGrid): the instancing cell edge, 0 = one cell.</summary>
    public int InstancingGrid { get; set; }
    /// <summary>The field's objects cast the sun's shadow (FieldShadows.MapObjects): below the level the client clears every
    /// caster (SetShadowCaster(node, false), worldservice_1.cpp:3428-3430), so no shadow cells are made.</summary>
    public bool CastsShadows { get; set; } = true;

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
    /// <summary>Optional: the field load's entity count, bumped as each entity of the set is placed (see <see cref="World.LoadCount"/>).</summary>
    public World.LoadCount Load;
    /// <summary>One drawn copy of a mesh of an entity: instance <see cref="Index"/> of <see cref="Mm"/>.</summary>
    public sealed class FadeDraw { public MultiMesh Mm; public int Index; public ModelMesh Mesh; public Node3D Parent; }
    /// <summary>The draws of every entity BGTransparencyMgr can fade (<see cref="IsFadeTarget"/>), for world/FieldFade.</summary>
    public readonly Dictionary<FieldEntity, List<FadeDraw>> FadeDraws = new();

    /// <summary>
    /// WorldService::FUN_141bbfe30 (worldservice_1.cpp:3638-3659) records an entity for BGTransparencyMgr when its model
    /// contains MS2MapProperties (0x2ea80520) and its Transparency (0x9004569a) is true, unless the hardware instancing
    /// service lists it as excluded; only an entity made at run time by MS2EntityManagerService::CreateEntity is put on that
    /// list (ms2entitymanagerservice.cpp:166-167), never a field's own entity.
    /// </summary>
    public static bool IsFadeTarget(FieldEntity e) => e.HasMixin("MS2MapProperties") && e.Bool("Transparency");

    void AddFadeDraw(FieldEntity e, MultiMesh mm, int index, ModelMesh mesh, Node3D parent)
    {
        if (!IsFadeTarget(e)) return;
        if (!FadeDraws.TryGetValue(e, out var list)) FadeDraws[e] = list = new List<FadeDraw>();
        list.Add(new FadeDraw { Mm = mm, Index = index, Mesh = mesh, Parent = parent });
    }

    /// <summary>The blended draws with the client's alpha key, for <see cref="AlphaSort"/>.</summary>
    public readonly List<AlphaSort.Entry> AlphaEntries = new();

    sealed class ModelEntry
    {
        public ModelData Data;
        public List<(ModelMesh mesh, Transform3D local)> Meshes = new();
        public Aabb Bounds;              // union of the mesh bounds in model space
        public List<Shape3D> Collision = new();   // cooked PhysX meshes of the NIF, model space
        public List<Shape3D> Overlap = new();     // the same meshes for the overlap tests (TwoSidedSpace), by index
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
                    // A triangle's front is its PhysX normal (v1 - v0) x (v2 - v0); Godot takes Plane(v0, v1, v2), the
                    // opposite, so each triangle goes in as (v0, v2, v1). The CCT's triangle sweeps test a triangle from
                    // its front only unless it carries TCF_DOUBLE_SIDED (NxUtilLib.h:17-24, the flags getTriangle fills,
                    // CharacterControllerCallbacks.cpp:173), and no MS2 NiPhysXMeshDesc sets NX_MESH_DOUBLE_SIDED (Mesh
                    // Flags 0), so a triangle mesh is one-sided: a capsule inside a closed prop's volume touches none of
                    // it. A convex shape's hull triangles go to the sweeps with edge flags 7 and no TCF_DOUBLE_SIDED
                    // (outputConvexToStream, CharacterControllerCallbacks.cpp:367), so they are one-sided too.
                    var faces = new Vector3[cm.Triangles.Length];
                    for (int i = 0; i < faces.Length; i++)
                    {
                        int v = cm.Triangles[i - i % 3 + (3 - i % 3) % 3] * 3;     // 0, 2, 1 of each triangle
                        faces[i] = new Vector3(cm.Vertices[v], cm.Vertices[v + 1], cm.Vertices[v + 2]);
                    }
                    if (faces.Length < 3) continue;
                    me.Collision.Add(new ConcavePolygonShape3D { Data = faces, BackfaceCollision = false });
                    // checkOverlapCapsule has no sides: a triangle mesh counts from either side, and a convex shape is a
                    // solid volume, so a capsule wholly inside a hull overlaps it
                    me.Overlap.Add(cm.Convex ? new ConvexPolygonShape3D { Points = faces }
                                             : new ConcavePolygonShape3D { Data = faces, BackfaceCollision = true });
                }
                bool first = true;
                foreach (var m0 in md.Meshes)
                {
                    // a skinned mesh is drawn at the file's node pose (nothing animates it here), not at its bind shape
                    var m = m0.Skinned ? md.Posed(m0) : m0;
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
        foreach (var _ in BuildSteps(map)) { }
        return World;
    }

    /// <summary>The finished field from <see cref="BuildSteps"/>.</summary>
    public Node3D World { get; private set; }

    // CubeMapInformationService::GeneratePhysX: a box of GeneratePhysXDimension (150 cube when unset) standing on the
    // entity position, turned by the Z rotation only; scale is ignored
    static BoxShape3D GeneratePhysXBox(FieldEntity e, out Transform3D at)
    {
        var d = e.Vector("GeneratePhysXDimension");
        var size = d.X == 0 && d.Y == 0 && d.Z == 0 ? new Vector3(150, 150, 150) : new Vector3((float)d.X, (float)d.Y, (float)d.Z);
        var p = e.Position;
        at = new Transform3D(new Basis(Vector3.Back, Mathf.DegToRad((float)e.RotationDeg.Z)),
                             new Vector3((float)p.X, (float)p.Y, (float)p.Z + size.Z / 2));
        return new BoxShape3D { Size = size };
    }

    /// <summary>
    /// <see cref="Build"/> in steps: it yields after each entity of the first pass and after each instancing group, so a
    /// caller can spread the build over frames (Field: the field load behind the loading screen). The nodes are built off
    /// the tree; <see cref="World"/> holds the result once the sequence ends.
    /// </summary>
    public IEnumerable<int> BuildSteps(FieldMap map)
    {
        World = null;
        Load?.Begin(map.Entities.Count);
        var world = new Node3D { Name = "World", Transform = ZupToYup };
        building = world;
        var visuals = new Node3D { Name = "Visuals" };
        var colliders = new Node3D { Name = "Colliders" };
        world.AddChild(visuals);
        world.AddChild(colliders);
        var twoSided = new TwoSidedSpace { Name = "TwoSided" };
        colliders.AddChild(twoSided);
        var npcGround = new NpcGroundSpace { Name = "NpcGround" };
        colliders.AddChild(npcGround);

        // key: the llid, plus the shell group and, for an own-group shell, the instancing batch (see ShellOf / BatchKey)
        var groups = new Dictionary<string, List<FieldEntity>>(StringComparer.OrdinalIgnoreCase);
        var chunks = new Dictionary<(int, int), StaticBody3D>();

        // colliders of entities with a PropCollisionGroup (8 = water) go to their own bodies, tagged ms2_group and on the
        // layer of their group (bit n = group n; group 0 stays on layer 1), so a query's mask is the client's groups mask
        var groupChunks = new Dictionary<(int, int, int), StaticBody3D>();
        StaticBody3D Chunk(Vec3 p, int group = 0)
        {
            var key = ((int)Math.Floor(p.X / ChunkSize), (int)Math.Floor(p.Y / ChunkSize));
            if (group != 0)
            {
                if (!groupChunks.TryGetValue((key.Item1, key.Item2, group), out var gb))
                {
                    gb = new StaticBody3D { Name = $"Chunk_{key.Item1}_{key.Item2}_g{group}", CollisionLayer = 1u << group, CollisionMask = 0 };
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

        int entityIndex = -1;
        foreach (var e in map.Entities)
        {
            entityIndex++;
            yield return 0;                             // (before the entity: its paths may end with continue)
            bool grouped = false;
            try
            {
                var xf = ModelBuilder.ToGodot(e.Transform);

                if (e.ModelName is "PhysXWhitebox" or "MS2PhysXWhitebox")
                {
                    var d = e.Vector("ShapeDimensions");
                    if (d.X > 0 && d.Y > 0 && d.Z > 0)
                    {
                        // PhysXWhiteboxService: a box centred on the entity, half extents = ShapeDimensions / 2, in the
                        // entity's PropCollisionGroup (PhysXWhiteboxService.cpp:392-395, :440)
                        var whitebox = new BoxShape3D { Size = new Vector3((float)d.X, (float)d.Y, (float)d.Z) };
                        Chunk(e.Position, (int)e.Number("PropCollisionGroup")).AddChild(new CollisionShape3D { Shape = whitebox, Transform = xf });
                        npcGround.Add(whitebox, xf);
                        Whiteboxes++;
                    }
                    continue;
                }

                // the npc ground (NpcGroundSpace) takes the server's collider for the entity: a fluid mesh none, a
                // GeneratePhysX entity its box only (not its NIF's PhysX meshes; a GeneratePhysX entity with no Mesh, such
                // as an MS2InteractActor, gets the box too), any other mesh its NIF's PhysX meshes; DisableCollision does
                // not matter there
                bool isMesh = e.HasMixin("Mesh");
                bool fluid = isMesh && e.Get("CubeType") == "Fluid";
                BoxShape3D generated = null;
                var generatedAt = default(Transform3D);
                if (e.HasMixin("MS2MapProperties") && e.Bool("GeneratePhysX"))
                {
                    generated = GeneratePhysXBox(e, out generatedAt);
                    if (!fluid) npcGround.Add(generated, generatedAt);
                }

                var llid = e.Get("NifAsset");
                if (llid == null || !llid.StartsWith("urn:llid:")) continue;
                var me = Model(llid);
                if (me == null) continue;
                if (isMesh && !fluid && generated == null)
                    foreach (var shape in me.Collision) npcGround.Add(shape, xf);

                if (e.Bool("IsVisible", true) && e.HasMixin("EffectMesh") && assets.Resolve(llid) is (string ea, string ee) && ea == "Resource/Model/Effect") { EffectEntities.Add((e, ea, ee)); }
                else if (e.Bool("IsVisible", true) && me.Elsewhere) DrawnElsewhere.Add(e);
                else if (e.Bool("IsVisible", true))
                {
                    var key = BatchKey(llid, e);
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<FieldEntity>();
                    list.Add(e);
                    grouped = true;            // counted when its group is placed
                }

                // DisableCollision (0x7e5f4195, DisableCollision by its uses): the client keeps the entity's actors but
                // clears their groups mask when the entity is added (WorldService::FUN_141bbd640,
                // worldservice_1.cpp:10478-10488, through CPhysXHelper::SetEntityCollisionGroup @0x141689ec0: the cube
                // actor of a GeneratePhysX entity, else each actor of its NIF PhysX props), so they are left out of the
                // world here.
                // STILL-OPEN: whether any client query that passes no groups mask still meets them
                if (e.Bool("DisableCollision")) continue;
                int group = (int)e.Number("PropCollisionGroup");
                // MyPC::FUN_141524a80 (mypc.cpp:2353-2366): a state sync taken while standing on a MS2CubeProp whose
                // IsFallReturn is set and whose PropCollisionGroup is 9 or 8 carries the cube's cell. Its shapes carry the
                // entity, so the hit report and the ground ray can name it (both actors of a cube are the same entity)
                bool fallReturn = e.HasMixin("MS2CubeProp") && e.Bool("IsFallReturn") && (group == 9 || group == 8);
                CollisionShape3D Tag(CollisionShape3D cs)
                {
                    if (fallReturn) cs.SetMeta(FallReturnMeta, entityIndex);
                    return cs;
                }
                // PropService::HandleSceneGraphAddedMessage (PropService.cpp:460-465, exe 0x142062560) returns before
                // AddPhysXPropToPropData for an entity whose GeneratePhysX (0x38141815) is set, whatever its model: such an
                // entity gets no NIF PhysX prop, and its only collision is the generated box below
                bool generate = e.Bool("GeneratePhysX");
                if (generate && me.Collision.Count > 0) SkippedNifMeshes++;
                else if (me.Collision.Count > 0)
                {
                    // the NIF's own PhysX meshes (deform pieces, stairs, water surface, props), every shape in the entity's
                    // PropCollisionGroup (ecrPhysX PropService::SetCollisionGroup, PropService.cpp:1471-1499)
                    for (int i = 0; i < me.Collision.Count; i++)
                    {
                        Chunk(e.Position, group).AddChild(Tag(new CollisionShape3D { Shape = me.Collision[i], Transform = xf }));
                        twoSided.Add(me.Overlap[i], xf, group);
                    }
                    Meshes++;
                }
                // FUN_1410cf160 (cubemapinformationservice_3.cpp:4026-4052) runs FUN_1410c5570, the GeneratePhysX box, for
                // an MS2CubeProp and for any other MS2PhysXProp (0xc9cd2193; MS2CubeProp contains it), but not for an
                // MS2SalePost (0xf01a7608), which it tests first
                if (generate && !e.HasMixin("MS2SalePost") && (e.HasMixin("MS2CubeProp") || e.HasMixin("MS2PhysXProp")))
                {
                    // CubeMapInformationService::GeneratePhysX: a box of GeneratePhysXDimension (150 cube when unset)
                    // standing on the entity position, turned by the Z rotation only; scale is ignored.
                    // GeneratePhysX puts its shape in group 9 whatever the entity's PropCollisionGroup
                    // (cubemapinformationservice_3.cpp:4343). At field load nothing sets the entity's group on it; the other
                    // SetEntityCollisionGroup callers run at run time only (housing's cell window FUN_1410c3670 and
                    // cubemapinformationservice_3.cpp:3155, 3666-3669, 3736, 4761/4776; functioncubeobject.cpp:998/1000;
                    // liftableobject.cpp:301/305; entityvibrator.cpp:287/345/485/581; worldservice_1.cpp:8727, 10392),
                    // or at field load only for 0x7e5f4195 entities (worldservice_1.cpp:10478-10488), which the stand-in
                    // skips. The entity keeps its own group for the hit report's tests (FUN_141675ed0 reads the property,
                    // pcbase.cpp:4335-4342): KindMeta
                    var box = Tag(new CollisionShape3D { Shape = generated ?? GeneratePhysXBox(e, out generatedAt), Transform = generatedAt });
                    box.SetMeta(KindMeta, group);
                    Chunk(e.Position, GeneratedBoxGroup).AddChild(box);
                    Boxes++;
                }
            }
            finally { if (!grouped) Load?.Add(); }      // nothing of it is left to place
        }

        foreach (var (key, ents) in groups)
        {
            var me = models[key.Split('\n')[0]];
            int mi = 0;
            // shells (FUN_1413fa4c0): the shared group draws through ModelBuilder.MakeFluidShell; an own group draws each
            // blended mesh's depth-only shell as a twin instance sorted just behind its colour, so it comes right before it
            var shell = ShellOf(ents[0]);
            var ownBlended = new List<MultiMesh>();
            // a plain group's blended meshes draw as the client's alpha objects: one per instancing batch, one per other copy
            var objects = shell == Shell.None ? AlphaObjects(ents) : null;
            if (objects != null && objects.Count < 2) objects = null;
            foreach (var (mesh, local) in me.Meshes)
            {
                if (objects != null && mesh.AlphaBlendOn)
                {
                    var shared = builder.BuildMesh(mesh, skin: false, instanceColor: true);
                    var glow = builder.GlowMaterial(mesh);
                    for (int k = 0; k < objects.Count; k++)
                    {
                        var obj = objects[k];
                        var om = new MultiMesh
                        {
                            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                            UseColors = true,
                            Mesh = shared,
                            InstanceCount = obj.Count,
                        };
                        for (int i = 0; i < obj.Count; i++)
                        {
                            om.SetInstanceTransform(i, ModelBuilder.ToGodot(obj[i].Transform) * local);
                            om.SetInstanceColor(i, InstanceColor(obj[i]));
                            AddFadeDraw(obj[i], om, i, mesh, visuals);
                        }
                        var on = new MultiMeshInstance3D { Name = $"{ents[0].ModelName}{mi}_o{k}", Multimesh = om, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                        visuals.AddChild(on);
                        SortAsObject(on, mesh, obj, local);
                        if (glow != null)
                        {
                            var gn = new MultiMeshInstance3D { Name = $"{ents[0].ModelName}{mi}_o{k}_glow", Multimesh = om, Layers = GlowPass.Bit, MaterialOverride = glow, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                            visuals.AddChild(gn);
                            SortAsObject(gn, mesh, obj, local);
                        }
                        MultiMeshes++;
                        Instances += obj.Count;
                    }
                    SplitObjects += objects.Count;
                    mi++;
                    continue;
                }
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
                    var sn = new MultiMeshInstance3D { Name = $"{ents[0].ModelName}{mi}_shell", Multimesh = mm, MaterialOverride = depthOnly, SortingOffset = -OwnShellSortOffset, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                    visuals.AddChild(sn);
                    AlphaEntries.Add(new AlphaSort.Entry { Node = sn, Composite = true, Extra = -OwnShellSortOffset });
                    OwnShells++;
                    ownBlended.Add(mm);
                }
                for (int i = 0; i < ents.Count; i++)
                {
                    mm.SetInstanceTransform(i, ModelBuilder.ToGodot(ents[i].Transform) * local);
                    mm.SetInstanceColor(i, InstanceColor(ents[i]));
                    AddFadeDraw(ents[i], mm, i, mesh, visuals);
                }
                // the sun's shadow: a MultiMesh is culled as a whole, and its box spans the field, so as one caster every copy is drawn
                // again in every shadow cascade. The shadow casters are per-cell twins (tight boxes, culled per cascade); the visible
                // instances stay one MultiMesh per mesh. A blended mesh gets none: its BLEND/ADD (depth_draw_never) or SHELL material
                // is transparent, and Godot leaves transparent materials out of the shadow pass (observed: Henesys, Ellinia and Tria
                // stills are pixel-identical with and without its casters). The glow twin casts none.
                var node = new MultiMeshInstance3D { Name = $"{ents[0].ModelName}{mi++}", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                visuals.AddChild(node);
                if (!mesh.AlphaBlendOn && CastsShadows) AddShadowCells(visuals, ents[0].ModelName, mm.Mesh, ents, local);
                if (mesh.AlphaBlendOn) SortBlended(node, mesh, shell, ents, local);
                if (builder.GlowMaterial(mesh) is { } gm)
                {
                    var gn = new MultiMeshInstance3D { Name = $"{ents[0].ModelName}{mi}_glow", Multimesh = mm, Layers = GlowPass.Bit, MaterialOverride = gm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                    visuals.AddChild(gn);
                    if (mesh.AlphaBlendOn) SortBlended(gn, mesh, shell, ents, local);
                }
                MultiMeshes++;
                Instances += ents.Count;
            }
            // an own composite is one slot of the client's alpha list (all its shells, then all its colour): with more than one
            // blended mesh, they share one box, so every shell sorts at the same centre, just behind every colour
            if (ownBlended.Count > 1)
            {
                var box = ownBlended[0].GetAabb();
                foreach (var b in ownBlended) box = box.Merge(b.GetAabb());
                foreach (var b in ownBlended) b.CustomAabb = box;
            }
            Load?.Add(ents.Count);                      // the group's entities are placed
            yield return 0;
        }
        World = world;
        building = null;
    }

    Node3D building;
    /// <summary>Frees the world of a <see cref="BuildSteps"/> sequence left unfinished (its field was freed meanwhile):
    /// it is not in the tree, so nothing else frees it.</summary>
    public void Abandon()
    {
        if (building != null && GodotObject.IsInstanceValid(building)) building.Free();
        building = null;
    }

    /// <summary>Edge of a shadow caster cell, game units.</summary>
    const float ShadowCell = 3000f;
    public int ShadowCasters;

    static (int, int, int) CellKey(FieldEntity e) =>
        ((int)Math.Floor(e.Position.X / ShadowCell), (int)Math.Floor(e.Position.Y / ShadowCell), (int)Math.Floor(e.Position.Z / ShadowCell));

    /// <summary>The sun shadow casters of one mesh: per cell, the drawn mesh and material with the drawn transforms and
    /// instance colours, shadow only. The same triangles reach the shadow map as from the drawn MultiMesh, culled per cell.</summary>
    void AddShadowCells(Node parent, string name, Mesh mesh, List<FieldEntity> ents, Transform3D local)
    {
        foreach (var cell in ents.GroupBy(CellKey))
        {
            var list = cell.ToList();
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = list.Count };
            for (int i = 0; i < list.Count; i++)
            {
                mm.SetInstanceTransform(i, ModelBuilder.ToGodot(list[i].Transform) * local);
                mm.SetInstanceColor(i, InstanceColor(list[i]));
            }
            parent.AddChild(new MultiMeshInstance3D { Name = name + "_shadow", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly });
            ShadowCasters++;
        }
    }

    /// <summary>
    /// The client's alpha objects for the copies of one plain (non-shell) model, in placement order. Only an entity with
    /// UseInstancing is batched (HardwareInstancingService::BuildInstancings); it joins the first batch whose
    /// Unk_MeshInstancingConfig::Equals matches: IsCastingShadow, IsReceivingShadow, the instancing cell (the shell flag
    /// skips it; plain batches always compare it) and MaterialColor (GetEntityColor, exact float compare). A batch of one is
    /// dropped and draws as its entity. Every other copy is its own entity, whose meshes go to the alpha list one by one
    /// (MS2RenderListProcessor::PreRenderProcessList), so each blended mesh of each object is one draw sorted at its own box.
    /// Not modelled: the nearby-light list (FUN_1413d7160) that Equals also compares. It tests
    /// |light.Position - scene.WorldTranslate| &lt;= light.Range + scene.WorldBound.Radius, and the client's live NiBound
    /// radius is not available here, so such batches stay together (an under-split, 46 objects in 44 groups over all fields).
    /// Null when a copy is kind A (placed IsHighPrioAlphaObject or Fluid on an instanced copy): those stay one draw.
    /// </summary>
    List<List<FieldEntity>> AlphaObjects(List<FieldEntity> ents)
    {
        var objects = new List<List<FieldEntity>>();
        var batches = new Dictionary<(int, int, int, Color, bool, bool), List<FieldEntity>>();
        foreach (var e in ents)
        {
            if (!e.Bool("UseInstancing")) { objects.Add(new List<FieldEntity> { e }); continue; }
            if (e.Bool("IsHighPrioAlphaObject") || e.Get("CubeType") == "Fluid") return null;
            var p = e.Position;
            var c = e.Numbers("MaterialColor");
            var colour = c != null && c.Length >= 3 ? new Color((float)c[0], (float)c[1], (float)c[2]) : new Color(0.70588237f, 0.70588237f, 0.70588237f);
            // Unk_InstancingCell: (int)pos / nCellSize per axis, all zero when the field sets no instancingGrid
            int g = InstancingGrid;
            var key = (g != 0 ? (int)(float)p.X / g : 0, g != 0 ? (int)(float)p.Y / g : 0, g != 0 ? (int)(float)p.Z / g : 0,
                       colour, e.Bool("IsCastingShadow", true), e.Bool("IsReceivingShadow", true));
            if (!batches.TryGetValue(key, out var list)) { batches[key] = list = new List<FieldEntity>(); objects.Add(list); }
            list.Add(e);
        }
        return objects;
    }

    /// <summary>An unsplit blended node: a plain object keyed by its bound, or a shell composite keyed by its centre (radius 0).</summary>
    void SortBlended(GeometryInstance3D node, ModelMesh mesh, Shell shell, List<FieldEntity> members, Transform3D local)
    {
        if (shell == Shell.None) SortAsObject(node, mesh, members, local);
        else AlphaEntries.Add(new AlphaSort.Entry { Node = node, Composite = true });
    }

    /// <summary>
    /// A plain alpha object's key: the client's NiMesh world bound over its members, in member order (AlphaSort.WorldBound).
    /// The client's member order is ascending EntityID; placement order is used here (the sequential-ID reading).
    /// </summary>
    void SortAsObject(GeometryInstance3D node, ModelMesh mesh, List<FieldEntity> members, Transform3D local)
    {
        if (mesh.Bound == null) { AlphaEntries.Add(new AlphaSort.Entry { Node = node, Composite = true }); return; }
        var (c, r) = AlphaSort.WorldBound(mesh.Bound, members.Select(e => ModelBuilder.ToGodot(e.Transform) * local));
        AlphaEntries.Add(new AlphaSort.Entry { Node = node, Center = c, Radius = r, Bound = mesh.Bound });
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
