using System.Numerics;
using Ms2.Net.Packets;

namespace Ms2.Ui.Skills;

/// <summary>
/// A posed CBoxCollision (client/ms2/boxcollision.cpp): local bounds min (-size.x/2, -add.y, -add.z), max (size.x/2, size.y,
/// size.z) (vfunc_4 @0x140adab30), posed by a position and Euler degrees (vfunc_3 @0x140ada620) into an oriented box whose
/// centre is the bounds centre with y negated, so the box reaches forward (-y) from the position. Client space: z up.
/// </summary>
public struct HitBox
{
    public Vector3 Center, Extents;
    /// <summary>The rotation's columns: the box's local x, y and z axes in the world.</summary>
    public Vector3 AxisX, AxisY, AxisZ;
    /// <summary>The lowest and highest corner z (FUN_140adad80 @0x140adad80, FUN_140adad00 @0x140adad00).</summary>
    public float MinZ, MaxZ;

    /// <summary>
    /// vfunc_3 @0x140ada620 (boxcollision.cpp:46) at scale 1: M = rotZ(rot.z), times rotX(rot.x) when rot.x is not 0 (NxMat33's
    /// counter-clockwise rotations); the centre is pos + M * (bounds centre with y negated) (m_ptDir is 0 from the ctor); the 8
    /// corners give the z range.
    /// </summary>
    public static HitBox Pose(Vector3 size, Vector3 add, Vector3 pos, Vector3 rotDegrees)
    {
        float z = rotDegrees.Z * 3.1415927f / 180f, cz = MathF.Cos(z), sz = MathF.Sin(z);
        var ax = new Vector3(cz, sz, 0);
        var ay = new Vector3(-sz, cz, 0);
        var az = Vector3.UnitZ;
        if (!(rotDegrees.X > -1.1920929e-07f && rotDegrees.X < 1.1920929e-07f))
        {
            float x = rotDegrees.X * 3.1415927f / 180f, cx = MathF.Cos(x), sx = MathF.Sin(x);
            // M * rotX: column 1 = M * (0, cx, sx), column 2 = M * (0, -sx, cx)
            var y1 = ay * cx + az * sx;
            var z1 = ay * -sx + az * cx;
            ay = y1;
            az = z1;
        }
        return PoseAxes(size, add, pos, ax, ay, az);
    }

    /// <summary>CBoxCollision::vfunc_2 @0x140ada910 at scale 1: the same box posed by a rotation given as its columns
    /// <paramref name="ax"/>, <paramref name="ay"/> and <paramref name="az"/> (the arrow fill's matAim).</summary>
    public static HitBox PoseAxes(Vector3 size, Vector3 add, Vector3 pos, Vector3 ax, Vector3 ay, Vector3 az)
    {
        var min = new Vector3(-size.X / 2f, -add.Y, -add.Z);
        var max = new Vector3(size.X / 2f, size.Y, size.Z);
        var local = (min + max) * 0.5f;
        local.Y = -local.Y;
        var b = new HitBox
        {
            Extents = (max - min) * 0.5f,
            AxisX = ax, AxisY = ay, AxisZ = az,
            Center = pos + ax * local.X + ay * local.Y + az * local.Z,
        };
        // NxComputeBoxPoints: centre +- each axis * extent
        b.MinZ = float.MaxValue;
        b.MaxZ = -float.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            float cz8 = b.Center.Z + ((i & 1) != 0 ? 1 : -1) * ax.Z * b.Extents.X + ((i & 2) != 0 ? 1 : -1) * ay.Z * b.Extents.Y
                + ((i & 4) != 0 ? 1 : -1) * az.Z * b.Extents.Z;
            if (cz8 < b.MinZ) b.MinZ = cz8;
            if (b.MaxZ < cz8) b.MaxZ = cz8;
        }
        return b;
    }

    Vector3 Axis(int i) => i == 0 ? AxisX : i == 1 ? AxisY : AxisZ;
    float Extent(int i) => i == 0 ? Extents.X : i == 1 ? Extents.Y : Extents.Z;

    /// <summary>
    /// NxBoxBoxIntersect(a, b, fullTest = true): the separating axis test over both boxes' axes and their 9 cross products.
    /// STILL-OPEN: the PhysX body is not in the restored source; the 1e-6 added to |R| is the usual guard for parallel axes.
    /// </summary>
    public static bool Intersect(in HitBox a, in HitBox b)
    {
        Span<float> r = stackalloc float[9], ar = stackalloc float[9];
        var d = b.Center - a.Center;
        Span<float> t = stackalloc float[3];
        for (int i = 0; i < 3; i++)
        {
            var ai = a.Axis(i);
            t[i] = Vector3.Dot(d, ai);
            for (int j = 0; j < 3; j++)
            {
                r[i * 3 + j] = Vector3.Dot(ai, b.Axis(j));
                ar[i * 3 + j] = MathF.Abs(r[i * 3 + j]) + 1e-6f;
            }
        }
        for (int i = 0; i < 3; i++)
        {
            float rb = b.Extents.X * ar[i * 3] + b.Extents.Y * ar[i * 3 + 1] + b.Extents.Z * ar[i * 3 + 2];
            if (MathF.Abs(t[i]) > a.Extent(i) + rb) return false;
        }
        for (int j = 0; j < 3; j++)
        {
            float ra = a.Extents.X * ar[j] + a.Extents.Y * ar[3 + j] + a.Extents.Z * ar[6 + j];
            float tj = t[0] * r[j] + t[1] * r[3 + j] + t[2] * r[6 + j];
            if (MathF.Abs(tj) > ra + b.Extent(j)) return false;
        }
        for (int i = 0; i < 3; i++)
        {
            int i1 = (i + 1) % 3, i2 = (i + 2) % 3;
            for (int j = 0; j < 3; j++)
            {
                int j1 = (j + 1) % 3, j2 = (j + 2) % 3;
                float ra = a.Extent(i1) * ar[i2 * 3 + j] + a.Extent(i2) * ar[i1 * 3 + j];
                float rb = b.Extent(j1) * ar[i * 3 + j2] + b.Extent(j2) * ar[i * 3 + j1];
                float tl = t[i2] * r[i1 * 3 + j] - t[i1] * r[i2 * 3 + j];
                if (MathF.Abs(tl) > ra + rb) return false;
            }
        }
        return true;
    }

    /// <summary>NxSegmentOBBSqrDist: the squared distance from the segment a-b to this box's axes and extents placed at
    /// <paramref name="center"/>. The squared distance to a convex box is convex along the segment, so it is minimised by a
    /// ternary search. STILL-OPEN: the restored source does not hold NxSegmentOBBSqrDist; this is its defined result
    /// (the minimum over the segment), not its code.</summary>
    public float SegmentSqrDist(Vector3 a, Vector3 b, Vector3 center)
    {
        var self = this;
        float At(float t) => self.PointSqrDist(a + (b - a) * t, center);
        float lo = 0f, hi = 1f;
        for (int i = 0; i < 40; i++)
        {
            float m1 = lo + (hi - lo) / 3f, m2 = hi - (hi - lo) / 3f;
            if (At(m1) <= At(m2)) hi = m2; else lo = m1;
        }
        return Math.Min(At((lo + hi) * 0.5f), Math.Min(At(0f), At(1f)));
    }

    /// <summary>NxPointOBBSqrDist(point, centre, extents, rot): the squared distance from a point to the box (0 inside).</summary>
    public float PointSqrDist(Vector3 p, Vector3 center)
    {
        var d = p - center;
        float sum = 0;
        for (int i = 0; i < 3; i++)
        {
            float l = Vector3.Dot(d, Axis(i)), e = Extent(i), o = l < -e ? l + e : l > e ? l - e : 0;
            sum += o * o;
        }
        return sum;
    }
}

