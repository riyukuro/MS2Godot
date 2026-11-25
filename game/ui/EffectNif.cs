using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;

namespace Ms2Godot.Ui;

/// <summary>An effect xml's nif part as a model (CNifEffect).</summary>
public static class EffectNif
{
    public const string EffectArchive = "Resource/Model/Effect";

    /// <summary>The turn from Gamebryo's Z-up file space to Godot's Y-up that a built model's root carries.</summary>
    public static readonly Transform3D ZUp = new(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero);

    /// <summary>
    /// The effect nif as a model (a skinned mesh driven by the nif's own transform controllers), placed in its parent's
    /// file-space frame by the effect xml's transform (translate, Euler XYZ degrees, scale): the "Effect Root" local
    /// transform (CNifEffect::vfunc_3, nifeffectparam.cpp:705-713).
    /// </summary>
    public static Node3D Build(ModelBuilder builder, PackSet packs, Ms2.Formats.World.EffectPart part)
    {
        // an effect nif's textures ship in the effect archive (as CombatLayer and FieldBuilder search it); appended, so a
        // name already found in an earlier archive resolves as before
        if (!builder.TextureArchives.Contains(EffectArchive)) builder.TextureArchives = builder.TextureArchives.Append(EffectArchive).ToArray();
        string entry = part.FilePath.Replace('\\', '/').ToLowerInvariant();
        var nif = NifFile.Load(builder.Spec, packs.Read(EffectArchive, entry));
        var md = ModelBuilder.Read(nif, EffectArchive + "/" + entry);
        var root = builder.Build(md, System.IO.Path.GetFileNameWithoutExtension(part.FilePath));
        var kf = KfAnimation.FromControllers(nif);
        if (kf != null)
        {
            var player = root.GetNode<AnimationPlayer>("Model/AnimationPlayer");
            var a = ModelBuilder.BuildAnimation(kf, md);
            a.LoopMode = kf.Cycle == 0 ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
            player.GetAnimationLibrary("").AddAnimation("fx", a);
            player.Play("fx");
        }
        // degrees to radians, then FromEulerAnglesXYZ (nifeffectparam.cpp:708): Gamebryo's clockwise rotations
        var r = part.Rotation; var t = part.Translate;
        var xml = ModelBuilder.ToGodot(new Xform(new Vec3(t[0], t[1], t[2]), DummyWorld.EulerXyz(r[0], r[1], r[2]), part.Scale));
        // under a file-space parent (a character's Model node, a bone): the built root turns Z-up to Y-up again, so it
        // carries that turn undone
        root.Transform = xml * ZUp.AffineInverse();
        return root;
    }
}

/// <summary>
/// Gamebryo SeparateNode (guess/separatenode.cpp:56-70): a node whose world takes only the chosen parts of its parent's
/// world (translate, rotate, scale; the rest identity) and ignores its own local transform. CNifEffect::FUN_14154c580
/// (nifeffectparam.cpp:1158-1164) wraps an effect root in one when its update bits are not all set. Here it is a
/// top-level node that copies those parts of its parent's world every frame, in Gamebryo's frame (Godot world = ZUp *
/// Gamebryo world, so a uniform world scale passes through unchanged).
/// </summary>
public partial class SeparateNode3D : Node3D
{
    readonly bool translate, rotate, scale;

    /// <summary>Goes once it holds no child (an effect part's holder ended): the client's SeparateNode is the part's root and
    /// goes with it.</summary>
    public bool FreeWhenEmpty;

    public SeparateNode3D(bool translate, bool rotate, bool scale)
    {
        this.translate = translate; this.rotate = rotate; this.scale = scale;
        Name = "SeparateNode";
        TopLevel = true;
        ProcessPriority = 1000;     // after the animations that move the parent
    }

    public SeparateNode3D() { }

    public override void _Ready() => Follow();

    public override void _Process(double delta)
    {
        if (FreeWhenEmpty && GetChildCount() == 0) { QueueFree(); return; }
        Follow();
    }

    void Follow()
    {
        if (GetParent() is not Node3D parent || !parent.IsInsideTree()) return;
        var gb = EffectNif.ZUp.AffineInverse() * parent.GlobalTransform;     // the parent's Gamebryo world
        var basis = Basis.Identity;
        if (rotate) basis = gb.Basis.Orthonormalized();
        if (scale) basis = basis.Scaled(Vector3.One * gb.Basis.Scale.X);
        var world = new Transform3D(basis, translate ? gb.Origin : Vector3.Zero);
        GlobalTransform = EffectNif.ZUp * world;
    }
}
