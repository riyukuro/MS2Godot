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
/// - its nodes are children of the target's root, and they move when the root moves;
/// - FieldRemoveNpc frees them with the root, and the live fade list then ticks without them;
/// - the effect on the second npc goes when the field node goes;
/// - an actor that is gone (null) plays nothing.
/// A type 0 effect (the staff attack remain) on the first npc goes under the field at the npc's place, stays there when
/// the npc moves and outlives the npc's removal. The node effect is the first (in name order) whose nif parts are all
/// type 1+ with a first part lasting at least 2 s, so a part does not end on its own during the check.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class RemainFollowCheck : Node3D
{
    int frame;
    bool done;
    readonly List<string> fails = new();
    Node3D field, fx, fieldFx, worldFx;
    const string WorldXml = "ch/weapon/staff/eff_staff_attack01_remain_01.xml";
    NpcLayer npcs;
    CombatLayer combat;
    string xml;

    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    public override void _Process(double delta)
    {
        if (done) return;
        frame++;
        if (frame == 2)
        {
            try { Start(); }
            catch (System.Exception ex) { Check(false, "setup threw " + ex.Message); Finish(); }
        }
        else if (frame == 4) AfterRemove();
        else if (frame == 6) AfterField();
    }

    void Start()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var table = new NpcTable(packs);
        var effects = new EffectTable(packs);
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

        int n0 = root.GetChildCount();
        combat.PlayEffectOn(xml, root, 5);
        Check(combat.EffectsOnActors == 1, $"effects on actors {combat.EffectsOnActors}");
        Check(root.GetChildCount() > n0, "the effect added no child to the target's root");
        fx = root.GetChildCount() > n0 ? root.GetChild(n0) as Node3D : null;
        Check(fx != null && fx.GetParent() == root, "the effect node is not under the target's root");
        if (fx != null)
        {
            var before = fx.GlobalPosition;
            root.GlobalPosition += new Vector3(500, 0, 0);
            var moved = fx.GlobalPosition - before;
            Check(Mathf.Abs(moved.X - 500) < 0.01f && Mathf.Abs(moved.Y) < 0.01f && Mathf.Abs(moved.Z) < 0.01f,
                $"the effect moved by {moved}, not with its target");
        }

        int m0 = root2.GetChildCount();
        combat.PlayEffectOn(xml, root2, 5);
        fieldFx = root2.GetChildCount() > m0 ? root2.GetChild(m0) as Node3D : null;
        Check(fieldFx != null, "the second effect added no child");

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

        npcs.Remove(1, false);
    }

    void AfterRemove()
    {
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
