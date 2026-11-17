using Ms2.Formats.Nif;

namespace Ms2.Formats.Model;

public sealed class ModelBone
{
    public string Name;
    public int Parent = -1;               // index into Bones, -1 = child of the scene root
    public Xform Rest;                    // local rest transform (= skin bind pose on shipped humanoids)
    public Xform World;                   // rest transform relative to the scene root
}

public sealed class ModelMesh
{
    public int Block = -1;                // the NiMesh block this came from
    public string Name;
    public float[] Positions;             // xyz per vertex, skin space (scene root) for skinned meshes
    public float[] Normals;               // xyz per vertex, or null
    public float[] Tangents, Binormals;   // xyz per vertex (normal-map tangent frame), or null
    public float[] Uvs;                   // uv per vertex, or null
    public int[] Joints;                  // 4 bone indices per vertex (into ModelData.Bones), skinned only
    public float[] Weights;               // 4 weights per vertex, sum 1, skinned only
    public int[] Indices;                 // triangle list, file winding
    public bool Skinned;
    public int ParentBone = -1;           // rigid meshes: the NiNode they hang from (-1 = scene root)
    public float[] Bound;                 // the NiMesh "Bounding Sphere" (its model bound): centre xyz, radius; null if absent
    public Xform Local = Xform.Identity;  // rigid meshes: transform relative to ParentBone
    public bool AlphaTest;                // has an NiAlphaProperty
    public float[] Ambient, Diffuse, Emissive;   // NiMaterialProperty colours (rgb), or null without a material
    public float[] Specular;              // NiMaterialProperty specular colour (rgb), or null
    public float Glossiness;              // NiMaterialProperty glossiness (the specular power)
    public bool SpecularOn;               // an NiSpecularProperty with its enable flag set
    public float[] Colors;                // rgba 0..1 per vertex, or null
    public float[] Uvs1;                  // second uv set (TEXCOORD1), or null
    public int AlphaFlags = -1;           // NiAlphaProperty flags, -1 without one
    public int AlphaThreshold;            // NiAlphaProperty test threshold (0..255)
    public int VertexColorFlags = -1;     // NiVertexColorProperty flags, -1 without one (inherited from the scene root)
    public bool AlphaBlendOn => AlphaFlags >= 0 && (AlphaFlags & 1) != 0;
    public bool AlphaTestOn => AlphaFlags >= 0 && (AlphaFlags & 0x200) != 0;
    /// <summary>Vertex colours replace the material's ambient and diffuse (NiVertexColorProperty source AMB_DIFF).</summary>
    public bool VertexColorsAmbDiff => Colors != null && VertexColorFlags >= 0 && ((VertexColorFlags >> 4) & 3) == 2;
    public Dictionary<string, string> Textures = new();   // base, normal, gloss, glow, dark, detail, decal0, shaderN
    public string FlipSlot;               // texture slot an NiFlipController cycles (base, dark, ...), or null
    public string[] FlipFrames;           // its source file names, played evenly over FlipPeriod seconds, looping
    public float FlipPeriod;
    public List<TexScroll> Scrolls = new();   // NiTextureTransformController translations
    public string Material;               // active Gamebryo material name (MS2CharacterSkinMaterial, ...), or null
    public Dictionary<string, float> ExtraFloats = new();      // NiFloatExtraData by name (FresnelBoost, ColorBoost, ...)
    public Dictionary<string, float[]> ExtraColors = new();    // NiColorExtraData by name, rgba (OverrideColor0..2, ...)
    public Dictionary<string, int> ExtraInts = new();          // NiIntegerExtraData by name (ColorOverrideMapIndex, ...)
}

/// <summary>A looping texture translation: the offset along U (Axis 0) or V (Axis 1) of a slot's uv, linear between keys.</summary>
public sealed class TexScroll
{
    public string Slot;
    public int Axis;
    public float Start, Stop;             // controller time range; the keys loop over it
    public float[] Times, Values;
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

    public int BoneIndex(string name) => Bones.FindIndex(b => b.Name == name);

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

    static Dictionary<string, string> TexturesOf(NifFile nif, int mi)
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

    /// <summary>NiNode and the node subclasses that only change how children are drawn (sorting, billboarding, switching).</summary>
    static bool IsNode(string type) => type is "NiNode" or "NiSortAdjustNode" or "NiBillboardNode" or "NiSwitchNode" or "NiLODNode";

    static readonly string[] SlotNames = { "base", "dark", "detail", "gloss", "glow", "bump", "normal", "parallax", "decal0" };

