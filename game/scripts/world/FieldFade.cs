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
/// faded material (<see cref="ModelBuilder.FadeMaterial"/>). Not modelled: the sorting of the faded entities
/// (SortEntities), AlphaShellComposite::MarkScene, the shadow and glow of a faded entity, and props drawn elsewhere (animated
/// props); the frustum test uses the entity's merged mesh box for its NiBound.
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

    sealed class Target
    {
        public FieldEntity Entity;
        public List<FieldBuilder.FadeDraw> Draws;
        public int Group;
        public Vector3 Centre; public float Radius;
    }

    sealed class Element
    {
        public float Alpha;
        public ulong Start;
        public bool Running(ulong now) => now - Start < FadeMs;
        public float Ratio(ulong now) => Math.Min((now - Start) / (float)FadeMs, 1f);
        public readonly List<Part> Parts = new();
    }

    /// <summary>One faded draw. <see cref="TestRef"/> is the alpha-test reference of its cloned NiAlphaProperty
    /// (TransparencyMaterial::FUN_141860140), which every vfunc_4 call scales; it starts again from the mesh's own
    /// reference only when a new element clones the property again.</summary>
    sealed class Part
    {
        public FieldBuilder.FadeDraw draw; public Transform3D xf; public MultiMeshInstance3D node; public ShaderMaterial mat;
        public int TestRef;
        public void SetFade(float a) => ModelBuilder.SetFade(mat, draw.Mesh, a, ref TestRef);
    }

    readonly List<Target> targets = new();
    readonly Dictionary<int, List<Target>> groups = new();
    readonly Dictionary<FieldEntity, Target> byEntity = new();
    readonly Dictionary<Target, Element> fadeOut = new(), fadeIn = new();
    readonly Dictionary<Mesh, Vector3[]> faces = new();
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
                var b = d.Parent.GlobalTransform * d.Mm.GetInstanceTransform(d.Index) * d.Mm.Mesh.GetAabb();   // the instances are in the field's Z-up frame
                box = first ? b : box.Merge(b);
                first = false;
            }
            t.Centre = box.GetCenter(); t.Radius = box.Size.Length() / 2;
            targets.Add(t);
            byEntity[e] = t;
            if (t.Group > 0)
            {
                if (!groups.TryGetValue(t.Group, out var g)) groups[t.Group] = g = new List<Target>();
                g.Add(t);
            }
        }
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
        var hit = new HashSet<Target>();
        Vector3 a = pos + Vector3.Up * RayLow, b = pos + Vector3.Up * RayHigh;
        Vector3 da = (eye - a).Normalized(), db = (eye - b).Normalized();
        foreach (var t in targets)
        {
            if (!InFrustum(planes, t.Centre, t.Radius)) continue;
            // the second ray's test against the first hit only skips an insert the set already holds
            if (RayHits(t, a, da) || RayHits(t, b, db)) Add(hit, t);
        }
        Apply(hit, now);
    }

    void Add(HashSet<Target> hit, Target t)
    {
        hit.Add(t);
        if (t.Group > 0 && groups.TryGetValue(t.Group, out var g))
            foreach (var m in g) hit.Add(m);
    }

    static bool InFrustum(Godot.Collections.Array<Plane> planes, Vector3 c, float r)
    {
        for (int i = 0, n = planes.Count; i < n; i++)     // indexed: foreach boxes an enumerator per target
            if (planes[i].DistanceTo(c) > r) return false;
        return true;
    }

    /// <summary>PerformPick on the entity's meshes: any triangle, either side, ahead of the origin on the unbounded ray.</summary>
    bool RayHits(Target t, Vector3 from, Vector3 dir)
    {
        foreach (var d in t.Draws)
        {
            var xf = d.Mm.GetInstanceTransform(d.Index);
            if (xf.Basis.Determinant() == 0) xf = Saved(t, d);   // hidden while it fades: its own transform
            var inv = (d.Parent.GlobalTransform * xf).AffineInverse();
            var o = inv * from;
            var v = inv.Basis * dir;
            if (!faces.TryGetValue(d.Mm.Mesh, out var f)) faces[d.Mm.Mesh] = f = d.Mm.Mesh.GetFaces();
            for (int i = 0; i + 2 < f.Length; i += 3)
                if (Triangle(o, v, f[i], f[i + 1], f[i + 2])) return true;
        }
        return false;
    }

    Transform3D Saved(Target t, FieldBuilder.FadeDraw d)
    {
        var el = fadeOut.TryGetValue(t, out var e) ? e : fadeIn.TryGetValue(t, out e) ? e : null;
        if (el != null) foreach (var p in el.Parts) if (p.draw == d) return p.xf;
        return d.Mm.GetInstanceTransform(d.Index);
    }

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
                var mesh = d.Mm.Mesh;
                var colour = (ShaderMaterial)mesh.SurfaceGetMaterial(0);
                if (colour.NextPass is ShaderMaterial next && colour.Shader != next.Shader) colour = next;   // a fluid shell's colour pass
                var mat = ModelBuilder.FadeMaterial(colour, d.Mesh);
                var xf = d.Mm.GetInstanceTransform(d.Index);
                var one = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = 1 };
                one.SetInstanceTransform(0, xf);
                one.SetInstanceColor(0, d.Mm.GetInstanceColor(d.Index));
                var node = new MultiMeshInstance3D { Name = "fade", Multimesh = one, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                d.Parent.AddChild(node);
                d.Mm.SetInstanceTransform(d.Index, new Transform3D(new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), xf.Origin));
                var part = new Part { draw = d, xf = xf, node = node, mat = mat, TestRef = d.Mesh.AlphaThreshold };
                part.SetFade(1f);
                el.Parts.Add(part);
            }
            fadeOut[t] = el;
        }
    }

    void MoveAllToFadeIn(ulong now)
    {
        foreach (var (t, el) in fadeOut) { fadeIn[t] = el; el.Start = now; }
        fadeOut.Clear();
    }

    void UpdateFadeOut(ulong now)
    {
        foreach (var el in fadeOut.Values)
        {
            if (!el.Running(now)) continue;
            float a = Mathf.Clamp(1f - el.Ratio(now), el.Alpha, 1f);
            foreach (var p in el.Parts) p.SetFade(a);
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
                p.draw.Mm.SetInstanceTransform(p.draw.Index, p.xf);
                p.node.QueueFree();
            }
            fadeIn.Remove(t);
        }
    }
}
