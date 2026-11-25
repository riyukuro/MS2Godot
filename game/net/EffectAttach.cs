using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot.Net;

/// <summary>
/// Where a nif part of an effect on an actor hangs (<see cref="EffectAttachRule"/>: CNifEffect::vfunc_3's attach switch,
/// nifeffectparam.cpp:717, then FUN_14154c580 :1158). The caller builds the part's holder (a Y-up node holding the part's
/// own translate, rotation and scale) and adds it under <see cref="Hung.Host"/> at <see cref="Hung.Place"/>.
/// - The bones, the model node, the dummies and a SeparateNode are in the nif's own Z-up frame (ModelBuilder keeps the file's
///   axes under the model node's turn), so the holder under them takes the inverse of that turn (<see cref="YUp"/>), as
///   EffectNif's root does.
/// - A named node is a bone of the model's skeleton, else one of the actor's effect dummies (<see cref="SetDummies"/>), else
///   the model root (GetObjectByName on m_hNode, whose NiNodes are those). Each bone gets one BoneAttachment3D under the
///   skeleton and each dummy one node (under its parent; with applyWorldTransform under the skeleton), made on first use and
///   shared by the parts on it.
/// - Partial update bits put a <see cref="Ui.SeparateNode3D"/> under the target, which goes when its last part goes.
/// </summary>
public static class EffectAttach
{
    /// <summary>The turn a Y-up holder takes under a node in the nif's Z-up frame (the inverse of <see cref="Ui.EffectNif.ZUp"/>).</summary>
    public static readonly Transform3D YUp = Ui.EffectNif.ZUp.AffineInverse();

    /// <summary>Set on a PC's root (the own player, another PC's body): the client's DynamicCast&lt;PC&gt; of the effect's
    /// owner, which types 2-4 need.</summary>
    public static readonly StringName PcMeta = "ms2_pc";

    static readonly StringName MeshMeta = "mesh";

    sealed record Dummies(IReadOnlyList<EffectDummy> Rows, string RootName);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Node3D, Dummies> dummies = new();

    /// <summary>The effect dummies the client attaches to the model under <paramref name="visual"/> at spawn (a PC's by
    /// gender, an npc's row's; PCBase::FUN_141674ef0), whose file root node is named <paramref name="rootName"/>.</summary>
    public static void SetDummies(Node3D visual, IReadOnlyList<EffectDummy> rows, string rootName = "Scene Root")
    {
        if (visual != null && rows != null) dummies.AddOrUpdate(visual, new Dummies(rows, rootName ?? "Scene Root"));
    }

    /// <summary>Where a part hangs: the node its holder goes under and the holder's transform there; <see cref="OwnTransform"/>
    /// false when the client replaces the part's own transform (type 2); <see cref="On"/> and <see cref="Node"/> say on what.</summary>
    public readonly record struct Hung(Node3D Host, Transform3D Place, bool OwnTransform, EffectHang On, string Node);

