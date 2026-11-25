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
///   skeleton and each dummy one <see cref="EffectDummyNode3D"/> under the skeleton, made on first use and shared by the
///   parts on it; a dummy follows its parent only while a part hangs on it. A type 0 part reads its node's frame without
///   making a node (<see cref="FrameOf"/>).
/// - Type 2's name is a worn item's mesh or a rigid item's self node (on its parent, at it), else a node of a worn kfm
///   item, which is a bone of that item's own skeleton (on its parent bone, at it). A rigid item keeps no other node, and
///   no type 2 part of the data names one (EffectAttachSweepTests).
/// - A rigid weapon's Point01 is one node under the weapon's mesh at the node's place, made on first use; a kfm weapon's
///   (an orb) is a bone of its own skeleton.
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
    static readonly StringName PointNodeName = "fx_" + EffectAttachRule.WeaponPointNode;
    static readonly NodePath PointNodePath = "fx_" + EffectAttachRule.WeaponPointNode;

    sealed record Dummies(IReadOnlyList<EffectDummy> Rows, string RootName);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Node3D, Dummies> dummies = new();

    /// <summary>The effect dummies the client attaches to the model under <paramref name="visual"/> at spawn (a PC's by
    /// gender, an npc's row's; PCBase::FUN_141674ef0), whose file root node is named <paramref name="rootName"/>.</summary>
    public static void SetDummies(Node3D visual, IReadOnlyList<EffectDummy> rows, string rootName = "Scene Root")
    {
        if (visual != null && rows != null) dummies.AddOrUpdate(visual, new Dummies(rows, rootName ?? "Scene Root"));
    }

    /// <summary>Where a part hangs: the node its holder goes under and the holder's transform there; <see cref="OwnTransform"/>
    /// false when the client replaces the part's own transform (type 2 without a SeparateNode); <see cref="On"/> and
    /// <see cref="Node"/> say on what.</summary>
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
        Node3D mesh = kind == 2 && pc && sk != null && name.Length > 0 ? ItemMesh(sk, name) : null;
        Skeleton3D itemSk = null;
        int itemBone = kind == 2 && pc && sk != null && name.Length > 0 && mesh == null ? ItemBone(sk, sk, name, out itemSk) : -1;
        MeshInstance3D weapon = null;
        Transform3D point = Transform3D.Identity;
        Skeleton3D kfmWeapon = null;
        int kfmPoint = -1;
        var hand = (Ms2.Net.Packets.EquipSlot)EffectAttachRule.WeaponSlot(kind);
        bool weaponFound = kind is 3 or 4 && pc && model != null
            && (EquipApplier.RigOf(model.GetParentOrNull<Node3D>()) is { } rig && rig.WeaponPoint(hand, out weapon, out point)
                || EquipApplier.KfmWeaponPoint(sk, hand, out kfmWeapon, out kfmPoint));
        var on = EffectAttachRule.Where(kind, model != null, bone >= 0, dummy != null, pc, mesh != null || itemBone >= 0, weaponFound);
        Node3D target;
        var local = Transform3D.Identity;    // the root's local transform on the target in the nif's frame, besides its own
        string node = name;
        switch (on)
        {
            case EffectHang.Bone: target = BoneNode(sk, bone); break;
            case EffectHang.Dummy:
                var dn = DummyNode(sk, model, rows, dummy, 0);
                dn?.Wake();
                target = dn;
                if (target == null) { target = model; on = EffectHang.ModelRoot; }    // a dummy whose parent is missing is not attached
                break;
            case EffectHang.ModelRoot: target = model; node = model.Name; break;
            case EffectHang.ItemMesh when mesh != null:
                target = mesh.GetParentOrNull<Node3D>();
                if (target == null) return false;
                local = mesh.Transform;
                break;
            case EffectHang.ItemMesh:
                // a node of a kfm item's own skeleton: its parent bone (or the skeleton, for a top node), at its local now
                int up = itemSk.GetBoneParent(itemBone);
                target = up >= 0 ? BoneNode(itemSk, up) : itemSk;
                local = itemSk.GetBonePose(itemBone);
                break;
            case EffectHang.WeaponPoint:
                target = weapon != null ? PointNode(weapon, point) : BoneNode(kfmWeapon, kfmPoint);    // a kfm weapon's own bone
                node = EffectAttachRule.WeaponPointNode;
                break;
            default: return false;
        }
        Node3D host = target;
        if (EffectAttachRule.Separate(part))
        {
            var sep = new Ui.SeparateNode3D(part.UpdateTranslate, part.UpdateRotation, part.UpdateScale) { FreeWhenEmpty = true };
            target.AddChild(sep);    // its _Ready takes the target's world now
            host = sep;
        }
        // FUN_14154c580 (:1160-1171) and type 2 (:1135-1136) set the local transform of the effect's root, which is the
        // SeparateNode when there is one, and a SeparateNode's world ignores its local (SeparateNode::UpdateWorldData,
        // separatenode.cpp:56-70); so with one the part keeps its own transform under it, and only without one:
        // - no applyNodeTransform makes the root's local the inverse of the target's world, so it starts at its own
        //   transform in the world (Godot world = ZUp * client world, and ZUp * YUp is the identity);
        // - type 2 then sets the mesh's local transform over that, in place of the part's own.
        var place = YUp;
        bool own = true;
        if (host == target)
        {
            if (on == EffectHang.ItemMesh) { place = local * YUp; own = false; }
            else if (EffectAttachRule.Relative(part)) place = host.GlobalTransform.AffineInverse();
        }
        hung = new Hung(host, place, own, on, node);
        return true;
    }

    /// <summary>
    /// The world transform (Godot's, in the nif's Z-up frame like the bones) of the node an attach type 0 part's parentNode
    /// names on <paramref name="actor"/> (AttachToOwnerNode, nifeffectparam.cpp:1048-1053): a bone, else a dummy
    /// (<see cref="DummyWorld.Find"/> on the bones' and the model root's world now), else the model root; null when the actor
    /// has no model. It makes no node: the part reads the frame once.
    /// </summary>
    internal static Transform3D? FrameOf(Node3D actor, string name)
    {
        var sk = SkeletonOf(actor);
        var model = sk?.GetParentOrNull<Node3D>();
        if (model == null) return null;
        if (string.IsNullOrEmpty(name)) return model.GlobalTransform;
        if (sk.FindBone(name) is int b and >= 0) return BoneWorld(sk, b);
        if (DummiesOf(model) is { } rows && EffectDummyTable.Find(rows.Rows, name) != null
            && DummyWorld.Find(rows.Rows, name, ToClient(model.GlobalTransform), rows.RootName,
                   n => sk.FindBone(n) is int nb and >= 0 ? ToClient(BoneWorld(sk, nb)) : null) is { } w)
            return Ui.EffectNif.ZUp * ModelBuilder.ToGodot(w);
        return model.GlobalTransform;
    }

    static Transform3D BoneWorld(Skeleton3D sk, int bone) => sk.GlobalTransform * sk.GetBoneGlobalPose(bone);

    /// <summary>A Godot world transform as the client's (Z up, uniform scale): <see cref="ModelBuilder.ToGodot(Xform)"/>'s inverse after <see cref="YUp"/>.</summary>
    internal static Xform ToClient(Transform3D godotWorld)
    {
        var t = YUp * godotWorld;
        var b = t.Basis.Orthonormalized();
        return new Xform(new Vec3(t.Origin.X, t.Origin.Y, t.Origin.Z),
            new double[] { b.X.X, b.Y.X, b.Z.X, b.X.Y, b.Y.Y, b.Z.Y, b.X.Z, b.Y.Z, b.Z.Z }, t.Basis.Scale.X);
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
    /// The shared node of an effect dummy (EffectNode::ApplyDummy), an <see cref="EffectDummyNode3D"/> under the skeleton
    /// whose parent is the model root (by the file root's name), another dummy, or a bone. Null when its parent is not found
    /// (the client does not attach it).
    /// </summary>
    static EffectDummyNode3D DummyNode(Skeleton3D sk, Node3D model, Dummies rows, EffectDummy d, int depth)
    {
        if (depth > rows.Rows.Count) return null;
        string nodeName = ("fxd_" + d.Name).ValidateNodeName();
        if (sk.GetNodeOrNull(nodeName) is EffectDummyNode3D made) return made;
        Node3D parent = d.ParentNode == rows.RootName ? model
            : EffectDummyTable.Find(rows.Rows, d.ParentNode) is { } up ? DummyNode(sk, model, rows, up, depth + 1)
            : sk.FindBone(d.ParentNode) is int b and >= 0 ? BoneNode(sk, b) : null;
        if (parent == null) return null;
        var local = ModelBuilder.ToGodot(new Xform(new Vec3(d.Translate.X, d.Translate.Y, d.Translate.Z),
            DummyWorld.EulerXyz(d.Rotation.X, d.Rotation.Y, d.Rotation.Z), d.Scale));
        var n = new EffectDummyNode3D(parent, model, local, d.ApplyWorldTransform, d.ApplyLocalScale) { Name = nodeName };
        sk.AddChild(n);    // its _Ready places it now
        return n;
    }

    /// <summary>The weapon's Point01 node (CEquipItem::FindPoint01Node, equipitem.cpp:604-613): one child of the weapon's
    /// mesh at <paramref name="point"/>, the node's place on it, made on first use.</summary>
    static Node3D PointNode(MeshInstance3D weapon, Transform3D point)
    {
        if (weapon.GetNodeOrNull<Node3D>(PointNodePath) is { } made) return made;
        var n = new Node3D { Name = PointNodeName, Transform = point };
        weapon.AddChild(n);
        return n;
    }

    /// <summary>A bone named <paramref name="name"/> of a worn kfm item's own skeleton under <paramref name="n"/> (EquipApplier.AttachKfm
    /// builds the item's nif with its nodes as bones), by child index; -1 when none. The body's skeleton is not an item's.</summary>
    static int ItemBone(Node n, Skeleton3D body, string name, out Skeleton3D found)
    {
        found = null;
        int count = n.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            var c = n.GetChild(i);
            if (c is Ui.SeparateNode3D or EffectInstance or EffectDummyNode3D) continue;
            if (c is Skeleton3D isk && isk != body && isk.FindBone(name) is int b and >= 0) { found = isk; return b; }
            if (ItemBone(c, body, name, out found) is int fb and >= 0) return fb;
        }
        return -1;
    }

    /// <summary>A worn item's rigid mesh named <paramref name="name"/> under the skeleton (EquipApplier names each by its
    /// file name in the "mesh" meta), or a rigid item's self node of that name (<see cref="EquipApplier.ItemNodeMeta"/>),
    /// by child index.</summary>
    static Node3D ItemMesh(Node n, string name)
    {
        int count = n.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            var c = n.GetChild(i);
            if (c is MeshInstance3D mi && mi.HasMeta(MeshMeta) && (string)mi.GetMeta(MeshMeta) == name) return mi;
            if (c is Node3D itemNode && itemNode.HasMeta(EquipApplier.ItemNodeMeta) && (string)itemNode.GetMeta(EquipApplier.ItemNodeMeta) == name) return itemNode;
            if (c is Ui.SeparateNode3D or EffectInstance) continue;
            if (ItemMesh(c, name) is { } found) return found;
        }
        return null;
    }
}

