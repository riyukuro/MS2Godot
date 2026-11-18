using Godot;

namespace Ms2Godot;

/// <summary>
/// One ray query and one exclude list, reused by a caller that casts every frame: a new RefCounted query and Array per
/// cast is finalizable garbage (the way BlobShadow already reuses its own). Every field a caller varies is set on each
/// <see cref="Set"/>; the rest stay at the defaults a fresh query has, so a cast gives the result a new one would.
/// </summary>
sealed class RayQuery
{
    readonly PhysicsRayQueryParameters3D query = new();
    /// <summary>The exclude list; add to it and assign <see cref="Query"/>.Exclude again (the property takes a copy).</summary>
    public readonly Godot.Collections.Array<Rid> Ex = new();
    public PhysicsRayQueryParameters3D Query => query;

    /// <summary>
    /// A stand-in for CPhysXHelper::CastRay (physxhelper.cpp:187-232): scene raycastAllShapes, which has no cull option.
    /// A triangle mesh is double-sided for rays only with NX_MESH_DOUBLE_SIDED (Nxp.h:247), and NiPhysXMeshDesc copies
    /// its Mesh Flags into the triangle mesh desc's meshFlags (ToTriMeshDesc, NiPhysXMeshDesc.cpp:359). Every
    /// NiPhysXMeshDesc in the game data has Mesh Flags 0 (a survey of the fields' NIFs, not something the code fixes), so a
    /// ray passes through a mesh triangle from behind. The field's mesh shapes are one-sided in the PhysX winding.
    /// The groups mask is the caller's (RayCastFromPoint's nMask), as Godot collision layer bits: bit n = group n
    /// (MoveConfig.NpcLayer). Without one the cast tests every layer.
    /// </summary>
    public static RayQuery CastRay(uint groups = uint.MaxValue)
    {
        var r = new RayQuery();
        r.query.HitBackFaces = false;
        r.query.CollisionMask = groups;
        return r;
    }

    public PhysicsRayQueryParameters3D Set(Vector3 from, Vector3 to, Rid exclude)
    {
        query.From = from; query.To = to;
        Ex.Clear(); Ex.Add(exclude);
        query.Exclude = Ex;
        return query;
    }
}