    /// <summary>Hangs an attach type 1-4 part on <paramref name="actor"/> (in the tree); false when the client makes no part.
    /// A SeparateNode it needs is made here, under the target.</summary>
    public static bool Hang(EffectPart part, Node3D actor, out Hung hung)
    {
        hung = default;
        if (actor == null || !GodotObject.IsInstanceValid(actor) || !actor.IsInsideTree()) return false;
        var sk = SkeletonOf(actor);
        var model = sk?.GetParentOrNull<Node3D>();
        string name = part.ParentNode ?? "";
        int kind = part.AttachKind;
        int bone = kind == 1 && sk != null && name.Length > 0 ? sk.FindBone(name) : -1;
        var rows = kind == 1 && bone < 0 && model != null && name.Length > 0 ? DummiesOf(model) : null;
        var dummy = rows != null ? EffectDummyTable.Find(rows.Rows, name) : null;
        bool pc = kind >= 2 && IsPc(actor);
        MeshInstance3D mesh = kind == 2 && pc && sk != null && name.Length > 0 ? ItemMesh(sk, name) : null;
        MeshInstance3D weapon = null;
        Transform3D point = Transform3D.Identity;
        bool weaponFound = kind is 3 or 4 && pc && model != null
            && EquipApplier.RigOf(model.GetParentOrNull<Node3D>()) is { } rig
            && rig.WeaponPoint((Ms2.Net.Packets.EquipSlot)EffectAttachRule.WeaponSlot(kind), out weapon, out point);
        var on = EffectAttachRule.Where(kind, model != null, bone >= 0, dummy != null, pc, mesh != null, weaponFound);
        Node3D target;
        var local = Transform3D.Identity;    // the root's local transform on the target in the nif's frame, besides its own
        string node = name;
        switch (on)
        {
            case EffectHang.Bone: target = BoneNode(sk, bone); break;
            case EffectHang.Dummy:
                target = DummyNode(sk, model, rows, dummy, 0);
                if (target == null) { target = model; on = EffectHang.ModelRoot; }    // a dummy whose parent is missing is not attached
                break;
            case EffectHang.ModelRoot: target = model; node = model.Name; break;
            case EffectHang.ItemMesh:
                target = mesh.GetParentOrNull<Node3D>();
                if (target == null) return false;
                local = mesh.Transform;
                break;
            case EffectHang.WeaponPoint: target = weapon; local = point; node = EffectAttachRule.WeaponPointNode; break;
            default: return false;
        }
        Node3D host = target;
        if (EffectAttachRule.Separate(part))
        {
            var sep = new Ui.SeparateNode3D(part.UpdateTranslate, part.UpdateRotation, part.UpdateScale) { FreeWhenEmpty = true };
            target.AddChild(sep);    // its _Ready takes the target's world now
            host = sep;
        }
        var place = local * YUp;
        // without applyNodeTransform the root starts at its own transform in the world (Godot world = ZUp * client world)
        if (EffectAttachRule.Relative(part)) place = host.GlobalTransform.AffineInverse() * Ui.EffectNif.ZUp * place;
        hung = new Hung(host, place, on != EffectHang.ItemMesh, on, node);
        return true;
    }

    /// <summary>
    /// The node (in the nif's Z-up frame) an attach type 0 part's parentNode names on <paramref name="actor"/>
    /// (AttachToOwnerNode, nifeffectparam.cpp:1048-1053): a bone, else a dummy, else the model root; null when the actor has
    /// no model.
    /// </summary>
    internal static Node3D NodeOf(Node3D actor, string name)
    {
        var sk = SkeletonOf(actor);
        var model = sk?.GetParentOrNull<Node3D>();
        if (model == null) return null;
        if (string.IsNullOrEmpty(name)) return model;
        if (sk.FindBone(name) is int b and >= 0) return BoneNode(sk, b);
        if (DummiesOf(model) is { } rows && EffectDummyTable.Find(rows.Rows, name) is { } d && DummyNode(sk, model, rows, d, 0) is { } n) return n;
        return model;
    }

    /// <summary>The effect's owner is a PC (<see cref="PcMeta"/> on the actor or the root it is the visual of).</summary>
    public static bool IsPc(Node3D actor) =>
        actor.HasMeta(PcMeta) || actor.GetParentOrNull<Node3D>() is { } p && p.HasMeta(PcMeta);

    static Dummies DummiesOf(Node3D model) =>
        model.GetParentOrNull<Node3D>() is { } visual && dummies.TryGetValue(visual, out var d) ? d : null;

