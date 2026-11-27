using Ms2.Formats.Nif;

namespace Ms2.Formats.Model;

public sealed class ModelBone
{
    public string Name;
    public string FileName;               // the node's name in the file (Name is made unique per model, see NifFile.NodeName)
    public int Parent = -1;               // index into Bones, -1 = child of the scene root
    public Xform Rest;                    // local rest transform (= skin bind pose on shipped humanoids)
    public Xform World;                   // rest transform relative to the scene root
}

/// <summary>Which faces a mesh's draw culls (<see cref="ModelMesh.Cull"/>).</summary>
public enum FaceCull { Back, Front, None }

/// <summary>A cull mode under a mirrored world transform.</summary>
public static class FaceCulls
{
    /// <summary>
    /// The side Godot must cull for a mesh drawn with <paramref name="cull"/> under a world transform with a negative
    /// determinant: D3D9 culls by the screen-space winding with the render state's cull mode whatever the world matrix
    /// (NiDX9RenderState::ApplyStencil, NiDX9RenderState.cpp:615, the left-handed column only from SetLeftRightSwap,
    /// which the client never calls), so a mirrored mesh shows its other side; Godot flips the culled side for a mirrored
    /// instance, so back and front swap here. Unmirrored, or no culling: unchanged.
    /// </summary>
    public static FaceCull UnderWorld(this FaceCull cull, bool mirrored) =>
        !mirrored ? cull : cull switch { FaceCull.Back => FaceCull.Front, FaceCull.Front => FaceCull.Back, _ => FaceCull.None };
}

/// <summary>The blend a mesh draws with (ModelMesh.Blend).</summary>
public enum BlendForm { Opaque, Mix, Add, AddUnweighted }

public sealed class ModelMesh
{
    public int Block = -1;                // the NiMesh block this came from
    public string Name;                   // unique in the model (NifFile.NodeName): the target of a controller or kf channel
    public string FileName;               // the mesh's name in the file (body cuts, replace and self-node lookups)
    public float[] Positions;             // xyz per vertex, skin space (scene root) for skinned meshes
    public float[] Normals;               // xyz per vertex, or null
    public float[] Tangents, Binormals;   // xyz per vertex (normal-map tangent frame), or null
    public float[] Uvs;                   // uv per vertex, or null
    public int[] Joints;                  // 4 bone indices per vertex (into ModelData.Bones), skinned only
    public float[] Weights;               // 4 weights per vertex, sum 1, skinned only
    public int[] Indices;                 // triangle list, file winding
    public bool Skinned;
    public Dictionary<int, Xform> SkinBinds;  // skinned only: bone index -> scene root -> bone at this skin's bind
    public int ParentBone = -1;           // rigid meshes: the NiNode they hang from (-1 = scene root)
    public float[] Bound;                 // the NiMesh "Bounding Sphere" (its model bound): centre xyz, radius; null if absent
    public Xform Local = Xform.Identity;  // rigid meshes: transform relative to ParentBone
    /// <summary>Skinned meshes: the mesh node's transform relative to the scene root, which places <see cref="Bound"/> (the
    /// model bound is in the mesh's own space; its world bound is the mesh's world transform times it, NiBound::Update).</summary>
    public Xform BoundToRoot = Xform.Identity;
    /// <summary>Skinned meshes: the mesh node's own local transform (relative to its parent in the file), which it keeps when
    /// the client hangs it elsewhere (a kfm item's self mesh, CKfmModel::vfunc_12, kfmmodel.cpp:533-537).</summary>
    public Xform SkinMeshLocal = Xform.Identity;
    /// <summary>Skinned meshes: the skin's "Skeleton Transform" (m_kRootBoneParentToSkinTransform), the root bone's parent
    /// (the scene root) to skin space. Hardware skinning draws a vertex at the mesh's world times it times the bone's pose
    /// relative to the scene root (NiSkinningMeshModifier.cpp:891-908, GetWorldToSkinTransform in the .inl:58-64).</summary>
    public Xform SkinTransform = Xform.Identity;
    /// <summary>Skinned meshes whose NiSkinningMeshModifier has RECOMPUTE_BOUNDS (flag 0x2): each skin bone's index and its
    /// "Bone Bounds" entry (centre xyz, radius) in the skin's bone order, else null. The client then rebuilds the model bound
    /// every update from the bones' world transforms (NiSkinningMeshModifier.cpp:913-916, UpdateModelBounds :1036-1056).</summary>
    public List<(int Bone, float[] Bound)> BoneBounds;
    /// <summary>With <see cref="BoneBounds"/>: the mesh node relative to the scene root, times the skin's Skeleton Transform
    /// (m_kRootBoneParentToSkinTransform), times the scene root's own transform. UpdateModelBounds maps the merged bone bound
    /// by GetWorldToSkinTransform = SkeletonTransform * RootBoneParentWorld^-1 (NiSkinningMeshModifier.inl:58-65) and the mesh
    /// world transform then places it, so in skeleton (scene root) space the world bound is this times the merged bound.</summary>
    public Xform BoneBoundsToRoot = Xform.Identity;
    /// <summary>Skinned meshes: the frame PerformPick tests the mesh's triangles in, relative to the scene root. The pick reads
    /// the POSITION stream as stored, unskinned, at the mesh's world transform (FindIntersectionsMeshPosition,
    /// NiCollisionTraversals.cpp:2248-2330); the held <see cref="Positions"/> were moved to the scene root by the skin's
    /// first bone bind, so this is the mesh node relative to the scene root times the inverse of that move.</summary>
    public Xform PickToRoot = Xform.Identity;
    public bool AlphaTest;                // has an NiAlphaProperty
    public float[] Ambient, Diffuse, Emissive;   // NiMaterialProperty colours (rgb), or null without a material
    public float[] Specular;              // NiMaterialProperty specular colour (rgb), or null
    public float Glossiness;              // NiMaterialProperty glossiness (the specular power)
    public float MaterialAlpha = 1f;      // NiMaterialProperty alpha (1 without a material)
    /// <summary>The NiMesh's NiAVObject flags as streamed (NiAVObject::LoadBinary, NiAVObject.cpp:613).</summary>
    public int Flags;
    public AlphaAnim AlphaAnim;           // an NiAlphaController on that material, which animates its alpha, or null
    /// <summary>The mesh or a node above it carries a "NoShadow" UserPropBuffer tag (NifModel.TaggedNoShadow): the client's
    /// caster walks skip that subtree, so the mesh never casts.</summary>
    public bool NoShadow;
    /// <summary>The client gives the mesh's subtree a fog-off NiFogProperty (NifModel.FogOff), so it draws without the depth
    /// fog; the height fog is its own switch and stays.</summary>
    public bool NoFog;
    public bool SpecularOn;              // an NiSpecularProperty with its enable flag set
    public float[] Colors;                // rgba 0..1 per vertex, or null
    public float[] Uvs1;                  // second uv set (TEXCOORD1), or null
    public int AlphaFlags = -1;           // NiAlphaProperty flags, -1 without one
    public int AlphaThreshold;            // NiAlphaProperty test threshold (0..255)
    public int VertexColorFlags = -1;     // NiVertexColorProperty flags, -1 without one (inherited from the scene root)
    /// <summary>NiZBufferProperty flags of the nearest one on the mesh or its ancestors (Gamebryo's property inheritance:
    /// a child's property replaces its parent's), else the default property's 15: test on, write on, TEST_LESSEQUAL
    /// (NiZBufferProperty.inl:17-23). Bit 0 = test, bit 1 = write, bits 2-5 = test function (NiZBufferProperty.h:79-82).</summary>
    public int ZFlags = 15;
    public bool ZTest => (ZFlags & 1) != 0;
    public bool ZWrite => (ZFlags & 2) != 0;
    /// <summary>NiStencilProperty draw mode of the nearest one (bits 10-11, NiStencilProperty.h:121-122): 0 DRAW_CCW_OR_BOTH
    /// (the default property's, NiStencilProperty.inl:27), 1 DRAW_CCW, 2 DRAW_CW, 3 DRAW_BOTH (no culling).</summary>
    public int DrawMode;
    /// <summary>The faces the renderer culls for <see cref="DrawMode"/>, as NiDX9RenderState maps the draw mode with left-handed
    /// culling off (NiDX9RenderState::ApplyStencil, NiDX9RenderState.cpp:592, mapping :380-389): DRAW_BOTH none, DRAW_CW the front faces, DRAW_CCW and DRAW_CCW_OR_BOTH the
    /// back faces (front = the winding the model builders emit).</summary>
    public FaceCull Cull => DrawMode switch { 3 => FaceCull.None, 2 => FaceCull.Front, _ => FaceCull.Back };
    /// <summary>Under an NiSortAdjustNode whose mode is SORTING_OFF: the renderer draws the mesh unsorted, in scene order, after
    /// the opaque buckets and before the depth-sorted alpha list. The outermost sort-adjust ancestor decides, as its OnVisible
    /// marks its whole subtree's visible objects after the inner ones did (NiSortAdjustNode.cpp:36-57).</summary>
    public bool NoSort;
    /// <summary>Rigid meshes: the bone of the nearest NiBillboardNode above the mesh, or -1. Its FaceMode is BillboardMode
    /// (NiBillboardNode.h:34-41: 0 ALWAYS_FACE_CAMERA, 1 ROTATE_ABOUT_UP, 2 RIGID_FACE_CAMERA, 3 ALWAYS_FACE_CENTER,
    /// 4 RIGID_FACE_CENTER), and InBillboard takes the mesh's space to that node's.</summary>
    public int Billboard = -1;
    public int BillboardMode;
    public Xform InBillboard = Xform.Identity;
    /// <summary>A controller the field shader runs from the scene time: the alpha, a flipbook on the base or dark map, a uv
    /// controller on a map it draws (base, dark, glow, decal0), or a keyed morph.</summary>
    public bool AnimatesMaterial => AlphaAnim != null || FlipSlot is "base" or "dark" || UvAnims.Any(s => s.Slot is "base" or "dark" or "glow" or "decal0") || Morph != null;
    public bool AlphaBlendOn => AlphaFlags >= 0 && (AlphaFlags & 1) != 0;
    public bool AlphaTestOn => AlphaFlags >= 0 && (AlphaFlags & 0x200) != 0;
    /// <summary>NiAlphaProperty test function, flag bits 10-12 (TEST_FUNC_MASK 0x1c00, NiAlphaProperty.h:116-118): 0 ALWAYS,
    /// 1 LESS, 2 EQUAL, 3 LESSEQUAL, 4 GREATER, 5 NOTEQUAL, 6 GREATEREQUAL, 7 NEVER (NiAlphaProperty.h:49-56).</summary>
    public int AlphaTestFunc => AlphaFlags >= 0 ? (AlphaFlags >> 10) & 7 : 0;
    /// <summary>The pixel shader runs the alpha test: the property has the test flag and a function other than TEST_ALWAYS
    /// (NiStandardMaterialDescriptor.cpp:665-672 sets ALPHATEST only then). A mesh without the flag is never tested, whatever
    /// its threshold: the renderer hands it reference 0 and no clip side (D3D11ShaderConstantMap.cpp:3647,3692).</summary>
    public bool AlphaTests => AlphaTestOn && AlphaTestFunc != 0;
    /// <summary>The alpha test reference the D3D11 renderer hands the pixel shader: the property's threshold / 255 while it
    /// tests, else 0 (SCM_DEF_ALPHA_TEST_REF, D3D11ShaderConstantMap.cpp:3690-3700).</summary>
    public float AlphaTestRef => AlphaTestOn ? AlphaThreshold / 255f : 0f;
    /// <summary>When the pixel shader clips, as SCM_DEF_ALPHA_TEST_FUNC fills it (D3D11ShaderConstantMap.cpp:3630-3687):
    /// x clip above the reference, y below it, z equal to it. Without the property's test flag every side stays 0 (:3647).</summary>
    public (bool Above, bool Below, bool Equal) AlphaTestClip => !AlphaTestOn ? (false, false, false) : AlphaTestFunc switch
    {
        1 => (true, false, true),     // TEST_LESS
        2 => (true, true, false),     // TEST_EQUAL
        3 => (true, false, false),    // TEST_LESSEQUAL
        4 => (false, true, true),     // TEST_GREATER
        5 => (false, false, true),    // TEST_NOTEQUAL
        6 => (false, true, false),    // TEST_GREATEREQUAL
        7 => (true, true, true),      // TEST_NEVER
        _ => (false, false, false),   // TEST_ALWAYS
    };
    /// <summary>The pixel survives the test with <see cref="AlphaTestClip"/> against <paramref name="reference"/>.</summary>
    public bool AlphaTestKeeps(float alpha, float reference)
    {
        var (above, below, equal) = AlphaTestClip;
        return !(above && alpha > reference || below && alpha < reference || equal && alpha == reference);
    }
    /// <summary>The NiVertexColorProperty lighting mode is LIGHTING_E (flags bit 3 clear); without the property it is LIGHTING_E_A_D.</summary>
    public bool LightingE => VertexColorFlags >= 0 && (VertexColorFlags & 8) == 0;

