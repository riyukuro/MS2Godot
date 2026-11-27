using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot.World;

/// <summary>
/// BGTransparencyMgr (bgtransparencymgr.cpp): a field prop between the player and the camera fades to its
/// TransparencyAlpha and back. The targets are FieldBuilder's <see cref="FieldBuilder.FadeDraws"/> (the entities
/// WorldService::FUN_141bbfe30 records, worldservice_1.cpp:3638-3659).
///
/// Tick (0x140a0d000): UpdateFadeOut, UpdateFadeIn, and every 300 ms vfunc_1 (0x140a0db00). The user flags start at 1
/// (BGTransparencyMgr ctor), so the test runs. vfunc_1, for the player (the object MyPC::vfunc_10 adds): two rays from the feet
/// (vfunc_18) raised by GameObject's m_fUnk110 = 20 and m_fUnk114 = 100 (gameobject.cpp:197-198), each toward the camera
/// position, a direction only; each is picked (RayHits: PickService::PerformPick, front only false, closest false) against
/// every target whose world bound sphere is in the camera frustum (WorldService::FUN_141bc7920). A hit adds the entity and,
/// with a TransparencyGroupID above 0, every target of its group. Apply (0x140a0e330): no hit sends every faded entity back
/// (MoveAllToFadeIn); otherwise each faded entity not hit goes back (MoveMissingToFadeIn), and each hit entity in neither
/// map starts fading out: its alpha floor is property 0x188f01c1 (0.3 when unset), its meshes get a TransparencyStandardMaterial,
/// its timer starts at 300 ms. UpdateFadeOut sets each running element's materials to clamp(1 - ratio, alpha, 1);
/// UpdateFadeIn to clamp(ratio, alpha, 1), and a stopped fade-in releases the materials. An entity fading in is skipped by
/// Apply until it is released. The timers and the 300 ms test read the game clock (GameTimeClock), which moves at most
/// m_maxDelta = 0.25 s per frame (GameTimeClock.cpp:34, 108); <see cref="clock"/> moves the same way.
///
/// The instanced entity leaves its instancing while it fades (0xa0b6b179 set when HardwareInstancingService::FUN_1413d6f40
/// holds, cleared on release): here its copy in the shared MultiMesh is hidden and the entity draws alone with the
/// faded material (<see cref="ModelBuilder.FadeMaterial"/>), after a depth-only pass of its own (<see cref="ModelBuilder.FadeShell"/>:
/// Apply marks the entity into an alpha shell group of its own, AlphaShellComposite::MarkScene). The client sorts each
/// shell group among the transparent draws by its distance (AlphaShellComposite::Update, CompareAlpha); here each held
/// entity gets its own pair of render priorities above every other transparent draw (priority 0), the farthest the lowest
/// (<see cref="Sort"/>), so the depth pass of one draws right before its colour and a faded entity behind another shows
/// through it. Not modelled: the sorting of the faded entities (SortEntities), a transparent draw nearer than a faded
/// entity (it draws before the faded one here), the order kept for the 300 ms between tests, the shadow and glow of a faded entity; the frustum test uses the entity's
/// merged mesh box for its NiBound.
///
/// Only a mesh whose active material is an MS2StandardMaterial is faded (Apply, bgtransparencymgr.cpp:517-520: the
/// DynamicCast of GetActiveMaterial); a glass mesh or one without a material keeps its own draw, and the entity is in the map
/// all the same.
///
/// The props world/FieldProps draws (models that animate themselves, an entity's own or a reactable twin's) are targets
/// too, as the same entity (<see cref="UseProps"/>). Their pick is PerformPick's on each mesh as it is now: the ray must
/// meet the mesh's world bound first (FindIntersectionsRenderObject, FindBoundIntersect, NiCollisionUtils.inl:24-46);
/// then SimpleSkinPickObjectPolicy (NiPick.cpp:97-118) tests its triangles, the POSITION stream as stored at the mesh's
/// world transform, unskinned for a skinned mesh (ModelMesh.PickToRoot), and failing that a skinned mesh's bone bounds,
/// each at its bone's world transform (FindIntersectionsSkinnedMeshBounds, NiCollisionTraversals.cpp:2332-2393), which
/// only a RECOMPUTE_BOUNDS skin has (ModelMesh.BoneBounds). A skinned mesh's world bound is its merged bone bounds
/// (CharacterAlphaSort.SkinnedBound), else its model bound at the mesh node. A faded prop mesh draws with the faded
/// material as its override. Their frustum sphere is the merge of the meshes' world bounds now, read once per test for the
/// frustum and both rays, and only for a target whose outer sphere (<see cref="Outer"/>) is in the frustum and near a ray.
///
/// STILL-OPEN: the POSITION lock needs the stream's CPU read access (NiDataStreamElementLock in
/// FindIntersectionsMeshPosition); the stream access flags are not read here, so every mesh is picked as if readable.
/// A skinned mesh's node is placed at its file pose under the skeleton (ModelMesh.BoundToRoot), as for its alpha key.
///
/// STILL-OPEN: the camera option's user flags. BGTransparencyMgr::SetUserFlags is fed from the game option
/// (gameoption.cpp:534, mypc.cpp:1002), and m_nFlags 0 turns the test off and sends every faded entity back to fade-in;
/// there is no such option here yet, so this runs as if the flag were always set. vfunc_1's GameObject gate (FUN_14123c660
/// 0 or class 0x6200004, bgtransparencymgr.cpp:390) is left out too: it never stops the player.
/// </summary>
public partial class FieldFade : Node
{
    const double TestPeriodMs = 300, FadeMs = 300;
    /// <summary>GameTimeClock's m_maxDelta: the game clock moves at most 0.25 s per update (GameTimeClock.cpp:34, 108).</summary>
    const double MaxStepMs = 250;
    const float RayLow = 20f, RayHigh = 100f;
    static readonly StringName ReceiveParam = "ms2_receive";
    static readonly StringName LightOffParam = "ms2_light_off";

