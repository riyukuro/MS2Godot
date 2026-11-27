using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Replay;
using Ms2Godot.Movement;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/skill_key_order.tscn) of the own cast's key order against its move: the client throws a
/// frame's text keys in the actor update before the state manager's update moves the body (PCBase::vfunc_53,
/// pcbase.cpp:3730-3732; StateSkill::vfunc_3's FUN_1417edc20), so an attack point at the time of a move key fills where the
/// body stood before the move. Knight ShieldRush (10100091, Knight_ShieldRush_A: p0 and move0 at 0.1 s, a 450 rush with
/// ignoreCollisionGroups 20) plays on the own player:
/// - <see cref="Player.SkillKeysBeforeMove"/> (the HUD's attack key read) runs in the physics step that passes 0.1, with the
///   body where it stood when the rush started;
/// - a read after that physics step (the HUD frame's own read, where the point used to fill) sees the body already moved;
/// - the rush then moves the body forward.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class SkillKeyOrderCheck : Node3D
{
    readonly NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    Player pc;
    MethodInfo step;
    PackSet packs;
    int frames;
    const float Dt = 1f / 60;
    const int ShieldRush = 10100091;
    const string Sequence = "Knight_ShieldRush_A";

    public override void _Ready()
    {
        packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        builder.TextureArchives = builder.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
        var sb = new StaticBody3D { Position = new Vector3(0, -50, 0) };
        sb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4000, 100, 4000) } });
        AddChild(sb);
        var (visual, anim, md, bodyDir) = Ms2Godot.Look.PlayerLook.Build(builder, packs);
        pc = Player.Create(visual, anim, faceGrids);
        pc.Anims = ActionAnims.Load(packs, builder, md, anim, bodyDir);
        AddChild(pc);
        pc.SetProcess(false);
        pc.SetPhysicsProcess(false);
        pc.ScriptDriven = true;
        pc.BoundsMin = new Vector3(-2000, -2000, -1000);
        pc.BoundsMax = new Vector3(2000, 2000, 1000);
        pc.GlobalPosition = new Vector3(0, 5, 0);
        step = typeof(Player).GetMethod("Step", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    void Expect(bool ok, string what) { if (!ok) fails.Add(what); }

    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    void Run()
    {
        for (int i = 0; i < 30; i++) { step.Invoke(pc, new object[] { (double)Dt }); pc.Anim.Advance(Dt); }   // land
        var table = Ms2.Formats.World.SkillTable.For(packs);
        var m = table.Motion(ShieldRush, 1, 0);
        var st = table.StateMove(ShieldRush);
        Expect(m != null && m.IgnoreCollisionGroups == 20, $"ShieldRush motion {(m == null ? "missing" : $"ignore {m.IgnoreCollisionGroups}")}");
        if (m == null) return;
        var move = new CastMove(m.MoveType, m.KeyMove, m.MoveDistance, m.MoveAngle, st.MoveType, st.MovingInMovekey, st.MagicControl,
            new System.Numerics.Vector3(1, 0, 0), m.IgnoreCollisionGroups);
        Expect(pc.PlaySkillSequence(Sequence, 1, move, 0), $"{Sequence} did not play");
        float p0 = pc.Anims.TextKey(pc.SkillSequence, "p0"), move0 = pc.Anims.TextKey(pc.SkillSequence, "move0");
        Expect(p0 > 0 && p0 == move0, $"{Sequence} keys p0 {p0} move0 {move0}");
        if (p0 <= 0) return;
        var start = pc.GlobalPosition;
        Vector3? atRead = null, afterStep = null;
        int reads = 0;
        pc.SkillKeysBeforeMove = () =>
        {
            reads++;
            if (atRead == null && pc.SkillSequencePosition >= p0) atRead = pc.GlobalPosition;
        };
        for (int i = 0; i < 40 && pc.SkillSequencePosition >= 0 && pc.SkillSequencePosition < 0.3; i++)
        {
            step.Invoke(pc, new object[] { (double)Dt });
            if (afterStep == null && atRead != null) afterStep = pc.GlobalPosition;   // the HUD frame's read after the step
            pc.Anim.Advance(Dt);
        }
        var end = pc.GlobalPosition;
        Expect(reads > 0, "the key read before the move never ran");
        Expect(atRead != null, $"no key read passed p0 ({p0})");
        if (atRead is Vector3 a) Expect(Flat(a, start) < 1, $"the p0 read saw the body {Flat(a, start):0} from the rush's start");
        if (afterStep is Vector3 b) Expect(Flat(b, start) > 10, $"after the move0 step the body moved only {Flat(b, start):0}");
        Expect(Flat(end, start) > 200, $"the rush moved {Flat(end, start):0}");
        GD.Print($"skill key order: p0 read at {(atRead is Vector3 r ? Flat(r, start) : -1):0} from the start, after its step "
            + $"{(afterStep is Vector3 s ? Flat(s, start) : -1):0}, rush end {Flat(end, start):0} ({reads} reads)");
    }

    public override void _Process(double delta)
    {
        if (++frames != 5) return;
        SetProcess(false);
        try { Run(); }
        catch (System.Exception e) { fails.Add($"{e.InnerException ?? e}"); }
        foreach (var f in fails) GD.Print($"skill key order: FAIL {f}");
        GD.Print($"skill key order: {(fails.Count == 0 ? "OK" : "FAIL")}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
