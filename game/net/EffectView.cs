using Godot;

namespace Ms2Godot.Net;

/// <summary>
/// The camera's actor EffectSkipMgr::IsOutOfView (@0x1411b86e0, effectskipmgr.cpp:132-151) measures an effect's view
/// distance from (CameraMgr FUN_140a38d60, :139-142): the own player. Places are in the client's coordinates, as the
/// effect's own (x, -z, y of the game's).
/// </summary>
public static class EffectView
{
    /// <summary>The own player's place; null without one, which skips nothing (IsOutOfView's no actor, :141).</summary>
    public static System.Numerics.Vector3? Viewer =>
        LiveSession.Instance?.OwnPlayer is { } pl && GodotObject.IsInstanceValid(pl) && pl.IsInsideTree() ? Client(pl.GlobalPosition) : null;

    /// <summary>A world place of the game in the client's coordinates.</summary>
    public static System.Numerics.Vector3 Client(Vector3 g) => new(g.X, -g.Z, g.Y);
}
