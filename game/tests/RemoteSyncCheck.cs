using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Movement;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/remote_sync.tscn) of the remote PC body (Net.RemotePc) on real physics and the female body's
/// action sequences: a stone floor and a water pool (entity shapes tagged as the field build tags them).
/// - Spawned above the stone, a remote PC falls (the pending StateFall, pcbase.cpp:792) and lands with one "land" on stone.
/// - A walk record moves it along the sender's camera at the run speed with the run sequence; its "run*" text keys, if the
///   sequence has any, play steps on stone (PCBase::TextKeyEvent).
/// - A jump record jumps it; it lands by itself with one more "land" on stone.
/// - Spawned above the water, a remote PC lands with "land" on water and goes to the water idle (StateLand::vfunc_4).
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class RemoteSyncCheck : Node3D
{
    readonly List<string> fails = new();
    RemotePc stone, wet;
    readonly List<(string a, string g)> stoneFx = new(), wetFx = new();
    int frame;
    const float Dt = 1f / 60f;
    Vector3 walkStart;
    int jumpStates;

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        builder.TextureArchives = builder.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
        Floor(new Vector3(0, -50, 0), new Vector3(2000, 100, 2000), 0, "stone");
        Floor(new Vector3(3000, -50, 0), new Vector3(2000, 100, 2000), MoveConfig.WaterGroup, "water");
        var md = builder.LoadModel("female/f_body.nif");
        RemotePc Make(string name, Vector3 at, List<(string, string)> fx)
        {
            var visual = builder.Build(md, "Female");
            var anim = visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
            var anims = ActionAnims.Load(packs, builder, md, anim, "female");
            var pc = RemotePc.Create(name, visual, anim, anims, MoveConfig.SceneGravity, at, 0);
            pc.Effects = (a, g, p, y) => fx.Add((a, g));
            AddChild(pc.Body);
            return pc;
        }
        stone = Make("Stone", new Vector3(0, 25, 0), stoneFx);
        wet = Make("Wet", new Vector3(3000, 25, 0), wetFx);
        var run = stone.Anims.Name(ActionAnims.Act.Run);
        var keys = run != null && stone.Anims.Kf.TryGetValue(run, out var kf) ? string.Join(",", kf.TextKeys.Select(k => $"{k.time:0.###}:{k.text}")) : "-";
        GD.Print($"remote sync: run sequence '{run}' text keys [{keys}]");
    }

    void Floor(Vector3 center, Vector3 size, int group, string material)
    {
        var body = new StaticBody3D { CollisionLayer = 1u << group, Position = center };
        body.SetMeta(FieldBuilder.GroupMeta, group);
        var cs = new CollisionShape3D { Shape = new BoxShape3D { Size = size } };
        if (group != 0) cs.SetMeta(FieldBuilder.KindMeta, group);
        FieldBuilder.TagGround(cs, material);
        body.AddChild(cs);
        AddChild(body);
    }

    static SyncRecord Rec(int state, int sub, RemotePc pc, System.Numerics.Vector2 move)
    {
        var p = pc.Position;
        var s = new StateSync
        {
            State = (byte)state, SubState = (byte)sub, Position = new Vector3S((short)p.X, (short)p.Y, (short)p.Z),
            Speed = new Vector3S(57, 90, 0), Unknown1 = 2, SyncNumber = 0x7fffffff,   // the (0, 1) camera view
            Animation = (byte)((move.X == 0 ? 1 : move.X > 0 ? 2 : 0) + 4 * (move.Y == 0 ? 2 : move.Y > 0 ? 3 : 1)),
        };
        return SyncRecord.From(s);
    }

    public override void _PhysicsProcess(double delta)
    {
        frame++;
        if (frame == 3) { stone.Spawn(); wet.Spawn(); }
        if (frame < 3) return;
        stone.Tick(Dt); wet.Tick(Dt);
        switch (frame)
        {
            case 60:
                if (!stoneFx.SequenceEqual(new[] { ("land", "stone") })) fails.Add($"stone spawn landing {Fx(stoneFx)}");
                if (!wetFx.SequenceEqual(new[] { ("land", "water") })) fails.Add($"water spawn landing {Fx(wetFx)}");
                if (wet.States.Cur != RemotePcStates.StateIdle || wet.States.Sub != 7) fails.Add($"water: state {wet.States.Cur} sub {wet.States.Sub}");
                if (wet.Anim.CurrentAnimation != wet.Anims.Name(ActionAnims.Act.WaterIdle)) fails.Add($"water idle sequence {wet.Anim.CurrentAnimation}");
                walkStart = stone.Body.GlobalPosition;
                stoneFx.Clear();
                stone.States.Push(Rec(2, 0x18, stone, new System.Numerics.Vector2(0, 1)));
                break;
            case 120:
            {
                float moved = (stone.Body.GlobalPosition - walkStart).Length();
                if (Mathf.Abs(moved - 350f * 59f / 60f) > 15f) fails.Add($"walk moved {moved:0.0}");
                if (stone.States.Cur != RemotePcStates.StateWalk) fails.Add($"walk state {stone.States.Cur}");
                if (stone.Anim.CurrentAnimation != stone.Anims.Name(ActionAnims.Act.Run)) fails.Add($"run sequence {stone.Anim.CurrentAnimation}");
                if (stoneFx.Any(f => f.g != "stone")) fails.Add($"steps {Fx(stoneFx)}");
                GD.Print($"remote sync: walk moved {moved:0.0} in 1 s, {stoneFx.Count} step effects");
                stoneFx.Clear();
                stone.States.StateChanged = s => { if (s == RemotePcStates.StateJump || s == RemotePcStates.StateLand) jumpStates = jumpStates * 10 + s; };
                stone.States.Push(Rec(6, 0x21, stone, default));
                break;
            }
            case 180:
            {
                if (jumpStates != 64) fails.Add($"jump states {jumpStates}");
                if (!stoneFx.SequenceEqual(new[] { ("land", "stone") })) fails.Add($"jump landing {Fx(stoneFx)}");
                bool ok = fails.Count == 0;
                GD.Print($"remote sync: {(ok ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
                GetTree().Quit(ok ? 0 : 1);
                break;
            }
        }
    }

    static string Fx(List<(string a, string g)> l) => "[" + string.Join(",", l.Select(f => f.a + "/" + f.g)) + "]";
}
