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

    public PhysicsRayQueryParameters3D Set(Vector3 from, Vector3 to, Rid exclude)
    {
        query.From = from; query.To = to;
        Ex.Clear(); Ex.Add(exclude);
        query.Exclude = Ex;
        return query;
    }
}