    /// <summary>Which colour terms the mesh's vertex colours replace (NiStandardMaterial::HandleInitialSpecAmbDiffEmissiveColor,
    /// NiStandardMaterial.cpp:4377-4450). The source is the NiVertexColorProperty flags bits 4-5. EMISSIVE (1): the vertex colour is
    /// the emissive, in either lighting mode. AMB_DIFF (2): the vertex colour is the ambient and the diffuse, only with lighting
    /// E_A_D. The MS2 source 3 ("MS2: v2525"): the vertex colour is the ambient alone, also only with E_A_D. The opacity is split
    /// from the diffuse colour, or from the emissive under LIGHTING_E (:4436-4450), so it is the vertex alpha when the vertex
    /// colour is that term: AMB_DIFF with E_A_D, EMISSIVE with E. A mesh without a vertex colour stream keeps the material's
    /// terms. Effect meshes (EffectMesh.VertexColors) and character meshes (ModelBuilder.CharacterMaterial) take it from here.
    /// STILL-OPEN: the field meshes still use <see cref="VertexColorsAmbDiff"/>, which does not require E_A_D.</summary>
    public VertexColorUse VertexColors
    {
        get
        {
            if (Colors == null || VertexColorFlags < 0) return default;
            int src = (VertexColorFlags >> 4) & 3;
            bool ead = !LightingE;
            return src switch
            {
                1 => new VertexColorUse(true, false, false, !ead),
                2 when ead => new VertexColorUse(false, true, true, true),
                3 when ead => new VertexColorUse(false, false, true, false),
                _ => default,
            };
        }
    }

    /// <summary>NiAlphaProperty source blend factor, flag bits 1-4 (ONE 0, ZERO 1, SRCCOLOR 2, INVSRCCOLOR 3, DESTCOLOR 4,
    /// INVDESTCOLOR 5, SRCALPHA 6, INVSRCALPHA 7, NiAlphaProperty.h:31-45; SRC_BLEND_MASK 0x1e, :112-113).</summary>
    public int BlendSrc => AlphaFlags >= 0 ? (AlphaFlags >> 1) & 15 : 6;
    /// <summary>NiAlphaProperty destination blend factor, flag bits 5-8 (DEST_BLEND_MASK 0x1e0, :114-115); the same factor values.</summary>
    public int BlendDst => AlphaFlags >= 0 ? (AlphaFlags >> 5) & 15 : 7;

    /// <summary>How the mesh's colour meets the target, from the NiAlphaProperty blend bit and factors, which the renderer hands
    /// the D3D blend state as they are (NiDX9RenderState::ApplyAlpha, NiDX9RenderState.cpp:450-464): none (opaque),
    /// SRCALPHA / INVSRCALPHA (Mix: src * a + dst * (1 - a)), any source over ONE weighted by the source alpha (Add:
    /// dst + src * a), and ONE / ONE (AddUnweighted: dst + src, the opacity does not scale it). Every blended character and
    /// map mesh is one of these (probe over the Npc, Item and Map NIFs).</summary>
    public BlendForm Blend => !AlphaBlendOn ? BlendForm.Opaque
        : BlendDst == 0 ? (BlendSrc == 0 ? BlendForm.AddUnweighted : BlendForm.Add)
        : BlendForm.Mix;

    /// <summary>The opacity the pixel shader tests and blends: the material alpha (the animated one when an NiAlphaController
    /// drives it), or the vertex alpha where the vertex colour stands in for the term the opacity is split from
    /// (<see cref="VertexColors"/>), times the base map's alpha (HandleBaseMap, NiStandardMaterial.cpp:4503-4525).</summary>
    public static float Opacity(float baseAlpha, float materialAlpha, float vertexAlpha, bool vertexOpacity)
        => baseAlpha * (vertexOpacity ? vertexAlpha : materialAlpha);

    /// <summary>Vertex colours replace the material's ambient and diffuse (NiVertexColorProperty source AMB_DIFF).</summary>
    public bool VertexColorsAmbDiff => Colors != null && VertexColorFlags >= 0 && ((VertexColorFlags >> 4) & 3) == 2;
    public Dictionary<string, string> Textures = new();   // base, normal, gloss, glow, dark, detail, decal0, shaderN
    public Dictionary<string, TexMap> Maps = new();       // the same keys: each map's uv set, clamp and filter modes and static transform
    public string FlipSlot;               // texture slot an NiFlipController cycles (base, dark, ...), or null
    public string[] FlipFrames;           // its source file names, played evenly over FlipPeriod seconds, looping
    public float FlipPeriod;
    public List<UvAnim> UvAnims = new();  // NiTextureTransformControllers: translate, rotate or scale of one map
    /// <summary>The active CLAMP NiTextureTransformControllers. Not APP_INIT, so a field model's app time is past their stop
    /// when the field is entered, and each holds its last key: a constant on the map's transform (ModelBuilder).</summary>
    public List<UvAnim> UvHolds = new();
    /// <summary>An NiTextureTransformController on the texturing property drives the glow slot (any flags): a chaoslight
    /// part puts no layer on such a mesh when it has a glow map (ChaosLightEffect::AddLayers).</summary>
    public bool GlowMapDriven;

    /// <summary>The static transform the field shaders draw a map with (ModelBuilder.SetUvTransforms), or null for none:
    /// the map's own NiTextureTransform, or the controller default one (NiTextureTransformController.cpp:146-151,
    /// TexTransform.ControllerDefault) when a controller moves or holds a map that has no transform (moved: a running
    /// UvAnim on it), with each UvHold's last key set on it. A hold needs the map itself (Maps has the slot): on a slot the
    /// mesh has no map in it is dropped, as nothing samples that slot.</summary>
    public TexTransform DrawnTransform(string slot, bool moved)
    {
        Maps.TryGetValue(slot, out var tm);
        bool held = tm != null && UvHolds.Exists(h => h.Slot == slot);
        var t = tm?.Transform ?? (moved || held ? TexTransform.ControllerDefault() : null);
        if (t == null || !held) return t;
        t = t.Clone();
        foreach (var h in UvHolds)
            if (h.Slot == slot) t.Set(h.Operation, h.Values[^1]);
        return t;
    }
    public MorphAnim Morph;               // a keyed relative POSITION morph (Positions = its target 0), or null
    /// <summary>A keyed morph whatever its target and key counts: <see cref="Morph"/> when the field shader takes it, else
    /// one whose <see cref="MorphAnim.Base"/> holds target 0 (Positions then have the fixed weights baked). The effect
    /// runtime weighs the targets on the CPU, so it takes every one of these.</summary>
    public MorphAnim AnyMorph;
    public string Material;               // active Gamebryo material name (MS2CharacterSkinMaterial, ...), or null
    /// <summary>The client swaps this mesh to MS2AlphaBoostMaterial when the model of a Mesh or Actor entity loads
    /// (<see cref="ModelData.AlphaBoost"/>; FUN_141567000 @0x141567000, unit_141567000.cpp:112-124): field statics and
    /// character-path models alike.</summary>
    public bool AlphaBoost;
    /// <summary>The material the mesh draws with: MS2AlphaBoostMaterial where <see cref="AlphaBoost"/>
    /// swapped it, else <see cref="Material"/> (null: the renderer's default NiStandardMaterial).</summary>
    public string ActiveMaterial => AlphaBoost ? "MS2AlphaBoostMaterial" : Material;
    public Dictionary<string, float> ExtraFloats = new();      // NiFloatExtraData by name (FresnelBoost, ColorBoost, ...)
    public Dictionary<string, float[]> ExtraColors = new();    // NiColorExtraData by name, rgba (OverrideColor0..2, ...)
    public Dictionary<string, int> ExtraInts = new();          // NiIntegerExtraData by name (ColorOverrideMapIndex, ...)
    public string EnvMap;                 // sphere-mapped environment map (NiTextureEffect) on the mesh, or null
    public float[] EnvU, EnvV;            // its sphere-map axes in the mesh's own space (see ModelData.Load), unit xyz

    public ModelMesh Clone() => (ModelMesh)MemberwiseClone();
}

/// <summary>
/// An active, looping NiTextureTransformController: its value replaces one component of the slot's map transform,
/// Operation 0 translate U, 1 translate V, 2 rotate, 3 scale U, 4 scale V (TexTransform.Set).
/// </summary>
public sealed class UvAnim : FloatAnim
{
    public string Slot;
    public int Operation;
}

/// <summary>
/// An NiTextureTransform: the uv transform of one texture map, uv' = M (u, v, 1). The three methods build M as
/// NiTextureTransform::UpdateMatrix does (NiTextureTransform.cpp:110-250); the shader constant takes M as it is
/// (NiD3DShaderConstantMap::SetupTextureTransformMatrix). An NiTextureTransformController replaces one of the components
/// (NiTextureTransformController::UpdateValue, NiTextureTransformController.cpp:132-184).
/// </summary>
public sealed class TexTransform
{
    public const int MayaDeprecated = 0, Max = 1, Maya = 2;   // NiTextureTransform::TransformMethod (NiTextureTransform.h:24)
    public float TU, TV, SU = 1, SV = 1, Rotate, CU = 0.5f, CV = 0.5f;
    public int Method = Maya;

    public TexTransform Clone() => (TexTransform)MemberwiseClone();

    public void CopyFrom(TexTransform o)
    {
        TU = o.TU; TV = o.TV; SU = o.SU; SV = o.SV; Rotate = o.Rotate; CU = o.CU; CV = o.CV; Method = o.Method;
    }

    /// <summary>The transform a controller makes on a map that has none: translate 0, rotate 0, scale 1, centre 0.5,
    /// MAYA_TRANSFORM (NiTextureTransformController.cpp:146-151).</summary>
    public static TexTransform ControllerDefault() => new();

    /// <summary>Operation 0 translate U, 1 translate V, 2 rotate, 3 scale U, 4 scale V (TransformMember,
    /// NiTextureTransformController.h:34-38): the value replaces that component.</summary>
    public void Set(int operation, float v)
    {
        switch (operation) { case 0: TU = v; break; case 1: TV = v; break; case 2: Rotate = v; break; case 3: SU = v; break; case 4: SV = v; break; }
    }

    /// <summary>The two rows of M: u' = r[0] u + r[1] v + r[2], v' = r[3] u + r[4] v + r[5].</summary>
    public float[] Rows() { var r = new float[6]; Rows(r); return r; }

    /// <summary>The two rows of M into caller-owned storage of at least 6 floats (no allocation: the per-frame uv path).</summary>
    public void Rows(Span<float> r)
    {
        double c = Math.Cos(Rotate), s = Math.Sin(Rotate);
        double sx = SU, sy = SV, tx = TU, ty = TV, cx = CU, cy = CV;
        switch (Method)
        {
            case Max:
                r[0] = (float)(sx * c); r[1] = (float)(sx * s); r[2] = (float)(cx + sx * (c * (-cx - tx) + s * (-cy + ty)));
                r[3] = (float)(-sy * s); r[4] = (float)(sy * c); r[5] = (float)(cy + sy * (-s * (-cx - tx) + c * (-cy + ty)));
                break;
            case Maya:
                r[0] = (float)(c * sx); r[1] = (float)(-s * sy); r[2] = (float)((tx - cx) * c + (-ty - cy + 1) * s + cx);
                r[3] = (float)(-s * sx); r[4] = (float)(-c * sy); r[5] = (float)((-tx + cx) * s + (-ty - cy + 1) * c + cy);
                break;
            default:
                r[0] = (float)(c * sx); r[1] = (float)(-s * sy); r[2] = (float)((tx - cx) * c + (ty - cy) * -s + cx);
                r[3] = (float)(s * sx); r[4] = (float)(c * sy); r[5] = (float)((tx - cx) * s + (ty - cy) * c + cy);
                break;
        }
    }
}

