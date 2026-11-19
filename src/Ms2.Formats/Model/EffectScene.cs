using Ms2.Formats.Nif;

namespace Ms2.Formats.Model;

/// <summary>Gamebryo's NiTimeController timing: frequency, phase, the start/stop window and the cycle type (flags bits 1-2: 0 loop, 1 reverse, 2 clamp).</summary>
public sealed class TimeCtl
{
    public double Freq = 1, Phase, Start, Stop;
    public int Cycle;
    public bool Active = true;

    public static TimeCtl Of(NifStruct c)
    {
        long flags = c.IntOr("Flags", 0);
        return new TimeCtl { Freq = c.Float("Frequency"), Phase = c.Float("Phase"), Start = c.Float("Start Time"), Stop = c.Float("Stop Time"), Cycle = (int)((flags >> 1) & 3), Active = (flags & 8) != 0 };
    }

    /// <summary>The animation time a controller reads at effect time t.</summary>
    public double Local(double t)
    {
        double x = Freq * t + Phase, span = Stop - Start;
        if (span <= 1e-9) return Start;
        switch (Cycle)
        {
            case 0: { double m = (x - Start) % span; if (m < 0) m += span; return Start + m; }
            case 1: { double m = (x - Start) % (2 * span); if (m < 0) m += 2 * span; return Start + (m < span ? m : 2 * span - m); }
            default: return Math.Clamp(x, Start, Stop);
        }
    }
}

public sealed class EffectNode
{
    public int Block, Parent = -1;
    public string Name;
    public Xform Local = Xform.Identity;
    public bool Billboard;
    public bool Culled;                          // NiAVObject APP_CULLED flag
    public KfChannel Anim; public TimeCtl AnimCtl;
}

/// <summary>An NiTextureTransformController on a mesh's texturing property: the value replaces one component (operation 0
/// translate U, 1 translate V, 2 rotate, 3 scale U, 4 scale V) of the transform of one map, Slot (a ModelMesh.Maps key):
/// the fixed map at "Texture Slot", or with "Shader Map" set the shader map at that index of the shader map array
/// (NiTextureTransformController::SetInternalMap, NiTextureTransformController.inl:37-62).</summary>
public sealed class UvOp { public int Operation; public string Slot; public Func<double, double> Curve; public TimeCtl Ctl; }
public sealed class FlipAnim { public string[] Sources; public Func<double, double> Curve; public TimeCtl Ctl; }

/// <summary>An animated NiMaterialProperty colour (NiMaterialColorController): Target 0 ambient, 1 diffuse, 2 specular, 3 emissive (self illumination).</summary>
public sealed class ColorAnim { public int Target; public Func<double, double[]> Curve; public TimeCtl Ctl; }

public sealed class EffectMesh
{
    public List<ColorAnim> Colors = new();
    public ModelMesh Mesh; public int Node;
    public Func<double, double> Alpha; public TimeCtl AlphaCtl;
    public List<UvOp> Uv = new();
    public FlipAnim Flip;

    /// <summary>The material alpha at effect time t: an NiAlphaController's value replaces the NiMaterialProperty alpha (its
    /// m_fAlpha, the controller's target), else the static alpha read from the file (1 without a material: NiMaterialProperty's
    /// default). The client's opacity is this times the base map's alpha (NiStandardMaterial HandleColorAccumulation and
    /// CompositeFinalRGBAColor; ms2standardmaterial.cpp:758-791).</summary>
    public double Opacity(double t) => Alpha != null ? Alpha(AlphaCtl.Local(t)) : Mesh.MaterialAlpha;
}

public enum EmitterKind { Mesh, Box, Sphere, Cylinder, Other }

