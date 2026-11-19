using Godot;

namespace Ms2Godot;

/// <summary>
/// The keys of the dictionaries the physics server returns (IntersectRay, GetRestInfo), made once. An indexer given a
/// string converts it to a new Variant on every read; a static Variant does not.
/// </summary>
public static class PhysicsKeys
{
    public static readonly Variant Position = "position";
    public static readonly Variant Normal = "normal";
    public static readonly Variant Point = "point";
    public static readonly Variant Collider = "collider";
    public static readonly Variant ColliderId = "collider_id";
    public static readonly Variant Rid = "rid";
    public static readonly Variant Shape = "shape";
}
