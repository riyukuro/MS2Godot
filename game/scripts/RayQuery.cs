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

    /// <summary>
    /// The nearest hit of a ray from <paramref name="from"/> to <paramref name="to"/> on a collider whose PropCollisionGroup
    /// bit is in <paramref name="groups"/> (a collider without one is group 0): a hit of another group is excluded and the
    /// ray cast again, up to 16 times. The hit's collider is its instance id; the "collider" key would make an object
    /// Variant (a finalizable disposer, 116 B a ray), so the group comes from <see cref="ColliderGroups"/> and a caller
    /// that needs the object takes it from the id.
    /// </summary>
    public Hit? Nearest(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to, Rid self, uint groups)
    {
        var q = Set(from, to, self);
        for (int i = 0; i < 16; i++)
        {
            using var hit = space.IntersectRay(q);
            if (hit.Count == 0) return null;
            ulong id = (ulong)hit[PhysicsKeys.ColliderId];
            if ((groups & (1u << ColliderGroups.Of(id))) != 0)
                return new Hit((Vector3)hit[PhysicsKeys.Position], (Vector3)hit[PhysicsKeys.Normal], id, (int)hit[PhysicsKeys.Shape]);
            Ex.Add((Rid)hit[PhysicsKeys.Rid]);
            q.Exclude = Ex;
        }
        return null;
    }

    public readonly record struct Hit(Vector3 Position, Vector3 Normal, ulong ColliderId, int Shape);
}

/// <summary>
/// A collider's PropCollisionGroup by its instance id, read once: an instance id is not reused in a session and the group
/// is set when the collider is built. Cleared when a field leaves the tree (Field._ExitTree), so it holds one field's
/// colliders at a time.
/// </summary>
static class ColliderGroups
{
    static readonly System.Collections.Generic.Dictionary<ulong, int> byId = new();

    public static int Of(ulong colliderId)
    {
        if (byId.TryGetValue(colliderId, out int g)) return g;
        g = GodotObject.InstanceFromId(colliderId) is Node n && n.HasMeta(FieldBuilder.GroupMeta) ? (int)n.GetMeta(FieldBuilder.GroupMeta) : 0;
        byId[colliderId] = g;
        return g;
    }

    /// <summary>A collider whose group changes at runtime (FieldBuilder.TriggerCollider.SetGroup, the Trigger packet's show/hide) updates its entry.</summary>
    public static void Set(ulong colliderId, int group) => byId[colliderId] = group;

    public static void Clear() => byId.Clear();
}
