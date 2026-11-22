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
    public int BillboardMode = -1;               // an NiBillboardNode's face mode (BillboardFace.ModeOf), -1 for other nodes
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

/// <summary>The colour terms a mesh's vertex colours stand in for (EffectMesh.VertexColors); Opacity: the vertex alpha replaces the material alpha.</summary>
public readonly record struct VertexColorUse(bool Emissive, bool Diffuse, bool Ambient, bool Opacity)
{
    public bool Any => Emissive || Diffuse || Ambient || Opacity;
}

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

    /// <summary>The NiVertexColorProperty lighting mode is LIGHTING_E (ModelMesh.LightingE).</summary>
    public bool LightingE => Mesh.LightingE;

    /// <summary>Which colour terms the mesh's vertex colours replace (ModelMesh.VertexColors).</summary>
    public VertexColorUse VertexColors => Mesh.VertexColors;

    /// <summary>The material's colour terms at effect time t, NiMaterialColorController keys applied (Target 0 ambient, 1
    /// diffuse, 3 emissive): lit = emissive + diffuse, amb = the ambient colour, which the scene's ambient light multiplies.
    /// The client's diffuse coefficient is emissive + matAmbient * lightAmbient + matDiffuse * lightDiffuse
    /// (NiStandardMaterial::HandleColorAccumulation -> HandleShadingCoefficients, NiStandardMaterial.cpp:3677-3740), and the
    /// base map's colour is multiplied by it. Without lights reaching the mesh the diffuse light is taken as full (white).
    /// A mesh without any colour gets nothing from emissive and diffuse: an additive mesh lit only by its ambient colour
    /// (portal_b's Box01: ambient blue, diffuse and emissive black) draws that colour, not white.
    /// Without an NiMaterialProperty: diffuse white, emissive and ambient black.
    /// LightingE drops the light's ambient and diffuse terms (NiStandardMaterial.cpp:3719-3723), so the coefficient is the emissive
    /// colour alone: lit = emissive, amb = 0. The opacity then comes from the emissive constant's alpha (:4437-4444), which
    /// is the material alpha like the diffuse constant's (D3D11ShaderConstantMap.cpp:3216-3229, 3264-3276), so Opacity holds.
    /// The coefficient is not saturated: ComputeShadingCoefficients clamps only with m_bSaturateShading, which is false by
    /// default and never set (SetSaturateShading has no callers), so lit may exceed 1.
    /// The terms the vertex colours replace (VertexColors) are left out of lit and amb here; the shader adds the vertex colour in
    /// their place (EffectInstance's mesh shader).
    /// STILL-OPEN: the light diffuse is taken as white; the client adds the directional light's colour * N.L per vertex
    /// (ms2standardmaterial.cpp:1640-1700), not ported.</summary>
    public void Shade(double t, double[] lit, double[] amb)
    {
        double[] dif = null, emi = null, am = null;
        foreach (var a in Colors)
        {
            var v = a.Curve(a.Ctl != null ? a.Ctl.Local(t) : t);
            if (a.Target == 0) am = v; else if (a.Target == 1) dif = v; else if (a.Target == 3) emi = v;
        }
        var m = Mesh;
        var vc = VertexColors;
        for (int i = 0; i < 3; i++)
        {
            double d = dif != null ? dif[i] : m.Diffuse != null ? m.Diffuse[i] : 1, e = emi != null ? emi[i] : m.Emissive != null ? m.Emissive[i] : 0;
            if (vc.Emissive) e = 0;
            if (vc.Diffuse) d = 0;
            if (LightingE) { lit[i] = e; amb[i] = 0; continue; }
            lit[i] = d + e;
            amb[i] = vc.Ambient ? 0 : am != null ? am[i] : m.Ambient != null ? m.Ambient[i] : 0;
        }
    }
}

public enum EmitterKind { Mesh, Box, Sphere, Cylinder, Other }

