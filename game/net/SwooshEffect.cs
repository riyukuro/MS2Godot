using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot.Net;

/// <summary>
/// A swoosh part (CSwooshEffect, swoosheffectparam.cpp): a strip between two named nodes of the effect's target, laid again at
/// each sample (<see cref="SwooshTrail"/>). Its vertices are world points (the mesh hangs from a SeparateNode(0, 0, 0) that takes
/// none of the target's transform, swoosheffectparam.cpp:341), so the node here is top level at the identity.
/// - Made only on an actor (the target's scene node must be a NiNode, :293-295) and only at effect step 2 or 3
///   (<see cref="EffectStripParam.SwooshShape"/>).
/// - At its start (vfunc_4 @0x14181ed70, :484-517, at once for startDelay 0) the two nodes are looked up by name under the
///   actor (GetObjectByName): its skeleton's bones and named nodes, then the effect nifs hanging from it. A node not found
///   stops the trail (m_b2e false) with the client's log line.
/// - The material: texturePath's texture with vertex colour white as emissive, fog off, both sides, no z write, blended by
///   alphaMode (:426-476); with a nifPath, the first mesh of that nif lends its base texture and its blend (:362-418).
/// STILL-OPEN: the nif's texturing and material controllers (flip, uv, alpha) moved over by the client are not played; the
/// texture's DECAL apply mode is drawn as the texture times the white vertex colour.
/// </summary>
public sealed partial class SwooshEffect : MeshInstance3D
{
    readonly SwooshTrail trail;
    readonly EffectStripParam param;
    readonly Node3D actor;
    readonly ImmediateMesh mesh = new();
    readonly Material material;
    float delay, life;
    bool started, tracking;
    // a trail node: a bone (skeleton, index), a named node, or an effect instance's node (instance, index)
    struct TrailNode
    {
        public Skeleton3D Sk; public int Bone;
        public Node3D Node;
        public EffectInstance Fx; public int FxNode;
        public readonly bool Found => Sk != null || Node != null || Fx != null;
    }
    TrailNode trail1, trail2;
    public static int Made, NodesMissing, Laid;
    static readonly bool FxTrace = System.Environment.GetEnvironmentVariable("MS2_LIVE_FX_TRACE") == "1";

    SwooshEffect(EffectStripParam param, SwooshTrail trail, Node3D actor, Material material, float delay, float life)
    {
        this.param = param; this.trail = trail; this.actor = actor; this.material = material; this.delay = delay; this.life = life;
        Name = "Swoosh";
        TopLevel = true;
        Mesh = mesh;
        CastShadow = ShadowCastingSetting.Off;
        ExtraCullMargin = 16384;   // the strip moves every sample; its box is not kept
    }

    /// <summary>The swoosh of <paramref name="part"/> on <paramref name="actor"/>, null when the client makes none: no actor, a
    /// step that makes no swoosh, a nif with no mesh. <paramref name="ownerKind"/> is the effect's kind (m_unk_18, the
    /// target's m_nKind). A loop part lives until the actor goes.</summary>
    public static SwooshEffect Make(IReadOnlyList<EffectPart> parts, EffectPart part, Node3D actor, int ownerKind, ModelBuilder mb, System.Func<string, EffectScene> scene)
    {
        var p = part.Strip;
        // CreateEffectParam: no loop and no duration makes no param (CEffectParam::vfunc_2, effectservice.cpp:667-685)
        if (p == null || actor == null || !GodotObject.IsInstanceValid(actor) || !EffectRandom.ParamMade(parts, part)) return null;
        if (!p.SwooshShape(EffectStep, TrailMode, ownerKind, (float)Engine.GetFramesPerSecond(), out int segments, out int curve, out float interval)) return null;
        Material mat;
        if (p.NifPath.Length > 0)
        {
            // the first NiMesh of the nif (Unk_SceneWalker, :366-375); none: no swoosh
            var sc = scene(p.NifPath);
            if (sc == null || sc.Meshes.Count == 0) return null;
            var m = sc.Meshes[0].Mesh;
            string tex = m.Textures.TryGetValue("base", out var b) ? b : null;
            mat = MaterialFor(tex != null ? mb.Texture(tex) : null, m.Blend switch
            {
                BlendForm.Mix => EffectStripParam.AlphaStandard,
                BlendForm.AddUnweighted => EffectStripParam.AlphaAdd,
                BlendForm.Opaque => -1,
                _ => EffectStripParam.AlphaSwoosh,
            }, m.MaterialAlpha);
        }
        else mat = MaterialFor(mb.TextureEntry(CombatLayer.EffectArchive, EffectStripParam.TextureEntry(p.TexturePath)), p.AlphaMode, 1f);
        float life = part.Loop ? float.MaxValue : part.Duration;
        Made++;
        return new SwooshEffect(p, new SwooshTrail(segments, curve, interval), actor, mat, part.StartDelay, life);
    }

