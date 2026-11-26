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
    /// <summary>An NiVisController on the node: at each update the node is culled where the track is false
    /// (SetAppCulled(!bValue), NiVisController::Update, NiVisController.cpp:42-61); a culled node hides its subtree.</summary>
    public BoolTrack Vis; public TimeCtl VisCtl;
    public KfChannel Anim; public TimeCtl AnimCtl;
}

/// <summary>
/// An NiBoolInterpolator's value (NiBoolInterpolator::Update, NiBoolInterpolator.cpp:185-218): the keys of its NiBoolData,
/// or without keys the pose value. The keys are step keys (NiStepBoolKey::Interpolate, NiStepBoolKey.cpp:40-49): between
/// two keys the earlier one's value, the later one's from its time on (NiBoolKey::GenInterp, NiBoolKey.cpp:43-88, searches
/// the first key at or after the time); before the first key the first one's value, after the last the last one's.
/// </summary>
public sealed class BoolTrack
{
    public double[] Times = Array.Empty<double>(); public bool[] Values = Array.Empty<bool>();
    public bool Pose;
    /// <summary>The keys' interpolation type (NiAnimationKey::KeyType; 5 STEPKEY), 0 without keys.</summary>
    public int KeyType;

    public bool At(double t)
    {
        int n = Times.Length;
        if (n == 0) return Pose;
        if (n == 1) return Values[0];
        for (int j = 1; j < n; j++)
            if (t <= Times[j]) return t < Times[j] ? Values[j - 1] : Values[j];
        return Values[n - 1];
    }

    /// <summary>The track of NiBoolInterpolator block <paramref name="interp"/>, null when it gives no value: no keys and the
    /// INVALID_BOOL pose (2, NiInterpolator.cpp:26), for which Update returns false and the controller sets nothing.</summary>
    public static BoolTrack Of(NifFile n, long interp)
    {
        if (interp < 0 || interp >= n.Blocks.Count) return null;
        var e = n.Blocks[(int)interp];
        if (e.Type != "NiBoolInterpolator") return null;
        static long Num(object o) => o switch { bool b => b ? 1 : 0, null => 2, _ => Convert.ToInt64(o) };
        long pose = e.TryGetValue("Value", out var pv) ? Num(pv) : 2;
        var tr = new BoolTrack { Pose = pose == 1 };
        long di = e.IntOr("Data", -1);
        if (di >= 0 && n.Blocks[(int)di].TryGetValue("Data", out var g) && g is NifStruct grp && grp.IntOr("Num Keys", 0) > 0)
        {
            var keys = grp.Structs("Keys").ToList();
            tr.Times = keys.Select(k => (double)k.Float("Time")).ToArray();
            tr.Values = keys.Select(k => Num(k["Value"]) != 0).ToArray();
            tr.KeyType = (int)grp.IntOr("Interpolation", 0);
            return tr;
        }
        return pose == 2 ? null : tr;
    }
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
    /// <summary>The mesh is in a mesh-particle system's master particle (ParticleSystemDef.MasterMeshes): drawn only as the
    /// particles' clones.</summary>
    public bool Master;

    /// <summary>The material alpha at effect time t: an NiAlphaController's value replaces the NiMaterialProperty alpha (its
    /// m_fAlpha, the controller's target), else the static alpha read from the file (1 without a material: NiMaterialProperty's
    /// default). The client's opacity is this times the base map's alpha (NiStandardMaterial HandleColorAccumulation and
    /// CompositeFinalRGBAColor; ms2standardmaterial.cpp:758-791).</summary>
    public double Opacity(double t) => Alpha != null ? Alpha(AlphaCtl.Local(t)) : Mesh.MaterialAlpha;

    /// <summary>The NiVertexColorProperty lighting mode is LIGHTING_E (ModelMesh.LightingE).</summary>
    public bool LightingE => Mesh.LightingE;

    /// <summary>Which colour terms the mesh's vertex colours replace (ModelMesh.VertexColors).</summary>
    public VertexColorUse VertexColors => Mesh.VertexColors;

    /// <summary>The client swapped the block's material for MS2AlphaBoostMaterial when the effect loaded
    /// (ModelData.AlphaBoost: a material alpha or an alpha key above 1), whatever material it names.</summary>
    public bool AlphaBoost;

    /// <summary>The material the client draws the block with: MS2AlphaBoostMaterial under <see cref="AlphaBoost"/>, else the
    /// name the block stores (ModelMesh.Material), or null for none.</summary>
    public string ActiveMaterial => AlphaBoost ? "MS2AlphaBoostMaterial" : Mesh?.Material;

    /// <summary>The block draws through one of the two shipped effect files' techniques (HoTechnique), not a standard
    /// material: it names one and is not swapped for MS2AlphaBoostMaterial.</summary>
    public HoTechnique Ho => AlphaBoost ? null : HoTechnique.Of(Mesh?.Material);

    /// <summary>Under an _HO technique (<see cref="Ho"/>), the texture the pass reads for a map it samples that the block
    /// lacks (slot to file, null when none): the client sets no texture for it (NiD3DXEffectParameter::SetParameter returns
    /// ENTRYNOTFOUND without SetTexture, NiD3DXEffectParameter.cpp:862-881), and every technique of an effect file shares the
    /// file's one ID3DXEffect (NiD3DXEffectTechnique.cpp:66, GetRenderableEffect), so the pass reads the texture the last
    /// block drawn through that file set for the map. The map's texture transform is the identity (the block has none:
    /// NiD3DXEffectParameter.cpp:1755-1812). Taken here as the effect's own other drawn _HO blocks of the same file: when they
    /// carry that map with exactly one texture, it is lent (EffectScene.LendMissingMaps); else none, and the pass reads
    /// (0, 0, 0, 1), a NULL texture's value. With several, the client's pick depends on the draw order and is not known; in
    /// the data every Mask block that lacks its detail map has one candidate (lent) or none (HoMaterialTests census).
    /// STILL-OPEN: in the client the map also depends on the frame's draw order and on the other effects on screen.</summary>
    public Dictionary<string, string> Lent;

