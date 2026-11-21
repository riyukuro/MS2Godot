using Godot;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/trigger_group.tscn): a trigger mesh's body changes group at runtime (the Trigger packet's
/// hide = group 5, show = its own group, TriggerCollider.SetGroup). RayQuery.Nearest caches a collider's group by instance id,
/// so a ray cast before the change must not keep the old group: hidden, the body is skipped (the ray reaches the floor under it);
/// shown again, it is hit. Quits 0 when all three casts give the expected collider.
/// </summary>
public partial class TriggerGroupCheck : Node3D
{
    const uint Ground = 1u << 3;
    CharacterBody3D self; StaticBody3D floor; FieldBuilder.TriggerCollider tc;

    public override void _Ready()
    {
        var floor = new StaticBody3D { Position = new Vector3(0, -50, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(200, 100, 200) } });
        floor.SetMeta(FieldBuilder.GroupMeta, 3);
        AddChild(floor);
        var tc = new FieldBuilder.TriggerCollider { Group = 3 };
        tc.Body = new StaticBody3D { Position = new Vector3(0, 40, 0), CollisionLayer = 1u << 3 };
        tc.Body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(200, 10, 200) } });
        tc.Body.SetMeta(FieldBuilder.GroupMeta, 3);
        AddChild(tc.Body);
        var self = new CharacterBody3D(); AddChild(self);
        this.self = self; this.floor = floor; this.tc = tc;
        CallDeferred(nameof(Run));
    }

    async void Run()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        var space = GetWorld3D().DirectSpaceState;
        var rq = new RayQuery();
        ulong Cast() => rq.Nearest(space, new Vector3(0, 100, 0), new Vector3(0, -1000, 0), self.GetRid(), Ground)?.ColliderId ?? 0;
        ulong shown = Cast();
        tc.SetGroup(5);
        ulong hidden = Cast();
        tc.SetGroup(3);
        ulong again = Cast();
        bool ok = shown == tc.Body.GetInstanceId() && hidden == floor.GetInstanceId() && again == tc.Body.GetInstanceId();
        GD.Print($"trigger group: shown hits trigger {shown == tc.Body.GetInstanceId()}, hidden hits floor {hidden == floor.GetInstanceId()}, shown again hits trigger {again == tc.Body.GetInstanceId()}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