    /// <summary>The graphic option's effect step (CEffectService m_unk_188, FUN_1411a8880 clamps it to 1..3; 3 from the service's
    /// constructor).</summary>
    public static int EffectStep = 3;
    /// <summary>The service's m_unk_128 (1 from its constructor): the frame rate lowers the step of kind 3 and 5 owners.</summary>
    public static bool TrailMode = true;

    // the materials of a texture, held as long as the texture is: the model builder's texture cache can drop a texture (a
    // strong key here would keep every swoosh texture ever drawn)
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Texture2D, Dictionary<(int, float), Material>> byTexture = new();
    static readonly Dictionary<(int, float), Material> untextured = new();
    static Shader add, mix;

    /// <summary>The strip material of a texture (null: untextured) blended by <paramref name="alphaMode"/>, shared per texture,
    /// mode and alpha; the lightning parts take the same ones (<see cref="LightningEffect"/>).</summary>
    internal static Material MaterialFor(Texture2D tex, int alphaMode, float alpha)
    {
        var set = tex == null ? untextured : byTexture.GetValue(tex, static _ => new());
        if (set.TryGetValue((alphaMode, alpha), out var m)) return m;
        // alphaMode 0: src alpha / one; 1: src alpha / inverse src alpha; 2: one / one (src alpha with an alpha of 1);
        // -1 (a nif mesh with no blending): drawn as 1
        bool isMix = alphaMode == EffectStripParam.AlphaStandard || alphaMode == -1;
        var sm = new ShaderMaterial { Shader = isMix ? mix ??= ShaderFor(false) : add ??= ShaderFor(true) };
        sm.SetShaderParameter(Tex, tex);
        sm.SetShaderParameter(HasTex, tex != null);
        sm.SetShaderParameter(Opacity, alpha);
        sm.SetShaderParameter(Unweighted, alphaMode == EffectStripParam.AlphaAdd);
        return set[(alphaMode, alpha)] = sm;
    }

    static readonly StringName Tex = "tex", HasTex = "has_tex", Opacity = "opacity", Unweighted = "unweighted";

