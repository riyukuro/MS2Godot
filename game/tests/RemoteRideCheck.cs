using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Movement;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/remote_ride.tscn) of another player's mount (RemoteLayer.Ride.cs) on a stone floor, with the
/// first riding row of the pack whose model loads and that has a run speed:
/// - The ridee (RemotePc.CreateRidee) falls and lands by itself; the rider in StateRide follows it and plays StateRide's
///   idle sequence (rideAniPC_Idle) once the ridee idles.
/// - A run record on the ridee moves it at the row's run speed; the rider stays on it and plays rideAniPC_Run.
/// - The dismount puts the rider at the ridee's place in StateIdle (the ridee stood on the ground) with its model back
///   at the body.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class RemoteRideCheck : Node3D
{
    readonly NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    RemotePc rider, ridee;
    int frame;
    const float Dt = 1f / 60f;
    Vector3 runStart;
    PackSet packs;
    ModelBuilder builder;

    public override void _Ready()
    {
        packs = PackSet.Shared(Settings.DataDir);
        builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
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
        rider = RemotePc.Create("Rider", visual, anim, ActionAnims.Load(packs, builder, md, anim, "female"), MoveConfig.SceneGravity, new Vector3(0, 25, 0), 0, faceGrids);
        AddChild(rider.Body);
    }

    Mount FirstMount()
    {
        foreach (var e in packs.List("Xml").Where(e => e.StartsWith("riding/", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(e), out int id)) continue;
            try
            {
                var m = Mount.Load(packs, builder, id);
                if (m.Data.RunSpeed > 0 && m.SeatBone >= 0 && m.Data.AniPCRun != m.Data.AniPCIdle) return m;
                m.Visual.Free();
            }
            catch (Exception) { }
        }
        return null;
    }

    static SyncRecord Rec(int state, int sub, RemotePc pc, System.Numerics.Vector2 move)
    {
        var p = pc.Position;
        var s = new StateSync
        {
            State = (byte)state, SubState = (byte)sub, Position = new Vector3S((short)p.X, (short)p.Y, (short)p.Z),
            Speed = new Vector3S(57, 90, 0), Unknown1 = 2, SyncNumber = 0x7fffffff,
            Animation = (byte)((move.X == 0 ? 1 : move.X > 0 ? 2 : 0) + 4 * (move.Y == 0 ? 2 : move.Y > 0 ? 3 : 1)),
        };
        return SyncRecord.From(s);
    }

    string RiderKey(string seq) => rider.Anims.Sequence(seq, true);

    public override void _PhysicsProcess(double delta)
    {
        frame++;
        if (frame < 3) return;
        if (frame == 3)
        {
            rider.Spawn();
            var m = FirstMount();
            if (m == null) { Done("no riding row with a model and a run speed"); return; }
            ridee = RemotePc.CreateRidee("Ridee", m, MoveConfig.SceneGravity, rider.Body.Position + Vector3.Up * 25f, rider.RotationZ, faceGrids);
            AddChild(ridee.Body);
            ridee.Spawn();
            rider.States.RideOn(ridee.States, 1, 1);
            GD.Print($"remote ride: riding {m.Data.Id} run speed {ridee.RunSpeed}, rider idle '{m.Data.AniPCIdle}' run '{m.Data.AniPCRun}'");
            return;
        }
        rider.Tick(Dt);
        if (ridee != null) RemoteLayer.TickRide(rider, rider.Visual, ridee, Dt);
        var d = ridee?.Mount.Data;
        switch (frame)
        {
            case 60:
                if (ridee.States.Cur != RemotePcStates.StateIdle) fails.Add($"ridee landing: state {ridee.States.Cur}");
                if (rider.States.Cur != RemotePcStates.StateRide) fails.Add($"rider state {rider.States.Cur}");
                if (rider.Body.Position != ridee.Body.Position) fails.Add($"rider at {rider.Body.Position}, ridee at {ridee.Body.Position}");
                if (RiderKey(d.AniPCIdle) is { } idle && rider.Anim.CurrentAnimation != idle) fails.Add($"rider idle sequence {rider.Anim.CurrentAnimation}, not {idle}");
                GD.Print($"remote ride: landed at {ridee.Body.Position}, ridee {ridee.Anim.CurrentAnimation}, rider {rider.Anim.CurrentAnimation} at seat {rider.Visual.GlobalPosition} (bone {ridee.Mount.Data.RideBone} #{ridee.Mount.SeatBone})");
                runStart = ridee.Body.GlobalPosition;
                ridee.States.Push(Rec(2, 0x18, ridee, new System.Numerics.Vector2(0, 1)));
                break;
            case 120:
            {
                float moved = (ridee.Body.GlobalPosition - runStart).Length();
                float want = ridee.RunSpeed * 59f / 60f;
                if (Mathf.Abs(moved - want) > want * 0.05f) fails.Add($"run moved {moved:0.0}, want {want:0.0}");
                if (ridee.States.Cur != RemotePcStates.StateWalk) fails.Add($"run state {ridee.States.Cur}");
                if (rider.Body.Position != ridee.Body.Position) fails.Add("rider left behind on the run");
                if (RiderKey(d.AniPCRun) is { } run && rider.Anim.CurrentAnimation != run) fails.Add($"rider run sequence {rider.Anim.CurrentAnimation}, not {run}");
                if (!ridee.Anim.IsPlaying()) fails.Add("ridee plays nothing on the run");
                GD.Print($"remote ride: run moved {moved:0.0} (want {want:0.0}), ridee {ridee.Anim.CurrentAnimation}, rider {rider.Anim.CurrentAnimation}");
                ridee.States.Push(Rec(1, 0, ridee, default));
                break;
            }
            case 150:
            {
                var at = ridee.Body.Position;
                RemoteLayer.Dismount(rider, rider.Visual, ridee, 1, 1);
                ridee.Free();
                ridee = null;
                if (rider.Body.Position != at) fails.Add($"dismount at {rider.Body.Position}, ridee was at {at}");
                if (rider.States.Cur != RemotePcStates.StateIdle) fails.Add($"dismount state {rider.States.Cur}");
                if (rider.Visual.Position != Vector3.Zero) fails.Add($"dismount: model at {rider.Visual.Position} off the body");
                break;
            }
            case 170:
                Done(null);
                break;
        }
    }

    void Done(string fail)
    {
        if (fail != null) fails.Add(fail);
        bool ok = fails.Count == 0;
        GD.Print($"remote ride: {(ok ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
