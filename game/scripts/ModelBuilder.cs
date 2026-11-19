using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;

namespace Ms2Godot;

/// <summary>
/// Builds Godot nodes from MS2 model data at run time: Skeleton3D, skinned MeshInstance3D, AnimationPlayer.
/// Units stay 1:1 with the files (the female body is about 100 tall); the returned root rotates Z-up to Y-up.
/// </summary>
public sealed class ModelBuilder
{
    public const string ModelArchive = "Resource/Model/Character";
    public const string TextureArchive = "Resource/Model/Textures";
    public const string SkeletonName = "Skeleton";

    readonly NifSpec spec;
    public NifSpec Spec => spec;
    readonly PackSet packs;
    readonly Dictionary<string, Texture2D> textures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Archives searched (in order) for a texture file name.</summary>
    public string[] TextureArchives = { TextureArchive };

    public ModelBuilder(NifSpec spec, PackSet packs) { this.spec = spec; this.packs = packs; }

    public static Transform3D ToGodot(Xform x)
    {
        var r = x.R;
        float s = (float)x.S;
        var basis = new Basis(new Vector3((float)r[0], (float)r[3], (float)r[6]),
                              new Vector3((float)r[1], (float)r[4], (float)r[7]),
                              new Vector3((float)r[2], (float)r[5], (float)r[8])).Scaled(new Vector3(s, s, s));
        return new Transform3D(basis, new Vector3((float)x.T.X, (float)x.T.Y, (float)x.T.Z));
    }

