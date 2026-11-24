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
    double clock;
    bool started, tracking;
    // the trail nodes: a bone (skeleton, index), a named node, or an effect instance's node (instance, index)
    Skeleton3D boneSk1, boneSk2; int bone1 = -1, bone2 = -1;
    Node3D node1, node2;
    EffectInstance fx1, fx2; int fxNode1 = -1, fxNode2 = -1;
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
    /// step that makes no swoosh, a nif with no mesh. <paramref name="ownerKind"/> is the target's object kind (m_unk_18).</summary>
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

    static readonly Dictionary<(Texture2D, int, float), Material> materials = new();
    static Shader add, mix;

    static Material MaterialFor(Texture2D tex, int alphaMode, float alpha)
    {
        if (materials.TryGetValue((tex, alphaMode, alpha), out var m)) return m;
        // alphaMode 0: src alpha / one; 1: src alpha / inverse src alpha; 2: one / one (src alpha with an alpha of 1);
        // -1 (a nif mesh with no blending): drawn as 1
        bool isMix = alphaMode == EffectStripParam.AlphaStandard || alphaMode == -1;
        var sm = new ShaderMaterial { Shader = isMix ? mix ??= ShaderFor(false) : add ??= ShaderFor(true) };
        sm.SetShaderParameter(Tex, tex);
        sm.SetShaderParameter(HasTex, tex != null);
        sm.SetShaderParameter(Opacity, alpha);
        sm.SetShaderParameter(Unweighted, alphaMode == EffectStripParam.AlphaAdd);
        return materials[(tex, alphaMode, alpha)] = sm;
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
        if (life <= 0 || fx1 != null && !IsInstanceValid(fx1) || fx2 != null && !IsInstanceValid(fx2)) { QueueFree(); return; }
        if (!tracking || !Visible) return;   // an app-culled root lays nothing (vfunc_9 @0x14181ecd0)
        clock += delta;
        if (trail.Sample(clock, N(Point(1)), N(Point(2)))) Lay();
    }

    /// <summary>vfunc_4: the two trail nodes by name under the actor; one not found stops the trail.</summary>
    void Start()
    {
        tracking = Find(param.Node1, out boneSk1, out bone1, out node1, out fx1, out fxNode1);
        if (!tracking && FxTrace) GD.Print($"CSwooshEffect can't find TrailNode1 [{param.Node1}]");
        bool two = Find(param.Node2, out boneSk2, out bone2, out node2, out fx2, out fxNode2);
        if (!two && FxTrace) GD.Print($"CSwooshEffect can't find TrailNode2 [{param.Node2}]");
        if (FxTrace) GD.Print($"fx: swoosh {param.Node1}/{param.Node2} on {actor.Name}: {(tracking && two ? "tracking" : "nodes missing")} at {Time.GetTicksMsec()}");
        tracking &= two;
        if (!tracking) NodesMissing++;
    }

    bool Find(string name, out Skeleton3D sk, out int bone, out Node3D node, out EffectInstance fx, out int fxNode)
    {
        sk = null; bone = -1; node = null; fx = null; fxNode = -1;
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var s in actor.FindChildren("*", "Skeleton3D", true, false))
            if (s is Skeleton3D k && k.FindBone(name) is int b and >= 0) { sk = k; bone = b; return true; }
        if (actor.FindChild(name, true, false) is Node3D n) { node = n; return true; }
        foreach (var c in actor.FindChildren("*", "Node3D", true, false))
            if (c is EffectInstance e && e.NodeNamed(name) is int i and >= 0) { fx = e; fxNode = i; return true; }
        return false;
    }

    Vector3 Point(int which)
    {
        var (sk, bone, node, fx, fxNode) = which == 1 ? (boneSk1, bone1, node1, fx1, fxNode1) : (boneSk2, bone2, node2, fx2, fxNode2);
        if (sk != null) return sk.GlobalTransform * sk.GetBoneGlobalPose(bone).Origin;
        if (node != null) return IsInstanceValid(node) ? node.GlobalPosition : default;
        return fx.NodePoint(fxNode);
    }

    /// <summary>The strip's triangles (i, i + n, i + 1) and (i + 1, i + n, i + 1 + n) over the two rows, uv (k / (n - 1), 0 or 1)
    /// (UpdateTrail, :571-617).</summary>
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