    /// <summary>The detail map's alpha multiplies the opacity: the active material is one the client builds as an
    /// MS2StandardMaterial (ModelData.IsStandardMaterial; NiRenderObject::LoadBinary builds the named material,
    /// NiRenderObject.cpp:385-399, :77-86), whose HandleDetailMap samples the detail alpha into m_pkDetailOpacity
    /// (ms2standardmaterial.cpp:681-694) and multiplies the opacity by it (:342-343). A block that names none is given the
    /// renderer's default material, an NiStandardMaterial (NiRenderObject.cpp:420-424; NiRenderer.cpp:49-54, nothing in the
    /// client calls SetDefaultMaterial), which takes the detail colour only (NiStandardMaterial::HandleDetailMap,
    /// NiStandardMaterial.cpp:4765-4774), as does SoftParticlesMaterial. The EffectUVDistortion*_HO and EffectDissolve*_HO
    /// names draw through their effect file's technique (<see cref="Ho"/>), which reads the detail map's red channel as a
    /// mask and not through this.</summary>
    public bool DetailOpacity => ModelData.IsStandardMaterial(ActiveMaterial);

    /// <summary>The block draws its glow mask into the glow target: it has a glow map and does not blend, or blends SRCALPHA /
    /// INVSRCALPHA (NiAlphaProperty 6 / 7), the client's glow-click rule for a mesh (FUN_1414cffc0,
    /// ms2materialswapprocessor.cpp:158-170; FUN_141567260, unit_141567000.cpp:158-168), as the field's (ModelBuilder.GlowMaterial).
    /// STILL-OPEN: a block drawn through an _HO technique (<see cref="Ho"/>) is left out here.</summary>
    public bool GlowMask => Ho == null && Mesh != null && Mesh.Textures.ContainsKey("glow")
        && (Mesh.AlphaFlags < 0 || (Mesh.AlphaFlags & 1) == 0 || (((Mesh.AlphaFlags >> 1) & 15) == 6 && ((Mesh.AlphaFlags >> 5) & 15) == 7));

    /// <summary>The material's emissive and specular colours at effect time t (NiMaterialColorController keys applied,
    /// Target 3 emissive, 2 specular; else the NiMaterialProperty's): the MATERIALEMISSIVE and MATERIALSPECULAR constants
    /// a technique of <see cref="Ho"/> reads (NiD3DShaderConstantMap.cpp:2165-2201). Their alpha is the material alpha,
    /// <see cref="Opacity"/>.</summary>
    public void HoColors(double t, double[] emi, double[] spec)
    {
        double[] e = null, s = null;
        foreach (var a in Colors)
        {
            if (a.Target != 2 && a.Target != 3) continue;
            var v = a.Curve(a.Ctl != null ? a.Ctl.Local(t) : t);
            if (a.Target == 3) e = v; else s = v;
        }
        var m = Mesh;
        for (int i = 0; i < 3; i++)
        {
            emi[i] = e != null ? e[i] : m.Emissive != null ? m.Emissive[i] : 0;
            spec[i] = s != null ? s[i] : m.Specular != null ? m.Specular[i] : 0;
        }
    }

    /// <summary>The material's colour terms at effect time t, NiMaterialColorController keys applied (Target 0 ambient, 1
    /// diffuse, 3 emissive): emi the emissive, dif the diffuse and amb the ambient colour.
    /// The client's diffuse coefficient is emissive + matAmbient * ambientAccum + matDiffuse * diffuseAccum
    /// (ComputeShadingCoefficients; NiStandardMaterial::HandleColorAccumulation, NiStandardMaterial.cpp:3677-3740), and the
    /// base map's colour is multiplied by it. The accumulators are the lights' (<see cref="LightForm"/>), which the shader
    /// adds per vertex, as the client's vertex lighting does: an effect mesh has no shadow, normal or parallax map path that
    /// would force per-pixel lighting (NiStandardMaterial.cpp:640-675).
    /// A mesh without any colour gets nothing from emissive and diffuse: an additive mesh lit only by its ambient colour
    /// (portal_b's Box01: ambient blue, diffuse and emissive black) draws that colour, not white.
    /// Without an NiMaterialProperty: diffuse white, emissive and ambient black.
    /// LightingE drops the light's ambient and diffuse terms (NiStandardMaterial.cpp:3719-3723), so the coefficient is the emissive
    /// colour alone: dif = amb = 0. The opacity then comes from the emissive constant's alpha (:4437-4444), which
    /// is the material alpha like the diffuse constant's (D3D11ShaderConstantMap.cpp:3216-3229, 3264-3276), so Opacity holds.
    /// The coefficient is not saturated: ComputeShadingCoefficients clamps only with m_bSaturateShading, which is false by
    /// default and never set (SetSaturateShading has no callers), so it may exceed 1.
    /// The terms the vertex colours replace (VertexColors) are left out here; the shader adds the vertex colour in
    /// their place (EffectInstance's mesh shader).</summary>
    public void Shade(double t, double[] emi, double[] dif, double[] amb)
    {
        double[] di = null, em = null, am = null;
        foreach (var a in Colors)
        {
            var v = a.Curve(a.Ctl != null ? a.Ctl.Local(t) : t);
            if (a.Target == 0) am = v; else if (a.Target == 1) di = v; else if (a.Target == 3) em = v;
        }
        var m = Mesh;
        var vc = VertexColors;
        for (int i = 0; i < 3; i++)
        {
            double d = di != null ? di[i] : m.Diffuse != null ? m.Diffuse[i] : 1, e = em != null ? em[i] : m.Emissive != null ? m.Emissive[i] : 0;
            if (vc.Emissive) e = 0;
            if (vc.Diffuse) d = 0;
            emi[i] = e;
            if (LightingE) { dif[i] = 0; amb[i] = 0; continue; }
            dif[i] = d;
            amb[i] = vc.Ambient ? 0 : am != null ? am[i] : m.Ambient != null ? m.Ambient[i] : 0;
        }
    }