    sealed class Target
    {
        public FieldEntity Entity;
        public List<FieldBuilder.FadeDraw> Draws;
        public int Group;
        public Vector3 Centre; public float Radius;
        /// <summary>The sphere of <see cref="Draws"/> alone (they do not move).</summary>
        public Vector3 DrawsCentre; public float DrawsRadius;
        /// <summary>The entities world/FieldProps draws for it (its own, a reactable twin), and their meshes once built.</summary>
        public List<FieldEntity> PropEntities;
        public List<PropPick> Props;
        /// <summary>A sphere round every prop mesh, made once when the props are built (<see cref="PropsBuilt"/>): the test reads
        /// no node or bone of a target whose sphere is outside the frustum.</summary>
        public Vector3 OuterCentre; public float OuterRadius;
        /// <summary>Whether the outer sphere holds every prop mesh (false while one is out of the tree: the test then reads them all).</summary>
        public bool OuterAll;
    }

    /// <summary>One mesh of an animated prop, with what its pick needs.</summary>
    sealed class PropPick
    {
        public FieldProps.PropMesh M;
        public bool HasBound; public Vector3 Centre; public float Radius;   // the model bound
        public Transform3D PickToSkel, BoundToSkel;                         // skinned: relative to the skeleton
        public int[] Bones; public Vector3[] BoneCentres; public float[] BoneRadii;   // RECOMPUTE_BOUNDS
        // read once per test (Refresh), for the frustum sphere and both rays: whether it is drawn, the skeleton's world
        // transform (skinned) or the node's, the bones' poses under the skeleton, where the pick places the vertices,
        // and the world bound
        public bool Shown; public Transform3D Xf, PickXf; public Transform3D[] Poses; public Vector3 WC; public float WR;
    }

    sealed class Element
    {
        public float Alpha;
        /// <summary>When the timer started, on the fade clock (<see cref="clock"/>, milliseconds).</summary>
        public double Start;
        /// <summary>The element's place in <see cref="Sort"/> (-1: not placed yet).</summary>
        public int Rank = -1;
        public bool Running(double now) => now - Start < FadeMs;
        public float Ratio(double now) => Math.Min((float)((now - Start) / FadeMs), 1f);
        public readonly List<Part> Parts = new();
    }

    /// <summary>One faded draw (<see cref="draw"/>), or one faded prop mesh (<see cref="prop"/>). <see cref="TestRef"/> is the alpha-test reference of its cloned NiAlphaProperty
    /// (TransparencyMaterial::FUN_141860140), which every vfunc_4 call scales; it starts again from the mesh's own
    /// reference only when a new element clones the property again.</summary>
    sealed class Part
    {
        public FieldBuilder.FadeDraw draw; public Transform3D xf; public MultiMeshInstance3D node; public ShaderMaterial mat;
        /// <summary>The depth-only first pass the draw shows (<see cref="ModelBuilder.FadeShell"/>), <see cref="mat"/> its next pass.</summary>
        public ShaderMaterial shell;
        public MeshInstance3D prop;
        public ModelMesh model;
        public int TestRef;
        /// <summary>The draw's world bound centre (its box's, at its held transform), or the prop mesh's pick, whose bound
        /// each test reads (Refresh).</summary>
        public Vector3 centre; public PropPick pick;
        public Vector3 Centre => pick != null ? pick.WC : centre;
        public void SetFade(float a) => ModelBuilder.SetFade(mat, shell, model, a, ref TestRef);

        /// <summary>The faded draw goes: the copy is freed, or the prop mesh's override is taken off.</summary>
        public void Free()
        {
            if (draw != null) node.QueueFree();
            else if (GodotObject.IsInstanceValid(prop) && prop.MaterialOverride == shell) prop.MaterialOverride = null;
        }
    }

    readonly List<Target> targets = new();
    readonly Dictionary<int, List<Target>> groups = new();
    readonly Dictionary<FieldEntity, Target> byEntity = new();
    readonly List<Target> propTargets = new();
    FieldProps props;
    readonly Dictionary<(MultiMesh, int), Target> byDraw = new();
    readonly Dictionary<Target, Element> fadeOut = new(), fadeIn = new();
    // each mesh to its triangles, made once. Keyed by the mesh: the split alpha path makes several MultiMeshes over one mesh,
    // which share one copy
    readonly Dictionary<Mesh, Vector3[]> faces = new(ReferenceEqualityComparer.Instance);
    /// <summary>How many times a mesh's triangles were copied out of the engine (GetFaces).</summary>
    public int FacesBuilt { get; private set; }
    readonly Func<Vector3?> feet;
    readonly Func<Camera3D> camera;
    double nextTest;
    public int Faded => fadeOut.Count;

    public FieldFade(Dictionary<FieldEntity, List<FieldBuilder.FadeDraw>> draws, Func<Vector3?> feet, Func<Camera3D> camera)
    {
        Name = "FieldFade";
        this.feet = feet; this.camera = camera;
        foreach (var (e, list) in draws)
        {
            var t = new Target { Entity = e, Draws = list, Group = (int)e.Number("TransparencyGroupID", -1) };
            var box = new Aabb();
            bool first = true;
            foreach (var d in list)
            {
                byDraw[(d.Mm, d.Index)] = t;
                var b = d.Parent.GlobalTransform * d.Mm.GetInstanceTransform(d.Index) * d.Mm.Mesh.GetAabb();   // the instances are in the field's Z-up frame
                box = first ? b : box.Merge(b);
                first = false;
            }
            t.Centre = t.DrawsCentre = box.GetCenter(); t.Radius = t.DrawsRadius = box.Size.Length() / 2;
            AddTarget(t);
        }
    }

    void AddTarget(Target t)
    {
        targets.Add(t);
        byEntity[t.Entity] = t;
        if (t.Group > 0)
        {
            if (!groups.TryGetValue(t.Group, out var g)) groups[t.Group] = g = new List<Target>();
            g.Add(t);
        }
    }

