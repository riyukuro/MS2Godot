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
/// - The own pc as a hold target (RemoteLayer.FindHoldTarget with <see cref="RemoteLayer.SelfPc"/>), on the first row with
///   a passenger seat 0: unknown before it is in; on foot a pc without a ridee; riding, its ridee's feet z, the seat's
///   rideBone as the node, the asker's seat 0 and the own state ids.
/// - A ridee's object id is no hold target (the client finds the ridee, but its own node map is empty, so the hold
///   falls back): another player's mount ridden through OnRide mode 0 gives HoldKind.None by the action's object id.
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

    Mount FirstMount() => FirstMount(m => m.Data.RunSpeed > 0 && m.SeatBone >= 0 && m.Data.AniPCRun != m.Data.AniPCIdle);

    Mount FirstMount(Func<Mount, bool> pick)
    {
        foreach (var e in packs.List("Xml").Where(e => e.StartsWith("riding/", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(e), out int id)) continue;
            try
            {
                var m = Mount.Load(packs, builder, id);
                if (pick(m)) return m;
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
            SelfHoldTarget();
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
                var at0 = ridee.Body.GlobalPosition;
                if (ridee.States.Cur != RemotePcStates.StateIdle) fails.Add($"ridee landing: state {ridee.States.Cur}");
                if (rider.States.Cur != RemotePcStates.StateRide) fails.Add($"rider state {rider.States.Cur}");
                if (rider.Body.Position != ridee.Body.Position) fails.Add($"rider at {rider.Body.Position}, ridee at {ridee.Body.Position}");
                if (RiderKey(d.AniPCIdle) is { } idle && rider.Anim.CurrentAnimation != idle) fails.Add($"rider idle sequence {rider.Anim.CurrentAnimation}, not {idle}");
                GD.Print($"remote ride: landed at {ridee.Body.Position}, ridee {ridee.Anim.CurrentAnimation}, rider {rider.Anim.CurrentAnimation} at seat {rider.Visual.GlobalPosition} (bone {ridee.Mount.Data.RideBone} #{ridee.Mount.SeatBone})");
                // the hold poll asks each riding pc's seat nodes every frame: 0 B once the mount's list is found
                RemoteLayer.NearestHoldNode(null, ridee.Mount, at0, out _);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++) RemoteLayer.NearestHoldNode(null, ridee.Mount, at0, out _);
                long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
                if (alloc != 0) fails.Add($"hold poll of the mount's seats allocates {alloc} B per 100");
                GD.Print($"remote ride: hold poll of {ridee.Mount.RideBones().Length} seat node(s) {alloc} B per 100");
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

    void SelfHoldTarget()
    {
        const int Self = 9, Asker = 77;
        // a row with a passenger seat 0 whose rideBone the skeleton has, so the seat comes with the node
        var m = FirstMount(x => x.Data.Passenger(0) is { } p && x.Bone(p.RideBone) >= 0);
        if (m == null) { fails.Add("self hold: no riding row with a passenger seat"); return; }
        var field = new FieldStub(packs);
        AddChild(field);
        var layer = new RemoteLayer(field, builder, 1);
        var (visual, anim, md, bodyDir) = Ms2Godot.Look.PlayerLook.Build(builder, packs);
        var pc = Player.Create(visual, anim, faceGrids);
        pc.Anims = ActionAnims.Load(packs, builder, md, anim, bodyDir);
        AddChild(pc);
        pc.SetProcess(false);
        pc.SetPhysicsProcess(false);
        pc.GlobalPosition = new Vector3(300, 40, -200);
        string bone = m.Data.Passenger(0).RideBone;
        if (layer.FindHoldTarget(Asker, Self, bone).Kind != HoldKind.None) fails.Add("self hold: found before the own pc is in");
        layer.SelfPc = id => id == Self ? pc : null;
        layer.SelfPassengers = new Dictionary<int, int>();
        var foot = layer.FindHoldTarget(Asker, Self, bone);
        if (foot.Kind != HoldKind.Pc || foot.HasRidee || foot.HasSeat) fails.Add($"self hold on foot: kind {foot.Kind}, ridee {foot.HasRidee}, seat {foot.HasSeat}");
        if (layer.FindHoldTarget(Asker, Self + 1, bone).Kind != HoldKind.None) fails.Add("self hold: another id finds the own pc");
        pc.RideOn(m);
        var t = layer.FindHoldTarget(Asker, Self, bone);
        var w = m.BoneWorld(m.Bone(bone)).Origin;
        var want = new System.Numerics.Vector3(w.X, -w.Z, w.Y);
        if (t.Kind != HoldKind.Pc || !t.HasRidee || t.RideeZ != pc.GlobalPosition.Y) fails.Add($"self hold riding: kind {t.Kind}, ridee {t.HasRidee}, z {t.RideeZ} (feet {pc.GlobalPosition.Y})");
        if (!t.HasNode || System.Numerics.Vector3.Distance(t.NodePosition, want) > 0.01f) fails.Add($"self hold node: {t.HasNode} at {t.NodePosition}, mount bone {want}");
        if (!t.HasSeat || t.Seat != m.Data.Passenger(0).Sequences) fails.Add($"self hold: seat {t.HasSeat}, not the row's seat 0");
        if (t.RideeState != RemotePcStates.StateIdle || t.RideeAnimSpeed != 1f) fails.Add($"self hold ridee state {t.RideeState} speed {t.RideeAnimSpeed}");
        pc.DeadFlag = true;
        if (!layer.FindHoldTarget(Asker, Self, bone).Dead) fails.Add("self hold: a dead own pc is not a dead target");
        pc.DeadFlag = false;
        if (layer.FindHoldTarget(Asker, Self, bone).Dead) fails.Add("self hold: a live own pc is a dead target");
        // other players: IsDead of the remote pc (a 0-health add is dead)
        var live = new FieldAddUser { ObjectId = 31, User = new UserBlock { Name = "a", CharacterId = 131, Gender = 1 } };
        live.Stats[0].Health = 100; live.Stats[2].Health = 100;
        var dead = new FieldAddUser { ObjectId = 32, User = new UserBlock { Name = "b", CharacterId = 132, Gender = 1 } };
        dead.Stats[0].Health = 100; dead.Stats[2].Health = 0;
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, live, false));
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, dead, false));
        layer._Process(0);
        var tl = layer.FindHoldTarget(Asker, 31, bone);
        var td = layer.FindHoldTarget(Asker, 32, bone);
        if (tl.Kind != HoldKind.Pc || tl.Dead || td.Kind != HoldKind.Pc || !td.Dead) fails.Add($"remote pc targets: live {tl.Kind} dead {tl.Dead}, dead {td.Kind} dead {td.Dead}");
        GD.Print($"remote ride: own pc as hold target on {m.Data.Id}: node '{bone}' at {t.NodePosition}, ridee z {t.RideeZ}, seat {t.HasSeat}");
        // the hold poll over a mount with seat nodes: 0 B once its list is found
        RemoteLayer.NearestHoldNode(null, m, Vector3.Zero, out _);
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) RemoteLayer.NearestHoldNode(null, m, Vector3.Zero, out _);
        long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        if (m.RideBones().Length == 0 || alloc != 0) fails.Add($"hold poll of {m.RideBones().Length} seat node(s) allocates {alloc} B per 100");
        RideeIdNoTarget(layer, m.Data.Id, bone);
        pc.QueueFree();
        field.QueueFree();
        layer.Free();
    }

    /// <summary>Another player's ridee by its object id: no node in the ridee's own m_unk_b88, so no target (the fallback
    /// jump); the rider stays a pc target with its ridee.</summary>
    void RideeIdNoTarget(RemoteLayer layer, int rideId, string bone)
    {
        const int User = 33, Ridee = 5033, Asker = 77;
        var u = new FieldAddUser { ObjectId = User, User = new UserBlock { Name = "r", CharacterId = 133, Gender = 1 } };
        u.Stats[0].Health = 100; u.Stats[2].Health = 100;
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, u, false));
        layer._Process(0);
        layer.OnRide(new ResponseRide(0, User, new RideOnAction(0, rideId, Ridee, false, 0, 0, 0, 0), null, 0, 0, 0, 0, false));
        var t = layer.FindHoldTarget(Asker, Ridee, bone);
        var r = layer.FindHoldTarget(Asker, User, bone);
        if (t.Kind != HoldKind.None || t.HasNode) fails.Add($"ridee id {Ridee} as hold target: kind {t.Kind}, node {t.HasNode} (the client falls back)");
        if (r.Kind != HoldKind.Pc || !r.HasRidee) fails.Add($"rider {User}: kind {r.Kind}, ridee {r.HasRidee}");
        GD.Print($"remote ride: ridee id {Ridee} as hold target {t.Kind} (fallback), rider {r.Kind} with ridee {r.HasRidee}");
    }

    void Done(string fail)
    {
        if (fail != null) fails.Add(fail);
        bool ok = fails.Count == 0;
        GD.Print($"remote ride: {(ok ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