    /// <summary>How the field's lights shade the block: 0 not at all (LightingE: the emissive alone), 1 through Gamebryo's
    /// "Light" fragment, 2 through MS2's "MS2Light".
    /// The lights reach an effect: CEffectService attaches every light entity (FlatModel 0x2c4767f5: the field's ambient
    /// and directional lights) to each effect root it keeps (AttachEntityLight, effectservice.cpp:1780-1810), and a skill
    /// effect's EffectNode passes them down with useLighting, true by default (LightUnaffectedNode::UpdateEffectsDownward,
    /// lightunaffectednode.cpp:75; nifeffectparam.cpp:353). The ambient light is the ambient accumulator's start
    /// (SCM_DEF_AMBIENTLIGHT, ms2standardmaterial.cpp:1675-1685). For the directional light, with N.L = max(0, N . -dir):
    /// 1, the renderer's default NiStandardMaterial (a block that names no material, or a non-MS2 one; ModelData.IsStandardMaterial):
    /// diffuseAccum += sun * N.L and ambientAccum += its ambient colour (the Light fragment, StandardMaterialNodeLibraryFragments.xml);
    /// 2, an MS2StandardMaterial of type 0 (MS2StandardMaterial and MS2AlphaBoostMaterial, MS2StandardMaterial::Init,
    /// ms2standardmaterial.cpp:1816-1850; HandleMS2Light :852-875): diffuseAccum += sun * N.L + its ambient colour * (1 - N.L),
    /// the ambient accumulator untouched (MS2Light, ms2materialnodelibrary.cpp:316-328).
    /// Without normals the directional lights are dropped (NiStandardMaterial.cpp:668-673) and the ambient light alone stays.
    /// STILL-OPEN: the character types (MS2CharacterMaterial and the Chaos names) use MS2CharacterLight's half-Lambert
    /// (ms2materialnodelibrary.cpp:1084-1110) and are drawn here as 2; the field's point lights are not added.</summary>
    public int LightForm => LightingE ? 0 : ModelData.IsStandardMaterial(ActiveMaterial) ? 2 : 1;

    /// <summary>The light form a block of an effect part draws with: <paramref name="form"/> (<see cref="LightForm"/>) when the
    /// part's root passes the field lights down, 0 when its &lt;attachInfo useLighting&gt; is false (EffectPart.UseLighting):
    /// the EffectNode stops the lights (lightunaffectednode.cpp), and with no light in the effect state both accumulators stay
    /// 0 (NiD3DShaderConstantMap.cpp:2334-2360), so the block draws its emissive alone as LightingE does.</summary>
    public static int PartLightForm(int form, bool useLighting) => useLighting ? form : 0;

    /// <summary>The ambient and diffuse accumulators of light form <paramref name="form"/> (<see cref="LightForm"/>) for one
    /// channel: the field's ambient light, the directional light's colour and ambient colour, and ndl = max(0, N . -dir).
    /// The effect shader's light_vert is this, per vertex.</summary>
    public static (double Amb, double Dif) Light(int form, bool normals, double ndl, double ambient, double sun, double sunAmbient)
    {
        if (form == 0) return (0, 0);
        if (!normals) return (ambient, 0);
        ndl = Math.Max(ndl, 0);
        return form == 1 ? (ambient + sunAmbient, sun * ndl) : (ambient, sun * ndl + sunAmbient * (1 - ndl));
    }
}