/// <summary>NiPSMeshEmitter::MeshEmissionType (NiPSMeshEmitter.h:58-75).</summary>
public enum MeshEmitFrom { Vertices, FaceCenter, EdgeCenter, FaceSurface, EdgeSurface }

/// <summary>NiPSMeshEmitter::InitialVelocityType (NiPSMeshEmitter.h:80-98).</summary>
public enum MeshVelocity { Normals, Random, Direction }

public sealed class EmitterDef
{
    public EmitterKind Kind; public string Name; public int Node = -1;
    public double Speed, SpeedVar, SpeedFlipRatio, Declination, DeclinationVar, PlanarAngle, PlanarAngleVar, Size, SizeVar, Lifespan, LifespanVar;
    public double RotAngle, RotAngleVar, RotSpeed, RotSpeedVar; public Vec3 RotAxis; public bool RandomRotSpeedSign, RandomRotAxis;
    public double Width, Height, Depth, Radius;
    public float[] MeshPositions;                // mesh emitter: vertices (xyz) in the mesh's own space
    public float[] MeshNormals;                  // mesh emitter: vertex normals (xyz), or null
    public int[] MeshTriangles;                  // mesh emitter: triangle list indices, or null
    public int MeshNode = -1;                    // node the mesh hangs from
    public MeshEmitFrom MeshEmission;            // NiPSMeshEmitter "Mesh Emission Type"
    public MeshVelocity MeshVelocityType;        // NiPSMeshEmitter "Initial Velocity Type"
    public Func<double, double> Rate; public TimeCtl RateCtl;           // births per second (NiPSEmitParticlesCtlr)
    public Func<double, double> On; public TimeCtl OnCtl;               // emitter active

    /// <summary>A mesh emitter's start point and, for <see cref="MeshVelocity.Normals"/>, its direction, both in the mesh's
    /// own space: NiPSMeshEmitter::EmitFromVertex / EmitFromFace / EmitFromEdge (NiPSMeshEmitter.cpp:187-440).
    /// - vertices: a random vertex and its normal;
    /// - faces: a random non-degenerate triangle (ComputeIndices, NiPSMeshEmitter.cpp:706-760, at most 6 steps past
    ///   degenerate ones) and the unit mean of its normals; face centre is the centroid, face surface the point
    ///   v0 + sqrt(r1) (r2 (v2 - v0) - (v1 - v0)) + (v1 - v0);
    /// - edges: one of the triangle's three edges (NiRand() % 3) and the unit mean of its two normals; edge centre is the
    ///   midpoint, edge surface v0 + (v1 - v0) r.
    /// A mesh without indices emits from vertices. The normal goes through ComputeDirection (NiPSMeshEmitter.inl:100-108):
    /// x += declination var * s1, z += planar angle var * s2 (s in [-1, 1]), then made unit. Returns false when the
    /// velocity type is not normals or the mesh has no normals (the client then uses the emitter direction,
    /// NiPSMeshEmitter.cpp:230-238). Draws from <paramref name="rng"/> only; allocates nothing.</summary>
    public bool SampleMesh(Random rng, out Vec3 pos, out Vec3 dir)
    {
        var p = MeshPositions; var nm = MeshNormals; var ix = MeshTriangles;
        int a, b = -1, c = -1;
        var from = MeshEmission;
        if (ix == null || ix.Length < 3) from = MeshEmitFrom.Vertices;
        if (from == MeshEmitFrom.Vertices)
        {
            int nv = p.Length / 3;
            a = Math.Min((int)(rng.NextDouble() * nv), nv - 1);
        }
        else
        {
            int nt = ix.Length / 3, tri = Math.Min((int)(rng.NextDouble() * nt), nt - 1);
            for (int scan = 0; scan < 6; scan++)
            {
                if (ix[tri * 3] != ix[tri * 3 + 1] && ix[tri * 3] != ix[tri * 3 + 2] && ix[tri * 3 + 1] != ix[tri * 3 + 2]) break;
                if (++tri >= nt) tri = 0;
            }
            a = ix[tri * 3]; b = ix[tri * 3 + 1]; c = ix[tri * 3 + 2];
            if (from is MeshEmitFrom.EdgeCenter or MeshEmitFrom.EdgeSurface)
            {
                switch (rng.Next(3))
                {
                    case 1: a = b; b = c; break;
                    case 2: b = c; break;
                }
                c = -1;
            }
        }
        Vec3 P(int k) => new(p[k * 3], p[k * 3 + 1], p[k * 3 + 2]);
        Vec3 v0 = P(a);
        switch (from)
        {
            case MeshEmitFrom.Vertices: pos = v0; break;
            case MeshEmitFrom.FaceCenter: pos = (v0 + P(b) + P(c)) * (1.0 / 3); break;
            case MeshEmitFrom.FaceSurface:
            {
                Vec3 d1 = P(b) - v0, d2 = P(c) - v0;
                double sq = Math.Sqrt(rng.NextDouble());
                pos = v0 + (d2 * rng.NextDouble() - d1) * sq + d1;
                break;
            }
            case MeshEmitFrom.EdgeCenter: pos = (v0 + P(b)) * 0.5; break;
            default: pos = v0 + (P(b) - v0) * rng.NextDouble(); break;
        }
        dir = default;
        if (MeshVelocityType != MeshVelocity.Normals || nm == null || nm.Length != p.Length) return false;
        Vec3 N(int k) => new(nm[k * 3], nm[k * 3 + 1], nm[k * 3 + 2]);
        var n = N(a);
        if (b >= 0) n += N(b);
        if (c >= 0) n += N(c);
        if (b >= 0) n = Unit(n);
        n = new Vec3(n.X + DeclinationVar * (2 * rng.NextDouble() - 1), n.Y, n.Z + PlanarAngleVar * (2 * rng.NextDouble() - 1));
        dir = Unit(n);
        return true;
    }