/// <summary>
/// A posed CFrustumCollision (frustumcollision.cpp): a truncated pyramid from a near width to a far width over a depth, twice
/// the half height tall. CSkillCollisionManager::GetCached @0x141754580 (skillcollisionmanager.cpp:137) gives the near half
/// width (width / 2), the half height (height / 2), the far half width (endWidth / 2) and the depth (distance + zOffset);
/// vfunc_4 @0x141223bc0 adds rangeAdd (depth + add.y, half height + add.z / 2) and derives the cone, vfunc_3 @0x141222f50
/// poses it at a position and a z rotation.
/// </summary>
public sealed class HitFrustum
{
    readonly Vector3[] corners = new Vector3[8];
    Vector3 origin, apex, forward;
    float depth, apexDist, cosHalfAngle;
    // the two side faces' apex-to-far-corner edges (m_aEdgeA, m_aEdgeB) and the sides' ground projections (m_akEdge2)
    Vector3 a0, a1, a2, b0, b1, b2;

    public static HitFrustum Pose(SkillRangeShape r, float zOffset, Vector3 pos, Vector3 rotDegrees)
    {
        float near = r.Width * 0.5f, height = r.Height * 0.5f, far = r.EndWidth * 0.5f, depth = r.Distance + zOffset;
        // vfunc_4 @0x141223bc0
        depth += r.Add.Y;
        height += r.Add.Z / 2f;
        float apexDist = depth * near / (far - near);
        float halfAngle = MathF.Atan2(far, depth + apexDist);
        // vfunc_3 @0x141222f50
        SkillHit.Axes(rotDegrees.Z, out var fwd, out var right);
        fwd = Vector3.Normalize(fwd);
        right = Vector3.Normalize(right);
        var f = new HitFrustum { depth = depth, apexDist = apexDist, cosHalfAngle = MathF.Cos(halfAngle), forward = fwd };
        f.origin = pos + fwd * -r.Add.Y + new Vector3(0, 0, -r.Add.Z);
        f.apex = f.origin + fwd * -apexDist;
        var o = f.origin;
        var up = new Vector3(0, 0, height * 2f);
        f.corners[0] = o + right * -near + up;
        f.corners[1] = o + right * -near;
        f.corners[2] = o + right * near;
        f.corners[3] = o + right * near + up;
        var farCenter = o + fwd * depth;
        f.corners[4] = farCenter + right * -far + up;
        f.corners[5] = farCenter + right * -far;
        f.corners[6] = farCenter + right * far;
        f.corners[7] = farCenter + right * far + up;
        f.a0 = f.apex; f.a1 = f.corners[4]; f.a2 = f.corners[7];
        f.b0 = f.apex; f.b1 = f.corners[5]; f.b2 = f.corners[6];
        f.SetZSpan();
        return f;
    }

    float maxZ, minZ;

    /// <summary>CFrustumCollision::GetMaxZ @0x141224820 (frustumcollision.cpp:204) seeds with FLT_MIN, the smallest positive
    /// float, so a frustum wholly below z 0 reads a max of about 0; GetMinZ @0x1412248a0 seeds with FLT_MAX.</summary>
    void SetZSpan()
    {
        maxZ = 1.17549435e-38f;
        minZ = float.MaxValue;
        foreach (var c in corners) { if (maxZ < c.Z) maxZ = c.Z; if (c.Z < minZ) minZ = c.Z; }
    }