    /// <summary>The actor's skeleton (the first one under it, by child index): the model node is its parent.</summary>
    internal static Skeleton3D SkeletonOf(Node n)
    {
        if (n is Skeleton3D sk) return sk;
        int count = n.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            var c = n.GetChild(i);
            if (c is not Ui.SeparateNode3D and not EffectInstance && SkeletonOf(c) is { } s) return s;
        }
        return null;
    }

    /// <summary>The shared attachment of a bone (one per bone, named after it), placed at the bone at once.</summary>
    static BoneAttachment3D BoneNode(Skeleton3D sk, int bone)
    {
        string boneName = sk.GetBoneName(bone);
        string nodeName = ("fx_" + boneName).ValidateNodeName();
        if (sk.GetNodeOrNull(nodeName) is BoneAttachment3D a) return a;
        a = new BoneAttachment3D { Name = nodeName, BoneName = boneName };
        sk.AddChild(a);
        a.GlobalTransform = sk.GlobalTransform * sk.GetBoneGlobalPose(bone);   // the skeleton's next update sets the same
        return a;
    }

    /// <summary>
    /// The shared node of an effect dummy (EffectNode::ApplyDummy): under its parent (the model root by the file root's name,
    /// another dummy, or a bone) at the row's transform, or, with applyWorldTransform, an <see cref="EffectDummyNode3D"/>.
    /// Null when its parent is not found (the client does not attach it).
    /// </summary>
    static Node3D DummyNode(Skeleton3D sk, Node3D model, Dummies rows, EffectDummy d, int depth)
    {
        if (depth > rows.Rows.Count) return null;
        Node3D parent = d.ParentNode == rows.RootName ? model
            : EffectDummyTable.Find(rows.Rows, d.ParentNode) is { } up ? DummyNode(sk, model, rows, up, depth + 1)
            : sk.FindBone(d.ParentNode) is int b and >= 0 ? BoneNode(sk, b) : null;
        if (parent == null) return null;
        string nodeName = ("fxd_" + d.Name).ValidateNodeName();
        // a world dummy follows from the skeleton; an ordinary one is a child of its parent (DummyWorld.Resolve)
        var home = d.ApplyWorldTransform ? sk : parent;
        if (home.GetNodeOrNull(nodeName) is Node3D made) return made;
        var local = ModelBuilder.ToGodot(new Xform(new Vec3(d.Translate.X, d.Translate.Y, d.Translate.Z),
            DummyWorld.EulerXyz(d.Rotation.X, d.Rotation.Y, d.Rotation.Z), d.Scale));
        Node3D n = d.ApplyWorldTransform
            ? new EffectDummyNode3D(parent, model, local, d.ApplyLocalScale) { Name = nodeName }
            : new Node3D { Name = nodeName, Transform = local };
        home.AddChild(n);    // a world dummy's _Ready places it now
        return n;
    }

    /// <summary>A worn item's rigid mesh named <paramref name="name"/> under the skeleton (EquipApplier names each by its
    /// file name in the "mesh" meta), by child index.</summary>
    static MeshInstance3D ItemMesh(Node n, string name)
    {
        int count = n.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            var c = n.GetChild(i);
            if (c is MeshInstance3D mi && mi.HasMeta(MeshMeta) && (string)mi.GetMeta(MeshMeta) == name) return mi;
            if (c is Ui.SeparateNode3D or EffectInstance) continue;
            if (ItemMesh(c, name) is { } found) return found;
        }
        return null;
    }
}

/// <summary>
/// An effect dummy with applyWorldTransform (EffectNode::UpdateWorldData; <see cref="DummyWorld.Resolve"/> has the rule), in
/// the nif's Z-up frame like the bones: each frame at its parent's translate plus the row's z straight up the world (times the
/// parent's scale when the row applies the local scale), turned by the model root's rotation and the row's own, the root's
/// rotation turning the row's x and y. Top level, after the animations that move the parent.
/// </summary>
public partial class EffectDummyNode3D : Node3D
{
    readonly Node3D parent, model;
    readonly Vector3 translate;
    readonly Basis own;
    readonly float scale;
    readonly bool localScale;

    public EffectDummyNode3D(Node3D parent, Node3D model, Transform3D local, bool localScale)
    {
        this.parent = parent; this.model = model; this.localScale = localScale;
        translate = local.Origin; scale = local.Basis.Scale.X; own = local.Basis.Orthonormalized();
        TopLevel = true;
        ProcessPriority = 1000;
    }

    public EffectDummyNode3D() { }

    public override void _Ready() => Follow();

    public override void _Process(double delta) => Follow();

    void Follow()
    {
        if (!IsInstanceValid(parent) || !IsInstanceValid(model) || !parent.IsInsideTree()) return;
        var p = EffectAttach.YUp * parent.GlobalTransform;                       // the parent's client world
        float ps = localScale ? p.Basis.Scale.X : 1f;
        var target = (EffectAttach.YUp * model.GlobalTransform).Basis.Orthonormalized();
        var origin = p.Origin + new Vector3(0, 0, translate.Z * ps) + target * new Vector3(translate.X, translate.Y, 0);
        GlobalTransform = Ui.EffectNif.ZUp * new Transform3D((target * own).Scaled(Vector3.One * (ps * scale)), origin);
    }
}