/// <summary>
/// A technique of the two effect files the client ships for the _HO material names (Resource/Shaders.m2d:
/// fx/effectuvdistortionmask_ho.fxo and fx/effectdissolve_ho.fxo). ShaderService creates the NiD3DXEffectShaderLibrary
/// over that folder (ShaderService.cpp:356-380; NiD3DXEffectShaderLibrary.cpp:405-417), each technique is a shader named
/// after it (NiD3DXEffectParser.cpp:504-505), and NiSingleShaderMaterialLibrary gives a block naming one that shader. Each
/// is one ps_2_0 / vs_1_1 pass with UsesNiRenderState 1 (the block's alpha, z-buffer and stencil properties apply) and
/// UsesNiLightState 0 (no lighting). Read from the compiled passes:
/// <list type="bullet">
/// <item>Mask (EffectUVDistortionMask*): every map reads texcoord 0 through its own texture transform (TEXTRANSFORM base,
/// detail, normal, glow). n = Normal.r; the base and glow maps are read at their uv + n * MATERIALPOWER (the glossiness);
/// b = pow(Base, 1.8) in all four channels; rgb = b.rgb * c0.rgb (+ Glow.rgb * c1.rgb * 10 for the Glow techniques);
/// a = saturate(Detail.r * b.a * c0.a * 5). c0 is MATERIALEMISSIVE (emissive rgb, material alpha), times the vertex colour
/// for the Particle techniques; c1 is MATERIALSPECULAR, times the vertex colour for ParticleGlowLife. The base map clamps
/// except in the Wrap techniques; the detail, normal and glow maps wrap.</item>
/// <item>Dissolve (EffectDissolve*): x = 2 * c0.a + Detail.r - 1; rgb = Base.rgb * c0.rgb + Gloss(1 - 2x, 0).rgb;
/// a = x * Base.a, not saturated. c0 is MATERIALEMISSIVE, times the vertex colour for DissolveParticle. The base and
/// detail maps take their texture transforms, the gloss map none; the base and gloss maps clamp, the detail map wraps.</item>
/// </list>
/// </summary>
public sealed class HoTechnique
{
    public readonly string Name;
    public readonly bool Dissolve;     // EffectDissolve*, else EffectUVDistortionMask*
    public readonly bool Glow;         // the glow map term (Mask only)
    public readonly bool Wrap;         // the base map wraps (Mask only)
    public readonly bool VertexColor;  // c0 is times the vertex colour (the Particle techniques)
    public readonly bool GlowVertexColor;  // c1 is times the vertex colour (ParticleGlowLife)

    /// <summary>The maps (ModelMesh.Textures slots) the pass samples: base, detail and gloss for Dissolve; base, detail and
    /// normal, and glow for the Glow techniques, for Mask.</summary>
    public readonly string[] Maps;

    HoTechnique(string name, bool dissolve, bool glow, bool wrap, bool vc, bool glowVc)
    {
        Name = name; Dissolve = dissolve; Glow = glow; Wrap = wrap; VertexColor = vc; GlowVertexColor = glowVc;
        Maps = dissolve ? new[] { "base", "detail", "gloss" } : glow ? new[] { "base", "detail", "normal", "glow" } : new[] { "base", "detail", "normal" };
    }

    /// <summary>The nine techniques, in the files' order.</summary>
    public static readonly HoTechnique[] All =
    {
        new("EffectUVDistortionMask_HO", false, false, false, false, false),
        new("EffectUVDistortionMaskGlow_HO", false, true, false, false, false),
        new("EffectUVDistortionMaskParticle_HO", false, false, false, true, false),
        new("EffectUVDistortionMaskParticleGlow_HO", false, true, false, true, false),
        new("EffectUVDistortionMaskParticleGlowLife_HO", false, true, false, true, true),
        new("EffectUVDistortionMaskWrap_HO", false, false, true, false, false),
        new("EffectUVDistortionMaskGlowWrap_HO", false, true, true, false, false),
        new("EffectDissolve_HO", true, false, false, false, false),
        new("EffectDissolveParticle_HO", true, false, false, true, false),
    };