    /// <summary>
    /// The flipbook (NiFlipController) and uv translation (NiTextureTransformController, translate U / V) controllers on the
    /// mesh's NiTexturingProperty. Only active, looping controllers; time keys of the flipbook are taken as evenly spaced.
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
                long flags = cb.IntOr("Flags", 0);
                if ((flags & 8) == 0 || ((flags >> 1) & 3) != 0) continue;   // inactive, or not a loop
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
                    long ip = cb.IntOr("Interpolator", -1);
                    if (op is not (0 or 1) || ip < 0 || B[(int)ip].Type != "NiFloatInterpolator") continue;
                    long di = B[(int)ip].IntOr("Data", -1);
                    if (di < 0) continue;
                    var keys = B[(int)di].Struct("Data").Structs("Keys");
                    if (keys.Length == 0) continue;
                    mesh.Scrolls.Add(new TexScroll
                    {
                        Slot = slotName, Axis = (int)op, Start = start, Stop = stop,
                        Times = keys.Select(k => k.Float("Time")).ToArray(),
                        Values = keys.Select(k => k.Float("Value")).ToArray(),
                    });
                }
            }
        }
    }

    /// <summary>The active material name and the float / colour extra data that carry its shader attributes.</summary>
    static void ReadMaterial(NifFile nif, NifStruct b, ModelMesh mesh)
    {
        if (b.TryGetValue("Material Data", out var mdv) && mdv is NifStruct md && md.IntOr("Num Materials", 0) > 0)
        {
            long active = md.IntOr("Active Material", -1);
            var names = md.TryGetValue("Material Name", out var nv) ? nv : null;
            object pick = names switch
            {
                long[] l => l[active >= 0 && active < l.Length ? active : 0],
                object[] o => o[active >= 0 && active < o.Length ? active : 0],
                _ => null,
            };
            mesh.Material = pick switch
            {
                long l => nif.Str(l),
                NifStruct st when st.TryGetValue("Index", out var ix) && ix is long li => nif.Str(li),
                _ => null,
            };
        }
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

    public static ModelData Load(NifFile nif)
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
                var bone = new ModelBone { Name = nif.Name((int)k) ?? $"blk{k}", Parent = parentBone, Rest = local, World = world };
                md.Bones.Add(bone);
                boneOf[(int)k] = md.Bones.Count - 1;
                Add((int)k, md.Bones.Count - 1, world);
            }
        }
        Add(root, -1, Xform.Identity);

        for (int i = 0; i < B.Count; i++)
        {
            var b = B[i];
            if (b.Type != "NiMesh") continue;
            if ((b.IntOr("Flags", 0) & 1) != 0) continue;           // APP_CULLED helper geometry
            if (b.Int("Primitive Type") != 0) throw new NotSupportedException("only triangle lists");
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
            var pos = skinned ? Pick("POSITION_BP", "POSITION") : Pick("POSITION", "POSITION_BP");
            var nrm = skinned ? Pick("NORMAL_BP", "NORMAL") : Pick("NORMAL", "NORMAL_BP");
            int nv = pos.v.Length / 3;
            var mesh = new ModelMesh
            {
                Name = nif.Name(i), Block = i, Skinned = skinned,
                Positions = pos.v.Select(x => (float)x).ToArray(),
                Textures = TexturesOf(nif, i),
                Bound = BoundOf(B[i]),
            };
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
            ReadMaterial(nif, b, mesh);
            ReadTextureControllers(nif, b, mesh);
            mesh.Indices = s["INDEX"].v.Select(x => (int)x).ToArray();
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
                }
                if (prop.Type == "NiSpecularProperty") mesh.SpecularOn = (prop.IntOr("Flags", 0) & 1) != 0;
            }

            if (skinned)
            {
                var sk = B[si];
                if (sk.Int("Skeleton Root") != root) throw new NotSupportedException("skeleton root is not the scene root");
                var skBones = sk.Ints("Bones");
                // Vertices are in the skin's bind space; a bone's world transform times its bone transform maps that space
                // to the scene root. The body's skins use the scene root itself; item skins (clothes) are often offset.
                var bts = sk.Structs("Bone Transforms");
                if (bts.Length > 0 && boneOf.TryGetValue((int)skBones[0], out int b0))
                {
                    var toRoot = md.Bones[b0].World.Compose(XformOf(bts[0]));
                    if (!IsIdentity(toRoot)) ToRootSpace(mesh, toRoot);
                }
                var pal = s.TryGetValue("BONE_PALETTE", out var pp) ? pp.v.Select(x => (int)x).ToArray()
                                                                     : Enumerable.Range(0, skBones.Length).ToArray();
                var bones = pal.Select(p => boneOf[(int)skBones[p]]).ToArray();
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
                        if (ix >= bones.Length) { w[j] = 0; ix = 0; }
                        if (w[j] < 1e-6) w[j] = 0;
                        J[j] = w[j] == 0 ? 0 : bones[ix];
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
            }
            md.Meshes.Add(mesh);
        }
        return md;
    }
}
