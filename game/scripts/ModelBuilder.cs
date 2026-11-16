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
    readonly PackSet packs;
    readonly Dictionary<string, Texture2D> textures = new(StringComparer.OrdinalIgnoreCase);

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
        }

        var player = new AnimationPlayer { Name = "AnimationPlayer" };
        model.AddChild(player);
        player.RootNode = "..";
        player.AddAnimationLibrary("", new AnimationLibrary());
        return root;
    }

    ArrayMesh BuildMesh(ModelMesh m)
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
        if (m.Uvs != null)
        {
            var uv = new Vector2[nv];
            for (int i = 0; i < nv; i++) uv[i] = new Vector2(m.Uvs[i * 2], m.Uvs[i * 2 + 1]);
            arrays[(int)Mesh.ArrayType.TexUV] = uv;
        }
        if (m.Skinned)
        {
            arrays[(int)Mesh.ArrayType.Bones] = m.Joints;
            arrays[(int)Mesh.ArrayType.Weights] = m.Weights;
        }
        // file triangles are counter-clockwise (as in the glTF export); Godot's front faces are clockwise
        var idx = (int[])m.Indices.Clone();
        for (int i = 0; i + 2 < idx.Length; i += 3) (idx[i + 1], idx[i + 2]) = (idx[i + 2], idx[i + 1]);
        arrays[(int)Mesh.ArrayType.Index] = idx;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, BuildMaterial(m));
        return mesh;
    }

    Material BuildMaterial(ModelMesh m)
    {
        var mat = new StandardMaterial3D { Roughness = 0.8f, Metallic = 0f };
        if (m.Textures.TryGetValue("base", out var baseName))
        {
            var tex = Texture(baseName);
            if (tex != null) mat.AlbedoTexture = tex;
        }
        if (m.AlphaTest)
        {
            mat.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
            mat.AlphaScissorThreshold = 0.5f;
        }
        return mat;
    }

    Texture2D Texture(string fileName)
    {
        if (textures.TryGetValue(fileName, out var t)) return t;
        Texture2D tex = null;
        var entry = packs.FindByFileName(TextureArchive, fileName);
        if (entry == null) GD.PushWarning($"texture {fileName} not found in {TextureArchive}");
        else
        {
            var img = new Image();
            var err = img.LoadDdsFromBuffer(packs.Read(TextureArchive, entry));
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
