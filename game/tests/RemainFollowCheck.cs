using System.Collections.Generic;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Replay;
using Ms2.Net.Packets;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/remain_follow.tscn): a damage hit's effectRemain on its target (CombatLayer.PlayEffectOn,
/// CEffect SetActor), placed by each nif part's attach type (CNifEffect::vfunc_3). An effect whose nif parts are all
/// attached to a node (type 1+) is played on an npc root (NpcLayer.RootOf) and on a second npc. The check asserts:
/// - its nodes are under the target's root (on the node each part's attach type names), and they move when the root moves
///   (read a frame later: a world dummy such as Eff_Body_World follows in its own process, after this node's);
/// - FieldRemoveNpc frees them with the root, and the live fade list then ticks without them;
/// - the effect on the second npc goes when the field node goes;
/// - an actor that is gone (null) plays nothing.
/// A type 0 effect (the staff attack remain) on the first npc goes under the field at the npc's place, stays there when
/// the npc moves and outlives the npc's removal (<see cref="Places"/> has the other type 0 cases and <see cref="Hangs"/> the
/// type 1-4 ones). The node effect is the first (in name order) whose nif parts are all
/// type 1+ with a first part lasting at least 2 s, so a part does not end on its own during the check.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class RemainFollowCheck : Node3D
{
    int frame;
    bool done;
    readonly List<string> fails = new();
    Node3D field, fx, fieldFx, worldFx, emptySeparate, movedRoot;
    EffectDummyNode3D idleDummy;
    Vector3 fxOnRoot;
    static readonly NodePath EffectRoot = "Effect";
    const string WorldXml = "ch/weapon/staff/eff_staff_attack01_remain_01.xml";
    const string OriginXml = "ch/skill/knight/eff_knight_holystrike_remain_01.xml";
    EffectTable effects;
    NpcLayer npcs;
    CombatLayer combat;
    string xml;

    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    /// <summary>Equal to within float error of the inverse and products taken on the way (a scaled, turned bone).</summary>
    static bool Near(Transform3D a, Transform3D b) =>
        a.Origin.DistanceTo(b.Origin) < 1e-3f && a.Basis.X.DistanceTo(b.Basis.X) < 1e-4f && a.Basis.Y.DistanceTo(b.Basis.Y) < 1e-4f && a.Basis.Z.DistanceTo(b.Basis.Z) < 1e-4f;

    public override void _Process(double delta)
    {
        if (done) return;
        frame++;
        if (frame == 2)
        {
            try { Start(); }
            catch (System.Exception ex) { Check(false, "setup threw " + ex.Message); Finish(); }
        }
        else if (frame == 4) AfterMove();    // frame 3 ran the new dummies' first process after this node's
        else if (frame == 6) AfterRemove();
        else if (frame == 8) AfterField();
    }

    void Start()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var table = new NpcTable(packs);
        effects = new EffectTable(packs);
        field = new Node3D();
        AddChild(field);
        npcs = new NpcLayer(packs, builder, table, field);
        combat = new CombatLayer(packs, builder, effects, field);
        int npcId = 0;
        foreach (int id in new[] { 21000001, 21000002, 21000003, 11000001 })
            if (table.Find(id) != null) { npcId = id; break; }
        var entries = new List<string>(packs.List("Xml"));
        entries.Sort(System.StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!entry.StartsWith("effect/", System.StringComparison.OrdinalIgnoreCase)
                || !entry.EndsWith(".xml", System.StringComparison.OrdinalIgnoreCase)) continue;
            string name = entry.Substring("effect/".Length).ToLowerInvariant();
            var parts = effects.Get(name);
            if (parts == null || !parts.Exists(p => p.Type == "nif") || parts.Exists(p => p.Type == "nif" && p.AttachKind == 0)) continue;
            foreach (var p in parts)
                if (p.Type == "nif" && !string.IsNullOrEmpty(p.FilePath) && p.StartDelay <= 0 && (p.Loop || p.Duration >= 2f)
                    && packs.Exists(CombatLayer.EffectArchive, p.FilePath.Replace('\\', '/').ToLowerInvariant()))
                { xml = name; break; }
            if (xml != null) break;
        }
        Check(npcId != 0, "no npc row");
        Check(xml != null, "no node-attached effect with a lasting nif part");
        var worldParts = effects.Get(WorldXml);
        Check(worldParts != null && worldParts.Exists(p => p.Type == "nif") && worldParts.TrueForAll(p => p.Type != "nif" || p.AttachKind == 0),
            "the staff attack remain is not a type 0 effect");
        if (npcId == 0 || xml == null) { Finish(); return; }
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 1, NpcId = npcId }));
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 2, NpcId = npcId }));
        var root = npcs.RootOf(1);
        var root2 = npcs.RootOf(2);
        Check(root != null && root2 != null, "no npc roots");
        if (root == null || root2 == null) { Finish(); return; }

        combat.PlayEffectOn(xml, null, 5);
        Check(combat.EffectsOnActors == 0, "an absent actor played an effect");

        var seen = new HashSet<Node>();
        Walk(root, seen);
        combat.PlayEffectOn(xml, root, 5);
        Check(combat.EffectsOnActors == 1, $"effects on actors {combat.EffectsOnActors}");
        fx = NewEffect(root, seen);
        Check(fx != null && root.IsAncestorOf(fx), "the effect node is not under the target's root");
        if (fx != null)
        {
            // a part on a world dummy (Eff_Body_World) follows in the dummy's own process, so the offset is read next frame
            fxOnRoot = root.GlobalTransform.AffineInverse() * fx.GlobalPosition;
            movedRoot = root;
            root.GlobalPosition += new Vector3(500, 0, 0);
        }

        seen.Clear();
        Walk(root2, seen);
        combat.PlayEffectOn(xml, root2, 5);
        fieldFx = NewEffect(root2, seen);
        Check(fieldFx != null, "the second effect added no node");

        root.GlobalPosition = new Vector3(40, 0, 30);
        int w0 = field.GetChildCount(), r0 = root.GetChildCount();
        combat.PlayEffectOn(WorldXml, root, 5);
        worldFx = field.GetChildCount() > w0 ? field.GetChild(field.GetChildCount() - 1) as Node3D : null;
        Check(worldFx != null, "the type 0 effect added no child to the field");
        Check(root.GetChildCount() == r0, "the type 0 effect went under the target");
        if (worldFx != null)
        {
            var at = worldFx.GlobalPosition;
            Check(at.DistanceTo(new Vector3(40, 0, 30)) < 0.01f, $"the type 0 effect starts at {at}, not at its target");
            root.GlobalPosition += new Vector3(0, 0, 200);
            Check(worldFx.GlobalPosition.DistanceTo(at) < 0.01f, "the type 0 effect followed its target");
        }

        Places();
        Weapons(packs, builder);
        WornItems(packs, builder);
        ScaledNpc(table);
    }

    /// <summary>The starting weapons of the eleven jobs (KMS2 table/job.xml startItems), worn in the right hand here; the
    /// orb (15600514) is a kfm model, the others rigid meshes.</summary>
    static readonly int[] StartWeapons = { 13100225, 13200220, 13300219, 13400218, 15000224, 15100216, 15200223, 15300199, 15400274, 15500514, 15600514 };

    /// <summary>
    /// Types 3 and 4 (AttachToEquipSlot, nifeffectparam.cpp:1142-1151) on a PC wearing each job's starting weapon in the
    /// right hand (EquipApplier.Apply): every one has a Point01 node (FindPoint01Node, equipitem.cpp:604-613); a type 4
    /// part hangs on it (one shared node under a rigid weapon's mesh, the bone of a kfm weapon's own skeleton), within the
    /// bounds of that weapon's meshes; with update
    /// bits not all set its SeparateNode is under that node, at it; without applyNodeTransform it starts at the world
    /// origin (FUN_14154c580 :1166-1171); a type 3 part with no left hand weapon makes none.
    /// </summary>
    void Weapons(PackSet packs, ModelBuilder builder)
    {
        var md = builder.LoadModel("male/m_body.nif");
        var palettes = new Look.ColorPalettes(packs);
        int withPoint = 0, worn = 0;
        foreach (int id in StartWeapons)
        {
            var actor = new Node3D { Position = new Vector3(id % 1000, 0, -(id % 1000)) };
            field.AddChild(actor);
            var visual = builder.Build(md, "Male");
            actor.AddChild(visual);
            actor.SetMeta(EffectAttach.PcMeta, true);
            new EquipApplier(packs, builder) { Palettes = palettes }.Apply(visual, md, 0, new[] { new EquipItem { Id = id, Slot = EquipSlot.RH } });
            var rig = EquipApplier.RigOf(visual);
            var body = EffectAttach.SkeletonOf(visual);
            MeshInstance3D mesh = null;
            bool rigid = rig != null && rig.WeaponPoint(EquipSlot.RH, out mesh, out _);
            Skeleton3D kfmSk = null;
            bool kfm = !rigid && EquipApplier.KfmWeaponPoint(body, EquipSlot.RH, out kfmSk, out _);
            if (rig != null || kfm) worn++; else GD.Print($"remain follow: starting weapon {id} is not worn");
            if (rigid || kfm)
            {
                withPoint++;
                bool hung = EffectAttach.Hang(new EffectPart { AttachType = "WeaponR", ApplyNodeTransform = true }, actor, out var h);
                Check(hung && h.On == EffectHang.WeaponPoint && (rigid ? h.Host.GetParent() == mesh : h.Host is BoneAttachment3D { BoneName: "Point01" } && h.Host.GetParent() == kfmSk),
                    $"item {id}: a type 4 part hung on {h.On} {h.Host?.Name}, not on its weapon's Point01");
                if (hung)
                {
                    var at = (h.Host.GlobalTransform * h.Place).Origin;
                    var box = Bounds(visual, $"eq{id}_", null);
                    Check(box is { } b && b.Grow(10).HasPoint(at), $"item {id}: the type 4 part starts at {at}, outside its weapon {box}");
                    Check(EffectAttach.Hang(new EffectPart { AttachType = "WeaponR", ApplyNodeTransform = true, UpdateRotation = false }, actor, out var hs)
                        && hs.Host is Ui.SeparateNode3D && hs.Host.GetParent() == h.Host && (hs.Host.GlobalTransform * hs.Place).Origin.DistanceTo(at) < 1e-3f,
                        $"item {id}: a type 4 part with rotation off did not hang on a SeparateNode at the Point01 node");
                    Check(EffectAttach.Hang(new EffectPart { AttachType = "WeaponR" }, actor, out var hr) && Near(hr.Host.GlobalTransform * hr.Place, Transform3D.Identity),
                        $"item {id}: a type 4 part without applyNodeTransform starts at {(hr.Host?.GlobalTransform ?? default) * hr.Place}, not the world origin");
                }
            }
            Check(!EffectAttach.Hang(new EffectPart { AttachType = "WeaponL", ApplyNodeTransform = true }, actor, out _), $"item {id}: a type 3 part hung with no left hand weapon");
            actor.QueueFree();
        }
        GD.Print($"remain follow: {withPoint} of {worn} worn starting weapons have a Point01 node");
        Check(worn == StartWeapons.Length && withPoint == worn, $"{withPoint} of {worn} worn of {StartWeapons.Length} starting weapons have a Point01 node");
    }

    /// <summary>
    /// A real npc whose row scales its model (NpcLayer puts row.Scale on the visual): each of its dummies whose parent is the
    /// model root or a bone takes the parent's scale only with applyLocalScale (DummyWorld.Resolve): its frame's scale is the
    /// row's, times the parent's with applyLocalScale, and its offset from the parent the row's translate, likewise scaled;
    /// a type 1 part on it hangs where a type 0 part reads it. The npc picked is the first scaled one with a dummy without
    /// applyLocalScale under the model root or a bone, preferring an ordinary (not world) one.
    /// </summary>
    void ScaledNpc(NpcTable table)
    {
        bool Free(EffectDummy d) => !d.ApplyLocalScale && !d.ParentNode.StartsWith("Eff_", System.StringComparison.Ordinal);
        NpcRow pick = null;
        int pickRank = -1;
        foreach (var r in table.Rows)
        {
            if (System.Math.Abs(r.Scale - 1f) < 0.25f || string.IsNullOrEmpty(r.Kfm)) continue;
            int rank = 0;
            foreach (var d in r.EffectDummies) if (Free(d)) rank = System.Math.Max(rank, d.ApplyWorldTransform ? 1 : 2);
            if (rank > pickRank || rank == pickRank && pick != null && r.Id < pick.Id) { pick = r; pickRank = rank; }
        }
        Check(pick != null && pickRank > 0, "no scaled npc row with a dummy without applyLocalScale");
        if (pick == null) return;
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 3, NpcId = pick.Id }));
        var root = npcs.RootOf(3);
        var sk = root != null ? EffectAttach.SkeletonOf(root) : null;
        var model = sk?.GetParentOrNull<Node3D>();
        Check(model != null, $"npc {pick.Id} built no model");
        if (model == null) return;
        root.GlobalTransform = new Transform3D(new Basis(Vector3.Up, 0.7f), new Vector3(-300, 0, 120));
        int checkedRows = 0, free = 0;
        foreach (var d in pick.EffectDummies)
        {
            Transform3D parentWorld;
            if (d.ParentNode == "Scene Root") parentWorld = model.GlobalTransform;
            else if (sk.FindBone(d.ParentNode) is int b and >= 0) parentWorld = sk.GlobalTransform * sk.GetBoneGlobalPose(b);
            else continue;
            var w = combat.WorldPlace(new EffectPart { ApplyNodeTransform = true, ParentNode = d.Name }, root);
            w = field.GlobalTransform * w * EffectAttach.YUp.AffineInverse();    // back to the dummy's own frame
            float ps = d.ApplyLocalScale ? parentWorld.Basis.Scale.X : 1f;
            var t = new Vector3(d.Translate.X, d.Translate.Y, d.Translate.Z);
            float want = d.ApplyWorldTransform ? new Vector3(t.X, t.Y, t.Z * ps).Length() : t.Length() * ps;
            float off = w.Origin.DistanceTo(parentWorld.Origin);
            Check(Mathf.Abs(w.Basis.Scale.X - d.Scale * ps) < 1e-3f, $"npc {pick.Id} (scale {pick.Scale}) dummy {d.Name}: scale {w.Basis.Scale.X}, not {d.Scale * ps}");
            Check(Mathf.Abs(off - want) < 0.05f, $"npc {pick.Id} (scale {pick.Scale}) dummy {d.Name}: {off} from its parent, not {want}");
            Check(EffectAttach.Hang(new EffectPart { AttachToNode = true, ParentNode = d.Name, ApplyNodeTransform = true }, root, out var h) && h.On == EffectHang.Dummy
                && Near(h.Host.GlobalTransform * h.Place, field.GlobalTransform * combat.WorldPlace(new EffectPart { ApplyNodeTransform = true, ParentNode = d.Name }, root)),
                $"npc {pick.Id} dummy {d.Name}: a type 1 part did not hang where a type 0 part reads it");
            checkedRows++;
            if (!d.ApplyLocalScale) free++;
        }
        GD.Print($"remain follow: scaled npc {pick.Id} (scale {pick.Scale}): {checkedRows} dummies checked, {free} without applyLocalScale");
        Check(free > 0, $"npc {pick.Id}: no dummy without applyLocalScale was checked");
        npcs.Remove(3, false);
    }

    /// <summary>
    /// Type 2 (FUN_14154c260, nifeffectparam.cpp:1118-1137) on real worn items whose idle effects have type 2 parts, one
    /// per way the name is found (EffectAttachSweepTests): the male glow star cap's CP is a mesh, the female one's CP its
    /// self node (an empty node, its meshes under it), the glow star cape's MT_Point01 a node of its kfm model. Each part
    /// hangs on the parent of that node, at it, without its own transform.
    /// </summary>
    void WornItems(PackSet packs, ModelBuilder builder)
    {
        var palettes = new Look.ColorPalettes(packs);
        foreach (var (id, gender, slot, name) in new[] { (11300951, 0, EquipSlot.CP, "CP"), (11300952, 1, EquipSlot.CP, "CP"), (11820387, 0, EquipSlot.MT, "MT_Point01") })
        {
            var md = builder.LoadModel(gender == 0 ? "male/m_body.nif" : "female/f_body.nif");
            var actor = new Node3D { Position = new Vector3(id % 1000, 0, 50) };
            field.AddChild(actor);
            var visual = builder.Build(md, gender == 0 ? "Male" : "Female");
            actor.AddChild(visual);
            actor.SetMeta(EffectAttach.PcMeta, true);
            new EquipApplier(packs, builder) { Palettes = palettes }.Apply(visual, md, gender, new[] { new EquipItem { Id = id, Slot = slot } });
            bool hung = EffectAttach.Hang(new EffectPart { AttachType = "Mesh", ParentNode = name, ApplyNodeTransform = true }, actor, out var h);
            Check(hung && h.On == EffectHang.ItemMesh && !h.OwnTransform, $"item {id}: a type 2 part on [{name}] hung on {h.On}");
            if (hung && slot == EquipSlot.CP)
            {
                var box = Bounds(visual, $"eq{id}_", null);
                var at = (h.Host.GlobalTransform * h.Place).Origin;
                Check(box is { } b && b.Grow(10).HasPoint(at), $"item {id}: the type 2 part on [{name}] starts at {at}, outside the item {box}");
            }
            else if (hung)
            {
                // the kfm cape is skinned (no posed bounds): the part is at its MT_Point01 bone, by the spine it hangs on
                var body = EffectAttach.SkeletonOf(visual);
                var itemSk = h.Host as Skeleton3D ?? h.Host.GetParentOrNull<Skeleton3D>();
                int b = itemSk?.FindBone(name) ?? -1;
                Check(b >= 0 && Near(h.Host.GlobalTransform * h.Place, itemSk.GlobalTransform * itemSk.GetBoneGlobalPose(b) * EffectAttach.YUp),
                    $"item {id}: the type 2 part on [{name}] is not at the item's node");
                var spine = body.GlobalTransform * body.GetBoneGlobalPose(body.FindBone("Bip01 Spine1"));
                float d = (h.Host.GlobalTransform * h.Place).Origin.DistanceTo(spine.Origin);
                Check(d < 40f, $"item {id}: the type 2 part on [{name}] is {d} from Bip01 Spine1");
            }
            actor.QueueFree();
        }
    }

    /// <summary>The global bounds of the meshes under <paramref name="n"/> whose names, or an ancestor's (a kfm model's root),
    /// start with <paramref name="prefix"/>.</summary>
    static Aabb? Bounds(Node n, string prefix, Aabb? into, bool under = false)
    {
        under |= n.Name.ToString().StartsWith(prefix, System.StringComparison.Ordinal);
        if (n is MeshInstance3D m && under)
        {
            var box = m.GlobalTransform * m.GetAabb();
            into = into is { } b ? b.Merge(box) : box;
        }
        int count = n.GetChildCount();
        for (int i = 0; i < count; i++) into = Bounds(n.GetChild(i), prefix, into, under);
        return into;
    }

    void AfterMove()
    {
        if (fx != null && GodotObject.IsInstanceValid(fx) && GodotObject.IsInstanceValid(movedRoot))
        {
            var now = movedRoot.GlobalTransform.AffineInverse() * fx.GlobalPosition;
            Check(now.DistanceTo(fxOnRoot) < 0.01f, $"the effect is at {now} on its moved target, not {fxOnRoot}: it did not move with it");
        }
        npcs.Remove(1, false);
    }

    static void Walk(Node n, HashSet<Node> into)
    {
        into.Add(n);
        int count = n.GetChildCount();
        for (int i = 0; i < count; i++) Walk(n.GetChild(i), into);
    }

    /// <summary>The first node under <paramref name="n"/> not in <paramref name="seen"/> that is an effect part's holder (an
    /// effect instance, or a node holding a built "Effect" root).</summary>
    static Node3D NewEffect(Node n, HashSet<Node> seen)
    {
        int count = n.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            var c = n.GetChild(i);
            if (!seen.Contains(c) && c is Node3D d && (c is EffectInstance || c.GetNodeOrNull(EffectRoot) != null)) return d;
            if (NewEffect(c, seen) is { } f) return f;
        }
        return null;
    }

    /// <summary>
    /// The type 0 start place's other cases (CombatLayer.WorldPlace, AttachToOwnerNode nifeffectparam.cpp:1044-1072), on an
    /// actor turned, scaled 2 and moved, with the field moved off identity:
    /// - all bits give the node's world transform; rotation and scale masked off keep only its translation; translate masked
    ///   off keeps only its rotation and scale;
    /// - without applyNodeTransform the part is at the world origin, wherever the field is (the holy strike remain played
    ///   on the actor starts there too);
    /// - parentNode finds a bone of the actor's model, its frame turned to Y up (a bone turned about the nif's up turns the
    ///   part about Godot's up), else one of its effect dummies (Eff_Body_World: at the spine, upright and unscaled; Eff_UI:
    ///   170 up from the root; Eff_Head: a child of the head bone), else falls back to the model root, the actor's own frame.
    /// The actor is built as ModelBuilder builds one: a "Model" node turned by EffectNif.ZUp holding the skeleton.
    /// </summary>
    void Places()
    {
        var actor = new Node3D { Position = new Vector3(40, 0, 30), Rotation = new Vector3(0, 1f, 0), Scale = new Vector3(2, 2, 2) };
        field.AddChild(actor);
        field.Position = new Vector3(10, 0, 0);
        var model = new Node3D { Name = "Model", Transform = Ui.EffectNif.ZUp };
        actor.AddChild(model);
        var sk = new Skeleton3D { Name = "Skeleton" };
        model.AddChild(sk);
        int head = sk.AddBone("Bip01 Head");
        sk.SetBoneRest(head, new Transform3D(Basis.Identity, new Vector3(0, 5, 70)));      // the nif's frame: 70 up, 5 back
        int spine = sk.AddBone("Bip01 Spine1");
        sk.SetBoneRest(spine, new Transform3D(new Basis(Vector3.Back, 0.5f), new Vector3(0, 0, 50)));
        sk.ResetBonePoses();
        // a PC's dummies (Eff_Body_World and Eff_UI upright, Eff_Head an ordinary child)
        EffectAttach.SetDummies(actor, new[]
        {
            new EffectDummy { Name = "Eff_Body_World", ParentNode = "Bip01 Spine1", ApplyWorldTransform = true, ApplyLocalScale = false },
            new EffectDummy { Name = "Eff_UI", ParentNode = "Scene Root", ApplyWorldTransform = true, ApplyLocalScale = false, Translate = new System.Numerics.Vector3(0, 0, 170) },
            new EffectDummy { Name = "Eff_Head", ParentNode = "Bip01 Head", Translate = new System.Numerics.Vector3(80, 0, 0) },
            new EffectDummy { Name = "Eff_Head_Free", ParentNode = "Bip01 Head", ApplyLocalScale = false, Translate = new System.Numerics.Vector3(80, 0, 0) },
            new EffectDummy { Name = "Eff_Over_Head", ParentNode = "Eff_Head_Free", ApplyLocalScale = false, Translate = new System.Numerics.Vector3(0, 0, 10) },
        });
        Transform3D World(EffectPart p) => field.GlobalTransform * combat.WorldPlace(p, actor);
        Check(World(new EffectPart { ApplyNodeTransform = true }).IsEqualApprox(actor.GlobalTransform), "all bits: not the actor's world transform");
        var t = World(new EffectPart { ApplyNodeTransform = true, ApplyRotation = false, ApplyScale = false });
        Check(t.Basis.IsEqualApprox(Basis.Identity) && t.Origin.IsEqualApprox(actor.GlobalPosition), $"translate only gave {t}");
        var r = World(new EffectPart { ApplyNodeTransform = true, ApplyTranslate = false });
        Check(r.Origin.IsEqualApprox(Vector3.Zero) && r.Basis.IsEqualApprox(actor.GlobalBasis), $"rotation and scale only gave {r}");
        var none = World(new EffectPart());
        Check(none.IsEqualApprox(Transform3D.Identity), $"no applyNodeTransform starts at {none.Origin}, not the world origin");

        var spineAt = World(new EffectPart { ApplyNodeTransform = true, ParentNode = "Bip01 Spine1" });
        Check(spineAt.IsEqualApprox(actor.GlobalTransform * new Transform3D(new Basis(Vector3.Up, 0.5f), new Vector3(0, 50, 0))),
            $"the Bip01 Spine1 bone gave {spineAt}, not its Y-up frame");
        // upright at the spine: the actor's turn, not the bone's, and not its scale (applyLocalScale off)
        var bodyAt = World(new EffectPart { ApplyNodeTransform = true, ParentNode = "Eff_Body_World" });
        Check(bodyAt.IsEqualApprox(new Transform3D(new Basis(Vector3.Up, 1f), actor.GlobalTransform * new Vector3(0, 50, 0))),
            $"the Eff_Body_World dummy gave {bodyAt}");
        var uiAt = World(new EffectPart { ApplyNodeTransform = true, ParentNode = "Eff_UI" });
        Check(uiAt.IsEqualApprox(new Transform3D(new Basis(Vector3.Up, 1f), actor.GlobalPosition + new Vector3(0, 170, 0))), $"the Eff_UI dummy gave {uiAt}");
        var headAt = World(new EffectPart { ApplyNodeTransform = true, ParentNode = "Eff_Head" });
        Check(headAt.IsEqualApprox(actor.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(80, 70, -5))), $"the Eff_Head dummy gave {headAt}");
        Check(World(new EffectPart { ApplyNodeTransform = true, ParentNode = "Bip01 Head" }).IsEqualApprox(actor.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 70, -5))),
            "the parentNode bone was not used");
        // without applyLocalScale the actor's scale 2 does not reach the row's offset nor the part (DummyWorld.Resolve)
        var unscaled = new Transform3D(actor.GlobalBasis.Orthonormalized(), actor.GlobalTransform * new Vector3(0, 70, -5) + actor.GlobalBasis.Orthonormalized() * new Vector3(80, 0, 0));
        var freeAt = World(new EffectPart { ApplyNodeTransform = true, ParentNode = "Eff_Head_Free" });
        Check(Near(freeAt, unscaled), $"the Eff_Head_Free dummy (no local scale) gave {freeAt}, not {unscaled}");
        var overAt = World(new EffectPart { ApplyNodeTransform = true, ParentNode = "Eff_Over_Head" });
        Check(Near(overAt, unscaled * new Transform3D(Basis.Identity, new Vector3(0, 10, 0))), $"the Eff_Over_Head dummy (under Eff_Head_Free) gave {overAt}");
        Check(World(new EffectPart { ApplyNodeTransform = true, ParentNode = "No Such Node" }).IsEqualApprox(actor.GlobalTransform),
            "a missing parentNode does not fall back to the model root");
        Check(sk.GetChildCount() == 0, $"type 0 parts made {sk.GetChildCount()} nodes under the skeleton (bone or dummy nodes)");
        // a type 1 part on each dummy hangs where a type 0 part reads the dummy's frame
        foreach (var name in new[] { "Eff_Body_World", "Eff_UI", "Eff_Head", "Eff_Head_Free", "Eff_Over_Head" })
        {
            var p0 = World(new EffectPart { ApplyNodeTransform = true, ParentNode = name });
            Check(EffectAttach.Hang(new EffectPart { AttachToNode = true, ParentNode = name, ApplyNodeTransform = true }, actor, out var hn) && hn.On == EffectHang.Dummy
                && Near(hn.Host.GlobalTransform * hn.Place, p0), $"a type 1 part on {name} hung at {hn.Host?.GlobalTransform * hn.Place}, a type 0 part reads {p0}");
        }
        idleDummy = sk.GetNodeOrNull<EffectDummyNode3D>("fxd_Eff_UI");
        Check(idleDummy != null && idleDummy.IsProcessing(), "the Eff_UI dummy a part was hung on is not following");
        Hangs(actor, model, sk);

        var parts = effects.Get(OriginXml);
        Check(parts != null && parts.Exists(p => p.Type == "nif") && parts.TrueForAll(p => p.Type != "nif" || (p.AttachKind == 0 && !p.ApplyNodeTransform)),
            "the holy strike remain is not type 0 without applyNodeTransform");
        int c0 = field.GetChildCount();
        combat.PlayEffectOn(OriginXml, actor, 5);
        var h = field.GetChildCount() > c0 ? field.GetChild(field.GetChildCount() - 1) as Node3D : null;
        Check(h != null && h.GlobalPosition.IsEqualApprox(Vector3.Zero), $"the holy strike remain starts at {h?.GlobalPosition}, not the world origin");
        field.Position = Vector3.Zero;
    }

    /// <summary>
    /// Types 1-4 (EffectAttach.Hang, nifeffectparam.cpp:1079-1172) on the same actor:
    /// - type 1 hangs on one shared attachment of its bone, the holder at the bone's Y-up frame; on a dummy's node (upright at
    ///   the spine for Eff_Body_World); an empty or unknown name on the model root, at the actor's frame;
    /// - update bits not all set put a SeparateNode under the bone, which keeps only those parts (rotation off: no turn, the
    ///   scale 2 kept) and goes once empty;
    /// - no applyNodeTransform starts the holder at the world origin;
    /// - types 2-4 make no part on an owner that is not a PC, nor 3 / 4 on a PC with no weapon; type 2 on a PC hangs on the
    ///   parent of the worn mesh of that name, at that mesh, without the part's own transform.
    /// </summary>
    void Hangs(Node3D actor, Node3D model, Skeleton3D sk)
    {
        Transform3D At(EffectAttach.Hung h) => h.Host.GlobalTransform * h.Place;
        var onHead = new EffectPart { AttachToNode = true, ParentNode = "Bip01 Head", ApplyNodeTransform = true };
        bool hung = EffectAttach.Hang(onHead, actor, out var h1);
        Check(hung && h1.Host is BoneAttachment3D && h1.Host.GetParent() == sk && h1.On == EffectHang.Bone, $"a type 1 part hung on {h1.On} {h1.Host?.Name}");
        if (!hung) return;
        Check(At(h1).IsEqualApprox(actor.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 70, -5))), $"the type 1 holder is at {At(h1)}");
        Check(EffectAttach.Hang(onHead, actor, out var h2) && h2.Host == h1.Host, "a second part on the bone made a second attachment");
        Check(EffectAttach.Hang(new EffectPart { AttachToNode = true, ParentNode = "Eff_Body_World", ApplyNodeTransform = true }, actor, out var hd)
            && hd.On == EffectHang.Dummy && hd.Host is EffectDummyNode3D
            && At(hd).IsEqualApprox(new Transform3D(new Basis(Vector3.Up, 1f), actor.GlobalTransform * new Vector3(0, 50, 0))), $"a type 1 part on Eff_Body_World hung on {hd.On} at {At(hd)}");
        foreach (var name in new[] { "", "No Such Node" })
            Check(EffectAttach.Hang(new EffectPart { AttachToNode = true, ParentNode = name, ApplyNodeTransform = true }, actor, out var hm)
                && hm.Host == model && hm.On == EffectHang.ModelRoot && At(hm).IsEqualApprox(actor.GlobalTransform), $"parentNode [{name}] did not hang on the model root");
        var noTurn = new EffectPart { AttachToNode = true, ParentNode = "Bip01 Head", ApplyNodeTransform = true, UpdateRotation = false };
        if (EffectAttach.Hang(noTurn, actor, out var hs) && hs.Host is Ui.SeparateNode3D)
        {
            emptySeparate = hs.Host;
            Check(hs.Host.GetParent() == h1.Host, "the SeparateNode is not under the bone");
            Check(At(hs).Origin.IsEqualApprox(At(h1).Origin) && At(hs).Basis.IsEqualApprox(Basis.Identity.Scaled(new Vector3(2, 2, 2))),
                $"the SeparateNode holder is at {At(hs)}");
        }
        else Check(false, "update rotation off made no SeparateNode");
        Check(EffectAttach.Hang(new EffectPart { AttachToNode = true, ParentNode = "Bip01 Head" }, actor, out var hr) && Near(At(hr), Transform3D.Identity),
            $"no applyNodeTransform starts at {At(hr)}");
        // with a SeparateNode the relative start lands on it, whose world ignores its local: the part stays at the node
        Check(EffectAttach.Hang(new EffectPart { AttachToNode = true, ParentNode = "Bip01 Head", UpdateRotation = false }, actor, out var hsr)
            && hsr.Host is Ui.SeparateNode3D && At(hsr).Origin.IsEqualApprox(At(h1).Origin), $"no applyNodeTransform on a SeparateNode starts at {At(hsr)}, not the head");
        foreach (var kind in new[] { "Mesh", "WeaponL", "WeaponR" })
            Check(!EffectAttach.Hang(new EffectPart { AttachType = kind, ParentNode = "CP", ApplyNodeTransform = true }, actor, out _), $"a {kind} part hung on an owner that is not a PC");
        actor.SetMeta(EffectAttach.PcMeta, true);
        Check(!EffectAttach.Hang(new EffectPart { AttachType = "WeaponR", ApplyNodeTransform = true }, actor, out _), "a weapon part hung on a PC with no weapon");
        var att = new BoneAttachment3D { BoneName = "Bip01 Head" };
        sk.AddChild(att);
        var cp = new MeshInstance3D { Name = "eq1_CP", Transform = new Transform3D(Basis.Identity, new Vector3(0, 0, 10)) };
        cp.SetMeta("mesh", "CP");
        att.AddChild(cp);
        Check(EffectAttach.Hang(new EffectPart { AttachType = "Mesh", ParentNode = "CP", ApplyNodeTransform = true }, actor, out var hc)
            && hc.Host == att && !hc.OwnTransform && At(hc).IsEqualApprox(cp.GlobalTransform * EffectAttach.YUp), "a type 2 part did not hang at the worn mesh");
        Check(EffectAttach.Hang(new EffectPart { AttachType = "Mesh", ParentNode = "CP" }, actor, out var hc2) && !hc2.OwnTransform
            && At(hc2).IsEqualApprox(cp.GlobalTransform * EffectAttach.YUp), "a type 2 part without applyNodeTransform did not take the worn mesh's transform");
        Check(EffectAttach.Hang(new EffectPart { AttachType = "Mesh", ParentNode = "CP", ApplyNodeTransform = true, UpdateScale = false }, actor, out var hc3)
            && hc3.Host is Ui.SeparateNode3D && hc3.Host.GetParent() == att && hc3.OwnTransform, "a type 2 part on a SeparateNode did not keep its own transform under it");
        // a node of a worn kfm item (the item's own skeleton, as EquipApplier.AttachKfm builds it): on its parent bone, at it
        var itemSk = new Skeleton3D { Name = "ItemSkeleton" };
        att.AddChild(itemSk);
        int mtRoot = itemSk.AddBone("MT_Root");
        int mt = itemSk.AddBone("MT_Point01");
        itemSk.SetBoneParent(mt, mtRoot);
        itemSk.SetBoneRest(mtRoot, new Transform3D(Basis.Identity, new Vector3(0, 0, 4)));
        itemSk.SetBoneRest(mt, new Transform3D(new Basis(Vector3.Back, 0.3f), new Vector3(0, -6, 0)));
        itemSk.ResetBonePoses();
        Check(EffectAttach.Hang(new EffectPart { AttachType = "Mesh", ParentNode = "MT_Point01", ApplyNodeTransform = true }, actor, out var hk)
            && hk.On == EffectHang.ItemMesh && hk.Host is BoneAttachment3D ba && ba.GetParent() == itemSk && ba.BoneName == "MT_Root" && !hk.OwnTransform
            && Near(At(hk), itemSk.GlobalTransform * itemSk.GetBoneGlobalPose(mt) * EffectAttach.YUp), $"a type 2 part on a kfm item's node hung on {hk.Host?.Name} at {At(hk)}");
        actor.RemoveMeta(EffectAttach.PcMeta);
    }

    void AfterRemove()
    {
        Check(emptySeparate == null || !GodotObject.IsInstanceValid(emptySeparate), "an empty SeparateNode stayed");
        Check(idleDummy == null || !GodotObject.IsInstanceValid(idleDummy) || !idleDummy.IsProcessing(), "a dummy with no part on it still follows each frame");
        Check(fx == null || !GodotObject.IsInstanceValid(fx), "the effect outlived its removed target");
        Check(fieldFx != null && GodotObject.IsInstanceValid(fieldFx), "the second npc's effect went too early");
        Check(worldFx != null && GodotObject.IsInstanceValid(worldFx), "the type 0 effect went with its target");
        combat.Process(0.016f);
        field.QueueFree();
    }

    void AfterField()
    {
        Check(fieldFx == null || !GodotObject.IsInstanceValid(fieldFx), "the effect outlived the field");
        Check(worldFx == null || !GodotObject.IsInstanceValid(worldFx), "the type 0 effect outlived the field");
        Finish();
    }

    void Finish()
    {
        done = true;
        GD.Print($"remain follow: {xml}, {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
