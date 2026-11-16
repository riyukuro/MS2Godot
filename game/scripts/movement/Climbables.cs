using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot.Movement;

/// <summary>
/// Ladder and rope entities of a field (models with the Ladder or Rope mixin), in Godot space.
/// PC::FUN_141659b80 tests each one: its base is Position + RotationToDirection(Rotation) * ExtrudeOffset, and it is
/// usable when Climbable is set.
/// </summary>
public sealed class Climbables
{
    public sealed class Climb
    {
        public string Name;
        public bool IsRope;
        public Vector3 Position;      // entity position, Godot space
        public float YawDeg;          // entity Rotation.z
        public float Extrude;         // ExtrudeOffset
        public bool Usable;           // Climbable
        public Vector3 Base => Position + Direction(YawDeg) * Extrude;
    }

    public readonly List<Climb> All = new();

    /// <summary>GameObject::RotationToDirection: -Y turned by -z degrees (MS2 axes), as a Godot vector.</summary>
    public static Vector3 Direction(float yawDeg)
    {
        float r = Mathf.DegToRad(yawDeg);
        return FieldBuilder.ToGodotPoint(new Vec3(Math.Sin(r), -Math.Cos(r), 0));
    }

    public static Climbables From(FieldMap map)
    {
        var c = new Climbables();
        foreach (var e in map.Entities)
        {
            bool ladder = e.HasMixin("Ladder") || e.ModelName == "Ladder";
            bool rope = e.HasMixin("Rope") || e.ModelName == "Rope";
            if (!ladder && !rope) continue;
            c.All.Add(new Climb
            {
                Name = e.Name,
                IsRope = rope && !ladder,
                Position = FieldBuilder.ToGodotPoint(e.Position),
                YawDeg = (float)e.RotationDeg.Z,
                Extrude = (float)e.Number("ExtrudeOffset"),
                Usable = e.Bool("Climbable"),
            });
        }
        return c;
    }

    /// <summary>
    /// PC::FUN_141659b80 for the ladder model: side 1 = from below / beside (probe 90 above the feet, 50 reach),
    /// side 2 = from the top (probe 75 below, 70 reach); the probe height must lie within 150 above the base.
    /// </summary>
    public Climb Find(Vector3 feet, int side, Climb sameYawAs = null)
    {
        var my = feet;
        float reach = MoveConfig.LadderReach;
        if (side == 2) { my.Y -= MoveConfig.LadderProbeDown; reach = MoveConfig.LadderReachDown; }
        else my.Y += MoveConfig.LadderProbeUp;
        foreach (var l in All)
        {
            if (l.IsRope) continue;   // rope (StateRope) is not ported
            var b = l.Base;
            if (new Vector2(my.X - b.X, my.Z - b.Z).Length() >= reach) continue;
            if (my.Y < b.Y || my.Y > b.Y + MoveConfig.LadderSpan) continue;
            if (!l.Usable) continue;
            // StateLadder::FUN_1417d8b20: a following segment must have the same rotation
            if (sameYawAs != null && l != sameYawAs && l.YawDeg != sameYawAs.YawDeg) continue;
            return l;
        }
        return null;
    }
}