    /// <summary>CFrustumCollision::ContainsPoint @0x141223d80: in front of the near plane and short of the depth, and the
    /// ground direction from the apex within the half angle of the forward.</summary>
    public bool Contains(Vector3 pt)
    {
        float d = Vector3.Dot(forward, pt - origin);
        if (d <= 0f || depth <= d) return false;
        var dir = pt - apex;
        dir.Z = 0f;
        float len = dir.Length();
        if (len > 0f) dir /= len;
        return Vector3.Dot(forward, dir) > cosHalfAngle;
    }

    /// <summary>The CBaseCollision::TestShape / Intersect frustum against box case (basecollision.cpp:607, :738): the z spans
    /// overlap, then the box centre or a corner inside the frustum, a frustum corner inside the box, or a side edge crossing
    /// the box's bottom face (<see cref="IntersectsBox"/>).</summary>
    public bool Intersects(in HitBox box)
    {
        if (!(maxZ > box.MinZ)) return false;
        if (!(minZ < box.MaxZ)) return false;
        if (Contains(box.Center)) return true;
        Span<Vector3> bc = stackalloc Vector3[8];
        BoxCorners(box, bc);
        foreach (var c in bc) if (Contains(c)) return true;
        foreach (var c in corners) if (BoxContains(box, c)) return true;
        return IntersectsBox(bc);
    }

    /// <summary>NxComputeBoxPoints: the corners (-,-,-) (+,-,-) (+,+,-) (-,+,-) then the same at +z.</summary>
    internal static void BoxCorners(in HitBox b, Span<Vector3> into)
    {
        var x = b.AxisX * b.Extents.X; var y = b.AxisY * b.Extents.Y; var z = b.AxisZ * b.Extents.Z;
        var c = b.Center;
        into[0] = c - x - y - z; into[1] = c + x - y - z; into[2] = c + x + y - z; into[3] = c - x + y - z;
        into[4] = c - x - y + z; into[5] = c + x - y + z; into[6] = c + x + y + z; into[7] = c - x + y + z;
    }

    /// <summary>CBoxCollision::FUN_140adacb0 @0x140adacb0: the point inside the oriented box.</summary>
    static bool BoxContains(in HitBox b, Vector3 p)
    {
        var d = p - b.Center;
        return MathF.Abs(Vector3.Dot(d, b.AxisX)) <= b.Extents.X && MathF.Abs(Vector3.Dot(d, b.AxisY)) <= b.Extents.Y
            && MathF.Abs(Vector3.Dot(d, b.AxisZ)) <= b.Extents.Z;
    }

    /// <summary>CFrustumCollision::IntersectsBox @0x141223e70: each side's ground edge against the box's bottom face edges; a
    /// crossing farther from the apex than the near plane counts when the side's top edge point there is not below the box
    /// bottom and its bottom edge point not above the box top (both from the first side's edges, as the client reads them).</summary>
    bool IntersectsBox(ReadOnlySpan<Vector3> bc)
    {
        var dirA = a1 - a0; float lenA = dirA.Length(); if (lenA > 0f) dirA /= lenA;
        var dirB = b1 - b0; float lenB = dirB.Length(); if (lenB > 0f) dirB /= lenB;
        for (int side = 0; side < 2; side++)
        {
            var s0 = new Vector2(a0.X, a0.Y);
            var s1 = side == 0 ? new Vector2(a1.X, a1.Y) : new Vector2(a2.X, a2.Y);
            float sideLen = (s1 - s0).Length();
            for (int edge = 0; edge < 4; edge++)
            {
                var e0 = new Vector2(bc[edge].X, bc[edge].Y);
                var e1 = new Vector2(bc[(edge + 1) & 3].X, bc[(edge + 1) & 3].Y);
                if (!SegmentIntersect(s0, s1, e0, e1, out var hit)) continue;
                float hitLen = (hit - s0).Length();
                if (hitLen <= apexDist) continue;
                float t = hitLen / sideLen;
                var ptA = a0 + dirA * (lenA * t);
                var ptB = b0 + dirB * (lenB * t);
                if (!(ptA.Z < bc[0].Z) && !(ptB.Z > bc[4].Z)) return true;
            }
        }
        return false;
    }

    /// <summary>CFrustumCollision::SegmentIntersect @0x1412245a0.</summary>
    static bool SegmentIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, out Vector2 hit)
    {
        hit = default;
        float det = (b2.Y - b1.Y) * (a2.X - a1.X) - (b2.X - b1.X) * (a2.Y - a1.Y);
        if (det == 0f) return false;
        float numT = (b2.X - b1.X) * (a1.Y - b1.Y) - (b2.Y - b1.Y) * (a1.X - b1.X);
        float numU = (a2.X - a1.X) * (a1.Y - b1.Y) - (a2.Y - a1.Y) * (a1.X - b1.X);
        float t = numT / det, u = numU / det;
        if (t < 0.0 || 1.0 < t || u < 0.0 || 1.0 < u) return false;
        if (numT == 0f && numU == 0f) return false;
        hit = new Vector2(a1.X + t * (a2.X - a1.X), a1.Y + t * (a2.Y - a1.Y));
        return true;
    }
}

