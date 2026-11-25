using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/kf_nan.tscn) of the kf animations whose keys are NaN for the whole clip, every one in the data
/// (Ms2.Formats.Tests MagicClawEffectTests.EveryNonFiniteAnimationKey, the same seven in the KMS and the GMS2 data): the scale of
/// npc 02010040's Bottle_Point01 in its talk and chat up (317 skinned vertices), of 21803600's Point04 in its idles (17 rigid
/// meshes), and the scale, translation and rotation of the seesaw's Apple_Point01 (1 rigid mesh). Each is built on its model
/// (ModelBuilder.BuildAnimation) and played; then every key of the animation, every bone's global pose and every mesh's global
/// transform are finite (no NaN reaches the renderer as a skeleton, attachment or sort box), and the NaN node is collapsed to a
/// point (zero scale), drawing nothing as the client draws its NaN vertices. Quits with 0 when all hold, else 1.
/// </summary>
public partial class KfNanCheck : Node3D
{
    static readonly (string Archive, string Kf, string Nif, string Bone)[] Cases =
    {
        ("Resource/Model/Npc", "02/01/02010040/talk_a.kf", "02/01/02010040/02010040_rari.nif", "Bottle_Point01"),
        ("Resource/Model/Npc", "02/01/02010040/chatup_a.kf", "02/01/02010040/02010040_rari.nif", "Bottle_Point01"),
        ("Resource/Model/Npc", "21/21803600/idle_a.kf", "21/21803600/21803600_m_marbleturkasupport.nif", "Point04"),
        ("Resource/Model/Npc", "21/21803600/idle_b.kf", "21/21803600/21803600_m_marbleturkasupport.nif", "Point04"),
        ("Resource/Model/Npc", "21/21803600/attack_idle_a.kf", "21/21803600/21803600_m_marbleturkasupport.nif", "Point04"),
        ("Resource/Model/Map", "common/functobj/co_functobj_seesaw_a01/idle_a.kf", "common/functobj/co_functobj_seesaw_a01/co_functobj_seesaw_a01.nif", "Apple_Point01"),
        ("Resource/Model/Map", "common/functobj/co_functobj_seesaw_a01/co_functobj_seesaw_a01_off.kf", "common/functobj/co_functobj_seesaw_a01/co_functobj_seesaw_a01.nif", "Apple_Point01"),
    };
    readonly List<string> fails = new();
    readonly List<(string Name, Node3D Root, Skeleton3D Skel, int Bone)> built = new();
    int frames;

    static bool Finite(Vector3 v) => float.IsFinite(v.X + v.Y + v.Z);
    static bool Finite(Transform3D t) => Finite(t.Basis.X) && Finite(t.Basis.Y) && Finite(t.Basis.Z) && Finite(t.Origin);

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        foreach (var c in Cases)
        {
            var md = builder.LoadModel(c.Archive, c.Nif);
            var kf = KfAnimation.Load(NifFile.Load(ModelBuilder.SharedSpec(Settings.NifXml), packs.Read(c.Archive, c.Kf)));
            var anim = ModelBuilder.BuildAnimation(kf, md);
            int keys = 0;
            for (int tr = 0; tr < anim.GetTrackCount(); tr++)
                for (int k = 0; k < anim.TrackGetKeyCount(tr); k++, keys++)
                {
                    var v = anim.TrackGetKeyValue(tr, k);
                    bool ok = v.VariantType == Variant.Type.Quaternion ? float.IsFinite(v.AsQuaternion().X + v.AsQuaternion().Y + v.AsQuaternion().Z + v.AsQuaternion().W)
                            : Finite(v.AsVector3());
                    if (!ok) { fails.Add($"{c.Kf}: track {anim.TrackGetPath(tr)} key {k} is {v}"); break; }
                }
            var root = builder.Build(md, c.Kf.Replace('/', '_'));
            AddChild(root);
            var player = root.GetNode<AnimationPlayer>("Model/AnimationPlayer");
            player.GetAnimationLibrary("").AddAnimation("clip", anim);
            player.Play("clip");
            var skel = root.GetNode<Skeleton3D>("Model/" + ModelBuilder.SkeletonName);
            built.Add((c.Kf, root, skel, skel.FindBone(c.Bone)));
            GD.Print($"kf nan: {c.Kf}: {anim.GetTrackCount()} tracks, {keys} keys");
        }
    }

    public override void _Process(double delta)
    {
        if (++frames < 10) return;
        foreach (var (name, root, skel, bone) in built)
        {
            if (bone < 0) { fails.Add($"{name}: the NaN bone is not on the skeleton"); continue; }
            for (int b = 0; b < skel.GetBoneCount(); b++)
                if (!Finite(skel.GetBoneGlobalPose(b))) { fails.Add($"{name}: bone {skel.GetBoneName(b)} global pose {skel.GetBoneGlobalPose(b)}"); break; }
            if (skel.GetBonePoseScale(bone) != Vector3.Zero) fails.Add($"{name}: {skel.GetBoneName(bone)} scale {skel.GetBonePoseScale(bone)}, not collapsed");
            int meshes = 0;
            foreach (var n in root.FindChildren("*", nameof(MeshInstance3D), true, false))
            {
                meshes++;
                var mi = (MeshInstance3D)n;
                if (!Finite(mi.GlobalTransform)) { fails.Add($"{name}: mesh {mi.Name} global transform {mi.GlobalTransform}"); break; }
            }
            GD.Print($"kf nan: {name}: {skel.GetBoneCount()} bones, {meshes} meshes checked");
        }
        foreach (var f in fails) GD.PrintErr("FAIL " + f);
        GD.Print(fails.Count == 0 ? "kf nan: PASS" : $"kf nan: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
