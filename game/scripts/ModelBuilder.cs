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

    static readonly Dictionary<string, NifSpec> specs = new(StringComparer.Ordinal);

    /// <summary>
    /// The one parsed spec of a nif.xml for the process (main thread). Every model build (the field, the HUD portrait, a
    /// dialog's avatar) reads the same file, and its field and expression lists are filled on first use, so a new
    /// NifSpec per build parsed the file again and refilled them.
    /// </summary>
    public static NifSpec SharedSpec(string path) => specs.TryGetValue(path, out var s) ? s : specs[path] = new NifSpec(path);

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

    public ModelData LoadModel(string entry) => LoadModel(ModelArchive, entry);

    public ModelData LoadModel(string archive, string entry) =>
        keep == null ? ModelData.Load(NifFile.Load(spec, packs.Read(archive, entry)))
                     : keep.Get(keep.Models, (archive + "/" + entry).ToLowerInvariant(), () => ModelData.Load(NifFile.Load(spec, packs.Read(archive, entry))));

    public KfAnimation LoadKf(string entry) => KfAnimation.Load(NifFile.Load(spec, packs.Read(ModelArchive, entry)));

    /// <summary>A KF sequence sampled on a model (<see cref="BuildAnimation"/>); a kept builder hands out the one it built for the same model.</summary>
    public Animation KfAnimationFor(string entry, ModelData md) =>
        keep == null ? BuildAnimation(LoadKf(entry), md)
                     : keep.Get(keep.Animations, (entry, md), () => BuildAnimation(LoadKf(entry), md));

    /// <summary>The mesh of a model mesh (<see cref="BuildMesh"/> with its defaults); a kept builder hands out the one it built before.</summary>
    public ArrayMesh SharedMesh(ModelMesh m)
    {
        if (keep == null) return BuildMesh(m);
        keep.Used.Add(m);
        if (keep.Meshes.TryGetValue(m, out var am)) { keep.Used.UnionWith(keep.MeshUses[m]); return am; }
        keep.Recording = new List<object>();
        try { am = BuildMesh(m); }
        finally { keep.MeshUses[m] = keep.Recording; keep.Recording = null; }
        return keep.Meshes[m] = am;
    }

    /// <summary>
    /// What a kept builder holds: the parsed models, their sampled animations and built meshes, and (with
    /// <see cref="textures"/>) the textures. <see cref="Trim"/> drops what the last look did not use, so it holds one look.
    /// </summary>
    sealed class Kept
    {
        public readonly Dictionary<string, ModelData> Models = new();     // lower-case archive/entry
        public readonly Dictionary<(string, ModelData), Animation> Animations = new();
        public readonly Dictionary<ModelMesh, ArrayMesh> Meshes = new();
        public readonly Dictionary<ModelMesh, List<object>> MeshUses = new();   // the textures and flipbooks a kept mesh's material holds
        public readonly HashSet<object> Used = new();
        public List<object> Recording;

        public void Use(object key) { Used.Add(key); Recording?.Add(key); }

        public V Get<K, V>(Dictionary<K, V> d, K key, Func<V> make)
        {
            Used.Add(key);
            if (!d.TryGetValue(key, out var v)) d[key] = v = make();
            return v;
        }

        public void Trim<K, V>(Dictionary<K, V> d)
        {
            foreach (var k in d.Keys.Where(k => !Used.Contains(k)).ToList()) d.Remove(k);
        }
    }
    Kept keep;

    static ModelBuilder ui;

    /// <summary>
    /// The builder of the UI's character views (the HUD portrait, a dialog's avatar), kept while they show the same look:
    /// each of them built the player again from the pack, parsing the body, its animations and every item model and
    /// loading every texture once per open. It holds the last look only (<see cref="Trim"/>) and is dropped on logout
    /// and character choice (<see cref="DropUi"/>). Main thread. Each look build starts with a new builder's texture
    /// archive order (the EquipApplier of the last look put the Item archive first), so the body's textures are looked up
    /// as a new builder looks them up.
    /// </summary>
    public static ModelBuilder Ui(PackSet packs, string nifXml)
    {
        var spec = SharedSpec(nifXml);
        if (ui == null || ui.packs != packs || ui.spec != spec) ui = new ModelBuilder(spec, packs) { keep = new Kept() };
        ui.TextureArchives = new[] { TextureArchive };
        return ui;
    }

    /// <summary>Drops the UI builder's kept look: on leaving the game (logout, character select), on choosing a character
    /// and on a session reset. A dropped game connection does not drop it: the look stays kept (one look, see
    /// <see cref="Trim"/>) until one of those, and the next look build trims it to the new look anyway.</summary>
    public static void DropUi() => ui = null;

    /// <summary>The keys of what a kept builder holds (models by archive/entry, animations by entry), for checks.</summary>
    public IEnumerable<string> KeptKeys() => keep == null ? Enumerable.Empty<string>()
        : keep.Models.Keys.Concat(keep.Animations.Keys.Select(k => k.Item1)).Concat(textures.Keys);

    /// <summary>A kept builder drops the models, animations, meshes and textures the build since the last Trim did not use.</summary>
    public void Trim()
    {
        if (keep == null) return;
        keep.Trim(keep.Models); keep.Trim(keep.Animations); keep.Trim(keep.Meshes); keep.Trim(keep.MeshUses);
        foreach (var k in flipArrays.Keys.Where(k => !keep.Used.Contains("flip:" + k)).ToList()) flipArrays.Remove(k);
        foreach (var k in textures.Keys.Where(k => !keep.Used.Contains("tex:" + k.ToLowerInvariant())).ToList()) textures.Remove(k);
        keep.Used.Clear();
    }

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
            var mi = new MeshInstance3D { Name = m.Name, Mesh = SharedMesh(m) };
            // a "NoShadow"-tagged subtree never gets the caster mark (ModelMesh.NoShadow)
            if (m.NoShadow) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
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
            Attached(mi, m, skel, b => b);
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
    /// <param name="vertexColor">the vertex colours as COLOR without instanceColor (effect meshes whose material takes them)</param>
    public ArrayMesh BuildMesh(ModelMesh m, bool skin = true, bool instanceColor = false, bool uv2 = false, bool vertexColor = false)
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
        // character meshes carry COLOR where their vertex colours stand in for a colour term or the opacity (CharacterMaterial)
        if ((vertexColor || m.VertexColors.Any) && !instanceColor && m.Colors != null)
        {
            var c = new Color[nv];
            for (int i = 0; i < nv; i++) c[i] = new Color(m.Colors[i * 4], m.Colors[i * 4 + 1], m.Colors[i * 4 + 2], m.Colors[i * 4 + 3]);
            arrays[(int)Mesh.ArrayType.Color] = c;
        }
        if (instanceColor)
        {
            // field meshes: second uv set as UV2, vertex colours as CUSTOM0 so that COLOR stays the instance colour
            // and a keyed morph's targets 1-3 as offsets in CUSTOM1-3 (field_morph)
            if (m.Colors != null) arrays[(int)Mesh.ArrayType.Custom0] = m.Colors;
            if (m.Morph != null)
                for (int t = 0; t < m.Morph.Deltas.Length; t++) arrays[(int)Mesh.ArrayType.Custom1 + t] = m.Morph.Deltas[t];
            format |= FieldCustomFormat(m);
        }
        // file triangles are counter-clockwise (as in the glTF export); Godot's front faces are clockwise
        var idx = (int[])m.Indices.Clone();
        for (int i = 0; i + 2 < idx.Length; i += 3) (idx[i + 1], idx[i + 2]) = (idx[i + 2], idx[i + 1]);
        arrays[(int)Mesh.ArrayType.Index] = idx;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, (Mesh.ArrayFormat)format);
        mesh.SurfaceSetMaterial(0, BuildMaterial(m, instanceColor));
        if (instanceColor && m.Billboard >= 0) mesh.CustomAabb = BillboardAabb(m);
        else if (instanceColor && m.Morph != null) mesh.CustomAabb = MorphAabb(m);
        return mesh;
    }

    static readonly int[] CustomShift =
    {
        (int)Mesh.ArrayFormat.FormatCustom0Shift, (int)Mesh.ArrayFormat.FormatCustom1Shift,
        (int)Mesh.ArrayFormat.FormatCustom2Shift, (int)Mesh.ArrayFormat.FormatCustom3Shift,
    };

    /// <summary>The custom array formats of a field mesh: rgba vertex colours in CUSTOM0, morph offsets (xyz) in CUSTOM1-3.</summary>
    public static ulong FieldCustomFormat(ModelMesh m)
    {
        ulong format = 0;
        if (m.Colors != null) format |= (ulong)Mesh.ArrayCustomFormat.RgbaFloat << CustomShift[0];
        if (m.Morph != null)
            for (int t = 0; t < m.Morph.Deltas.Length; t++) format |= (ulong)Mesh.ArrayCustomFormat.RgbFloat << CustomShift[t + 1];
        return format;
    }

    /// <summary>A morph target's weight range: its key values, each Bezier segment's extremes between them, or its fixed
    /// weight.</summary>
    internal static (float lo, float hi) MorphRange(MorphAnim a, int t)
    {
        if (a.Weights[t] is not { } k) return (a.Static[t], a.Static[t]);
        float lo = k.Values.Min(), hi = k.Values.Max();
        if (k.KeyType == 2 && k.InTans != null && k.OutTans != null)
            for (int j = 0; j + 1 < k.Values.Length; j++)
            {
                var (sl, sh) = BezierSegmentRange(k.Values[j], k.Values[j + 1], k.OutTans[j], k.InTans[j + 1]);
                lo = Mathf.Min(lo, sl); hi = Mathf.Max(hi, sh);
            }
        return (lo, hi);
    }

    /// <summary>The range of one Bezier key segment as field_uv's key_segment draws it (NiInterpScalar::Bezier,
    /// NiInterpScalar.cpp:26-36): f(u) = p + (dp + (A + B u) u) u on 0..1, from its ends and the roots of f'(u) =
    /// dp + 2 A u + 3 B u^2 inside the segment.</summary>
    static (float lo, float hi) BezierSegmentRange(float p, float q, float dp, float dq)
    {
        float A = 3f * (q - p) - (dq + 2f * dp), B = dp + dq - 2f * (q - p);
        float lo = Mathf.Min(p, q), hi = Mathf.Max(p, q);
        void At(float u)
        {
            if (!(u > 0f && u < 1f)) return;
            float f = p + (dp + (A + B * u) * u) * u;
            lo = Mathf.Min(lo, f); hi = Mathf.Max(hi, f);
        }
        if (Mathf.Abs(B) < 1e-9f) { if (Mathf.Abs(A) > 1e-9f) At(-dp / (2f * A)); }
        else
        {
            float disc = A * A - 3f * B * dp;
            if (disc >= 0f) { float sq = Mathf.Sqrt(disc); At((-A + sq) / (3f * B)); At((-A - sq) / (3f * B)); }
        }
        return (lo, hi);
    }

    /// <summary>A morphing mesh's bounds in its own space: each vertex moved by every target over its weight range.</summary>
    static Aabb MorphAabb(ModelMesh m)
    {
        var w = MorphRanges(m.Morph);
        var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var hi = -lo;
        for (int i = 0; i + 2 < m.Positions.Length; i += 3)
        {
            var (vl, vh) = MorphBox(m, w, i);
            if (!float.IsFinite(vl.X + vl.Y + vl.Z + vh.X + vh.Y + vh.Z)) continue;   // a NaN target vertex draws nothing
            lo = lo.Min(vl);
            hi = hi.Max(vh);
        }
        return lo.X > hi.X ? new Aabb() : new Aabb(lo, hi - lo);
    }

    static (float lo, float hi)[] MorphRanges(MorphAnim a)
    {
        var w = new (float lo, float hi)[a.Deltas.Length];
        for (int t = 0; t < w.Length; t++) w[t] = MorphRange(a, t);
        return w;
    }

    /// <summary>The box one vertex (at Positions[i]) moves in: its rest position plus every target's offset over that
    /// target's weight range w.</summary>
    static (Vector3 lo, Vector3 hi) MorphBox(ModelMesh m, (float lo, float hi)[] w, int i)
    {
        Vector3 vl = new(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]), vh = vl;
        for (int t = 0; t < w.Length; t++)
        {
            var d = new Vector3(m.Morph.Deltas[t][i], m.Morph.Deltas[t][i + 1], m.Morph.Deltas[t][i + 2]);
            Vector3 p = d * w[t].lo, q = d * w[t].hi;
            vl += p.Min(q);
            vh += p.Max(q);
        }
        return (vl, vh);
    }

    /// <summary>A keyed morph (ModelMesh.Morph) for field_morph: the controller time, the fixed weights and each target's keys.</summary>
    static void SetMorph(ShaderMaterial sm, ModelMesh m)
    {
        if (m.Morph is not { } a) return;
        var keys = Vector3I.Zero;
        var kv = new Vector4[96];
        var fixedW = Vector3.Zero;
        for (int t = 0; t < a.Deltas.Length; t++)
        {
            fixedW[t] = a.Static[t];
            if (a.Weights[t] is not { } k) continue;
            keys[t] = k.Times.Length * 4 + k.KeyType;
            for (int j = 0; j < k.Times.Length; j++)
                kv[32 * t + j] = new Vector4(k.Times[j], k.Values[j], k.InTans?[j] ?? 0f, k.OutTans?[j] ?? 0f);
        }
        sm.SetShaderParameter(ShaderParam.MorphCount, a.Deltas.Length);
        sm.SetShaderParameter(ShaderParam.MorphTime, new Vector4(a.Start, a.Stop, a.Frequency, a.Phase));
        sm.SetShaderParameter(ShaderParam.MorphClamp, a.Clamp);
        sm.SetShaderParameter(ShaderParam.MorphStatic, fixedW);
        sm.SetShaderParameter(ShaderParam.MorphKeys, keys);
        sm.SetShaderParameter(ShaderParam.MorphKey, kv);
    }

    /// <summary>A billboard mesh's bounds in its own space: the sphere it sweeps as it turns about its billboard node's
    /// origin (field_billboard), so a turned billboard is not culled by its rest bounds.</summary>
    internal static Aabb BillboardAabb(ModelMesh m)
    {
        var x = ToGodot(m.InBillboard);
        float r = 0;
        if (m.Morph != null)
        {
            // a morphed billboard turns its morphed vertices (field_lit morphs, then turns): the radius takes every corner
            // of each vertex's morph box, which holds the vertex at any target weights
            var w = MorphRanges(m.Morph);
            for (int i = 0; i + 2 < m.Positions.Length; i += 3)
            {
                var (vl, vh) = MorphBox(m, w, i);
                if (!float.IsFinite(vl.X + vl.Y + vl.Z + vh.X + vh.Y + vh.Z)) continue;
                for (int k = 0; k < 8; k++)
                    r = Mathf.Max(r, (x * new Vector3((k & 1) != 0 ? vh.X : vl.X, (k & 2) != 0 ? vh.Y : vl.Y, (k & 4) != 0 ? vh.Z : vl.Z)).Length());
            }
        }
        else
            for (int i = 0; i + 2 < m.Positions.Length; i += 3)
                r = Mathf.Max(r, (x * new Vector3(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2])).Length());
        var inv = x.AffineInverse();
        var c = inv.Origin;
        float e = r * inv.Basis.Scale.X;
        return new Aabb(c - new Vector3(e, e, e), new Vector3(2 * e, 2 * e, 2 * e));
    }

    /// <summary>A mesh with a glow map also joins the glow pass: a twin on the glow layer sharing its mesh, parent and skin.</summary>
    public void AddGlowTwin(MeshInstance3D mi, ModelMesh m, Skin skin, float frozenAt = -1f)
    {
        bool uv2 = mi.Mesh is ArrayMesh am && (am.SurfaceGetFormat(0) & Mesh.ArrayFormat.FormatTexUV2) != 0;
        bool field = !(mi.Mesh?.SurfaceGetMaterial(0) is ShaderMaterial cm && IsCharacterShader(cm.Shader));
        if (!GlowPass.Wanted || GlowMaterial(m, uv2, frozenAt, field) is not { } gm) return;
        var g = new MeshInstance3D { Name = mi.Name + "_glow", Mesh = mi.Mesh, Layers = GlowPass.Bit, MaterialOverride = gm, Transform = mi.Transform, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        mi.GetParent().AddChild(g);
        if (skin != null) { g.Skin = skin; g.Skeleton = ".."; }
    }

    static string glowCode;
    static readonly Dictionary<string, Shader> glowShaders = new();

    /// <summary>The glow shader with the mesh's cull define (CullDefine): the glow pass culls as the colour pass does; and the
    /// BLEND form (with ZWRITE for the z write bit) for a blended mesh.</summary>
    static Shader GlowVariant(string cull, bool blend, bool zwrite)
    {
        var defines = (cull != null ? $"#define {cull}\n" : "") + (blend ? "#define BLEND\n" : "") + (blend && zwrite ? "#define ZWRITE\n" : "");
        if (glowShaders.TryGetValue(defines, out var sh)) return sh;
        glowCode ??= GD.Load<Shader>("res://shaders/field_glow.gdshader").Code;
        int nl = glowCode.IndexOf('\n', glowCode.IndexOf("shader_type")) + 1;
        glowShaders[defines] = sh = new Shader { Code = defines.Length == 0 ? glowCode : glowCode.Insert(nl, defines) };
        return sh;
    }

    // the maps field_uv transforms, in its map order
    static readonly string[] UvMaps = { "base", "dark", "glow", "decal0" };
    // field_uv's UV_CTL_MAX: the uv controllers one material takes
    const int UvCtlMax = 12;

    /// <summary>A mesh under an NiBillboardNode (ModelMesh.Billboard): its face mode and its transform below that node, for
    /// field_billboard, which turns it to the camera in the vertex shader.</summary>
    static void SetBillboard(ShaderMaterial sm, ModelMesh m)
    {
        if (m.Billboard < 0) return;
        var x = ToGodot(m.InBillboard);
        sm.SetShaderParameter(ShaderParam.BbMode, m.BillboardMode);
        sm.SetShaderParameter(ShaderParam.BbX, new Projection(x));
        sm.SetShaderParameter(ShaderParam.BbXInv, new Projection(x.AffineInverse()));
    }

    /// <summary>
    /// The texture transforms of the maps the field shaders draw (field_uv): each map's static NiTextureTransform, and its
    /// running uv controllers over it. A map a controller moves but that has no transform gets the controller's default
    /// one (NiTextureTransformController.cpp:146-151, TexTransform.ControllerDefault). A CLAMP controller (ModelMesh.UvHolds)
    /// holds its last key in the static transform: the field's app time is past its stop (ModelMesh.DrawnTransform).
    /// STILL-OPEN: the shader takes at most UvCtlMax (12, the most one texturing property has in the KR data) controllers
    /// of up to 4 keys each; others keep the static value (field scan: a 5-key translate, on a base map in one model and a
    /// glow map in another: 2 models, 5 entities).
    /// </summary>
    static void SetUvTransforms(ShaderMaterial sm, ModelMesh m)
    {
        var tt = new Vector4[8];
        var method = Vector4I.Zero;
        int[] target = new int[UvCtlMax], keys = new int[UvCtlMax];
        Vector4[] time = new Vector4[UvCtlMax], kt = new Vector4[UvCtlMax], kv = new Vector4[UvCtlMax],
            kin = new Vector4[UvCtlMax], kout = new Vector4[UvCtlMax];
        int n = 0;
        bool any = false;
        for (int map = 0; map < UvMaps.Length; map++)
        {
            m.Maps.TryGetValue(UvMaps[map], out var tm);
            bool moved = false;
            foreach (var ua in m.UvAnims)
            {
                if (ua.Slot != UvMaps[map] || ua.Times.Length > 4 || n == UvCtlMax) continue;
                moved = true;
                target[n] = map * 8 + ua.Operation;
                keys[n] = ua.Times.Length * 4 + ua.KeyType;
                time[n] = new Vector4(ua.Start, ua.Stop, ua.Frequency, ua.Phase);
                for (int k = 0; k < ua.Times.Length; k++)
                {
                    kt[n][k] = ua.Times[k];
                    kv[n][k] = ua.Values[k];
                    if (ua.InTans != null) { kin[n][k] = ua.InTans[k]; kout[n][k] = ua.OutTans[k]; }
                }
                n++;
            }
            var t = m.DrawnTransform(UvMaps[map], moved);
            if (t == null) continue;
            any = true;
            method[map] = t.Method + 1;
            tt[2 * map] = new Vector4(t.TU, t.TV, t.SU, t.SV);
            tt[2 * map + 1] = new Vector4(t.Rotate, t.CU, t.CV, 0f);
        }
        if (!any) return;
        sm.SetShaderParameter(ShaderParam.UvTt, tt);
        sm.SetShaderParameter(ShaderParam.UvTtMethod, method);
        if (n == 0) return;
        sm.SetShaderParameter(ShaderParam.UvCtlCount, n);
        sm.SetShaderParameter(ShaderParam.UvCtlTarget, target);
        sm.SetShaderParameter(ShaderParam.UvCtlKeys, keys);
        sm.SetShaderParameter(ShaderParam.UvCtlTime, time);
        sm.SetShaderParameter(ShaderParam.UvCtlT, kt);
        sm.SetShaderParameter(ShaderParam.UvCtlV, kv);
        sm.SetShaderParameter(ShaderParam.UvCtlIn, kin);
        sm.SetShaderParameter(ShaderParam.UvCtlOut, kout);
    }

    /// <summary>The glow pass material of a mesh with a glow map (shaders/field_glow), or null.
    /// Null as well for a mesh the client's glow click leaves out (FUN_1414cffc0, ms2materialswapprocessor.cpp:158-170): one
    /// that blends other than SRCALPHA / INVSRCALPHA (FUN_141567260, unit_141567000.cpp:158-168). Particle systems, the
    /// predicate's other case, are not NiMesh and never reach here. A static entity's (frozenAt >= 0) holds its uv
    /// controllers at that time, as its field material does (<see cref="Freeze"/>).
    /// STILL-OPEN: a map on uv set 2 or above reads UV (the meshes carry two uv sets).</summary>
    /// <param name="uv2">the mesh carries its second uv set as UV2</param>
    /// <param name="field">a field mesh (vertex colours in CUSTOM0, field_lit's opacity rule) rather than a character mesh (COLOR)</param>
    public Material GlowMaterial(ModelMesh m, bool uv2, float frozenAt = -1f, bool field = true)
    {
        if (!m.Textures.TryGetValue("glow", out var glowName) || Texture(glowName) is not { } glow) return null;
        if (m.AlphaBlendOn && (((m.AlphaFlags >> 1) & 15) != 6 || ((m.AlphaFlags >> 5) & 15) != 7)) return null;   // NiAlphaProperty SRCALPHA 6, INVSRCALPHA 7
        var sm = new ShaderMaterial { Shader = GlowVariant(CullDefine(m), m.AlphaBlendOn, m.ZWrite) };
        bool OnUv2(string key) => uv2 && m.Maps.TryGetValue(key, out var map) && map.UvSet == 1;
        var tex = m.Textures.TryGetValue("base", out var baseName) ? Texture(baseName) : null;
        sm.SetShaderParameter(ShaderParam.HasTex, tex != null);
        if (tex != null) sm.SetShaderParameter(ShaderParam.BaseTex, tex);
        sm.SetShaderParameter(ShaderParam.BaseUv2, OnUv2("base"));
        sm.SetShaderParameter(ShaderParam.GlowTex, glow);
        sm.SetShaderParameter(ShaderParam.GlowUv2, OnUv2("glow"));
        // with a detail map the glow is Detail.a * Glow.a (MS2GlowMaterial::HandleGlowColor @0x1427761b0)
        var detail = m.Textures.TryGetValue("detail", out var detailName) ? Texture(detailName) : null;
        sm.SetShaderParameter(ShaderParam.HasDetail, detail != null);
        if (detail != null) sm.SetShaderParameter(ShaderParam.DetailTex, detail);
        sm.SetShaderParameter(ShaderParam.DetailUv2, OnUv2("detail"));
        // the glow pass ends in the same alpha test as the colour pass (MS2GlowMaterial -> HandleMS2FinalPixelOutputs ->
        // HandleAlphaTest with the property's function and reference, ms2standardmaterial.cpp:786)
        // and tests (and blends) the colour pass's opacity: field_lit's for a field mesh (STILL-OPEN there: VertexColorsAmbDiff),
        // character_lit's (ModelMesh.VertexColors) for a character mesh
        sm.SetShaderParameter(ShaderParam.MatAlpha, m.MaterialAlpha);
        SetAlphaAnim(sm, m);
        sm.SetShaderParameter(ShaderParam.VcSource, m.Colors == null ? 0 : field ? 1 : m.VertexColors.Any ? 2 : 0);
        sm.SetShaderParameter(ShaderParam.VcOpacity, field ? m.VertexColorsAmbDiff : m.VertexColors.Opacity);
        sm.SetShaderParameter(ShaderParam.AlphaTest, m.AlphaTests);
        sm.SetShaderParameter(ShaderParam.AlphaRef, m.AlphaTestRef);
        SetAlphaClip(sm, m);
        SetUvTransforms(sm, m);
        SetBillboard(sm, m);
        SetMorph(sm, m);
        if (frozenAt >= 0 && m.AnimatesMaterial) sm.SetShaderParameter(ShaderParam.FrozenTime, frozenAt);
        return sm;
    }

    static string fieldCode;
    static readonly Dictionary<string, Shader> fieldShaders = new();
    static readonly Dictionary<Shader, string> fieldCull = new();

    /// <summary>The field shader compiled with <paramref name="forms"/> (BLEND, ADD, SHELL, FADE defines) and a cull define.</summary>
    static Shader FieldVariant(string forms, string cull)
    {
        var key = forms + "|" + cull;
        if (fieldShaders.TryGetValue(key, out var sh)) return sh;
        fieldCode ??= GD.Load<Shader>("res://shaders/field_lit.gdshader").Code;
        int nl = fieldCode.IndexOf('\n', fieldCode.IndexOf("shader_type")) + 1;
        var defines = forms + (cull != null ? $"#define {cull}\n" : "");
        fieldShaders[key] = sh = new Shader { Code = defines.Length == 0 ? fieldCode : fieldCode.Insert(nl, defines) };
        fieldCull[sh] = cull;
        return sh;
    }

    /// <summary>The cull define for a mesh's NiStencilProperty draw mode (ModelMesh.DrawMode), as NiDX9RenderState maps it
    /// (NiDX9RenderState::ApplyStencil, NiDX9RenderState.cpp:592, mapping :380-389, left-handed off): DRAW_BOTH (3) none, DRAW_CW (2) the front faces, DRAW_CCW and
    /// DRAW_CCW_OR_BOTH the back faces (field_lit's default). The same mapping as the effect meshes (EffectInstance.RenderModes).</summary>
    internal static string CullDefine(ModelMesh m) => m.Cull switch { FaceCull.None => "CULL_NONE", FaceCull.Front => "CULL_FRONT", _ => null };

    /// <summary>The field shader for a NiAlphaProperty: opaque, blended, or additive (destination factor ONE), with the mesh's culling.</summary>
    static Shader FieldShader(ModelMesh m)
    {
        var cull = CullDefine(m);
        if (!m.AlphaBlendOn) return FieldVariant("", cull);
        return FieldVariant(((m.AlphaFlags >> 5) & 15) == 0 ? "#define BLEND\n#define ADD\n" : "#define BLEND\n", cull);
    }

    /// <summary>
    /// The time a field's static entities are added at, in seconds wrapped at the shader TIME's default one-hour rollover.
    /// <see cref="Freeze"/> holds their material controllers there. It is wall time since start, not the shader's TIME
    /// (the sum of the frame steps, which differs under --fixed-fps or after a stall). Only the held value matters, as
    /// the client's controllers hold the game time at the add.
    /// </summary>
    public static float FreezeTimeNow() => (float)(Time.GetTicksMsec() / 1000.0 % 3600.0);

    /// <summary>
    /// A static entity's field mesh: every field material on it (and its next passes) holds its controllers at
    /// <paramref name="t"/> (field_lit frozen_time). The mesh's materials must be its own, not shared with a dynamic entity.
    /// </summary>
    public static void Freeze(Mesh mesh, float t)
    {
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            for (var m = mesh.SurfaceGetMaterial(s) as ShaderMaterial; m != null; m = m.NextPass as ShaderMaterial)
                m.SetShaderParameter(ShaderParam.FrozenTime, t);
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

    /// <summary>The alpha test as the D3D11 renderer runs it: the reference is the property's threshold / 255, zero included
    /// (SCM_DEF_ALPHA_TEST_REF, D3D11ShaderConstantMap.cpp:3690-3700), and the test function picks which side of it is clipped
    /// (SCM_DEF_ALPHA_TEST_FUNC, :3630-3687). The taxi booth's glass tests GREATER 0 (flags 0x12ED), so every pixel with any
    /// opacity stays; the old 0.5 fallback for a zero threshold cut its faces away to the centre of the sphere-map glare.</summary>
    static void SetAlphaClip(ShaderMaterial sm, ModelMesh m)
    {
        var (above, below, equal) = m.AlphaTestClip;
        sm.SetShaderParameter(ShaderParam.AlphaClip, new Vector3(above ? 1 : 0, below ? 1 : 0, equal ? 1 : 0));
    }

    /// <summary>
    /// The faded form of a field mesh's colour material (TransparencyMaterial::FUN_141860140): blending on with the mesh's
    /// own NiAlphaProperty blend (an additive one stays additive; a mesh without one gets a default property, source alpha /
    /// inverse source alpha) and the depth write on. The caller sets its alpha with <see cref="SetFade"/>.
    /// </summary>
    public static ShaderMaterial FadeMaterial(ShaderMaterial colour, ModelMesh m)
    {
        var fade = (ShaderMaterial)colour.Duplicate();
        fade.Shader = FieldVariant(m.AlphaBlendOn && ((m.AlphaFlags >> 5) & 15) == 0 ? "#define BLEND\n#define ADD\n#define FADE\n" : "#define BLEND\n#define FADE\n", CullDefine(m));
        fade.SetShaderParameter(ShaderParam.AlphaTest, m.AlphaTests);
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
        // the depth pass culls as its colour pass does
        shell.Shader = FieldVariant("#define BLEND\n#define SHELL\n", colour.Shader != null && fieldCull.TryGetValue(colour.Shader, out var cull) ? cull : null);
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
            SetAlphaAnim(sm, m);
            // only the property's test flag tests (NiStandardMaterialDescriptor.cpp:665-672); without it the mesh is drawn whole
            sm.SetShaderParameter(ShaderParam.AlphaTest, m.AlphaTests);
            sm.SetShaderParameter(ShaderParam.AlphaRef, m.AlphaTestRef);
            SetAlphaClip(sm, m);
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
            SetUvTransforms(sm, m);
            SetBillboard(sm, m);
            SetMorph(sm, m);
            return sm;
        }
        return CharacterMaterial(m, tex);
    }

    /// <summary>A mesh's NiAlphaController (ModelMesh.AlphaAnim) as the ms2_alpha_anim uniforms; up to 8 keys.</summary>
    static void SetAlphaAnim(ShaderMaterial sm, ModelMesh m)
    {
        if (m.AlphaAnim is not { } aa || aa.Times.Length > 8) return;
        static float[] Pad(float[] v) { var r = new float[8]; if (v != null) System.Array.Copy(v, r, v.Length); return r; }
        sm.SetShaderParameter(ShaderParam.AlphaAnimCount, aa.Times.Length);
        sm.SetShaderParameter(ShaderParam.AlphaAnimType, aa.KeyType);
        sm.SetShaderParameter(ShaderParam.AlphaAnimTime, new Vector4(aa.Start, aa.Stop, aa.Frequency, aa.Phase));
        sm.SetShaderParameter(ShaderParam.AlphaAnimT, Pad(aa.Times));
        sm.SetShaderParameter(ShaderParam.AlphaAnimV, Pad(aa.Values));
        sm.SetShaderParameter(ShaderParam.AlphaAnimIn, Pad(aa.InTans));
        sm.SetShaderParameter(ShaderParam.AlphaAnimOut, Pad(aa.OutTans));
    }

    static Shader characterShader;
    static string characterCode;
    static readonly Dictionary<string, Shader> characterShaders = new();

    /// <summary>The render modes and defines of a character mesh's shader form. Blended meshes (ModelMesh.Blend) blend as the
    /// D3D state the NiAlphaProperty factors give (NiDX9RenderState::ApplyAlpha, NiDX9RenderState.cpp:450-464): SRCALPHA /
    /// INVSRCALPHA blend_mix, a source over ONE blend_add, ONE / ONE blend_premul_alpha with ALPHA 0 (dst + src, unweighted).
    /// The z write and z test are the mesh's NiZBufferProperty (NiDX9RenderState::ApplyZBuffer, NiDX9RenderState.cpp:626-647; the render list
    /// processor only re-sorts, it does not change them), as the effect meshes' (EffectInstance.RenderModes); a tested cut-out
    /// with the write bit writes depth in the blended pass as it did when it drew opaque. Every form culls by the mesh's
    /// NiStencilProperty draw mode (ModelMesh.Cull, NiDX9RenderState::ApplyStencil, NiDX9RenderState.cpp:592, mapping :380-389).</summary>
    internal static (string Modes, string Defines) CharacterForm(ModelMesh m) => CharacterForm(m, m.Blend);

    static (string Modes, string Defines) CharacterForm(ModelMesh m, BlendForm blend)
    {
        if (blend == BlendForm.Opaque)
            return (CullMode(m), "");
        var op = blend switch { BlendForm.Mix => "blend_mix", BlendForm.Add => "blend_add", _ => "blend_premul_alpha" };
        return ($"{op}, {Net.EffectInstance.RenderModes(m)}", blend == BlendForm.AddUnweighted ? "#define BLEND\n#define ONE\n" : "#define BLEND\n");
    }

    /// <summary>The character shader in the mesh's form (CharacterForm): its render mode line and defines.</summary>
    static Shader CharacterShader(ModelMesh m)
    {
        var (modes, defines) = CharacterForm(m);
        return CharacterShaderOf(modes, defines);
    }

    static Shader CharacterShaderOf((string Modes, string Defines) form) => CharacterShaderOf(form.Modes, form.Defines);

    static Shader CharacterShaderOf(string modes, string defines)
    {
        characterShader ??= GD.Load<Shader>("res://shaders/character_lit.gdshader");
        if (modes == "cull_back" && defines.Length == 0) return characterShader;
        var key = defines + modes;
        if (characterShaders.TryGetValue(key, out var sh)) return sh;
        characterCode ??= characterShader.Code;
        const string opaque = "render_mode unshaded, cull_back;";
        int nl = characterCode.IndexOf('\n', characterCode.IndexOf("shader_type")) + 1;
        var code = characterCode.Replace(opaque, $"render_mode unshaded, {modes};").Insert(nl, defines);
        return characterShaders[key] = new Shader { Code = code };
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Mesh, ShaderMaterial> casterMaterials = new();

    /// <summary>
    /// What every built character mesh gets once it hangs under its skeleton (Build, EquipApplier.Attach): the shadow caster
    /// of a blended mesh, and the client's alpha sort key (CharacterAlphaSort).
    /// Every blended mesh casts its shadow as the opaque tested form: SetShadowCaster @0x141567330 (unit_141567000.cpp, from
    /// the character and equipment builds) marks every render object under the root with no blend check, skipping only the
    /// "NoShadow" tagged subtrees and the helper objects, and the shadow write draws opaque, cut only by the alpha test
    /// (NiDirectionalShadowWriteMaterial.cpp:405-409: alpha override on, blend off) into a depth-as-colour target
    /// (NiShadowTechnique.cpp:66-71), so the colour pass's z-write bit does not matter. Godot casts no shadow from a
    /// transparent material, so the blended mesh stops casting and a shadow-only twin of the same mesh, skin and culling,
    /// drawn with the opaque form and the same opacity test, casts in its place. A child of the mesh, so it hides, moves and
    /// frees with it.
    /// A blended mesh that <see cref="ShadowCullingKeeps"/> drops casts nothing: no twin, and the mesh itself stops casting.
    /// </summary>
    /// <param name="skelBone">the skeleton's bone index for a bone index of the mesh's model</param>
    public static void Attached(MeshInstance3D mi, ModelMesh m, Skeleton3D skel, System.Func<int, int> skelBone)
    {
        if (m.Blend != BlendForm.Opaque && !ShadowCullingKeeps(m)) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        else if (m.Blend != BlendForm.Opaque && mi.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off
            && mi.Mesh is { } mesh && mesh.SurfaceGetMaterial(0) is ShaderMaterial sm && IsCharacterShader(sm.Shader))
        {
            if (!casterMaterials.TryGetValue(mesh, out var caster))
            {
                caster = (ShaderMaterial)sm.Duplicate();
                caster.Shader = CharacterShaderOf(CullMode(m), "");
                // the shadow write tests the base map's alpha only (NiDirectionalShadowWriteMaterial.cpp:192-229, with ALPHATEST
                // and BASEMAPCOUNT 1, :109-110, :437-438): no material alpha, alpha controller or vertex alpha in its opacity
                caster.SetShaderParameter(ShaderParam.MatAlpha, 1f);
                caster.SetShaderParameter(ShaderParam.AlphaAnimCount, 0);
                caster.SetShaderParameter(ShaderParam.VcOpacity, false);
                casterMaterials.Add(mesh, caster);
            }
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            var t = new MeshInstance3D { Name = mi.Name + "_shadow", Mesh = mesh, MaterialOverride = caster, CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly };
            mi.AddChild(t);
            if (mi.Skin != null) { t.Skin = mi.Skin; t.Skeleton = "../.."; }
        }
        // SetNodeAlpha (NodeFade) turns blending on with the mesh's own NiAlphaProperty factors (SRCALPHA / INVSRCALPHA without
        // one, alphaanimation.cpp:478-545) and skips a material with an NiAlphaController and a node flagged 0x400
        // (IS_PARTICLE_SYSTEM, FUN_14098dfc0 :392-395)
        if (m.AlphaAnim == null && (m.Flags & 0x400) == 0 && mi.Mesh?.SurfaceGetMaterial(0) is ShaderMaterial own && IsCharacterShader(own.Shader))
        {
            var fade = m.Blend != BlendForm.Opaque ? own.Shader
                : CharacterShaderOf(CharacterForm(m, m.BlendDst == 0 ? (m.BlendSrc == 0 ? BlendForm.AddUnweighted : BlendForm.Add) : BlendForm.Mix));
            mi.SetMeta(NodeFade.Meta, fade);
            if (fade != own.Shader) mi.SetMeta(NodeFade.Swap, true);
            // no NiMaterialProperty of its own: it draws with the root's added one (EnsureAlphaProperties :426-441)
            if (m.Diffuse == null) mi.SetMeta(NodeFade.Inherit, true);
        }
        CharacterAlphaSort.Register(mi, m, skel, skelBone);
    }

    /// <summary>
    /// Whether the shadow map's culling pass lets a blended mesh through. MS2::OnInit installs ShadowCullingProcess as the
    /// shadow render culling process (ms2.cpp:1146). Its Process (@0x141753590, unit_141753550.cpp:86-139) skips an object
    /// whose own NiAlphaProperty blends unless FUN_141753a00 (@0x141753a00, :150-157) holds: the object has an
    /// NiMaterialProperty of its own whose alpha IsNearlyEqual 1 (@0x14028d470, commonutil.cpp:669: closer than 1e-6).
    /// A model's effect quads (blended, material alpha 0.2 or 2.3) thus cast nothing. Geometry the client turns transparent
    /// at runtime (TransparencyMaterial, transparencymaterial.cpp:63-71: the 0x10 flag or the HasMS2MapProperties extra
    /// data) still passes; NIF data carries neither mark.
    /// STILL-OPEN: the ancestor test (:91-99) run while CameraMgr's m_27c (camera.cpp:1274) is set: a mesh under a node
    /// with a blending NiAlphaProperty and no material alpha of 1 is skipped too. The client reads the material's current
    /// alpha at cull time; this takes the NIF's, once, at build, so a material alpha an NiAlphaController or SetNodeAlpha
    /// moves to or from 1 does not change whether the mesh casts.
    /// </summary>
    internal static bool ShadowCullingKeeps(ModelMesh m) =>
        !m.AlphaBlendOn || (m.Diffuse != null && MathF.Abs(m.MaterialAlpha - 1f) < 1e-6f);

    static string CullMode(ModelMesh m) => m.Cull switch { FaceCull.None => "cull_disabled", FaceCull.Front => "cull_front", _ => "cull_back" };

    /// <summary>Whether <paramref name="s"/> is the character shader or one of its blended forms.</summary>
    internal static bool IsCharacterShader(Shader s) => s != null && (s == characterShader || characterShaders.ContainsValue(s));

    /// <summary>
    /// Character meshes: shaders/character_lit.gdshader with the NIF's material colours and the shader attributes the
    /// exporter stores as extra data (OverrideColor0..2, FresnelBoost, FresnelExponent, ColorBoost). The dye control map
    /// is the texture slot named by ColorOverrideMapIndex (shader map 0 by default).
    /// </summary>
    ShaderMaterial CharacterMaterial(ModelMesh m, Texture2D tex)
    {
        var sm = new ShaderMaterial { Shader = CharacterShader(m) };
        // the opacity's terms (ModelMesh.Opacity): the material alpha, its NiAlphaController, the vertex colour's share
        sm.SetShaderParameter(ShaderParam.MatAlpha, m.MaterialAlpha);
        SetAlphaAnim(sm, m);
        var vc = m.VertexColors;
        sm.SetShaderParameter(ShaderParam.VcEmissive, vc.Emissive);
        sm.SetShaderParameter(ShaderParam.VcDiffuse, vc.Diffuse);
        sm.SetShaderParameter(ShaderParam.VcAmbient, vc.Ambient);
        sm.SetShaderParameter(ShaderParam.VcOpacity, vc.Opacity);
        sm.SetShaderParameter(ShaderParam.LightingE, m.LightingE);
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
        sm.SetShaderParameter(ShaderParam.AlphaTest, m.AlphaTests);
        sm.SetShaderParameter(ShaderParam.AlphaRef, m.AlphaTestRef);
        SetAlphaClip(sm, m);
        return sm;
    }

    /// <summary>Meshes the client gives the player's skin colours (its skin colour walk only touches this material).</summary>
    public static bool IsSkinMaterial(ModelMesh m) => m.Material == "MS2CharacterSkinMaterial";

    readonly Dictionary<string, Texture2DArray> flipArrays = new();

    /// <summary>A flipbook's frames as one texture array, or null when a frame is missing or the sizes differ.</summary>
    Texture2DArray FlipArray(string[] frames)
    {
        string key = string.Join("|", frames);
        keep?.Use("flip:" + key);
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
        keep?.Use("tex:" + fileName.ToLowerInvariant());
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
            // the process's texture cache (NifCache, the client's CNifLoadingService): a texture a field, an effect or
            // a character view loaded before is not read and decoded again; class 5 (DependTime) as the field's
            // palette textures and the item and effect texture loads take it
            tex = Dds(archive, entry);
        textures[fileName] = tex;
        return tex;
    }

    /// <summary>A texture by its entry in an archive, folder and all (an emotion's texture animation names its frames by
    /// path, CTextureAnimationData::GetBaseTexturePath); null when the archive lacks it.</summary>
    public Texture2D TextureEntry(string archive, string entry)
    {
        if (string.IsNullOrEmpty(entry) || !packs.Exists(archive, entry)) return null;
        keep?.Use("tex:" + entry);
        return Dds(archive, entry);
    }

    Texture2D Dds(string archive, string entry) =>
        NifCache.Texture(packs, (archive + "/" + entry).ToLowerInvariant(), NifCache.CacheClass.DependTime, () =>
        {
            var img = new Image();
            var err = img.LoadDdsFromBuffer(packs.Read(archive, entry));
            if (err == Error.Ok) return ImageTexture.CreateFromImage(img);
            GD.PushWarning($"texture {entry}: {err}");
            return null;
        });

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