/// <summary>One NiTexturingProperty map: the texture file, the uv set it reads (TEXCOORD_MASK), its clamp mode
/// (CLAMPMODE: 0 clamp S clamp T, 1 clamp S wrap T, 2 wrap S clamp T, 3 wrap both; NiTexturingProperty.h:140-147) and
/// its texture transform, or null. ShaderIndex is a shader map's place in the shader map array (-1 for the fixed maps).</summary>
public sealed class TexMap
{
    public string File;
    public int UvSet;
    public int Clamp = 3;
    /// <summary>The map's filter mode (NiTexturingProperty::FilterMode, its flags' bits 8-11; FILTER_BILERP when the map
    /// sets none, NiTexturingProperty::Map::Map, NiTexturingProperty.cpp:903-911): the renderer samples the map through
    /// it (NiDX9FragmentShader::SetupTextures, NiDX9FragmentShader.cpp:487; ms_akD3DFilterMapping, NiDX9Renderer.cpp:2197-2217):
    /// 0 NEAREST (point, no mip levels), 1 BILERP (linear, no mip levels), 2 TRILERP (linear between the mip levels).</summary>
    public int Filter = 1;
    public TexTransform Transform;
    public int ShaderIndex = -1;
    public bool ClampS => Clamp is 0 or 1;
    public bool ClampT => Clamp is 0 or 2;
}

/// <summary>
/// A looping NiAlphaController on the NiMaterialProperty: its NiFloatInterpolator keys, linear (KeyType 1) or Bezier
/// (KeyType 2, NiBezFloatKey with InTan / OutTan in file order). NiTimeController::ComputeScaledTime maps the time as
/// freq * t + phase, looped over Start..Stop; NiFloatKey::GenInterp holds the end values outside the keys.
/// </summary>
public sealed class AlphaAnim : FloatAnim { }

/// <summary>
/// A relative POSITION morph (NiMorphMeshModifier) under a running NiMorphWeightsController (see ModelData.ReadMorph):
/// targets 1.. are offsets from target 0 (the mesh's Positions), each weighted by its NiFloatInterpolator's keys or by
/// a fixed weight. The controller's time maps as freq * t + phase, looped over Start..Stop or, for Clamp, held in it
/// (NiTimeController::ComputeScaledTime).
/// </summary>
public sealed class MorphAnim
{
    public float Start, Stop, Frequency = 1f, Phase;
    public bool Clamp;
    public float[][] Deltas;     // per target 1..: xyz offset per vertex, the mesh's own space
    public float[] Base;         // target 0 when the mesh's Positions are not it (ModelMesh.AnyMorph of a baked morph), else null
    public float[] Static;       // per target 1..: its weight where Weights has no keys
    public FloatAnim[] Weights;  // per target 1..: its keys (KeyType, Times, Values, tangents; time fields unused), or null
}

/// <summary>A looping float controller's time mapping and NiFloatData keys (see <see cref="AlphaAnim"/>).</summary>
public class FloatAnim
{
    public float Start, Stop, Frequency = 1f, Phase;
    public int KeyType;
    public float[] Times, Values, InTans, OutTans;
}

/// <summary>
/// The MS2 conventions over a NIF scene graph: skeleton (every NiNode below the root), skinned / rigid NiMesh
/// geometry and material texture names. MS2 is Z-up; values are kept in file units and axes.
/// </summary>
public sealed class ModelData
{
    public string RootName;
    public Xform RootLocal;
    public List<ModelBone> Bones = new();
    public List<ModelMesh> Meshes = new();
    /// <summary>What the load read on an assumption the file broke, one line per mesh and case: another vertex stream whose
    /// submesh regions are not the position stream's (only the position stream's are read), or two submeshes whose vertex
    /// regions overlap under different bone palettes (the later submesh's palette is kept). Empty on every KMS2, KMS2_DEV
    /// and GMS2 model.</summary>
    public List<string> Warnings = new();

    /// <summary>
    /// Where an attached asset's self node goes when the body lacks the target node: the body's rigid mesh named
    /// <paramref name="target"/> gives the bone it hangs on and kXf, its local transform (FUN_141202280 @0x141202280,
    /// equipmodel.cpp:993). A replace asset always hangs on Bip01 Head (CKfmModel::vfunc_13 @0x14143e900, kfmmodel.cpp:548-585)
    /// with SetLocalTransform(kXf * local); a non-replace asset hangs on that mesh's own bone (vfunc_12 @0x14143e670,
    /// kfmmodel.cpp:518-544). Without such a mesh: Bip01 Head, no offset (Local null). The mesh is found by the exact name only;
    /// the client's "<part><suffix>" suffix loop (FUN_141202280) is not ported (STILL-OPEN).
    /// </summary>
    public (string Bone, Xform? Local) FindBodyAttach(string target, bool replace)
    {
        var m = Meshes.FirstOrDefault(x => x.FileName == target && x.ParentBone >= 0);
        if (m == null) return ("Bip01 Head", null);
        return (replace ? "Bip01 Head" : Bones[m.ParentBone].Name, m.Local);
    }

    public int BoneIndex(string name) => Bones.FindIndex(b => b.Name == name);

    /// <summary>
    /// FUN_141422f30 (@0x141422f30, itemugcmesh.cpp:455), the test behind CNifModel::vfunc_15 (@0x1415664f0, nifmodel.cpp:293) and
    /// CKfmModel::vfunc_15 (@0x14143ed00, kfmmodel.cpp:598), run over a parsed file's scene graph: true when some object, a node or
    /// a mesh, has no MS2CharacterSkinMaterial among ALL of its materials and its own NiTexturingProperty (GetProperty(8)) has an
    /// NiSourceTexture in shader map 0 (the first entry of the shader map array). It runs on demand over the file
    /// (<see cref="ModelTextureCache"/>); a loaded <see cref="ModelData"/> keeps neither the file nor the answer.
    /// </summary>
    public static bool HasShaderTexture(NifFile nif) => nif.Roots.Count > 0 && ScanShaderTexture(nif, nif.Roots[0]);

