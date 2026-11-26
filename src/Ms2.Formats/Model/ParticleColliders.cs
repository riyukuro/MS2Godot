using System.Numerics;

namespace Ms2.Formats.Model;

/// <summary>The particle colliders kernel: NiPSSimulatorCollidersKernel (NiPSSimulatorCollidersKernel.cpp) runs after the
/// forces and before the final step. For each particle it finds the collider hit first in the time from the particle's
/// last update to now, moves the particle to the hit point, takes the bounced velocity and sets the particle's last
/// update time to the hit's time; then it tries the colliders not yet hit, until none is. The final step moves the
/// particle by its velocity times the time left (NiPSSimulatorKernelHelpers::UpdateParticlePosition) and ages it by the
/// same time (UpdateParticleAgeDeath), so a particle that bounced ages by less than the step in that step. Floats, like
/// the kernel. Allocates nothing.</summary>
public static class ParticleColliders
{
    /// <summary>NiPSSimulatorKernelDefinitions' NIPSKERNEL_EPSILON (NiPSKernelDefinitions.h:78).</summary>
    public const float KernelEpsilon = 0.0001f;
    /// <summary>DetectPlanarCollision's sfCollisionEpsilon, 2^-21 (NiPSColliderHelpers.inl).</summary>
    public static readonly float PlaneEpsilon = MathF.Pow(2f, -21f);

    /// <summary>One collider at one time, in the particles' space. A plane (NiPSPlanarCollider::Update,
    /// NiPSPlanarCollider.cpp:72-128) goes through the collider object's position with the normal
    /// rotation * (X axis x Y axis), unitized; its axes are turned by the rotation (not unitized) and its half width and
    /// height are scaled by the object's scale and squared. A sphere (NiPSSphericalCollider::Update,
    /// NiPSSphericalCollider.cpp:80-92) sits at the object's position with its radius times the object's scale.</summary>
    public struct Collider
    {
        public bool Sphere;
        public Vector3 Normal, Pos, RotX, RotY;
        public float Constant, HalfSqWidth, HalfSqHeight, Radius, RadiusSq, Bounce;
        public bool Die;

        /// <param name="rotX">The plane's X axis turned by the collider object's rotation.</param>
        /// <param name="rotY">Its Y axis, the same way. As the rotation keeps the cross product, the normal is
        /// rotX x rotY, unitized.</param>
        public static Collider Plane(Vector3 pos, Vector3 rotX, Vector3 rotY, float scale, float width, float height, float bounce, bool die)
        {
            var n = Vector3.Normalize(Vector3.Cross(rotX, rotY));
            float hw = width * scale * 0.5f, hh = height * scale * 0.5f;
            return new Collider
            {
                Normal = n, Pos = pos, Constant = Vector3.Dot(n, pos), RotX = rotX, RotY = rotY,
                HalfSqWidth = hw * hw, HalfSqHeight = hh * hh, Bounce = bounce, Die = die,
            };
        }

        public static Collider Ball(Vector3 pos, float scale, float radius, float bounce, bool die)
        {
            float r = radius * scale;
            return new Collider { Sphere = true, Pos = pos, Radius = r, RadiusSq = r * r, Bounce = bounce, Die = die };
        }

        /// <summary>Detect for this collider's kind.</summary>
        public readonly bool Detect(ref float tHit, out Vector3 hit, float t0, Vector3 pos, Vector3 vel) =>
            Sphere ? DetectSphere(ref tHit, out hit, t0, pos, vel) : DetectPlane(ref tHit, out hit, t0, pos, vel);

        /// <summary>NiPSColliderHelpers::DetectSphericalCollision: a particle outside the sphere hits it where its line
        /// enters it, one inside where its line leaves it; one on the surface (within the kernel epsilon) does not hit.</summary>
        public readonly bool DetectSphere(ref float tHit, out Vector3 hit, float t0, Vector3 pos, Vector3 vel)
        {
            hit = default;
            var d = pos - Pos;
            float diff = d.LengthSquared() - RadiusSq;
            bool inside = -diff > KernelEpsilon;
            if (!inside && !(diff > KernelEpsilon)) return false;
            float time = -Vector3.Dot(vel, d);
            if (!inside && !(time > 0)) return false;
            float speedSq = vel.LengthSquared();
            time /= speedSq;
            float closestSq = (pos + time * vel - Pos).LengthSquared();
            if (!inside && !(closestSq < RadiusSq)) return false;
            float halfChord = MathF.Sqrt((RadiusSq - closestSq) / speedSq);
            time = inside ? time + halfChord : time - halfChord;
            if (!(time < tHit - t0)) return false;
            tHit = t0 + time; hit = pos + time * vel;
            return true;
        }