/// <summary>
/// A skill attack's posed area (CSkillCollisionManager::GetCached @0x141754580, skillcollisionmanager.cpp:121): a box of
/// (width, distance + zOffset, height) or a cylinder of radius distance and the given height, both with rangeAdd as the extra
/// size behind and below (m_ptSize).
/// A frustum (rangeType 3) is a <see cref="HitFrustum"/> (CFrustumCollision, frustumcollision.cpp).
/// A hole cylinder (rangeType 4, CHoleCylinderCollision, holecylindercollision.cpp) is a ring: <see cref="Radius"/> is its inner
/// radius and <see cref="OuterRadius"/> its outer (CSkillCollisionManager::GetCached @0x141754580: width and endWidth).
/// </summary>
public struct SkillHitShape
{
    public int Shape;
    public HitBox Box;
    public HitFrustum Frustum;
    /// <summary>The cylinder: centre (the pose position, unit_141083230 vfunc_3 @0x1410833c0), radius, height, rangeAdd.z.</summary>
    public Vector3 CylinderCenter;
    public float Radius, Height, AddZ;
    /// <summary>The hole cylinder's outer radius (endWidth); <see cref="Radius"/> holds its inner radius (width).</summary>
    public float OuterRadius;

    public static SkillHitShape Pose(SkillRangeShape r, float zOffset, Vector3 pos, Vector3 rotDegrees)
    {
        var s = new SkillHitShape { Shape = r.Shape };
        if (r.Shape == 1) s.Box = HitBox.Pose(new Vector3(r.Width, r.Distance + zOffset, r.Height), r.Add, pos, rotDegrees);
        else if (r.Shape == 2) { s.CylinderCenter = pos; s.Radius = r.Distance; s.Height = r.Height; s.AddZ = r.Add.Z; }
        else if (r.Shape == 4) { s.CylinderCenter = pos; s.Radius = r.Width; s.OuterRadius = r.EndWidth; s.Height = r.Height; s.AddZ = r.Add.Z; }
        else if (r.Shape == 3) s.Frustum = HitFrustum.Pose(r, zOffset, pos, rotDegrees);
        return s;
    }

    /// <summary>
    /// CBaseCollision::Intersect @0x1409ff0d0 (basecollision.cpp:671) of this area against a box: box and box by the separating
    /// axis test, a cylinder by the squared distance from its centre to the box (the box centre at the cylinder's height) within
    /// the radius and the z spans overlapping, a hole cylinder by <see cref="RingHit"/>.
    /// </summary>
    public readonly bool Intersects(in HitBox box)
    {
        if (Shape == 1) return HitBox.Intersect(Box, box);
        if (Shape == 3) return Frustum != null && Frustum.Intersects(box);
        if (Shape == 4) return RingHit(box);
        if (Shape != 2) return false;
        var c = box.Center;
        c.Z = CylinderCenter.Z;
        return box.PointSqrDist(CylinderCenter, c) <= Radius * Radius
            && CylinderCenter.Z + Height > box.MinZ && CylinderCenter.Z - AddZ < box.MaxZ;
    }

    /// <summary>
    /// The hole cylinder against a box (basecollision.cpp:567 and :703): the box centre or a corner inside the ring
    /// (CHoleCylinderCollision::ContainsPoint @0x140d039a0: z between the centre minus rangeAdd.z and the centre plus the height,
    /// planar distance from inner to outer radius), else, with the z spans overlapping, the squared distance from the ring's
    /// inner-to-outer segment toward the box centre (both on the ground) to the box under min(150, outer - inner).
    /// </summary>
    readonly bool RingHit(in HitBox box)
    {
        if (RingContains(box.Center)) return true;
        Span<Vector3> bc = stackalloc Vector3[8];
        HitFrustum.BoxCorners(box, bc);
        foreach (var c in bc) if (RingContains(c)) return true;
        if (!(box.MinZ < CylinderCenter.Z + Height && box.MaxZ > CylinderCenter.Z - AddZ)) return false;
        var center = new Vector3(CylinderCenter.X, CylinderCenter.Y, 0f);
        var boxCenter = new Vector3(box.Center.X, box.Center.Y, 0f);
        // INFERRED: a body centred on the ring centre gives a zero axis (efd::Point3::Unitize of a zero vector is not in the
        // restored source; a zero result is assumed), so the segment is the point at the ring centre, which is inside the box.
        var axis = SkillHit.Unitize(boxCenter - center);
        float distSq = box.SegmentSqrDist(center + axis * Radius, center + axis * OuterRadius, boxCenter);
        return Math.Min(150f, OuterRadius - Radius) > distSq;
    }

    readonly bool RingContains(Vector3 pt)
    {
        if (CylinderCenter.Z - AddZ > pt.Z || CylinderCenter.Z + Height < pt.Z) return false;
        float dx = pt.X - CylinderCenter.X, dy = pt.Y - CylinderCenter.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        return !(dist < Radius || dist > OuterRadius);
    }

    /// <summary>
    /// CBaseCollision::TestShape @0x1409fe460 (basecollision.cpp:518) for the box, cylinder and hole cylinder shapes against a body box, with the
    /// sort metric: box hits keep the front plane distance (y) and side plane distance (x, absolute in modes 3 and 4) of the body
    /// centre; cylinder hits keep the squared distance (z).
    /// </summary>
    public readonly bool Test(Vector3 pos, Vector3 right, Vector3 dir, in HitBox body, int mode, ref Vector3 metric)
    {
        bool hit = false;
        if (Shape == 4)
        {
            hit = RingHit(body);
            if (hit)
            {
                // the planar distance from the ring centre to the body centre (GetPlanarDistance @0x140966ea0)
                float dx = CylinderCenter.X - body.Center.X, dy = CylinderCenter.Y - body.Center.Y;
                metric.Z = MathF.Sqrt(dx * dx + dy * dy);
            }
        }
        else if (Shape == 1 || Shape == 3)
        {
            hit = Shape == 1 ? HitBox.Intersect(Box, body) : Frustum != null && Frustum.Intersects(body);
            if (hit)
            {
                // NiPlane(n, p).Distance(c) = n.c - n.p
                float forward = Vector3.Dot(dir, body.Center) - Vector3.Dot(dir, pos);
                float lateral = Vector3.Dot(right, body.Center) - Vector3.Dot(right, pos);
                if (mode == 2 || mode == 1) { metric.X = lateral; metric.Y = MathF.Abs(forward); }
                else if (mode == 4 || mode == 3) { metric.X = MathF.Abs(lateral); metric.Y = MathF.Abs(forward); }
            }
        }
        else if (Shape == 2)
        {
            var c = body.Center;
            c.Z = CylinderCenter.Z;
            float d2 = body.PointSqrDist(CylinderCenter, c);
            if (d2 <= Radius * Radius)
                hit = CylinderCenter.Z + Height > body.MinZ && CylinderCenter.Z - AddZ < body.MaxZ;
            if (hit) metric.Z = d2;
        }
        return hit;
    }
}