    static Quaternion ToGodot(Quat q) => new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W).Normalized();

    public ModelData LoadModel(string entry) => ModelData.Load(NifFile.Load(spec, packs.Read(ModelArchive, entry)));

    public ModelData LoadModel(string archive, string entry) => ModelData.Load(NifFile.Load(spec, packs.Read(archive, entry)));

    public KfAnimation LoadKf(string entry) => KfAnimation.Load(NifFile.Load(spec, packs.Read(ModelArchive, entry)));

    /// <summary>Parents and rests of a model's bones on its skeleton, then the poses reset to them.</summary>
    public static void SetRests(Skeleton3D skel, ModelData md)
    {
        for (int i = 0; i < md.Bones.Count; i++)
        {
            skel.SetBoneParent(i, md.Bones[i].Parent);
            // a zero-scale rest has no rotation to read back (the pose is kept as a quaternion), so the rest carries
            // scale 1 and the pose the zero scale
            var rest = md.Bones[i].Rest;
            skel.SetBoneRest(i, ToGodot(rest.S == 0 ? rest with { S = 1 } : rest));
        }
        skel.ResetBonePoses();
        for (int i = 0; i < md.Bones.Count; i++)
            if (md.Bones[i].Rest.S == 0) skel.SetBonePoseScale(i, Vector3.Zero);
    }

    /// <summary>
    /// The skin of a skinned mesh: each bone binds through that mesh's skin bone transform (NiSkinningMeshModifier), not
    /// through the file's node pose. The two agree on the player's body (within 4e-5), but many NPC, effect and map
    /// models store another pose (an effect creature rests or hides there), and meshes of one model may bind one bone
    /// differently, so each skinned mesh has its own skin; bones outside the mesh's skin keep World^-1.
    /// </summary>
    public static Skin SkinOf(ModelData md, ModelMesh m)
    {
        var skin = new Skin();
        for (int i = 0; i < md.Bones.Count; i++)
            skin.AddBind(i, m.SkinBinds != null && m.SkinBinds.TryGetValue(i, out var bind) ? ToGodot(bind)
                          : md.Bones[i].World.S == 0 ? Transform3D.Identity : ToGodot(md.Bones[i].World).AffineInverse());
        return skin;
    }

    /// <summary>Root (Y-up) -> Model (file Z-up, scene root transform) -> Skeleton + meshes; AnimationPlayer under Model.</summary>
    public Node3D Build(ModelData md, string name)
    {
        var root = new Node3D { Name = name };
        var model = new Node3D { Name = "Model", Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero) * ToGodot(md.RootLocal) };
        root.AddChild(model);

        var skel = new Skeleton3D { Name = SkeletonName };
        model.AddChild(skel);
        foreach (var b in md.Bones) skel.AddBone(b.Name);
        SetRests(skel, md);

        foreach (var m in md.Meshes)
        {
            var mi = new MeshInstance3D { Name = m.Name, Mesh = BuildMesh(m) };
            var skin = m.Skinned ? SkinOf(md, m) : null;
            if (m.Skinned)
            {
                skel.AddChild(mi);
                mi.Skin = skin;
                mi.Skeleton = "..";
            }
            else if (m.ParentBone >= 0)
            {
                var att = new BoneAttachment3D { Name = m.Name + "_attach", BoneName = md.Bones[m.ParentBone].Name };
                skel.AddChild(att);
                mi.Transform = ToGodot(m.Local);
                att.AddChild(mi);
            }
            else
            {
                mi.Transform = ToGodot(m.Local);
                skel.AddChild(mi);
            }
            AddGlowTwin(mi, m, skin);
        }

        var player = new AnimationPlayer { Name = "AnimationPlayer" };
        model.AddChild(player);
        player.RootNode = "..";
        player.AddAnimationLibrary("", new AnimationLibrary());
        return root;
    }

    /// <param name="skin">false: drop bone data (static placement of a skinned prop, drawn in its bind pose)</param>
    /// <param name="instanceColor">field mesh: the MultiMesh instance colour is the material colour (field_lit shader)</param>
    /// <param name="uv2">the second uv set as UV2 without instanceColor (effect meshes: a map may read uv set 1)</param>
    public ArrayMesh BuildMesh(ModelMesh m, bool skin = true, bool instanceColor = false, bool uv2 = false)
    {
        int nv = m.Positions.Length / 3;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        var pos = new Vector3[nv];
        for (int i = 0; i < nv; i++) pos[i] = new Vector3(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
        arrays[(int)Mesh.ArrayType.Vertex] = pos;
        if (m.Normals != null)
        {
            var n = new Vector3[nv];
            for (int i = 0; i < nv; i++) n[i] = new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
            arrays[(int)Mesh.ArrayType.Normal] = n;
        }
        if (!instanceColor)
        {
            // character_lit reads TANGENT/BINORMAL, so every surface it draws needs a tangent array (Godot warns otherwise).
            // Godot keeps the tangent and a handedness sign; it rebuilds the binormal as cross(normal, tangent) * sign.
            // Meshes without the NIF's tangent frame get a placeholder frame; their normal map and hair term stay off
            // (CharacterMaterial gates both on m.Tangents).
            var t = new float[nv * 4];
            for (int i = 0; i < nv; i++)
            {
                Vector3 tt;
                float w = 1f;
                if (m.Tangents != null)
                {
                    tt = new Vector3(m.Tangents[i * 3], m.Tangents[i * 3 + 1], m.Tangents[i * 3 + 2]);
                    if (m.Normals != null && m.Binormals != null)
                    {
                        var nn = new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
                        var bb = new Vector3(m.Binormals[i * 3], m.Binormals[i * 3 + 1], m.Binormals[i * 3 + 2]);
                        w = nn.Cross(tt).Dot(bb) < 0 ? -1f : 1f;
                    }
                }
                else
                {
                    var nn = m.Normals != null ? new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]) : Vector3.Up;
                    tt = nn.Cross(Mathf.Abs(nn.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
                }
                t[i * 4] = tt.X; t[i * 4 + 1] = tt.Y; t[i * 4 + 2] = tt.Z; t[i * 4 + 3] = w;
            }
            arrays[(int)Mesh.ArrayType.Tangent] = t;
        }
        if (m.Uvs != null)
        {
            var uv = new Vector2[nv];
            for (int i = 0; i < nv; i++) uv[i] = new Vector2(m.Uvs[i * 2], m.Uvs[i * 2 + 1]);
            arrays[(int)Mesh.ArrayType.TexUV] = uv;
        }
        if (m.Skinned && skin)
        {
            arrays[(int)Mesh.ArrayType.Bones] = m.Joints;
            arrays[(int)Mesh.ArrayType.Weights] = m.Weights;
        }
        ulong format = 0;
        if ((instanceColor || uv2) && m.Uvs1 != null)
        {
            var u2 = new Vector2[nv];
            for (int i = 0; i < nv; i++) u2[i] = new Vector2(m.Uvs1[i * 2], m.Uvs1[i * 2 + 1]);
            arrays[(int)Mesh.ArrayType.TexUV2] = u2;
        }
        if (instanceColor)
        {
            // field meshes: second uv set as UV2, vertex colours as CUSTOM0 so that COLOR stays the instance colour
            if (m.Colors != null)
            {
                arrays[(int)Mesh.ArrayType.Custom0] = m.Colors;
                format |= (ulong)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift;
            }
        }
        // file triangles are counter-clockwise (as in the glTF export); Godot's front faces are clockwise
        var idx = (int[])m.Indices.Clone();
        for (int i = 0; i + 2 < idx.Length; i += 3) (idx[i + 1], idx[i + 2]) = (idx[i + 2], idx[i + 1]);
        arrays[(int)Mesh.ArrayType.Index] = idx;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, (Mesh.ArrayFormat)format);
        mesh.SurfaceSetMaterial(0, BuildMaterial(m, instanceColor));
        return mesh;
    }

    /// <summary>A mesh with a glow map also joins the glow pass: a twin on the glow layer sharing its mesh, parent and skin.</summary>
    public void AddGlowTwin(MeshInstance3D mi, ModelMesh m, Skin skin)
    {
        if (!GlowPass.Wanted || GlowMaterial(m) is not { } gm) return;
        var g = new MeshInstance3D { Name = mi.Name + "_glow", Mesh = mi.Mesh, Layers = GlowPass.Bit, MaterialOverride = gm, Transform = mi.Transform, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        mi.GetParent().AddChild(g);
        if (skin != null) { g.Skin = skin; g.Skeleton = ".."; }
    }

    static Shader glowShader;

    /// <summary>The glow pass material of a mesh with a glow map (shaders/field_glow), or null.</summary>
    public Material GlowMaterial(ModelMesh m)
    {
        if (!m.Textures.TryGetValue("glow", out var glowName) || Texture(glowName) is not { } glow) return null;
        glowShader ??= GD.Load<Shader>("res://shaders/field_glow.gdshader");
        var sm = new ShaderMaterial { Shader = glowShader };
        var tex = m.Textures.TryGetValue("base", out var baseName) ? Texture(baseName) : null;
        sm.SetShaderParameter(ShaderParam.HasTex, tex != null);
        if (tex != null) sm.SetShaderParameter(ShaderParam.BaseTex, tex);
        sm.SetShaderParameter(ShaderParam.GlowTex, glow);
        sm.SetShaderParameter(ShaderParam.AlphaTest, m.AlphaTestOn || m.AlphaBlendOn || m.AlphaTest);
        return sm;
    }

    static Shader fieldShader, fieldBlendShader, fieldAddShader, fieldShellShader, fieldFadeShader, fieldFadeAddShader;

    /// <summary>The field shader for a NiAlphaProperty: opaque, blended, or additive (destination factor ONE).</summary>
    static Shader FieldShader(ModelMesh m)
    {
        if (fieldShader == null)
        {
            var code = GD.Load<Shader>("res://shaders/field_lit.gdshader").Code;
            fieldShader = new Shader { Code = code };
            int nl = code.IndexOf('\n', code.IndexOf("shader_type")) + 1;
            fieldBlendShader = new Shader { Code = code.Insert(nl, "#define BLEND\n") };
            fieldAddShader = new Shader { Code = code.Insert(nl, "#define BLEND\n#define ADD\n") };
            fieldShellShader = new Shader { Code = code.Insert(nl, "#define BLEND\n#define SHELL\n") };
            fieldFadeShader = new Shader { Code = code.Insert(nl, "#define BLEND\n#define FADE\n") };
            fieldFadeAddShader = new Shader { Code = code.Insert(nl, "#define BLEND\n#define ADD\n#define FADE\n") };
        }
        if (!m.AlphaBlendOn) return fieldShader;
        return ((m.AlphaFlags >> 5) & 15) == 0 ? fieldAddShader : fieldBlendShader;
    }

    /// <summary>
    /// A blended fluid cube mesh as the client's alpha shell. FUN_1413fa4c0 turns on the z-buffer write of a fluid cube and
    /// marks it into shell group 1 (AlphaShellComposite::MarkScene); Ms2RenderListProcessor hands those meshes to the
    /// group's AlphaShellComposite, whose vfunc_0 draws every shell first with the blend set to ZERO / ONE (depth only,
    /// z test LESSEQUAL) and then again with its own blend. Only the nearest fluid surface of each pixel gets colour.
    /// Here the first pass is the SHELL form of the field shader and the mesh's own blended material is its next pass.
    /// Every fluid is kind A: MeshInstancing::Init stamps DAT_143ae51d0 when FUN_1413fa390 (IsHighPrioAlphaObject or CubeType
    /// "Fluid") holds, and AlphaShellComposite::Update then adds FUN_141b71c50 (far distance x 10000) to the composite's key.
    /// CompareAlpha draws the largest key first, so the whole fluid composite draws before every unbiased transparent
    /// object. Godot sorts on render priority before depth, so the shell pass gets priority -2 and the colour pass -1:
    /// every fluid depth pass, then every fluid colour, then every other transparent (priority 0). Not modelled: the
    /// planar depth (Godot sorts by radial distance) and a kind-A own composite that the client sorts among the fluid.
    /// </summary>
    public static void MakeFluidShell(ArrayMesh mesh)
    {
        if (ShellMaterial(mesh) is not { } shell) return;
        var colour = (ShaderMaterial)mesh.SurfaceGetMaterial(0);
        shell.RenderPriority = -2;
        colour.RenderPriority = -1;
        shell.NextPass = colour;
        mesh.SurfaceSetMaterial(0, shell);
    }

    /// <summary>
    /// The faded form of a field mesh's colour material (TransparencyMaterial::FUN_141860140): blending on with the mesh's
    /// own NiAlphaProperty blend (an additive one stays additive; a mesh without one gets a default property, source alpha /
    /// inverse source alpha) and the depth write on. The caller sets its alpha with <see cref="SetFade"/>.
    /// </summary>
    public static ShaderMaterial FadeMaterial(ShaderMaterial colour, ModelMesh m)
    {
        FieldShader(m);
        var fade = (ShaderMaterial)colour.Duplicate();
        fade.Shader = m.AlphaBlendOn && ((m.AlphaFlags >> 5) & 15) == 0 ? fieldFadeAddShader : fieldFadeShader;
        // only the property's own test flag tests: the 0.5 cut-out BuildMaterial gives an untested property would cut the
        // faded alpha (texture alpha times the fade) away
        fade.SetShaderParameter(ShaderParam.AlphaTest, m.AlphaTestOn);
        // the mesh's own NiVertexColorProperty is swapped for one with source mode 3 (TransparencyMaterial::FUN_141860140,
        // TransparencyStandardMaterial::vfunc_2); an inherited one is left as it is
        fade.SetShaderParameter(ShaderParam.FadeVcSwap, m.VertexColorFlags >= 0);
        return fade;
    }

    /// <summary>
    /// TransparencyStandardMaterial::vfunc_4 (0x141860bf0): the material alpha becomes the mesh's own NiMaterialProperty
    /// alpha (1 without one) times the fade, and when the NiAlphaProperty tests alpha (flag 0x200) its reference becomes
    /// (int)(fade * reference) &amp; 0xff. The reference is read back from the live NiAlphaProperty, the clone that
    /// TransparencyMaterial::FUN_141860140 attached and ReleaseMaterials drops, so it compounds on every call while the
    /// clone lives (the material alpha is taken from the saved m_spMaterial and does not). <paramref name="testRef"/> is
    /// that live reference: start it at the mesh's own reference when the fade material is made.
    /// </summary>
    public static void SetFade(ShaderMaterial fade, ModelMesh m, float alpha, ref int testRef)
    {
        fade.SetShaderParameter(ShaderParam.FadeAlpha, m.MaterialAlpha * alpha);
        if (!m.AlphaTestOn) return;
        testRef = (int)(alpha * testRef) & 0xff;
        fade.SetShaderParameter(ShaderParam.AlphaRef, testRef / 255f);
    }

    /// <summary>The depth-only SHELL form of a blended field mesh's material (colour unchanged, depth written), or null.</summary>
    public static ShaderMaterial ShellMaterial(ArrayMesh mesh)
    {
        if (mesh.GetSurfaceCount() == 0 || mesh.SurfaceGetMaterial(0) is not ShaderMaterial colour) return null;
        var shell = (ShaderMaterial)colour.Duplicate();
        shell.Shader = fieldShellShader;
        return shell;
    }

    Material BuildMaterial(ModelMesh m, bool instanceColor)
    {
        Texture2D tex = m.Textures.TryGetValue("base", out var baseName) ? Texture(baseName) : null;
        if (instanceColor)
        {
            // field meshes: Gamebryo-style gamma-space lighting (shaders/field_lit.gdshader)
            var sm = new ShaderMaterial { Shader = FieldShader(m) };
            sm.SetShaderParameter(ShaderParam.HasTex, tex != null);
            if (tex != null) sm.SetShaderParameter(ShaderParam.BaseTex, tex);
            var dark = m.Textures.TryGetValue("dark", out var darkName) ? Texture(darkName) : null;
            sm.SetShaderParameter(ShaderParam.HasDark, dark != null);
            if (dark != null) sm.SetShaderParameter(ShaderParam.DarkTex, dark);
            sm.SetShaderParameter(ShaderParam.DarkUv2, m.Uvs1 != null);
            var decal = m.Textures.TryGetValue("decal0", out var decalName) ? Texture(decalName) : null;
            sm.SetShaderParameter(ShaderParam.HasDecal, decal != null);
            if (decal != null) sm.SetShaderParameter(ShaderParam.DecalTex, decal);
            sm.SetShaderParameter(ShaderParam.HasVcolor, m.Colors != null);
            sm.SetShaderParameter(ShaderParam.VcolorAmbDiff, m.VertexColorsAmbDiff);
            sm.SetShaderParameter(ShaderParam.MatAlpha, m.MaterialAlpha);
            // alpha test as flagged; a material with an alpha property but neither flag keeps the old 0.5 cut-out
            bool test = m.AlphaTestOn || (m.AlphaTest && !m.AlphaBlendOn);
            sm.SetShaderParameter(ShaderParam.AlphaTest, test);
            sm.SetShaderParameter(ShaderParam.AlphaRef, m.AlphaTestOn && m.AlphaThreshold > 0 ? m.AlphaThreshold / 255f : 0.5f);
            if (m.Emissive != null) sm.SetShaderParameter(ShaderParam.Emissive, new Vector3(m.Emissive[0], m.Emissive[1], m.Emissive[2]));
            if (m.Ambient != null) sm.SetShaderParameter(ShaderParam.MatAmbient, new Vector3(m.Ambient[0], m.Ambient[1], m.Ambient[2]));
            if (m.Diffuse != null) sm.SetShaderParameter(ShaderParam.MatDiffuse, new Vector3(m.Diffuse[0], m.Diffuse[1], m.Diffuse[2]));
            var glow = m.Textures.TryGetValue("glow", out var glowName) ? Texture(glowName) : null;
            sm.SetShaderParameter(ShaderParam.HasGlow, glow != null);
            if (glow != null) sm.SetShaderParameter(ShaderParam.GlowTex, glow);
            // a sphere-mapped environment map adds env * gloss to the specular (HandleMS2PostLightTextureApplication,
            // ms2standardmaterial.cpp); MS2GlassMaterial also adds the specular's average to the opacity
            var env = m.EnvMap != null ? Texture(m.EnvMap) : null;
            sm.SetShaderParameter(ShaderParam.HasEnv, env != null);
            if (env != null)
            {
                sm.SetShaderParameter(ShaderParam.EnvTex, env);
                sm.SetShaderParameter(ShaderParam.EnvU, new Vector3(m.EnvU[0], m.EnvU[1], m.EnvU[2]));
                sm.SetShaderParameter(ShaderParam.EnvV, new Vector3(m.EnvV[0], m.EnvV[1], m.EnvV[2]));
                var gloss = m.Textures.TryGetValue("gloss", out var gn) ? Texture(gn) : null;
                sm.SetShaderParameter(ShaderParam.HasGloss, gloss != null);
                if (gloss != null) sm.SetShaderParameter(ShaderParam.GlossTex, gloss);
            }
            sm.SetShaderParameter(ShaderParam.Glass, m.Material == "MS2GlassMaterial");
            int flipSlot = m.FlipSlot == "base" ? 0 : m.FlipSlot == "dark" ? 1 : -1;
            var flip = flipSlot >= 0 ? FlipArray(m.FlipFrames) : null;
            if (flip != null)
            {
                sm.SetShaderParameter(ShaderParam.FlipTex, flip);
                sm.SetShaderParameter(ShaderParam.FlipSlot, flipSlot);
                sm.SetShaderParameter(ShaderParam.FlipCount, m.FlipFrames.Length);
                sm.SetShaderParameter(ShaderParam.FlipPeriod, m.FlipPeriod);
            }
            foreach (var sc in m.Scrolls)
            {
                if (sc.Slot is not ("base" or "dark")) continue;
                sm.SetShaderParameter(sc.Slot + "_scroll_axis", sc.Axis);
                sm.SetShaderParameter(sc.Slot + "_scroll_key", new Vector4(sc.Times[0], sc.Times[^1], sc.Values[0], sc.Values[^1]));
                sm.SetShaderParameter(sc.Slot + "_scroll_range", new Vector2(sc.Start, sc.Stop));
            }
            return sm;
        }
        return CharacterMaterial(m, tex);
    }

    static Shader characterShader;

    /// <summary>
    /// Character meshes: shaders/character_lit.gdshader with the NIF's material colours and the shader attributes the
    /// exporter stores as extra data (OverrideColor0..2, FresnelBoost, FresnelExponent, ColorBoost). The dye control map
    /// is the texture slot named by ColorOverrideMapIndex (shader map 0 by default).
    /// </summary>
    ShaderMaterial CharacterMaterial(ModelMesh m, Texture2D tex)
    {
        characterShader ??= GD.Load<Shader>("res://shaders/character_lit.gdshader");
        var sm = new ShaderMaterial { Shader = characterShader };
        sm.SetShaderParameter(ShaderParam.HasTex, tex != null);
        if (tex != null) sm.SetShaderParameter(ShaderParam.BaseTex, tex);
        int ctrlIndex = m.ExtraInts.TryGetValue("ColorOverrideMapIndex", out var ci) ? ci : 0;
        var ctrl = m.Textures.TryGetValue("shader" + ctrlIndex, out var ctrlName) ? Texture(ctrlName) : null;
        sm.SetShaderParameter(ShaderParam.HasCtrl, ctrl != null);
        if (ctrl != null) sm.SetShaderParameter(ShaderParam.CtrlTex, ctrl);
        for (int i = 0; i < 3; i++)
            if (m.ExtraColors.TryGetValue("OverrideColor" + i, out var c))
                sm.SetShaderParameter(ShaderParam.Color[i], new Vector3(c[0], c[1], c[2]));
        Vector3 V(float[] a, float d) => a == null ? new Vector3(d, d, d) : new Vector3(a[0], a[1], a[2]);
        sm.SetShaderParameter(ShaderParam.MatAmbient, V(m.Ambient, 1f));
        sm.SetShaderParameter(ShaderParam.MatDiffuse, V(m.Diffuse, 1f));
        sm.SetShaderParameter(ShaderParam.Emissive, V(m.Emissive, 0f));
        sm.SetShaderParameter(ShaderParam.FresnelBoost, m.ExtraFloats.GetValueOrDefault("FresnelBoost", 5f));
        sm.SetShaderParameter(ShaderParam.FresnelExponent, m.ExtraFloats.GetValueOrDefault("FresnelExponent", 4f));
        sm.SetShaderParameter(ShaderParam.ColorBoost, m.ExtraFloats.GetValueOrDefault("ColorBoost", 1f));
        sm.SetShaderParameter(ShaderParam.Skin, IsSkinMaterial(m));
        // normal map and specular (on with an enabled NiSpecularProperty; the gloss map scales it; hair adds the
        // anisotropic term from its tangent map, custom map 1)
        var normal = m.Tangents != null && m.Textures.TryGetValue("normal", out var nn) ? Texture(nn) : null;
        sm.SetShaderParameter(ShaderParam.HasNormal, normal != null);
        if (normal != null) sm.SetShaderParameter(ShaderParam.NormalTex, normal);
        sm.SetShaderParameter(ShaderParam.SpecularOn, m.SpecularOn && m.Specular != null);
        sm.SetShaderParameter(ShaderParam.MatSpecular, V(m.Specular, 0f));
        sm.SetShaderParameter(ShaderParam.SpecPower, m.Glossiness > 0 ? m.Glossiness : 1f);
        var gloss = m.Textures.TryGetValue("gloss", out var gn) ? Texture(gn) : null;
        sm.SetShaderParameter(ShaderParam.HasGloss, gloss != null);
        if (gloss != null) sm.SetShaderParameter(ShaderParam.GlossTex, gloss);
        var hair = m.Material == "MS2CharacterHairMaterial" && m.Tangents != null && m.Textures.TryGetValue("shader1", out var hn) ? Texture(hn) : null;
        sm.SetShaderParameter(ShaderParam.HasHair, hair != null);
        if (hair != null) sm.SetShaderParameter(ShaderParam.HairTex, hair);
        // MS2StandardMaterial (and its alpha-boost name) adds the glow map's colour after lighting
        // (HandleMS2PostLightTextureApplication, ms2standardmaterial.cpp:1542-1555; NiStandardMaterial::HandleGlowMap:
        // TEXTURE_RGB_APPLY_ADD of the glow rgb), scaled by the GlowModifier attribute when the mesh has one (:252-257,
        // HandleGlowModifier :1353-1363)
        var glow = m.Material is "MS2StandardMaterial" or "MS2AlphaBoostMaterial" && m.Textures.TryGetValue("glow", out var gwn) ? Texture(gwn) : null;
        sm.SetShaderParameter(ShaderParam.HasGlow, glow != null);
        if (glow != null)
        {
            sm.SetShaderParameter(ShaderParam.GlowTex, glow);
            sm.SetShaderParameter(ShaderParam.GlowScale, m.ExtraFloats.GetValueOrDefault("GlowModifier", 1f));
        }
        bool test = m.AlphaTestOn || m.AlphaTest;
        sm.SetShaderParameter(ShaderParam.AlphaTest, test);
        sm.SetShaderParameter(ShaderParam.AlphaRef, m.AlphaTestOn && m.AlphaThreshold > 0 ? m.AlphaThreshold / 255f : 0.5f);
        return sm;
    }

    /// <summary>Meshes the client gives the player's skin colours (its skin colour walk only touches this material).</summary>
    public static bool IsSkinMaterial(ModelMesh m) => m.Material == "MS2CharacterSkinMaterial";

    readonly Dictionary<string, Texture2DArray> flipArrays = new();

    /// <summary>A flipbook's frames as one texture array, or null when a frame is missing or the sizes differ.</summary>
    Texture2DArray FlipArray(string[] frames)
    {
        string key = string.Join("|", frames);
        if (flipArrays.TryGetValue(key, out var arr)) return arr;
        var images = new Godot.Collections.Array<Image>();
        foreach (var f in frames)
        {
            var img = Texture(f)?.GetImage();
            if (img == null || (images.Count > 0 && (img.GetSize() != images[0].GetSize() || img.GetFormat() != images[0].GetFormat()
                                                    || img.HasMipmaps() != images[0].HasMipmaps())))
            {
                images.Clear();
                break;
            }
            images.Add(img);
        }
        if (images.Count > 0)
        {
            arr = new Texture2DArray();
            if (arr.CreateFromImages(images) != Error.Ok) arr = null;
        }
        else arr = null;
        flipArrays[key] = arr;
        return arr;
    }

    public Texture2D Texture(string fileName)
    {
        if (textures.TryGetValue(fileName, out var t)) return t;
        Texture2D tex = null;
        string entry = null, archive = null;
        foreach (var a in TextureArchives)
            if ((entry = packs.FindByFileName(a, fileName)) != null) { archive = a; break; }
        // Packed raster assets are .dds only; a NIF embeds the source name (.psd/.tga), looked up here under the same base name as
        // .dds. Observed in the data, not cited: no client code that maps a NIF texture name to .dds was found (the Gamebryo
        // NiSourceTexture and file-system sources are not in the decomp; textureutil.cpp GetImageConverter disables the NIF
        // image reader); the only extension swap found is Scaleform's UI image loader (sfr_gfx_02e.cpp).
        if (entry == null && !fileName.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
        {
            var dds = System.IO.Path.ChangeExtension(fileName, ".dds");
            foreach (var a in TextureArchives)
                if ((entry = packs.FindByFileName(a, dds)) != null) { archive = a; break; }
        }
        if (entry == null) GD.PushWarning($"texture {fileName} not found in {string.Join(", ", TextureArchives)}");
        else
        {
            var img = new Image();
            var err = img.LoadDdsFromBuffer(packs.Read(archive, entry));
            if (err != Error.Ok) GD.PushWarning($"texture {entry}: {err}");
            else tex = ImageTexture.CreateFromImage(img);
        }
        textures[fileName] = tex;
        return tex;
    }

    /// <summary>Samples a KF sequence into a looping Godot animation on the model's skeleton bones.</summary>
    public static Animation BuildAnimation(KfAnimation kf, ModelData md, double fps = 30.0)
    {
        var anim = new Animation { Length = (float)kf.Duration, LoopMode = Animation.LoopModeEnum.Linear };
        int nfr = Math.Max(2, (int)Math.Round(kf.Duration * fps) + 1);
        foreach (var (node, ch) in kf.Channels)
        {
            if (md.BoneIndex(node) < 0) continue;
            var path = new NodePath($"{SkeletonName}:{node}");
            if (ch.T != null)
            {
                int tr = anim.AddTrack(Animation.TrackType.Position3D);
                anim.TrackSetPath(tr, path);
                for (int f = 0; f < nfr; f++)
                {
                    double t = kf.Duration * f / (nfr - 1);
                    var v = ch.T(t);
                    anim.PositionTrackInsertKey(tr, t, new Vector3((float)v.X, (float)v.Y, (float)v.Z));
                }
            }
            if (ch.R != null)
            {
                int tr = anim.AddTrack(Animation.TrackType.Rotation3D);
                anim.TrackSetPath(tr, path);
                Quaternion? prev = null;
                for (int f = 0; f < nfr; f++)
                {
                    double t = kf.Duration * f / (nfr - 1);
                    var q = ToGodot(ch.R(t));
                    if (prev is Quaternion p && p.Dot(q) < 0) q = -q;
                    prev = q;
                    anim.RotationTrackInsertKey(tr, t, q);
                }
            }
            if (ch.S != null)
            {
                int tr = anim.AddTrack(Animation.TrackType.Scale3D);
                anim.TrackSetPath(tr, path);
                for (int f = 0; f < nfr; f++)
                {
                    double t = kf.Duration * f / (nfr - 1);
                    float s = (float)ch.S(t);
                    anim.ScaleTrackInsertKey(tr, t, new Vector3(s, s, s));
                }
            }
        }
        return anim;
    }
}