        public readonly float Distance(Vector3 p) => Vector3.Dot(Normal, p) - Constant;

        readonly bool Inside(Vector3 p)
        {
            var d = p - Pos;
            var x = Vector3.Dot(RotX, d) * RotX;
            var y = Vector3.Dot(RotY, d) * RotY;
            return x.LengthSquared() < HalfSqWidth && y.LengthSquared() < HalfSqHeight;
        }

        /// <summary>NiPSColliderHelpers::DetectPlanarCollision: whether the particle at <paramref name="pos"/> moving at
        /// <paramref name="vel"/> from <paramref name="t0"/> reaches the plane, within its width and height, before
        /// <paramref name="tHit"/>; when it does, tHit becomes the hit's time and <paramref name="hit"/> its point.</summary>
        public readonly bool DetectPlane(ref float tHit, out Vector3 hit, float t0, Vector3 pos, Vector3 vel)
        {
            hit = default;
            float dist = Distance(pos);
            // moving away from the plane, or along it
            if (Vector3.Dot(vel, Normal) * dist > 0f) return false;
            if (MathF.Abs(dist) < PlaneEpsilon)
            {
                if (!Inside(pos)) return false;
                tHit = t0; hit = pos;
                return true;
            }
            float dt = tHit - t0;
            float along = Vector3.Dot(Normal, dt * vel);
            float sum = dist + along;
            if (MathF.Abs(sum) < PlaneEpsilon) sum = 0f;
            if (dist * sum >= 0f) return false;
            float toPlane = -dist * dt / along;
            var p = pos + toPlane * vel;
            // a hit point that rounding put past the plane is pulled back to the particle's side
            if (Distance(p) * dist < 0f)
            {
                float m = MathF.Max(MathF.Abs(p.X), MathF.Max(MathF.Abs(p.Y), MathF.Abs(p.Z)));
                p += (dist < 0f ? -m : m) * PlaneEpsilon * Normal;
            }
            if (!Inside(p)) return false;
            tHit = t0 + toPlane; hit = p;
            return true;
        }

        /// <summary>NiPSColliderHelpers::HandlePlanarCollision / HandleSphericalCollision: the velocity mirrored about the
        /// plane's normal, or the sphere's normal at the hit point, times the bounce. A particle that hits a plane at the
        /// step's end with a speed along the normal below the kernel epsilon only loses that speed.</summary>
        public readonly Vector3 Bounced(Vector3 vel, Vector3 hit, float tHit, float now)
        {
            if (Sphere)
            {
                var n = (hit - Pos) / Radius;
                var o = Vector3.Dot(vel, n) * n;
                return (vel - (o + o)) * Bounce;
            }
            float dot = Vector3.Dot(vel, Normal);
            var off = dot * Normal;
            if (tHit == now && dot < KernelEpsilon) return vel - off;
            return (vel - (off + off)) * Bounce;
        }
    }

    /// <summary>The kernel's loop for one particle over the colliders in <paramref name="colliders"/>[0..count): updates
    /// <paramref name="pos"/>, <paramref name="vel"/> and <paramref name="tLast"/> (the particle's last update time, from
    /// 0 at the step's start to <paramref name="now"/>) and tells whether a collider that kills on contact was hit. Each
    /// collider is hit at most once per step; the hit time is not reset between tries, as in the kernel
    /// (fCollisionTime, NiPSSimulatorCollidersKernel.cpp). <paramref name="tried"/> is scratch with room for count.</summary>
    public static bool Step(ReadOnlySpan<Collider> colliders, Span<int> tried, ref Vector3 pos, ref Vector3 vel, ref float tLast, float now)
    {
        int left = colliders.Length;
        for (int i = 0; i < left; i++) tried[i] = i;
        float tHit = now;
        bool die = false;
        while (left > 0)
        {
            int best = -1;
            Vector3 hit = default;
            for (int j = 0; j < left; j++)
                if (colliders[tried[j]].Detect(ref tHit, out var h, tLast, pos, vel)) { best = j; hit = h; }
            if (best < 0) break;
            ref readonly var c = ref colliders[tried[best]];
            vel = c.Bounced(vel, hit, tHit, now);
            pos = hit;
            tLast = tHit;
            die |= c.Die;
            tried[best] = tried[--left];
        }
        return die;
    }
}