/// <summary>What an object is to the target filter: a PC, an npc (CNpc) or a pet (CPet).</summary>
public enum HitKind : byte { Pc, Npc, Pet }

/// <summary>
/// An object the area pick tests: its id, position, body box (vfunc_26) and whether it is the caster, with what the target
/// filter (<see cref="SkillHit.FilterTarget"/>) reads of it.
/// </summary>
public struct HitCandidate
{
    public int Id;
    public Vector3 Position;
    public HitBox Body;
    public bool IsOwner;
    public HitKind Kind;
    /// <summary>vfunc_116: an npc's friendly (CNpc::vfunc_116), 1 for a PC; 2 is never a target.</summary>
    public int Team;
    /// <summary>FUN_141670a00(4): the current hp.</summary>
    public long Hp;
    /// <summary>IsHidden, or the states 6 / 7 (FUN_14166cb00): skipped unless the target kind has flag 4.</summary>
    public bool Hidden;
    /// <summary>PC::HasPvPPoint.</summary>
    public bool PvPPoint;
    /// <summary>CNpc::GetNpcClass (CNpcData m_nClass).</summary>
    public int NpcClass;
    /// <summary>CNpc::FUN_1410930a0's state half: the dead state 0xc with &lt;corpse hitAble&gt;; the hp 0 is checked apart.</summary>
    public bool CorpseHit;
    /// <summary>A pet with an owner (CPet::GetOwner).</summary>
    public bool PetOwned;
    /// <summary>PC::FUN_141661bc0, m_nFF0: the party id (CPartyManager::GetPartyId, pc.cpp:4438), 0 with no party.</summary>
    public long PartyId;
    /// <summary>PC::FUN_1416749a0()-&gt;IsPvpOnOff (<see cref="AdditionalBuffList.IsPvpOnOff"/>).</summary>
    public bool PvpOnOff;
    /// <summary>PCBase::FUN_1416749a0()-&gt;GetForceApplyTarget (<see cref="AdditionalBuffList.ForceApplyTarget"/>): read off the
    /// caster, its bits join the filter flags.</summary>
    public int ForceApplyTarget;
    /// <summary>PCBase::IsDead on a PC (the auto target's pick keeps dead PCs apart, <see cref="SkillCast.AutoTarget"/>); the
    /// candidates here hold no other PCs yet, so nothing sets it in the game.</summary>
    public bool Dead;
    /// <summary>PCBase::GetCurStateId on a PC: the arrow fill's state filter (<see cref="SkillAttackData.StateHitIgnore"/>).</summary>
    public int StateId;
    /// <summary>PCBase::FUN_1416749a0()-&gt;GetInvincible on a PC (<see cref="AdditionalBuffList.Invincible"/>): 2 turns an
    /// arrow aside (the arrow fill's bounce-off).</summary>
    public int Invincible;
    /// <summary>An npc's CNpcData m_nDefenceMaterial (+0x70, "defenceMaterial", npcdata.cpp:1455): the hit sound's defence
    /// material (CMagic::PlayHitEffects); the other objects use 1.</summary>
    public int DefenceMaterial;
    /// <summary>An npc's CNpcData::IsBoss (CNpc::vfunc_58 is then 6, else 5).</summary>
    public bool Boss;
    /// <summary>An npc's collision widthOffset, depthOffset and heightOffset (CNpc::vfunc_51), unturned.</summary>
    public Vector3 CollisionOffset;
    /// <summary>An npc bound to an owner (CNpc m_nF20, NpcRide mode 1): CNpc::vfunc_51's offset is then the owner's position
    /// minus the npc's, unturned (unit_14108e1e0_npcactorandvibrate.cpp:2217-2222); null when not bound or the owner is not here.</summary>
    public Vector3? BoundOffset;
}

/// <summary>The area pick of a skill attack and the target records the damage send carries.</summary>
public static class SkillHit
{
    /// <summary>
    /// FUN_140a01250 @0x140a01250: forward = MakeZRotation(-z) * -UNIT_Y, right = the same * -UNIT_X, with Gamebryo's
    /// clockwise MakeZRotation (z degrees counter-clockwise): forward (sin z, -cos z, 0), right (-cos z, -sin z, 0).
    /// </summary>
    public static void Axes(float rotZDegrees, out Vector3 forward, out Vector3 right)
    {
        float a = rotZDegrees * 3.1415927f / 180f, s = MathF.Sin(a), c = MathF.Cos(a);
        forward = new Vector3(s, -c, 0);
        right = new Vector3(-c, -s, 0);
    }

