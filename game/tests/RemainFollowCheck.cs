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
    }

    /// <summary>The starting weapons of the eleven jobs (KMS2 table/job.xml startItems), worn in the right hand here.</summary>
    static readonly int[] StartWeapons = { 13100225, 13200220, 13300219, 13400218, 15000224, 15100216, 15200223, 15300199, 15400274, 15500514, 15600514 };

    /// <summary>
    /// Types 3 and 4 (AttachToEquipSlot, nifeffectparam.cpp:1142-1151) on a PC wearing each job's starting weapon in the
    /// right hand (EquipApplier.Apply): a type 4 part hangs on that weapon's Point01 node (FindPoint01Node,
    /// equipitem.cpp:604-613), within the bounds of that weapon's meshes; a type 3 part with no left hand weapon
    /// makes none. At least one weapon has the node.
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
            if (rig != null) worn++;
            if (rig != null && rig.WeaponPoint(EquipSlot.RH, out var mesh, out _))
            {
                withPoint++;
                bool hung = EffectAttach.Hang(new EffectPart { AttachType = "WeaponR", ApplyNodeTransform = true }, actor, out var h);
                Check(hung && h.On == EffectHang.WeaponPoint && h.Host == mesh, $"item {id}: a type 4 part hung on {h.On} {h.Host?.Name}, not {mesh.Name}");
                if (hung)
                {
                    var at = (h.Host.GlobalTransform * h.Place).Origin;
                    var box = Bounds(visual, $"eq{id}_", null);
                    Check(box is { } b && b.Grow(10).HasPoint(at), $"item {id}: the type 4 part starts at {at}, outside its weapon {box}");
                }
            }
            Check(!EffectAttach.Hang(new EffectPart { AttachType = "WeaponL", ApplyNodeTransform = true }, actor, out _), $"item {id}: a type 3 part hung with no left hand weapon");
            actor.QueueFree();
        }
        GD.Print($"remain follow: {withPoint} of {worn} worn starting weapons have a Point01 node");
        Check(withPoint > 0, "no starting weapon has a Point01 node");
    }

    /// <summary>The global bounds of the meshes under <paramref name="n"/> whose names start with <paramref name="prefix"/>.</summary>
    static Aabb? Bounds(Node n, string prefix, Aabb? into)
    {
        if (n is MeshInstance3D m && m.Name.ToString().StartsWith(prefix, System.StringComparison.Ordinal))
        {
            var box = m.GlobalTransform * m.GetAabb();
            into = into is { } b ? b.Merge(box) : box;
        }
        int count = n.GetChildCount();
        for (int i = 0; i < count; i++) into = Bounds(n.GetChild(i), prefix, into);
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
        Check(World(new EffectPart { ApplyNodeTransform = true, ParentNode = "No Such Node" }).IsEqualApprox(actor.GlobalTransform),
            "a missing parentNode does not fall back to the model root");
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
        actor.RemoveMeta(EffectAttach.PcMeta);
    }

    void AfterRemove()
    {
        Check(emptySeparate == null || !GodotObject.IsInstanceValid(emptySeparate), "an empty SeparateNode stayed");
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
