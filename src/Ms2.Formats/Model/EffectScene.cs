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

/// <summary>
/// A mesh's NiMorphMeshModifier under its NiMorphWeightsController (ModelData.ReadMorph reads the targets): target 0 plus
/// each offset target weighted at the controller's time, the weights under 0.001 left out (NiMorphingKernel.cpp:30-40). A
/// target whose interpolator has keys follows them (NiMorphWeightsController::Update, the same curve as the effect's other
/// float controllers); the others keep their fixed weight (MorphAnim.Static).
/// </summary>
public sealed class EffectMorph
{
    public MorphAnim Anim;                 // the targets (Deltas, Base) and the fixed weights
    public Func<double, double>[] Weights; // per offset target: its keyed curve, or null for the fixed weight
    public TimeCtl Ctl;

    /// <summary>The offset targets' weights at effect time t into <paramref name="w"/> (its length at most the targets').</summary>
    public void WeightsAt(double t, Span<float> w)
    {
        double lt = Ctl.Local(t);
        for (int i = 0; i < w.Length; i++)
        {
            float v = Weights[i] != null ? (float)Weights[i](lt) : Anim.Static[i];
            w[i] = Math.Abs(v) < 0.001f || !float.IsFinite(v) ? 0f : v;
        }
    }
}

public sealed class EffectMesh
{
    public List<ColorAnim> Colors = new();
    public ModelMesh Mesh; public int Node;
    public Func<double, double> Alpha; public TimeCtl AlphaCtl;
    public List<UvOp> Uv = new();
    public FlipAnim Flip;
    public EffectMorph Morph;

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

/// <summary>An animated float: the curve read at the controller's time.</summary>
public sealed class EmitterAnim
{
    public Func<double, double> Curve; public TimeCtl Ctl;
    public double At(double t) => Curve(Ctl.Local(t));
}

/// <summary>The emitter values an NiPSEmitterFloatCtlr can drive, in <see cref="EmitterDef.Anims"/>'s order.</summary>
public enum EmitterField { Speed, SpeedVar, SpeedFlipRatio, Declination, DeclinationVar, PlanarAngle, PlanarAngleVar, Size, SizeVar, Lifespan, LifespanVar, RotAngle, RotAngleVar, RotSpeed, RotSpeedVar }

/// <summary>An emitter's values at one time (<see cref="EmitterDef.At"/>).</summary>
public struct EmitterValues
{
    public double Speed, SpeedVar, SpeedFlipRatio, Declination, DeclinationVar, PlanarAngle, PlanarAngleVar, Size, SizeVar, Lifespan, LifespanVar;
    public double RotAngle, RotAngleVar, RotSpeed, RotSpeedVar;
}

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
    /// <summary>The emitter's float controllers by <see cref="EmitterField"/>, or null when it has none. Each NiPSEmitterFloatCtlr
    /// stores its interpolator's value at its scaled time into one emitter value through a plain setter (SetTargetFloatValue,
    /// e.g. NiPSEmitterSpeedCtlr.cpp:40 SetSpeed, NiPSEmitter.inl:33), so the emitter emits with the animated value.
    /// The Radius controllers drive the particle size (NiPSEmitterRadiusCtlr.cpp:40 SetSize, RadiusVar SetSizeVar), not a volume radius.</summary>
    public EmitterAnim[] Anims;