    /// <summary>
    /// The area's pose (StateSkill::FUN_1417e95a0 @0x1417e95a0, stateskill.cpp:1148): unless attackMethodType is 1
    /// (SkillLevelData::FUN_1408043f0 @0x1408043f0), rangeOffset turns with the caster
    /// (MakeZRotation(DirectionToNegYawRadians(forward) + pi), so offset x runs along the right axis and y along forward) and
    /// rangeZRotateDegree adds to the rotation's z.
    /// </summary>
    public static void AreaPose(SkillAttackData attack, Vector3 ownerPos, Vector3 ownerRotDegrees, out Vector3 pos, out Vector3 rot)
    {
        var offset = Vector3.Zero;
        var height = Vector3.Zero;
        if (!attack.ArrowRecord)
        {
            Axes(ownerRotDegrees.Z, out var f, out var r);
            var o = attack.Range.Offset;
            offset = r * o.X + f * o.Y + new Vector3(0, 0, o.Z);
            height.Z = attack.Range.ZRotateDegree;
        }
        pos = ownerPos + offset;
        rot = ownerRotDegrees + height;
    }

    struct Entry { public int Id; public Vector3 Metric; public float Facing; }

    [ThreadStatic] static List<Entry> hitScratch;
    [ThreadStatic] static List<int> pickScratch;

    /// <summary>
    /// The world query's filter FilterTarget @0x1409ff8d0 (basecollision.cpp:784): can <paramref name="self"/> target
    /// <paramref name="other"/>. Flags: 1 hostile, 2 friendly, 4 the hidden ones too, 8 owner-less pets; a set bit of
    /// <paramref name="npcKinds"/> (applyTargetIgnoreNpcRanks) drops an npc of that class. Hostile means the caster's team is 2 or
    /// the caster's vfunc_122 says so: PC::vfunc_122 @0x14165f5d0 (pc.cpp:3112) on a PC target with the field's
    /// <paramref name="pkMode"/> is hostile unless it is the caster itself, shares the caster's nonzero party id, or either has
    /// pvp on/off set; else (and for its HasPvPPoint xor, which this filter already checked) the teams differ (PCBase::vfunc_122
    /// @0x1416781c0, pcbase.cpp:2784). A hostile target needs hp, or for an npc hp 0 with a hittable corpse; a friendly one
    /// needs hp. The caster's buff-summed GetForceApplyTarget bits are OR'd into <paramref name="flags"/> first (:792).
    /// STILL-OPEN: Hidden has the PCs' buff-summed IsHidden only, not the states 6 / 7 (FUN_14166cb00) nor npcs' lists;
    /// PvPPoint has no feed.
    /// </summary>
    public static bool FilterTarget(in HitCandidate self, in HitCandidate other, int flags, int npcKinds, bool pkMode = false)
    {
        flags |= self.ForceApplyTarget;
        if (other.Team == 2) return false;
        if ((flags & 4) == 0 && other.Hidden) return false;
        if (other.Kind == HitKind.Pet) return (flags & 8) != 0 && !other.PetOwned;
        if (self.Kind == HitKind.Pc && other.Kind == HitKind.Pc && self.PvPPoint != other.PvPPoint) return false;
        if (npcKinds != 0 && other.Kind == HitKind.Npc && (npcKinds & (1 << other.NpcClass)) != 0) return false;
        bool hostile = self.Team == 2 || (pkMode && self.Kind == HitKind.Pc && other.Kind == HitKind.Pc
            ? self.Id != other.Id && !(self.PartyId != 0 && self.PartyId == other.PartyId)
                && !self.PvpOnOff && !other.PvpOnOff
            : self.Team != other.Team);
        if ((flags & 1) != 0 && hostile)
        {
            if (other.Hp > 0) return true;
            if (other.Kind == HitKind.Npc && other.Hp == 0 && other.CorpseHit) return true;
        }
        return (flags & 2) != 0 && (self.Team == 2 || !hostile) && other.Hp > 0;
    }

    /// <summary>
    /// WorldService::FUN_141bd2ea0 (worldservice_1.cpp:6602) as CBaseCollision::Detect @0x1409fc6c0 (basecollision.cpp:221)
    /// calls it: the objects the caster (the <see cref="HitCandidate.IsOwner"/> one; none, nothing) may target by
    /// <see cref="FilterTarget"/>, the caster included when it passes. The objects here are PCs, npcs and pets only, so the
    /// query's class 0x6200016 skip has nothing to drop. <paramref name="pkMode"/> is the field's (CFieldData::GetPkMode).
    /// </summary>
    public static void Query(ReadOnlySpan<HitCandidate> objects, int kind, int npcKinds, List<HitCandidate> into, bool pkMode = false)
    {
        into.Clear();
        int owner = -1;
        for (int i = 0; i < objects.Length; i++)
            if (objects[i].IsOwner) { owner = i; break; }
        if (owner < 0) return;
        foreach (ref readonly var o in objects)
            if (FilterTarget(objects[owner], o, kind, npcKinds, pkMode)) into.Add(o);
    }

    /// <summary>
    /// CBaseCollision::DetectWithEulerRotation @0x1409fc390 then Detect @0x1409fc6c0 (basecollision.cpp:213), over candidates the
    /// caller has already queried by target kind (<see cref="Query"/>) with the Ridee objects and the excluded ids left out.
    /// The caster is skipped (before the test, or after a hit when feature 0xd7 is on) and put first when includeCaster is set;
    /// the hits are ordered by the sort mode: 0 shuffled, 1/2 side distance up/down, 3/4 front distance up/down (cylinder:
    /// squared distance), 5/6 facing down/up. <paramref name="rand"/> is the CRT rand().
    /// STILL-OPEN: the range's buff / party / guild filter and stat sort, the rider of a Ridee caster, and the all-anchors and
    /// extra body boxes (vfunc_28 / vfunc_27) are not ported: one body box per object.
    /// </summary>
    public static void Detect(in SkillHitShape shape, Vector3 pos, Vector3 rotDegrees, int sortMode, bool includeCaster,
        bool ownerAfterTest, ReadOnlySpan<HitCandidate> objects, Func<int> rand, List<int> targets)
    {
        Axes(rotDegrees.Z, out var dir, out var right);
        Detect(shape, pos, Vector3.Normalize(dir), Vector3.Normalize(right), sortMode, includeCaster, ownerAfterTest, objects, rand, targets);
    }