public sealed class EmitterDef
{
    public EmitterKind Kind; public string Name; public int Node = -1;
    public double Speed, SpeedVar, SpeedFlipRatio, Declination, DeclinationVar, PlanarAngle, PlanarAngleVar, Size, SizeVar, Lifespan, LifespanVar;
    public double RotAngle, RotAngleVar, RotSpeed, RotSpeedVar; public Vec3 RotAxis; public bool RandomRotSpeedSign, RandomRotAxis;
    public double Width, Height, Depth, Radius;
    public float[] MeshPositions;                // mesh emitter: vertices (xyz) in the mesh's own space
    public int MeshNode = -1;                    // node the mesh hangs from
    public Func<double, double> Rate; public TimeCtl RateCtl;           // births per second (NiPSEmitParticlesCtlr)
    public Func<double, double> On; public TimeCtl OnCtl;               // emitter active
}

public enum ForceKind { Bomb, Gravity, Drag, Other }

public sealed class ForceDef
{
    public ForceKind Kind; public string Name, Type; public bool Active = true;
    public Vec3 Axis; public double Strength, Decay, DeltaV, Percentage; public int ForceType;
    public Func<double, double> On; public TimeCtl OnCtl;
}

public sealed class ParticleSystemDef
{
    public int Node; public string Name;
    public List<EmitterDef> Emitters = new();
    public List<ForceDef> Forces = new();
    public (double t, double v)[] SizeKeys = Array.Empty<(double, double)>();
    public (double t, byte r, byte g, byte b, byte a)[] ColorKeys = Array.Empty<(double, byte, byte, byte, byte)>();
    public double GrowTime, ShrinkTime; public int GrowGeneration, ShrinkGeneration;
    public bool Aligned;                         // NiPSAlignedQuadGenerator (stretched along velocity) vs facing
    public double ScaleAmountU, ScaleLimitU, ScaleRestU, ScaleAmountV, ScaleLimitV, ScaleRestV;
    public int FramesAcross = 1, FramesDown = 1, NumFrames, InitialFrame, InitialFrameVar; public double InitialTime, FinalTime; public bool PingPong;
    public string Texture;                       // base texture file (or the first shader map)
    public int AlphaFlags = -1;                  // NiAlphaProperty flags of the system
    public bool Additive => AlphaFlags >= 0 && ((AlphaFlags >> 5) & 15) == 0 && (AlphaFlags & 1) != 0;
    public int MaxParticles = 100; public bool WorldSpace;
    public int Colliders, Spawners;
}

/// <summary>
/// An effect NIF as a scene: its node tree with node transform controllers, the static meshes with alpha, uv-scroll and flipbook controllers,
/// and the particle systems with their emitters, forces, simulator steps and generator. Read through nif.xml; the time controllers keep Gamebryo's
/// frequency / phase / start / stop / cycle semantics.
/// </summary>
public sealed class EffectScene
{
    public List<EffectNode> Nodes = new();
    public List<EffectMesh> Meshes = new();
    public List<ParticleSystemDef> Systems = new();
    public Dictionary<int, int> NodeOfBlock = new();
    public HashSet<string> Unsupported = new();          // controller / block types met but not read
    public double Duration;                              // longest controller window

    static Xform XformOf(NifStruct b)
    {
        var t = b.Struct("Translation");
        var r = b.Struct("Rotation");
        var R = r.Values.Select(v => (double)(float)v).ToArray();
        return new Xform(new Vec3(t.Float("x"), t.Float("y"), t.Float("z")), R, b.Float("Scale"));
    }

    // ---- curves ----

    sealed class K { public double T, V, Fwd, Bwd; }

