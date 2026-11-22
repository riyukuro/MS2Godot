using System.Numerics;

namespace Ms2.Formats.World;

/// <summary>
/// A skill motion's push cylinder (SkillMotionData m_fPushCylinderVelH .. m_fPushCylinderHeight, +0xcc..+0xdc,
/// skilloverlapcompare.h:233-237; read by SkillMotionData::Load from the &lt;motion&gt;'s "pushCylinderVelH", "pushCylinderVelV",
/// "pushCylinderOuter", "pushCylinderInner" and "pushCylinderHeight", skillleveldata.cpp:1197-1201).
/// </summary>
/// <param name="VelH">The horizontal speed away from the centre (&gt; 0) or toward it (&lt; 0), units per second.</param>
/// <param name="VelV">The vertical speed, units per second.</param>
/// <param name="Outer">The outer radius.</param>
/// <param name="Inner">The inner radius.</param>
/// <param name="Height">How far above the centre the cylinder reaches.</param>
public readonly record struct PushCylinder(float VelH, float VelV, float Outer, float Inner, float Height);

/// <summary>
/// The force zones around an actor, as WorldService::FUN_141bc8420 (@0x141bc8420, worldservice_1.cpp:5503-5608) finds them
/// for PCBase::vfunc_68 (pcbase.cpp:1771-1775): the region skill objects' first points with their motion 0's push cylinder,
/// then the npcs in the skill state (0x10) at their positions with their cast's motion's.
/// </summary>
public static class ForceZone
{
    /// <summary>One zone: its centre (field units and axes, Z up) and its cylinder.</summary>
    public readonly record struct Source(Vector3 Centre, PushCylinder Cylinder);

    /// <summary>The 1e-6 test of IsNearlyEqual.</summary>
    static bool Nearly(float a, float b) => MathF.Abs(a - b) < 1e-6f;

    /// <summary>
    /// FUN_141bc8420: false for a dead actor or with no zone. A zone is skipped when its horizontal and vertical speeds are both
    /// nearly 0, or its outer radius or height is; it counts when the actor's horizontal distance from the centre is within
    /// inner..outer, the actor's Z is at most the centre's plus the height and at least the centre's minus 105. The nearest
    /// zone wins (a strictly nearer one replaces it, so of equal ones the first met stays: the region objects in their map's
    /// order, here the ids' insertion order where the client's m_3a0 is a hash map, then the npcs).
    /// Its horizontal speed &lt; 0 pulls toward the centre by speed * dt (when the actor is past the inner radius), &gt; 0
    /// pushes away by the least of speed * dt and the way left to the outer radius. A vertical speed adds speed * dt up,
    /// for a rising one at most to the cylinder's top (none above it), and sets <paramref name="lifted"/>; a falling one adds
    /// its speed * dt (down) and leaves <paramref name="lifted"/> unset.
    /// </summary>
    /// <param name="position">The actor's position (PCBase::GetPosition, the feet), field units and axes.</param>
    /// <param name="velocity">The move this frame (in field units; vfunc_68 scales it by 0.01 into the controller's).</param>
    /// <param name="lifted">Set when a rising zone acted: vfunc_68's bNoVelocity, the frame's own velocity is not added.</param>
    public static bool Step(Vector3 position, bool dead, float dt, RegionSkillObjects regions, IReadOnlyList<Source> npcs,
        out Vector3 velocity, out bool lifted)
    {
        velocity = Vector3.Zero;
        lifted = false;
        if (dead) return false;
        float best = float.MaxValue;
        PushCylinder? zone = null;
        var zonePos = Vector3.Zero;
        if (regions != null)
            foreach (var o in regions.Values)
            {
                if (o.Attr == null) continue;
                // GetFirstPoint (regionskillobject.cpp:301-306): the first point, the zero vector without points
                var pt = o.Points is { Length: > 0 } ps ? new Vector3((float)ps[0].X, (float)ps[0].Y, (float)ps[0].Z) : Vector3.Zero;
                Consider(position, pt, o.Attr.PushCylinder, ref best, ref zone, ref zonePos);
            }
        if (npcs != null)
            for (int i = 0; i < npcs.Count; i++) Consider(position, npcs[i].Centre, npcs[i].Cylinder, ref best, ref zone, ref zonePos);
        if (zone is not { } c) return false;
        bool changed = false;
        if (0f > c.VelH)
        {
            if (best > c.Inner)
            {
                var dir = Away(position, zonePos);
                float amount = c.VelH * dt < best - c.Inner ? c.VelH * dt : best - c.Inner;
                velocity = dir * amount;
                changed = true;
            }
        }
        else if (0f < c.VelH)
        {
            var dir = Away(position, zonePos);
            float amount = c.VelH * dt < c.Outer - best ? c.VelH * dt : c.Outer - best;
            velocity = dir * amount;
            changed = true;
        }
        if (0f != c.VelV)
        {
            float lift = 0f;
            if (0f < c.VelV)
            {
                float room = zonePos.Z + c.Height - position.Z;
                lift = room < 0f ? 0f : room;
                lifted = true;
            }
            velocity += Vector3.UnitZ * (c.VelV * dt < lift ? c.VelV * dt : lift);
            changed = true;
        }
        return changed;
    }

    static void Consider(Vector3 position, Vector3 centre, PushCylinder p, ref float best, ref PushCylinder? zone, ref Vector3 zonePos)
    {
        if (Nearly(p.VelH, 0f) && Nearly(p.VelV, 0f)) return;
        if (Nearly(p.Outer, 0f) || Nearly(p.Height, 0f)) return;
        float dx = centre.X - position.X, dy = centre.Y - position.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);   // GetDistance2D
        if (dist > p.Outer) return;
        if (dist < p.Inner) return;
        if (position.Z > centre.Z + p.Height) return;
        if (position.Z + 105f < centre.Z) return;
        if (dist < best)
        {
            best = dist;
            zone = p;
            zonePos = centre;
        }
    }

    /// <summary>(position - centre) with z 0, unitized as NiPoint3::Unitize does (a length at most 1e-6 gives zero).</summary>
    static Vector3 Away(Vector3 position, Vector3 centre)
    {
        var d = position - centre;
        d.Z = 0f;
        float len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        return len > 1e-6f ? d / len : Vector3.Zero;
    }
}
