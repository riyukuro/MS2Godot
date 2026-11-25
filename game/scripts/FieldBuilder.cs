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
    public static readonly StringName FallReturnMeta = "ms2_fall_return";
    /// <summary>Meta on a collision shape whose entity's PropCollisionGroup is not the group of its body (a GeneratePhysX
    /// cube box); a shape without it has its body's group.</summary>
    public static readonly StringName KindMeta = "ms2_kind";
    /// <summary>Meta on a collider body or node: its PropCollisionGroup.</summary>
    public static readonly StringName GroupMeta = "ms2_group";
    /// <summary>Meta on a collision shape whose entity has a MapAttribute (0xde5dd580): the ground material that
    /// PCBase::UpdateGroundMaterial reads from the entity in the controller's hit report.</summary>
    public static readonly StringName GroundMeta = "ms2_ground";
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
    /// <summary>The field's shadow level (FieldShadows.Level), for which entities receive the sun's shadow (ShadowReceivers).</summary>
    public int ShadowLevel { get; set; } = 2;

    /// <summary>
    /// How far behind its colour an own-group shell twin sorts (game units): Godot sorts the alpha list by priority, then by
    /// instance depth, with an unstable sort, so the twin has to be strictly deeper to be drawn right before its colour.
    /// </summary>
    const float OwnShellSortOffset = 0.05f;

    /// <summary>Optional: true for a model drawn elsewhere per entity (world/FieldProps: self-animating NIFs); its visible
    /// entities go to <see cref="DrawnElsewhere"/> instead of a MultiMesh. Collision stays here.</summary>
    public Func<string, ModelData, KfAnimation, bool> DrawsInstance;
    /// <summary>The field's hold on the models it uses (NifCache); the field disposes it when it is freed.</summary>
    public NifCache.Lease Lease;
    public readonly List<FieldEntity> DrawnElsewhere = new();
    /// <summary>The <see cref="DrawnElsewhere"/> entities that receive no sun shadow (ShadowReceivers), for world/FieldProps.</summary>
    public readonly HashSet<FieldEntity> NotReceivingElsewhere = new();
    /// <summary>The field's point and spot lights (FieldLights); null or empty: no light masks.</summary>
    public FieldLightSet Lights { get; set; }
    /// <summary>The <see cref="DrawnElsewhere"/> entities that some field lights do not reach, with those lights' bits
    /// (<see cref="FieldLightSet.Unlit"/>), for world/FieldProps.</summary>
    public readonly Dictionary<FieldEntity, uint> LightOffElsewhere = new();
    /// <summary>Draws given a light mask (ms2_light_off != 0).</summary>
    public int LightMaskedDraws;
    /// <summary>Visible EffectMesh entities (portals, fountains' effect models): playing effects, drawn by world/FieldEffects.</summary>
    public readonly List<(FieldEntity entity, string archive, string entry)> EffectEntities = new();
    /// <summary>Visible dynamic Mesh entities whose model holds particle systems, with the model's particles-only effect
    /// scene: world/FieldEffects runs them, as the field's meshes draw the rest of the model.</summary>
    public readonly List<(FieldEntity entity, EffectScene particles)> ParticleEntities = new();
    /// <summary>The models of static entities with particle systems, which are not run (<see cref="IsStatic"/>).</summary>
    public readonly List<string> StaticParticleModels = new();
    /// <summary>Optional: the field load's entity count, bumped as each entity of the set is placed (see <see cref="World.LoadCount"/>).</summary>
    public World.LoadCount Load;
    /// <summary>One drawn copy of a mesh of an entity: instance <see cref="Index"/> of <see cref="Mm"/>.</summary>
    public sealed class FadeDraw { public MultiMesh Mm; public int Index; public ModelMesh Mesh; public Node3D Parent; public bool NotReceiving; public uint LightOff; }
    /// <summary>The draws of every entity BGTransparencyMgr can fade (<see cref="IsFadeTarget"/>), for world/FieldFade.</summary>
    public readonly Dictionary<FieldEntity, List<FadeDraw>> FadeDraws = new();

    /// <summary>
    /// WorldService::FUN_141bbfe30 (worldservice_1.cpp:3638-3659) records an entity for BGTransparencyMgr when its model
    /// contains MS2MapProperties (0x2ea80520) and its Transparency (0x9004569a) is true, unless the hardware instancing
    /// service lists it as excluded; only an entity made at run time by MS2EntityManagerService::CreateEntity is put on that
    /// list (ms2entitymanagerservice.cpp:166-167), never a field's own entity.
    /// </summary>
    public static bool IsFadeTarget(FieldEntity e) => e.HasMixin("MS2MapProperties") && e.Bool("Transparency");

    /// <summary>
    /// The instances (drawn and shadow-only) of every trigger mesh and trigger cube entity (<see cref="IsTriggerDrawn"/>), for
    /// world/FieldTriggers: the server shows and hides them by trigger id (TriggerObjectMesh::vfunc_2,
    /// triggeractioncli.cpp:643; TriggerObjectCube::vfunc_2, :818-822).
    /// </summary>
    public readonly Dictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> TriggerMeshDraws = new();
    /// <summary>The drawn instances of every trigger mesh entity, which a Trigger packet's fade draws alone (<see cref="World.TriggerFade"/>).</summary>
    public readonly Dictionary<FieldEntity, List<FadeDraw>> TriggerFadeDraws = new();

    /// <summary>
    /// The colliders of a trigger mesh entity: its own body (not a merged chunk) and its own overlap copies, whose collision
    /// group the Trigger packet switches (FUN_140b60e40, see world/FieldTriggers). <see cref="Group"/> is the entity's
    /// PropCollisionGroup (0xada5c5d2), the group a shown mesh gets.
    /// </summary>
    public sealed class TriggerCollider
    {
        public StaticBody3D Body;
        public TwoSidedSpace Overlap;
        public int OverlapKey = -1;
        public int Group;

        /// <summary>CPhysXHelper::FUN_141689d00 (physxhelper.cpp:326): the actor and each of its shapes go to
        /// <paramref name="group"/> (layer bit n = group n), as a field body's group meta says.</summary>
        public void SetGroup(int group)
        {
            Body.CollisionLayer = 1u << group;
            Body.SetMeta(GroupMeta, group);
            ColliderGroups.Set(Body.GetInstanceId(), group);   // a ray may have cached the old group (RayQuery.Nearest)
            if (OverlapKey >= 0) Overlap.SetLayer(OverlapKey, 1u << group);
        }
    }

    /// <summary>The colliders of every trigger mesh entity that has any (<see cref="TriggerCollider"/>).</summary>
    public readonly Dictionary<FieldEntity, TriggerCollider> TriggerColliders = new();

    /// <summary>
    /// A trigger object whose event fades the entity and switches its colliders (TriggerObject::Create,
    /// Ms2.Net.Packets.TriggerObjects.KindOf): the mesh kind (TriggerObjectMesh::vfunc_2, triggeractioncli.cpp:651,679) and
    /// the rope / ladder kind (TriggerObjectRope::vfunc_2, triggeractioncli.cpp:744,746). The trigger mesh draws and
    /// colliders above hold both.
    /// </summary>
    public static bool IsTriggerMesh(FieldEntity e) =>
        Ms2.Net.Packets.TriggerObjects.KindOf(e.HasMixin) is Ms2.Net.Packets.TriggerObjectKind.Mesh or Ms2.Net.Packets.TriggerObjectKind.Rope;

    /// <summary>
    /// A trigger object whose event shows or hides the entity's draws: a trigger mesh (<see cref="IsTriggerMesh"/>) or a
    /// trigger cube, whose event sets the entity's IsVisible alone (TriggerObjectCube::vfunc_2, triggeractioncli.cpp:818-822)
    /// and leaves its colliders.
    /// </summary>
    public static bool IsTriggerDrawn(FieldEntity e) =>
        Ms2.Net.Packets.TriggerObjects.KindOf(e.HasMixin) is Ms2.Net.Packets.TriggerObjectKind.Mesh or Ms2.Net.Packets.TriggerObjectKind.Rope
            or Ms2.Net.Packets.TriggerObjectKind.Cube;

    /// <summary>
    /// The instances (drawn and shadow-only) of every field breakable (MS2Breakable), for world/FieldBreakables: the
    /// server's Breakable state hides (state 4) and shows them (CBreakableObject::SetState, breakableobject.cpp:270).
    /// </summary>
    public readonly Dictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> BreakableMeshDraws = new();
    /// <summary>The drawn instances of every field breakable, which state 4's 500 ms fade draws alone.</summary>
    public readonly Dictionary<FieldEntity, List<FadeDraw>> BreakableFadeDraws = new();

    /// <summary>A field breakable object (CBreakableObject, the MS2Breakable mixin).</summary>
    public static bool IsBreakable(FieldEntity e) => e.HasMixin("MS2Breakable");

    /// <summary>
    /// The instances (drawn and shadow-only) of every registered vibrate object (Ms2.Ui.FieldVibrates.Registers), for
    /// world/FieldVibrateDraws: a vibrator moves them (its shake and its return's drop), and fades and shows them.
    /// </summary>
    public readonly Dictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> VibrateMeshDraws = new();
    /// <summary>The drawn instances of every registered vibrate object, which its fades draw alone.</summary>
    public readonly Dictionary<FieldEntity, List<FadeDraw>> VibrateFadeDraws = new();
    /// <summary>The colliders of every registered vibrate object: its own body, whose group a break switches
    /// (CPhysXHelper::SetEntityCollisionGroup, see world/FieldVibrateDraws).</summary>
    public readonly Dictionary<FieldEntity, TriggerCollider> VibrateColliders = new();

    /// <summary>A vibrate object of the field load's registry (WorldService m_5e8: MS2Vibrate, Enabled, a size).</summary>
    public static bool IsVibrate(FieldEntity e) => Ms2.Ui.FieldVibrates.Registers(e);

    /// <summary>
    /// The instances (drawn and shadow-only) of every field lift (MS2Liftable), for world/FieldLiftables: a lift's
    /// SetActive fades it out and shows it again (CLiftableObject::SetActive, liftableobject.cpp:371-396).
    /// </summary>
    public readonly Dictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> LiftableMeshDraws = new();
    /// <summary>The drawn instances of every field lift, which SetActive(0)'s 500 ms fade draws alone.</summary>
    public readonly Dictionary<FieldEntity, List<FadeDraw>> LiftableFadeDraws = new();

    /// <summary>A field lift (CLiftableObject, the MS2Liftable mixin; the field load's model 0x32767dd).</summary>
    public static bool IsLiftable(FieldEntity e) => e.HasMixin(Ms2.Ui.LiftableObjects.Model);

    /// <summary>
    /// The instances (drawn and shadow-only) of every MS2InteractMesh entity, for world/FieldInteractDraws: an interact object's
    /// state 2 hides them and its other states show them (CInteractMeshObject::vfunc_3, IsVisible 0xa0b6b179).
    /// </summary>
    public readonly Dictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> InteractMeshDraws = new();

    /// <summary>
    /// The instances (drawn and shadow-only) of each MS2InteractMesh entity's reactable model (asset B, which state 1 sets as
    /// the entity's NifAsset: CInteractMeshObject::vfunc_1 and FUN_1413ff690, interactobject.cpp:752-777 and :909-921;
    /// <see cref="Ms2.Ui.LiveInteractObjects.ReactableModel"/>), keyed by the entity. A twin of the entity with that NifAsset
    /// draws them in the field's groups; world/FieldInteractDraws hides them at the load and shows them in place of the
    /// entity's <see cref="InteractMeshDraws"/> while its NifAsset is asset B. They are in no other list (no fade, trigger,
    /// breakable, vibrate or lift draw), and the twin has no collision, particles or effect.
    /// STILL-OPEN: the swapped scene graph's PhysX props (the client's entity gets asset B's NIF collision); a reactable model
    /// drawn elsewhere (world/FieldProps) or played as an effect has no twin.
    /// </summary>
    public readonly Dictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> InteractReactableDraws = new();
    /// <summary>The reactable twins made by the build, with the entity each stands for.</summary>
    readonly Dictionary<FieldEntity, FieldEntity> reactableOf = new();

    /// <summary>The field's collider root (in the field's z-up space), where world/FieldInteractDraws adds an interact object's
    /// box.</summary>
    public Node3D Colliders { get; private set; }

    void AddTriggerDraw(FieldEntity e, MultiMesh mm, int index)
    {
        if (reactableOf.TryGetValue(e, out var of))
        {
            if (!InteractReactableDraws.TryGetValue(of, out var twin)) InteractReactableDraws[of] = twin = new List<(MultiMesh, int)>();
            twin.Add((mm, index));
            return;
        }
        if (e.HasMixin("MS2InteractMesh"))
        {
            if (!InteractMeshDraws.TryGetValue(e, out var own)) InteractMeshDraws[e] = own = new List<(MultiMesh, int)>();
            own.Add((mm, index));
        }
        if (IsVibrate(e))
        {
            if (!VibrateMeshDraws.TryGetValue(e, out var own)) VibrateMeshDraws[e] = own = new List<(MultiMesh, int)>();
            own.Add((mm, index));
        }
        if (IsBreakable(e))
        {
            if (!BreakableMeshDraws.TryGetValue(e, out var own)) BreakableMeshDraws[e] = own = new List<(MultiMesh, int)>();
            own.Add((mm, index));
        }
        if (IsLiftable(e))
        {
            if (!LiftableMeshDraws.TryGetValue(e, out var own)) LiftableMeshDraws[e] = own = new List<(MultiMesh, int)>();
            own.Add((mm, index));
        }
        if (!IsTriggerDrawn(e)) return;
        if (!TriggerMeshDraws.TryGetValue(e, out var list)) TriggerMeshDraws[e] = list = new List<(MultiMesh, int)>();
        list.Add((mm, index));
    }

    void AddFadeDraw(FieldEntity e, MultiMesh mm, int index, ModelMesh mesh, Node3D parent, bool notReceiving, uint lightOff)
    {
        AddTriggerDraw(e, mm, index);
        if (reactableOf.ContainsKey(e)) return;
        if (IsVibrate(e))
        {
            if (!VibrateFadeDraws.TryGetValue(e, out var own)) VibrateFadeDraws[e] = own = new List<FadeDraw>();
            own.Add(new FadeDraw { Mm = mm, Index = index, Mesh = mesh, Parent = parent, NotReceiving = notReceiving });
        }
        if (IsBreakable(e))
        {
            if (!BreakableFadeDraws.TryGetValue(e, out var own)) BreakableFadeDraws[e] = own = new List<FadeDraw>();
            own.Add(new FadeDraw { Mm = mm, Index = index, Mesh = mesh, Parent = parent, NotReceiving = notReceiving });
        }
        if (IsLiftable(e))
        {
            if (!LiftableFadeDraws.TryGetValue(e, out var own)) LiftableFadeDraws[e] = own = new List<FadeDraw>();
            own.Add(new FadeDraw { Mm = mm, Index = index, Mesh = mesh, Parent = parent, NotReceiving = notReceiving });
        }
        if (IsTriggerMesh(e))
        {
            if (!TriggerFadeDraws.TryGetValue(e, out var drawn)) TriggerFadeDraws[e] = drawn = new List<FadeDraw>();
            drawn.Add(new FadeDraw { Mm = mm, Index = index, Mesh = mesh, Parent = parent, NotReceiving = notReceiving });
        }
        if (!IsFadeTarget(e)) return;
        if (!FadeDraws.TryGetValue(e, out var list)) FadeDraws[e] = list = new List<FadeDraw>();
        list.Add(new FadeDraw { Mm = mm, Index = index, Mesh = mesh, Parent = parent, NotReceiving = notReceiving, LightOff = lightOff });
    }

    /// <summary>The blended draws with the client's alpha key, for <see cref="AlphaSort"/>.</summary>
    public readonly List<AlphaSort.Entry> AlphaEntries = new();

    sealed class ModelEntry
    {
        public ModelData Data;
        public List<(ModelMesh mesh, Transform3D local)> Meshes;
        public Aabb Bounds;              // union of the mesh bounds in model space
        public List<Shape3D> Collision;  // cooked PhysX meshes of the NIF, model space
        public List<Shape3D> Overlap;    // the same meshes for the overlap tests (TwoSidedSpace), by index
        public bool Elsewhere;           // DrawsInstance took it (per field)
        public bool AnimatesMaterial;    // a mesh has a shader-run controller (alpha, flipbook, uv scroll, morph): IsStatic splits the batch
        public EffectScene Particles;    // the model's emitting particle systems (ModelAsset.Particles); null: none
        public Vector3 SphereCenter;     // the model's NiBound in model space (ModelSphere)
        public float SphereRadius;
    }

    /// <summary>
    /// The entity scene graph's model bound: each mesh's stored model bound (NiMesh "Bounding Sphere") moved to model space,
    /// merged as NiNode::UpdateWorldBound does (NiNode.cpp:330-352: the first bound of non-zero radius, then NiBound::Merge).
    /// The client merges level by level down the node tree; here the meshes are merged in model order, flat, which gives the
    /// same sphere when the model has one mesh and a slightly different one otherwise (Merge is not associative).
    /// A similarity transform moves the merged sphere as it moves each part, so the world bound is this one moved by the
    /// entity's placement (c' = s R c + T, r' = s r: NiBound::Update).
    /// </summary>
    static (Vector3 center, float radius) ModelSphere(List<(ModelMesh mesh, Transform3D local)> meshes)
    {
        Vector3 c = default; float r = 0;
        foreach (var (m, local) in meshes)
        {
            if (m.Bound == null) continue;
            var bc = local * new Vector3(m.Bound[0], m.Bound[1], m.Bound[2]);
            float br = local.Basis.Scale.X * m.Bound[3];
            if (r == 0) { c = bc; r = br; }
            else (c, r) = AlphaSort.Merge(c, r, bc, br);
        }
        return (c, r);
    }

    /// <summary>The lights that do not reach entity <paramref name="e"/> drawn with model <paramref name="me"/>
    /// (<see cref="FieldLightSet.Unlit"/>, on its world bound in field units); 0 for all.</summary>
    uint LightOff(FieldEntity e, ModelEntry me)
    {
        if (Lights == null || Lights.Lights.Count == 0) return 0;
        var c = ModelBuilder.ToGodot(e.Transform) * me.SphereCenter;   // Z-up field space, as the entity transform
        return Lights.Unlit(e, new Vec3(c.X, c.Y, c.Z), (float)e.Scale * me.SphereRadius);
    }

    static readonly StringName LightOffParam = "ms2_light_off";

    void MaskLights(GeometryInstance3D node, uint lightOff)
    {
        if (lightOff == 0) return;
        node.SetInstanceShaderParameter(LightOffParam, (float)(lightOff & ((1u << FieldLightSet.MaskBits) - 1)));
        LightMaskedDraws++;
    }

    readonly Dictionary<string, ModelEntry> models = new(StringComparer.OrdinalIgnoreCase);

    public FieldBuilder(PackSet packs, AssetIndex assets, ModelBuilder builder)
    {
        this.packs = packs; this.assets = assets; this.builder = builder;
        builder.TextureArchives = new[] { MapArchive, ModelBuilder.TextureArchive, "Resource/Model/Effect" };   // effect meshes (fountains, torches) use Effect.m2d textures
    }

    /// <summary>MS2 (x, y, z) -> Godot (x, z, -y).</summary>
    public static Vector3 ToGodotPoint(Vec3 p) => new((float)p.X, (float)p.Z, (float)-p.Y);

    /// <summary>MS2 z-up to Godot y-up as <see cref="ToGodotPoint"/> maps it: x stays, y to -z, z to y. The basis is
    /// written with 0 and 1 entries: a rotation by -pi/2 has a float cosine of -4.4e-8, not 0, and moves points off.</summary>
    public static Transform3D ZupToYup => new(new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0)), Vector3.Zero);

    ModelEntry Model(string llid)
    {
        if (models.TryGetValue(llid, out var me)) return me;
        me = null;
        var (archive, entry) = assets.Resolve(llid);
        if (archive != null && packs.Exists(archive, entry))
        {
            // the process's model cache (NifCache): a model a field built before is not parsed again; this field holds
            // it (Lease) until it is freed
            var asset = NifCache.Model(packs, (archive + "/" + entry).ToLowerInvariant(), NifCache.CacheClass.DependTime, Lease,
                                       () => LoadAsset(archive, entry));
            if (asset != null)
            {
                if (asset.ControllersError != null) GD.PushWarning($"controllers {llid}: {asset.ControllersError}");
                me = new ModelEntry { Data = asset.Data, Meshes = asset.Meshes, Bounds = asset.Bounds, Collision = asset.Collision,
                                      Overlap = asset.Overlap, Elsewhere = DrawsInstance?.Invoke(llid, asset.Data, asset.Controllers) == true,
                                      AnimatesMaterial = asset.Data.Meshes.Any(m => m.AnimatesMaterial), Particles = asset.Particles };
                (me.SphereCenter, me.SphereRadius) = ModelSphere(asset.Meshes);
            }
        }
        if (me == null) Missing++;
        models[llid] = me;
        return me;
    }

    /// <summary>A field model as the field build uses it: its data, meshes at the file's pose, bounds, collision and
    /// its own controllers' animation (FieldProps). Shared by every field that uses the model (NifCache); read only.</summary>
    sealed class ModelAsset
    {
        public ModelData Data;
        public readonly List<(ModelMesh mesh, Transform3D local)> Meshes = new();
        public Aabb Bounds;
        public readonly List<Shape3D> Collision = new();
        public readonly List<Shape3D> Overlap = new();
        public KfAnimation Controllers;     // null: none
        public string ControllersError;
        public EffectScene Particles;       // null: no emitting particle system (ParticlesOf)
    }

    /// <summary>
    /// A field model's particle systems as an effect scene with no meshes (the field draws those). Every node of the
    /// model's graph updates with it, so its NiPSParticleSystems simulate as an effect's do (EffectInstance). A system
    /// with no emitter, or under a node that is culled in the file (its subtree is not drawn), is left out.
    /// STILL-OPEN: a visibility controller that unculls such a node over time; a static entity's systems (updated once, at
    /// add time, so nothing is emitted: FieldBuilder.IsStatic) are not run at all.
    /// </summary>
    static EffectScene ParticlesOf(NifFile nif)
    {
        if (!nif.Blocks.Any(b => b.Type is "NiPSParticleSystem" or "NiPSMeshParticleSystem")) return null;
        var sc = EffectScene.Load(nif);
        sc.Meshes.Clear();
        bool Culled(int i) => i >= 0 && (sc.Nodes[i].Culled || Culled(sc.Nodes[i].Parent));
        sc.Systems.RemoveAll(d => d.Emitters.Count == 0 || Culled(d.Node));
        return sc.Systems.Count > 0 ? sc : null;
    }

    ModelAsset LoadAsset(string archive, string entry)
    {
        var me = new ModelAsset();
        {
            try
            {
                var nif = NifFile.Load(builder.Spec, packs.Read(archive, entry));
                var md = ModelBuilder.Read(nif, archive + "/" + entry);
                me.Data = md;
                try { me.Controllers = KfAnimation.FromControllers(nif); }
                catch (Exception ex) { me.ControllersError = ex.Message; }
                try { me.Particles = ParticlesOf(nif); }
                catch (Exception ex) { GD.PushWarning($"particles {archive}:{entry}: {ex.Message}"); }
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
        return me;
    }

    public Node3D Build(FieldMap map)
    {
        foreach (var _ in BuildSteps(map)) { }
        return World;
    }

    /// <summary>The finished field from <see cref="BuildSteps"/>.</summary>
    public Node3D World { get; private set; }

    /// <summary>
    /// Marks a collision shape as an entity's: CPhysXHelper::GetEntityFromActor (physxhelper.cpp:269) finds the entity of
    /// every field prop's actor, and the hit report keeps it. The value is the entity's MapAttribute (0xde5dd580), "" when
    /// it has none.
    /// </summary>
    public static void TagGround(CollisionShape3D cs, string material) => cs.SetMeta(GroundMeta, material ?? "");

    /// <summary>
    /// The MapAttribute of the entity a touched shape owner belongs to: null when the shape has no entity
    /// (GameObjectControllerHitReport::onShapeHit, 0x140cfade0, then clears the report's entity), "" when the entity has none.
    /// </summary>
    public static string GroundMaterial(Node owner) =>
        owner != null && owner.HasMeta(GroundMeta) ? (string)owner.GetMeta(GroundMeta) : null;

    // CubeMapInformationService::GeneratePhysX: a box of GeneratePhysXDimension (150 cube when unset) standing on the
    // entity position, turned by the Z rotation only; scale is ignored
    BoxShape3D GeneratePhysXBox(FieldEntity e, out Transform3D at)
    {
        var d = e.Vector("GeneratePhysXDimension");
        var size = d.X == 0 && d.Y == 0 && d.Z == 0 ? new Vector3(150, 150, 150) : new Vector3((float)d.X, (float)d.Y, (float)d.Z);
        var p = e.Position;
        at = new Transform3D(new Basis(Vector3.Back, Mathf.DegToRad((float)e.RotationDeg.Z)),
                             new Vector3((float)p.X, (float)p.Y, (float)p.Z + size.Z / 2));
        return BoxOf(size);
    }

    // boxes of one size share one shape resource: a field has thousands of boxes of a few sizes, and every shape is a
    // wrapper the SDK tracks for the whole session; nothing changes a shape after it is built
    readonly System.Collections.Generic.Dictionary<Vector3, BoxShape3D> boxes = new();
    BoxShape3D BoxOf(Vector3 size)
    {
        if (!boxes.TryGetValue(size, out var b)) boxes[size] = b = new BoxShape3D { Size = size };
        return b;
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
        float freezeAt = ModelBuilder.FreezeTimeNow();
        var world = new Node3D { Name = "World", Transform = ZupToYup };
        building = world;
        var visuals = new Node3D { Name = "Visuals" };
        var colliders = new Node3D { Name = "Colliders" };
        Colliders = colliders;
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
                    gb.SetMeta(GroupMeta, group);
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

        // the copies that receive no sun shadow (ShadowReceivers): their meshes (opaque and blended) draw in groups of their
        // own whose ms2_receive instance uniform (field_lit.gdshader) is 0; world/FieldProps does the same for its entities
        var notReceiving = ShadowReceivers.NotReceiving(map.Entities, ShadowLevel);

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
                        // its prop goes to the scene graph with the entity (PhysXWhiteboxService.cpp:173-183) and
                        // ecrPhysX::PropService keeps it as the entity's prop (:237-238) like any other, so a touched
                        // whitebox is the hit report's entity
                        var whitebox = BoxOf(new Vector3((float)d.X, (float)d.Y, (float)d.Z));
                        var wcs = new CollisionShape3D { Shape = whitebox, Transform = xf };
                        TagGround(wcs, e.Get("MapAttribute"));
                        Chunk(e.Position, (int)e.Number("PropCollisionGroup")).AddChild(wcs);
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

                // a visible dynamic model's particle systems run (ParticlesOf), whichever draw takes its meshes; a model that
                // plays through the effect runtime (PlaysAsEffect) runs them there, so a skinned EffectMesh map model, which
                // stays on the prop path, runs them here
                var (ea, ee) = assets.Resolve(llid);
                bool asEffect = ea != null && ee != null && e.PlaysAsEffect(ea, me.Data);
                if (me.Particles != null && e.Bool("IsVisible", true) && !asEffect)
                {
                    if (IsStatic(e)) StaticParticleModels.Add(e.ModelName);
                    else ParticleEntities.Add((e, me.Particles));
                }
                if (asEffect) { EffectEntities.Add((e, ea, ee)); }
                else if (e.Bool("IsVisible", true) && me.Elsewhere)
                {
                    DrawnElsewhere.Add(e);
                    if (notReceiving.Contains(e)) NotReceivingElsewhere.Add(e);
                    if (LightOff(e, me) is var off and not 0) LightOffElsewhere[e] = off;
                }
                else if (e.Bool("IsVisible", true))
                {
                    Group(groups, llid, e, me, notReceiving.Contains(e));
                    grouped = true;            // counted when its group is placed
                }

                // DisableCollision (0x7e5f4195, DisableCollision by its uses): the client keeps the entity's actors but
                // clears their groups mask when the entity is added (WorldService::FUN_141bbd640,
                // worldservice_1.cpp:10478-10488, through CPhysXHelper::SetEntityCollisionGroup @0x141689ec0: the cube
                // actor of a GeneratePhysX entity, else each actor of its NIF PhysX props), so they are left out of the
                // world here.
                // STILL-OPEN: whether any client query that passes no groups mask still meets them
                // A trigger mesh keeps them: a Trigger packet's SetEntityCollisionGroup (FUN_140b60e40) sets its shapes'
                // groups mask again (FUN_141689d00 with no mask, physxhelper.cpp:337-350), so it collides once a packet
                // switches it; until then its body is on no layer
                bool disabled = e.Bool("DisableCollision");
                bool trigger = IsTriggerMesh(e);
                if (disabled && !trigger) continue;
                // a vibrate object's colliders also get a body of their own: its break and return switch their group
                // (CEntityVibrator, entityvibrator.cpp:338-342 and the return's SetEntityCollisionGroup)
                bool vibrate = !trigger && IsVibrate(e);
                int group = (int)e.Number("PropCollisionGroup");
                // MyPC::FUN_141524a80 (mypc.cpp:2353-2366): a state sync taken while standing on a MS2CubeProp whose
                // IsFallReturn is set and whose PropCollisionGroup is 9 or 8 carries the cube's cell. Its shapes carry the
                // entity, so the hit report and the ground ray can name it (both actors of a cube are the same entity)
                bool fallReturn = e.HasMixin("MS2CubeProp") && e.Bool("IsFallReturn") && (group == 9 || group == 8);
                var material = e.Get("MapAttribute");
                CollisionShape3D Tag(CollisionShape3D cs)
                {
                    if (fallReturn) cs.SetMeta(FallReturnMeta, entityIndex);
                    TagGround(cs, material);
                    return cs;
                }
                // PropService::HandleSceneGraphAddedMessage (PropService.cpp:460-465, exe 0x142062560) returns before
                // AddPhysXPropToPropData for an entity whose GeneratePhysX (0x38141815) is set, whatever its model: such an
                // entity gets no NIF PhysX prop, and its only collision is the generated box below
                bool generate = e.Bool("GeneratePhysX");
                // A trigger mesh's colliders get a body of their own instead of the chunk's, so the Trigger packet can move
                // them to another group (TriggerObjectMesh::vfunc_2 -> FUN_140b60e40; world/FieldTriggers). The body is made
                // when the first shape comes, in the group it has at field load: the PropCollisionGroup for the NIF's
                // meshes, 9 for the GeneratePhysX box (one or the other, never both: see below)
                TriggerCollider tc = null;
                StaticBody3D Body(int at)
                {
                    if (!trigger && !vibrate) return Chunk(e.Position, at);
                    if (tc == null)
                    {
                        tc = new TriggerCollider { Group = group, Overlap = twoSided };
                        tc.Body = new StaticBody3D { Name = $"{(vibrate ? "Vibrate" : "Trigger")}_{entityIndex}", CollisionLayer = disabled ? 0u : 1u << at, CollisionMask = 0 };
                        tc.Body.SetMeta(GroupMeta, at);
                        colliders.AddChild(tc.Body);
                        if (vibrate) VibrateColliders[e] = tc; else TriggerColliders[e] = tc;
                    }
                    return tc.Body;
                }
                if (generate && me.Collision.Count > 0) SkippedNifMeshes++;
                else if (me.Collision.Count > 0)
                {
                    // the NIF's own PhysX meshes (deform pieces, stairs, water surface, props), every shape in the entity's
                    // PropCollisionGroup (ecrPhysX PropService::SetCollisionGroup, PropService.cpp:1471-1499)
                    for (int i = 0; i < me.Collision.Count; i++)
                    {
                        Body(group).AddChild(Tag(new CollisionShape3D { Shape = me.Collision[i], Transform = xf }));
                        if (tc == null) twoSided.Add(me.Overlap[i], xf, group);
                        else
                        {
                            if (tc.OverlapKey < 0) tc.OverlapKey = twoSided.AddOwn(disabled ? 0u : 1u << group);
                            twoSided.AddOwn(tc.OverlapKey, me.Overlap[i], xf);
                        }
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
                    Body(GeneratedBoxGroup).AddChild(box);
                    Boxes++;
                }
            }
            finally { if (!grouped) Load?.Add(); }      // nothing of it is left to place
        }

        // the interact meshes' reactable models (InteractReactableDraws): a twin of the entity with asset B as its NifAsset joins
        // the groups as the entity would with that asset (same transform, colour, lights and shadow receipt)
        var interactRows = InteractObjectTable.Shared(packs);
        foreach (var e in map.Entities)
        {
            var llidB = Ms2.Ui.LiveInteractObjects.ReactableModel(e, interactRows);
            if (llidB == null) continue;
            yield return 0;
            var me = Model(llidB);
            if (me == null || me.Elsewhere) continue;
            var raw = new XEntity { Id = e.Raw.Id, ModelName = e.Raw.ModelName, Name = e.Raw.Name, Sets = e.Raw.Sets,
                                    Props = new Dictionary<string, string>(e.Raw.Props, StringComparer.Ordinal) };
            raw.Props["NifAsset"] = llidB;
            var twin = new FieldEntity { Raw = raw, Flat = e.Flat };
            var (ba, be) = assets.Resolve(llidB);
            if (ba != null && be != null && twin.PlaysAsEffect(ba, me.Data)) continue;
            reactableOf[twin] = e;
            Group(groups, llidB, twin, me, notReceiving.Contains(e));
            ReactableTwins++;
        }

        foreach (var (key, ents) in groups)
        {
            var me = models[key.Split('\n')[0]];
            bool noRecv = key.Contains(NotReceivingKey, StringComparison.Ordinal);
            uint lightOff = groupLightOff.TryGetValue(key, out var lo) ? lo : 0;
            bool frozen = key.EndsWith(FrozenKey, StringComparison.Ordinal);
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
                    if (frozen) { ModelBuilder.Freeze(shared, freezeAt); FrozenGroups++; }
                    var glow = builder.GlowMaterial(mesh, mesh.Uvs1 != null, frozen ? freezeAt : -1f);
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
                            AddFadeDraw(obj[i], om, i, mesh, visuals, noRecv, lightOff);
                        }
                        var on = new MultiMeshInstance3D { Name = $"{ents[0].ModelName}{mi}_o{k}", Multimesh = om, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                        if (noRecv) { on.SetInstanceShaderParameter(ReceiveParam, 0f); NotReceivingDraws++; NotReceivingBlended++; }
                        MaskLights(on, lightOff);
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
                if (frozen) { ModelBuilder.Freeze(mm.Mesh, freezeAt); FrozenGroups++; }
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
                    AddFadeDraw(ents[i], mm, i, mesh, visuals, noRecv, lightOff);
                }
                // the sun's shadow: a MultiMesh is culled as a whole, and its box spans the field, so as one caster every copy is drawn
                // again in every shadow cascade. The shadow casters are per-cell twins (tight boxes, culled per cascade); the visible
                // instances stay one MultiMesh per mesh. A blended mesh gets none: its BLEND/ADD (depth_draw_never) or SHELL material
                // is transparent, and Godot leaves transparent materials out of the shadow pass (observed: Henesys, Ellinia and Tria
                // stills are pixel-identical with and without its casters). The glow twin casts none.
                // STILL-OPEN: the client's shadow culling keeps a blended mesh whose own material alpha is 1
                // (ShadowCullingProcess::Process, unit_141753550.cpp:114-115, as ModelBuilder.ShadowCullingKeeps); 1471 of the
                // 3846 blended map meshes in the restore data are such, and here they cast nothing.
                var node = new MultiMeshInstance3D { Name = $"{ents[0].ModelName}{mi++}", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                if (noRecv) { node.SetInstanceShaderParameter(ReceiveParam, 0f); NotReceivingDraws++; if (mesh.AlphaBlendOn) NotReceivingBlended++; }
                MaskLights(node, lightOff);
                visuals.AddChild(node);
                if (!mesh.AlphaBlendOn && !mesh.NoShadow && CastsShadows) AddShadowCells(visuals, ents[0].ModelName, mm.Mesh, ents.Where(e => e.CastsShadow).ToList(), local);
                if (mesh.AlphaBlendOn) SortBlended(node, mesh, shell, ents, local);
                if (builder.GlowMaterial(mesh, mesh.Uvs1 != null, frozen ? freezeAt : -1f) is { } gm)
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
            int placed = ents.Count;                    // the group's entities are placed (a reactable twin is not one)
            if (reactableOf.Count > 0) foreach (var x in ents) if (reactableOf.ContainsKey(x)) placed--;
            Load?.Add(placed);
            yield return 0;
        }
        World = world;
        building = null;
    }

    /// <summary>The reactable twins the build drew (<see cref="InteractReactableDraws"/>).</summary>
    public int ReactableTwins;

    /// <summary>Puts a visible entity drawn with <paramref name="llid"/> in its group: copies reached by different field lights
    /// draw apart, each with its light mask (the client's instancing batches split on their nearby-light list the same way,
    /// Unk_MeshInstancingConfig::Equals).</summary>
    void Group(Dictionary<string, List<FieldEntity>> groups, string llid, FieldEntity e, ModelEntry me, bool noRecv)
    {
        uint off = LightOff(e, me);
        var key = BatchKey(llid, e) + (noRecv ? NotReceivingKey : "")
                  + (off != 0 ? LightKey + off : "")
                  + (me.AnimatesMaterial && IsStatic(e) ? FrozenKey : "");
        if (off != 0) groupLightOff[key] = off;
        if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<FieldEntity>();
        list.Add(e);
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
    /// instance colours, shadow only. The same triangles reach the shadow map as from the drawn MultiMesh, culled per cell.
    /// Only the copies whose FieldEntity.CastsShadow holds are passed (IsCastingShadow: grass, flowers, fences, water).</summary>
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
                AddTriggerDraw(list[i], mm, i);
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

    /// <summary>The group key suffix of copies some field lights do not reach, followed by their light bits.</summary>
    const string LightKey = "\nlightoff";
    readonly Dictionary<string, uint> groupLightOff = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The group key suffix of copies that receive no sun shadow (after BatchKey; '\n' never ends one).</summary>
    const string NotReceivingKey = "\nnorecv";
    static readonly StringName ReceiveParam = "ms2_receive";
    /// <summary>MultiMeshes (opaque and blended) drawn with the sun's shadow off (ShadowReceivers).</summary>
    public int NotReceivingDraws, NotReceivingBlended;

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
    /// SceneGraphService::OnEntityAdded (SceneGraphService.cpp:2377-2380): an entity is static when its model contains Bakeable
    /// and its IsStatic is true (false when unset), and SetIsDynamic(!isStatic). A static entity goes to AddToUpdateOnce
    /// (:1255-1262) and is updated once, at the game time it is added; a dynamic one is updated every tick on the game clock
    /// (MS2SceneGraphService UpdateDynamicEntities @0x1414f12f0, every bucket through UpdateBucket @0x1414f1f50, with no culling).
    /// </summary>
    public static bool IsStatic(FieldEntity e) => e.HasMixin("Bakeable") && e.Bool("IsStatic");

    /// <summary>The batch-key suffix of static entities whose model animates a material: their meshes hold the add time.</summary>
    const string FrozenKey = "\nstatic";
    public int FrozenGroups;

    /// <summary>
    /// The draw group of a placed entity: its llid, split by shell group; an own-group shell further by the instancing batch.
    /// Unk_MeshInstancingConfig::Equals matches a shell batch (UseShellRender) on the asset, IsCastingShadow,
    /// IsReceivingShadow and MaterialColor, and not on the instancing cell. Its nearby-light list (FUN_1413d7160) splits
    /// batches too; the light-mask suffix of the group key (LightOff) does that where the lit sets differ.
    /// </summary>
    static string BatchKey(string llid, FieldEntity e) => ShellOf(e) switch
    {
        Shell.Shared => llid + "\n1",
        Shell.Own => $"{llid}\n2\n{e.Get("MaterialColor")}\n{e.Bool("IsCastingShadow", true)}\n{e.Bool("IsReceivingShadow", true)}",
        _ => llid,
    };
}
