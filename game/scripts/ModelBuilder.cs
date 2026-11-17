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

    /// <summary>Root (Y-up) -> Model (file Z-up, scene root transform) -> Skeleton + meshes; AnimationPlayer under Model.</summary>
    public Node3D Build(ModelData md, string name)
    {
        var root = new Node3D { Name = name };
        var model = new Node3D { Name = "Model", Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero) * ToGodot(md.RootLocal) };
        root.AddChild(model);

        var skel = new Skeleton3D { Name = SkeletonName };
        model.AddChild(skel);
        foreach (var b in md.Bones) skel.AddBone(b.Name);
        for (int i = 0; i < md.Bones.Count; i++)
        {
            skel.SetBoneParent(i, md.Bones[i].Parent);
            skel.SetBoneRest(i, ToGodot(md.Bones[i].Rest));
        }
        skel.ResetBonePoses();

        var skin = new Skin();
        for (int i = 0; i < md.Bones.Count; i++) skin.AddBind(i, ToGodot(md.Bones[i].World).AffineInverse());

        foreach (var m in md.Meshes)
        {
            var mi = new MeshInstance3D { Name = m.Name, Mesh = BuildMesh(m) };
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
            AddGlowTwin(mi, m, m.Skinned ? skin : null);
        }

        var player = new AnimationPlayer { Name = "AnimationPlayer" };
        model.AddChild(player);
        player.RootNode = "..";
        player.AddAnimationLibrary("", new AnimationLibrary());
        return root;
    }

    /// <param name="skin">false: drop bone data (static placement of a skinned prop, drawn in its bind pose)</param>
    /// <param name="instanceColor">field mesh: the MultiMesh instance colour is the material colour (field_lit shader)</param>
    public ArrayMesh BuildMesh(ModelMesh m, bool skin = true, bool instanceColor = false)
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
        if (m.Normals != null && m.Tangents != null && m.Binormals != null && !instanceColor)
        {
            // Godot keeps the tangent and a handedness sign; it rebuilds the binormal as cross(normal, tangent) * sign
            var t = new float[nv * 4];
            for (int i = 0; i < nv; i++)
            {
                var nn = new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
                var tt = new Vector3(m.Tangents[i * 3], m.Tangents[i * 3 + 1], m.Tangents[i * 3 + 2]);
                var bb = new Vector3(m.Binormals[i * 3], m.Binormals[i * 3 + 1], m.Binormals[i * 3 + 2]);
                t[i * 4] = tt.X; t[i * 4 + 1] = tt.Y; t[i * 4 + 2] = tt.Z;
                t[i * 4 + 3] = nn.Cross(tt).Dot(bb) < 0 ? -1f : 1f;
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
        if (instanceColor)
        {
            // field meshes: second uv set as UV2, vertex colours as CUSTOM0 so that COLOR stays the instance colour
            if (m.Uvs1 != null)
            {
                var uv2 = new Vector2[nv];
                for (int i = 0; i < nv; i++) uv2[i] = new Vector2(m.Uvs1[i * 2], m.Uvs1[i * 2 + 1]);
                arrays[(int)Mesh.ArrayType.TexUV2] = uv2;
            }
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
        sm.SetShaderParameter("has_tex", tex != null);
        if (tex != null) sm.SetShaderParameter("base_tex", tex);
        sm.SetShaderParameter("glow_tex", glow);
        sm.SetShaderParameter("alpha_test", m.AlphaTestOn || m.AlphaBlendOn || m.AlphaTest);
        return sm;
    }

    static Shader fieldShader, fieldBlendShader, fieldAddShader;

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
        }
        if (!m.AlphaBlendOn) return fieldShader;
        return ((m.AlphaFlags >> 5) & 15) == 0 ? fieldAddShader : fieldBlendShader;
    }

    Material BuildMaterial(ModelMesh m, bool instanceColor)
    {
        Texture2D tex = m.Textures.TryGetValue("base", out var baseName) ? Texture(baseName) : null;
        if (instanceColor)
        {
            // field meshes: Gamebryo-style gamma-space lighting (shaders/field_lit.gdshader)
            var sm = new ShaderMaterial { Shader = FieldShader(m) };
            sm.SetShaderParameter("has_tex", tex != null);
            if (tex != null) sm.SetShaderParameter("base_tex", tex);
            var dark = m.Textures.TryGetValue("dark", out var darkName) ? Texture(darkName) : null;
            sm.SetShaderParameter("has_dark", dark != null);
            if (dark != null) sm.SetShaderParameter("dark_tex", dark);
            sm.SetShaderParameter("dark_uv2", m.Uvs1 != null);
            var decal = m.Textures.TryGetValue("decal0", out var decalName) ? Texture(decalName) : null;
            sm.SetShaderParameter("has_decal", decal != null);
            if (decal != null) sm.SetShaderParameter("decal_tex", decal);
            sm.SetShaderParameter("has_vcolor", m.Colors != null);
            sm.SetShaderParameter("vcolor_amb_diff", m.VertexColorsAmbDiff);
            // alpha test as flagged; a material with an alpha property but neither flag keeps the old 0.5 cut-out
            bool test = m.AlphaTestOn || (m.AlphaTest && !m.AlphaBlendOn);
            sm.SetShaderParameter("alpha_test", test);
            sm.SetShaderParameter("alpha_ref", m.AlphaTestOn && m.AlphaThreshold > 0 ? m.AlphaThreshold / 255f : 0.5f);
            if (m.Emissive != null) sm.SetShaderParameter("emissive", new Vector3(m.Emissive[0], m.Emissive[1], m.Emissive[2]));
            if (m.Ambient != null) sm.SetShaderParameter("mat_ambient", new Vector3(m.Ambient[0], m.Ambient[1], m.Ambient[2]));
            if (m.Diffuse != null) sm.SetShaderParameter("mat_diffuse", new Vector3(m.Diffuse[0], m.Diffuse[1], m.Diffuse[2]));
            var glow = m.Textures.TryGetValue("glow", out var glowName) ? Texture(glowName) : null;
            sm.SetShaderParameter("has_glow", glow != null);
            if (glow != null) sm.SetShaderParameter("glow_tex", glow);
            int flipSlot = m.FlipSlot == "base" ? 0 : m.FlipSlot == "dark" ? 1 : -1;
            var flip = flipSlot >= 0 ? FlipArray(m.FlipFrames) : null;
            if (flip != null)
            {
                sm.SetShaderParameter("flip_tex", flip);
                sm.SetShaderParameter("flip_slot", flipSlot);
                sm.SetShaderParameter("flip_count", m.FlipFrames.Length);
                sm.SetShaderParameter("flip_period", m.FlipPeriod);
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
        sm.SetShaderParameter("has_tex", tex != null);
        if (tex != null) sm.SetShaderParameter("base_tex", tex);
        int ctrlIndex = m.ExtraInts.TryGetValue("ColorOverrideMapIndex", out var ci) ? ci : 0;
        var ctrl = m.Textures.TryGetValue("shader" + ctrlIndex, out var ctrlName) ? Texture(ctrlName) : null;
        sm.SetShaderParameter("has_ctrl", ctrl != null);
        if (ctrl != null) sm.SetShaderParameter("ctrl_tex", ctrl);
        for (int i = 0; i < 3; i++)
            if (m.ExtraColors.TryGetValue("OverrideColor" + i, out var c))
                sm.SetShaderParameter("color" + i, new Vector3(c[0], c[1], c[2]));
        Vector3 V(float[] a, float d) => a == null ? new Vector3(d, d, d) : new Vector3(a[0], a[1], a[2]);
        sm.SetShaderParameter("mat_ambient", V(m.Ambient, 1f));
        sm.SetShaderParameter("mat_diffuse", V(m.Diffuse, 1f));
        sm.SetShaderParameter("emissive", V(m.Emissive, 0f));
        sm.SetShaderParameter("fresnel_boost", m.ExtraFloats.GetValueOrDefault("FresnelBoost", 5f));
        sm.SetShaderParameter("fresnel_exponent", m.ExtraFloats.GetValueOrDefault("FresnelExponent", 4f));
        sm.SetShaderParameter("color_boost", m.ExtraFloats.GetValueOrDefault("ColorBoost", 1f));
        sm.SetShaderParameter("skin", IsSkinMaterial(m));
        // normal map and specular (on with an enabled NiSpecularProperty; the gloss map scales it; hair adds the
        // anisotropic term from its tangent map, custom map 1)
        var normal = m.Tangents != null && m.Textures.TryGetValue("normal", out var nn) ? Texture(nn) : null;
        sm.SetShaderParameter("has_normal", normal != null);
        if (normal != null) sm.SetShaderParameter("normal_tex", normal);
        sm.SetShaderParameter("specular_on", m.SpecularOn && m.Specular != null);
        sm.SetShaderParameter("mat_specular", V(m.Specular, 0f));
        sm.SetShaderParameter("spec_power", m.Glossiness > 0 ? m.Glossiness : 1f);
        var gloss = m.Textures.TryGetValue("gloss", out var gn) ? Texture(gn) : null;
        sm.SetShaderParameter("has_gloss", gloss != null);
        if (gloss != null) sm.SetShaderParameter("gloss_tex", gloss);
        var hair = m.Material == "MS2CharacterHairMaterial" && m.Tangents != null && m.Textures.TryGetValue("shader1", out var hn) ? Texture(hn) : null;
        sm.SetShaderParameter("has_hair", hair != null);
        if (hair != null) sm.SetShaderParameter("hair_tex", hair);
        bool test = m.AlphaTestOn || m.AlphaTest;
        sm.SetShaderParameter("alpha_test", test);
        sm.SetShaderParameter("alpha_ref", m.AlphaTestOn && m.AlphaThreshold > 0 ? m.AlphaThreshold / 255f : 0.5f);
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