    /// <summary>
    /// The props world/FieldProps draws (<paramref name="drawn"/>, FieldBuilder.DrawnElsewhere) whose entity is a target
    /// (FieldBuilder.IsFadeTarget): an entity's own, or the twin of a reactable model (<paramref name="reactableTwins"/>, by
    /// its entity), which fades with its entity as the static twin does. Their meshes are taken once the props are built.
    /// </summary>
    public void UseProps(FieldProps fieldProps, IEnumerable<FieldEntity> drawn, IReadOnlyDictionary<FieldEntity, FieldEntity> reactableTwins)
    {
        props = fieldProps;
        if (props == null) return;
        var ownerOf = new Dictionary<FieldEntity, FieldEntity>();
        if (reactableTwins != null) foreach (var (e, twin) in reactableTwins) ownerOf[twin] = e;
        foreach (var p in drawn)
        {
            var e = ownerOf.TryGetValue(p, out var o) ? o : p;
            if (!FieldBuilder.IsFadeTarget(e)) continue;
            if (!byEntity.TryGetValue(e, out var t))
            {
                t = new Target { Entity = e, Draws = new List<FieldBuilder.FadeDraw>(), Group = (int)e.Number("TransparencyGroupID", -1), DrawsRadius = -1 };
                AddTarget(t);
            }
            if (t.PropEntities == null) { t.PropEntities = new List<FieldEntity>(); propTargets.Add(t); }
            t.PropEntities.Add(p);
        }
    }

    /// <summary>
    /// The animated props' fade passes, as <see cref="Warm"/> does the draws': every prop target's meshes taken
    /// (<see cref="PropsBuilt"/>) and one hidden copy per new format. Field calls it once FieldContents.Start has built the
    /// props, in the load's last frame; the loading screen covers the frames after it (FieldContents' settle), so the copies
    /// are compiled there with the props themselves, not in the first test's frame. A target whose props are not all built
    /// is left to the test, as before.
    /// </summary>
    public void WarmProps()
    {
        if (props == null) return;
        foreach (var t in propTargets) PropsBuilt(t);
    }

    /// <summary>The target's prop meshes, made once every one of its props is built; false before.</summary>
    bool PropsBuilt(Target t)
    {
        if (t.Props != null) return true;
        foreach (var p in t.PropEntities) if (props.NodeOf(p) == null) return false;
        t.Props = new List<PropPick>();
        foreach (var p in t.PropEntities)
            foreach (var m in props.MeshesOf(p))
            {
                var mesh = m.Mesh;
                var pk = new PropPick { M = m };
                if (mesh.Bound != null) { pk.HasBound = true; pk.Centre = new Vector3(mesh.Bound[0], mesh.Bound[1], mesh.Bound[2]); pk.Radius = mesh.Bound[3]; }
                if (mesh.Skinned)
                {
                    pk.PickToSkel = ModelBuilder.ToGodot(mesh.PickToRoot);
                    pk.BoundToSkel = ModelBuilder.ToGodot(mesh.BoundToRoot);
                    if (mesh.BoneBounds is { Count: > 0 } bb)
                    {
                        pk.Bones = new int[bb.Count]; pk.BoneCentres = new Vector3[bb.Count]; pk.BoneRadii = new float[bb.Count];
                        pk.Poses = new Transform3D[bb.Count];
                        for (int i = 0; i < bb.Count; i++)
                        {
                            pk.Bones[i] = bb[i].Bone;   // the skeleton's bones are the model's, in order (FieldProps.BuildOne)
                            pk.BoneCentres[i] = new Vector3(bb[i].Bound[0], bb[i].Bound[1], bb[i].Bound[2]);
                            pk.BoneRadii[i] = bb[i].Bound[3];
                        }
                        pk.BoundToSkel = ModelBuilder.ToGodot(mesh.BoneBoundsToRoot);
                        pk.HasBound = true;
                    }
                }
                t.Props.Add(pk);
            }
        Outer(t);
        foreach (var pk in t.Props) WarmProp(pk);
        return true;
    }

    /// <summary>
    /// The target's outer sphere: its draws' sphere merged with every prop mesh's world bound at build (drawn or not), the
    /// props' radius doubled. A prop is placed once (FieldProps) and its animation moves its meshes about their place, so a
    /// mesh's bound stays within it while it moves less than its own radius again.
    /// STILL-OPEN: a prop whose animation carries a mesh farther than that (a long translation) can leave the outer sphere
    /// while its bound is in the frustum, and is then not tested; the animations' extents are not read here.
    /// </summary>
    void Outer(Target t)
    {
        OuterBuilds++;
        Skeleton3D skel = null; Transform3D sx = default;
        Vector3 c = default; float r = -1;
        t.OuterAll = true;
        foreach (var pk in t.Props)
        {
            if (!GodotObject.IsInstanceValid(pk.M.Node) || !pk.M.Node.IsInsideTree()) { t.OuterAll = false; continue; }
            Read(pk, ref skel, ref sx);
            if (r < 0) { c = pk.WC; r = pk.WR; }
            else (c, r) = AlphaSort.Merge(c, r, pk.WC, pk.WR);
        }
        if (r >= 0) r *= 2;
        if (t.DrawsRadius >= 0) (c, r) = r < 0 ? (t.DrawsCentre, t.DrawsRadius) : AlphaSort.Merge(t.DrawsCentre, t.DrawsRadius, c, r);
        t.OuterCentre = c; t.OuterRadius = r;
    }

    // the fade's passes already built for a mesh format: (additive form, culling, vertex format, prop copy), which give
    // the two passes' shaders (ModelBuilder.FadeForm) and so their pipelines
    readonly HashSet<(bool, string, ulong, bool)> warmed = new();
    /// <summary>The hidden copies <see cref="Warm"/> and <see cref="WarmProps"/> (or a test taking a prop target first) made.</summary>
    public int Warmed { get; private set; }
    /// <summary>The prop targets' outer spheres built (<see cref="Outer"/>): once per target, and again only when a target that
    /// had a node out of the tree has them all back.</summary>
    public int OuterBuilds { get; private set; }

    /// <summary>A mesh's colour material, the one Apply fades (a fluid shell's colour pass, not its depth pass).</summary>
    static ShaderMaterial Colour(Mesh mesh)
    {
        if (mesh?.GetSurfaceCount() is not > 0 || mesh.SurfaceGetMaterial(0) is not ShaderMaterial colour) return null;
        return colour.NextPass is ShaderMaterial next && colour.Shader != next.Shader ? next : colour;
    }

    static ulong Format(Mesh mesh) => mesh is ArrayMesh am ? (ulong)am.SurfaceGetFormat(0) : 0;

