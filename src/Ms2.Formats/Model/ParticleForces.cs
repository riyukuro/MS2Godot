using System.Numerics;

namespace Ms2.Formats.Model;

/// <summary>The particle forces kernel: NiPSSimulatorForcesKernel::Execute (NiPSSimulatorForcesKernel.cpp:44-237) applies the
/// active forces one after another in the step's order, each taking the velocity the previous one left
/// (pkIVelocities = pkOVelocities after the first force, :232-236), against the particle's position at the start of the
/// step. dt is the time since the particle's last update. The force's position and direction are in the particle system's
/// space: the force object's transform relative to the system, the direction its rotation times the force's axis,
/// unitized (NiPSGravityForce::Update, NiPSGravityForce.cpp:65-79; NiPSBombForce::Update, NiPSBombForce.cpp:68-77; NiPSDragForce::Update, NiPSDragForce.cpp:66-75).
/// Floats, like the kernel.</summary>
public static class ParticleForces
{
    /// <summary>NiPSForceHelpers::ApplyBombForce (NiPSForceHelpers.inl:29-112).</summary>
    public static Vector3 Bomb(Vector3 vel, Vector3 pos, Vector3 bombPos, Vector3 bombDir, float deltaV, float decay, int decayType, int symmType, float dt)
    {
        Vector3 dir = pos - bombPos;
        float dist = dir.Length();
        if (decayType != 0 && dist > decay) return vel;
        Vector3 force;
        switch (symmType)
        {
            case 0: force = dir / dist; break;                              // SYMM_SPHERICAL
            case 2:                                                         // SYMM_PLANAR
                dist = Vector3.Dot(bombDir, dir);
                if (dist < 0f) { dist = -dist; force = -bombDir; } else force = bombDir;
                break;
            case 1:                                                         // SYMM_CYLINDRICAL
                dist = Vector3.Dot(bombDir, dir);
                force = dir - dist * bombDir;
                dist = force.Length();
                if (dist != 0f) force /= dist;
                break;
            default: return vel;
        }
        float fDecay = 1f;
        if (decayType == 1) fDecay = (decay - dist) / decay;                // DECAY_LINEAR
        else if (decayType == 2) fDecay = MathF.Exp(-dist / decay);         // DECAY_EXPONENTIAL
        return vel + fDecay * deltaV * dt * force;
    }

    /// <summary>NiPSForceHelpers::ApplyPlanarGravityForce / ApplySphericalGravityForce (NiPSForceHelpers.inl:760-963), without
    /// turbulence. The strength is Strength * 1.6 (:770, :878). Planar (forceType 0) pulls along the direction; with a
    /// decay it fades as exp(-decay * |dir . (gravPos - pos)|). Spherical (1) pulls toward gravPos, unitized; with a decay
    /// it fades as exp(-decay * distance).</summary>
    public static Vector3 Gravity(Vector3 vel, Vector3 pos, Vector3 gravPos, Vector3 gravDir, float strength, float decay, int forceType, float dt)
    {
        float s = strength * 1.6f;
        Vector3 dir;
        float fDecay = 1f;
        if (forceType == 0)
        {
            dir = gravDir;
            if (decay != 0f) fDecay = MathF.Exp(-decay * MathF.Abs(Vector3.Dot(gravDir, gravPos - pos)));
        }
        else if (forceType == 1)
        {
            dir = gravPos - pos;
            float dist = dir.Length();
            if (dist > 0f) dir /= dist;                                     // NiPoint3::Unitize leaves a zero vector zero
            if (decay != 0f) fDecay = MathF.Exp(-decay * dist);
        }
        else return vel;
        return vel + dir * (s * fDecay * dt);
    }

    /// <summary>NiPSForceHelpers::ApplyDragForce (NiPSForceHelpers.inl:115-164): the velocity's part along the drag direction
    /// shrinks by Percentage per 1/30 s, linearly in time, never past zero; the percentage fades from Range to RangeFalloff.</summary>
    public static Vector3 Drag(Vector3 vel, Vector3 pos, Vector3 dragPos, Vector3 dragDir, float percentage, float range, float rangeFalloff, float dt)
    {
        float lenSquared = dragDir.LengthSquared();
        float distance = (pos - dragPos).Length();
        float pct = percentage;
        if (distance > range)
        {
            if (distance >= rangeFalloff) return vel;
            pct = (1f - (distance - range) / (rangeFalloff - range)) * pct;
        }
        float proj = Vector3.Dot(vel, dragDir);
        float deltaT = dt / 0.0333333f;
        if (pct * deltaT > 1f) return vel + (-proj / lenSquared) * dragDir;
        return vel + (-pct * deltaT * (proj / lenSquared)) * dragDir;
    }
    /// <summary>One active force for a step, in the particles' space: Kind picks the helper; Strength (gravity),
    /// DeltaV (bomb) and Percentage (drag) are its amount; Decay, Range and Falloff its distances.</summary>
    public struct Step
    {
        public ForceKind Kind; public Vector3 Pos, Dir;
        public float Strength, DeltaV, Percentage, Decay, Range, Falloff; public int DecayType, SymmType, ForceType;
    }

    /// <summary>The kernel's loop for one particle (NiPSSimulatorForcesKernel.cpp:44-237): each force in the list's order
    /// on the velocity the one before left, all against the step's start position.</summary>
    public static Vector3 Apply(Vector3 vel, Vector3 pos, List<Step> forces, float dt)
    {
        for (int i = 0; i < forces.Count; i++)
        {
            var f = forces[i];
            vel = f.Kind switch
            {
                ForceKind.Gravity => Gravity(vel, pos, f.Pos, f.Dir, f.Strength, f.Decay, f.ForceType, dt),
                ForceKind.Bomb => Bomb(vel, pos, f.Pos, f.Dir, f.DeltaV, f.Decay, f.DecayType, f.SymmType, dt),
                ForceKind.Drag => Drag(vel, pos, f.Pos, f.Dir, f.Percentage, f.Range, f.Falloff, dt),
                _ => vel,
            };
        }
        return vel;
    }
}