    // Textures are taken raw (no source_color): the colour buffer holds gamma values (field_lit.gdshader). The texturing
    // property's filter mode 0 (nearest) and clamp mode 0 (clamp both), :427-429.
    static Shader ShaderFor(bool additive) => new()
    {
        Code = "shader_type spatial;\nrender_mode unshaded, cull_disabled, depth_draw_never, fog_disabled, " + (additive ? "blend_add" : "blend_mix") + @";
uniform sampler2D tex : repeat_disable, filter_nearest;
uniform bool has_tex = true;
uniform float opacity = 1.0;
uniform bool unweighted = false;
void fragment() {
    vec4 c = has_tex ? texture(tex, UV) : vec4(1.0);
    ALBEDO = c.rgb;
    ALPHA = unweighted ? 1.0 : c.a * opacity;
}",
    };

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(actor)) { QueueFree(); return; }
        if (!started)
        {
            delay -= (float)delta;
            if (delay > 0) return;
            started = true;
            Start();
        }
        life -= (float)delta;
        if (life <= 0) { QueueFree(); return; }
        if (!tracking || !Visible) return;   // an app-culled root lays nothing (vfunc_9 @0x14181ecd0)
        // UpdateTrail (:552-556) against the game time, m_dLastSample 0 from the constructor (:270): the first update samples
        double now = Time.GetTicksUsec() * 1e-6;
        if (!trail.Due(now)) return;
        // a trail node gone with its skeleton, node or effect (the actor's model rebuilt, the effect ended): the trail stops
        if (!Point(trail1, out var a) || !Point(trail2, out var b)) { QueueFree(); return; }
        if (trail.Sample(now, N(a), N(b))) Lay();
    }

    /// <summary>vfunc_4: the two trail nodes by name under the actor; one not found stops the trail. One walk over the actor
    /// takes its skeletons' bones and its effect instances' nodes; a named node is the engine's own search.</summary>
    void Start()
    {
        string n1 = param.Node1, n2 = param.Node2;
        TrailNode b1 = default, b2 = default;
        Walk(actor, n1, n2, ref b1, ref b2);
        trail1 = Pick(b1, n1);
        trail2 = Pick(b2, n2);
        if (!trail1.Found && FxTrace) GD.Print($"CSwooshEffect can't find TrailNode1 [{n1}]");
        if (!trail2.Found && FxTrace) GD.Print($"CSwooshEffect can't find TrailNode2 [{n2}]");
        tracking = trail1.Found && trail2.Found;
        if (FxTrace) GD.Print($"fx: swoosh {n1}/{n2} on {actor.Name}: {(tracking ? "tracking" : "nodes missing")} at {Time.GetTicksMsec()}");
        if (!tracking) NodesMissing++;
    }

    /// <summary>A bone first, then a node of that name, then an effect instance's node.</summary>
    TrailNode Pick(TrailNode walked, string name)
    {
        if (walked.Sk != null) return new TrailNode { Sk = walked.Sk, Bone = walked.Bone };
        if (!string.IsNullOrEmpty(name) && actor.FindChild(name, true, false) is Node3D n) return new TrailNode { Node = n };
        return walked.Fx != null ? new TrailNode { Fx = walked.Fx, FxNode = walked.FxNode } : default;
    }

    /// <summary>The first skeleton bone and the first effect instance node of each name under <paramref name="n"/>, by child
    /// index (no child arrays).</summary>
    static void Walk(Node n, string name1, string name2, ref TrailNode t1, ref TrailNode t2)
    {
        if (n is Skeleton3D sk)
        {
            if (t1.Sk == null && !string.IsNullOrEmpty(name1) && sk.FindBone(name1) is int a and >= 0) { t1.Sk = sk; t1.Bone = a; }
            if (t2.Sk == null && !string.IsNullOrEmpty(name2) && sk.FindBone(name2) is int b and >= 0) { t2.Sk = sk; t2.Bone = b; }
        }
        else if (n is EffectInstance e)
        {
            if (t1.Fx == null && !string.IsNullOrEmpty(name1) && e.NodeNamed(name1) is int a and >= 0) { t1.Fx = e; t1.FxNode = a; }
            if (t2.Fx == null && !string.IsNullOrEmpty(name2) && e.NodeNamed(name2) is int b and >= 0) { t2.Fx = e; t2.FxNode = b; }
        }
        int count = n.GetChildCount();
        for (int i = 0; i < count; i++) Walk(n.GetChild(i), name1, name2, ref t1, ref t2);
    }

    /// <summary>The node's world point this frame; false when what holds it was freed.</summary>
    static bool Point(in TrailNode t, out Vector3 at)
    {
        at = default;
        if (t.Sk != null)
        {
            if (!IsInstanceValid(t.Sk)) return false;
            at = t.Sk.GlobalTransform * t.Sk.GetBoneGlobalPose(t.Bone).Origin;
            return true;
        }
        if (t.Node != null)
        {
            if (!IsInstanceValid(t.Node)) return false;
            at = t.Node.GlobalPosition;
            return true;
        }
        if (t.Fx == null || !IsInstanceValid(t.Fx)) return false;
        at = t.Fx.NodePoint(t.FxNode);
        return true;
    }

    /// <summary>The strip's triangles (i, i + n, i + 1) and (i + 1, i + n, i + 1 + n) over the two rows, uv (k / (n - 1), 0 or 1)
    /// (UpdateTrail, :571-617). The ImmediateMesh is built again at each sample, as the client rewrites its strip's vertex
    /// rows; a mesh kept at the largest size with its arrays updated in place would save the rebuild.</summary>
    void Lay()
    {
        mesh.ClearSurfaces();
        int n = trail.Count;
        if (n < 2) return;
        mesh.SurfaceBegin(Godot.Mesh.PrimitiveType.Triangles, material);
        float last = n - 1;
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 a0 = G(trail.CurveA[i]), a1 = G(trail.CurveA[i + 1]), b0 = G(trail.CurveB[i]), b1 = G(trail.CurveB[i + 1]);
            float u0 = i / last, u1 = (i + 1) / last;
            mesh.SurfaceSetUV(new Vector2(u0, 0)); mesh.SurfaceAddVertex(a0);
            mesh.SurfaceSetUV(new Vector2(u0, 1)); mesh.SurfaceAddVertex(b0);
            mesh.SurfaceSetUV(new Vector2(u1, 0)); mesh.SurfaceAddVertex(a1);
            mesh.SurfaceSetUV(new Vector2(u1, 0)); mesh.SurfaceAddVertex(a1);
            mesh.SurfaceSetUV(new Vector2(u0, 1)); mesh.SurfaceAddVertex(b0);
            mesh.SurfaceSetUV(new Vector2(u1, 1)); mesh.SurfaceAddVertex(b1);
        }
        mesh.SurfaceEnd();
        Laid++;
    }

    static System.Numerics.Vector3 N(Vector3 v) => new(v.X, v.Y, v.Z);
    static Vector3 G(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