    /// <summary>
    /// Builds the fade's two passes (<see cref="ModelBuilder.FadeMaterial"/>, <see cref="ModelBuilder.FadeShell"/>) for the
    /// static draws of every target, at the field load and behind the loading screen: one hidden copy, made as Apply makes
    /// its own, for each vertex format and pair of fade shaders the targets take. Godot builds a shader and its pipelines
    /// for a mesh when an instance of it enters the scene (its pipeline precompile), hidden or not; without the copies
    /// that happened in the frame of the first fade. The animated props are built later (FieldContents.Start) and warmed
    /// then (<see cref="WarmProps"/>). The copies stay hidden for the field's life.
    /// </summary>
    public void Warm()
    {
        foreach (var t in targets)
            foreach (var d in t.Draws)
            {
                if (!ModelData.IsStandardMaterial(d.Mesh.ActiveMaterial)) continue;
                var (add, cull) = ModelBuilder.FadeForm(d.Mesh);
                if (warmed.Contains((add, cull, Format(d.Mm.Mesh), false)) || Colour(d.Mm.Mesh) is not { } colour) continue;
                warmed.Add((add, cull, Format(d.Mm.Mesh), false));
                var shell = ModelBuilder.FadeShell(ModelBuilder.FadeMaterial(colour, d.Mesh), d.Mesh);
                var one = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = d.Mm.Mesh, InstanceCount = 1 };
                one.SetInstanceTransform(0, d.Mm.GetInstanceTransform(d.Index));
                d.Parent.AddChild(new MultiMeshInstance3D { Name = "warm_pass", Multimesh = one, MaterialOverride = shell, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false });
                Warmed++;
            }
    }

    /// <summary>A prop mesh's fade passes, as <see cref="Warm"/>: a hidden copy of its mesh beside it.</summary>
    void WarmProp(PropPick pk)
    {
        var node = pk.M.Node;
        if (!ModelData.IsStandardMaterial(pk.M.Mesh.ActiveMaterial) || !GodotObject.IsInstanceValid(node) || node.GetParent() is not Node3D parent
            || Colour(node.Mesh) is not { } colour) return;
        var (add, cull) = ModelBuilder.FadeForm(pk.M.Mesh);
        if (!warmed.Add((add, cull, Format(node.Mesh), true))) return;
        var shell = ModelBuilder.FadeShell(ModelBuilder.FadeMaterial(colour, pk.M.Mesh), pk.M.Mesh);
        parent.AddChild(new MeshInstance3D { Name = "warm_pass", Mesh = node.Mesh, Transform = node.Transform, MaterialOverride = shell, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false });
        Warmed++;
    }

    static bool AllInTree(Target t)
    {
        foreach (var pk in t.Props) if (!GodotObject.IsInstanceValid(pk.M.Node) || !pk.M.Node.IsInsideTree()) return false;
        return true;
    }

    /// <summary>Whether the prop mesh is drawn now (its node, or the entity's, not hidden; the node not freed).</summary>
    static bool Drawn(PropPick pk) => GodotObject.IsInstanceValid(pk.M.Node) && pk.M.Node.IsVisibleInTree();

    /// <summary>
    /// Reads the prop mesh's transforms once (<paramref name="skel"/> and <paramref name="sx"/> carry the last skeleton's world
    /// transform: the meshes of one prop share it) and its world bound from them (NiBound::Update at its world transform; a
    /// RECOMPUTE_BOUNDS skin's from its bones, as CharacterAlphaSort.SkinnedBound), or without a model bound the box of its
    /// mesh where the pick places it.
    /// </summary>
    static void Read(PropPick pk, ref Skeleton3D skel, ref Transform3D sx)
    {
        if (pk.M.Mesh.Skinned)
        {
            if (!ReferenceEquals(pk.M.Skel, skel)) { skel = pk.M.Skel; sx = skel.GlobalTransform; }
            pk.Xf = sx;
            pk.PickXf = sx * pk.PickToSkel;
        }
        else pk.Xf = pk.PickXf = pk.M.Node.GlobalTransform;
        if (pk.Bones != null)
        {
            Vector3 c = default; float r = 0;
            for (int i = 0; i < pk.Bones.Length; i++)
            {
                var pose = pk.Poses[i] = pk.M.Skel.GetBoneGlobalPose(pk.Bones[i]);
                var bc = pose * pk.BoneCentres[i];
                float br = pose.Basis.Scale.X * pk.BoneRadii[i];
                if (i == 0) { c = bc; r = br; }
                else (c, r) = AlphaSort.Merge(c, r, bc, br);
            }
            var w = pk.Xf * pk.BoundToSkel;
            pk.WC = w * c; pk.WR = w.Basis.Scale.X * r;
        }
        else if (pk.HasBound)
        {
            var x = pk.M.Mesh.Skinned ? pk.Xf * pk.BoundToSkel : pk.Xf;
            pk.WC = x * pk.Centre; pk.WR = x.Basis.Scale.X * pk.Radius;
        }
        else
        {
            var box = pk.PickXf * pk.M.Node.GetAabb();
            pk.WC = box.GetCenter(); pk.WR = box.Size.Length() / 2;
        }
    }

    /// <summary>The frustum sphere of a target with props: its draws' sphere merged with each drawn prop mesh's bound now;
    /// a radius below 0 when nothing of it is drawn. Each drawn mesh's transforms and bones are read here once, for the
    /// test's two rays as well.</summary>
    static void Refresh(Target t)
    {
        Vector3 c = t.DrawsCentre; float r = t.DrawsRadius;
        Skeleton3D skel = null; Transform3D sx = default;
        foreach (var pk in t.Props)
        {
            if (!(pk.Shown = Drawn(pk))) continue;
            Read(pk, ref skel, ref sx);
            if (r < 0) { c = pk.WC; r = pk.WR; }
            else (c, r) = AlphaSort.Merge(c, r, pk.WC, pk.WR);
        }
        t.Centre = c; t.Radius = r;
    }

    /// <summary>FindRaySphereIntersectHelper / FindBoundIntersect (NiCollisionUtils.cpp:96-150, NiCollisionUtils.inl:24-46) for
    /// a unit direction: an origin inside the sphere hits; else the sphere must be ahead and the ray come within it.</summary>
    static bool SphereHit(Vector3 o, Vector3 dir, Vector3 c, float r)
    {
        var d = o - c;
        float cc = d.LengthSquared() - r * r;
        if (cc <= 0) return true;
        float b = d.Dot(dir);
        if (b >= 0) return false;
        return b * b - cc >= 0;
    }

    /// <summary>PerformPick on one prop mesh (SimpleSkinPickObjectPolicy behind the bound test), at the transforms
    /// <see cref="Refresh"/> read.</summary>
    bool PropHits(PropPick pk, Vector3 from, Vector3 dir)
    {
        if (pk.HasBound && !SphereHit(from, dir, pk.WC, pk.WR)) return false;
        if (HitsFaces(pk.M.Node.Mesh, pk.PickXf, from, dir)) return true;
        if (pk.Bones == null) return false;
        for (int i = 0; i < pk.Bones.Length; i++)
        {
            var bone = pk.Xf * pk.Poses[i];
            if (SphereHit(from, dir, bone * pk.BoneCentres[i], bone.Basis.Scale.X * pk.BoneRadii[i])) return true;
        }
        return false;
    }

    /// <summary>Any triangle of the mesh at <paramref name="xf"/>, either side, ahead of the origin.</summary>
    bool HitsFaces(Mesh mesh, Transform3D xf, Vector3 from, Vector3 dir)
    {
        if (mesh == null) return false;
        var inv = xf.AffineInverse();
        var o = inv * from;
        var v = inv.Basis * dir;
        if (!faces.TryGetValue(mesh, out var f)) { faces[mesh] = f = mesh.GetFaces(); FacesBuilt++; }
        for (int i = 0; i + 2 < f.Length; i += 3)
            if (Triangle(o, v, f[i], f[i + 1], f[i + 2])) return true;
        return false;
    }

    public override void _EnterTree() => TriggerFade.Switched += OnSwitched;
    public override void _ExitTree() => TriggerFade.Switched -= OnSwitched;

    /// <summary>
    /// A trigger, breakable, vibrate, liftable or interact switch set one of the draws a fade holds (TriggerFade.Switched): its
    /// copy goes and the transform the switch wrote stays. An entity left with no copy leaves the fade, so the next test starts
    /// it again from the instances as they are.
    /// </summary>
    void OnSwitched(MultiMesh mm, int index)
    {
        if (!byDraw.TryGetValue((mm, index), out var t)) return;
        Drop(fadeOut, t, mm, index);
        Drop(fadeIn, t, mm, index);
    }

    static void Drop(Dictionary<Target, Element> map, Target t, MultiMesh mm, int index)
    {
        if (!map.TryGetValue(t, out var el)) return;
        for (int i = el.Parts.Count - 1; i >= 0; i--)
        {
            var p = el.Parts[i];
            if (p.draw == null || p.draw.Mm != mm || p.draw.Index != index) continue;
            p.Free();
            el.Parts.RemoveAt(i);
        }
        if (el.Parts.Count == 0) map.Remove(t);
    }

    /// <summary>
    /// The fade clock, in milliseconds: the game clock BGTransparencyMgr's tick and timers read (service 0xE001001B,
    /// GameTimeClock), which adds the frame's real time capped at m_maxDelta = 0.25 s (GameTimeClock.cpp:34, 108); nothing
    /// in the client calls SetMaxDelta. It drives the 300 ms test and the fade timers.
    /// </summary>
    double clock;
    ulong lastUsec;

    /// <summary>One update of the fade clock: the time since the last one, at most <see cref="MaxStepMs"/>.</summary>
    void Step(double ms) => clock += Math.Min(ms, MaxStepMs);

    public override void _Process(double delta)
    {
        ulong us = Time.GetTicksUsec();
        if (lastUsec != 0) Step((us - lastUsec) / 1000.0);
        lastUsec = us;
        Tick();
    }

    /// <summary>The tick (0x140a0d000) at the fade clock's time: UpdateFadeOut, UpdateFadeIn, then the test when its 300 ms are up.</summary>
    void Tick()
    {
        UpdateFadeOut(clock);
        UpdateFadeIn(clock);
        if (clock >= nextTest)
        {
            Test(clock);
            nextTest = clock + TestPeriodMs;
        }
        Rekey();
    }

    /// <summary>The held fades keyed again each frame, as PreRenderProcessList updates every shell group with the camera's
    /// direction before its sort (ms2renderlistprocessor.cpp:877-881).</summary>
    void Rekey()
    {
        if (fadeOut.Count + fadeIn.Count > 0 && camera() is { } cam && cam.IsInsideTree()) Sort(-cam.GlobalBasis.Z);
    }

    void Test(double now)
    {
        var cam = camera();
        if (cam == null || !cam.IsInsideTree() || feet() is not { } pos) return;
        var eye = cam.GlobalPosition;
        var planes = cam.GetFrustum();
        using var planesOwner = (Godot.Collections.Array)planes;   // a tracked Array per call until disposed
        // the planes read once into managed memory: each read of the Godot array is a call into the engine
        int np = Math.Min(planes.Count, frustum.Length);
        for (int i = 0; i < np; i++) frustum[i] = planes[i];
        var hit = new HashSet<Target>();
        Vector3 a = pos + Vector3.Up * RayLow, b = pos + Vector3.Up * RayHigh;
        Vector3 da = (eye - a).Normalized(), db = (eye - b).Normalized();
        foreach (var t in targets)
        {
            if (t.PropEntities != null && props != null && PropsBuilt(t))
            {
                // a held entity is read all the same: Sort orders it by its sphere now. Any other is skipped unread when its
                // outer sphere is out of the frustum or neither ray comes near it: its bound is within that sphere
                // a target with a node out of the tree has no outer sphere and is read in full (Refresh); its sphere is
                // built again only once every node is back, so its bones are not read twice per test
                // STILL-OPEN: the target sphere is merged member by member, so it can reach past the outer sphere into the
                // frustum while its geometry does not; the client then casts its rays, this skips it. A ray could only hit
                // that geometry between the eye and the near plane (not drawn), so nothing shown differs, but a fade begun
                // there would already be at its floor when the camera turns. No test pins this edge.
                if (!t.OuterAll && AllInTree(t)) Outer(t);
                if (t.OuterAll && !fadeOut.ContainsKey(t) && !fadeIn.ContainsKey(t)
                    && (t.OuterRadius < 0 || !InFrustum(frustum, np, t.OuterCentre, t.OuterRadius)
                        || !NearSphere(a, da, t.OuterCentre, t.OuterRadius) && !NearSphere(b, db, t.OuterCentre, t.OuterRadius))) continue;
                Refresh(t);
            }
            if (t.Radius < 0 || !InFrustum(frustum, np, t.Centre, t.Radius)) continue;
            // the second ray's test against the first hit only skips an insert the set already holds
            if (RayHits(t, a, da) || RayHits(t, b, db)) Add(hit, t);
        }
        Apply(hit, now);
    }

    readonly List<(float Key, Element El)> sorted = new();

    static float Depth(Element el, Vector3 dir)
    {
        float sum = 0;
        foreach (var p in el.Parts) sum += p.Centre.Dot(dir);
        return sum / el.Parts.Count;
    }

    /// <summary>
    /// Each held entity's draw order among the transparent draws, each frame (<see cref="Tick"/>). Its key is AlphaShellComposite::Update's
    /// (@0x14098f020, alphacomponent.cpp:229-273): the mean over its shells of FUN_140994320, the dot of the shell's world
    /// bound centre with the camera's world direction (GetCameraData, ms2renderlistprocessor.cpp:665, 879). CompareAlpha
    /// (@0x1414dee00) draws the largest key first, so the deepest along the view goes first. Element k (from the deepest)
    /// draws its depth passes at render priority 1 + 2k and its colour at 2 + 2k (Godot's limit 127 holds 63 apart; more
    /// share the last pair).
    /// STILL-OPEN: the client sorts the groups among its other alpha objects (ComputeDepth's keys, one std::sort,
    /// ms2renderlistprocessor.cpp:882); here they draw after every other transparent draw.
    /// Update moves the key by FUN_141b71c50's bias for a shell with either of two extra data tags
    /// (DAT_143ae51d0, DAT_143ae5190); the tags are not read here, so no key is moved. A draw's centre is its box's, not
    /// its NiBound's.
    /// STILL-OPEN: a held prop's key uses its parts' centres from the last test (Refresh, every 300 ms); the client reads
    /// each shell's GetWorldBound every frame (alphacomponent.cpp:134-143). Two held entities at nearly one depth, one a
    /// swinging prop, can draw in the wrong order for up to 300 ms where they overlap. Reading the bones here every frame
    /// would bring back the per-frame bone reads the test was built to drop.
    /// </summary>
    void Sort(Vector3 dir)
    {
        sorted.Clear();
        foreach (var (t, el) in fadeOut) sorted.Add((Depth(el, dir), el));
        foreach (var (t, el) in fadeIn) sorted.Add((Depth(el, dir), el));
        if (sorted.Count == 0) return;
        sorted.Sort((x, y) => y.Key.CompareTo(x.Key));
        for (int k = 0; k < sorted.Count; k++)
        {
            var el = sorted[k].El;
            int rank = Math.Min(k, 62);
            if (el.Rank == rank) continue;
            el.Rank = rank;
            foreach (var p in el.Parts)
            {
                p.shell.RenderPriority = 1 + 2 * rank;
                p.mat.RenderPriority = 2 + 2 * rank;
            }
        }
    }

    void Add(HashSet<Target> hit, Target t)
    {
        hit.Add(t);
        if (t.Group > 0 && groups.TryGetValue(t.Group, out var g))
            foreach (var m in g) hit.Add(m);
    }

    readonly Plane[] frustum = new Plane[6];

    static bool InFrustum(Plane[] planes, int n, Vector3 c, float r)
    {
        for (int i = 0; i < n; i++)
            if (planes[i].DistanceTo(c) > r) return false;
        return true;
    }

    /// <summary>
    /// Whether the ray from <paramref name="o"/> along the unit <paramref name="dir"/> (ahead of the origin only) comes
    /// within the target's bounding sphere: a ray that does not can hit none of its triangles (the sphere holds the box of
    /// every draw's mesh at its place, made with the transforms RayHits uses), so the triangle loop is skipped. The radius
    /// has a margin for float rounding, so a ray that grazes the sphere is still tested triangle by triangle.
    /// </summary>
    static bool NearSphere(Vector3 o, Vector3 dir, Vector3 c, float r)
    {
        var d = c - o;
        float r2 = r * 1.001f + 0.01f; r2 *= r2;
        float d2 = d.LengthSquared(), along = d.Dot(dir);
        if (along < 0) return d2 <= r2;      // the centre is behind: only an origin inside the sphere can hit
        return d2 - along * along <= r2;
    }

    /// <summary>PerformPick on the entity's meshes: any triangle, either side, ahead of the origin on the unbounded ray.</summary>
    bool RayHits(Target t, Vector3 from, Vector3 dir)
    {
        if (!NearSphere(from, dir, t.Centre, t.Radius)) return false;
        foreach (var d in t.Draws)
        {
            // while a fade holds it, its own transform; hidden otherwise (a state, a trigger, the other model of an interact
            // mesh, or a zero basis from any other draw switch): not drawn, so not picked
            Transform3D xf;
            if (Saved(t, d) is { } saved) xf = saved;
            else if (TriggerFade.Hidden(d.Mm, d.Index)) continue;
            else
            {
                xf = d.Mm.GetInstanceTransform(d.Index);
                if (xf.Basis.Determinant() == 0) continue;
            }
            if (HitsFaces(d.Mm.Mesh, d.Parent.GlobalTransform * xf, from, dir)) return true;
        }
        if (t.Props != null)
            foreach (var pk in t.Props)
                if (pk.Shown && PropHits(pk, from, dir)) return true;
        return false;
    }

    /// <summary>The draw's own transform while a fade holds it, else null.</summary>
    Transform3D? Saved(Target t, FieldBuilder.FadeDraw d)
    {
        var el = fadeOut.TryGetValue(t, out var e) ? e : fadeIn.TryGetValue(t, out e) ? e : null;
        if (el != null) foreach (var p in el.Parts) if (p.draw == d) return p.xf;
        return null;
    }

    /// <summary>
    /// Drops the entity's fade at once, without putting its instances back: the caller is about to set them itself
    /// (world/FieldInteractDraws switching an interact mesh's model or hiding it). A fade-in's end would otherwise write back a
    /// transform the switch had hidden.
    /// </summary>
    public void Release(FieldEntity e)
    {
        if (e == null || !byEntity.TryGetValue(e, out var t)) return;
        if (fadeOut.Remove(t, out var a)) foreach (var p in a.Parts) p.Free();
        if (fadeIn.Remove(t, out var b)) foreach (var p in b.Parts) p.Free();
    }

    /// <summary>For the headless check: the entity as if the 300 ms test had hit it alone (Apply).</summary>
    internal void FadeOutNow(FieldEntity e)
    {
        if (e == null || !byEntity.TryGetValue(e, out var t)) return;
        if (t.PropEntities != null && props != null && PropsBuilt(t)) Refresh(t);
        Apply(new HashSet<Target> { t }, clock);
        if (camera() is { } cam && cam.IsInsideTree()) Sort(-cam.GlobalBasis.Z);
    }

    /// <summary>For the headless check: whether the pick of the entity's draws (RayHits) meets the ray.</summary>
    internal bool Picks(FieldEntity e, Vector3 from, Vector3 dir)
    {
        if (e == null || !byEntity.TryGetValue(e, out var t)) return false;
        if (t.PropEntities != null && props != null && PropsBuilt(t)) Refresh(t);
        return t.Radius >= 0 && RayHits(t, from, dir);
    }

    /// <summary>For the headless check: the draws a fade of the entity holds (none: not fading).</summary>
    internal IReadOnlyList<FieldBuilder.FadeDraw> Fading(FieldEntity e)
    {
        if (e == null || !byEntity.TryGetValue(e, out var t)) return Array.Empty<FieldBuilder.FadeDraw>();
        var el = fadeOut.TryGetValue(t, out var a) ? a : fadeIn.TryGetValue(t, out a) ? a : null;
        return el == null ? Array.Empty<FieldBuilder.FadeDraw>() : el.Parts.Where(p => p.draw != null).Select(p => p.draw).ToList();
    }

    /// <summary>For the headless check: the prop meshes a fade of the entity holds, with the material they draw, the depth pass
    /// before the fade material (none: not fading).</summary>
    internal IReadOnlyList<(MeshInstance3D Node, ShaderMaterial Material)> FadingProps(FieldEntity e)
    {
        if (e == null || !byEntity.TryGetValue(e, out var t)) return Array.Empty<(MeshInstance3D, ShaderMaterial)>();
        var el = fadeOut.TryGetValue(t, out var a) ? a : fadeIn.TryGetValue(t, out a) ? a : null;
        return el == null ? Array.Empty<(MeshInstance3D, ShaderMaterial)>() : el.Parts.Where(p => p.prop != null).Select(p => (p.prop, p.shell)).ToList();
    }

    /// <summary>For the headless check: the fade material and model of every part a fade of the entity holds.</summary>
    internal IReadOnlyList<(ShaderMaterial Material, ModelMesh Mesh)> FadeMaterials(FieldEntity e)
    {
        if (e == null || !byEntity.TryGetValue(e, out var t)) return Array.Empty<(ShaderMaterial, ModelMesh)>();
        var el = fadeOut.TryGetValue(t, out var a) ? a : fadeIn.TryGetValue(t, out a) ? a : null;
        return el == null ? Array.Empty<(ShaderMaterial, ModelMesh)>() : el.Parts.Select(p => (p.mat, p.model)).ToList();
    }

    /// <summary>For the headless check: the material every part a fade of the entity holds draws (its first pass) and its fade material.</summary>
    internal IReadOnlyList<(Material Drawn, ShaderMaterial Fade)> FadePasses(FieldEntity e)
    {
        if (e == null || !byEntity.TryGetValue(e, out var t)) return Array.Empty<(Material, ShaderMaterial)>();
        var el = fadeOut.TryGetValue(t, out var a) ? a : fadeIn.TryGetValue(t, out a) ? a : null;
        return el == null ? Array.Empty<(Material, ShaderMaterial)>() : el.Parts.Select(p => (p.prop != null ? p.prop.MaterialOverride : p.node.MaterialOverride, p.mat)).ToList();
    }

    /// <summary>For the headless check: when the entity's fade (out, or back in) started, on the fade clock.</summary>
    internal double? StartOf(FieldEntity e)
    {
        if (e == null || !byEntity.TryGetValue(e, out var t)) return null;
        return fadeOut.TryGetValue(t, out var a) ? a.Start : fadeIn.TryGetValue(t, out a) ? a.Start : null;
    }

    /// <summary>For the headless check: whether the entity is a target, its props not taken.</summary>
    internal bool IsTarget(FieldEntity e) => e != null && byEntity.ContainsKey(e);

    /// <summary>For the headless check: whether the entity is a fade target, and its test sphere (radius below 0: nothing drawn).</summary>

    internal (Vector3 Centre, float Radius)? TargetBound(FieldEntity e)
    {
        if (e == null || !byEntity.TryGetValue(e, out var t)) return null;
        if (t.PropEntities != null && props != null && PropsBuilt(t)) Refresh(t);
        return (t.Centre, t.Radius);
    }

    /// <summary>For the headless check: runs the 300 ms test now.</summary>
    internal void TestNow() => Test(clock);

    /// <summary>For the headless check: a frame <paramref name="ms"/> after the last one, the fades stepped on the fade clock
    /// (capped as GameTimeClock's step), the held fades keyed again, and the test not run.</summary>
    internal void StepFrame(double ms)
    {
        Step(ms);
        UpdateFadeOut(clock);
        UpdateFadeIn(clock);
        Rekey();
    }

    /// <summary>For the headless check: the fade clock (milliseconds).</summary>
    internal double Now => clock;

    /// <summary>Whether a fade holds the entity (fading out or back in).</summary>
    public bool Holds(FieldEntity e) => e != null && byEntity.TryGetValue(e, out var t) && (fadeOut.ContainsKey(t) || fadeIn.ContainsKey(t));

    static bool Triangle(Vector3 o, Vector3 v, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        var e1 = p1 - p0; var e2 = p2 - p0;
        var p = v.Cross(e2);
        float det = e1.Dot(p);
        if (Math.Abs(det) < 1e-12f) return false;
        float inv = 1f / det;
        var s = o - p0;
        float u = s.Dot(p) * inv;
        if (u < 0 || u > 1) return false;
        var q = s.Cross(e1);
        float w = v.Dot(q) * inv;
        if (w < 0 || u + w > 1) return false;
        return e2.Dot(q) * inv >= 0;
    }

    void Apply(HashSet<Target> hit, double now)
    {
        if (hit.Count == 0) { MoveAllToFadeIn(now); return; }
        foreach (var t in fadeOut.Keys.Where(t => !hit.Contains(t)).ToList())
        {
            var el = fadeOut[t];
            fadeOut.Remove(t);
            fadeIn[t] = el;
            el.Start = now;
        }
        foreach (var t in hit)
        {
            if (fadeOut.ContainsKey(t) || fadeIn.ContainsKey(t)) continue;
            // the alpha floor is property 0x188f01c1, which no flat model carries, so it stays 0.3
            var el = new Element { Alpha = 0.3f, Start = now };
            foreach (var d in t.Draws)
            {
                // a hidden draw (a state, a trigger, an interact mesh's other model) is not drawn, so not faded
                if (TriggerFade.Hidden(d.Mm, d.Index) || d.Mm.GetInstanceTransform(d.Index).Basis.Determinant() == 0) continue;
                // only a mesh whose active material is an MS2StandardMaterial gets a TransparencyStandardMaterial
                // (bgtransparencymgr.cpp:517-520); any other (MS2GlassMaterial, no material) keeps its own draw
                if (!ModelData.IsStandardMaterial(d.Mesh.ActiveMaterial)) continue;
                var mesh = d.Mm.Mesh;
                var colour = (ShaderMaterial)mesh.SurfaceGetMaterial(0);
                if (colour.NextPass is ShaderMaterial next && colour.Shader != next.Shader) colour = next;   // a fluid shell's colour pass
                var mat = ModelBuilder.FadeMaterial(colour, d.Mesh);
                var shell = ModelBuilder.FadeShell(mat, d.Mesh);
                var xf = d.Mm.GetInstanceTransform(d.Index);
                var one = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = 1 };
                one.SetInstanceTransform(0, xf);
                one.SetInstanceColor(0, d.Mm.GetInstanceColor(d.Index));
                var node = new MultiMeshInstance3D { Name = "fade", Multimesh = one, MaterialOverride = shell, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                if (d.NotReceiving) node.SetInstanceShaderParameter(ReceiveParam, 0f);   // the copy's own draw takes no shadow (FieldBuilder)
                if (d.LightOff != 0) node.SetInstanceShaderParameter(LightOffParam, (float)d.LightOff);   // nor the lights its draw does not take
                d.Parent.AddChild(node);
                d.Mm.SetInstanceTransform(d.Index, new Transform3D(new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), xf.Origin));
                var part = new Part { draw = d, xf = xf, node = node, mat = mat, shell = shell, model = d.Mesh, TestRef = d.Mesh.AlphaThreshold,
                                      centre = (d.Parent.GlobalTransform * xf * mesh.GetAabb()).GetCenter() };
                part.SetFade(1f);
                el.Parts.Add(part);
            }
            if (t.Props != null)
                foreach (var pk in t.Props)
                {
                    // a prop mesh not drawn now is not faded, as a hidden draw is not
                    if (!Drawn(pk) || !ModelData.IsStandardMaterial(pk.M.Mesh.ActiveMaterial)) continue;
                    var node = pk.M.Node;
                    if (node.Mesh?.SurfaceGetMaterial(0) is not ShaderMaterial colour) continue;
                    if (colour.NextPass is ShaderMaterial next && colour.Shader != next.Shader) colour = next;
                    var mat = ModelBuilder.FadeMaterial(colour, pk.M.Mesh);
                    var shell = ModelBuilder.FadeShell(mat, pk.M.Mesh);
                    node.MaterialOverride = shell;
                    var part = new Part { prop = node, mat = mat, shell = shell, model = pk.M.Mesh, TestRef = pk.M.Mesh.AlphaThreshold, pick = pk };
                    part.SetFade(1f);
                    el.Parts.Add(part);
                }
            if (el.Parts.Count > 0) fadeOut[t] = el;
        }
    }

    void MoveAllToFadeIn(double now)
    {
        foreach (var (t, el) in fadeOut) { fadeIn[t] = el; el.Start = now; }
        fadeOut.Clear();
    }

    /// <summary>
    /// UpdateFadeOut (0x140a0ebf0): each running element's materials go to clamp(1 - ratio, alpha, 1); an element whose timer
    /// has run out is skipped (Timer::IsRunning, timer.cpp:159-164) and keeps the value its last running frame set. The
    /// timer reads the game clock (service 0xE001001B, GameTimeClock), which moves at most m_maxDelta = 0.25 s per frame
    /// (GameTimeClock.cpp:34, 108), and so does <see cref="clock"/>: the first frame after the Apply is always running, at a
    /// ratio of 250 / 300 or less, so a fade whose floor is 1/6 or more reaches it however long that frame took.
    /// </summary>
    void UpdateFadeOut(double now)
    {
        foreach (var el in fadeOut.Values)
        {
            if (!el.Running(now)) continue;
            float a = Mathf.Clamp(1f - el.Ratio(now), el.Alpha, 1f);
            foreach (var p in el.Parts) p.SetFade(a);
        }
    }

    void UpdateFadeIn(double now)
    {
        List<Target> done = null;
        foreach (var (t, el) in fadeIn)
        {
            if (!el.Running(now)) { (done ??= new()).Add(t); continue; }
            float a = Mathf.Clamp(el.Ratio(now), el.Alpha, 1f);
            foreach (var p in el.Parts) p.SetFade(a);
        }
        if (done == null) return;
        foreach (var t in done)
        {
            foreach (var p in fadeIn[t].Parts)
            {
                // a draw a switch hid meanwhile stays hidden
                if (p.draw != null && !TriggerFade.Hidden(p.draw.Mm, p.draw.Index)) p.draw.Mm.SetInstanceTransform(p.draw.Index, p.xf);
                p.Free();
            }
            fadeIn.Remove(t);
        }
    }
}