    static Vec3 Unit(Vec3 v)
    {
        double l = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return l > 0 ? v * (1 / l) : v;
    }

    /// <summary>A triangle list of <paramref name="count"/> indices from <paramref name="start"/> of the INDEX stream, or null
    /// when the range is outside the stream, is not whole triangles or holds an index at or past
    /// <paramref name="vertexCount"/>; a null list makes the emitter emit from vertices.</summary>
    public static int[] Triangles(double[] index, int start, int count, int vertexCount)
    {
        if (start < 0 || count < 3 || count % 3 != 0 || start > index.Length - count) return null;
        var t = new int[count];
        for (int i = 0; i < count; i++)
        {
            double x = index[start + i];
            if (!(x >= 0 && x < vertexCount)) return null;
            t[i] = (int)x;
        }
        return t;
    }

    /// <summary>The emitter-space direction for a declination and planar angle (variances already applied):
    /// NiPSEmitter::ComputeDirection (NiPSEmitter.inl:267-311). Declination 0 is +Z. A pre-RPI system (NiPSParticleSystem
    /// "Pre RPI") uses (sin dec cos plan, sin dec sin plan, cos dec); otherwise the RPI method (cos dec sin plan, -sin dec,
    /// cos dec cos plan).</summary>
    public static Vec3 Direction(double dec, double plan, bool preRpi)
    {
        if (dec == 0) return new Vec3(0, 0, 1);
        double sd = Math.Sin(dec), cd = Math.Cos(dec), sp = Math.Sin(plan), cp = Math.Cos(plan);
        return preRpi ? new Vec3(sd * cp, sd * sp, cd) : new Vec3(cd * sp, -sd, cd * cp);
    }
}

public enum ForceKind { Bomb, Gravity, Drag, Other }