/// <summary>
/// An effect dummy (EffectNode::UpdateWorldData; <see cref="DummyWorld.Resolve"/> has the rule), in the nif's Z-up frame
/// like the bones, top level under the skeleton. With P its parent's client world, whose scale counts as 1 unless the row
/// applies the local scale:
/// - applyWorldTransform: at P's translate plus the row's z straight up the world (times P's scale), turned by the model
///   root's rotation and the row's own, the root's rotation turning the row's x and y;
/// - otherwise: P times the row's local transform (so an npc's scale, on its visual, does not scale the row's offset or
///   the parts on it unless the row applies the local scale).
/// A parent dummy's world is worked out here, not read from its node, so a dummy needs no other to be processed. It
/// follows each frame after the animations that move the parent, and stops while no part hangs on it (<see cref="Wake"/>).
/// </summary>
public partial class EffectDummyNode3D : Node3D
{
    readonly Node3D parent, model;
    readonly Transform3D local;
    readonly bool world, localScale;
    int idle;

    public EffectDummyNode3D(Node3D parent, Node3D model, Transform3D local, bool world, bool localScale)
    {
        this.parent = parent; this.model = model; this.local = local; this.world = world; this.localScale = localScale;
        TopLevel = true;
        ProcessPriority = 1000;
    }

