using System.Collections.Generic;
using Godot;

namespace Ms2Godot;

/// <summary>
/// The field's triangle meshes again, double-sided, in a physics space of their own that only overlap tests query.
/// PhysX's NxScene::checkOverlapCapsule is a plain shape intersection with no sides, while the CCT's sweeps and the
/// raycasts take the mesh triangles one-sided (see FieldBuilder.Model and RayQuery.CastRay). A Godot concave shape is one
/// or the other for every query, so the one-sided shapes stay in the world and these copies answer the overlap tests.
/// Built off the tree with the field; the space and its body exist while the node is in the tree.
/// </summary>
public sealed partial class TwoSidedSpace : Node3D
{
    const string Meta = "ms2_two_sided_space";

    readonly List<(Shape3D shape, Transform3D xform)> shapes = new();
    readonly PhysicsShapeQueryParameters3D query = new();
    Rid space, body;

    /// <summary>A double-sided copy of a one-sided mesh shape, at <paramref name="xform"/> in this node's space.</summary>
    public void Add(ConcavePolygonShape3D oneSided, Transform3D xform) =>
        shapes.Add((new ConcavePolygonShape3D { Data = oneSided.Data, BackfaceCollision = true }, xform));

    public override void _EnterTree()
    {
        space = PhysicsServer3D.SpaceCreate();
        PhysicsServer3D.SpaceSetActive(space, true);
        body = PhysicsServer3D.BodyCreate();
        PhysicsServer3D.BodySetMode(body, PhysicsServer3D.BodyMode.Static);
        PhysicsServer3D.BodySetSpace(body, space);
        var at = GlobalTransform;
        foreach (var (shape, xform) in shapes) PhysicsServer3D.BodyAddShape(body, shape.GetRid(), at * xform);
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

    /// <summary>Whether <paramref name="shape"/> at <paramref name="xform"/> (global) touches any mesh triangle, from either side.</summary>
    public static bool Overlaps(World3D world, Shape3D shape, Transform3D xform)
    {
        if (world == null || !world.HasMeta(Meta) || world.GetMeta(Meta).AsGodotObject() is not TwoSidedSpace s || !s.space.IsValid)
            return false;
        s.query.Shape = shape;
        s.query.Transform = xform;
        return PhysicsServer3D.SpaceGetDirectState(s.space).IntersectShape(s.query, 1).Count > 0;
    }
}
