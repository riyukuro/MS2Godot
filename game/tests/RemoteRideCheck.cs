using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.Options;
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
/// - The move-speed rates (vfunc_96): the PC's of its movement speed, the ridee's of the mount speed copied at the ride
///   on, a stat packet's change and the hold to the total.
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
            for (int kind = 0; kind < 3; kind += 2) { ridee.SetStat(kind, MoveSpeedRate.MovementSpeedStat, 100); ridee.SetStat(kind, MoveSpeedRate.MountSpeedStat, 100); }   // FieldAddUser's speeds: rate 1
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
        RideEffects(layer, m.Data.Id);
        NpcBeforePlayer(layer, pc);
        HideRider(layer, pc);
        RideeStats(layer, m.Data.Id);
        RideeSpeeds(layer, m.Data.Id);
        RideShadow(layer, pc);
        OwnUnRideSkillEffect(pc, m);
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

    /// <summary>PCBase::vfunc_120 / vfunc_121's effects at another player: the ride-on effect, the ride-off effect, and
    /// after an UnRideSkill (type 24) ride off the row's rideOffBySkillEffect too; a plain ride off plays only the
    /// ride-off effect.</summary>
    void RideEffects(RemoteLayer layer, int rideId)
    {
        const int User = 34;
        var tables = Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage);
        string on = tables.CommonResource(LiveSession.RideOnEffectKey), off = tables.CommonResource(LiveSession.RideOffEffectKey);
        if (string.IsNullOrEmpty(on) || string.IsNullOrEmpty(off)) { fails.Add($"ride effects: common resource '{on}' / '{off}'"); return; }
        var u = new FieldAddUser { ObjectId = User, User = new UserBlock { Name = "e", CharacterId = 134, Gender = 1 } };
        u.Stats[0].Health = 100; u.Stats[2].Health = 100;
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, u, false));
        layer._Process(0);
        var played = new List<string>();
        layer.RideEffectsPlayed = (id, path) => played.Add($"{id}:{path}");
        ResponseRide Mount() => new ResponseRide(0, User, new RideOnAction(0, rideId, 5034, false, 0, 0, 0, 0), null, 0, 0, 0, 0, false);
        layer.OnRide(Mount());
        if (played.Count != 1 || played[0] != $"{User}:{on}") fails.Add($"ride on effects: {string.Join(", ", played)}");
        played.Clear();
        layer.OnRide(new ResponseRide(1, User, null, new RideOffAction(0, 0), 0, 0, 0, 0, false));
        if (played.Count != 1 || played[0] != $"{User}:{off}") fails.Add($"plain ride off effects: {string.Join(", ", played)}");
        layer.OnRide(Mount());
        var row = layer.RideeRow(User);
        if (row == null) { fails.Add("ride effects: no ridee row after the second ride on"); return; }
        row.RideOffBySkillEffect = on;    // a stand-in path: the rows' own value is data
        played.Clear();
        layer.OnRide(new ResponseRide(1, User, null, new RideOffAction(RemoteLayer.UnRideSkillType, 0), 0, 0, 0, 0, false));
        if (played.Count != 2 || played[0] != $"{User}:{off}" || played[1] != $"{User}:{on}") fails.Add($"UnRideSkill ride off effects: {string.Join(", ", played)}");
        layer.RideEffectsPlayed = null;
        GD.Print($"remote ride: ride effects on '{on}', off '{off}', UnRideSkill adds the row's effect ({played.Count} played)");
    }

    /// <summary>Ridee::GetRunSpeed and FUN_1417fb370 at another player's mount: with no worn item, buff or hot time the
    /// rate of 0xae is 0 and the run speed is the row's; a rate of 0.5 runs it at 1.5 times; a water-running mount
    /// (type 1 or 2, or the rider's event buff type 3) swims at the row's run speed with no rate, any other at its swim
    /// speed.</summary>
    void RideeSpeeds(RemoteLayer layer, int rideId)
    {
        const int User = 39;
        var u = new FieldAddUser { ObjectId = User, User = new UserBlock { Name = "v", CharacterId = 139, Gender = 1, Level = 30 } };
        u.Stats[0].Health = 100; u.Stats[2].Health = 100;
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, u, false));
        layer._Process(0);
        layer.OnRide(new ResponseRide(0, User, new RideOnAction(0, rideId, 5039, false, 0, 0, 0, 0), null, 0, 0, 0, 0, false));
        var ridee = layer.RideeOf(User);
        if (ridee == null) { fails.Add("ridee speeds: no ridee"); return; }
        var d = ridee.Mount.Data;
        float rate0 = ridee.RiderAbilityRate?.Invoke(Player.RunXSameSpeedAbility) ?? -1f, plain = ridee.RunSpeed;
        bool water = ridee.WaterRunner;
        var keep = ridee.RiderAbilityRate; var keepBuff = ridee.RiderEventBuff;
        ridee.RiderAbilityRate = t => t == Player.RunXSameSpeedAbility ? 0.5f : 0f;
        float rated = ridee.RunSpeed;
        ridee.RiderEventBuff = t => t == 3;
        bool buffWater = ridee.WaterRunner;
        ridee.States.ChangeState(RemotePcStates.StateSwim);    // sub 0x84 at the row's raw run speed (stateswim.cpp:147-153)
        ridee.States.Update(1 / 60f);
        float swim = ridee.States.Speed;
        ridee.States.ChangeState(RemotePcStates.StateIdle);
        ridee.RiderAbilityRate = keep; ridee.RiderEventBuff = keepBuff;
        layer.OnRide(new ResponseRide(1, User, null, new RideOffAction(0, 0), 0, 0, 0, 0, false));
        if (rate0 != 0f || Mathf.Abs(plain - d.RunSpeed * 100f) > 0.01f || Mathf.Abs(rated - d.RunSpeed * 150f) > 0.01f
            || water != (d.Type is 1 or 2) || !buffWater || Mathf.Abs(swim - d.RunSpeed * 100f) > 0.01f)
            fails.Add($"ridee speeds: rate {rate0}, run {plain} of {d.RunSpeed}, at 0.5 {rated}, water runner {water} (type {d.Type}), with event buff 3 {buffWater}, swims at {swim}");
        GD.Print($"remote ride: ridee {d.Id} rate {rate0}, run {plain}, at rate 0.5 {rated}, water runner {water} (type {d.Type}), with event buff 3 {buffWater} swims at {swim} under rate 0.5 (row run {d.RunSpeed * 100f})");
    }

    /// <summary>PCBase::vfunc_96 with the stats: the PC's rate is calc_msiR of its movement speed, the ridee's calc_rmsiR of
    /// the mount speed it took from the rider at the ride on (pcbase.cpp:3317-3320); a later stat packet reaches the ridee
    /// too, its current held to the total.</summary>
    void RideeStats(RemoteLayer layer, int rideId)
    {
        const int User = 36;
        var u = new FieldAddUser { ObjectId = User, User = new UserBlock { Name = "s", CharacterId = 136, Gender = 1 } };
        for (int k = 0; k < 3; k++) u.Stats[k] = (100, 100, 120, 150, 100);
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, u, false));
        layer._Process(0);
        float? pcRate = layer.MoveSpeedRateOf(User, false);
        layer.OnRide(new ResponseRide(0, User, new RideOnAction(0, rideId, 5036, false, 0, 0, 0, 0), null, 0, 0, 0, 0, false));
        float? onRate = layer.MoveSpeedRateOf(User, true);
        void Stat(long total, long current)
        {
            var v = new Dictionary<byte, long[]> { [PcStat.MountSpeed] = new[] { total, total, current } };
            layer.Tracker.Events.Enqueue(new UserStatEvent(0, new PcStat(User, v)));
            layer._Process(0);
        }
        Stat(150, 130);
        float? changed = layer.MoveSpeedRateOf(User, true);
        Stat(150, 180);
        float? held = layer.MoveSpeedRateOf(User, true);
        layer.OnRide(new ResponseRide(1, User, null, new RideOffAction(0, 0), 0, 0, 0, 0, false));
        if (pcRate != 1.2f || onRate != 1.5f || changed != 1.3f || held != 1.5f)
            fails.Add($"move-speed rates: pc {pcRate}, ridee {onRate}, after mount speed 130 {changed}, 180 of 150 {held}");
        GD.Print($"remote ride: move-speed rate pc {pcRate}, ridee {onRate}, mount speed 130 -> {changed}, 180 of 150 -> {held}");
    }

    /// <summary>StateRide::vfunc_4 / vfunc_5 (statemanager.cpp:1095, :1103): the rider's simple shadow is off while it rides
    /// and back on after; a dead rider refuses StateRide and keeps it. Ridee::vfunc_49 (ridee.cpp:848-858) gives the mount its
    /// own, the row's &lt;shadow scale&gt; wide (150 without one). Both for another player and the own pc.</summary>
    void RideShadow(RemoteLayer layer, Player pc)
    {
        BlobShadow.Load(packs);
        // the lowest graphics shadow level that shows both simple shadows (the option's level is put back at the end)
        var graphic = Ms2Godot.GameOptions.Current.GraphicOption;
        int optionLevel = graphic.ShadowLevel;
        bool Simple() => FieldShadows.SimpleShadow(GraphicOptionTable.PC) && FieldShadows.SimpleShadow(GraphicOptionTable.MyPC);
        for (int level = 0; level <= 4; level++)
        {
            graphic.ShadowLevel = level;
            FieldShadows.Begin(packs, 0);
            if (Simple()) break;
        }
        try { RideShadow(layer, pc, Simple()); }
        finally { graphic.ShadowLevel = optionLevel; FieldShadows.Begin(packs, 0); }
    }

    void RideShadow(RemoteLayer layer, Player pc, bool simple)
    {
        const int User = 37, DeadUser = 38;
        if (!simple) { fails.Add("ride shadow: no graphics shadow level shows the PC and myPC simple shadows"); return; }
        var m = FirstMount(x => x.Data.ShadowScale != 150) ?? FirstMount(x => true);
        if (m == null) { fails.Add("ride shadow: no riding row"); return; }
        int rideId = m.Data.Id;
        float scale = m.Data.ShadowScale;
        m.Visual.Free();
        BlobShadow Shadow(Node n) => n?.GetNodeOrNull<BlobShadow>(FieldShadows.ShadowNode);
        void Add(int id, long hp)
        {
            var u = new FieldAddUser { ObjectId = id, User = new UserBlock { Name = "w", CharacterId = 100 + id, Gender = 1 } };
            u.Stats[0].Health = 100; u.Stats[2].Health = hp;
            layer.Tracker.Events.Enqueue(new UserAddedEvent(0, u, false));
            layer._Process(0);
        }
        void Ride(int id) => layer.OnRide(new ResponseRide(0, id, new RideOnAction(0, rideId, 5000 + id, false, 0, 0, 0, 0), null, 0, 0, 0, 0, false));
        void Off(int id) => layer.OnRide(new ResponseRide(1, id, null, new RideOffAction(0, 0), 0, 0, 0, 0, false));
        Add(User, 100);
        var rider = Shadow(layer.HoldVisual(User)?.GetParent());
        bool before = rider is { Off: false };
        Ride(User);
        bool riding = rider is { Off: true };
        var mount = Shadow(layer.GetNodeOrNull($"Ridee{User}"));
        bool mountOk = mount is { Off: false } && mount.Radius == scale;
        Off(User);
        bool after = rider is { Off: false };
        if (!before || !riding || !after || !mountOk)
            fails.Add($"remote ride shadow {rideId}: rider shown before {before}, off riding {riding}, shown after {after}; mount's {mount?.Radius} of {scale}");
        Add(DeadUser, 0);
        var dead = Shadow(layer.HoldVisual(DeadUser)?.GetParent());
        Ride(DeadUser);
        if (dead is not { Off: false }) fails.Add($"ride shadow: a dead rider's shadow went off ({dead?.Off})");
        Off(DeadUser);
        var own = Shadow(pc) ?? BlobShadow.Add(pc, 100f, 3f);
        var om = Mount.Load(packs, builder, rideId);
        pc.RideOn(om);
        bool ownRiding = own is { Off: true };
        var ownMount = Shadow(om.Visual);
        bool ownMountOk = ownMount is { Off: false } && ownMount.Radius == scale;
        pc.RideOff();
        bool ownAfter = own is { Off: false };
        if (!ownRiding || !ownAfter || !ownMountOk)
            fails.Add($"own ride shadow {rideId}: off riding {ownRiding}, shown after {ownAfter}; mount's {ownMount?.Radius} of {scale}");
        GD.Print($"remote ride: ride shadow {rideId}: rider's off riding, mount's {mount?.Radius} wide (row {scale}); a dead rider keeps its own");
    }

    /// <summary>hideRider: PCBase::vfunc_120 hides the rider of such a row (AddHideReason(2), the body's vfunc_23(1)) and
    /// vfunc_121 shows it again; a row without it leaves the rider shown. Both for another player and the own pc.</summary>
    void HideRider(RemoteLayer layer, Player pc)
    {
        const int User = 35;
        var hide = FirstMount(x => x.Data.HideRider);
        var plain = FirstMount(x => !x.Data.HideRider);
        if (hide == null || plain == null) { fails.Add($"hideRider: rows hide {hide?.Data.Id} plain {plain?.Data.Id}"); return; }
        var u = new FieldAddUser { ObjectId = User, User = new UserBlock { Name = "h", CharacterId = 135, Gender = 1 } };
        u.Stats[0].Health = 100; u.Stats[2].Health = 100;
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, u, false));
        layer._Process(0);
        var body = layer.HoldVisual(User);
        bool Ride(int id)
        {
            layer.OnRide(new ResponseRide(0, User, new RideOnAction(0, id, 5035, false, 0, 0, 0, 0), null, 0, 0, 0, 0, false));
            return body.Visible;
        }
        bool Off()
        {
            layer.OnRide(new ResponseRide(1, User, null, new RideOffAction(0, 0), 0, 0, 0, 0, false));
            return body.Visible;
        }
        bool rOn = Ride(hide.Data.Id), rOff = Off(), pOn = Ride(plain.Data.Id), pOff = Off();
        if (rOn || !rOff || !pOn || !pOff) fails.Add($"remote hideRider {hide.Data.Id}: shown riding {rOn}, after {rOff}; plain {plain.Data.Id} riding {pOn}, after {pOff}");
        pc.RideOn(hide);
        bool oOn = pc.Visual.Visible, mountShown = hide.Visual.Visible;
        pc.RideOn(plain);                     // RideOn rides the old mount off first
        bool oSwap = pc.Visual.Visible;
        pc.RideOff();
        bool oOff = pc.Visual.Visible;
        if (oOn || !mountShown || !oSwap || !oOff) fails.Add($"own hideRider: shown riding {oOn} (mount {mountShown}), plain after swap {oSwap}, after {oOff}");
        GD.Print($"remote ride: hideRider {hide.Data.Id} hides the rider ({!rOn}, own {!oOn}), shown after ride off ({rOff}, own {oOff}); plain {plain.Data.Id} shown");
    }

    /// <summary>The own pc's ride off through LiveSession's ResponseRide (mode 1, the own object id): PCBase::vfunc_121 plays
    /// the ride-off effect, and after an UnRideSkill (type 24) the row's rideOffBySkillEffect after it; a ride off with no
    /// mount plays nothing.</summary>
    void OwnUnRideSkillEffect(Player pc, Mount m)
    {
        const int Self = 9;
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var tables = Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage);
        string on = tables.CommonResource(LiveSession.RideOnEffectKey), off = tables.CommonResource(LiveSession.RideOffEffectKey);
        var ls = new LiveSession();
        typeof(LiveSession).GetField("player", Private).SetValue(ls, pc);
        typeof(LiveSession).GetField("selfObject", Private).SetValue(ls, Self);
        var respond = typeof(LiveSession).GetMethod("OnResponseRide", Private).CreateDelegate<Action<ResponseRide>>(ls);
        var played = new List<string>();
        ls.OwnEffectsPlayed = played.Add;
        if (pc.Mount != null) pc.RideOff();
        pc.RideOn(m);
        string had = m.Data.RideOffBySkillEffect;
        m.Data.RideOffBySkillEffect = on;     // a stand-in path: the rows' own value is data
        respond(new ResponseRide(1, Self, null, new RideOffAction(RemoteLayer.UnRideSkillType, 0), 0, 0, 0, 0, false));
        m.Data.RideOffBySkillEffect = had;
        var skill = string.Join(", ", played);
        played.Clear();
        respond(new ResponseRide(1, Self, null, new RideOffAction(RemoteLayer.UnRideSkillType, 0), 0, 0, 0, 0, false));
        if (skill != $"{off}, {on}" || pc.Mount != null) fails.Add($"own UnRideSkill ride off effects: {skill} (mount {pc.Mount?.Data.Id})");
        if (played.Count != 0) fails.Add($"own ride off with no mount played {string.Join(", ", played)}");
        ls.Free();
        GD.Print($"remote ride: own UnRideSkill ride off plays '{skill}'; with no mount {played.Count}");
    }

    /// <summary>The hold key end to end: with the npc away the player is the pick and the poll keeps it; an npc's
    /// Hold_PointNN nearer than the player's makes FindNearestActor return the npc and HoldPCAction::vfunc_1 refuse
    /// (FindPCById fails, m_n18 cleared); at equal distances the lower object id wins.</summary>
    void NpcBeforePlayer(RemoteLayer layer, Player pc)
    {
        const int User = 31;
        int npcId = 0;
        var table = new Ms2.Formats.World.NpcTable(packs);
        foreach (int id in new[] { 21000001, 21000002, 21000003, 11000001 })
            if (table.Find(id) != null) { npcId = id; break; }
        if (npcId == 0) { fails.Add("npc hold: no npc row"); return; }
        foreach (int obj in new[] { 20, 40 })
            layer.Tracker.Events.Enqueue(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = obj, NpcId = npcId }));
        layer._Process(0);
        var from = pc.GlobalPosition;
        Vector3 Place(int id, Vector3 at)
        {
            var sk = layer.HoldVisual(id)?.GetNodeOrNull<Skeleton3D>("Model/" + ModelBuilder.SkeletonName);
            if (sk == null) return Vector3.Inf;
            int b = sk.FindBone("Hold_Point00");
            if (b < 0) b = sk.AddBone("Hold_Point00");
            int parent = sk.GetBoneParent(b);
            var local = sk.GlobalTransform.AffineInverse() * at;
            sk.SetBonePosePosition(b, parent >= 0 ? sk.GetBoneGlobalPose(parent).AffineInverse() * local : local);
            return sk.GlobalTransform * sk.GetBoneGlobalPose(b).Origin;
        }
        var world = new HoldWorld(layer, from, 9);
        var key = new Ms2.Ui.HoldPcAction();
        var pAt = Place(User, from + new Vector3(30, 0, 0));
        var nAt = Place(20, from + new Vector3(0, 0, 300));
        Place(40, from + new Vector3(0, 0, 200));
        if (pAt == Vector3.Inf || nAt == Vector3.Inf) { fails.Add("npc hold: no skeleton on the player or the npc"); return; }
        int far = world.FindHoldTarget(230, out _);
        bool polled2 = key.Poll(world);
        if (far != User || !polled2 || key.Target != User) fails.Add($"npc away: pick {far}, poll {polled2}, target {key.Target} (want player {User})");
        Place(20, from + new Vector3(0, 0, 10));
        int got = world.FindHoldTarget(230, out string node);
        bool polled = key.Poll(world);
        if (got != 20 || node != "Hold_Point00" || polled || key.Target != 0) fails.Add($"npc nearer: pick {got} '{node}', poll {polled}, target {key.Target} (want npc 20, refused, 0)");
        // a tie at 30: npc 20 (lower id) beats player 31, npc 40 (higher) does not
        Place(20, from + new Vector3(-30, 0, 0));
        int tieLow = world.FindHoldTarget(230, out _);
        Place(20, from + new Vector3(0, 0, 300));
        Place(40, from + new Vector3(-30, 0, 0));
        int tieHigh = world.FindHoldTarget(230, out _);
        if (tieLow != 20 || tieHigh != User) fails.Add($"hold tie: lower id {tieLow} (want 20), higher id {tieHigh} (want {User})");
        GD.Print($"remote ride: hold key npc 20 nearer than player {User} picks {got} and refuses ({polled}); npc away picks {far}; ties {tieLow} / {tieHigh}");
    }

    /// <summary>The hold key's world over the remote layer, the own feet at <c>from</c>: usable, state 1, boarding allowed.</summary>
    sealed class HoldWorld : Ms2.Ui.IHoldPcWorld
    {
        readonly RemoteLayer layer; readonly Vector3 from; readonly int self;
        public HoldWorld(RemoteLayer layer, Vector3 from, int self) { this.layer = layer; this.from = from; this.self = self; }
        public bool HoldPcUsable => true;
        public int HoldPcState => 1;
        public int HoldPcSelfId => self;
        public bool HoldPcRiding => false;
        public int FindHoldTarget(float range, out string node)
        {
            float best = float.MaxValue; int id = 0; node = null;
            layer.NearestHold(from, range, ref best, ref id, ref node);
            return id;
        }
        public bool IsPc(int objectId) => layer.HoldPcInfo(objectId, out _, out _, out _, out _, out _, out _, out _);
        public bool CanBoard(int objectId) => true;
        public bool NoBoarding(int objectId) => false;
    }

    void Done(string fail)
    {
        if (fail != null) fails.Add(fail);
        bool ok = fails.Count == 0;
        GD.Print($"remote ride: {(ok ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
