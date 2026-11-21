using System.Diagnostics;
using Godot;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless probe (res://tests/ray_cost.tscn): the down ray of the own player's fall-return, climb and ground-entity
/// reads, cast at a floor of group 3 under a ledge of group 5 (the group filter skips the ledge, a second cast). Prints bytes
/// and µs per ray for the previous shape (the "collider" key read, an object Variant) and for RayQuery.Nearest (the instance
/// id and the group cache); quits 0 when Nearest allocates less and both find the same floor.
/// </summary>
public partial class RayCostCheck : Node3D
{
    const int Rays = 2000;
    const uint Groups = 1u << 3;

    public override void _Ready()
    {
        var floor = Body(3, new Vector3(0, -50, 0), new Vector3(200, 100, 200));
        var ledge = Body(5, new Vector3(0, 40, 0), new Vector3(200, 10, 200));
        AddChild(floor); AddChild(ledge);
        var self = new CharacterBody3D(); AddChild(self);
        CallDeferred(nameof(Run), self);
    }

    static StaticBody3D Body(int group, Vector3 pos, Vector3 size)
    {
        var b = new StaticBody3D { Position = pos };
        b.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        b.SetMeta(FieldBuilder.GroupMeta, group);
        return b;
    }

    ulong OldRay(RayQuery rq, PhysicsDirectSpaceState3D space, Rid self, out Vector3 pos)
    {
        var q = rq.Set(new Vector3(0, 100, 0), new Vector3(0, -1000, 0), self);
        var exclude = rq.Ex;
        pos = default;
        for (int i = 0; i < 16; i++)
        {
            using var hit = space.IntersectRay(q);
            if (hit.Count == 0) return 0;
            using var colV = hit[PhysicsKeys.Collider];
            var col = colV.AsGodotObject();
            int group = col is Node n && n.HasMeta(FieldBuilder.GroupMeta) ? (int)n.GetMeta(FieldBuilder.GroupMeta) : 0;
            if ((Groups & (1u << group)) != 0) { pos = (Vector3)hit[PhysicsKeys.Position]; return col.GetInstanceId(); }
            exclude.Add((Rid)hit[PhysicsKeys.Rid]);
            q.Exclude = exclude;
        }
        return 0;
    }

    async void Run(CharacterBody3D self)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        var space = GetWorld3D().DirectSpaceState;
        var rq = new RayQuery();
        var rid = self.GetRid();
        bool same = true;
        double oldB = 0, newB = 0, oldUs = 0, newUs = 0;
        for (int pass = 0; pass < 2; pass++)   // pass 0 warms up
        {
            long b0 = System.GC.GetAllocatedBytesForCurrentThread(); long t0 = Stopwatch.GetTimestamp();
            ulong lastOld = 0;
            for (int i = 0; i < Rays; i++) lastOld = OldRay(rq, space, rid, out _);
            long b1 = System.GC.GetAllocatedBytesForCurrentThread(); long t1 = Stopwatch.GetTimestamp();
            ulong lastNew = 0;
            for (int i = 0; i < Rays; i++) lastNew = rq.Nearest(space, new Vector3(0, 100, 0), new Vector3(0, -1000, 0), rid, Groups)?.ColliderId ?? 0;
            long b2 = System.GC.GetAllocatedBytesForCurrentThread(); long t2 = Stopwatch.GetTimestamp();
            if (pass == 1)
            {
                oldB = (b1 - b0) / (double)Rays; newB = (b2 - b1) / (double)Rays;
                oldUs = Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds * 1000 / Rays; newUs = Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds * 1000 / Rays;
                same = lastOld != 0 && lastOld == lastNew;
            }
        }
        GD.Print($"ray cost: old {oldB:F1} B/ray {oldUs:F1} us, Nearest {newB:F1} B/ray {newUs:F1} us, same floor {same}");
        GetTree().Quit(same && newB < oldB ? 0 : 1);
    }
}