    static Func<double, double> Curve(NifFile n, long interp)
    {
        if (interp < 0) return null;
        var B = n.Blocks; var e = B[(int)interp];
        double constant = e.TryGetValue("Value", out var v) ? (v is float f ? f : v is long l ? l : 0) : 0;
        if (e.TryGetValue("Value", out var vv) && vv is NifStruct) return null;   // a transform / color interpolator
        long di = e.IntOr("Data", -1);
        if (di < 0) return _ => constant;
        var data = B[(int)di];
        if (!data.TryGetValue("Data", out var g) || g is not NifStruct grp || grp.IntOr("Num Keys", 0) == 0) return _ => constant;
        long it = grp.Int("Interpolation");
        var keys = new List<K>();
        foreach (var k in grp.Structs("Keys"))
        {
            double val = k["Value"] is float fv ? fv : Convert.ToDouble(k["Value"]);
            var key = new K { T = k.Float("Time"), V = val };
            if (it == 2) { key.Fwd = k["Forward"] is float a ? a : 0; key.Bwd = k["Backward"] is float b ? b : 0; }
            keys.Add(key);
        }
        return t =>
        {
            if (t <= keys[0].T) return keys[0].V;
            if (t >= keys[^1].T) return keys[^1].V;
            for (int i = 0; i + 1 < keys.Count; i++)
            {
                var a = keys[i]; var b = keys[i + 1];
                if (t < a.T || t > b.T) continue;
                if (it == 5) return a.V;                                          // constant: hold the previous key
                double span = b.T - a.T, u = (t - a.T) / (span == 0 ? 1 : span);
                if (it == 2)
                {
                    double h00 = 2 * u * u * u - 3 * u * u + 1, h10 = u * u * u - 2 * u * u + u, h01 = -2 * u * u * u + 3 * u * u, h11 = u * u * u - u * u;
                    return h00 * a.V + h10 * a.Bwd + h01 * b.V + h11 * b.Fwd;
                }
                return a.V * (1 - u) + b.V * u;
            }
            return keys[^1].V;
        };
    }

    /// <summary>An rgb curve from an NiPoint3Interpolator (NiPosData) or NiColorInterpolator (NiColorData); null without keys or a pose.</summary>
    static Func<double, double[]> ColorCurve(NifFile n, long interp)
    {
        if (interp < 0) return null;
        var B = n.Blocks; var e = B[(int)interp];
        double[] Rgb(object o) => o is NifStruct s ? new[] { (double)s.Float(s.ContainsKey("x") ? "x" : "r"), s.Float(s.ContainsKey("y") ? "y" : "g"), s.Float(s.ContainsKey("z") ? "z" : "b") } : null;
        var pose = e.TryGetValue("Value", out var pv) ? Rgb(pv) : null;
        if (pose != null && (double.IsNaN(pose[0]) || pose[0] < -1e20)) pose = null;       // the invalid-pose marker
        long di = e.IntOr("Data", -1);
        if (di < 0 || !B[(int)di].TryGetValue("Data", out var g) || g is not NifStruct grp || grp.IntOr("Num Keys", 0) == 0)
            return pose == null ? null : _ => pose;
        long it = grp.Int("Interpolation");
        var keys = new List<(double t, double[] v, double[] f, double[] b)>();
        foreach (var k in grp.Structs("Keys"))
            keys.Add((k.Float("Time"), Rgb(k["Value"]), it == 2 ? Rgb(k["Forward"]) : null, it == 2 ? Rgb(k["Backward"]) : null));
        return t =>
        {
            if (t <= keys[0].t) return keys[0].v;
            if (t >= keys[^1].t) return keys[^1].v;
            for (int i = 0; i + 1 < keys.Count; i++)
            {
                var a = keys[i]; var b = keys[i + 1];
                if (t < a.t || t > b.t) continue;
                if (it == 5) return a.v;
                double span = b.t - a.t, u = (t - a.t) / (span == 0 ? 1 : span);
                var r = new double[3];
                for (int c = 0; c < 3; c++)
                    r[c] = it == 2 ? (2 * u * u * u - 3 * u * u + 1) * a.v[c] + (u * u * u - 2 * u * u + u) * a.b[c] + (-2 * u * u * u + 3 * u * u) * b.v[c] + (u * u * u - u * u) * b.f[c]
                                   : a.v[c] * (1 - u) + b.v[c] * u;        // TBC keys fall back to linear
                return r;
            }
            return keys[^1].v;
        };
    }

    static Vec3 V3(NifStruct s) => new(s.Float("x"), s.Float("y"), s.Float("z"));