public sealed class ForceDef
{
    public ForceKind Kind; public string Name, Type; public bool Active = true;
    public Vec3 Axis; public double Strength, Decay, DeltaV, Percentage; public int ForceType;
    /// <summary>NiPSBombForce: DecayType 0 none, 1 linear, 2 exponential; SymmType 0 spherical, 1 cylindrical, 2 planar
    /// (NiPSForceDefinitions.h:93-133). Node is the force's object: the "Gravity Object", the "Bomb Object" or the
    /// "Drag Object". A force without one does nothing: its Update returns false and the forces step leaves it out of the
    /// kernel's data (NiPSGravityForce.cpp:65-79, NiPSBombForce.cpp:63-66, NiPSDragForce.cpp:61-64,
    /// NiPSSimulatorForcesStep.cpp:186-226). A drag with Percentage &lt;= 0 is left out the same way.</summary>
    public int DecayType, SymmType, Node = -1;
    /// <summary>NiPSDragForce range: full drag inside Range, fading linearly to none at RangeFalloff.</summary>
    public double Range = float.MaxValue, RangeFalloff = float.MaxValue;
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
    public bool PreRpi = true;                   // "Pre RPI": the emitters' direction method (EmitterDef.Direction)
    public int Colliders, Spawners;
    /// <summary>The system's colour terms, read as an effect mesh's: its NiMaterialProperty and NiVertexColorProperty, and the
    /// NiMaterialColorControllers on that material (Material.Colors). The client draws the particle quads through the same
    /// NiStandardMaterial path as any mesh: the facing quad generator gives them a normal (camRight x camUp,
    /// NiPSFacingQuadGenerator.cpp:340-364) and, when the system has colours, a COLOR index 0 stream (:191-207), so the key
    /// colour stands in for the terms the vertex colour mode names (EffectMesh.VertexColors) and the material gives the rest
    /// (EffectMesh.Shade). Material.Mesh.Colors is an empty array when the system has colours ("Has Colors",
    /// NiPSParticleSystem::LoadBinary, NiPSParticleSystem.cpp:1227-1228), null without.</summary>
    public EffectMesh Material;
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