    public EffectDummyNode3D() { }

    public override void _Ready() => Follow();

    public override void _Process(double delta)
    {
        // the caller adds the part right after Hang, so an empty frame or two is a dummy whose parts have gone
        if (GetChildCount() == 0 && ++idle >= 2) { SetProcess(false); return; }
        if (GetChildCount() > 0) idle = 0;
        Follow();
    }

    /// <summary>Places the dummy now and follows each frame again (a part is about to hang on it).</summary>
    public void Wake()
    {
        idle = 0;
        SetProcess(true);
        Follow();
    }

    void Follow()
    {
        if (ClientWorld() is { } w) GlobalTransform = Ui.EffectNif.ZUp * w;
    }

    /// <summary>The dummy's world in the client's frame now (Godot world = ZUp times it), null when a node is gone.</summary>
    internal Transform3D? ClientWorld()
    {
        if (!IsInstanceValid(parent) || !IsInstanceValid(model) || !parent.IsInsideTree()) return null;
        Transform3D p;
        if (parent is EffectDummyNode3D up) { if (up.ClientWorld() is { } pw) p = pw; else return null; }
        else p = EffectAttach.YUp * parent.GlobalTransform;
        float ps = localScale ? p.Basis.Scale.X : 1f;
        var pr = p.Basis.Orthonormalized();
        if (!world) return new Transform3D(pr.Scaled(new Vector3(ps, ps, ps)), p.Origin) * local;
        var t = local.Origin;
        var own = local.Basis.Orthonormalized();
        float s = local.Basis.Scale.X * ps;
        var target = (EffectAttach.YUp * model.GlobalTransform).Basis.Orthonormalized();
        var origin = p.Origin + new Vector3(0, 0, t.Z * ps) + target * new Vector3(t.X, t.Y, 0);
        return new Transform3D((target * own).Scaled(new Vector3(s, s, s)), origin);
    }
}