    public static EffectScene Load(NifFile n)
    {
        var B = n.Blocks;
        var sc = new EffectScene();
        var parent = new Dictionary<int, int>();
        for (int i = 0; i < B.Count; i++)
            if (B[i].TryGetValue("Children", out var c) && c is long[] kids)
                foreach (var k in kids) if (k >= 0) parent[(int)k] = i;

        bool IsNode(string t) => t is "NiNode" or "NiBillboardNode" or "NiMesh" or "NiPSParticleSystem" or "NiPSMeshParticleSystem";
        for (int i = 0; i < B.Count; i++)
        {
            if (!IsNode(B[i].Type)) continue;
            sc.NodeOfBlock[i] = sc.Nodes.Count;
            sc.Nodes.Add(new EffectNode
            {
                Block = i, Name = n.Name(i), Local = XformOf(B[i]), Billboard = B[i].Type == "NiBillboardNode",
                Culled = (B[i].IntOr("Flags", 0) & 1) != 0,
            });
        }
        foreach (var nd in sc.Nodes) nd.Parent = parent.TryGetValue(nd.Block, out var p) && sc.NodeOfBlock.TryGetValue(p, out var pn) ? pn : -1;

        var model = ModelData.Load(n);
        var meshOf = new Dictionary<int, EffectMesh>();
        foreach (var m in model.Meshes)
        {
            if (m.Block < 0 || !sc.NodeOfBlock.TryGetValue(m.Block, out int node)) continue;
            var em = new EffectMesh { Mesh = m, Node = node };
            sc.Meshes.Add(em); meshOf[m.Block] = em;
        }
        // mesh blocks that reference a property block (texturing) so texture controllers can find their meshes
        IEnumerable<EffectMesh> MeshesWithProperty(int prop) => sc.Meshes.Where(m => B[m.Mesh.Block].Ints("Properties").Contains(prop));

        var systems = new Dictionary<int, ParticleSystemDef>();
        foreach (var (blk, node) in sc.NodeOfBlock)
            if (B[blk].Type is "NiPSParticleSystem" or "NiPSMeshParticleSystem") systems[blk] = sc.ReadSystem(n, blk, node);

        // controllers
        for (int i = 0; i < B.Count; i++)
        {
            if (!B[i].TryGetValue("Controller", out var cv) || cv is not long first || first < 0) continue;
            long ci = first; int guard = 0;
            while (ci >= 0 && guard++ < 64)
            {
                var c = B[(int)ci];
                sc.ReadController(n, c, i, meshOf, systems, MeshesWithProperty);
                ci = c.IntOr("Next Controller", -1);
            }
        }
        foreach (var s in systems.Values) sc.Systems.Add(s);
        foreach (var t in B.Select(b => b.Type).Distinct())
            if (t.StartsWith("NiPS", StringComparison.Ordinal) && !KnownPs.Contains(t)) sc.Unsupported.Add(t);
        return sc;
    }

    static readonly HashSet<string> KnownPs = new()
    {
        "NiPSParticleSystem", "NiPSSimulator", "NiPSSimulatorGeneralStep", "NiPSSimulatorForcesStep", "NiPSSimulatorCollidersStep", "NiPSSimulatorFinalStep",
        "NiPSSimulatorMeshAlignStep", "NiPSMeshEmitter", "NiPSBoxEmitter", "NiPSSphereEmitter", "NiPSCylinderEmitter", "NiPSAlignedQuadGenerator",
        "NiPSFacingQuadGenerator", "NiPSBoundUpdater", "NiPSBombForce", "NiPSGravityForce", "NiPSDragForce", "NiPSEmitParticlesCtlr", "NiPSForceActiveCtlr",
    };

    // NiTexturingProperty::MapEnum (NiTexturingProperty.h:332) as ModelMesh.Maps keys
    static readonly string[] MapKeys = { "base", "dark", "detail", "gloss", "glow", "bump", "normal", "parallax", "decal0" };

