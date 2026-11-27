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
/// Apply until it is released.
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
/// material as its override. Their frustum sphere is the merge of the meshes' world bounds now.
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
    const ulong TestPeriodMs = 300, FadeMs = 300;
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
    }

    /// <summary>One mesh of an animated prop, with what its pick needs.</summary>
    sealed class PropPick
    {
        public FieldProps.PropMesh M;
        public bool HasBound; public Vector3 Centre; public float Radius;   // the model bound
        public Transform3D PickToSkel, BoundToSkel;                         // skinned: relative to the skeleton
        public int[] Bones; public Vector3[] BoneCentres; public float[] BoneRadii;   // RECOMPUTE_BOUNDS
    }

    sealed class Element
    {
        public float Alpha;
        public ulong Start;
        /// <summary>The element's place in <see cref="Sort"/> (-1: not placed yet).</summary>
        public int Rank = -1;
        /// <summary>The fade-out's end value is set (<see cref="UpdateFadeOut"/>).</summary>
        public bool Settled;
        public bool Running(ulong now) => now - Start < FadeMs;
        public float Ratio(ulong now) => Math.Min((now - Start) / (float)FadeMs, 1f);
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
    ulong nextTest;
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
        return true;
    }

    /// <summary>Whether the prop mesh is drawn now (its node, or the entity's, not hidden; the node not freed).</summary>
    static bool Drawn(PropPick pk) => GodotObject.IsInstanceValid(pk.M.Node) && pk.M.Node.IsVisibleInTree();

    /// <summary>The prop mesh's world bound now (NiBound::Update at its world transform; a RECOMPUTE_BOUNDS skin's from its
    /// bones), or without a model bound the box of its mesh at its node.</summary>
    static (Vector3 c, float r) WorldBound(PropPick pk)
    {
        if (pk.Bones != null) return CharacterAlphaSort.SkinnedBound(pk.M.Skel, pk.BoundToSkel, pk.Bones, pk.BoneCentres, pk.BoneRadii);
        if (pk.HasBound)
        {
            var x = pk.M.Mesh.Skinned ? pk.M.Skel.GlobalTransform * pk.BoundToSkel : pk.M.Node.GlobalTransform;
            return (x * pk.Centre, x.Basis.Scale.X * pk.Radius);
        }
        var box = PickTransform(pk) * pk.M.Node.GetAabb();
        return (box.GetCenter(), box.Size.Length() / 2);
    }

    /// <summary>Where the pick places the mesh's held vertices: a skinned mesh's unskinned at its node, a rigid one's at its node.</summary>
    static Transform3D PickTransform(PropPick pk) => pk.M.Mesh.Skinned ? pk.M.Skel.GlobalTransform * pk.PickToSkel : pk.M.Node.GlobalTransform;

    /// <summary>The frustum sphere of a target with props: its draws' sphere merged with each drawn prop mesh's bound now;
    /// a radius below 0 when nothing of it is drawn.</summary>
    static void Refresh(Target t)
    {
        Vector3 c = t.DrawsCentre; float r = t.DrawsRadius;
        foreach (var pk in t.Props)
        {
            if (!Drawn(pk)) continue;
            var (pc, pr) = WorldBound(pk);
            if (r < 0) { c = pc; r = pr; }
            else (c, r) = AlphaSort.Merge(c, r, pc, pr);
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

    /// <summary>PerformPick on one prop mesh (SimpleSkinPickObjectPolicy behind the bound test).</summary>
    bool PropHits(PropPick pk, Vector3 from, Vector3 dir)
    {
        if (pk.HasBound)
        {
            var (c, r) = WorldBound(pk);
            if (!SphereHit(from, dir, c, r)) return false;
        }
        if (HitsFaces(pk.M.Node.Mesh, PickTransform(pk), from, dir)) return true;
        if (pk.Bones == null) return false;
        var skel = pk.M.Skel.GlobalTransform;
        for (int i = 0; i < pk.Bones.Length; i++)
        {
            var bone = skel * pk.M.Skel.GetBoneGlobalPose(pk.Bones[i]);
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

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksMsec();
        UpdateFadeOut(now);
        UpdateFadeIn(now);
        if (now >= nextTest)
        {
            Test(now);
            nextTest = now + TestPeriodMs;
        }
    }

    void Test(ulong now)
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
        if (props != null) foreach (var t in propTargets) if (PropsBuilt(t)) Refresh(t);
        foreach (var t in targets)
        {
            if (t.Radius < 0 || !InFrustum(frustum, np, t.Centre, t.Radius)) continue;
            // the second ray's test against the first hit only skips an insert the set already holds
            if (RayHits(t, a, da) || RayHits(t, b, db)) Add(hit, t);
        }
        Apply(hit, now);
        Sort(eye);
    }

    readonly List<(float Dist, Element El)> sorted = new();

    /// <summary>
    /// Each held entity's draw order among the transparent draws: the farthest from the eye first, as CompareAlpha draws the
    /// largest key first. Element k (from the farthest) draws its depth passes at render priority 1 + 2k and its colour
    /// at 2 + 2k (Godot's limit 127 holds 63 apart; more share the last pair).
    /// </summary>
    void Sort(Vector3 eye)
    {
        sorted.Clear();
        foreach (var (t, el) in fadeOut) sorted.Add((t.Centre.DistanceSquaredTo(eye), el));
        foreach (var (t, el) in fadeIn) sorted.Add((t.Centre.DistanceSquaredTo(eye), el));
        if (sorted.Count == 0) return;
        sorted.Sort((x, y) => y.Dist.CompareTo(x.Dist));
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
                if (Drawn(pk) && PropHits(pk, from, dir)) return true;
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
        Apply(new HashSet<Target> { t }, Time.GetTicksMsec());
        if (camera() is { } cam && cam.IsInsideTree()) Sort(cam.GlobalPosition);
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

    /// <summary>For the headless check: when the entity's fade (out, or back in) started, on the tick clock.</summary>
    internal ulong? StartOf(FieldEntity e)
    {
        if (e == null || !byEntity.TryGetValue(e, out var t)) return null;
        return fadeOut.TryGetValue(t, out var a) ? a.Start : fadeIn.TryGetValue(t, out a) ? a.Start : null;
    }

    /// <summary>For the headless check: whether the entity is a fade target, and its test sphere (radius below 0: nothing drawn).</summary>
    internal (Vector3 Centre, float Radius)? TargetBound(FieldEntity e)
    {
        if (e == null || !byEntity.TryGetValue(e, out var t)) return null;
        if (t.PropEntities != null && props != null && PropsBuilt(t)) Refresh(t);
        return (t.Centre, t.Radius);
    }

    /// <summary>For the headless check: runs the 300 ms test now.</summary>
    internal void TestNow() => Test(Time.GetTicksMsec());

    /// <summary>For the headless check: steps the fades to <paramref name="now"/> (milliseconds on the tick clock).</summary>
    internal void StepTo(ulong now) { UpdateFadeOut(now); UpdateFadeIn(now); }

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

    void Apply(HashSet<Target> hit, ulong now)
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
                var part = new Part { draw = d, xf = xf, node = node, mat = mat, shell = shell, model = d.Mesh, TestRef = d.Mesh.AlphaThreshold };
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
                    var part = new Part { prop = node, mat = mat, shell = shell, model = pk.M.Mesh, TestRef = pk.M.Mesh.AlphaThreshold };
                    part.SetFade(1f);
                    el.Parts.Add(part);
                }
            if (el.Parts.Count > 0) fadeOut[t] = el;
        }
    }

    void MoveAllToFadeIn(ulong now)
    {
        foreach (var (t, el) in fadeOut) { fadeIn[t] = el; el.Start = now; }
        fadeOut.Clear();
    }

    /// <summary>
    /// UpdateFadeOut (0x140a0ebf0): each running element's materials go to clamp(1 - ratio, alpha, 1); an element whose timer
    /// has run out is skipped (Timer::IsRunning, timer.cpp:159-164). The timer reads the game clock (service 0xE001001B,
    /// GameTimeClock), which moves at most m_maxDelta = 0.25 s per frame (GameTimeClock.cpp:34, 108): the client's first
    /// frame after the Apply is always running, at a ratio of 250 / 300 or less, and with a frame rate above about 11 fps
    /// its last running frame has a ratio past 1 - alpha and has already set the floor. Our tick clock has no such cap,
    /// so a frame here can come later than the 300 ms (the first draw of a fade material builds its pipeline), and the
    /// element would stay at the value of the frame before, 1 when that was the Apply's own: an element seen out of time
    /// for the first time is set once to its end value, clamp(1 - 1, alpha, 1), what the client's capped frame sets for
    /// any floor of 1/6 or more.
    /// </summary>
    void UpdateFadeOut(ulong now)
    {
        foreach (var el in fadeOut.Values)
        {
            bool running = el.Running(now);
            if (!running && el.Settled) continue;
            float a = Mathf.Clamp(1f - el.Ratio(now), el.Alpha, 1f);
            foreach (var p in el.Parts) p.SetFade(a);
            el.Settled = !running;
        }
    }

    void UpdateFadeIn(ulong now)
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
