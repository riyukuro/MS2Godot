using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Replay;
using Ms2.Ui.Skills;
using Ms2Godot.Movement;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/remote_hit.tscn) of another player's StateHit (RemotePcHit.cs) on a stone floor:
/// - A lifting hit (StateHit::vfunc_4) puts the landed PC in StateHit off the ground, playing Jump_FlyDamg_A; the lift
///   (FUN_1412407f0's lift branch) raises it, and an unforced hit while in StateHit keeps the first one's data.
/// - The push (CPushControl::vfunc_1 after the state's move) moves it the push's distance along its direction and turns it
///   against the move (PCBase::vfunc_106), then ends.
/// - Past the hit's end time it is back in StateIdle on the ground.
/// - A hit with no sequence on the body goes to StateIdle on the next frame (StateHit::vfunc_9).
/// - A dead PC takes no hit (PC::vfunc_124).
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class RemoteHitCheck : Node3D
{
    readonly NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    RemotePc pc;
    int frame;
    const float Dt = 1f / 60f;
    Vector3 start;
    float maxRise;

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        builder.TextureArchives = builder.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
        var body = new StaticBody3D { CollisionLayer = 1, Position = new Vector3(0, -50, 0) };
        body.SetMeta(FieldBuilder.GroupMeta, 0);
        var cs = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4000, 100, 4000) } };
        FieldBuilder.TagGround(cs, "stone");
        body.AddChild(cs);
        AddChild(body);
        var md = builder.LoadModel("female/f_body.nif");
        var visual = builder.Build(md, "Female");
        var anim = visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
        pc = RemotePc.Create("Pc", visual, anim, ActionAnims.Load(packs, builder, md, anim, "female"), MoveConfig.SceneGravity, new Vector3(0, 25, 0), 0, faceGrids);
        AddChild(pc.Body);
    }

    public override void _PhysicsProcess(double delta)
    {
        frame++;
        if (frame < 3) return;
        if (frame == 3) { pc.Spawn(); return; }
        pc.Tick(Dt);
        if (frame > 60) maxRise = Mathf.Max(maxRise, pc.Body.Position.Y - start.Y);
        switch (frame)
        {
            case 60:
                if (pc.States.Cur != RemotePcStates.StateIdle || !pc.OnGround) { Done($"not landed: state {pc.States.Cur}"); return; }
                start = pc.Body.Position;
                pc.EnterHit(new StateHitData { SpeedRate = 1f, Duration = 1f, UpDistance = 100f }, true);
                if (pc.States.Cur != RemotePcStates.StateHit) fails.Add($"lifting hit: state {pc.States.Cur}");
                if (!pc.Lifted || pc.OnGround) fails.Add($"lifting hit: lifted {pc.Lifted}, on ground {pc.OnGround}");
                if (pc.HitAnim != pc.Anims.Sequence(StateHitTimeline.FlySequence, false) || pc.HitAnim.Length == 0)
                    fails.Add($"lifting hit plays '{pc.HitAnim}', not {StateHitTimeline.FlySequence}");
                pc.EnterHit(new StateHitData { SpeedRate = 1f, Duration = 0.1f, Priority = 7 }, false);
                if (pc.HitPushPriority != 0 || pc.HitState.Duration != 1f) fails.Add("an unforced hit in StateHit replaced the first one");
                pc.Push.Dir = new System.Numerics.Vector3(1, 0, 0);
                pc.Push.Speed = 1f;
                pc.Push.Duration = 0.3f;
                pc.Push.SetCurve(0);
                break;
            case 90:
            {
                float moved = pc.Body.Position.X - start.X;
                if (Mathf.Abs(moved - 100f) > 5f) fails.Add($"push moved {moved:0.0}, not 100");
                if (pc.Pushed) fails.Add("the push still runs after its duration");
                if (Mathf.Abs(pc.RotationZ + 90f) > 0.5f) fails.Add($"pushed facing {pc.RotationZ:0.0}, not against the move (-90)");
                if (maxRise < 50f) fails.Add($"the lift rose {maxRise:0.0}");
                GD.Print($"remote hit: push moved {moved:0.0}, facing {pc.RotationZ:0.0}, risen {maxRise:0.0}, state {pc.States.Cur}");
                break;
            }
            case 150:
                if (pc.States.Cur != RemotePcStates.StateIdle) fails.Add($"after the hit's end: state {pc.States.Cur}");
                if (!pc.OnGround || Mathf.Abs(pc.Body.Position.Y - start.Y) > 2f) fails.Add($"after the hit's end: y {pc.Body.Position.Y:0.0}, on ground {pc.OnGround}");
                pc.EnterHit(new StateHitData { SpeedRate = 1f, Duration = 0.5f, Animation = "NoSuchSequence" }, true);
                if (pc.States.Cur != RemotePcStates.StateHit || pc.HitAnim.Length != 0) fails.Add($"hit without a sequence: state {pc.States.Cur}, anim '{pc.HitAnim}'");
                break;
            case 151:
                if (pc.States.Cur != RemotePcStates.StateIdle) fails.Add($"hit without a sequence: state {pc.States.Cur} a frame later");
                pc.States.SetDead(true);
                pc.EnterHit(new StateHitData { SpeedRate = 1f, Duration = 0.5f }, true);
                if (pc.States.Cur == RemotePcStates.StateHit) fails.Add("a dead PC took a hit");
                pc.States.SetDead(false);
                Done(null);
                break;
        }
    }

    void Done(string fail)
    {
        if (fail != null) fails.Add(fail);
        bool ok = fails.Count == 0;
        GD.Print($"remote hit: {(ok ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