    void ReadController(NifFile n, NifStruct c, int ownerBlock, Dictionary<int, EffectMesh> meshOf, Dictionary<int, ParticleSystemDef> systems, Func<int, IEnumerable<EffectMesh>> meshesWithProperty)
    {
        var B = n.Blocks;
        long target = c.IntOr("Target", ownerBlock), interp = c.IntOr("Interpolator", -1);
        var ctl = TimeCtl.Of(c);
        Duration = Math.Max(Duration, ctl.Stop);
        switch (c.Type)
        {
            case "NiTransformController":
                if (NodeOfBlock.TryGetValue((int)target, out int node) && interp >= 0)
                {
                    var ch = KfAnimation.ChannelOf(n, B[(int)interp]);
                    if (ch != null) { Nodes[node].Anim = ch; Nodes[node].AnimCtl = ctl; }
                }
                break;
            case "NiAlphaController":
            {
                // The client finds alpha controllers on the material property as well as on the object
                // (HasAlphaControllerProperty walks the property's controllers): a property's controller fades every mesh using it.
                var f = Curve(n, interp);
                if (meshOf.TryGetValue((int)target, out var am)) { am.Alpha = f; am.AlphaCtl = ctl; }
                else foreach (var m in meshesWithProperty((int)target)) { m.Alpha = f; m.AlphaCtl = ctl; }
                break;
            }
            case "NiMaterialColorController":
                if (ColorCurve(n, interp) is { } cc)
                    foreach (var m in meshesWithProperty((int)target)) m.Colors.Add(new ColorAnim { Target = (int)c.IntOr("Target Color", 1), Curve = cc, Ctl = ctl });
                break;
            case "NiTextureTransformController":
            {
                int slot = (int)c.IntOr("Texture Slot", 0);
                bool shaderMap = c.IntOr("Shader Map", 0) != 0;
                foreach (var m in meshesWithProperty((int)target))
                {
                    string key = shaderMap ? m.Mesh.Maps.FirstOrDefault(kv => kv.Value.ShaderIndex == slot).Key : (slot < MapKeys.Length ? MapKeys[slot] : null);
                    if (key != null && Curve(n, interp) is { } f) m.Uv.Add(new UvOp { Operation = (int)c.IntOr("Operation", 0), Slot = key, Curve = f, Ctl = ctl });
                }
                break;
            }
            case "NiFlipController":
            {
                var src = c.Ints("Sources").Select(s => s >= 0 ? n.StrField(B[(int)s], "File Name") : null).ToArray();
                foreach (var m in meshesWithProperty((int)target)) m.Flip = new FlipAnim { Sources = src, Curve = Curve(n, interp), Ctl = ctl };
                break;
            }
            case "NiPSEmitParticlesCtlr":
                if (systems.TryGetValue((int)target, out var ps))
                {
                    string en = n.Str(c.IntOr("Emitter Name", -1));
                    foreach (var e in ps.Emitters.Where(e => e.Name == en || ps.Emitters.Count == 1))
                    {
                        e.Rate = Curve(n, interp); e.RateCtl = ctl;
                        e.On = Curve(n, c.IntOr("Emitter Active Interpolator", -1)); e.OnCtl = ctl;
                    }
                }
                break;
            case "NiPSForceActiveCtlr":
                if (systems.TryGetValue((int)target, out var ps2))
                {
                    string fn = n.Str(c.IntOr("Force Name", -1));
                    foreach (var f in ps2.Forces.Where(f => f.Name == fn)) { f.On = Curve(n, interp); f.OnCtl = ctl; }
                }
                break;
            default:
                if (c.Type.StartsWith("Ni", StringComparison.Ordinal)) Unsupported.Add(c.Type);
                break;
        }
    }