    /// <summary>The pick with the sort axes given: <paramref name="dir"/> and <paramref name="right"/> (unit).
    /// CBaseCollision::DetectWithMatrixRotation @basecollision.cpp:202 gives the matrix's column 0 and that x UNIT_Z.</summary>
    public static void Detect(in SkillHitShape shape, Vector3 pos, Vector3 dir, Vector3 right, int sortMode, bool includeCaster,
        bool ownerAfterTest, ReadOnlySpan<HitCandidate> objects, Func<int> rand, List<int> targets)
    {
        targets.Clear();
        if (objects.Length <= 0) return;
        bool facing = sortMode == 5 || sortMode == 6;
        int first = 0;
        bool hasFirst = false;
        var hits = hitScratch ??= new List<Entry>();
        hits.Clear();
        foreach (ref readonly var o in objects)
        {
            if (!ownerAfterTest && o.IsOwner)
            {
                if (includeCaster) { first = o.Id; hasFirst = true; }
                continue;
            }
            var metric = Vector3.Zero;
            if (!shape.Test(pos, right, dir, o.Body, sortMode, ref metric)) continue;
            if (ownerAfterTest && o.IsOwner)
            {
                if (includeCaster) { first = o.Id; hasFirst = true; }
                continue;
            }
            var e = new Entry { Id = o.Id, Metric = metric };
            if (facing)
            {
                var to = o.Position - pos;
                to.Z = 0;
                to = Unitize(to);
                e.Facing = Vector3.Dot(dir, to);
            }
            hits.Add(e);
        }
        if (sortMode == 0)
        {
            for (int i = hits.Count - 1; i >= 1; i--)
            {
                int j = rand() % (i + 1);
                if (i != j) (hits[i], hits[j]) = (hits[j], hits[i]);
            }
        }
        else
        {
            bool round = shape.Shape == 2 || shape.Shape == 4;
            for (int i = 0; i < hits.Count; i++)
                for (int j = i + 1; j < hits.Count; j++)
                {
                    var a = hits[i];
                    var b = hits[j];
                    bool swap = sortMode switch
                    {
                        2 => a.Metric.X < b.Metric.X,
                        1 => a.Metric.X > b.Metric.X,
                        3 => round && a.Metric.Z > b.Metric.Z || a.Metric.Y > b.Metric.Y,
                        4 => round && a.Metric.Z < b.Metric.Z || a.Metric.Y < b.Metric.Y,
                        5 => a.Facing < b.Facing,
                        6 => a.Facing > b.Facing,
                        _ => false,
                    };
                    if (swap) (hits[i], hits[j]) = (b, a);
                }
        }
        if (hasFirst) targets.Add(first);
        foreach (var h in hits) targets.Add(h.Id);
    }

    /// <summary>efd::Point3::Unitize: a zero vector stays zero.</summary>
    internal static Vector3 Unitize(Vector3 v)
    {
        float l = v.Length();
        return l > 1e-06f ? v / l : Vector3.Zero;
    }

    /// <summary>
    /// StateSkill::PickAttackTargets @0x1417f3180 (stateskill.cpp:2565) after the area pick: the first targetCount hits
    /// (CSkillSystem::FUN_141769410 @0x141769410), with arrow overlap the rest of the count filled by random repeats; each gets a
    /// record with a new serial (GameObject::GenerateSerialId @0x14123cec0) and the next hit index. Then target-less records:
    /// up to the count when <paramref name="arrowRecord"/> (CSkillRecord::FUN_141759f70 @0x141759f70, skillrecord.cpp:346:
    /// <see cref="SkillAttackData.ArrowRecord"/> of the record's m_nAttackIndex attack), else one for a nonTarget 1 arrow.
    /// <paramref name="hits"/> is null when the arrow is nonTarget 1 (no area pick).
    /// Between the picked records and the target-less ones, an arrow with a bounceCount chains each picked record
    /// (<see cref="Chain"/>) over <paramref name="objects"/>, so the links take their serials in that order.
    /// </summary>
    public static void PickTargets(SkillAttackData attack, bool arrowRecord, IReadOnlyList<int> hits, Func<int> rand, Func<long> serial,
        List<SkillDamageTarget> tags, ReadOnlySpan<HitCandidate> objects = default, bool pkMode = false)
    {
        tags.Clear();
        int limit = attack.TargetCount;
        int hitCount = hits?.Count ?? 0;
        int count = limit < hitCount ? limit : hitCount;
        int used = 0;
        var picked = pickScratch ??= new List<int>();
        picked.Clear();
        for (int i = 0; i < count; i++) { used++; picked.Add(hits[i]); }
        int extra = limit - count;
        if (attack.Arrow.Overlap != 0 && extra > 0 && hitCount > 0)
            for (int i = 0; i < extra; i++) { used++; picked.Add(hits[rand() % hitCount]); }
        int hit = 0;
        foreach (int id in picked) tags.Add(new SkillDamageTarget(serial(), id, (byte)hit++, Array.Empty<SkillDamageLink>()));
        int bounce = attack.Arrow.BounceCount;
        if (bounce > 0)
            for (int i = 0; i < tags.Count; i++)
            {
                var links = Chain(attack, bounce, tags[i].TargetId, objects, rand, serial, pkMode);
                if (links.Count != 0) tags[i] = tags[i] with { Links = links };
            }
        if (arrowRecord)
        {
            int rest = limit - used < 0 ? 0 : limit - used;
            for (int i = 0; i < rest; i++) tags.Add(new SkillDamageTarget(serial(), 0, (byte)hit++, Array.Empty<SkillDamageLink>()));
        }
        else if (attack.Arrow.IsNonTargetOne)
            tags.Add(new SkillDamageTarget(serial(), 0, 0, Array.Empty<SkillDamageLink>()));
    }

