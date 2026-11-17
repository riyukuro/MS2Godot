using Godot;
using Ms2.Formats.Model;

namespace Ms2Godot.Net;

/// <summary>
/// A playing effect NIF: its node tree with the transform controllers, its static meshes with the alpha, uv-scroll and flipbook controllers, and its
/// particle systems simulated on the CPU (emitters, gravity and drag forces, size / color keys over the particle's age, grow and shrink ramps)
/// and drawn as camera-facing or velocity-aligned quads in one MultiMesh per system. All of it lives in MS2 file space (Z up) under one rotated node.
/// Not simulated: bomb and other forces, colliders, spawners (death spawns), emitter-radius / color / morph controllers, mesh particles (drawn as quads).
/// </summary>
public sealed partial class EffectInstance : Node3D
{
    sealed class MeshState { public EffectMesh Def; public MeshInstance3D Node; public ShaderMaterial Mat; public Texture2D[] Flip; public Texture2D Base; public bool Add; }

    sealed class PSys
    {
        public ParticleSystemDef Def; public MultiMeshInstance3D Node; public MultiMesh MM; public ShaderMaterial Mat;
        public Vector3[] Pos, Vel; public float[] Age, Life, Size, Rot, RotSpeed, Frame0; public int Count;
        public double[] Accum; public float[] Buf;
    }

    readonly EffectScene scene; readonly ModelBuilder mb;
    readonly Node3D z = new() { Name = "Z", Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero) };
    readonly List<MeshState> meshes = new(); readonly List<PSys> systems = new();
    readonly Transform3D[] world; readonly bool[] done;
    readonly Random rng;
    readonly float emitUntil, life;
    float t;
    public bool Finished { get; private set; }
    public int ParticlesEmitted;
    public float Delay;                  // seconds before it starts (the effect xml's startDelay)
    /// <summary>Placement in file space (Z up): translate, rotate, scale from the effect xml.</summary>
    public Transform3D FileSpace { set => z.Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero) * value; }

    static Shader meshAdd, meshMix, partAdd, partMix;
    static readonly string MeshCode = @"shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, BLEND;
uniform sampler2D tex : source_color, repeat_enable, filter_linear;
uniform bool has_tex = true;
uniform sampler2D glow_tex : source_color, repeat_enable, filter_linear;
uniform bool has_glow = false;
uniform vec2 uv_off = vec2(0.0);
uniform vec2 uv_scale = vec2(1.0);
uniform float uv_rot = 0.0;
uniform float alpha = 1.0;
uniform vec4 tint : source_color = vec4(1.0);
void fragment() {
    vec2 uv = UV - 0.5;
    float c = cos(uv_rot), s = sin(uv_rot);
    uv = vec2(c * uv.x - s * uv.y, s * uv.x + c * uv.y) * uv_scale + 0.5 + uv_off;
    vec4 t = has_tex ? texture(tex, uv) : vec4(1.0);
    vec3 col = t.rgb * tint.rgb;
    if (has_glow) col += texture(glow_tex, uv).rgb;   // a glow map adds its colour whatever the material colour is
    ALBEDO = col;
    ALPHA = clamp(t.a * tint.a * alpha, 0.0, 1.0);
}";
    static readonly string PartCode = @"shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, BLEND;
