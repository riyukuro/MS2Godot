using System.Collections.Generic;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/npc_capsule.tscn) of a live npc's controller and the skill move's ignored groups:
/// - NpcLayer gives a live npc the controller capsule of its row (FUN_141670d50, unit_14108e1e0_npcactorandvibrate.cpp:2101)
///   in group 4 (CNpc::vfunc_126 at the spawn, :541), group 5 with the row's capsule ignore, and an npc added dead or
///   killed later goes to group 5 (StateDeadNPC::vfunc_4, statedead.cpp:248).
/// - The own player's controller move (PCBase::vfunc_68) stops at the live npc; a move that leaves out the skill motion's
///   ignoreCollisionGroups 20 (groups 2 and 4, heavy gunner JetBoots 10600081; StateSkill::FUN_1417edc20,
///   stateskill.cpp:2862-2874) passes it, and so does a plain move once the npc is dead.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class NpcCapsuleCheck : Node3D
{
    readonly Movement.NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    NpcLayer npcs;
    Player runner;
    int frames;

    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    static uint Layer(int id, Node parent) =>
        parent.GetNodeOrNull<StaticBody3D>($"Npc{id}/Capsule")?.CollisionLayer ?? 0;

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var table = new Ms2.Formats.World.NpcTable(packs);
        npcs = new NpcLayer(packs, builder, table, this);
        var ids = new List<int>();
        foreach (int id in new[] { 21000001, 21000002, 21000003, 11000001 })
            if (table.Find(id) != null) ids.Add(id);
        Check(ids.Count >= 2, $"{ids.Count} npc rows");
        if (ids.Count < 2) return;
        Check(!table.Find(ids[0]).CapsuleIgnore, $"npc {ids[0]} has the capsule ignore");
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 1, NpcId = ids[0] }));
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 2, NpcId = ids[0], Hidden = true }));
        // a row with the capsule ignore (the test's own copy of the flag, set back after the add)
        var ignoreRow = table.Find(ids[1]);
        bool keep = ignoreRow.CapsuleIgnore;
        ignoreRow.CapsuleIgnore = true;
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 3, NpcId = ids[1] }));
        ignoreRow.CapsuleIgnore = keep;
        Check(Layer(1, this) == 1u << 4, $"live npc capsule layer {Layer(1, this):x}, not group 4");
        Check(Layer(2, this) == 1u << 5, $"npc added dead: capsule layer {Layer(2, this):x}, not group 5");
        Check(Layer(3, this) == 1u << 5, $"capsule-ignore npc: capsule layer {Layer(3, this):x}, not group 5");
        var capsule = GetNodeOrNull<StaticBody3D>("Npc1/Capsule");
        Check(capsule != null && capsule.HasMeta(Movement.MoveConfig.ControllerMeta), "the capsule is no controller");

        var floor = new StaticBody3D { Position = new Vector3(0, -50, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4000, 100, 4000) } });
        AddChild(floor);
        var visual = new Node3D();
        var anim = new AnimationPlayer();
        visual.AddChild(anim);
        runner = Player.Create(visual, anim, faceGrids);
        AddChild(runner);
        runner.SetProcess(false);
        runner.SetPhysicsProcess(false);
        runner.ScriptDriven = true;
        runner.BoundsMin = new Vector3(-2000, -2000, -1000);
        runner.BoundsMax = new Vector3(2000, 2000, 1000);
        // the other two npcs out of the way of the runner's line
        GetNode<Node3D>("Npc2").Position = new Vector3(0, 4, 1000);
        GetNode<Node3D>("Npc3").Position = new Vector3(0, 4, -1000);
    }

    // the runner from x -300 along +x by 10 a step for 60 steps, its moves leaving out exclude; the end x
    float Rush(uint exclude)
    {
        var step = typeof(Player).GetMethod("MoveStep", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        runner.GlobalPosition = new Vector3(-300, 0.5f, 0);
        for (int i = 0; i < 60; i++) step.Invoke(runner, new object[] { new Vector3(10, 0, 0), 1f / 60, exclude });
        return runner.GlobalPosition.X;
    }

    void Run()
    {
        if (runner != null)
        {
            float plain = Rush(0), ignored = Rush(20);
            Check(plain < 0, $"a plain move went through the live npc (x {plain:0})");
            Check(ignored > 250, $"a move leaving out groups 2 and 4 stopped at the npc (x {ignored:0})");
            npcs.Remove(1, true);
            Check(Layer(1, this) == 1u << 5, $"killed npc capsule layer {Layer(1, this):x}, not group 5");
            float dead = Rush(0);
            Check(dead > 250, $"a plain move stopped at the dead npc (x {dead:0})");
            GD.Print($"npc capsule check: plain move ends at x {plain:0}, the ignore-20 move at {ignored:0}, past the dead npc at {dead:0}");
        }
        foreach (var f in fails) GD.Print($"npc capsule check: FAIL {f}");
        GD.Print($"npc capsule check: {(fails.Count == 0 ? "OK" : "FAIL")}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (++frames < 3) return;
        SetPhysicsProcess(false);
        try { Run(); }
        catch (System.Exception e) { GD.Print($"npc capsule check: {e.InnerException ?? e}: FAIL"); GetTree().Quit(1); }
    }
}
