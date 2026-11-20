using System.Collections.Generic;
using Godot;

namespace Ms2Godot;

/// <summary>
/// The field's NIF collision meshes again, in a physics space of their own that only overlap tests query. PhysX's
/// NxScene::checkOverlapCapsule is a plain shape intersection with no sides, while the CCT's sweeps and the raycasts take
/// the mesh and hull triangles one-sided (see FieldBuilder.Model and RayQuery.CastRay). A Godot concave shape is one or
/// the other for every query, so the one-sided shapes stay in the world and these copies (double-sided triangle meshes,
/// solid convex hulls; one per NIF mesh, shared by its placements) answer the overlap tests. Built off the tree with the
/// field; the space and its bodies exist while the node is in the tree. Each PropCollisionGroup gets a body on its own
/// layer (bit n = group n, as in the world), so a test passes the client's groups mask.
/// </summary>
public sealed partial class TwoSidedSpace : Node3D
{
    static readonly StringName Meta = "ms2_two_sided_space";

    readonly List<(Shape3D shape, Transform3D xform, int group)> shapes = new();
    readonly Dictionary<int, Rid> bodies = new();
    // the copies of a trigger mesh entity, on a body of their own whose layer FieldTriggers switches (see AddOwn)
    readonly List<(Shape3D shape, Transform3D xform, int own)> ownShapes = new();
    readonly List<(Rid body, uint layer)> owns = new();
    readonly PhysicsShapeQueryParameters3D query = new();
    Rid space;

    /// <summary>A mesh's overlap shape placed at <paramref name="xform"/> in this node's space, in collision group
    /// <paramref name="group"/>.</summary>
    public void Add(Shape3D shape, Transform3D xform, int group) => shapes.Add((shape, xform, group));

    /// <summary>A body of its own on <paramref name="layer"/>, for the copies of one entity whose collision group changes at
    /// run time (a trigger mesh, see world/FieldTriggers); its key for <see cref="AddOwn(int, Shape3D, Transform3D)"/> and
    /// <see cref="SetLayer"/>.</summary>
    public int AddOwn(uint layer)
    {
        owns.Add((default, layer));
        return owns.Count - 1;
    }

    /// <summary>A mesh's overlap shape on the own body <paramref name="own"/>.</summary>
    public void AddOwn(int own, Shape3D shape, Transform3D xform) => ownShapes.Add((shape, xform, own));

    /// <summary>The own body's layer (bit n = group n; 0: no test meets it).</summary>
    public void SetLayer(int own, uint layer)
    {
        var (body, _) = owns[own];
        owns[own] = (body, layer);
        if (body.IsValid) PhysicsServer3D.BodySetCollisionLayer(body, layer);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) { shapes.Clear(); ownShapes.Clear(); }
    }

    public override void _EnterTree()
    {
        space = PhysicsServer3D.SpaceCreate();
        PhysicsServer3D.SpaceSetActive(space, true);
        var at = GlobalTransform;
        foreach (var (shape, xform, group) in shapes)
        {
            if (!bodies.TryGetValue(group, out var body))
            {
                bodies[group] = body = PhysicsServer3D.BodyCreate();
                PhysicsServer3D.BodySetMode(body, PhysicsServer3D.BodyMode.Static);
                PhysicsServer3D.BodySetCollisionLayer(body, 1u << group);
                PhysicsServer3D.BodySetCollisionMask(body, 0);
                PhysicsServer3D.BodySetSpace(body, space);
            }
            PhysicsServer3D.BodyAddShape(body, shape.GetRid(), at * xform);
        }
        for (int i = 0; i < owns.Count; i++)
        {
            var body = PhysicsServer3D.BodyCreate();
            PhysicsServer3D.BodySetMode(body, PhysicsServer3D.BodyMode.Static);
            PhysicsServer3D.BodySetCollisionLayer(body, owns[i].layer);
            PhysicsServer3D.BodySetCollisionMask(body, 0);
            PhysicsServer3D.BodySetSpace(body, space);
            owns[i] = (body, owns[i].layer);
        }
        foreach (var (shape, xform, own) in ownShapes) PhysicsServer3D.BodyAddShape(owns[own].body, shape.GetRid(), at * xform);
        GetWorld3D().SetMeta(Meta, this);
    }

    public override void _ExitTree()
    {
        var world = GetWorld3D();
        if (world != null && world.HasMeta(Meta) && world.GetMeta(Meta).AsGodotObject() == this) world.RemoveMeta(Meta);
        foreach (var body in bodies.Values) PhysicsServer3D.FreeRid(body);
        bodies.Clear();
        for (int i = 0; i < owns.Count; i++)
        {
            if (owns[i].body.IsValid) PhysicsServer3D.FreeRid(owns[i].body);
            owns[i] = (default, owns[i].layer);
        }
        PhysicsServer3D.FreeRid(space);
        space = default;
    }

    /// <summary>Whether <paramref name="shape"/> at <paramref name="xform"/> (global) touches any mesh triangle of a group in
    /// <paramref name="groups"/>, from either side.</summary>
    public static bool Overlaps(World3D world, Shape3D shape, Transform3D xform, uint groups)
    {
        if (world == null || !world.HasMeta(Meta) || world.GetMeta(Meta).AsGodotObject() is not TwoSidedSpace s || !s.space.IsValid)
            return false;
        s.query.Shape = shape;
        s.query.Transform = xform;
        s.query.CollisionMask = groups;
        using var found = (Godot.Collections.Array)PhysicsServer3D.SpaceGetDirectState(s.space).IntersectShape(s.query, 1);   // the underlying Array is tracked
        return found.Count > 0;
    }
}
