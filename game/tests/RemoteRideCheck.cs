using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.Options;
using Ms2.Formats.Text;
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
///   at the body, and its report takes the ridee's touched entity (the stone) over the water it held before the ride.
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

    readonly List<string> steps = new();

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
                ridee.Effects = (action, ground, at, yaw) => steps.Add(action);
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
                // the ridee's run sequence throws its step keys (kf + companion xml) through PCBase::TextKeyEvent
                var runKeys = ridee.Mount.TextKeys(ridee.Anim.CurrentAnimation);
                int stepKeys = runKeys?.Count(k => k.text.StartsWith("run", StringComparison.OrdinalIgnoreCase)) ?? 0;
                if (stepKeys > 0 && steps.Count == 0) fails.Add($"ridee run sequence has {stepKeys} run key(s), no step played");
                ridee.Effects = null;
                GD.Print($"remote ride: ridee run sequence keys {runKeys?.Count ?? 0} ({stepKeys} run), steps played {steps.Count}: {string.Join(",", steps.Distinct())}");
                GD.Print($"remote ride: run moved {moved:0.0} (want {want:0.0}), ridee {ridee.Anim.CurrentAnimation}, rider {rider.Anim.CurrentAnimation}");
                ridee.States.Push(Rec(1, 0, ridee, default));
                break;
            }
            case 150:
            {
                var at = ridee.Body.Position;
                // the rider's report still holds what it touched before the ride: a water cell; the ridee's holds the stone
                var water = new StaticBody3D { Position = new Vector3(3000, -50, 0) };
                var wcs = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(150, 150, 150) } };
                wcs.SetMeta(FieldBuilder.KindMeta, MoveConfig.WaterGroup);
                water.AddChild(wcs);
                AddChild(water);
                const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(RemotePc).GetMethod("SetHitEntity", Private).Invoke(rider, new object[] { water, 0 });
                var owner = typeof(RemotePc).GetField("hitOwner", Private);
                ulong rideeOwner = (ulong)owner.GetValue(ridee);
                bool wetBefore = rider.OnWater;
                RemoteLayer.Dismount(rider, rider.Visual, ridee, 1, 1);
                ulong riderOwner = (ulong)owner.GetValue(rider);
                GD.Print($"remote ride: dismount report: rider on water {wetBefore} then {rider.OnWater}, ridee's entity {rideeOwner != 0 && riderOwner == rideeOwner}");
                if (!wetBefore || rideeOwner == 0 || riderOwner != rideeOwner || rider.OnWater)
                    fails.Add($"dismount report: the rider keeps its own entity (water {rider.OnWater}), not the ridee's");
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
        RiderWater(layer, m.Data.Id);
        RideeBox(m);
        NpcBeforePlayer(layer, pc);
        HideRider(layer, pc);
        RideeStats(layer, m.Data.Id);
        RideeSpeeds(layer, m.Data.Id);
        RideShadow(layer, pc);
        OwnUnRideSkillEffect(pc, m);
        LoopEffect(pc);
        KeysByKfm();
        KeyCaseAndOrder(m.Data.Id);
        pc.QueueFree();
        field.QueueFree();
        layer.Free();
    }

    /// <summary>
    /// The client's text key reader (FUN_1414a90c0, ms2animationservice.cpp:349-405) finds the companion xml's element by
    /// the kf name with TinyXML, which matches by case, and NiTextKeyExtraData::LoadBinary (NiTextKeyExtraData.cpp:69-72)
    /// puts the kf's own keys first and the xml's after, unsorted. A mount pointed at Npc kfms whose companion xml names
    /// a kf only with another case gets just the kf's keys for that sequence, and a sequence whose xml has a key earlier
    /// than a kf key keeps the kf's keys then the xml's.
    /// </summary>
    void KeyCaseAndOrder(int rideId)
    {
        const System.Reflection.BindingFlags Own = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var entryField = typeof(Mount).GetField("kfmEntry", Own);
        var folderField = typeof(Mount).GetField("folder", Own);
        var kfmField = typeof(Mount).GetField("kfm", Own);
        // kfms whose xml names a kf only with another case (a scan of the Npc archive finds 49 such names)
        string[] names = { "02010039_berzzo", "02010049_el_woman1", "02020069_m_bungbung", "11000074_karl", "11000100_hiphopman1" };
        var all = packs.List(Mount.NpcArchive);
        int caseSeqs = 0, caseWrong = 0, lateSeqs = 0, orderWrong = 0;
        var wrong = new List<string>();
        foreach (var n in names)
        {
            string entry = all.FirstOrDefault(e => e.EndsWith("/" + n + ".kfm", StringComparison.OrdinalIgnoreCase));
            string xml = entry == null ? null : entry[..^4] + ".xml";
            if (xml == null || !packs.Exists(Mount.NpcArchive, xml)) { wrong.Add($"{n}: no kfm and xml"); continue; }
            var els = PackXml.Parse(packs.Read(Mount.NpcArchive, xml)).Root.Elements().ToList();
            Mount mt;
            try { mt = Mount.Load(packs, builder, rideId); } catch (Exception e) { wrong.Add($"{n}: {e.Message}"); continue; }
            var kfm = KfmFile.Load(packs.Read(Mount.NpcArchive, entry));
            entryField.SetValue(mt, entry);
            folderField.SetValue(mt, entry[..entry.LastIndexOf('/')]);
            kfmField.SetValue(mt, kfm);
            var lib = mt.Anim.GetAnimationLibrary("");
            foreach (var sq in kfm.Sequences)
            {
                if (string.IsNullOrEmpty(sq.Name)) continue;    // a nameless sequence (one in m_bungbung) is never played by name
                string kfName = Path.GetFileName(sq.KfPath.Replace('\\', '/'));
                string stem = Path.GetFileNameWithoutExtension(kfName);
                var exact = els.FirstOrDefault(e => e.Name.LocalName == stem);
                bool caseOnly = exact == null && els.Any(e => string.Equals(e.Name.LocalName, stem, StringComparison.OrdinalIgnoreCase));
                if (!caseOnly && exact == null) continue;
                string kfEntry = $"{entry[..entry.LastIndexOf('/')]}/{kfName.ToLowerInvariant()}";
                if (!packs.Exists(Mount.NpcArchive, kfEntry)) continue;
                var own = KfAnimation.Load(Ms2.Formats.Nif.NifFile.Load(builder.Spec, packs.Read(Mount.NpcArchive, kfEntry))).TextKeys;
                var want = new List<(double time, string text)>(own);
                if (!caseOnly)
                    foreach (var k in exact.Elements("key"))
                    {
                        string text = (string)k.Attribute("text");
                        if (string.IsNullOrEmpty(text)) continue;
                        double.TryParse((string)k.Attribute("time"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t);
                        want.Add((t, text));
                    }
                bool late = !caseOnly && own.Count > 0 && want.Count > own.Count && want.Skip(own.Count).Min(k => k.time) < own.Max(k => k.time);
                if (!caseOnly && !late) continue;
                string key = sq.Name.ToLowerInvariant();
                if (lib.HasAnimation(key)) lib.RemoveAnimation(key);    // a sequence of this name from another kfm
                var got = mt.TextKeys(mt.SequenceKey(sq.Name, false)) ?? new List<(double time, string text)>();
                bool same = got.SequenceEqual(want);
                if (caseOnly) { caseSeqs++; if (!same) caseWrong++; }
                else { lateSeqs++; if (!same) orderWrong++; }
                if (!same && wrong.Count < 4) wrong.Add($"{n} {sq.Name}: {got.Count} key(s) for {want.Count}{(caseOnly ? " (case only)" : " (xml key earlier)")}");
            }
            mt.Visual.Free();
        }
        GD.Print($"remote ride: key case and order: case-only sequences {caseSeqs} ({caseWrong} took xml keys), xml-earlier sequences {lateSeqs} ({orderWrong} reordered){(wrong.Count > 0 ? "; " + string.Join("; ", wrong) : "")}");
        if (caseSeqs == 0 || lateSeqs == 0 || caseWrong != 0 || orderWrong != 0 || wrong.Count > 0)
            fails.Add($"key case and order: case-only {caseSeqs} ({caseWrong} wrong), xml-earlier {lateSeqs} ({orderWrong} wrong); {string.Join("; ", wrong)}");
    }

    /// <summary>A sequence's text keys are kept by the kfm that names it: the same mount loaded twice, the second pointed at
    /// another kfm entry in the folder with no companion xml, loads a sequence whose first-kfm keys include the xml's. The
    /// first mount then looks the sequence up again with its animation already built (its sequence maps cleared, as a mount
    /// sharing the built library does) and must get its own list back, not the second kfm's.</summary>
    void KeysByKfm()
    {
        const System.Reflection.BindingFlags Own = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var f = typeof(Mount).GetField("kfmEntry", Own);
        var kf = typeof(Mount).GetField("kfm", Own);
        var seqKeys = typeof(Mount).GetField("seqKeys", Own);
        var textKeys = typeof(Mount).GetField("textKeys", Own);
        int mounts = 0, pairs = 0, took = 0;
        foreach (var e in packs.List("Xml").Where(e => e.StartsWith("riding/", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            if (pairs >= 4 || mounts >= 40 || !int.TryParse(Path.GetFileNameWithoutExtension(e), out int id)) continue;
            Mount a, b;
            try { a = Mount.Load(packs, builder, id); b = Mount.Load(packs, builder, id); }
            catch (Exception) { continue; }
            mounts++;
            string entry = (string)f.GetValue(b);
            f.SetValue(b, entry[..entry.LastIndexOf('/')] + "/other_kfm_without_xml.kfm");
            foreach (var s in ((KfmFile)kf.GetValue(a)).Sequences)
            {
                var ak = a.TextKeys(a.SequenceKey(s.Name, false));
                var bk = b.TextKeys(b.SequenceKey(s.Name, false));
                if (ak == null || bk == null || ak.Count == bk.Count) continue;
                pairs++;
                ((System.Collections.IDictionary)seqKeys.GetValue(a)).Clear();
                ((System.Collections.IDictionary)textKeys.GetValue(a)).Clear();
                var again = a.TextKeys(a.SequenceKey(s.Name, false));
                if (again == null || again.Count != ak.Count) took++;
                break;
            }
            a.Visual.Free();
            b.Visual.Free();
        }
        if (pairs == 0 || took != 0) fails.Add($"text keys by kfm: {took} of {pairs} sequence(s) looked up again took the other kfm's keys ({mounts} mount(s))");
        GD.Print($"remote ride: text keys by kfm: {pairs} sequence(s) keyed by the xml too, of {mounts} mount(s), {took} took the other kfm's keys");
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

    /// <summary>FUN_141675ed0 (pcbase.cpp:4328-4345) at another player: a mounted PC answers with its ridee's water test, so
    /// a rider whose own report is stone reads water while its ridee stands on water; after the ride off it reads its own
    /// report again.</summary>
    void RiderWater(RemoteLayer layer, int rideId)
    {
        const int User = 35;
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var u = new FieldAddUser { ObjectId = User, User = new UserBlock { Name = "w", CharacterId = 135, Gender = 1 } };
        u.Stats[0].Health = 100; u.Stats[2].Health = 100;
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, u, false));
        layer._Process(0);
        var users = (System.Collections.IDictionary)typeof(RemoteLayer).GetField("users", Private).GetValue(layer);
        var rec = users[User];
        var pc = rec == null ? null : (RemotePc)rec.GetType().GetField("Pc").GetValue(rec);
        if (pc == null) { fails.Add("rider water: no remote pc"); return; }
        var group = typeof(RemotePc).GetField("hitGroup", Private);
        group.SetValue(pc, 0);
        layer.OnRide(new ResponseRide(0, User, new RideOnAction(0, rideId, 5035, false, 0, 0, 0, 0), null, 0, 0, 0, 0, false));
        var rd = layer.RideeOf(User);
        if (rd == null) { fails.Add("rider water: no ridee after the ride on"); return; }
        group.SetValue(rd, MoveConfig.WaterGroup);
        bool riding = pc.OnWater;
        group.SetValue(rd, 0);
        bool ridingDry = pc.OnWater;
        group.SetValue(rd, MoveConfig.WaterGroup);
        layer.OnRide(new ResponseRide(1, User, null, new RideOffAction(0, 0), 0, 0, 0, 0, false));
        group.SetValue(pc, 0);
        bool after = pc.OnWater;
        GD.Print($"remote ride: rider water: ridee on water {riding}, on stone {ridingDry}; after the ride off {after}");
        if (!riding || ridingDry || after) fails.Add($"rider water: riding on a water ridee {riding}, on a stone ridee {ridingDry}, after the ride off {after}");
    }

    /// <summary>Ridee::MakeNxBoxController (ridee.cpp:487, :1184-1231) at another player's mount: a row with physic set
    /// gets a kinematic box of the row's width, height and depth, posed by vfunc_53 (:1166-1180) at the ridee's position
    /// with its centre half the height plus 20 up, turned by its Z rotation; the ridee's own sweeps pass through it; a row
    /// without physic gets none.</summary>
    void RideeBox(Mount plain)
    {
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var m = FirstMount(x => x.Data.Physic);
        if (m == null) { fails.Add("ridee box: no riding row with physic set"); return; }
        var rd = RemotePc.CreateRidee("BoxRidee", m, MoveConfig.SceneGravity, rider.Body.Position + new Vector3(400, 25, 0), 90f, faceGrids);
        AddChild(rd.Body);
        rd.Spawn();
        rd.Tick(Dt);
        static AnimatableBody3D BoxOf(RemotePc p) => p.Body.GetChildren().OfType<AnimatableBody3D>().FirstOrDefault();
        var box = BoxOf(rd);
        var size = (box?.GetChildren().OfType<CollisionShape3D>().FirstOrDefault()?.Shape as BoxShape3D)?.Size;
        var want = rd.Body.GlobalPosition + Vector3.Up * (m.Data.Height / 2 + 20f);
        var at = box?.GlobalPosition ?? Vector3.Zero;
        float yaw = box == null ? float.NaN : Mathf.RadToDeg(box.GlobalBasis.GetEuler().Y);
        var nx = typeof(RemotePc).GetField("nx", Private).GetValue(rd);
        var ignored = (Rid)nx.GetType().GetField("IgnoreBody").GetValue(nx);
        bool ownPasses = box != null && ignored == box.GetRid();
        // the plain row must have no physic, else the boxless check tests nothing
        bool plainNone = false;
        if (plain.Data.Physic) fails.Add($"ridee box: plain row {plain.Data.Id} has physic set");
        else
        {
            var pr = RemotePc.CreateRidee("PlainRidee", plain, MoveConfig.SceneGravity, rider.Body.Position + new Vector3(-400, 25, 0), 0f, faceGrids);
            plainNone = BoxOf(pr) == null;
            pr.Free();
        }
        GD.Print($"remote ride: ridee box {m.Data.Id}: size {size}, centre {at} for {want}, yaw {yaw:0.#}, own sweeps pass {ownPasses}; row {plain.Data.Id} without physic boxless {plainNone}");
        if (size != new Vector3(m.Data.Width, m.Data.Height, m.Data.Depth) || !at.IsEqualApprox(want) || !Mathf.IsEqualApprox(yaw, 90f, 0.01f) || !ownPasses || !plainNone)
            fails.Add($"ridee box {m.Data.Id}: size {size}, centre {at} for {want}, yaw {yaw}, own sweeps pass {ownPasses}, plain row boxless {plainNone}");
        // FUN_1417082c0 (ridee.cpp:407) releases the actor at once: the box is out of the space before the frame ends
        rd.Free();
        bool released = box != null && !box.IsInsideTree();
        GD.Print($"remote ride: ridee box released at once {released}");
        if (!released) fails.Add("ridee box: still in the physics space after the ridee was freed");
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
        // PCBase::ApplyHideReasons (pcbase.cpp:1188-1203): a hidden PC's simple shadow goes off, back on when shown
        var users = (System.Collections.IDictionary)typeof(RemoteLayer)
            .GetField("users", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(layer);
        var rpc = (RemotePc)users[User].GetType().GetField("Pc").GetValue(users[User]);
        rpc.SetHideReason(1, true);
        bool hiddenOff = rider is { Off: true };
        rpc.SetHideReason(1, false);
        bool shownOn = rider is { Off: false };
        if (!hiddenOff || !shownOn) fails.Add($"remote hide reason shadow: off hidden {hiddenOff}, on shown {shownOn}");
        Add(DeadUser, 0);
        var dead = Shadow(layer.HoldVisual(DeadUser)?.GetParent());
        Ride(DeadUser);
        if (dead is not { Off: false }) fails.Add($"ride shadow: a dead rider's shadow went off ({dead?.Off})");
        Off(DeadUser);
        var own = Shadow(pc) ?? BlobShadow.Add(pc, 100f, 3f);
        pc.SetHideReason(1, true);
        bool ownHiddenOff = own is { Off: true };
        pc.SetHideReason(1, false);
        if (!ownHiddenOff || own is not { Off: false }) fails.Add($"own hide reason shadow: off hidden {ownHiddenOff}, on shown {own?.Off == false}");
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
        HideMyPC(pc);   // at this shadow level: the own pc's and its mount's simple shadows are both there
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
        bool oOn = pc.Visual.Visible, mountShown = hide.Visual.Visible, tagKept = !pc.HidesNameTag;   // uihudentity.cpp:200-207
        pc.RideOn(plain);                     // RideOn rides the old mount off first
        bool oSwap = pc.Visual.Visible;
        pc.RideOff();
        bool oOff = pc.Visual.Visible;
        if (oOn || !mountShown || !tagKept || !oSwap || !oOff) fails.Add($"own hideRider: shown riding {oOn} (mount {mountShown}, name tag {tagKept}), plain after swap {oSwap}, after {oOff}");
        GD.Print($"remote ride: hideRider {hide.Data.Id} hides the rider ({!rOn}, own {!oOn}), shown after ride off ({rOff}, own {oOff}); plain {plain.Data.Id} shown");
    }

    /// <summary>Field property 3 through LiveSession's 0xCA path, on both protocols (the Maple2 server's visible_my_pc sends an add
    /// of kind 3 to hide and a remove of it to show): FieldPropertyHideMyPCCli::vfunc_4 / vfunc_5 (fieldpropertycli.cpp:146-171)
    /// add and remove hide reason 1 on the local player and its ridee; ApplyHideReasons hides the body and turns its simple
    /// shadow off (pcbase.cpp:1188-1203); a rider that has the reason gets on with its ridee hidden (pcbase.cpp:3334-3335);
    /// a remove of a kind not registered does nothing, and ClearFieldProperties (a field leave) unapplies it. The hidden own pc
    /// has no name tag while its ridee is hidden too (UIHudEntity::Entity::vfunc_8, uihudentity.cpp:200-210).</summary>
    void HideMyPC(Player pc)
    {
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var own = pc.GetNodeOrNull<BlobShadow>(FieldShadows.ShadowNode) ?? BlobShadow.Add(pc, 100f, 3f);
        foreach (var profile in new[] { Ms2.Net.ProtocolProfile.Kms2525, Ms2.Net.ProtocolProfile.Gms2 })
        {
            var plain = FirstMount(x => !x.Data.HideRider);   // a new ridee each pass: ride off frees the visual
            if (plain == null) { fails.Add("hide my pc: no mount without hideRider"); return; }
            var ls = new LiveSession();
            typeof(LiveSession).GetField("player", Private).SetValue(ls, pc);
            void Recv(string hex) => ls.FieldProperties.Apply(FieldPropertyMessage.Read(new Maple2.PacketLib.Tools.ByteReader(Convert.FromHexString(hex)), profile));
            string at = profile.IsGms2 ? "GMS2" : "KMS";
            bool Shown() => pc.Visual.Visible && !own.Off && !pc.IsHidden;
            Recv("0203");
            bool removeNothing = Shown() && !pc.HidesNameTag;
            Recv("0103");
            bool hidden = !pc.Visual.Visible && own.Off && pc.HasHideReason(Player.HideReasonField) && pc.HidesNameTag;
            Recv("0203");
            bool back = Shown() && !pc.HidesNameTag;
            Recv("0001000000" + "03");
            bool listHidden = !pc.Visual.Visible && own.Off;
            pc.RideOn(plain);
            var ms = plain.Visual.GetNodeOrNull<BlobShadow>(FieldShadows.ShadowNode);
            bool mountHidden = plain.Hidden && !plain.Visual.Visible && ms is { Off: true } && pc.HidesNameTag;
            Recv("0203");
            bool mountBack = !plain.Hidden && plain.Visual.Visible && ms is { Off: false } && pc.Visual.Visible && !pc.HidesNameTag;
            Recv("0103");
            bool mountHiddenRiding = plain.Hidden && !plain.Visual.Visible && !pc.Visual.Visible;
            ls.FieldProperties.Clear();
            bool cleared = !plain.Hidden && plain.Visual.Visible && pc.Visual.Visible && !pc.IsHidden;
            pc.RideOff();
            ls.Free();
            if (!removeNothing || !hidden || !back || !listHidden || !mountHidden || !mountBack || !mountHiddenRiding || !cleared)
                fails.Add($"hide my pc {at}: remove unregistered {removeNothing}, hidden {hidden}, back {back}, list {listHidden}, mount on hidden {mountHidden}, "
                    + $"mount back {mountBack}, add riding {mountHiddenRiding}, field leave {cleared}");
            else GD.Print($"remote ride: hide my pc {at}: kind 3 hides the own pc, its shadow, its name tag and its mount {plain.Data.Id}; its remove and a field leave show them");
        }
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

    /// <summary>
    /// Ridee::vfunc_12's loop effect (ridee.cpp:527-534) and ~Ridee's stop (:402-406), on the first riding row with a
    /// loopEffect whose model loads: another player's ride on keeps the row's effect at its ridee in the field's effect layer;
    /// the ride off stops it, and a PC that leaves while riding stops it too. The own pc's mount keeps it the same way from
    /// its ride on to its ride off. A row without one keeps nothing.
    /// </summary>
    void LoopEffect(Player pc)
    {
        const int User = 35;
        var loopMount = FirstMount(x => x.Data.LoopEffect != null);
        if (loopMount == null) { fails.Add("loop effect: no riding row with a loopEffect loads"); return; }
        int rideId = loopMount.Data.Id;
        string path = loopMount.Data.LoopEffect;
        var field = new FieldStub(packs);
        AddChild(field);
        var fx = new Ms2Godot.World.RegionSkills(packs, builder);
        field.AddChild(fx);
        var layer = new RemoteLayer(field, builder, 1);
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var kept = (System.Collections.IDictionary)typeof(Ms2Godot.World.RegionSkills).GetField("kept", Private).GetValue(fx);
        if (!fx.Has(path)) fails.Add($"loop effect: no {path} in the data");
        var u = new FieldAddUser { ObjectId = User, User = new UserBlock { Name = "l", CharacterId = 135, Gender = 1 } };
        u.Stats[0].Health = 100; u.Stats[2].Health = 100;
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, u, false));
        layer._Process(0);
        ResponseRide On(int id) => new(0, User, new RideOnAction(0, id, 5035, false, 0, 0, 0, 0), null, 0, 0, 0, 0, false);
        layer.OnRide(On(rideId));
        int h = layer.RideeOf(User)?.Mount?.LoopFx.Handle ?? 0;
        if (h == 0 || !kept.Contains(h)) fails.Add($"remote loop effect of {rideId}: handle {h} not kept");
        layer.OnRide(new ResponseRide(1, User, null, new RideOffAction(0, 0), 0, 0, 0, 0, false));
        if (h != 0 && kept.Contains(h)) fails.Add($"remote loop effect {h} still kept after the ride off");
        layer.OnRide(On(rideId));
        int h2 = layer.RideeOf(User)?.Mount?.LoopFx.Handle ?? 0;
        layer.Tracker.Events.Enqueue(new UserRemovedEvent(0, User));
        layer._Process(0);
        if (h2 == 0 || kept.Contains(h2)) fails.Add($"remote loop effect {h2} of a PC that left riding: still kept or never kept");
        // a row without one
        var plain = FirstMount(x => x.Data.LoopEffect == null);
        int before = kept.Count;
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, u, false));
        layer._Process(0);
        layer.OnRide(On(plain.Data.Id));
        if ((layer.RideeOf(User)?.Mount?.LoopFx.Handle ?? -1) != 0 || kept.Count != before) fails.Add($"plain row {plain.Data.Id} kept a loop effect");

        // the own pc
        if (pc.Mount != null) pc.RideOff();
        pc.KeepRideEffect = (p, actor) => fx.SpawnOn(p, actor, Ms2.Audio.SoundMusicEffectState.CasterMyPc);
        pc.StopRideEffect = id => fx.FadeOut(id, 0);
        pc.RideEffectScope = () => fx;
        pc.RideOn(loopMount);
        int own = loopMount.LoopFx.Handle;
        if (own == 0 || !kept.Contains(own)) fails.Add($"own loop effect of {rideId}: handle {own} not kept");
        pc.RideOff();
        if (own != 0 && kept.Contains(own)) fails.Add($"own loop effect {own} still kept after the ride off");
        pc.KeepRideEffect = null; pc.StopRideEffect = null; pc.RideEffectScope = null;
        layer.Free();
        field.QueueFree();
        GD.Print($"remote ride: loop effect of {rideId} '{path}' kept at the ridee ({h}, {h2}) and own ({own}), stopped at the ride off and the leave");
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