uniform sampler2D tex : source_color, filter_linear;
uniform bool has_tex = true;
uniform vec2 frames = vec2(1.0, 1.0);
varying float frame;
void vertex() { frame = INSTANCE_CUSTOM.x; }
void fragment() {
    float fi = floor(frame + 0.5);
    vec2 cell = vec2(mod(fi, frames.x), floor(fi / frames.x));
    vec4 t = has_tex ? texture(tex, (UV + cell) / frames) : vec4(1.0);
    ALBEDO = t.rgb * COLOR.rgb;
    ALPHA = clamp(t.a * COLOR.a, 0.0, 1.0);
}";

    static Shader Sh(ref Shader cache, string code, string blend) => cache ??= new Shader { Code = code.Replace("BLEND", blend) };

    static float Rad(float x) => x;

    public EffectInstance(EffectScene scene, ModelBuilder mb, float duration, int seed)
    {
        this.scene = scene; this.mb = mb; rng = new Random(seed);
        emitUntil = duration > 0 ? duration : Mathf.Max((float)scene.Duration, 0.3f);
        life = emitUntil + 1.2f;
        world = new Transform3D[scene.Nodes.Count]; done = new bool[scene.Nodes.Count];
        AddChild(z);
        foreach (var em in scene.Meshes)
        {
            if (scene.Nodes[em.Node].Culled && false) continue;
            var ms = new MeshState { Def = em };
            var m = em.Mesh;
            bool add = m.AlphaFlags >= 0 && (m.AlphaFlags & 1) != 0 && ((m.AlphaFlags >> 5) & 15) == 0;
            var mat = new ShaderMaterial { Shader = add ? Sh(ref meshAdd, MeshCode, "blend_add") : Sh(ref meshMix, MeshCode, "blend_mix") };
            ms.Base = m.Textures.TryGetValue("base", out var bn) ? mb.Texture(bn) : (m.Textures.TryGetValue("shader0", out var s0) ? mb.Texture(s0) : null);
            mat.SetShaderParameter("has_tex", ms.Base != null);
            if (ms.Base != null) mat.SetShaderParameter("tex", ms.Base);
            if (m.Textures.TryGetValue("glow", out var gn) && mb.Texture(gn) is { } gt) { mat.SetShaderParameter("has_glow", true); mat.SetShaderParameter("glow_tex", gt); }
            ms.Add = add;
            if (m.Diffuse != null || m.Emissive != null) mat.SetShaderParameter("tint", Tint(em, 0, add));
            if (em.Flip != null) ms.Flip = em.Flip.Sources.Select(f => f == null ? null : mb.Texture(f)).ToArray();
            ms.Mat = mat;
            ms.Node = new MeshInstance3D { Mesh = mb.BuildMesh(m, false), MaterialOverride = mat, Name = m.Name ?? "mesh" };
            z.AddChild(ms.Node);
            meshes.Add(ms);
        }
        foreach (var d in scene.Systems)
        {
            if (d.Emitters.Count == 0) continue;
            var ps = new PSys { Def = d };
            int n = d.MaxParticles;
            ps.Pos = new Vector3[n]; ps.Vel = new Vector3[n]; ps.Age = new float[n]; ps.Life = new float[n]; ps.Size = new float[n]; ps.Rot = new float[n]; ps.RotSpeed = new float[n]; ps.Frame0 = new float[n];
            ps.Accum = new double[d.Emitters.Count];
            var mat = new ShaderMaterial { Shader = d.Additive ? Sh(ref partAdd, PartCode, "blend_add") : Sh(ref partMix, PartCode, "blend_mix") };
            var tex = d.Texture != null ? mb.Texture(d.Texture) : null;
            mat.SetShaderParameter("has_tex", tex != null);
            if (tex != null) mat.SetShaderParameter("tex", tex);
            mat.SetShaderParameter("frames", new Vector2(d.FramesAcross, d.FramesDown));
            ps.Mat = mat;
            ps.MM = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, InstanceCount = n, VisibleInstanceCount = 0,
                Mesh = new QuadMesh { Size = Vector2.One, Material = mat }, CustomAabb = new Aabb(new Vector3(-5000, -5000, -5000), new Vector3(10000, 10000, 10000)) };
            ps.Node = new MultiMeshInstance3D { Multimesh = ps.MM, Name = d.Name ?? "particles", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            z.AddChild(ps.Node);
            systems.Add(ps);
        }
        Advance(0);
    }

    /// <summary>The unlit colour of a mesh at effect time t: the material's emissive plus diffuse (clamped), with NiMaterialColorController keys applied.
    /// Without any colour an additive mesh stays white (as before).</summary>
    static Color Tint(EffectMesh em, double t, bool add)
    {
        var m = em.Mesh;
        double[] dif = m.Diffuse?.Select(v => (double)v).ToArray() ?? new double[] { 1, 1, 1 };
        double[] emi = m.Emissive?.Select(v => (double)v).ToArray() ?? new double[3];
        foreach (var a in em.Colors)
        {
            var v = a.Curve(a.Ctl.Local(t));
            if (a.Target == 1) dif = v; else if (a.Target == 3) emi = v;
        }
        double r = Math.Min(1, dif[0] + emi[0]), g = Math.Min(1, dif[1] + emi[1]), b = Math.Min(1, dif[2] + emi[2]);
        if (add && r + g + b < 1e-4) return Colors.White;
        return new Color((float)r, (float)g, (float)b);
    }

    public override void _Process(double delta)
    {
        if (Finished) return;
        if (Delay > 0) { Delay -= (float)delta; z.Visible = false; return; }
        z.Visible = true;
        Advance((float)delta);
    }

    static Transform3D Local(EffectNode n, float t)
    {
        var tr = ModelBuilder.ToGodot(n.Local);
        if (n.Anim == null) return tr;
        double lt = n.AnimCtl != null ? n.AnimCtl.Local(t) : t;
        var basis = tr.Basis; var origin = tr.Origin;
        float scale = tr.Basis.Scale.X;
        if (n.Anim.T != null) { var v = n.Anim.T(lt); origin = new Vector3((float)v.X, (float)v.Y, (float)v.Z); }
        if (n.Anim.S != null) scale = (float)n.Anim.S(lt);
        if (n.Anim.R != null) { var q = n.Anim.R(lt); basis = new Basis(new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W).Normalized()); }
        else basis = basis.Orthonormalized();
        return new Transform3D(basis.Scaled(new Vector3(scale, scale, scale)), origin);
    }

    Transform3D World(int i, float t, Vector3 camLocal)
    {
        if (i < 0) return Transform3D.Identity;
        if (done[i]) return world[i];
        var n = scene.Nodes[i];
        var l = Local(n, t);
        var p = n.Parent >= 0 ? World(n.Parent, t, camLocal) : Transform3D.Identity;
        var w = p * l;
        if (n.Billboard)
        {
            // always face the camera: the node's X axis toward the camera, Z kept up (Gamebryo cameras look along their X axis)
            var to = camLocal - w.Origin;
            if (to.LengthSquared() > 1e-6f)
            {
                var x = to.Normalized();
                var up = Vector3.Back;       // +Z in file space
                var y = up.Cross(x);
                if (y.LengthSquared() < 1e-6f) y = Vector3.Up;
                y = y.Normalized();
                var zz = x.Cross(y).Normalized();
                float s = w.Basis.Scale.X;
                w = new Transform3D(new Basis(x, y, zz).Scaled(new Vector3(s, s, s)), w.Origin);
            }
        }
        done[i] = true; world[i] = w;
        return w;
    }

    float R() => (float)rng.NextDouble();
    float Spread(float v) => v * (2 * R() - 1);

    const float FarSq = 12000f * 12000f;

    void Advance(float dt)
    {
        t += dt;
        if (t > life) { Finished = true; QueueFree(); return; }
        var cam = GetViewport()?.GetCamera3D();
        var camLocal = cam != null ? z.GlobalTransform.AffineInverse() * cam.GlobalPosition : new Vector3(0, -1000, 300);
        Array.Clear(done);

        foreach (var ms in meshes)
        {
            var d = ms.Def;
            var nd = scene.Nodes[d.Node];
            ms.Node.Transform = World(d.Node, t, camLocal);
            ms.Node.Visible = !nd.Culled;
            if (d.Colors.Count > 0) ms.Mat.SetShaderParameter("tint", Tint(d, t, ms.Add));
            if (d.Alpha != null) ms.Mat.SetShaderParameter("alpha", (float)d.Alpha(d.AlphaCtl.Local(t)));
            if (d.Uv.Count > 0)
            {
                Vector2 off = Vector2.Zero, sc = Vector2.One; float rot = 0;
                foreach (var op in d.Uv)
                {
                    float v = (float)op.Curve(op.Ctl.Local(t));
                    switch (op.Operation) { case 0: off.X += v; break; case 1: off.Y += v; break; case 2: rot += v; break; case 3: sc.X = v; break; case 4: sc.Y = v; break; }
                }
                ms.Mat.SetShaderParameter("uv_off", off); ms.Mat.SetShaderParameter("uv_scale", sc); ms.Mat.SetShaderParameter("uv_rot", rot);
            }
            if (d.Flip != null && ms.Flip != null && ms.Flip.Length > 0)
            {
                double lt = d.Flip.Ctl.Local(t);
                int idx = d.Flip.Curve != null ? (int)Math.Floor(d.Flip.Curve(lt) + 0.001)
                                               : (int)Math.Floor((lt - d.Flip.Ctl.Start) / Math.Max(d.Flip.Ctl.Stop - d.Flip.Ctl.Start, 1e-6) * ms.Flip.Length);
                idx = Math.Clamp(idx, 0, ms.Flip.Length - 1);
                var tex = ms.Flip[idx];
                if (tex != null) ms.Mat.SetShaderParameter("tex", tex);
            }
        }

        // beyond the draw distance (camera far 18000, fog out at 20000 in most fields) nothing of a particle system shows: skip the simulation
        if (camLocal.LengthSquared() > FarSq) { foreach (var ps in systems) ps.MM.VisibleInstanceCount = 0; return; }
        foreach (var ps in systems) Step(ps, dt, camLocal);
    }

    readonly List<(Vector3 axis, float pct)> drags = new();

    void Step(PSys ps, float dt, Vector3 camLocal)
    {
        var d = ps.Def;
        // forces on at this time
        Vector3 accel = Vector3.Zero; drags.Clear();
        foreach (var f in d.Forces)
        {
            bool on = f.Active;
            if (f.On != null) on = f.On(f.OnCtl.Local(t)) > 0.5;
            if (!on) continue;
            if (f.Kind == ForceKind.Gravity) accel += new Vector3((float)f.Axis.X, (float)f.Axis.Y, (float)f.Axis.Z) * (float)f.Strength;
            else if (f.Kind == ForceKind.Drag) drags.Add((new Vector3((float)f.Axis.X, (float)f.Axis.Y, (float)f.Axis.Z), (float)f.Percentage));
        }
        // simulate
        for (int i = ps.Count - 1; i >= 0; i--)
        {
            ps.Age[i] += dt;
            if (ps.Age[i] >= ps.Life[i]) { Swap(ps, i, ps.Count - 1); ps.Count--; continue; }
            var v = ps.Vel[i] + accel * dt;
            foreach (var (axis, pct) in drags)
            {
                float comp = v.Dot(axis);
                v -= axis * comp * (1f - Mathf.Pow(Mathf.Max(0f, 1f - pct), dt * 30f));
            }
            ps.Vel[i] = v;
            ps.Pos[i] += v * dt;
            ps.Rot[i] += ps.RotSpeed[i] * dt;
        }
        // emit
        if (t <= emitUntil)
            for (int ei = 0; ei < d.Emitters.Count; ei++)
            {
                var e = d.Emitters[ei];
                double rate = e.Rate != null ? e.Rate(e.RateCtl.Local(t)) : 0;
                bool on = e.On == null || e.On(e.OnCtl.Local(t)) > 0.5;
                if (!on || rate <= 0) continue;
                ps.Accum[ei] += rate * dt;
                while (ps.Accum[ei] >= 1 && ps.Count < ps.Pos.Length) { ps.Accum[ei] -= 1; Emit(ps, e, camLocal); }
                if (ps.Count >= ps.Pos.Length) ps.Accum[ei] = 0;
            }
        // draw
        var up = Vector3.Back;
        var buf = ps.Buf ??= new float[ps.Pos.Length * 20];
        for (int i = 0; i < ps.Count; i++)
        {
            float n = ps.Age[i] / ps.Life[i];
            float size = ps.Size[i];
            if (d.SizeKeys.Length > 0) size *= (float)Lerp(d.SizeKeys, n);
            if (d.GrowTime > 0) size *= Mathf.Clamp(ps.Age[i] / (float)d.GrowTime, 0f, 1f);
            if (d.ShrinkTime > 0) size *= Mathf.Clamp((ps.Life[i] - ps.Age[i]) / (float)d.ShrinkTime, 0f, 1f);
            var color = Colors.White;
            if (d.ColorKeys.Length > 0) color = ColorAt(d.ColorKeys, n);
            var pos = ps.Pos[i];
            var toCam = (camLocal - pos); toCam = toCam.LengthSquared() > 1e-6f ? toCam.Normalized() : Vector3.Back;
            Basis b;
            if (d.Aligned)
            {
                var vel = ps.Vel[i]; float sp = vel.Length();
                var along = sp > 1e-3f ? vel / sp : Vector3.Right;
                float stretch = Mathf.Clamp((float)(d.ScaleRestV + d.ScaleAmountV * sp * 0.01), (float)Math.Min(d.ScaleRestV, d.ScaleLimitV), (float)Math.Max(d.ScaleRestV, d.ScaleLimitV));
                var side = along.Cross(toCam); side = side.LengthSquared() > 1e-6f ? side.Normalized() : up.Cross(along).Normalized();
                b = new Basis(along * size * stretch, side * size, toCam);
            }
            else
            {
                var right = up.Cross(toCam); right = right.LengthSquared() > 1e-6f ? right.Normalized() : Vector3.Right;
                var upv = toCam.Cross(right).Normalized();
                float c = Mathf.Cos(ps.Rot[i]), s = Mathf.Sin(ps.Rot[i]);
                b = new Basis((right * c + upv * s) * size, (upv * c - right * s) * size, toCam);
            }
            // one buffer write per system instead of three engine calls per particle: 12 transform floats (rows), colour, custom data
            int o = i * 20;
            buf[o] = b.X.X; buf[o + 1] = b.Y.X; buf[o + 2] = b.Z.X; buf[o + 3] = pos.X;
            buf[o + 4] = b.X.Y; buf[o + 5] = b.Y.Y; buf[o + 6] = b.Z.Y; buf[o + 7] = pos.Y;
            buf[o + 8] = b.X.Z; buf[o + 9] = b.Y.Z; buf[o + 10] = b.Z.Z; buf[o + 11] = pos.Z;
            buf[o + 12] = color.R; buf[o + 13] = color.G; buf[o + 14] = color.B; buf[o + 15] = color.A;
            float frame = ps.Frame0[i];
            if (d.NumFrames > 1)
            {
                float u = Mathf.Clamp((n - (float)d.InitialTime) / Mathf.Max((float)(d.FinalTime - d.InitialTime), 1e-4f), 0f, 1f);
                frame = (ps.Frame0[i] + Mathf.Floor(u * d.NumFrames * (d.PingPong ? 1 : 0.999f))) % Mathf.Max(d.FramesAcross * d.FramesDown, 1);
            }
            buf[o + 16] = frame; buf[o + 17] = 0; buf[o + 18] = 0; buf[o + 19] = 0;
        }
        if (ps.Count > 0) ps.MM.Buffer = buf;
        ps.MM.VisibleInstanceCount = ps.Count;
    }

    static void Swap(PSys p, int a, int b)
    {
        if (a == b) return;
        (p.Pos[a], p.Pos[b]) = (p.Pos[b], p.Pos[a]); (p.Vel[a], p.Vel[b]) = (p.Vel[b], p.Vel[a]); (p.Age[a], p.Age[b]) = (p.Age[b], p.Age[a]);
        (p.Life[a], p.Life[b]) = (p.Life[b], p.Life[a]); (p.Size[a], p.Size[b]) = (p.Size[b], p.Size[a]); (p.Rot[a], p.Rot[b]) = (p.Rot[b], p.Rot[a]);
        (p.RotSpeed[a], p.RotSpeed[b]) = (p.RotSpeed[b], p.RotSpeed[a]); (p.Frame0[a], p.Frame0[b]) = (p.Frame0[b], p.Frame0[a]);
    }

    static double Lerp((double t, double v)[] k, float n)
    {
        if (n <= k[0].t) return k[0].v;
        if (n >= k[^1].t) return k[^1].v;
        for (int i = 0; i + 1 < k.Length; i++)
            if (n >= k[i].t && n <= k[i + 1].t) { double u = (n - k[i].t) / Math.Max(k[i + 1].t - k[i].t, 1e-9); return k[i].v * (1 - u) + k[i + 1].v * u; }
        return k[^1].v;
    }

    static Color ColorAt((double t, byte r, byte g, byte b, byte a)[] k, float n)
    {
        Color C(int i) => new(k[i].r / 255f, k[i].g / 255f, k[i].b / 255f, k[i].a / 255f);
        if (n <= k[0].t) return C(0);
        if (n >= k[^1].t) return C(k.Length - 1);
        for (int i = 0; i + 1 < k.Length; i++)
            if (n >= k[i].t && n <= k[i + 1].t) { float u = (float)((n - k[i].t) / Math.Max(k[i + 1].t - k[i].t, 1e-9)); return C(i).Lerp(C(i + 1), u); }
        return C(k.Length - 1);
    }

    void Emit(PSys ps, EmitterDef e, Vector3 camLocal)
    {
        int i = ps.Count++;
        ParticlesEmitted++;
        var ew = World(e.Node >= 0 ? e.Node : ps.Def.Node, t, camLocal);
        Vector3 local;
        switch (e.Kind)
        {
            case EmitterKind.Box: local = new Vector3(Spread((float)e.Width / 2), Spread((float)e.Height / 2), Spread((float)e.Depth / 2)); break;
            case EmitterKind.Sphere: { Vector3 v; do v = new Vector3(Spread(1), Spread(1), Spread(1)); while (v.LengthSquared() > 1); local = v * (float)e.Radius; break; }
            case EmitterKind.Cylinder: { float a = R() * Mathf.Tau, r = Mathf.Sqrt(R()) * (float)e.Radius; local = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, Spread((float)e.Height / 2)); break; }
            case EmitterKind.Mesh when e.MeshPositions != null && e.MeshPositions.Length >= 3:
            {
                int vi = rng.Next(e.MeshPositions.Length / 3) * 3;
                var v = new Vector3(e.MeshPositions[vi], e.MeshPositions[vi + 1], e.MeshPositions[vi + 2]);
                var mw = e.MeshNode >= 0 ? World(e.MeshNode, t, camLocal) : Transform3D.Identity;
                // the emitter's own transform positions the mesh points relative to the emitter object
                ps.Pos[i] = mw * v;
                local = Vector3.Zero;
                goto haveMeshPos;
            }
            default: local = Vector3.Zero; break;
        }
        ps.Pos[i] = ew * local;
        haveMeshPos:
        float dec = (float)e.Declination + Spread((float)e.DeclinationVar), pl = (float)e.PlanarAngle + Spread((float)e.PlanarAngleVar);
        var dirLocal = new Vector3(Mathf.Cos(dec), Mathf.Sin(dec) * Mathf.Cos(pl), Mathf.Sin(dec) * Mathf.Sin(pl));
        var dir = (ew.Basis.Orthonormalized() * dirLocal).Normalized();
        float speed = (float)e.Speed + Spread((float)e.SpeedVar);
        ps.Vel[i] = dir * speed;
        ps.Life[i] = Mathf.Max(0.02f, (float)e.Lifespan + Spread((float)e.LifespanVar));
        ps.Size[i] = Mathf.Max(0f, (float)e.Size + Spread((float)e.SizeVar));
        ps.Age[i] = 0;
        ps.Rot[i] = (float)e.RotAngle + Spread((float)e.RotAngleVar);
        float rs = (float)e.RotSpeed + Spread((float)e.RotSpeedVar);
        if (e.RandomRotSpeedSign && R() < 0.5f) rs = -rs;
        ps.RotSpeed[i] = rs;
        ps.Frame0[i] = ps.Def.InitialFrame + (ps.Def.InitialFrameVar > 0 ? rng.Next(ps.Def.InitialFrameVar + 1) : 0);
    }
}