    [ThreadStatic] static List<HitCandidate> chainQueried;
    [ThreadStatic] static List<int> chainFound, chainIds;

    /// <summary>
    /// StateSkill::FUN_1417f3e80 @0x1417f3e80 (stateskill.cpp:2689), the bounce chain of a picked record that hits
    /// <paramref name="rootId"/>. Each bounce (<paramref name="bounce"/> of them, stopping early when nothing is found) searches
    /// around the chain's last hit: a cylinder of the arrow's bounceRadius, 150 high, at that object's position. The search
    /// goes over the objects the owner may target (<see cref="Query"/>: the range's applyTarget and applyTargetIgnoreNpcRanks),
    /// sorted by mode 3 (the squared distance), without the owner. It stops when the last hit or the owner is not among
    /// <paramref name="objects"/>. Which objects are left:
    /// <list type="bullet">
    /// <item>bounceType 2: the found objects that are not in the chain. When none are left and bounceOverlap is 1, then with
    /// more than one found the chain starts over (it is emptied), the last hit is dropped from the found ones, and they are
    /// shuffled (for each i, swap with rand() % count). With one found, nothing is left.</item>
    /// <item>bounceType 1, or bounceOverlap 0: the found objects without those in the chain.</item>
    /// <item>Otherwise every found object, the last hit included.</item>
    /// </list>
    /// The first one left gets the next link: a new serial, its object, the bounce number (bounceCount - bounces left + 1) and
    /// hit index 0. It is added to the chain, and the chain goes on from it.
    /// </summary>
    public static List<SkillDamageLink> Chain(SkillAttackData attack, int bounce, int rootId, ReadOnlySpan<HitCandidate> objects,
        Func<int> rand, Func<long> serial, bool pkMode = false)
    {
        var links = new List<SkillDamageLink>();
        var arrow = attack.Arrow;
        var queried = chainQueried ??= new List<HitCandidate>();
        var found = chainFound ??= new List<int>();
        var chain = chainIds ??= new List<int>();
        chain.Clear();
        chain.Add(rootId);
        int lastId = rootId;
        for (; bounce != 0; bounce--)
        {
            int from = -1, owner = -1;
            for (int i = 0; i < objects.Length; i++)
            {
                if (from < 0 && objects[i].Id == lastId && lastId != 0) from = i;
                if (owner < 0 && objects[i].IsOwner) owner = i;
            }
            if (from < 0 || owner < 0) break;
            var at = objects[from].Position;
            var shape = new SkillHitShape { Shape = 2, CylinderCenter = at, Radius = arrow.BounceRadius, Height = 150f };
            Query(objects, attack.Range.ApplyTarget, attack.Range.IgnoreNpcRanks, queried, pkMode);
            Detect(shape, at, Vector3.Zero, 3, false, false, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(queried), rand, found);
            if (arrow.BounceType == 2)
            {
                int left = found.Count;
                foreach (int id in chain)
                    if (found.IndexOf(id) >= 0) left--;
                if (arrow.BounceOverlap == 1 && left == 0)
                {
                    if (found.Count > 1)
                    {
                        int last = chain.Count != 0 ? chain[^1] : 0;
                        chain.Clear();
                        int at0 = found.IndexOf(last);
                        if (at0 >= 0) found.RemoveAt(at0);
                        for (int i = 0; i < found.Count; i++)
                        {
                            int pick = rand() % found.Count;
                            (found[i], found[pick]) = (found[pick], found[i]);
                        }
                    }
                    else found.Clear();
                }
                else RemoveChain(found, chain);
            }
            else if (arrow.BounceType == 1 || arrow.BounceOverlap == 0) RemoveChain(found, chain);
            if (found.Count == 0) break;
            int next = found[0];
            links.Add(new SkillDamageLink(serial(), next, (byte)(arrow.BounceCount - bounce + 1), 0));
            chain.Add(next);
            lastId = next;
        }
        return links;
    }

    /// <summary>FindTargetIndex @0x1417f5710 then RemoveAt for each record of the chain: one occurrence each.</summary>
    static void RemoveChain(List<int> found, List<int> chain)
    {
        foreach (int id in chain)
        {
            int i = found.IndexOf(id);
            if (i >= 0) found.RemoveAt(i);
        }
    }

    /// <summary>The body box of a player (PC::SetDefaultBoundingBox, pc.cpp:2776): size (70, 35, 150), add (0, 35, 0).</summary>
    public static HitBox PlayerBody(Vector3 pos, float rotZDegrees) =>
        HitBox.Pose(new Vector3(70, 35, 150), new Vector3(0, 35, 0), pos, new Vector3(0, 0, rotZDegrees));

    /// <summary>
    /// The body box of an npc with a box collision (CNpc::BuildCollision, unit_14108e1e0_npcactorandvibrate.cpp:1648): size
    /// (width, depth / 2, height), add (0, depth / 2, 0).
    /// </summary>
    public static HitBox NpcBody(float width, float depth, float height, Vector3 pos, float rotZDegrees) =>
        HitBox.Pose(new Vector3(width, depth / 2f, height), new Vector3(0, depth / 2f, 0), pos, new Vector3(0, 0, rotZDegrees));
}
