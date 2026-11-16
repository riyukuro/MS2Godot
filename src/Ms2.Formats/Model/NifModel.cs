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
    public string Name;
    public float[] Positions;             // xyz per vertex, skin space (scene root) for skinned meshes
    public float[] Normals;               // xyz per vertex, or null
    public float[] Uvs;                   // uv per vertex, or null
    public int[] Joints;                  // 4 bone indices per vertex (into ModelData.Bones), skinned only
    public float[] Weights;               // 4 weights per vertex, sum 1, skinned only
    public int[] Indices;                 // triangle list, file winding
    public bool Skinned;
    public int ParentBone = -1;           // rigid meshes: the NiNode they hang from (-1 = scene root)
    public Xform Local = Xform.Identity;  // rigid meshes: transform relative to ParentBone
    public bool AlphaTest;                // has an NiAlphaProperty
    public Dictionary<string, string> Textures = new();   // base, normal, gloss, glow, dark, detail, shaderN
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
                                                ("glow", "Glow Texture"), ("dark", "Dark Texture"), ("detail", "Detail Texture") })
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
                if (k < 0 || B[(int)k].Type != "NiNode") continue;
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
                Name = nif.Name(i), Skinned = skinned,
                Positions = pos.v.Select(x => (float)x).ToArray(),
                Textures = TexturesOf(nif, i),
            };
            if (nrm.v != null)
            {
                mesh.Normals = new float[nv * 3];
                for (int k = 0; k < nv; k++)
                {
                    double x = nrm.v[k * 3], y = nrm.v[k * 3 + 1], z = nrm.v[k * 3 + 2];
                    double l = Math.Sqrt(x * x + y * y + z * z);
                    if (l == 0) l = 1;
                    mesh.Normals[k * 3] = (float)(x / l); mesh.Normals[k * 3 + 1] = (float)(y / l); mesh.Normals[k * 3 + 2] = (float)(z / l);
                }
            }
            if (s.TryGetValue("TEXCOORD", out var uv)) mesh.Uvs = uv.v.Select(x => (float)x).ToArray();
            mesh.Indices = s["INDEX"].v.Select(x => (int)x).ToArray();
            foreach (var p in b.Ints("Properties"))
                if (p >= 0 && B[(int)p].Type == "NiAlphaProperty") mesh.AlphaTest = true;

            if (skinned)
            {
                var sk = B[si];
                if (sk.Int("Skeleton Root") != root) throw new NotSupportedException("skeleton root is not the scene root");
                var skBones = sk.Ints("Bones");
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