    ParticleSystemDef ReadSystem(NifFile n, int blk, int node)
    {
        var B = n.Blocks; var b = B[blk];
        var d = new ParticleSystemDef { Node = node, Name = n.Name(blk), MaxParticles = (int)Math.Clamp(b.IntOr("Max Num Particles", 100), 1, 2000), WorldSpace = b.IntOr("World Space", 0) != 0, Spawners = (int)b.IntOr("Num Spawners", 0) };

        foreach (var pi in b.Ints("Properties"))
        {
            if (pi < 0) continue;
            var p = B[(int)pi];
            if (p.Type == "NiAlphaProperty") d.AlphaFlags = (int)p.Int("Flags");
            else if (p.Type == "NiTexturingProperty")
            {
                if (p.IntOr("Has Base Texture", 0) != 0 && p.Has("Base Texture") && p.Struct("Base Texture").Int("Source") is long s0 && s0 >= 0) d.Texture = n.StrField(B[(int)s0], "File Name");
                else if (p.TryGetValue("Shader Textures", out var stv) && stv is object[] sts)
                    foreach (NifStruct st in sts)
                        if (st.IntOr("Has Map", 0) != 0 && st.Struct("Map").Int("Source") is long s1 && s1 >= 0 && d.Texture == null) d.Texture = n.StrField(B[(int)s1], "File Name");
            }
        }

        long sim = b.IntOr("Simulator", -1);
        if (sim >= 0)
            foreach (var si in B[(int)sim].Ints("Simulation Steps"))
            {
                var st = B[(int)si];
                switch (st.Type)
                {
                    case "NiPSSimulatorGeneralStep":
                        d.GrowTime = st.Float("Grow Time"); d.ShrinkTime = st.Float("Shrink Time"); d.GrowGeneration = (int)st.Int("Grow Generation"); d.ShrinkGeneration = (int)st.Int("Shrink Generation");
                        // the key structs list the value before the time in the file, so nif.xml's Time / Value fields come out swapped:
                        // float keys: Time holds the value and Value the time; byte-color keys: the 4 color bytes sit in the bits of "Time" and the time in the bits of "Value"
                        d.SizeKeys = st.Structs("Size Keys").Select(k => ((double)Convert.ToSingle(k["Value"]), (double)k.Float("Time"))).ToArray();
                        d.ColorKeys = st.Structs("Color Keys").Select(k =>
                        {
                            var col = k.Struct("Value");
                            float t = BitConverter.ToSingle(new[] { (byte)col.Int("r"), (byte)col.Int("g"), (byte)col.Int("b"), (byte)col.Int("a") }, 0);
                            var cb = BitConverter.GetBytes(k.Float("Time"));
                            return ((double)t, cb[0], cb[1], cb[2], cb[3]);
                        }).ToArray();
                        break;
                    case "NiPSSimulatorForcesStep":
                        foreach (var fi in st.Ints("Forces"))
                        {
                            var f = B[(int)fi];
                            var fd = new ForceDef { Name = n.Name((int)fi), Type = f.Type, Active = f.IntOr("Active", 1) != 0 };
                            switch (f.Type)
                            {
                                case "NiPSGravityForce": fd.Kind = ForceKind.Gravity; fd.Axis = V3(f.Struct("Gravity Axis")); fd.Strength = f.Float("Strength"); fd.ForceType = (int)f.Int("Force Type"); fd.Decay = f.Float("Decay"); break;
                                case "NiPSDragForce": fd.Kind = ForceKind.Drag; fd.Axis = V3(f.Struct("Drag Axis")); fd.Percentage = f.Float("Percentage"); break;
                                case "NiPSBombForce": fd.Kind = ForceKind.Bomb; fd.Axis = V3(f.Struct("Bomb Axis")); fd.DeltaV = f.Float("Delta V"); fd.Decay = f.Float("Decay"); break;
                                default: fd.Kind = ForceKind.Other; Unsupported.Add(f.Type); break;
                            }
                            d.Forces.Add(fd);
                        }
                        break;
                    case "NiPSSimulatorCollidersStep": d.Colliders += (int)st.IntOr("Num Colliders", 0); break;
                }
            }

        long gen = b.IntOr("Generator", -1);
        if (gen >= 0)
        {
            var g = B[(int)gen];
            d.Aligned = g.Type == "NiPSAlignedQuadGenerator";
            double F(string k, double def) => g.TryGetValue(k, out var v) && v is float f ? f : def;
            d.ScaleAmountU = F("Scale Amount U", 0); d.ScaleLimitU = F("Scale Limit U", 1); d.ScaleRestU = F("Scale Rest U", 1);
            d.ScaleAmountV = F("Scale Amount V", 0); d.ScaleLimitV = F("Scale Limit V", 1); d.ScaleRestV = F("Scale Rest V", 1);
            if (g.Has("Num Frames Across"))
            {
                d.FramesAcross = Math.Max(1, (int)g.Int("Num Frames Across")); d.FramesDown = Math.Max(1, (int)g.Int("Num Frames Down"));
                d.NumFrames = (int)g.Int("Num Frames"); d.InitialFrame = (int)g.Int("Initial Frame"); d.InitialFrameVar = (int)g.Int("Initial Frame Variation");
                d.InitialTime = g.Float("Initial Time"); d.FinalTime = g.Float("Final Time"); d.PingPong = g.IntOr("Ping Pong", 0) != 0;
            }
        }

        foreach (var ei in b.Ints("Emitters"))
        {
            if (ei < 0) continue;
            var e = B[(int)ei];
            var ed = new EmitterDef
            {
                Name = n.Name((int)ei), Speed = e.Float("Speed"), SpeedVar = e.Float("Speed Var"), SpeedFlipRatio = e.Float("Speed Flip Ratio"),
                Declination = e.Float("Declination"), DeclinationVar = e.Float("Declination Var"), PlanarAngle = e.Float("Planar Angle"), PlanarAngleVar = e.Float("Planar Angle Var"),
                Size = e.Float("Size"), SizeVar = e.Float("Size Var"), Lifespan = e.Float("Lifespan"), LifespanVar = e.Float("Lifespan Var"),
                RotAngle = e.Float("Rotation Angle"), RotAngleVar = e.Float("Rotation Angle Var"), RotSpeed = e.Float("Rotation Speed"), RotSpeedVar = e.Float("Rotation Speed Var"),
                RotAxis = V3(e.Struct("Rotation Axis")), RandomRotSpeedSign = e.IntOr("Random Rot Speed Sign", 0) != 0, RandomRotAxis = e.IntOr("Random Rot Axis", 0) != 0,
            };
            long eo = e.IntOr("Emitter Object", -1);
            if (eo >= 0 && NodeOfBlock.TryGetValue((int)eo, out int en)) ed.Node = en;
            switch (e.Type)
            {
                case "NiPSBoxEmitter": ed.Kind = EmitterKind.Box; ed.Width = e.Float("Emitter Width"); ed.Height = e.Float("Emitter Height"); ed.Depth = e.Float("Emitter Depth"); break;
                case "NiPSSphereEmitter": ed.Kind = EmitterKind.Sphere; ed.Radius = e.Float("Emitter Radius"); break;
                case "NiPSCylinderEmitter": ed.Kind = EmitterKind.Cylinder; ed.Radius = e.Float("Emitter Radius"); ed.Height = e.Float("Emitter Height"); break;
                case "NiPSMeshEmitter":
                {
                    ed.Kind = EmitterKind.Mesh;
                    foreach (var mb in e.Ints("Mesh Emitters"))
                    {
                        var mm = ModelData.MeshStreams(n, (int)mb);
                        if (mm.TryGetValue("POSITION", out var pos)) { ed.MeshPositions = pos.v.Select(x => (float)x).ToArray(); ed.MeshNode = NodeOfBlock.GetValueOrDefault((int)mb, -1); break; }
                    }
                    break;
                }
                default: ed.Kind = EmitterKind.Other; Unsupported.Add(e.Type); break;
            }
            d.Emitters.Add(ed);
        }
        return d;
    }
}
