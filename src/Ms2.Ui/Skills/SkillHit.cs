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
        var min = new Vector3(-size.X / 2f, -add.Y, -add.Z);
        var max = new Vector3(size.X / 2f, size.Y, size.Z);
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
/// A skill attack's posed area (CSkillCollisionManager::GetCached @0x141754580, skillcollisionmanager.cpp:121): a box of
/// (width, distance + zOffset, height) or a cylinder of radius distance and the given height, both with rangeAdd as the extra
/// size behind and below (m_ptSize).
/// STILL-OPEN: frustum (3) and hole_cylinder (4) shapes are not ported; they hit nothing here.
/// </summary>
public struct SkillHitShape
{
    public int Shape;
    public HitBox Box;
    /// <summary>The cylinder: centre (the pose position, unit_141083230 vfunc_3 @0x1410833c0), radius, height, rangeAdd.z.</summary>
    public Vector3 CylinderCenter;
    public float Radius, Height, AddZ;

    public static SkillHitShape Pose(SkillRangeShape r, float zOffset, Vector3 pos, Vector3 rotDegrees)
    {
        var s = new SkillHitShape { Shape = r.Shape };
        if (r.Shape == 1) s.Box = HitBox.Pose(new Vector3(r.Width, r.Distance + zOffset, r.Height), r.Add, pos, rotDegrees);
        else if (r.Shape == 2) { s.CylinderCenter = pos; s.Radius = r.Distance; s.Height = r.Height; s.AddZ = r.Add.Z; }
        return s;
    }

    /// <summary>
    /// CBaseCollision::TestShape @0x1409fe460 (basecollision.cpp:518) for the box and cylinder shapes against a body box, with the
    /// sort metric: box hits keep the front plane distance (y) and side plane distance (x, absolute in modes 3 and 4) of the body
    /// centre; cylinder hits keep the squared distance (z).
    /// </summary>
    public readonly bool Test(Vector3 pos, Vector3 right, Vector3 dir, in HitBox body, int mode, ref Vector3 metric)
    {
        bool hit = false;
        if (Shape == 1)
        {
            hit = HitBox.Intersect(Box, body);
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
    /// <summary>PC::FUN_1416749a0()-&gt;IsPvpOnOff.</summary>
    public bool PvpOnOff;
    /// <summary>The pvp on/off is not known (no feed): on a PK field the PC counts as having it set, so it is not hostile and
    /// no target goes out the client would not send.</summary>
    public bool PvpUnknown;
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
        if (attack.AttackMethodType != 1)
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
    /// needs hp.
    /// STILL-OPEN: the caster's GetForceApplyTarget extra flags (a buff state) are not ported; pvp on/off
    /// (AdditionalCliList m_bPvpOnOff, set by a buff level's pvpOnOff, additionalcli_2.cpp:1081) has no feed, so the field's PCs
    /// come with <see cref="HitCandidate.PvpUnknown"/>; Hidden and PvPPoint have no feed either.
    /// </summary>
    public static bool FilterTarget(in HitCandidate self, in HitCandidate other, int flags, int npcKinds, bool pkMode = false)
    {
        if (other.Team == 2) return false;
        if ((flags & 4) == 0 && other.Hidden) return false;
        if (other.Kind == HitKind.Pet) return (flags & 8) != 0 && !other.PetOwned;
        if (self.Kind == HitKind.Pc && other.Kind == HitKind.Pc && self.PvPPoint != other.PvPPoint) return false;
        if (npcKinds != 0 && other.Kind == HitKind.Npc && (npcKinds & (1 << other.NpcClass)) != 0) return false;
        bool hostile = self.Team == 2 || (pkMode && self.Kind == HitKind.Pc && other.Kind == HitKind.Pc
            ? self.Id != other.Id && !(self.PartyId != 0 && self.PartyId == other.PartyId)
                && !(self.PvpOnOff || self.PvpUnknown) && !(other.PvpOnOff || other.PvpUnknown)
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
        targets.Clear();
        if (objects.Length <= 0) return;
        Axes(rotDegrees.Z, out var dir, out var right);
        dir = Vector3.Normalize(dir);
        right = Vector3.Normalize(right);
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
    static Vector3 Unitize(Vector3 v)
    {
        float l = v.Length();
        return l > 1e-06f ? v / l : Vector3.Zero;
    }

    /// <summary>
    /// StateSkill::PickAttackTargets @0x1417f3180 (stateskill.cpp:2565) after the area pick: the first targetCount hits
    /// (CSkillSystem::FUN_141769410 @0x141769410), with arrow overlap the rest of the count filled by random repeats; each gets a
    /// record with a new serial (GameObject::GenerateSerialId @0x14123cec0) and the next hit index. Then target-less records:
    /// up to the count when <paramref name="attackMethodType"/> is 1 (CSkillRecord::FUN_141759f70 @0x141759f70,
    /// skillrecord.cpp:346: the type of the record's m_nAttackIndex attack), else one for a nonTarget 1 arrow.
    /// <paramref name="hits"/> is null when the arrow is nonTarget 1 (no area pick).
    /// STILL-OPEN: the bounce chains (FUN_1417f3e80, arrow bounceCount) are not picked; every record goes without links.
    /// </summary>
    public static void PickTargets(SkillAttackData attack, int attackMethodType, IReadOnlyList<int> hits, Func<int> rand, Func<long> serial,
        List<SkillDamageTarget> tags)
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
        if (attackMethodType == 1)
        {
            int rest = limit - used < 0 ? 0 : limit - used;
            for (int i = 0; i < rest; i++) tags.Add(new SkillDamageTarget(serial(), 0, (byte)hit++, Array.Empty<SkillDamageLink>()));
        }
        else if (attack.Arrow.IsNonTargetOne)
            tags.Add(new SkillDamageTarget(serial(), 0, 0, Array.Empty<SkillDamageLink>()));
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
