using System.Collections.Generic;
using Godot;

namespace Ms2Godot;

/// <summary>
/// Server stand-in: the colliders of the field's entities with DisableCollision set, in a physics space of their own that
/// only the npc ground cast queries (<see cref="FieldNpcs"/>). The client builds no collision for such an entity, so they
/// stay out of the world space the player, the camera and the shadows use; the official server's npc ground counts them
/// as floors, so a spawn point over a grating or an invisible box stands on it. The shapes are the ones the entity would
/// make without the flag: its NIF PhysX meshes (one-sided, as in the world) and its GeneratePhysX box. Built off the
/// tree with the field; the space and its body exist while the node is in the tree.
/// </summary>
public sealed partial class NpcGroundSpace : Node3D
{
    const string Meta = "ms2_npc_ground_space";

    readonly List<(Shape3D shape, Transform3D xform)> shapes = new();
    readonly RayQuery ray = RayQuery.CastRay();
    Rid space, body;

    /// <summary>The number of shapes added.</summary>
    public int Count => shapes.Count;

    /// <summary>A disabled entity's shape placed at <paramref name="xform"/> in this node's space.</summary>
    public void Add(Shape3D shape, Transform3D xform) => shapes.Add((shape, xform));

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) shapes.Clear();
    }

    public override void _EnterTree()
    {
        space = PhysicsServer3D.SpaceCreate();
        PhysicsServer3D.SpaceSetActive(space, true);
        body = PhysicsServer3D.BodyCreate();
        PhysicsServer3D.BodySetMode(body, PhysicsServer3D.BodyMode.Static);
        var at = GlobalTransform;
        foreach (var (shape, xform) in shapes) PhysicsServer3D.BodyAddShape(body, shape.GetRid(), at * xform);
        // the space is set after the shapes, so they are in its broadphase at once: the npcs are spawned before the
        // first physics step
        PhysicsServer3D.BodySetSpace(body, space);
        GetWorld3D().SetMeta(Meta, this);
    }

    public override void _ExitTree()
    {
        var world = GetWorld3D();
        if (world != null && world.HasMeta(Meta) && world.GetMeta(Meta).AsGodotObject() == this) world.RemoveMeta(Meta);
        PhysicsServer3D.FreeRid(body);
        PhysicsServer3D.FreeRid(space);
        body = space = default;
    }

    /// <summary>The first disabled-collision surface on the segment <paramref name="from"/> to <paramref name="to"/> (global),
    /// or null when there is none.</summary>
    public static Vector3? Hit(World3D world, Vector3 from, Vector3 to)
    {
        if (world == null || !world.HasMeta(Meta) || world.GetMeta(Meta).AsGodotObject() is not NpcGroundSpace s || !s.space.IsValid)
            return null;
        s.ray.Query.From = from;
        s.ray.Query.To = to;
        var hit = PhysicsServer3D.SpaceGetDirectState(s.space).IntersectRay(s.ray.Query);
        return hit.Count == 0 ? null : (Vector3)hit["position"];
    }
}