    /// <summary>The technique of that name, or null (every shipped block spells it as the technique does).</summary>
    public static HoTechnique Of(string name)
    {
        if (name == null) return null;
        foreach (var h in All) if (h.Name == name) return h;
        return null;
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
    /// <summary>Mesh emitter: the share of its mesh refs that are null. The client picks one ref at random per particle
    /// and a null pick (or no refs at all) creates no particle (NiPSMeshEmitter.cpp:103-125, ComputeInitialPositionAndVelocity
    /// false, NiPSEmitter.cpp:165-183 continue); 1 means the emitter emits nothing.</summary>
    public double MeshNullShare;
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

/// <summary>A particle collider (NiPSPlanarCollider / NiPSSphericalCollider, read by NiPSCollider::LoadBinary and the two
/// LoadBinary overrides). The colliders step hands the kernel only the active ones (NiPSSimulatorCollidersStep::Update,
/// NiPSSimulatorCollidersStep.cpp:231-262). Node is the "Collider Object", -1 without one: a plane without one stays
/// through the system's origin with its own axes and size, a sphere at the system's origin with its own radius
/// (NiPSPlanarCollider::Update, NiPSPlanarCollider.cpp:72-128; NiPSSphericalCollider.cpp:41-44, :80-92).</summary>
public sealed class ColliderDef
{
    public bool Sphere, Active = true, SpawnOnCollide, DieOnCollide;
    public double Bounce = 1, Width = 1, Height = 1, Radius = 1;
    public Vec3 XAxis = new(1, 0, 0), YAxis = new(0, 1, 0);
    public int Node = -1, Spawner = -1; public string Type;
    /// <summary>The "Collider Object" block as the file has it, -1 without one; Node is its scene node, -1 when it is not one.</summary>
    public int ObjectBlock = -1;
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
    /// <summary>"Has Rotation Axes": the system keeps a rotation axis per particle (the emitter's, or a random one:
    /// NiPSEmitter.cpp:248-260), which the mesh-align kernel turns the particle about (MeshAlign).</summary>
    public bool HasRotationAxes;
    /// <summary>The aligned quads' texture animation (AlignedQuad.TexAnim): the generator's flipbook fields, used only when the
    /// system has "Has Animated Textures" (NiPSAlignedQuadGenerator.cpp:325-337); without it every quad shows the whole texture.</summary>
    public bool AnimatedTextures, UVScrolling, PingPong;
    public int FramesAcross = 1, FramesDown = 1, NumFrames, InitialFrame; public double InitialFrameVar, NumFramesVar, InitialTime, FinalTime = 1;
    public string Texture;                       // base texture file (or the first shader map)
    public int AlphaFlags = -1;                  // NiAlphaProperty flags of the system
    public bool Additive => AlphaFlags >= 0 && ((AlphaFlags >> 5) & 15) == 0 && (AlphaFlags & 1) != 0;
    public int MaxParticles = 100; public bool WorldSpace;
    public bool PreRpi = true;                   // "Pre RPI": the emitters' direction method (EmitterDef.Direction)
    /// <summary>An NiPSResetOnLoopCtlr on the system: its time always loops (it sets the LOOP cycle at each update), and when
    /// it wraps the system is reset, every particle gone (NiPSResetOnLoopCtlr::Update, NiPSResetOnLoopCtlr.cpp;
    /// NiPSParticleSystem::ResetParticleSystem, NiPSParticleSystem.cpp:217-234; a mesh system's clones go back to their
    /// pools, NiPSMeshParticleSystem.cpp:174-201). Null without one, or when it is inactive (DontDoUpdate).</summary>
    public TimeCtl ResetCtl;
    public int Colliders, Spawners;
    /// <summary>The colliders of the system's NiPSSimulatorCollidersStep, in its order (ParticleColliders).</summary>
    public List<ColliderDef> ColliderList = new();
    /// <summary>The system's material and colour terms, read as an effect mesh's: the material name, maps, NiAlphaProperty,
    /// NiMaterialProperty and NiVertexColorProperty, and the NiMaterialColorControllers on that material (Material.Colors).
    /// The client draws the particle quads through the material the block names, as a mesh's: an _HO technique
    /// (EffectMesh.Ho, with the key colour as its vertex colour), MS2AlphaBoostMaterial under EffectMesh.AlphaBoost, or else
    /// a standard material, the same path as any mesh: the facing quad generator gives them a normal (camRight x camUp,
    /// NiPSFacingQuadGenerator.cpp:340-364) and, when the system has colours, a COLOR index 0 stream (:191-207), so the key
    /// colour stands in for the terms the vertex colour mode names (EffectMesh.VertexColors) and the material gives the rest
    /// (EffectMesh.Shade). Material.Mesh.Colors is an empty array when the system has colours ("Has Colors",
    /// NiPSParticleSystem::LoadBinary, NiPSParticleSystem.cpp:1227-1228), null without.</summary>
    public EffectMesh Material;
    /// <summary>The detail map's alpha multiplies the opacity: the system's active material is a standard one, as a mesh's
    /// (EffectMesh.DetailOpacity on Material: the boost swap counts, an _HO technique reads the detail as a mask).</summary>
    public bool DetailOpacity => Material?.DetailOpacity == true;
    /// <summary>An NiPSMeshParticleSystem: each particle draws a clone of one of its master particles (MeshAlign), the
    /// system itself nothing (NiPSMeshParticleSystem::RenderImmediate, NiPSMeshParticleSystem.cpp:482).</summary>
    public bool MeshParticles;
    /// <summary>The master particles ("Master Particles"), one per generation: each one's subtree as node indices with the
    /// root first and every node after its parent, and the effect meshes in it (EffectScene.Meshes indices). An empty array
    /// for a null master.</summary>
    public int[][] MasterNodes = Array.Empty<int[]>(), MasterMeshes = Array.Empty<int[]>();
    /// <summary>NiPSSimulatorMeshAlignStep's "Num Rotation Keys" (NiPSSimulatorMeshAlignStep::LoadBinary, :732-745), -1 when the
    /// system has no such step. MeshAlign keeps the keyed rotation at the identity (0 keys in both data sets).</summary>
    public int MeshAlignKeys = -1;
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
    /// <summary>The first NiMesh a walk from the root meets, null for none; it may be a culled one, which Meshes leaves out
    /// (an emitter's shape: the ride effect eff_item_ride_50600337_r_ringer_sprun), so it is read on its own then. The walk is
    /// the client's node stack (Unk_NodeQueue::Next @0x14098e0d0, alphaanimation.cpp:375-388: it pops the tail and queues a
    /// node's children at the tail, so a node's last child is met first), and the test is an exact NiMesh, so a particle
    /// system (an NiMesh kind) is passed over; the masters of a mesh-particle system hang under no node, so it never meets
    /// them. The swoosh takes this mesh's properties (swoosheffectparam.cpp:353-366).</summary>
    public ModelMesh WalkedMesh;

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

    /// <summary>EffectMesh.Lent for every drawn _HO block (the meshes, and the systems with an emitter): each map its
    /// technique samples that it lacks, when the effect's other drawn _HO blocks of the same file carry it with one texture.</summary>
    void LendMissingMaps()
    {
        var drawn = new List<EffectMesh>();
        foreach (var m in Meshes) if (m.Ho != null) drawn.Add(m);
        foreach (var s in Systems) if (s.Emitters.Count > 0 && s.Material?.Ho != null) drawn.Add(s.Material);
        foreach (var em in drawn)
            foreach (var slot in em.Ho.Maps)
            {
                if (em.Mesh.Textures.ContainsKey(slot)) continue;
                string only = null; bool several = false;
                foreach (var o in drawn)
                    if (o != em && o.Ho.Dissolve == em.Ho.Dissolve && o.Mesh.Textures.TryGetValue(slot, out var f))
                    {
                        if (only == null) only = f;
                        else if (!string.Equals(only, f, StringComparison.OrdinalIgnoreCase)) several = true;
                    }
                if (only != null && !several) (em.Lent ??= new())[slot] = only;
            }
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

        var model = ModelData.Load(n, keepCulledSubtrees: true, keepMasterParticles: true);   // the nodes carry their own culling (EffectNode.Culled); ReadMasters marks the masters
        var meshOf = new Dictionary<int, EffectMesh>();
        foreach (var m in model.Meshes)
        {
            if (m.Block < 0 || !sc.NodeOfBlock.TryGetValue(m.Block, out int node)) continue;
            var em = new EffectMesh { Mesh = m, Node = node, AlphaBoost = ModelData.AlphaBoost(n, m.Block) };
            sc.Meshes.Add(em); meshOf[m.Block] = em;
        }
        if (n.Roots.Count > 0)
        {
            var stack = new List<long> { n.Roots[0] };
            var seen = new HashSet<long>();
            while (stack.Count > 0)
            {
                long b = stack[^1]; stack.RemoveAt(stack.Count - 1);
                if (b < 0 || b >= B.Count || !seen.Add(b)) continue;
                if (B[(int)b].Type == "NiMesh")
                {
                    sc.WalkedMesh = meshOf.TryGetValue((int)b, out var wm) ? wm.Mesh : ModelData.Load(n, onlyBlock: (int)b).Meshes.FirstOrDefault();
                    break;
                }
                // only an NiNode kind has Children (NiDynamicCast(NiNode) in Next)
                if (B[(int)b].TryGetValue("Children", out var wc) && wc is long[] wk) foreach (var k in wk) if (k >= 0) stack.Add(k);
            }
        }
        var systems = new Dictionary<int, ParticleSystemDef>();
        foreach (var (blk, node) in sc.NodeOfBlock)
            if (B[blk].Type is "NiPSParticleSystem" or "NiPSMeshParticleSystem") systems[blk] = sc.ReadSystem(n, blk, node, parent);

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
        foreach (var (blk, s) in systems) if (B[blk].Type == "NiPSMeshParticleSystem") sc.ReadMasters(n, blk, s, meshOf);
        sc.LendMissingMaps();
        foreach (var t in B.Select(b => b.Type).Distinct())
            if (t.StartsWith("NiPS", StringComparison.Ordinal) && !KnownPs.Contains(t) && !EmitterFloatCtlrs.ContainsKey(t)) sc.Unsupported.Add(t);
        return sc;
    }

    /// <summary>NiTimeController's MANAGERCONTROLLED_MASK flag.</summary>
    public const int ManagerControlled = 0x20;

    static readonly HashSet<string> KnownPs = new()
    {
        "NiPSParticleSystem", "NiPSSimulator", "NiPSSimulatorGeneralStep", "NiPSSimulatorForcesStep", "NiPSSimulatorCollidersStep", "NiPSSimulatorFinalStep",
        "NiPSSimulatorMeshAlignStep", "NiPSMeshEmitter", "NiPSBoxEmitter", "NiPSSphereEmitter", "NiPSCylinderEmitter", "NiPSAlignedQuadGenerator",
        "NiPSFacingQuadGenerator", "NiPSBoundUpdater", "NiPSBombForce", "NiPSGravityForce", "NiPSDragForce", "NiPSEmitParticlesCtlr", "NiPSForceActiveCtlr",
        "NiPSGravityStrengthCtlr", "NiPSPlanarCollider", "NiPSSphericalCollider", "NiPSResetOnLoopCtlr",
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
            case "NiVisController":
                // a manager-controlled one takes its value from a sequence (UpdateValue) and does nothing on its own; an
                // inactive one does not update (DontDoUpdate; a bool interpolator is not AlwaysUpdate)
                if (NodeOfBlock.TryGetValue((int)target, out int vn) && (c.IntOr("Flags", 0) & ManagerControlled) == 0 && ctl.Active && BoolTrack.Of(n, interp) is { } vis)
                { Nodes[vn].Vis = vis; Nodes[vn].VisCtl = ctl; }
                break;
            case "NiPSResetOnLoopCtlr":
                if (systems.TryGetValue((int)target, out var rs) && ctl.Active) { ctl.Cycle = 0; rs.ResetCtl = ctl; }
                break;
            case "NiPSEmitParticlesCtlr":
                if (systems.TryGetValue((int)target, out var ps))
                {
                    // the controller binds the first emitter with its name (GetEmitterByName, NiPSParticleSystem.cpp:285-299,
                    // through NiPSEmitterCtlr::PostLinkObject, NiPSEmitterCtlr.cpp:119-125) and with none it does nothing
                    // (NiPSEmitParticlesCtlr::Update, NiPSEmitParticlesCtlr.cpp:81)
                    string en = n.Str(c.IntOr("Emitter Name", -1));
                    if (ps.Emitters.Find(x => x.Name == en) is { } emitted)
                    {
                        emitted.Rate = Curve(n, interp); emitted.RateCtl = ctl;
                        emitted.On = Curve(n, c.IntOr("Emitter Active Interpolator", -1)); emitted.OnCtl = ctl;
                    }
                }
                break;
            case "NiPSForceActiveCtlr":
                if (systems.TryGetValue((int)target, out var ps2))
                {
                    // the first force with the name (NiPSForceCtlr::GetForcePointerFromName, NiPSForceCtlr.cpp:100-118;
                    // GetForceByName, NiPSSimulatorForcesStep.inl:29-43)
                    string fn = n.Str(c.IntOr("Force Name", -1));
                    if (ps2.Forces.Find(x => x.Name == fn) is { } toggled) { toggled.On = Curve(n, interp); toggled.OnCtl = ctl; }
                }
                break;
            case "NiPSGravityStrengthCtlr":
                // NiPSForceCtlr finds its force by name (GetForceByName); an inactive controller does not update
                // (NiTimeController::DontDoUpdate, NiTimeController.inl:152), so the force keeps its stored strength
                if (systems.TryGetValue((int)target, out var ps3) && ctl.Active && ValueCurve(n, interp) is { } gs)
                {
                    // the first force with the name, taken only when it is a gravity force
                    // (NiPSGravityStrengthCtlr::InterpTargetIsCorrectType, NiPSGravityStrengthCtlr.cpp:59-62)
                    string fn = n.Str(c.IntOr("Force Name", -1));
                    if (ps3.Forces.Find(x => x.Name == fn) is { Kind: ForceKind.Gravity } gravity) gravity.StrengthAnim = new EmitterAnim { Curve = gs, Ctl = ctl };
                }
                break;
            default:
                if (EmitterFloatCtlrs.TryGetValue(c.Type, out var field))
                {
                    // NiPSEmitterCtlr binds the first emitter with its name (NiPSEmitterCtlr.cpp:119-125, GetEmitterByName,
                    // NiPSParticleSystem.cpp:285-299); an inactive controller does not update (NiPSEmitterFloatCtlr::Update,
                    // NiPSEmitterFloatCtlr.cpp:87-109)
                    if (systems.TryGetValue((int)target, out var ps4) && ctl.Active && ValueCurve(n, interp) is { } ef)
                    {
                        string en = n.Str(c.IntOr("Emitter Name", -1));
                        if (ps4.Emitters.Find(x => x.Name == en) is { } animated)
                        {
                            animated.Anims ??= new EmitterAnim[Enum.GetValues<EmitterField>().Length];
                            animated.Anims[(int)field] = new EmitterAnim { Curve = ef, Ctl = ctl };
                        }
                    }
                    break;
                }
                if (c.Type.StartsWith("Ni", StringComparison.Ordinal)) Unsupported.Add(c.Type);
                break;
        }
    }

    /// <summary>A mesh-particle system's master particles (ParticleSystemDef.MasterNodes / MasterMeshes). The masters are not
    /// in the scene graph (both data sets), so their meshes are not drawn where they are (EffectMesh.Master); each particle
    /// draws a clone (NiPSMeshParticleSystem::InitializeParticle, NiPSMeshParticleSystem.cpp:230-300). A clone takes the
    /// system's property state under its own (UpdatePropertiesDownward, :270), and the system is fog-off
    /// (NifModel.ParticleSystemFlag), so the master meshes draw without the depth fog (EffectFogOffTests).</summary>
    void ReadMasters(NifFile n, int blk, ParticleSystemDef d, IReadOnlyDictionary<int, EffectMesh> meshOf)
    {
        var B = n.Blocks;
        d.MeshParticles = true;
        var masters = B[blk].Ints("Master Particles").ToArray();
        d.MasterNodes = new int[masters.Length][]; d.MasterMeshes = new int[masters.Length][];
        for (int g = 0; g < masters.Length; g++)
        {
            var nodes = new List<int>(); var meshes = new List<int>();
            var seen = new HashSet<int>();
            void Walk(int b)
            {
                if (!seen.Add(b) || !NodeOfBlock.TryGetValue(b, out int nd)) return;
                nodes.Add(nd);
                if (meshOf.TryGetValue(b, out var em)) { meshes.Add(Meshes.IndexOf(em)); em.Master = true; em.Mesh.NoFog = true; }
                if (B[b].Has("Children"))   // a mesh (an NiAVObject leaf) has none
                    foreach (var k in B[b].Ints("Children")) if (k >= 0) Walk((int)k);
            }
            if (masters[g] >= 0) Walk((int)masters[g]);
            d.MasterNodes[g] = nodes.ToArray(); d.MasterMeshes[g] = meshes.ToArray();
        }
    }

    ParticleSystemDef ReadSystem(NifFile n, int blk, int node, IReadOnlyDictionary<int, int> parent)
    {
        var B = n.Blocks; var b = B[blk];
        var d = new ParticleSystemDef { Node = node, Name = n.Name(blk), MaxParticles = (int)Math.Clamp(b.IntOr("Max Num Particles", 100), 1, 2000), WorldSpace = b.IntOr("World Space", 0) != 0, PreRpi = b.IntOr("Pre RPI", 1) != 0, Spawners = (int)b.IntOr("Num Spawners", 0) };
        d.HasRotations = b.IntOr("Has Rotations", 1) != 0;
        d.HasRotationAxes = b.IntOr("Has Rotation Axes", 0) != 0;
        d.AnimatedTextures = b.IntOr("Has Animated Textures", 0) != 0;
        d.NormalMethod = (int)b.IntOr("Normal Method", AlignedQuad.Camera); d.UpMethod = (int)b.IntOr("Up Method", AlignedQuad.Camera);
        if (b.Has("Normal Direction")) d.NormalDirection = V3(b.Struct("Normal Direction"));
        if (b.Has("Up Direction")) d.UpDirection = V3(b.Struct("Up Direction"));
        // The quads are drawn through the material the system block names, as a mesh's (ModelData.MaterialName; most name
        // MS2StandardMaterial, see ParticleSystemDef.DetailOpacity), or the renderer's default NiStandardMaterial when it
        // names none (NiRenderer::ApplyDefaultMaterial, NiRenderer.cpp:343-345), or MS2AlphaBoostMaterial in its place
        // (AlphaBoost), or an _HO technique (EffectMesh.Ho, which reads the maps as HoTechnique gives). The standard ones'
        // HandlePreLightTextureApplication applies the base map and then the detail map at twice its colour
        // (NiStandardMaterial::HandleBaseMap / HandleDetailMap, NiStandardMaterial.cpp:4500-4527, 4765-4774, called at
        // :5517-5541), each through its own texture transform (GetTextureTransformMappingFromEnum, :364-430): so the system keeps its maps as a mesh does (ModelMesh.Maps).
        var mat = new ModelMesh { Block = blk, Name = d.Name, Colors = b.IntOr("Has Colors", 0) != 0 ? Array.Empty<float>() : null,
                                  Textures = ModelData.TexturesOf(n, blk), Maps = ModelData.MapsOf(n, blk), Material = ModelData.MaterialName(n, b) };
        // the fog-off subtrees as a mesh's (ModelMesh.NoFog): PrepareLoadedNif runs FUN_141567b60 on every loaded nif, effects
        // too (CNifLoadingService::CreateCachedNif, nifeffectparam.cpp:2338), and its walk tests a particle system as any object;
        // LoadBinary has set its flag 0x400 (NiPSParticleSystem.cpp:1282), so the walk always finds it fog-off
        (_, mat.NoFog) = ModelData.SubtreeTags(n, parent, n.Roots[0], blk);
        d.Material = new EffectMesh { Mesh = mat, Node = node, AlphaBoost = ModelData.AlphaBoost(n, blk) };
        // The quads' culled side comes down the scene graph as a mesh's does (ModelData.ReadDrawState): the client draws a
        // system through the same NiDX9RenderState::ApplyStencil as a mesh, and with no NiStencilProperty on it or above it
        // (no effect system has one) the default DRAW_CCW_OR_BOTH culls the back faces, the side away from the
        // quad's normal (AlignedQuad.Quad, x cross y = the normal; a facing quad's normal points at the camera). So a
        // fixed-normal quad seen from behind is not drawn: the healing pillar's six LOCAL_FIXED quads (eff_com_recovery
        // PArray01) go through D3DCULL_CW with their back to the camera and change no pixel in the client's frame.
        ModelData.ReadDrawState(n, parent, blk, mat);

        foreach (var pi in b.Ints("Properties"))
        {
            if (pi < 0) continue;
            var p = B[(int)pi];
            if (p.Type == "NiAlphaProperty") { d.AlphaFlags = mat.AlphaFlags = (int)p.Int("Flags"); mat.AlphaThreshold = (int)p.IntOr("Threshold", 0); }
            else if (p.Type == "NiVertexColorProperty") mat.VertexColorFlags = (int)p.Int("Flags");
            else if (p.Type == "NiMaterialProperty")
            {
                float[] Rgb(string k) { var c = p.Struct(k); return new[] { c.Float("r"), c.Float("g"), c.Float("b") }; }
                mat.Ambient = Rgb("Ambient Color"); mat.Diffuse = Rgb("Diffuse Color"); mat.Emissive = Rgb("Emissive Color");
                mat.Specular = Rgb("Specular Color"); mat.Glossiness = p.Float("Glossiness");
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
                    case "NiPSSimulatorMeshAlignStep":
                        d.MeshAlignKeys = (int)st.IntOr("Num Rotation Keys", 0);
                        break;
                    case "NiPSSimulatorCollidersStep":
                        d.Colliders += (int)st.IntOr("Num Colliders", 0);
                        foreach (var ci in st.Ints("Colliders"))
                        {
                            if (ci < 0 || ci >= B.Count) continue;
                            var c = B[(int)ci];
                            if (c.Type != "NiPSPlanarCollider" && c.Type != "NiPSSphericalCollider") { Unsupported.Add(c.Type); continue; }
                            var cd = new ColliderDef
                            {
                                Type = c.Type, Sphere = c.Type == "NiPSSphericalCollider", Active = c.IntOr("Active", 1) != 0,
                                Bounce = c.Float("Bounce"), SpawnOnCollide = c.IntOr("Spawn on Collide", 0) != 0,
                                DieOnCollide = c.IntOr("Die on Collide", 0) != 0, Spawner = (int)c.IntOr("Spawner", -1),
                            };
                            if (cd.Sphere) cd.Radius = c.Float("Radius");
                            else { cd.Width = c.Float("Width"); cd.Height = c.Float("Height"); cd.XAxis = V3(c.Struct("X Axis")); cd.YAxis = V3(c.Struct("Y Axis")); }
                            long co = c.IntOr("Collider Object", -1);
                            cd.ObjectBlock = co >= 0 && co < B.Count ? (int)co : -1;
                            if (co >= 0 && NodeOfBlock.TryGetValue((int)co, out int cn)) cd.Node = cn;
                            // The collider takes its object's world transform whatever NiAVObject it is
                            // (NiPSPlanarCollider.cpp:93-109); one that is not a scene node here would sit at the system's
                            // origin instead, so it is listed as not read (0 in the data, EveryColliderIsRead).
                            else if (cd.ObjectBlock >= 0) Unsupported.Add($"{c.Type} object {B[cd.ObjectBlock].Type}");
                            d.ColliderList.Add(cd);
                        }
                        break;
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
                    var refs = e.Ints("Mesh Emitters");
                    int nulls = refs.Count(r => r < 0 || r >= n.Blocks.Count);
                    ed.MeshNullShare = refs.Length == 0 ? 1 : (double)nulls / refs.Length;
                    foreach (var mb in refs)
                    {
                        if (mb < 0 || mb >= n.Blocks.Count) continue;
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
