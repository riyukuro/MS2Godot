using System.Collections.Generic;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/platform_npc.tscn) of a PC carried by a moving platform among live npc capsules.
/// PCBase::vfunc_68 (pcbase.cpp:1797-1823): the marker's change since the last frame on the same marker joins the frame's
/// move when it is not upward, and the controller's Move(pMove, nFlags) sweeps it with the PC's groups (0x399 less nFlags,
/// which holds GetIgnoreCollisionGroups and the skill motion's ignoreCollisionGroups, :1771); an upward change is set at
/// once (vfunc_19, no sweep). A touched npc controller goes to onControllerHit (0x140cfaec0), which leaves the hit
/// report's entity alone, so the platform stays the ridden marker. So:
/// - with no npc, and with a live npc beside the path, the PC moves by the marker's change;
/// - a live npc (group 4) in the path stops the carried PC at its capsule while the platform runs on under it;
/// - the same move without groups 2 and 4 (ignoreCollisionGroups 20) passes it, and so does a plain move past a dead npc
///   (group 5, not in 0x399);
/// - an upward ride is set at once even into a live npc's capsule;
/// - a remote PC (the same vfunc_68) stops and passes the same way;
/// - a blob shadow over the platform lies on its box (ground ray groups 0x389, PCBase::vfunc_93), also under a live npc.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class PlatformNpcCheck : Node3D
{
    readonly Movement.NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    NpcLayer npcs;
    Player runner;
    System.Action<float, Vector3, uint> integrate;
    int npcRow, frames, nextId = 10;
    float npcRadius;
    const float Dt = 1f / 60;
    static readonly StringName TestId = "platform_npc_check_id";

    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    // field (x, y, z) to Godot (x, z, -y)
    static Vector3 G(Ms2.Formats.Model.Vec3 v) => new((float)v.X, (float)v.Z, (float)-v.Y);

    /// <summary>A moving NIF breakable with the props of a field's platform: <paramref name="dist"/> along
    /// <paramref name="dir"/> in 2 s after a 1 s start wait (CBreakableObject::Update, breakableobject.cpp:376-423).</summary>
    static FieldEntity Platform(string id, string dir, string dist)
    {
        var raw = new XEntity { Id = id, ModelName = "MS2BreakableNIF", Name = id };
        raw.Props["Position"] = "100, 200, 300";
        raw.Props["Rotation"] = "0, 0, 0";
        raw.Props["moveDirection"] = dir;
        raw.Props["moveDistance"] = dist;
        raw.Props["moveDuration"] = "2";
        raw.Props["moveStopDurationS"] = "1";
        raw.Props["moveStopDurationE"] = "5";
        raw.Props["moveRoundType"] = "2";
        var flat = new FlatModel { Name = "MS2BreakableNIF" };
        flat.Mixins.Add("MS2Breakable");
        return new FieldEntity { Raw = raw, Flat = flat };
    }

    sealed class Ride
    {
        public Ms2.Ui.BreakableObjects Markers;
        public Ms2.Ui.BreakableObjects.Marker Marker;
        public StaticBody3D Box;
    }

    // The platform's box: 1200 x 20 x 1200 with its top at the marker's position, group 0 as a field's breakable box
    // (FieldBreakables: layer 1 << group, the marker's name on the shape as its entity).
    Ride MakeRide(string id, string dir, string dist)
    {
        var r = new Ride { Markers = new Ms2.Ui.BreakableObjects() };
        r.Markers.AttachField(new[] { Platform(id, dir, dist) });
        r.Marker = r.Markers.Find(id);
        r.Box = new StaticBody3D { Name = "Box_" + id, CollisionLayer = 1, CollisionMask = 0 };
        var shape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1200, 20, 1200) } };
        shape.SetMeta(FieldBuilder.BreakableMeta, id);
        r.Box.AddChild(shape);
        AddChild(r.Box);
        Tick(r, 0);
        return r;
    }

    // WorldService's tick: the markers move before the actors (worldservice_1.cpp:9648-9655), the box follows (OnMoved)
    static void Tick(Ride r, long ms)
    {
        r.Markers.Update(ms, (int)ms);
        r.Box.GlobalPosition = G(r.Marker.Position) + new Vector3(0, -10, 0);
        r.Box.ForceUpdateTransform();
    }

    Node3D AddNpc(Vector3 at, bool dead = false)
    {
        int id = nextId++;
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = id, NpcId = npcRow, Hidden = dead }));
        var npc = GetNode<Node3D>($"Npc{id}");
        npc.SetMeta(TestId, id);
        Place(npc, at);
        return npc;
    }

    static void Place(Node3D npc, Vector3 at)
    {
        npc.GlobalPosition = at;
        npc.GetNode<Node3D>("Capsule").ForceUpdateTransform();
    }

    static uint Layer(Node3D npc) => npc.GetNode<StaticBody3D>("Capsule").CollisionLayer;

    /// <summary>The marker's whole move (Godot axes) over the ride's 3 s.</summary>
    Vector3 MarkerTravel(string dir, string dist)
    {
        var r = MakeRide("probe", dir, dist);
        var a = G(r.Marker.Position);
        Tick(r, 3000);
        var d = G(r.Marker.Position) - a;
        r.Box.Free();
        return d;
    }

    /// <summary>The PC stands on the ride's center, then rides it 3 s with its frame's move leaving out
    /// <paramref name="exclude"/>; the PC's travel, and the marker's in <paramref name="markerMove"/>.</summary>
    Vector3 RideLocal(Ride r, uint exclude, out Vector3 markerMove, out bool onRide)
    {
        runner.Platforms = r.Markers;
        runner.GlobalPosition = G(r.Marker.Position) + new Vector3(0, 0.5f, 0);
        // lands: a move down to the box makes it the report's entity (a body standing still makes no move, :1778-1780)
        for (int i = 0; i < 10; i++) integrate(Dt, i == 0 ? Vector3.Down * 2 : Vector3.Zero, exclude);
        float y0 = runner.GlobalPosition.Y - G(r.Marker.Position).Y;
        var p0 = runner.GlobalPosition;
        var m0 = G(r.Marker.Position);
        for (long ms = 0; ms <= 3000; ms += 1000 / 60)
        {
            Tick(r, ms);
            integrate(Dt, Vector3.Zero, exclude);
        }
        markerMove = G(r.Marker.Position) - m0;
        float y1 = runner.GlobalPosition.Y - G(r.Marker.Position).Y;
        onRide = Mathf.Abs(y0) < 2f && Mathf.Abs(y1) < 2f;
        if (!onRide) GD.Print($"platform npc check: ride {r.Marker.Name}: feet {y0:0.00} over the box before, {y1:0.00} after");
        var d = runner.GlobalPosition - p0;
        return new Vector3(d.X, markerMove.Y != 0 ? d.Y : 0, d.Z);
    }

    // what RemotePcStates hands its move: the horizontal velocity by the state (pcbase.cpp:1783-1788); no bNoVelocity
    static bool Drift(RemotePc pc) => Ms2.Net.Replay.RemotePcStates.KeepsDrift(pc.States.Cur, pc.Jumping);

    Vector3 RideRemote(RemotePc pc, Ride r, uint exclude, out Vector3 markerMove)
    {
        pc.Platforms = r.Markers;
        pc.Body.GlobalPosition = G(r.Marker.Position) + new Vector3(0, 0.5f, 300);
        for (int i = 0; i < 10; i++) pc.Move(i == 0 ? new System.Numerics.Vector3(0, 0, -2) : System.Numerics.Vector3.Zero, Dt, Drift(pc), false, exclude);
        var p0 = pc.Body.GlobalPosition;
        var m0 = G(r.Marker.Position);
        for (long ms = 0; ms <= 3000; ms += 1000 / 60)
        {
            Tick(r, ms);
            pc.Move(System.Numerics.Vector3.Zero, Dt, Drift(pc), false, exclude);
        }
        markerMove = G(r.Marker.Position) - m0;
        var d = pc.Body.GlobalPosition - p0;
        return new Vector3(d.X, 0, d.Z);
    }

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var table = new NpcTable(packs);
        npcs = new NpcLayer(packs, builder, table, this);
        foreach (int id in new[] { 21000001, 21000002, 21000003, 11000001 })
            if (table.Find(id) is { CapsuleIgnore: false } row) { npcRow = id; npcRadius = row.CapsuleRadius; break; }
        Check(npcRow != 0, "no npc row without the capsule ignore");
        if (npcRow == 0) return;
        var floor = new StaticBody3D { Position = new Vector3(0, -50, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(8000, 100, 8000) } });
        AddChild(floor);
        var visual = new Node3D();
        var anim = new AnimationPlayer();
        visual.AddChild(anim);
        runner = Player.Create(visual, anim, faceGrids);
        AddChild(runner);
        runner.SetProcess(false);
        runner.SetPhysicsProcess(false);
        runner.ScriptDriven = true;
        runner.BoundsMin = new Vector3(-4000, -4000, -1000);
        runner.BoundsMax = new Vector3(4000, 4000, 2000);
        integrate = (System.Action<float, Vector3, uint>)System.Delegate.CreateDelegate(typeof(System.Action<float, Vector3, uint>),
            runner, typeof(Player).GetMethod("Integrate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
    }

    void Run()
    {
        if (npcRow == 0) { Finish(); return; }
        var travel = MarkerTravel("1, 0, 0", "300");
        Check(travel.Length() > 290, $"the platform travels {travel}, not 300");
        var dir = travel.Normalized();
        var side = new Vector3(-dir.Z, 0, dir.X);
        float reach = Movement.MoveConfig.Radius + npcRadius;

        // a: no npc
        var r = MakeRide("pa", "1, 0, 0", "300");
        var free = RideLocal(r, 0, out var m, out bool on);
        Check(free.DistanceTo(m) < 0.5f && on, $"no npc: the PC travels {free}, the platform {m} (on it {on})");
        r.Box.Free();

        // b: a live npc beside the path
        r = MakeRide("pb", "1, 0, 0", "300");
        var start = G(r.Marker.Position);
        var beside = AddNpc(start + dir * 150 + side * (reach + 5));
        Check(Layer(beside) == 1u << 4, $"the npc beside: layer {Layer(beside):x}, not group 4");
        var besideMove = RideLocal(r, 0, out m, out on);
        Check(besideMove.DistanceTo(m) < 0.5f && on, $"npc beside: the PC travels {besideMove}, the platform {m} (on it {on})");
        Place(beside, new Vector3(0, 0, -6000));
        r.Box.Free();

        // c: a live npc in the path stops the carried PC; the skill's ignored groups 20 pass it
        r = MakeRide("pc", "1, 0, 0", "300");
        start = G(r.Marker.Position);
        var ahead = AddNpc(start + dir * 120);
        var blocked = RideLocal(r, 0, out m, out on);
        float along = blocked.Dot(dir);
        Check(along < 120 - reach + 1 && along > 120 - reach - 5 && on,
            $"live npc ahead: the PC travels {along:0.0} along the ride (stop at {120 - reach:0.0}), the platform {m.Length():0.0} (on it {on})");
        r.Box.Free();
        r = MakeRide("pc2", "1, 0, 0", "300");
        var passed = RideLocal(r, 0x14, out m, out on);
        Check(passed.DistanceTo(m) < 0.5f && on, $"live npc ahead, ignore 20: the PC travels {passed}, the platform {m} (on it {on})");
        r.Box.Free();

        // d: the same npc dead (group 5)
        npcs.Remove((int)ahead.GetMeta(TestId), true);
        Check(Layer(ahead) == 1u << 5, $"the dead npc: layer {Layer(ahead):x}, not group 5");
        r = MakeRide("pd", "1, 0, 0", "300");
        var pastDead = RideLocal(r, 0, out m, out on);
        Check(pastDead.DistanceTo(m) < 0.5f && on, $"dead npc ahead: the PC travels {pastDead}, the platform {m} (on it {on})");
        r.Box.Free();
        Place(ahead, new Vector3(0, 0, -6500));

        // e: an upward ride into a live npc's capsule is set at once
        r = MakeRide("pe", "0, 0, 1", "200");
        start = G(r.Marker.Position);
        var over = AddNpc(start + new Vector3(0, Movement.MoveConfig.Height + 40, 0));
        var up = RideLocal(r, 0, out m, out on);
        Check(Mathf.Abs(up.Y - m.Y) < 0.5f && m.Y > 190 && on, $"upward under a live npc: the PC rises {up.Y:0.0}, the platform {m.Y:0.0} (on it {on})");
        Place(over, new Vector3(0, 0, -7000));
        r.Box.Free();

        // f: a remote PC stops at the live npc and passes it without groups 2 and 4
        var rvis = new Node3D();
        var ranim = new AnimationPlayer();
        rvis.AddChild(ranim);
        var remote = RemotePc.Create("Pc90", rvis, ranim, null, Movement.MoveConfig.SceneGravity, new Vector3(0, 0.5f, 3000), 0, faceGrids);
        AddChild(remote.Body);
        r = MakeRide("pf", "1, 0, 0", "300");
        start = G(r.Marker.Position) + new Vector3(0, 0, 300);
        var remoteFree = RideRemote(remote, r, 0, out m);
        Check(remoteFree.DistanceTo(m) < 0.5f, $"remote PC, no npc: travels {remoteFree}, the platform {m}");
        r.Box.Free();
        r = MakeRide("pf2", "1, 0, 0", "300");
        var rAhead = AddNpc(start + dir * 120);
        var remoteBlocked = RideRemote(remote, r, 0, out m).Dot(dir);
        Check(remoteBlocked < 120 - reach + 1 && remoteBlocked > 120 - reach - 5,
            $"remote PC, live npc ahead: travels {remoteBlocked:0.0} (stop at {120 - reach:0.0}), the platform {m.Length():0.0}");
        r.Box.Free();
        r = MakeRide("pf3", "1, 0, 0", "300");
        var remotePassed = RideRemote(remote, r, 0x14, out m);
        Check(remotePassed.DistanceTo(m) < 0.5f, $"remote PC, live npc ahead, ignore 20: travels {remotePassed}, the platform {m}");

        // g: a blob shadow over the platform, and over a live npc standing on it, lies on the platform's top
        Place(rAhead, G(r.Marker.Position) + new Vector3(200, 0, 0));
        float top = G(r.Marker.Position).Y;
        float onBox = ShadowOver(G(r.Marker.Position) + new Vector3(-200, 60, 0));
        float overNpc = ShadowOver(G(r.Marker.Position) + new Vector3(200, Movement.MoveConfig.Height + 30, 0));
        // the shadow sits 13 over its ground hit, as on the floor in the npc capsule check
        Check(Mathf.Abs(onBox - top - 13) < 0.5f, $"a shadow over the platform lies at y {onBox:0.0}, top {top:0.0}");
        Check(Mathf.Abs(overNpc - top - 13) < 0.5f, $"a shadow over a live npc on the platform lies at y {overNpc:0.0}, top {top:0.0}");
        r.Box.Free();

        GD.Print($"platform npc check: free {free.Length():0.0}, npc beside {besideMove.Length():0.0}, live npc ahead stops at " +
                 $"{along:0.0} (contact {120 - reach:0.0}), ignore 20 {passed.Length():0.0}, dead npc {pastDead.Length():0.0}, " +
                 $"upward {up.Y:0.0}; remote free {remoteFree.Length():0.0}, stops at {remoteBlocked:0.0}, ignore 20 " +
                 $"{remotePassed.Length():0.0}; shadows at {onBox:0.0} / {overNpc:0.0} (top {top:0.0})");
        Finish();
    }

    float ShadowOver(Vector3 at)
    {
        BlobShadow.Load(PackSet.Shared(Settings.DataDir));
        var owner = new Node3D();
        AddChild(owner);
        owner.GlobalPosition = at;
        var s = BlobShadow.Add(owner, 30);
        if (s == null) { owner.Free(); return float.NaN; }
        s._Process(0);
        float y = s.Visible ? s.GlobalPosition.Y : float.NaN;
        owner.Free();
        return y;
    }

    void Finish()
    {
        foreach (var f in fails) GD.Print($"platform npc check: FAIL {f}");
        GD.Print(fails.Count == 0 ? "platform npc check: OK" : $"platform npc check: {fails.Count} failed");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (++frames < 3) return;
        SetPhysicsProcess(false);
        try { Run(); }
        catch (System.Exception e) { GD.Print($"platform npc check: {e.InnerException ?? e}: FAIL"); GetTree().Quit(1); }
    }
}