    /// <summary>An rgb curve from an NiPoint3Interpolator (NiPosData) or NiColorInterpolator (NiColorData); null without keys or a pose.
    /// The returned array belongs to the curve and is overwritten by its next call: read it before calling again.</summary>
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
        var r = new double[3];          // the interpolated value, reused by the next call (the caller reads it at once)
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
                for (int c = 0; c < 3; c++)
                    r[c] = it == 2 ? (2 * u * u * u - 3 * u * u + 1) * a.v[c] + (u * u * u - 2 * u * u + u) * a.b[c] + (-2 * u * u * u + 3 * u * u) * b.v[c] + (u * u * u - u * u) * b.f[c]
                                   : a.v[c] * (1 - u) + b.v[c] * u;        // TBC keys fall back to linear
                return r;
            }
            return keys[^1].v;
        };
    }

    static Vec3 V3(NifStruct s) => new(s.Float("x"), s.Float("y"), s.Float("z"));

    /// <summary>The first element and element count of submesh 0 in the NiMesh's stream holding
    /// <paramref name="semantic"/> (index 0): the stream ref's "Submesh To Region Map"[0] picks one of the stream's
    /// "Regions" (NiDataStreamRef::GetRegionForSubmesh). (0, 0) when the region is missing or outside the stream's
    /// <paramref name="total"/> elements. STILL-OPEN: a stream with no submesh map or no regions is taken whole.</summary>
    internal static (int start, int count) Submesh0(NifFile n, int mesh, string semantic, int total)
    {
        foreach (var r in n.Blocks[mesh].Structs("Datastreams"))
        {
            bool has = false;
            foreach (var cs in r.Structs("Component Semantics"))
                if (cs.IntOr("Index", 0) == 0 && n.StrField(cs, "Name") == semantic) { has = true; break; }
            if (!has) continue;
            var map = r.Ints("Submesh To Region Map");
            var regions = n.Blocks[(int)r.Int("Stream")].Structs("Regions");
            if (map.Length == 0 || regions.Length == 0) return (0, total);
            if (map[0] < 0 || map[0] >= regions.Length) return (0, 0);
            long start = regions[(int)map[0]].Int("Start Index"), count = regions[(int)map[0]].Int("Num Indices");
            return start >= 0 && count >= 0 && start + count <= total ? ((int)start, (int)count) : (0, 0);
        }
        return (0, 0);
    }

    public static EffectScene Load(NifFile n)
    {
        var B = n.Blocks;
        var sc = new EffectScene();
        var parent = new Dictionary<int, int>();
        for (int i = 0; i < B.Count; i++)
            if (B[i].TryGetValue("Children", out var c) && c is long[] kids)
                foreach (var k in kids) if (k >= 0) parent[(int)k] = i;

        // NiSortAdjustNode is an NiNode (NiSortAdjustNode.h:20): its own transform and transform controller move its children,
        // as any node's do; it only changes how the renderer sorts them (NifModel's NoSort).
        bool IsNode(string t) => t is "NiNode" or "NiSortAdjustNode" or "NiBillboardNode" or "NiMesh" or "NiPSParticleSystem" or "NiPSMeshParticleSystem";
        for (int i = 0; i < B.Count; i++)
        {
            if (!IsNode(B[i].Type)) continue;
            sc.NodeOfBlock[i] = sc.Nodes.Count;
            sc.Nodes.Add(new EffectNode
            {
                Block = i, Name = n.Name(i), Local = XformOf(B[i]), Billboard = B[i].Type == "NiBillboardNode",
                BillboardMode = B[i].Type == "NiBillboardNode" ? BillboardFace.ModeOf(B[i].IntOr("Billboard Mode", BillboardFace.RotateAboutUp)) : -1,
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
        var systems = new Dictionary<int, ParticleSystemDef>();
        foreach (var (blk, node) in sc.NodeOfBlock)
            if (B[blk].Type is "NiPSParticleSystem" or "NiPSMeshParticleSystem") systems[blk] = sc.ReadSystem(n, blk, node);

        // mesh blocks that reference a property block (texturing) so texture controllers can find their meshes, and the
        // particle systems' materials (ParticleSystemDef.Material), so a colour controller on a system's material reaches it
        IEnumerable<EffectMesh> MeshesWithProperty(int prop) => sc.Meshes.Concat(systems.Values.Select(s => s.Material))
            .Where(m => B[m.Mesh.Block].Ints("Properties").Contains(prop));

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
        var d = new ParticleSystemDef { Node = node, Name = n.Name(blk), MaxParticles = (int)Math.Clamp(b.IntOr("Max Num Particles", 100), 1, 2000), WorldSpace = b.IntOr("World Space", 0) != 0, PreRpi = b.IntOr("Pre RPI", 1) != 0, Spawners = (int)b.IntOr("Num Spawners", 0) };
        // The quads are drawn through an NiStandardMaterial (SoftParticlesMaterial, softparticlesmaterial.cpp:241-251), whose
        // HandlePreLightTextureApplication applies the base map and then the detail map at twice its colour
        // (NiStandardMaterial::HandleBaseMap / HandleDetailMap, NiStandardMaterial.cpp:4500-4527, 4765-4774, called at
        // :5517-5541), each through its own texture transform (GetTextureTransformMappingFromEnum, :364-430): so the system keeps its maps as a mesh does (ModelMesh.Maps).
        var mat = new ModelMesh { Block = blk, Name = d.Name, Colors = b.IntOr("Has Colors", 0) != 0 ? Array.Empty<float>() : null,
                                  Textures = ModelData.TexturesOf(n, blk), Maps = ModelData.MapsOf(n, blk) };
        d.Material = new EffectMesh { Mesh = mat, Node = node };

        foreach (var pi in b.Ints("Properties"))
        {
            if (pi < 0) continue;
            var p = B[(int)pi];
            if (p.Type == "NiAlphaProperty") d.AlphaFlags = (int)p.Int("Flags");
            else if (p.Type == "NiVertexColorProperty") mat.VertexColorFlags = (int)p.Int("Flags");
            else if (p.Type == "NiMaterialProperty")
            {
                float[] Rgb(string k) { var c = p.Struct(k); return new[] { c.Float("r"), c.Float("g"), c.Float("b") }; }
                mat.Ambient = Rgb("Ambient Color"); mat.Diffuse = Rgb("Diffuse Color"); mat.Emissive = Rgb("Emissive Color");
                mat.MaterialAlpha = p.Float("Alpha");
            }
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
                                case "NiPSGravityForce": fd.Kind = ForceKind.Gravity; fd.Axis = V3(f.Struct("Gravity Axis")); fd.Strength = f.Float("Strength"); fd.ForceType = (int)f.Int("Force Type"); fd.Decay = f.Float("Decay");
                                    long gob = f.IntOr("Gravity Object", -1);
                                    if (gob >= 0 && NodeOfBlock.TryGetValue((int)gob, out int gn)) fd.Node = gn;
                                    break;
                                case "NiPSDragForce":
                                    fd.Kind = ForceKind.Drag; fd.Axis = V3(f.Struct("Drag Axis")); fd.Percentage = f.Float("Percentage");
                                    fd.Range = f.Float("Range"); fd.RangeFalloff = f.Float("Range Falloff");
                                    long dob = f.IntOr("Drag Object", -1);
                                    if (dob >= 0 && NodeOfBlock.TryGetValue((int)dob, out int dn)) fd.Node = dn;
                                    break;
                                case "NiPSBombForce":
                                    fd.Kind = ForceKind.Bomb; fd.Axis = V3(f.Struct("Bomb Axis")); fd.DeltaV = f.Float("Delta V"); fd.Decay = f.Float("Decay");
                                    fd.DecayType = (int)f.IntOr("Decay Type", 0); fd.SymmType = (int)f.IntOr("Symmetry Type", 0);
                                    long bo = f.IntOr("Bomb Object", -1);
                                    if (bo >= 0 && NodeOfBlock.TryGetValue((int)bo, out int bn)) fd.Node = bn;
                                    break;
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
                        if (!mm.TryGetValue("POSITION", out var pos) || pos.n != 3) continue;
                        // The client locks each stream's submesh 0 region only (NiDataStreamElementLock::begin/count,
                        // GetRegionForSubmesh(0); ComputeIndices(kIndicesLock, 0, ...) NiPSMeshEmitter.cpp:281/356), and an
                        // index counts from that region's first vertex.
                        var (ps, pc) = Submesh0(n, (int)mb, "POSITION", pos.v.Length / 3);
                        if (pc <= 0) continue;
                        ed.MeshPositions = pos.v.Skip(ps * 3).Take(pc * 3).Select(x => (float)x).ToArray();
                        if (mm.TryGetValue("NORMAL", out var nrm) && nrm.n == 3)
                        {
                            var (ns, nc) = Submesh0(n, (int)mb, "NORMAL", nrm.v.Length / 3);
                            if (nc == pc) ed.MeshNormals = nrm.v.Skip(ns * 3).Take(nc * 3).Select(x => (float)x).ToArray();
                        }
                        // STILL-OPEN: a tristrip mesh (Primitive Type 1) emits from vertices here; the client takes its
                        // triangles through NiIndexedPrimitiveIterator.
                        if (mm.TryGetValue("INDEX", out var idx) && idx.n == 1 && n.Blocks[(int)mb].IntOr("Primitive Type", 0) == 0)
                        {
                            var (xs, xc) = Submesh0(n, (int)mb, "INDEX", idx.v.Length);
                            ed.MeshTriangles = EmitterDef.Triangles(idx.v, xs, xc, pc);
                        }
                        ed.MeshNode = NodeOfBlock.GetValueOrDefault((int)mb, -1);
                        break;
                    }
                    ed.MeshEmission = (MeshEmitFrom)e.IntOr("Mesh Emission Type", 0);
                    ed.MeshVelocityType = (MeshVelocity)e.IntOr("Initial Velocity Type", 0);
                    break;
                }
                default: ed.Kind = EmitterKind.Other; Unsupported.Add(e.Type); break;
            }
            d.Emitters.Add(ed);
        }
        return d;
    }
}
