using System;
using System.Collections.Generic;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/platform_ride.tscn) of a ride on 02000142's lift box, the marker that rises 1200 over 5 s
/// (CBreakableObject::Update, breakableobject.cpp:362-445). The client moves the box actor with setGlobalPosition at the end
/// of the marker's Update (:435-444), which the PhysX scene takes at once, and the world tick runs every marker's Update
/// before the actors (worldservice_1.cpp:9648-9655), so the PC's move in the same frame (PCBase::vfunc_68,
/// pcbase.cpp:1797-1866) sweeps against the box where the marker is now.
/// What the player should see: standing on the box, rising with it, jumping and landing on it, coming down with it: the
/// feet stay on the box top all the time it is stood on, and never inside it.
/// Each frame is one drawn frame (the box's Godot transform reaches the physics server on its own only after the frame's
/// process calls), with a fixed 33 ms clock; the body is the PC capsule, carried the way Player.Carry carries it
/// (<see cref="PlatformCarry"/>: an upward change set at once, any other joined to the move), with the hit report's marker
/// kept through the jump. Quits with 0 when it holds, else 1. Needs MS2_DATA.
/// </summary>
public partial class PlatformRideCheck : Node
{
    const int FieldId = 2000142;
    const string LiftName = "e3868ecfeb3042bda4e289658f3b58e6";   // MS2BreakableNIF 7000, moved by lever 10000245
    const int FrameMs = 33;
    const float Gravity = 1.5f, JumpSpeed = 20f;   // per frame, in field units

    readonly List<string> fails = new();
    readonly Movement.NxController.FaceGrids faceGrids = new();
    readonly PlatformCarry carry = new();
    BreakableObjects markers;
    FieldBreakables draws;
    BreakableObjects.Marker lift;
    CharacterBody3D body;
    Movement.NxController nx;
    string hitMarker;
    int frame, clock, risingFrames = -1, jumpFrame = -1, landFrame = -1, standFrames;
    float vy, worstBelow, worstAbove;
    double lastTop;
    bool jumping, sawDescent;

    public override void _Ready()
    {
        try { Build(); }
        catch (Exception e) { fails.Add(e.ToString()); }
    }

    void Build()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        string xblock = FieldData.XBlockOf(packs, FieldId);
        var map = FieldMap.Load(packs, new FlatLibrary(packs), xblock);
        markers = new BreakableObjects { Pick = _ => 0 };
        markers.AttachField(map.Entities);
        lift = markers.Find(LiftName);
        if (lift == null || !lift.Moves || !lift.HasBox) { fails.Add($"{xblock}: no moving box marker {LiftName}"); return; }

        var world = new Node3D { Name = "World", Transform = FieldBuilder.ZupToYup };
        var colliders = new Node3D { Name = "Colliders" };
        world.AddChild(colliders);
        AddChild(world);
        draws = new FieldBreakables(markers, new Dictionary<FieldEntity, List<(MultiMesh, int)>>(),
                                    new Dictionary<FieldEntity, List<FieldBuilder.FadeDraw>>(), colliders);

        body = new CharacterBody3D { CollisionLayer = Movement.MoveConfig.PcLayer, CollisionMask = Movement.MoveConfig.PcGroups };
        var cs = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = Movement.MoveConfig.Radius, Height = Movement.MoveConfig.Height },
            Position = new Vector3(0, Movement.MoveConfig.Height / 2, 0),
        };
        body.AddChild(cs);
        AddChild(body);
        var p = lift.Position;
        body.GlobalPosition = new Vector3((float)p.X, (float)Top(), (float)-p.Y);
        nx = new Movement.NxController(body, cs, faceGrids);
        // the hit report's entity (Player.SetHitEntity): the touched shape's breakable marker, none for any other shape
        nx.ShapeHit = (collider, shape) =>
        {
            var owner = collider is CollisionObject3D co && shape >= 0 ? co.ShapeOwnerGetOwner(co.ShapeFindOwner(shape)) as Node : null;
            hitMarker = owner != null && owner.HasMeta(FieldBuilder.BreakableMeta) ? (string)owner.GetMeta(FieldBuilder.BreakableMeta) : null;
        };
        hitMarker = lift.Name;   // stood on it from the start
    }

    double Top() => lift.Position.Z + lift.Size.Z;

    public override void _Process(double delta)
    {
        if (fails.Count > 0 || markers == null) { Done(); return; }
        if (++frame < 3) return;   // the box is in the physics space after the first frames
        try { Step(); }
        catch (Exception e) { fails.Add(e.ToString()); Done(); }
    }

    void Step()
    {
        clock += FrameMs;
        markers.UpdateOnce((ulong)frame, clock, clock);   // the markers first (MarkerStep), the box follows (FieldBreakables)

        // the carry (pcbase.cpp:1797-1817), then the frame's own move
        carry.Step(markers, hitMarker, out var d);
        var g = new Vector3((float)d.X, (float)d.Z, (float)-d.Y);
        var move = Vector3.Zero;
        if (PlatformCarry.SetsAtOnce(d)) body.GlobalPosition += g;
        else move += g;

        if (lift.Phase == 2) risingFrames++;
        if (!jumping && landFrame < 0 && risingFrames == 30) { jumping = true; vy = JumpSpeed; jumpFrame = frame; }
        vy -= Gravity;
        var hit = nx.Move(move + new Vector3(0, vy, 0));
        if ((hit & Movement.NxController.Hit.Down) != 0)
        {
            vy = 0;
            if (jumping && frame > jumpFrame + 2) { jumping = false; landFrame = frame; }
        }

        float feet = body.GlobalPosition.Y;
        float off = feet - (float)Top();
        bool down = lastTop != 0 && Top() < lastTop;   // the way back: phase 3 still when entered from the end wait (Marker.Phase)
        lastTop = Top();
        if (!jumping)
        {
            standFrames++;
            worstBelow = Math.Min(worstBelow, off);
            if (down) { sawDescent = true; worstAbove = Math.Max(worstAbove, off); }
        }
        if (frame % 15 == 0 || frame == landFrame)
            GD.Print($"platform ride check: frame {frame} phase {lift.Phase} top {Top():0.0} feet {feet:0.0} ({off:+0.0;-0.0}){(jumping ? " jumping" : "")}{(frame == landFrame ? " LANDED" : "")}");
        if (sawDescent && lift.Phase == 1) Done();
        else if (frame > 1200) { fails.Add($"the lift's cycle did not end by frame {frame} (phase {lift.Phase})"); Done(); }
    }

    bool done;

    void Done()
    {
        if (done) return;
        done = true;
        if (fails.Count == 0)
        {
            if (landFrame < 0) fails.Add("the jump never landed on the box");
            if (worstBelow < -1f) fails.Add($"feet {-worstBelow:0.0} inside the box top while stood on it (the box a frame behind its marker)");
            if (worstAbove > 2f) fails.Add($"feet {worstAbove:0.0} above the box top while it came down");
        }
        draws?.Detach();
        bool ok = fails.Count == 0;
        GD.Print($"platform ride check: {standFrames} frames stood on the lift, landed at frame {landFrame}, deepest {-worstBelow:0.0} inside, highest {worstAbove:0.0} above on the way down: {(ok ? "OK" : "FAIL")}");
        foreach (var f in fails) GD.Print("platform ride check: FAIL " + f);
        GetTree().Quit(ok ? 0 : 1);
    }
}