    /// <summary>The values at effect time t: the stored ones, each with a controller replaced by its value. Allocates nothing.</summary>
    public EmitterValues At(double t)
    {
        var v = new EmitterValues
        {
            Speed = Speed, SpeedVar = SpeedVar, SpeedFlipRatio = SpeedFlipRatio, Declination = Declination, DeclinationVar = DeclinationVar,
            PlanarAngle = PlanarAngle, PlanarAngleVar = PlanarAngleVar, Size = Size, SizeVar = SizeVar, Lifespan = Lifespan, LifespanVar = LifespanVar,
            RotAngle = RotAngle, RotAngleVar = RotAngleVar, RotSpeed = RotSpeed, RotSpeedVar = RotSpeedVar,
        };
        var a = Anims;
        if (a == null) return v;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == null) continue;
            double x = a[i].At(t);
            switch ((EmitterField)i)
            {
                case EmitterField.Speed: v.Speed = x; break;
                case EmitterField.SpeedVar: v.SpeedVar = x; break;
                case EmitterField.SpeedFlipRatio: v.SpeedFlipRatio = x; break;
                case EmitterField.Declination: v.Declination = x; break;
                case EmitterField.DeclinationVar: v.DeclinationVar = x; break;
                case EmitterField.PlanarAngle: v.PlanarAngle = x; break;
                case EmitterField.PlanarAngleVar: v.PlanarAngleVar = x; break;
                case EmitterField.Size: v.Size = x; break;
                case EmitterField.SizeVar: v.SizeVar = x; break;
                case EmitterField.Lifespan: v.Lifespan = x; break;
                case EmitterField.LifespanVar: v.LifespanVar = x; break;
                case EmitterField.RotAngle: v.RotAngle = x; break;
                case EmitterField.RotAngleVar: v.RotAngleVar = x; break;
                case EmitterField.RotSpeed: v.RotSpeed = x; break;
                default: v.RotSpeedVar = x; break;
            }
        }
        return v;
    }

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
    public bool SampleMesh(Random rng, out Vec3 pos, out Vec3 dir) => SampleMesh(rng, DeclinationVar, PlanarAngleVar, out pos, out dir);

    /// <summary><see cref="SampleMesh(Random, out Vec3, out Vec3)"/> with the declination and planar angle variances of one time (<see cref="At"/>).</summary>
    public bool SampleMesh(Random rng, double declinationVar, double planarAngleVar, out Vec3 pos, out Vec3 dir)
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
        n = new Vec3(n.X + declinationVar * (2 * rng.NextDouble() - 1), n.Y, n.Z + planarAngleVar * (2 * rng.NextDouble() - 1));
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

    /// <summary>The triangles a mesh emitter picks from on a strip mesh (Primitive Type 1): <paramref name="count"/> indices
    /// from <paramref name="start"/> are one strip of count - 2 triangles. NiPSMeshEmitter::ComputeIndices (NiPSMeshEmitter.cpp:
    /// 713-760) reads triangle t as kIndicesIter[t][0..2], through the index iterator NiDataStreamPrimitiveLock::BeginIndexed16/32
    /// makes. For a strip that iterator steps 1 index, takes 3, and reverses odd windings (CreateIndexedIteratorHelper,
    /// NiDataStreamPrimitiveLock.inl:35-39); the random-access operator[] marks triangle t odd by t &amp; 1 and the primitive's
    /// operator[] then returns its first two swapped (NiTPrimitiveRandomAccessIterator.h:79-93, 269-285). So triangle t is
    /// (s[t], s[t+1], s[t+2]) on even t and (s[t+1], s[t], s[t+2]) on odd t; the degenerate ones stay, since the pick counts
    /// them and then steps past them
    /// (<see cref="SampleMesh"/>). Null as <see cref="Triangles"/>: a range outside the stream, under 3 indices, or an index
    /// at or past <paramref name="vertexCount"/>.</summary>
    public static int[] StripTriangles(double[] index, int start, int count, int vertexCount)
    {
        if (start < 0 || count < 3 || start > index.Length - count) return null;
        var t = new int[(count - 2) * 3];
        for (int i = 0; i < count; i++)
        {
            double x = index[start + i];
            if (!(x >= 0 && x < vertexCount)) return null;
        }
        for (int q = 0; q < count - 2; q++)
        {
            int odd = q & 1;
            t[q * 3] = (int)index[start + q + odd];
            t[q * 3 + 1] = (int)index[start + q + 1 - odd];
            t[q * 3 + 2] = (int)index[start + q + 2];
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
    /// <summary>A gravity force's NiPSGravityStrengthCtlr, or null: it stores its value as the strength (SetTargetFloatValue,
    /// NiPSGravityStrengthCtlr.cpp:72, NiPSGravityForce::SetStrength).</summary>
    public EmitterAnim StrengthAnim;
    public double StrengthAt(double t) => StrengthAnim != null ? StrengthAnim.At(t) : Strength;
}

public sealed class ParticleSystemDef
{
    public int Node; public string Name;
    public List<EmitterDef> Emitters = new();
    public List<ForceDef> Forces = new();
    public (double t, double v)[] SizeKeys = Array.Empty<(double, double)>();
    public (double t, byte r, byte g, byte b, byte a)[] ColorKeys = Array.Empty<(double, byte, byte, byte, byte)>();
    public double GrowTime, ShrinkTime; public int GrowGeneration, ShrinkGeneration;
    public bool Aligned;                         // NiPSAlignedQuadGenerator (AlignedQuad) vs NiPSFacingQuadGenerator
    public double ScaleAmountU, ScaleLimitU, ScaleRestU, ScaleAmountV, ScaleLimitV, ScaleRestV, CenterU, CenterV;
    /// <summary>The system's quad alignment (AlignedQuad): "Normal Method" / "Up Method" (AlignMethod, ALIGN_CAMERA when
    /// absent) and their directions, read by NiPSParticleSystem::LoadBinary; HasRotations: "Has Rotations", the system
    /// keeps rotation angles (without them the kernels take no rotation).</summary>
    public int NormalMethod = AlignedQuad.Camera, UpMethod = AlignedQuad.Camera; public Vec3 NormalDirection, UpDirection;
    public bool HasRotations = true;
    /// <summary>The aligned quads' texture animation (AlignedQuad.TexAnim): the generator's flipbook fields, used only when the
    /// system has "Has Animated Textures" (NiPSAlignedQuadGenerator.cpp:325-337); without it every quad shows the whole texture.</summary>
    public bool AnimatedTextures, UVScrolling, PingPong;
    public int FramesAcross = 1, FramesDown = 1, NumFrames, InitialFrame; public double InitialFrameVar, NumFramesVar, InitialTime, FinalTime = 1;
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
            if (t.StartsWith("NiPS", StringComparison.Ordinal) && !KnownPs.Contains(t) && !EmitterFloatCtlrs.ContainsKey(t)) sc.Unsupported.Add(t);
        return sc;
    }

    static readonly HashSet<string> KnownPs = new()
    {
        "NiPSParticleSystem", "NiPSSimulator", "NiPSSimulatorGeneralStep", "NiPSSimulatorForcesStep", "NiPSSimulatorCollidersStep", "NiPSSimulatorFinalStep",
        "NiPSSimulatorMeshAlignStep", "NiPSMeshEmitter", "NiPSBoxEmitter", "NiPSSphereEmitter", "NiPSCylinderEmitter", "NiPSAlignedQuadGenerator",
        "NiPSFacingQuadGenerator", "NiPSBoundUpdater", "NiPSBombForce", "NiPSGravityForce", "NiPSDragForce", "NiPSEmitParticlesCtlr", "NiPSForceActiveCtlr",
        "NiPSGravityStrengthCtlr",
    };

    /// <summary>The NiPSEmitterFloatCtlr types and the emitter value each sets (each one's SetTargetFloatValue, line 40 of its .cpp).</summary>
    public static readonly Dictionary<string, EmitterField> EmitterFloatCtlrs = new()
    {
        ["NiPSEmitterSpeedCtlr"] = EmitterField.Speed, ["NiPSEmitterSpeedVarCtlr"] = EmitterField.SpeedVar, ["NiPSEmitterSpeedFlipRatioCtlr"] = EmitterField.SpeedFlipRatio,
        ["NiPSEmitterDeclinationCtlr"] = EmitterField.Declination, ["NiPSEmitterDeclinationVarCtlr"] = EmitterField.DeclinationVar,
        ["NiPSEmitterPlanarAngleCtlr"] = EmitterField.PlanarAngle, ["NiPSEmitterPlanarAngleVarCtlr"] = EmitterField.PlanarAngleVar,
        ["NiPSEmitterRadiusCtlr"] = EmitterField.Size, ["NiPSEmitterRadiusVarCtlr"] = EmitterField.SizeVar,
        ["NiPSEmitterLifeSpanCtlr"] = EmitterField.Lifespan, ["NiPSEmitterLifeSpanVarCtlr"] = EmitterField.LifespanVar,
        ["NiPSEmitterRotAngleCtlr"] = EmitterField.RotAngle, ["NiPSEmitterRotAngleVarCtlr"] = EmitterField.RotAngleVar,
        ["NiPSEmitterRotSpeedCtlr"] = EmitterField.RotSpeed, ["NiPSEmitterRotSpeedVarCtlr"] = EmitterField.RotSpeedVar,
    };

    /// <summary>A float interpolator's curve, or null when it gives no value: no curve, or no keys and the INVALID_FLOAT pose
    /// (-FLT_MAX, NiInterpolator.cpp:27), for which NiFloatInterpolator::Update returns false and the controller sets nothing
    /// (NiFloatInterpolator.cpp:185-209).</summary>
    static Func<double, double> ValueCurve(NifFile n, long interp)
    {
        var f = Curve(n, interp);
        if (f == null) return null;
        var e = n.Blocks[(int)interp];
        long di = e.IntOr("Data", -1);
        bool keys = di >= 0 && n.Blocks[(int)di].TryGetValue("Data", out var g) && g is NifStruct grp && grp.IntOr("Num Keys", 0) > 0;
        if (!keys && !(e.TryGetValue("Value", out var v) && v is float pose && pose > -float.MaxValue)) return null;
        return f;
    }

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
            case "NiMorphWeightsController":
                // ReadMorph read the first running controller of the mesh (ModelMesh.AnyMorph); a mesh without one keeps
                // its stored or baked positions
                if (meshOf.TryGetValue((int)target, out var mm) && mm.Morph == null && mm.Mesh.AnyMorph is { } ma && ctl.Active)
                {
                    var ips = c.Ints("Interpolators");
                    var w = new Func<double, double>[ma.Deltas.Length];
                    for (int t = 0; t < w.Length; t++)
                        if (ma.Weights[t] != null && t + 1 < ips.Length) w[t] = Curve(n, ips[t + 1]);
                    mm.Morph = new EffectMorph { Anim = ma, Weights = w, Ctl = ctl };
                }
                break;
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
            case "NiPSGravityStrengthCtlr":
                // NiPSForceCtlr finds its force by name (GetForceByName); an inactive controller does not update
                // (NiTimeController::DontDoUpdate, NiTimeController.inl:152), so the force keeps its stored strength
                if (systems.TryGetValue((int)target, out var ps3) && ctl.Active && ValueCurve(n, interp) is { } gs)
                {
                    string fn = n.Str(c.IntOr("Force Name", -1));
                    foreach (var f in ps3.Forces.Where(f => f.Name == fn && f.Kind == ForceKind.Gravity)) f.StrengthAnim = new EmitterAnim { Curve = gs, Ctl = ctl };
                }
                break;
            default:
                if (EmitterFloatCtlrs.TryGetValue(c.Type, out var field))
                {
                    // NiPSEmitterCtlr finds its emitter by name (NiPSEmitterCtlr.cpp:67, GetEmitterByName); an inactive
                    // controller does not update (NiPSEmitterFloatCtlr::Update, NiPSEmitterFloatCtlr.cpp:87-109)
                    if (systems.TryGetValue((int)target, out var ps4) && ctl.Active && ValueCurve(n, interp) is { } ef)
                    {
                        string en = n.Str(c.IntOr("Emitter Name", -1));
                        foreach (var e in ps4.Emitters.Where(e => e.Name == en))
                        {
                            e.Anims ??= new EmitterAnim[Enum.GetValues<EmitterField>().Length];
                            e.Anims[(int)field] = new EmitterAnim { Curve = ef, Ctl = ctl };
                        }
                    }
                    break;
                }
                if (c.Type.StartsWith("Ni", StringComparison.Ordinal)) Unsupported.Add(c.Type);
                break;
        }
    }

    ParticleSystemDef ReadSystem(NifFile n, int blk, int node)
    {
        var B = n.Blocks; var b = B[blk];
        var d = new ParticleSystemDef { Node = node, Name = n.Name(blk), MaxParticles = (int)Math.Clamp(b.IntOr("Max Num Particles", 100), 1, 2000), WorldSpace = b.IntOr("World Space", 0) != 0, PreRpi = b.IntOr("Pre RPI", 1) != 0, Spawners = (int)b.IntOr("Num Spawners", 0) };
        d.HasRotations = b.IntOr("Has Rotations", 1) != 0;
        d.AnimatedTextures = b.IntOr("Has Animated Textures", 0) != 0;
        d.NormalMethod = (int)b.IntOr("Normal Method", AlignedQuad.Camera); d.UpMethod = (int)b.IntOr("Up Method", AlignedQuad.Camera);
        if (b.Has("Normal Direction")) d.NormalDirection = V3(b.Struct("Normal Direction"));
        if (b.Has("Up Direction")) d.UpDirection = V3(b.Struct("Up Direction"));
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

        // The quad generator is one of the mesh's modifiers (NiMesh "Modifiers", beside the NiPSSimulator); the system's own
        // "Generator" is its NiPSBoundUpdater (NiPSParticleSystem::LoadBinary).
        foreach (var gi in b.Ints("Modifiers"))
        {
            if (gi < 0 || B[(int)gi].Type != "NiPSAlignedQuadGenerator") continue;
            var g = B[(int)gi];
            d.Aligned = true;
            double F(string k, double def) => g.TryGetValue(k, out var v) && v is float f ? f : def;
            d.ScaleAmountU = F("Scale Amount U", 0); d.ScaleLimitU = F("Scale Limit U", 10000); d.ScaleRestU = F("Scale Rest U", 1);
            d.ScaleAmountV = F("Scale Amount V", 0); d.ScaleLimitV = F("Scale Limit V", 10000); d.ScaleRestV = F("Scale Rest V", 1);
            d.CenterU = F("Center U", 0); d.CenterV = F("Center V", 0);
            d.UVScrolling = g.IntOr("UV Scrolling", 0) != 0; d.PingPong = g.IntOr("Ping Pong", 0) != 0;
            d.FramesAcross = (int)g.IntOr("Num Frames Across", 1); d.FramesDown = (int)g.IntOr("Num Frames Down", 1);
            d.NumFrames = (int)g.IntOr("Num Frames", 0); d.InitialFrame = (int)g.IntOr("Initial Frame", 0);
            d.InitialFrameVar = F("Initial Frame Variation", 0); d.NumFramesVar = F("Num Frames Variation", 0);
            d.InitialTime = F("Initial Time", 0); d.FinalTime = F("Final Time", 1);
            break;
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
                        // a triangle list, or a strip (Primitive Type 1) read as the client's index iterator reads it
                        long primitive = n.Blocks[(int)mb].IntOr("Primitive Type", 0);
                        if (mm.TryGetValue("INDEX", out var idx) && idx.n == 1 && (primitive == 0 || primitive == 1))
                        {
                            var (xs, xc) = Submesh0(n, (int)mb, "INDEX", idx.v.Length);
                            ed.MeshTriangles = primitive == 0 ? EmitterDef.Triangles(idx.v, xs, xc, pc) : EmitterDef.StripTriangles(idx.v, xs, xc, pc);
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