    /// <summary>FUN_141422f30's walk (Unk_NodeQueue breadth first from the root) over the file's blocks.</summary>
    internal static bool ScanShaderTexture(NifFile nif, int root)
    {
        var B = nif.Blocks;
        var queue = new Queue<int>(); queue.Enqueue(root);
        var seen = new HashSet<int>();
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            if (!seen.Add(i)) continue;
            var b = B[i];
            if (b.Has("Children")) foreach (var k in b.Ints("Children")) if (k >= 0) queue.Enqueue((int)k);
            bool skin = false;
            if (b.TryGetValue("Material Data", out var mdv) && mdv is NifStruct md && md.IntOr("Num Materials", 0) > 0
                && md.TryGetValue("Material Name", out var names))
            {
                foreach (var n in names switch { long[] l => l.Select(x => (object)x), object[] o => o, _ => Enumerable.Empty<object>() })
                {
                    string name = n switch
                    {
                        long l => nif.Str(l),
                        NifStruct st when st.TryGetValue("Index", out var ix) && ix is long li => nif.Str(li),
                        _ => null,
                    };
                    if (name == "MS2CharacterSkinMaterial") skin = true;
                }
            }
            if (skin) continue;
            foreach (var p in b.Has("Properties") ? b.Ints("Properties") : Array.Empty<long>())
            {
                if (p < 0 || B[(int)p].Type != "NiTexturingProperty") continue;
                var tex = B[(int)p];
                if (tex.TryGetValue("Shader Textures", out var stv) && stv is object[] sts && sts.Length > 0
                    && sts[0] is NifStruct st0 && st0.IntOr("Has Map", 0) != 0 && st0.Struct("Map").Int("Source") >= 0)
                    return true;
                break;   // GetProperty(8): the first texturing property
            }
        }
        return false;
    }

    /// <summary>
    /// A skinned mesh drawn at the file's node pose, as Gamebryo's skinning draws an unanimated skin
    /// (NiSkinningMeshModifier: vertex = sum w_j BoneWorld_j * SkinBind_j * p), in scene root space; a copy with the
    /// positions, normals and tangent frame moved. A bone outside the mesh's skin binds through World^-1 (identity at
    /// scale 0), as <c>ModelBuilder.SkinOf</c> binds it.
    /// </summary>
    public ModelMesh Posed(ModelMesh m)
    {
        var o = m.Clone();
        var mats = new Dictionary<int, Xform>();
        Xform Mat(int j)
        {
            if (mats.TryGetValue(j, out var x)) return x;
            var w = Bones[j].World;
            x = m.SkinBinds != null && m.SkinBinds.TryGetValue(j, out var b) ? w.Compose(b) : w.S == 0 ? w : Xform.Identity;
            return mats[j] = x;
        }
        int n = m.Positions.Length / 3;
        float[] Move(float[] a, bool point)
        {
            if (a == null) return null;
            var r = new float[a.Length];
            for (int v = 0; v < n; v++)
            {
                double x = 0, y = 0, z = 0;
                for (int k = 0; k < 4; k++)
                {
                    float wt = m.Weights[v * 4 + k];
                    if (wt == 0) continue;
                    var t = Mat(m.Joints[v * 4 + k]);
                    var q = point ? t.Apply(new Vec3(a[v * 3], a[v * 3 + 1], a[v * 3 + 2]))
                                  : (t with { T = new Vec3(0, 0, 0) }).Apply(new Vec3(a[v * 3], a[v * 3 + 1], a[v * 3 + 2]));
                    x += wt * q.X; y += wt * q.Y; z += wt * q.Z;
                }
                if (!point)
                {
                    double l = Math.Sqrt(x * x + y * y + z * z);
                    if (l > 0) { x /= l; y /= l; z /= l; }
                }
                r[v * 3] = (float)x; r[v * 3 + 1] = (float)y; r[v * 3 + 2] = (float)z;
            }
            return r;
        }
        o.Positions = Move(m.Positions, true);
        o.Normals = Move(m.Normals, false);
        o.Tangents = Move(m.Tangents, false);
        o.Binormals = Move(m.Binormals, false);
        return o;
    }

    /// <summary>An NiMesh's stored "Bounding Sphere" (NiBound: Center, Radius), the model bound Gamebryo keeps for it.</summary>
    static float[] BoundOf(NifStruct b)
    {
        if (!b.TryGetValue("Bounding Sphere", out var v) || v is not NifStruct bs || !bs.TryGetValue("Center", out var cv) || cv is not NifStruct c) return null;
        return new[] { c.Float("x"), c.Float("y"), c.Float("z"), bs.Float("Radius") };
    }

    static Xform XformOf(NifStruct b)
    {
        var t = b.Struct("Translation");
        var r = b.Struct("Rotation");
        // the 9 floats in file order are the matrix row-major for column vectors (nif.xml names them m11 m21 ...)
        var R = r.Values.Select(v => (double)(float)v).ToArray();
        return new Xform(new Vec3(t.Float("x"), t.Float("y"), t.Float("z")), R, b.Float("Scale"));
    }

    static int Code(uint f) => (int)(f & 0xff);

    /// <summary>Byte size of one element of a component format code (n = (f >> 16) &amp; 0xff elements).</summary>
    static int ElemSize(int code) => code switch
    {
        >= 0x01 and <= 0x08 => 1, >= 0x0D and <= 0x10 => 1,
        >= 0x11 and <= 0x18 => 2, >= 0x1D and <= 0x20 => 2,
        >= 0x21 and <= 0x28 => 4,
        >= 0x31 and <= 0x34 => 2, >= 0x35 and <= 0x38 => 4,
        _ => throw new NotSupportedException($"component format 0x{code:x}"),
    };

    static double ReadElem(byte[] d, int o, int code) => code switch
    {
        >= 0x01 and <= 0x04 => (sbyte)d[o],
        (>= 0x05 and <= 0x08) or (>= 0x0D and <= 0x10) => d[o],
        >= 0x11 and <= 0x14 => BitConverter.ToInt16(d, o),
        (>= 0x15 and <= 0x18) or (>= 0x1D and <= 0x20) => BitConverter.ToUInt16(d, o),
        >= 0x21 and <= 0x24 => BitConverter.ToInt32(d, o),
        >= 0x25 and <= 0x28 => BitConverter.ToUInt32(d, o),
        >= 0x31 and <= 0x34 => (double)BitConverter.ToHalf(d, o),
        >= 0x35 and <= 0x38 => BitConverter.ToSingle(d, o),
        _ => throw new NotSupportedException($"component format 0x{code:x}"),
    };

    /// <summary>Decodes a NiMesh's data streams: semantic (with index suffix when &gt; 0) -> (values, width).</summary>
    public static Dictionary<string, (double[] v, int n)> MeshStreams(NifFile nif, int mi)
    {
        var B = nif.Blocks;
        var o = new Dictionary<string, (double[], int)>();
        foreach (var r in B[mi].Structs("Datastreams"))
        {
            var ds = B[(int)r.Int("Stream")];
            var data = ds.Struct("Data").Bytes("Data");
            var fmts = ds.Ints("Component Formats").Select(x => (uint)x).ToArray();
            var sems = r.Structs("Component Semantics");
            int rec = 0;
            var comps = new List<(string key, int code, int n, int off)>();
            for (int k = 0; k < fmts.Length; k++)
            {
                int code = Code(fmts[k]), n = (int)((fmts[k] >> 16) & 0xff), size = (int)((fmts[k] >> 8) & 0xff);
                if (ElemSize(code) * n != size && ElemSize(code) != size) throw new InvalidDataException($"format 0x{fmts[k]:x}");
                var nm = nif.StrField(sems[k], "Name");
                long idx = sems[k].Int("Index");
                comps.Add((idx == 0 ? nm : nm + idx, code, n, rec));
                rec += ElemSize(code) * n;
            }
            int count = rec == 0 ? 0 : data.Length / rec;
            foreach (var c in comps)
            {
                var a = new double[count * c.n];
                int es = ElemSize(c.code);
                for (int i = 0; i < count; i++)
                    for (int j = 0; j < c.n; j++)
                        a[i * c.n + j] = ReadElem(data, i * rec + c.off + j * es, c.code);
                o[c.key] = (a, c.n);
            }
        }
        return o;
    }

    /// <summary>
    /// Each of the NiMesh's <paramref name="submeshes"/> regions (first element, count) in its stream holding
    /// <paramref name="semantic"/> (index 0): NiDataStreamRef::GetRegionForSubmesh, the stream ref's "Submesh To Region Map"
    /// picking one of the stream's "Regions". Null when the stream is missing, has no map or no regions, or a region is missing
    /// or outside the stream's <paramref name="total"/> elements: the stream is then taken whole.
    /// </summary>
    public static (int Start, int Count)[] SubmeshRegions(NifFile nif, int mesh, string semantic, int submeshes, int total)
    {
        if (submeshes < 1) return null;
        foreach (var r in nif.Blocks[mesh].Structs("Datastreams"))
        {
            bool has = false;
            foreach (var cs in r.Structs("Component Semantics"))
                if (cs.IntOr("Index", 0) == 0 && nif.StrField(cs, "Name") == semantic) { has = true; break; }
            if (!has) continue;
            var map = r.Ints("Submesh To Region Map");
            var regions = nif.Blocks[(int)r.Int("Stream")].Structs("Regions");
            if (map.Length < submeshes || regions.Length == 0) return null;
            var o = new (int, int)[submeshes];
            for (int sm = 0; sm < submeshes; sm++)
            {
                if (map[sm] < 0 || map[sm] >= regions.Length) return null;
                long start = regions[(int)map[sm]].Int("Start Index"), count = regions[(int)map[sm]].Int("Num Indices");
                if (start < 0 || count < 0 || start + count > total) return null;
                o[sm] = ((int)start, (int)count);
            }
            return o;
        }
        return null;
    }

    /// <summary>The first per-vertex stream of the mesh (one element per vertex, <paramref name="vertexCount"/> in all; so not
    /// INDEX, BONE_PALETTE or MORPHWEIGHTS) whose submesh regions are not <paramref name="vertexRegions"/>, by its first
    /// semantic, or null when they all match.</summary>
    static string OtherVertexRegions(NifFile nif, int mesh, (int Start, int Count)[] vertexRegions, int vertexCount)
    {
        foreach (var r in nif.Blocks[mesh].Structs("Datastreams"))
        {
            var names = r.Structs("Component Semantics").Select(cs => nif.StrField(cs, "Name")).ToArray();
            if (names.Contains("INDEX") || names.Contains("BONE_PALETTE")) continue;
            var stream = nif.Blocks[(int)r.Int("Stream")];
            long record = stream.Ints("Component Formats").Sum(f => (long)ElemSize(Code((uint)f)) * ((f >> 16) & 0xff));   // as MeshStreams
            if (record <= 0 || stream.Struct("Data").Bytes("Data").Length / record != vertexCount) continue;
            var map = r.Ints("Submesh To Region Map");
            var regions = stream.Structs("Regions");
            for (int sm = 0; sm < vertexRegions.Length; sm++)
            {
                string name = names.Length > 0 ? names[0] : "?";
                if (sm >= map.Length || map[sm] < 0 || map[sm] >= regions.Length) return name;
                var g = regions[(int)map[sm]];
                if (g.Int("Start Index") != vertexRegions[sm].Start || g.Int("Num Indices") != vertexRegions[sm].Count) return name;
            }
        }
        return null;
    }

    /// <summary>
    /// The triangle list the client draws: submesh by submesh, the INDEX region's values counted from the submesh's first vertex.
    /// NiD3DShader::RenderMeshes draws each submesh with DrawIndexedPrimitive(BaseVertexIndex 0, StartIndex = the index region's
    /// start) after PreRenderSubmesh set every vertex stream at its region's start (SetStreamSource offset = region start *
    /// stride, NiD3DShader.cpp:245-289, 526-566), so an index is relative to that region. Without regions the stream is taken
    /// whole, as before; a strip without vertex regions that match its index regions still takes each index region as its own
    /// strip, counted from vertex 0, so no triangle bridges two submeshes.
    /// With <paramref name="strips"/> (Primitive Type 1, PRIMITIVE_TRISTRIPS) each submesh's region is one strip, drawn as
    /// D3DPT_TRIANGLESTRIP (NiDX9Renderer::GetDX9PrimitiveFromNiMeshPrimitiveType, NiDX9Renderer.cpp:4857) with
    /// count - 2 triangles (NiMesh::GetPrimitiveCountFromElementCount, NiMesh.cpp:312). It is unrolled as the engine's
    /// NiMeshAlgorithms::ForEachIndexedTriStrip walks it (NiMeshAlgorithms.inl:533-575): triangle t is (s[t], s[t+1], s[t+2]),
    /// the first two swapped on odd t to keep the winding. A triangle with a repeated vertex has no area and D3D draws nothing
    /// for it, so it is left out.
    /// </summary>
    public static int[] SubmeshIndices(double[] index, (int Start, int Count)[] indexRegions, (int Start, int Count)[] vertexRegions, bool strips = false)
    {
        if (indexRegions == null || vertexRegions == null || indexRegions.Length != vertexRegions.Length)
        {
            if (!strips) return index.Select(x => (int)x).ToArray();
            indexRegions ??= new[] { (0, index.Length) };
            vertexRegions = new (int, int)[indexRegions.Length];
        }
        if (!strips)
        {
            var o = new int[indexRegions.Sum(r => r.Count)];
            int at = 0;
            for (int sm = 0; sm < indexRegions.Length; sm++)
                for (int q = indexRegions[sm].Start; q < indexRegions[sm].Start + indexRegions[sm].Count; q++)
                    o[at++] = (int)index[q] + vertexRegions[sm].Start;
            return o;
        }
        var t = new List<int>(indexRegions.Sum(r => Math.Max(0, r.Count - 2)) * 3);
        for (int sm = 0; sm < indexRegions.Length; sm++)
        {
            int s0 = indexRegions[sm].Start, vs = vertexRegions[sm].Start;
            for (int q = 0; q + 2 < indexRegions[sm].Count; q++)
            {
                int odd = q & 1;
                int a = (int)index[s0 + q + odd] + vs, b = (int)index[s0 + q + 1 - odd] + vs, c = (int)index[s0 + q + 2] + vs;
                if (a == b || b == c || a == c) continue;
                t.Add(a); t.Add(b); t.Add(c);
            }
        }
        return t.ToArray();
    }

    internal static Dictionary<string, string> TexturesOf(NifFile nif, int mi)
    {
        var B = nif.Blocks;
        var o = new Dictionary<string, string>();
        foreach (var p in B[mi].Ints("Properties"))
        {
            if (p < 0 || B[(int)p].Type != "NiTexturingProperty") continue;
            var b = B[(int)p];
            foreach (var (key, slot) in new[] { ("base", "Base Texture"), ("gloss", "Gloss Texture"), ("normal", "Normal Texture"),
                                                ("glow", "Glow Texture"), ("dark", "Dark Texture"), ("detail", "Detail Texture"),
                                                ("decal0", "Decal 0 Texture") })
            {
                if (b.IntOr("Has " + slot, 0) == 0 || !b.Has(slot)) continue;
                long src = b.Struct(slot).Int("Source");
                if (src >= 0) o[key] = nif.StrField(B[(int)src], "File Name");
            }
            if (b.TryGetValue("Shader Textures", out var stv) && stv is object[] sts)
                foreach (NifStruct st in sts)
                {
                    if (st.IntOr("Has Map", 0) == 0) continue;
                    long src = st.Struct("Map").Int("Source");
                    if (src >= 0) o["shader" + st.Int("Map ID")] = nif.StrField(B[(int)src], "File Name");
                }
        }
        return o;
    }

    /// <summary>The maps of the mesh's NiTexturingProperty, keyed as TexturesOf: file, uv set, clamp mode, filter mode, static transform.</summary>
    internal static Dictionary<string, TexMap> MapsOf(NifFile nif, int mi)
    {
        var B = nif.Blocks;
        var o = new Dictionary<string, TexMap>();
        TexMap Map(NifStruct d)
        {
            long src = d.Int("Source"), flags = d.IntOr("Flags", 0x3100);
            var m = new TexMap { File = src >= 0 ? nif.StrField(B[(int)src], "File Name") : null, UvSet = (int)(flags & 0xFF), Clamp = (int)((flags >> 12) & 3), Filter = (int)((flags >> 8) & 0xF) };
            if (d.IntOr("Has Texture Transform", 0) != 0)
            {
                var tr = d.Struct("Translation"); var sc = d.Struct("Scale"); var ce = d.Struct("Center");
                m.Transform = new TexTransform
                {
                    TU = tr.Float("u"), TV = tr.Float("v"), SU = sc.Float("u"), SV = sc.Float("v"), Rotate = d.Float("Rotation"),
                    Method = (int)d.IntOr("Transform Method", TexTransform.Maya), CU = ce.Float("u"), CV = ce.Float("v"),
                };
            }
            return m;
        }
        foreach (var p in B[mi].Ints("Properties"))
        {
            if (p < 0 || B[(int)p].Type != "NiTexturingProperty") continue;
            var b = B[(int)p];
            for (int k = 0; k < MapSlots.Length; k++)
            {
                var (key, field) = MapSlots[k];
                if (key == null || b.IntOr("Has " + field, 0) == 0 || !b.Has(field)) continue;
                o[key] = Map(b.Struct(field));
            }
            if (b.TryGetValue("Shader Textures", out var stv) && stv is object[] sts)
                for (int k = 0; k < sts.Length; k++)
                {
                    var st = (NifStruct)sts[k];
                    if (st.IntOr("Has Map", 0) == 0) continue;
                    var m = Map(st.Struct("Map")); m.ShaderIndex = k;
                    o["shader" + st.Int("Map ID")] = m;
                }
        }
        return o;
    }

    /// <summary>The fixed maps in NiTexturingProperty::MapEnum order (NiTexturingProperty.h:332): key and nif.xml field.</summary>
    static readonly (string key, string field)[] MapSlots =
    {
        ("base", "Base Texture"), ("dark", "Dark Texture"), ("detail", "Detail Texture"), ("gloss", "Gloss Texture"),
        ("glow", "Glow Texture"), ("bump", "Bump Map Texture"), ("normal", "Normal Texture"), ("parallax", "Parallax Texture"),
        ("decal0", "Decal 0 Texture"),
    };

    /// <summary>NiNode and the node subclasses that only change how children are drawn (sorting, billboarding, switching).</summary>
    static bool IsNode(string type) => type is "NiNode" or "NiSortAdjustNode" or "NiBillboardNode" or "NiSwitchNode" or "NiLODNode";

    static readonly string[] SlotNames = { "base", "dark", "detail", "gloss", "glow", "bump", "normal", "parallax", "decal0" };

    /// <summary>
    /// The flipbook (NiFlipController) and uv (NiTextureTransformController) controllers on the mesh's NiTexturingProperty.
    /// Only active, looping controllers; time keys of the flipbook are taken as evenly spaced. An active CLAMP uv controller
    /// goes to UvHolds.
    /// </summary>
    static void ReadTextureControllers(NifFile nif, NifStruct mb, ModelMesh mesh)
    {
        var B = nif.Blocks;
        foreach (var p in mb.Ints("Properties"))
        {
            if (p < 0 || B[(int)p].Type != "NiTexturingProperty") continue;
            for (long c = B[(int)p].IntOr("Controller", -1), guard = 0; c >= 0 && guard < 16; guard++)
            {
                var cb = B[(int)c];
                c = cb.IntOr("Next Controller", -1);
                // ChaosLightEffect::AddLayers: any NiTextureTransformController on the glow map, active or not
                if (cb.Type == "NiTextureTransformController" && cb.IntOr("Shader Map", 0) == 0 && cb.IntOr("Texture Slot", -1) == 4)
                    mesh.GlowMapDriven = true;
                long flags = cb.IntOr("Flags", 0);
                int cycle = (int)((flags >> 1) & 3);
                if ((flags & 8) == 0 || cycle is not (0 or 2)) continue;   // inactive, or REVERSE
                if (cycle == 2 && cb.Type != "NiTextureTransformController") continue;
                int slot = (int)cb.IntOr("Texture Slot", 0);
                string slotName = slot < SlotNames.Length ? SlotNames[slot] : null;
                float start = cb.Float("Start Time"), stop = cb.Float("Stop Time");
                if (slotName == null || stop <= start) continue;
                if (cb.Type == "NiFlipController")
                {
                    var frames = cb.Ints("Sources").Where(x => x >= 0).Select(x => nif.StrField(B[(int)x], "File Name")).ToArray();
                    if (frames.Length < 2) continue;
                    mesh.FlipSlot = slotName;
                    mesh.FlipFrames = frames;
                    mesh.FlipPeriod = stop - start;
                }
                else if (cb.Type == "NiTextureTransformController")
                {
                    long op = cb.IntOr("Operation", -1);
                    // Texture Slot of a Shader Map controller indexes the shader maps, not base/dark/glow/decal
                    if (cb.IntOr("Shader Map", 0) != 0) continue;
                    if (op is < 0 or > 4 || ReadFloatAnim<UvAnim>(nif, cb, clamp: cycle == 2) is not { } ua) continue;
                    ua.Slot = slotName;
                    ua.Operation = (int)op;
                    (cycle == 2 ? mesh.UvHolds : mesh.UvAnims).Add(ua);
                }
            }
        }
    }

    /// <summary>
    /// The NiAlphaController on an NiMaterialProperty: only an active, looping one (flags bit 3, cycle LOOP) with an
    /// NiFloatInterpolator over linear or Bezier keys; anything else leaves the static alpha.
    /// </summary>
    static AlphaAnim ReadAlphaController(NifFile nif, NifStruct prop)
    {
        var B = nif.Blocks;
        for (long c = prop.IntOr("Controller", -1), guard = 0; c >= 0 && guard < 16; guard++)
        {
            var cb = B[(int)c];
            c = cb.IntOr("Next Controller", -1);
            if (cb.Type == "NiAlphaController" && ReadFloatAnim<AlphaAnim>(nif, cb) is { } aa) return aa;
        }
        return null;
    }

    /// <summary>
    /// An active, looping (flags bit 3, cycle LOOP) float controller with an NiFloatInterpolator over linear or Bezier
    /// keys, or null.
    /// </summary>
    static T ReadFloatAnim<T>(NifFile nif, NifStruct cb, bool clamp = false) where T : FloatAnim, new()
    {
        var B = nif.Blocks;
        long flags = cb.IntOr("Flags", 0);
        if ((flags & 8) == 0 || ((flags >> 1) & 3) != (clamp ? 2 : 0)) return null;   // inactive, or not a loop (a clamp when asked)
        float start = cb.Float("Start Time"), stop = cb.Float("Stop Time");
        long ip = cb.IntOr("Interpolator", -1);
        if (stop <= start || ip < 0 || B[(int)ip].Type != "NiFloatInterpolator") return null;
        long di = B[(int)ip].IntOr("Data", -1);
        if (di < 0) return null;
        var data = B[(int)di].Struct("Data");
        var keys = data.Structs("Keys");
        int type = (int)data.IntOr("Interpolation", 0);
        if (keys.Length == 0 || type is not (1 or 2)) return null;
        return new T
        {
            Start = start, Stop = stop, Frequency = cb.Float("Frequency"), Phase = cb.Float("Phase"), KeyType = type,
            Times = keys.Select(k => k.Float("Time")).ToArray(),
            Values = keys.Select(k => k.Float("Value")).ToArray(),
            InTans = type == 2 ? keys.Select(k => k.Float("Forward")).ToArray() : null,
            OutTans = type == 2 ? keys.Select(k => k.Float("Backward")).ToArray() : null,
        };
    }

    /// <summary>
    /// An NiMorphMeshModifier with relative targets (FLAG_RELATIVETARGETS) on POSITION alone, as every morph in the field
    /// data, and the NiMorphWeightsController on the mesh. The modifier rewrites POSITION as target 0 (MORPH_POSITION)
    /// plus each weighted target (NiMorphingKernel.cpp:30-40, weights under 0.001 skipped) on its first update, so the
    /// stored POSITION is not what is drawn: Positions becomes target 0. A controller that does not run (inactive,
    /// MANAGERCONTROLLED, a cycle other than LOOP or CLAMP) leaves the stored MORPHWEIGHTS, as does a target whose
    /// NiFloatInterpolator does not update (no data and the invalid value -FLT_MAX; NiMorphWeightsController::Update
    /// keeps the weight when Update fails); a target with no data and a valid value takes that value. Morph is set when
    /// a target has linear or Bezier keys; otherwise the fixed weights are baked into Positions.
    /// More than 4 targets or 32 keys a target (none in the field data; some effect meshes, EffectMorphSweepTests) do not
    /// fit the field shader: Positions get the fixed weights baked, and AnyMorph keeps the morph with target 0 in Base.
    /// STILL-OPEN: absolute targets, morphed normals (FLAG_UPDATENORMALS), skinned morphs, TCB or other key types: none
    /// in the field or effect data (EffectMorphSweepTests); they keep the stored POSITION.
    /// </summary>
    static void ReadMorph(NifFile nif, NifStruct mb, Dictionary<string, (double[] v, int n)> s, bool skinned, ModelMesh mesh)
    {
        var B = nif.Blocks;
        NifStruct mod = null;
        foreach (var m in mb.Ints("Modifiers"))
            if (m >= 0 && B[(int)m].Type == "NiMorphMeshModifier") { mod = B[(int)m]; break; }
        if (mod == null || skinned) return;
        long flags = mod.IntOr("Flags", 0);
        int nt = (int)mod.IntOr("Num Targets", 0);
        var el = mod.Structs("Elements");
        if ((flags & 1) == 0 || (flags & 2) != 0 || nt < 2 || el.Length != 1) return;
        var sem = el[0].Struct("Semantic");
        if (nif.StrField(sem, "Name") != "POSITION" || sem.Int("Index") != 0) return;
        int nv = mesh.Positions.Length / 3;
        var targets = new double[nt][];
        for (int t = 0; t < nt; t++)
        {
            if (!s.TryGetValue(t == 0 ? "MORPH_POSITION" : "MORPH_POSITION" + t, out var ts) || ts.n != 3 || ts.v.Length != nv * 3) return;
            targets[t] = ts.v;
        }
        if (!s.TryGetValue("MORPHWEIGHTS", out var ws) || ws.v.Length < nt) return;
        var anim = new MorphAnim { Deltas = new float[nt - 1][], Static = new float[nt - 1], Weights = new FloatAnim[nt - 1] };
        for (int t = 1; t < nt; t++) anim.Static[t - 1] = (float)ws.v[t];
        for (long c = mb.IntOr("Controller", -1), guard = 0; c >= 0 && guard < 16; guard++)
        {
            var cb = B[(int)c];
            c = cb.IntOr("Next Controller", -1);
            if (cb.Type != "NiMorphWeightsController") continue;
            long cf = cb.IntOr("Flags", 0);
            int cycle = (int)((cf >> 1) & 3);
            float start = cb.Float("Start Time"), stop = cb.Float("Stop Time");
            // ACTIVE (0x8) and not MANAGERCONTROLLED (0x20), LOOP or CLAMP
            if ((cf & 8) == 0 || (cf & 0x20) != 0 || cycle is not (0 or 2) || stop < start) break;
            (anim.Start, anim.Stop, anim.Frequency, anim.Phase, anim.Clamp) = (start, stop, cb.Float("Frequency"), cb.Float("Phase"), cycle == 2);
            var ips = cb.Ints("Interpolators");
            for (int t = 1; t < nt && t < ips.Length; t++)
            {
                if (ips[t] < 0 || B[(int)ips[t]].Type != "NiFloatInterpolator") continue;
                var ib = B[(int)ips[t]];
                if (FloatKeys(nif, ib) is { } k) anim.Weights[t - 1] = k;
                else if (ib.IntOr("Data", -1) < 0 && ib.Float("Value") != -float.MaxValue) anim.Static[t - 1] = ib.Float("Value");
            }
            break;
        }
        bool keyed = anim.Weights.Any(w => w != null);
        bool gpu = keyed && nt <= 4 && anim.Weights.All(w => w == null || w.Times.Length <= 32);
        var pos = new float[nv * 3];
        for (int q = 0; q < pos.Length; q++)
        {
            double v = targets[0][q];
            if (!gpu)
                for (int t = 1; t < nt; t++)
                    if (Math.Abs(anim.Static[t - 1]) >= 0.001f) v += anim.Static[t - 1] * targets[t][q];
            pos[q] = (float)v;
        }
        mesh.Positions = pos;
        if (!keyed) return;
        for (int t = 1; t < nt; t++) anim.Deltas[t - 1] = targets[t].Select(x => (float)x).ToArray();
        if (!gpu) anim.Base = targets[0].Select(x => (float)x).ToArray();
        else mesh.Morph = anim;
        mesh.AnyMorph = anim;
    }

    /// <summary>An NiFloatInterpolator's linear (1) or Bezier (2) keys as in ReadFloatAnim, or null.</summary>
    static FloatAnim FloatKeys(NifFile nif, NifStruct ip)
    {
        long di = ip.IntOr("Data", -1);
        if (di < 0) return null;
        var data = nif.Blocks[(int)di].Struct("Data");
        var keys = data.Structs("Keys");
        int type = (int)data.IntOr("Interpolation", 0);
        if (keys.Length == 0 || type is not (1 or 2)) return null;
        return new FloatAnim
        {
            KeyType = type,
            Times = keys.Select(k => k.Float("Time")).ToArray(),
            Values = keys.Select(k => k.Float("Value")).ToArray(),
            InTans = type == 2 ? keys.Select(k => k.Float("Forward")).ToArray() : null,
            OutTans = type == 2 ? keys.Select(k => k.Float("Backward")).ToArray() : null,
        };
    }

    /// <summary>The active material name of a render object block (its "Material Data"), or null when it names none. An
    /// active index past the names (0xFFFFFFFF is NO_MATERIAL) is no material: GetActiveMaterial returns null
    /// (NiRenderObject.inl:90-95) and the renderer draws the block with its default material
    /// (NiDX9Renderer::GetShaderAndVertexDecl_NoErrorShader, NiDX9Renderer.cpp:5004-5006).</summary>
    internal static string MaterialName(NifFile nif, NifStruct b)
    {
        if (!(b.TryGetValue("Material Data", out var mdv) && mdv is NifStruct md && md.IntOr("Num Materials", 0) > 0)) return null;
        long active = md.IntOr("Active Material", -1);
        var names = md.TryGetValue("Material Name", out var nv) ? nv : null;
        object pick = names switch
        {
            long[] l when active >= 0 && active < l.Length => l[active],
            object[] o when active >= 0 && active < o.Length => o[active],
            _ => null,
        };
        return pick switch
        {
            long l => nif.Str(l),
            NifStruct st when st.TryGetValue("Index", out var ix) && ix is long li => nif.Str(li),
            _ => null,
        };
    }

    /// <summary>The material names MS2MaterialLibrary::CreateMaterial builds as an MS2StandardMaterial
    /// (ms2materiallibrary.cpp:486-500). A render object gets its material by the name it stores
    /// (NiRenderObject::LoadBinary, NiRenderObject.cpp:385-418, through ApplyMaterial and NiMaterialLibrary::CreateMaterial,
    /// :77-86).</summary>
    public static readonly string[] StandardMaterialNames =
    {
        "MS2StandardMaterial", "MS2CharacterMaterial", "MS2CharacterSkinMaterial", "MS2CharacterHairMaterial",
        "MS2AlphaBoostMaterial", "MS2ChaosMaterial", "MS2ChaosSkinMaterial", "MS2ChaosHairMaterial",
    };

    /// <summary>The name is one the client builds as an MS2StandardMaterial (<see cref="StandardMaterialNames"/>).</summary>
    public static bool IsStandardMaterial(string name) => name != null && Array.IndexOf(StandardMaterialNames, name) >= 0;

    /// <summary>The material type MS2StandardMaterial::Init (@0x142754010, ms2standardmaterial.cpp:1816-1851) gives a name it
    /// builds: 0 for MS2StandardMaterial and MS2AlphaBoostMaterial, 1-3 for the character and chaos materials (GetMaterialType
    /// @0x14274ee10, :465-476); the character light (m_bCharacterLighting, :1850) is types 1-3. -1: a name not built as an
    /// MS2StandardMaterial (<see cref="StandardMaterialNames"/>).</summary>
    public static int StandardMaterialType(string name) => name switch
    {
        "MS2StandardMaterial" or "MS2AlphaBoostMaterial" => 0,
        "MS2CharacterMaterial" or "MS2ChaosMaterial" => 1,
        "MS2CharacterSkinMaterial" or "MS2ChaosSkinMaterial" => 2,
        "MS2CharacterHairMaterial" or "MS2ChaosHairMaterial" => 3,
        _ => -1,
    };

    /// <summary>A material of type 0 (<see cref="StandardMaterialType"/>): HandleMS2Light (ms2standardmaterial.cpp:858-1048)
    /// lights it with the "MS2Light" node and the lights' own directions, as the field's meshes, not with the character
    /// light, whatever model the mesh hangs under.</summary>
    public static bool IsStandardLightMaterial(string name) => StandardMaterialType(name) == 0;

    /// <summary>The character light: types 1-3 (<see cref="LightForm"/>).</summary>
    public const int CharacterLightForm = 0;
    /// <summary>The "MS2Light" node of type 0 (<see cref="LightForm"/>).</summary>
    public const int MS2LightForm = 1;
    /// <summary>NiStandardMaterial's own light fragment (<see cref="LightForm"/>).</summary>
    public const int NiStandardLightForm = 2;

    /// <summary>How a character-path mesh is lit by the material it draws with (<see cref="ModelMesh.ActiveMaterial"/>):
    /// types 1-3 by the character light, type 0 by MS2Light (<see cref="IsStandardLightMaterial"/>), and a mesh that names
    /// no material by the renderer's default material, an NiStandardMaterial (NiRenderer::NiRenderer,
    /// NiRenderer.cpp:49-54: m_spInitialDefaultMaterial; NiRenderObject::LoadBinary, NiRenderObject.cpp:389-412, applies
    /// only the names the file stores). STILL-OPEN: a name no library builds (not one of
    /// <see cref="StandardMaterialNames"/>) keeps the character light here.</summary>
    public static int LightForm(string material) =>
        material == null ? NiStandardLightForm : IsStandardLightMaterial(material) ? MS2LightForm : CharacterLightForm;

    /// <summary>The z-buffer and stencil state of the render object (a mesh or a particle system) at block
    /// <paramref name="block"/>: the nearest NiZBufferProperty and NiStencilProperty up the scene graph
    /// (NiNode::UpdateProperties pushes each node's properties over its parent's; <see cref="ModelMesh.ZFlags"/>,
    /// <see cref="ModelMesh.DrawMode"/>), and whether an NiSortAdjustNode above it turns sorting off
    /// (<see cref="ModelMesh.NoSort"/>). <paramref name="parent"/> maps a block to the node whose Children hold it.</summary>
    public static void ReadDrawState(NifFile nif, IReadOnlyDictionary<int, int> parent, int block, ModelMesh mesh)
    {
        var B = nif.Blocks;
        bool zSet = false, stSet = false;
        for (int n = block, guard = 0; n >= 0 && guard < 256; n = parent.TryGetValue(n, out int up) ? up : -1, guard++)
        {
            int z = -1, st = -1;
            if (B[n].Type == "NiSortAdjustNode") mesh.NoSort = B[n].IntOr("Sorting Mode", 0) == 1;   // SORTING_OFF
            if (!B[n].TryGetValue("Properties", out var pv) || pv is not long[] props) continue;
            foreach (var p in props)
            {
                if (p < 0) continue;
                if (B[(int)p].Type == "NiZBufferProperty") z = (int)p;
                else if (B[(int)p].Type == "NiStencilProperty") st = (int)p;
            }
            if (z >= 0 && !zSet) { mesh.ZFlags = (int)B[z].Int("Flags"); zSet = true; }
            if (st >= 0 && !stSet) { mesh.DrawMode = ((int)B[st].Int("Flags") >> 10) & 3; stSet = true; }
        }
    }

    /// <summary>The client gives the render object (a mesh or a particle system) at block <paramref name="block"/>
    /// MS2AlphaBoostMaterial when it loads, whatever material it names: its own NiMaterialProperty has an alpha above 1, or
    /// that property's first controller is an NiAlphaController whose key-based interpolator has a float key above 1.
    /// The controller's flags, times and key type don't matter (PrepareLoadedNif's render setup walk, @0x141567b60,
    /// unit_141567000.cpp:349-362, calls @0x141569280, :853-865, which calls ApplyAndSetActiveMaterial; HasAlphaKeyAboveOne,
    /// @0x141569170, :828-849).</summary>
    public static bool AlphaBoost(NifFile nif, int block)
    {
        var B = nif.Blocks;
        foreach (var p in B[block].Ints("Properties"))
        {
            if (p < 0 || B[(int)p].Type != "NiMaterialProperty") continue;
            var prop = B[(int)p];
            if (prop.Float("Alpha") > 1f) return true;
            long c = prop.IntOr("Controller", -1);
            if (c < 0 || B[(int)c].Type != "NiAlphaController") return false;
            long ip = B[(int)c].IntOr("Interpolator", -1);
            if (ip < 0 || B[(int)ip].Type != "NiFloatInterpolator") return false;
            long di = B[(int)ip].IntOr("Data", -1);
            if (di < 0) return false;
            foreach (var k in B[(int)di].Struct("Data").Structs("Keys"))
                if (k.Float("Value") > 1f) return true;
            return false;
        }
        return false;
    }

    /// <summary>The active material name and the float / colour extra data that carry its shader attributes.</summary>
    static void ReadMaterial(NifFile nif, NifStruct b, ModelMesh mesh)
    {
        mesh.Material = MaterialName(nif, b);
        foreach (var e in b.Ints("Extra Data List"))
        {
            if (e < 0) continue;
            var x = nif.Blocks[(int)e];
            var name = nif.Name((int)e);
            if (name == null) continue;
            if (x.Type == "NiFloatExtraData") mesh.ExtraFloats[name] = x.Float("Float Data");
            else if (x.Type == "NiIntegerExtraData") mesh.ExtraInts[name] = (int)x.Int("Integer Data");
            else if (x.Type == "NiColorExtraData")
            {
                var c = x.Struct("Data");
                mesh.ExtraColors[name] = new[] { c.Float("r"), c.Float("g"), c.Float("b"), c.Float("a") };
            }
        }
    }

    /// <summary>
    /// The block's UserPropBuffer string (NiStringExtraData named "UserPropBuffer", DAT_143ae51b8, named
    /// in worldfadeoutrenderstep.cpp:274) contains "NoShadow", case ignored. SetShadowCaster and
    /// FUN_141567540 (unit_141567000.cpp:186-262) walk the model breadth first and skip such a node without entering it, so
    /// no render object under it gets the caster mark (the flag 0x20 or the caster extra data). Casting is opt-in: the
    /// shadow visitor only draws marked objects (MS2_IS_SHADOW_CASTER, ms2shadowclickgenerator.cpp:103-108), and a loaded
    /// NIF has none, so the subtree casts nothing. The walk also skips nodes with NiAVObject flag 0x400 (IS_PARTICLE_SYSTEM,
    /// NiAVObject.h:267, set by the NiPSParticleSystem constructors); ModelData loads only NiMesh, so none of those is drawn.
    /// </summary>
    public static bool TaggedNoShadow(NifFile nif, int block) =>
        UserProp(nif, block) is { } v && v.Contains("NoShadow", StringComparison.OrdinalIgnoreCase);

    /// <summary>The block's UserPropBuffer string, or null. NiObjectNET::GetExtraData returns the first entry of the name.</summary>
    static string UserProp(NifFile nif, int block)
    {
        foreach (var e in nif.Blocks[block].Ints("Extra Data List"))
        {
            if (e < 0) continue;
            var x = nif.Blocks[(int)e];
            if (x.Type != "NiStringExtraData" || nif.Name((int)e) != "UserPropBuffer") continue;
            return nif.StrField(x, "String Data");
        }
        return null;
    }

    /// <summary>
    /// The client's render setup of a loaded model (FUN_141567b60 @0x141567b60, unit_141567000.cpp:346-390, run first by
    /// PrepareLoadedNif @0x1415672e0 :172-181) walks it and gives a fog-off NiFogProperty to every object that has a
    /// UserPropBuffer containing "nofog" (case ignored), has NiAVObject flag 0x400 (FUN_14098dfc0, alphaanimation.cpp:391-395),
    /// or has its own NiAlphaProperty blending with destination ONE (FUN_1415671f0 @0x1415671f0, :147-156: flags bit 0 and
    /// dest bits 5-8 = 0). FUN_141569620 (:900-915) drops the fog properties of that subtree and attaches the new one at its
    /// root, so the object and everything under it draw with FOG_NONE (NiStandardMaterialDescriptor.cpp:625-650), FOGENABLED 0
    /// (NiStandardMaterial.cpp:1077), no depth fog. A mesh is fog-off when it or a node above it matches. The height fog is
    /// switched by gbHeightFogEnable and the mesh's own blend (NiStandardMaterial.cpp:687-697), not by this.
    /// Flag 0x400 is IS_PARTICLE_SYSTEM (NiAVObject.h:267): NiPSParticleSystem::LoadBinary sets it after reading the stored
    /// flags (NiPSParticleSystem.cpp:1282, exe 0x141e9afd0), so every NiPSParticleSystem and NiPSMeshParticleSystem (its
    /// subclass) is fog-off, whatever its file flags hold (<see cref="ParticleSystemFlag"/>).
    /// </summary>
    public static bool FogOff(NifFile nif, int block)
    {
        var b = nif.Blocks[block];
        if (UserProp(nif, block) is { } v && v.Contains("nofog", StringComparison.OrdinalIgnoreCase)) return true;
        if (ParticleSystemFlag(nif, block)) return true;
        if (!b.TryGetValue("Properties", out var pv) || pv is not long[] props) return false;
        foreach (var p in props)
        {
            if (p < 0 || nif.Blocks[(int)p].Type != "NiAlphaProperty") continue;
            int f = (int)nif.Blocks[(int)p].Int("Flags");
            return (f & 1) != 0 && ((f >> 5) & 15) == 0;
        }
        return false;
    }

    /// <summary>
    /// The loaded object's NiAVObject flag 0x400 (IS_PARTICLE_SYSTEM, NiAVObject.h:267; tested by FUN_14098dfc0,
    /// alphaanimation.cpp:391-395): the flags the file stores, or a particle system's, which its constructors and
    /// NiPSParticleSystem::LoadBinary set (NiPSParticleSystem.cpp:137, 194, 1282).
    /// </summary>
    public static bool ParticleSystemFlag(NifFile nif, int block)
    {
        var b = nif.Blocks[block];
        return b.Type is "NiPSParticleSystem" or "NiPSMeshParticleSystem" || (b.IntOr("Flags", 0) & 0x400) != 0;
    }

    /// <summary>
    /// A mesh's <see cref="ModelMesh.NoShadow"/> and <see cref="ModelMesh.NoFog"/>: the block or any node above it up to
    /// <paramref name="root"/> (<paramref name="parent"/>: child block to its NiNode) is tagged NoShadow
    /// (<see cref="TaggedNoShadow"/>) or fog-off (<see cref="FogOff"/>). Both client walks act on the matching node's whole
    /// subtree: the shadow walk does not enter it, and FUN_141569620 attaches the fog-off property at its root.
    /// </summary>
    internal static (bool NoShadow, bool NoFog) SubtreeTags(NifFile nif, IReadOnlyDictionary<int, int> parent, int root, int block)
    {
        bool noShadow = false, noFog = false;
        for (int at = block; ; )
        {
            if (!noShadow && TaggedNoShadow(nif, at)) noShadow = true;
            if (!noFog && FogOff(nif, at)) noFog = true;
            if (noShadow && noFog || at == root || !parent.TryGetValue(at, out at)) break;
        }
        return (noShadow, noFog);
    }

    static bool IsIdentity(Xform x)
    {
        if (Math.Abs(x.T.X) + Math.Abs(x.T.Y) + Math.Abs(x.T.Z) > 1e-3 || Math.Abs(x.S - 1) > 1e-4) return false;
        var id = Xform.Identity.R;
        for (int i = 0; i < 9; i++) if (Math.Abs(x.R[i] - id[i]) > 1e-4) return false;
        return true;
    }

    static void ToRootSpace(ModelMesh m, Xform x)
    {
        var o = x.Apply(new Vec3(0, 0, 0));
        for (int i = 0; i + 2 < m.Positions.Length; i += 3)
        {
            var p = x.Apply(new Vec3(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]));
            m.Positions[i] = (float)p.X; m.Positions[i + 1] = (float)p.Y; m.Positions[i + 2] = (float)p.Z;
        }
        foreach (var d in new[] { m.Normals, m.Tangents, m.Binormals })
        {
            if (d == null) continue;
            for (int i = 0; i + 2 < d.Length; i += 3)
            {
                var n = x.Apply(new Vec3(d[i], d[i + 1], d[i + 2])) - o;
                double l = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
                if (l == 0) l = 1;
                d[i] = (float)(n.X / l); d[i + 1] = (float)(n.Y / l); d[i + 2] = (float)(n.Z / l);
            }
        }
    }

    /// <summary>xyz vectors normalised to unit length (zero vectors kept), or null.</summary>
    static float[] Unit(double[] v, int nv)
    {
        if (v == null || v.Length < nv * 3) return null;
        var o = new float[nv * 3];
        for (int k = 0; k < nv; k++)
        {
            double x = v[k * 3], y = v[k * 3 + 1], z = v[k * 3 + 2];
            double l = Math.Sqrt(x * x + y * y + z * z);
            if (l == 0) l = 1;
            o[k * 3] = (float)(x / l); o[k * 3 + 1] = (float)(y / l); o[k * 3 + 2] = (float)(z / l);
        }
        return o;
    }

    /// <summary>
    /// The environment map Gamebryo gives a mesh: NiNode::PushLocalEffects adds each node's effect list to the state the
    /// node passes down (scene root first), and NiDynamicEffectState::AddEffect keeps one environment map, the last one
    /// added, so the nearest node with an ENVIRONMENT_MAP (texture type 2) NiTextureEffect decides. The standard material
    /// then uses it only with its switch on (NiStandardMaterialDescriptor::SetEnvMapDescriptor). Only the SPHERE_MAP
    /// coordinate generation (2, TEXEFFECT_SPHERICAL) is read: there NiTextureEffect::UpdateProjection makes the world
    /// projection rows 0.5 * Rot column 2 and -0.5 * Rot column 1 (Rot the effect's world rotation) with translation
    /// (0.5, 0.5, 0), so u = 0.5 * dot(EnvU, R) + 0.5 and v = -0.5 * dot(EnvV, R) + 0.5 for the world reflection vector R.
    /// EnvU and EnvV are those two columns in the mesh's own space (scene root space for a skinned mesh).
    /// </summary>
    static void ReadEnvMap(NifFile nif, Dictionary<int, int> parent, int root, int mi, ModelData md, Dictionary<int, int> boneOf, ModelMesh mesh)
    {
        var B = nif.Blocks;
        int fx = -1;
        for (int n = parent.TryGetValue(mi, out var p0) ? p0 : root; fx < 0; n = parent[n])
        {
            foreach (var e in B[n].Ints("Effects"))
                if (e >= 0 && B[(int)e].Type == "NiTextureEffect" && B[(int)e].IntOr("Texture Type", -1) == 2) fx = (int)e;
            if (n == root || !parent.ContainsKey(n)) break;
        }
        if (fx < 0) return;
        var ef = B[fx];
        if (ef.IntOr("Switch State", 1) == 0 || ef.IntOr("Coordinate Generation Type", -1) != 2) return;
        long src = ef.IntOr("Source Texture", -1);
        if (src < 0) return;
        // the effect's transform in scene root space (bone World excludes the root's own transform, as the meshes do)
        int ep = parent.TryGetValue(fx, out var p1) ? p1 : root;
        var fxWorld = (boneOf.TryGetValue(ep, out var eb) ? md.Bones[eb].World : Xform.Identity).Compose(XformOf(ef));
        var meshWorld = mesh.Skinned ? Xform.Identity
                      : (mesh.ParentBone >= 0 ? md.Bones[mesh.ParentBone].World : Xform.Identity).Compose(mesh.Local);
        var rel = meshWorld.Inverse().Compose(fxWorld) with { T = new Vec3(0, 0, 0) };
        float[] Axis(Vec3 a)
        {
            var v = rel.Apply(a);
            double l = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (l == 0) l = 1;
            return new[] { (float)(v.X / l), (float)(v.Y / l), (float)(v.Z / l) };
        }
        mesh.EnvMap = nif.StrField(B[(int)src], "File Name");
        mesh.EnvU = Axis(new Vec3(0, 0, 1));
        mesh.EnvV = Axis(new Vec3(0, 1, 0));
    }

    /// <summary>
    /// A model nif's bones and meshes. A mesh flagged APP_CULLED in the file is left out, and so is a mesh under a node
    /// flagged so: NiAVObject::Cull (NiAVObject.cpp:419-423) hands an object to the culling process only when it is not
    /// app-culled, and NiNode::OnVisible (NiNode.cpp:642-653) reaches its children only through Cull, so nothing under a
    /// culled node is drawn; the character mesh merger skips the same subtrees (FUN_141049d50 and FUN_14104d450,
    /// charactermeshmerger.cpp:979, :1846: a culled object is passed over and its children are not queued).
    /// <paramref name="keepCulledSubtrees"/> keeps the meshes under a culled node, for a reader that carries the node flags
    /// itself (EffectScene: its visibility controllers can show such a node). An effect nif read through this path instead
    /// (EffectNif.Build, the CombatLayer, reactor, drop, ride and field NPC model loaders) loses them, since KfAnimation
    /// plays no visibility.
    /// The meshes of NiPSMeshParticleSystem master particles (MasterParticleBlocks) are left out too, since they are drawn
    /// only as the particles' clones; <paramref name="keepMasterParticles"/> keeps them, for EffectScene, which marks them
    /// EffectMesh.Master and draws them only as clones.
    /// <paramref name="onlyBlock"/> reads that one NiMesh block and no other, culled or a master or not, for a reader that takes
    /// a mesh's properties without drawing it (EffectScene.WalkedMesh, the swoosh).
    /// STILL-OPEN: a KF sequence's visibility track (KfAnimation reads none) that shows such a node in a model.
    /// </summary>
    public static ModelData Load(NifFile nif, bool keepCulledSubtrees = false, bool keepMasterParticles = false, int onlyBlock = -1)
    {
        var B = nif.Blocks;
        var md = new ModelData();
        var parent = new Dictionary<int, int>();
        for (int i = 0; i < B.Count; i++)
            if (B[i].TryGetValue("Children", out var c) && c is long[] kids)
                foreach (var k in kids) if (k >= 0) parent[(int)k] = i;

        int root = nif.Roots[0];
        md.RootName = nif.Name(root);
        md.RootLocal = XformOf(B[root]);
        var boneOf = new Dictionary<int, int>();     // block -> bone index

        void Add(int i, int parentBone, Xform parentWorld)
        {
            foreach (var k in B[i].Ints("Children"))
            {
                if (k < 0 || !IsNode(B[(int)k].Type)) continue;
                var local = XformOf(B[(int)k]);
                var world = parentWorld.Compose(local);
                var bone = new ModelBone { Name = nif.NodeName((int)k) ?? $"blk{k}", FileName = nif.Name((int)k), Parent = parentBone, Rest = local, World = world };
                md.Bones.Add(bone);
                boneOf[(int)k] = md.Bones.Count - 1;
                Add((int)k, md.Bones.Count - 1, world);
            }
        }
        Add(root, -1, Xform.Identity);

        var masters = keepMasterParticles || onlyBlock >= 0 ? new HashSet<int>() : MasterParticleBlocks(nif);
        for (int i = onlyBlock >= 0 ? onlyBlock : 0; i < (onlyBlock >= 0 ? Math.Min(onlyBlock + 1, B.Count) : B.Count); i++)
        {
            var b = B[i];
            if (b.Type != "NiMesh") continue;
            if (onlyBlock < 0)
            {
                if ((b.IntOr("Flags", 0) & 1) != 0) continue;           // APP_CULLED helper geometry
                if (masters.Contains(i)) continue;                       // drawn only as mesh particles (MasterParticleBlocks)
                if (!keepCulledSubtrees && UnderCulledNode(B, parent, i)) continue;
            }
            long primitive = b.Int("Primitive Type");                  // 0 triangles, 1 triangle strips
            if (primitive != 0 && primitive != 1) throw new NotSupportedException("only triangle lists and strips");
            var s = MeshStreams(nif, i);
            int si = -1;
            foreach (var m in b.Ints("Modifiers"))
                if (m >= 0 && B[(int)m].Type == "NiSkinningMeshModifier") { si = (int)m; break; }
            bool skinned = si >= 0 && s.ContainsKey("BLENDINDICES");
            (double[] v, int n) Pick(params string[] keys)
            {
                foreach (var k in keys) if (s.TryGetValue(k, out var x)) return x;
                return (null, 0);
            }
            string positionKey = skinned ? (s.ContainsKey("POSITION_BP") ? "POSITION_BP" : "POSITION") : (s.ContainsKey("POSITION") ? "POSITION" : "POSITION_BP");
            var pos = Pick(positionKey);
            var nrm = skinned ? Pick("NORMAL_BP", "NORMAL") : Pick("NORMAL", "NORMAL_BP");
            int nv = pos.v.Length / 3;
            var mesh = new ModelMesh
            {
                Name = nif.NodeName(i), FileName = nif.Name(i), Block = i, Skinned = skinned,
                Positions = pos.v.Select(x => (float)x).ToArray(),
                Textures = TexturesOf(nif, i),
                Maps = MapsOf(nif, i),
                Bound = BoundOf(B[i]),
            };
            (mesh.NoShadow, mesh.NoFog) = SubtreeTags(nif, parent, root, i);
            mesh.Normals = Unit(nrm.v, nv);
            mesh.Tangents = Unit((skinned ? Pick("TANGENT_BP", "TANGENT") : Pick("TANGENT", "TANGENT_BP")).v, nv);
            mesh.Binormals = Unit((skinned ? Pick("BINORMAL_BP", "BINORMAL") : Pick("BINORMAL", "BINORMAL_BP")).v, nv);
            if (s.TryGetValue("TEXCOORD", out var uv)) mesh.Uvs = uv.v.Select(x => (float)x).ToArray();
            if (s.TryGetValue("TEXCOORD1", out var uv1)) mesh.Uvs1 = uv1.v.Select(x => (float)x).ToArray();
            if (s.TryGetValue("COLOR", out var col) && col.n >= 3)
            {
                // byte colours come as 0..255; float colours as 0..1
                double k = col.v.Length > 0 && col.v.Max() > 1.0 ? 1.0 / 255 : 1.0;
                mesh.Colors = new float[nv * 4];
                for (int c = 0; c < nv; c++)
                    for (int j = 0; j < 4; j++)
                        mesh.Colors[c * 4 + j] = j < col.n ? (float)(col.v[c * col.n + j] * k) : 1f;
            }
            mesh.Flags = (int)b.IntOr("Flags", 0);
            ReadMaterial(nif, b, mesh);
            mesh.AlphaBoost = AlphaBoost(nif, i);
            ReadTextureControllers(nif, b, mesh);
            ReadMorph(nif, b, s, skinned, mesh);
            // the submeshes (skin partitions): each draws its own INDEX region over its own vertex region
            int submeshes = (int)b.IntOr("Num Submeshes", 1);
            var vertexRegions = SubmeshRegions(nif, i, positionKey, submeshes, nv);
            if (vertexRegions != null && OtherVertexRegions(nif, i, vertexRegions, nv) is { } other)
                md.Warnings.Add($"{mesh.Name}: stream {other} has other submesh regions than {positionKey}");
            mesh.Indices = SubmeshIndices(s["INDEX"].v, SubmeshRegions(nif, i, "INDEX", submeshes, s["INDEX"].v.Length), vertexRegions, primitive == 1);
            foreach (var p in b.Ints("Properties"))
            {
                if (p < 0) continue;
                var prop = B[(int)p];
                if (prop.Type == "NiAlphaProperty")
                {
                    mesh.AlphaTest = true;
                    mesh.AlphaFlags = (int)prop.Int("Flags");
                    mesh.AlphaThreshold = (int)prop.IntOr("Threshold", 0);
                }
                if (prop.Type == "NiVertexColorProperty") mesh.VertexColorFlags = (int)prop.Int("Flags");
                if (prop.Type == "NiMaterialProperty")
                {
                    float[] Rgb(string k) { var c = prop.Struct(k); return new[] { c.Float("r"), c.Float("g"), c.Float("b") }; }
                    mesh.Ambient = Rgb("Ambient Color");
                    mesh.Diffuse = Rgb("Diffuse Color");
                    mesh.Emissive = Rgb("Emissive Color");
                    mesh.Specular = Rgb("Specular Color");
                    mesh.Glossiness = prop.Float("Glossiness");
                    mesh.MaterialAlpha = prop.Float("Alpha");
                    mesh.AlphaAnim = ReadAlphaController(nif, prop);
                }
                if (prop.Type == "NiSpecularProperty") mesh.SpecularOn = (prop.IntOr("Flags", 0) & 1) != 0;
            }

            ReadDrawState(nif, parent, i, mesh);

            if (skinned)
            {
                var sk = B[si];
                if (sk.Int("Skeleton Root") != root) throw new NotSupportedException("skeleton root is not the scene root");
                var skBones = sk.Ints("Bones");
                // Vertices are in the skin's bind space; a bone's world transform times its bone transform maps that space
                // to the scene root. The body's skins use the scene root itself; item skins (clothes) are often offset.
                var bts = sk.Structs("Bone Transforms");
                int mpar = parent.TryGetValue(i, out var mp) ? mp : root;
                var meshToRoot = (boneOf.TryGetValue(mpar, out var mpb) ? md.Bones[mpb].World : Xform.Identity).Compose(XformOf(b));
                mesh.BoundToRoot = meshToRoot;
                mesh.PickToRoot = meshToRoot;
                mesh.SkinMeshLocal = XformOf(b);
                mesh.SkinTransform = XformOf(sk.Struct("Skeleton Transform"));
                if ((sk.IntOr("Flags", 0) & 2) != 0)
                {
                    var bb = sk.Structs("Bone Bounds");
                    mesh.BoneBounds = new List<(int, float[])>();
                    for (int p = 0; p < skBones.Length && p < bb.Length; p++)
                    {
                        var c = bb[p].Struct("Center");
                        if (boneOf.TryGetValue((int)skBones[p], out int bbp))
                            mesh.BoneBounds.Add((bbp, new[] { c.Float("x"), c.Float("y"), c.Float("z"), bb[p].Float("Radius") }));
                    }
                    mesh.BoneBoundsToRoot = meshToRoot.Compose(XformOf(sk.Struct("Skeleton Transform"))).Compose(md.RootLocal);
                }
                if (bts.Length > 0 && boneOf.TryGetValue((int)skBones[0], out int b0))
                {
                    var toRoot = md.Bones[b0].World.Compose(XformOf(bts[0]));
                    // a bone hidden by a zero scale in the file's pose (an effect creature before it flies in) has no
                    // usable bind there: the skin's Skeleton Transform places the skin in the scene root instead (identity in
                    // the select-pose effect nifs, so its direction is not checked)
                    if (toRoot.S == 0) toRoot = XformOf(sk.Struct("Skeleton Transform"));
                    if (!IsIdentity(toRoot)) { ToRootSpace(mesh, toRoot); mesh.PickToRoot = meshToRoot.Compose(toRoot.Inverse()); }
                    // each skin bone's bind (scene root -> bone) from its bone transform, the mesh now being in root space
                    var rootToSkin = toRoot.Inverse();
                    mesh.SkinBinds = new Dictionary<int, Xform>();
                    for (int p = 0; p < skBones.Length && p < bts.Length; p++)
                        if (boneOf.TryGetValue((int)skBones[p], out int bp)) mesh.SkinBinds.TryAdd(bp, XformOf(bts[p]).Compose(rootToSkin));
                }
                var pal = s.TryGetValue("BONE_PALETTE", out var pp) ? pp.v.Select(x => (int)x).ToArray()
                                                                     : Enumerable.Range(0, skBones.Length).ToArray();
                var bones = pal.Select(p => boneOf[(int)skBones[p]]).ToArray();
                // a vertex's blend indices count from its submesh's BONE_PALETTE region (NiD3DShaderConstantMap.cpp:1627-1636
                // loads the bone matrices of kRCC.m_uiSubmesh's region); a vertex outside every submesh keeps the whole palette
                var palStart = new int[nv];
                var palCount = new int[nv];
                Array.Fill(palCount, bones.Length);
                if (SubmeshRegions(nif, i, "BONE_PALETTE", submeshes, bones.Length) is { } palRegions && vertexRegions != null)
                {
                    var owner = new int[nv];
                    Array.Fill(owner, -1);
                    bool overlap = false;
                    for (int sm = 0; sm < submeshes; sm++)
                        for (int v = vertexRegions[sm].Start; v < vertexRegions[sm].Start + vertexRegions[sm].Count; v++)
                        {
                            if (owner[v] >= 0 && palRegions[owner[v]] != palRegions[sm]) overlap = true;
                            owner[v] = sm;
                            (palStart[v], palCount[v]) = palRegions[sm];
                        }
                    if (overlap) md.Warnings.Add($"{mesh.Name}: submeshes share vertices under different bone palettes");
                }
                var bi = s["BLENDINDICES"]; var bw = s["BLENDWEIGHT"];
                mesh.Joints = new int[nv * 4];
                mesh.Weights = new float[nv * 4];
                for (int k = 0; k < nv; k++)
                {
                    var w = new double[4];
                    double sum3 = 0;
                    for (int j = 0; j < bw.n && j < 4; j++) { w[j] = bw.v[k * bw.n + j]; sum3 += w[j]; }
                    if (bw.n == 3) w[3] = Math.Clamp(1.0 - sum3, 0, 1);
                    var J = new int[4];
                    for (int j = 0; j < 4; j++)
                    {
                        int ix = j < bi.n ? (int)bi.v[k * bi.n + j] : 0;
                        if (ix >= palCount[k]) { w[j] = 0; ix = 0; }
                        if (w[j] < 1e-6) w[j] = 0;
                        J[j] = w[j] == 0 ? 0 : bones[palStart[k] + ix];
                    }
                    double sum = w.Sum();
                    if (sum <= 0) { w[0] = 1; sum = 1; }
                    for (int j = 0; j < 4; j++) { mesh.Joints[k * 4 + j] = J[j]; mesh.Weights[k * 4 + j] = (float)(w[j] / sum); }
                }
            }
            else
            {
                int par = parent.TryGetValue(i, out var pi) ? pi : root;
                mesh.ParentBone = boneOf.TryGetValue(par, out var pb) ? pb : -1;
                mesh.Local = XformOf(b);
                // the nearest NiBillboardNode above the mesh, and the mesh's transform below it
                for (int n = par, guard = 0; n >= 0 && n != root && guard < 256; n = parent.TryGetValue(n, out int up) ? up : -1, guard++)
                {
                    if (B[n].Type != "NiBillboardNode" || !boneOf.TryGetValue(n, out int bb)) continue;
                    mesh.Billboard = bb;
                    mesh.BillboardMode = (int)(B[n].IntOr("Billboard Mode", 1) & 7);   // FACE_MODE_MASK (NiBillboardNode.h:81)
                    var meshWorld = (mesh.ParentBone >= 0 ? md.Bones[mesh.ParentBone].World : Xform.Identity).Compose(mesh.Local);
                    mesh.InBillboard = md.Bones[bb].World.Inverse().Compose(meshWorld);
                    break;
                }
            }
            ReadEnvMap(nif, parent, root, i, md, boneOf, mesh);
            md.Meshes.Add(mesh);
        }
        return md;
    }

    /// <summary>The blocks of the file's NiPSMeshParticleSystem master particles: each master and everything under it. The
    /// masters are not in the scene graph (no node holds one as a child, in both data sets); the system only clones them,
    /// one clone per particle under its particle container (NiPSMeshParticleSystem::InitializeParticle,
    /// NiPSMeshParticleSystem.cpp:229-300), so their meshes are drawn only as the particles (EffectInstance.DrawClones)
    /// and never where they stand in the file.</summary>
    public static HashSet<int> MasterParticleBlocks(NifFile nif)
    {
        var B = nif.Blocks;
        var set = new HashSet<int>();
        void Walk(int b)
        {
            if (b < 0 || b >= B.Count || !set.Add(b)) return;
            if (B[b].Has("Children")) foreach (var k in B[b].Ints("Children")) Walk((int)k);
        }
        for (int i = 0; i < B.Count; i++)
            if (B[i].Type == "NiPSMeshParticleSystem" && B[i].Has("Master Particles"))
                foreach (var m in B[i].Ints("Master Particles")) Walk((int)m);
        return set;
    }

    /// <summary>Whether a node above block <paramref name="i"/> is flagged APP_CULLED (NiAVObject flag 0x1) in the file.</summary>
    static bool UnderCulledNode(List<NifStruct> B, Dictionary<int, int> parent, int i)
    {
        for (int a = i; parent.TryGetValue(a, out var p); a = p)
            if ((B[p].IntOr("Flags", 0) & 1) != 0) return true;
        return false;
    }
}
