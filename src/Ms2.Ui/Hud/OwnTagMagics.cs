using System.Numerics;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>The own player as a magic's owner sees it at a tick: the position and z rotation (degrees) the area is posed from,
/// whether the owner (or the rider of the owner's ridee) is the local player, and the dead and stunned tests.</summary>
public readonly record struct MagicOwner(Vector3 Position, float RotZ, bool LocalPlayer, bool Dead, bool Stunned);

/// <summary>CPhysXHelper::RayCastFromPoint on the groups 0x201 (magicdata.cpp:1786): true when a ray from <paramref name="from"/>
/// along the unit <paramref name="dir"/> hits within <paramref name="length"/>.</summary>
public delegate bool MagicRayBlocked(Vector3 from, Vector3 dir, float length);

/// <summary>
/// The own cast's magics as far as they send their region hit (SkillDamage 1/1, <see cref="SkillHitRequest"/>) and register
/// tag areas for <see cref="TombTagPass"/>: the CMagicModel each target record of a
/// fired attack gets (StateSkill::FUN_1417ecce0 @0x1417ecce0, stateskill.cpp:1347: CMagicModel::Init(tag, 0, ZERO, 0, 0, 0)), its
/// start tick the attack's tick (SetTargetStartTicks from m_mapC0, :1370) and its CMagicData the attack's magicPathID row at the
/// record's index (FUN_14145c300 of m_n84, magicdata.cpp:2614; an unknown id makes no magic).
/// <list type="bullet">
/// <item>The tick (CMagic::FUN_14145de80 @0x14145de80, magicdata.cpp:755), for a magic without attackMethodType 1
/// (SkillTargetRecord::FUN_141759090 0): before start + delay it runs the region fill and the registration
/// (FUN_141461bd0, FUN_141462490); after it, the state goes 0, 1, then 2 once spawnTime passed, where a lifeTime that ran out
/// goes to 3 without them, else it runs them.</item>
/// <item>Both are due once now reaches start + delay - CMainSystem::FUN_1414734a0 (the latency maximum of the last time sync,
/// the lead of <see cref="Tick"/>). The region fill (FUN_141464d00, magicdata.cpp:1750) runs once (m_n1CC) and sends the
/// hits (<see cref="Fill"/>), for every attack (brokenOffence or not). The registration (FUN_141462490, magicdata.cpp:1594) runs once (m_n1D0) for a local owner and an attack with brokenOffence
/// (or the flag at +0x100, which registers nothing without brokenOffence) and hands the area to WorldService::FUN_141bc3470,
/// which drops a missing area.</item>
/// <item>A skill whose magicControl enable is 2 (m_n538) makes no region fill, so it registers nothing.</item>
/// </list>
/// So a magic is dropped after its one fill and registration.
/// STILL-OPEN: attackMethodType 1 (the arrow fill FUN_141465b30 and the registration every tick), the linked records, the
/// cancel of a delay-0 magic when the skill state ends (CMagicService::RemoveCasterModels / CMagicModel::CanCancel), the
/// latency maximum (0 here: the time sync's latency is not kept), CFunctionCubeService::SetTagArea, the magic's own
/// flight and effects (PlayHitEffects), MyPC::FUN_14152e0c0 after a fill, and the splash send's call sites
/// (FUN_14145f570 at the magic's end, FUN_141466da0 per piercing interval: both need the flight; the packet is
/// <see cref="SkillSplashRequest"/>) are not ported.
/// </summary>
public sealed class OwnTagMagics
{
    sealed class Magic
    {
        public TagRecord Tag;
        public SkillAttackData Attack;
        public int Start, Delay, Spawn, Life;
        // the target record: the cast id (m_nTagId), its serial (m_nTagSub), its target (m_nCasterId), the attack index
        // (m_b5c) and the send count (m_n8c); the magic's start point (m_ptStart), set once at the Add.
        // STILL-OPEN: attackMethodType 2's tick sets m_ptStart again each tick (FUN_141462900 calls FUN_1414609e0,
        // magicdata.cpp:1232).
        public long CastId, Serial;
        public int TargetId, SendCount;
        public byte AttackIndex;
        public Vector3 StartPoint;
    }

    readonly List<Magic> live = new();
    readonly List<HitCandidate> queried = new();
    readonly List<int> detected = new(), kept = new(), others = new();

    /// <summary>The magics that may still fill or register.</summary>
    public int Count => live.Count;

    /// <summary>The magics without a cast record (no region send, the registration only).</summary>
    public int Add(SkillData skill, SkillAttackData attack, IReadOnlyList<SkillDamageTarget> tags, int startMs, MagicPathTable paths,
        Action onHit = null) => Add(skill, attack, null, tags, startMs, paths, null, onHit);

    /// <summary>
    /// FUN_1417ecce0's models for the attack <paramref name="attack"/> of <paramref name="skill"/> fired at
    /// <paramref name="startMs"/> by the record <paramref name="r"/> with the target records <paramref name="tags"/> (one magic
    /// each, its row picked by the record's index <see cref="SkillDamageTarget.Hit"/>). Returns the number kept.
    /// <paramref name="onHit"/> is each tag's <see cref="TagRecord.Hit"/>. <paramref name="nodePosition"/> is the owner's
    /// vf36_GetNodePosition (the owner's position when null): the start point (<see cref="StartPoint"/>).
    /// </summary>
    public int Add(SkillData skill, SkillAttackData attack, CastRecord r, IReadOnlyList<SkillDamageTarget> tags, int startMs,
        MagicPathTable paths, Func<string, Vector3> nodePosition, Action onHit = null)
    {
        if (attack == null || tags == null || paths == null) return 0;
        if (attack.AttackMethodType == 1 || (skill?.MagicControlEnable ?? 0) == 2) return 0;
        // STILL-OPEN: FUN_1417ecce0's direction for a PC whose FUN_141489940 is 10 (stateskill.cpp:1335-1342: -x turned by the
        // record's euler rotation) is not ported; the record's z rotation's forward is used.
        var dir = Vector3.Zero;
        if (r != null) SkillHit.Axes(r.Rotation.Z, out dir, out _);
        int n = 0;
        for (int i = 0; i < tags.Count; i++)
        {
            var t = tags[i];
            var d = paths.Get(attack.MagicPathId, t.Hit);
            if (d == null) continue;
            live.Add(new Magic
            {
                Tag = new TagRecord { BrokenOffence = attack.BrokenOffence, Hit = onHit },
                Attack = attack, Start = startMs,
                Delay = MagicPathTable.Ms(d.DelayTime), Spawn = MagicPathTable.Ms(d.SpawnTime), Life = MagicPathTable.Ms(d.LifeTime),
                CastId = r?.CastId ?? 0, Serial = t.Serial, TargetId = t.TargetId, SendCount = r?.SendCount ?? 0,
                AttackIndex = (byte)(r?.AttackIndex ?? 0),
                StartPoint = StartPoint(d, dir, nodePosition?.Invoke(d.FireNode) ?? r?.Position ?? Vector3.Zero),
            });
            n++;
        }
        return n;
    }

    /// <summary>
    /// CMagic::FUN_1414609e0 @0x1414609e0 (magicdata.cpp:1397) for an own magic (m_n530 0): the owner's fire node
    /// <paramref name="node"/>, or the row's fireFixedPosition when that is not zero, plus the row's fireOffsetPosition turned by
    /// MakeZRotation(DirectionToNegYawRadians(<paramref name="dir"/>) + pi - dirRotZDegree) with rotation set, else by
    /// MakeZRotation(-dirRotZDegree) (identity at 0). <paramref name="dir"/> is the target record's direction (m_ptDir, the
    /// attack's: PickAttackTargets, stateskill.cpp:2609). The arrow fill's start ray test (:1436) is not this path.
    /// </summary>
    public static Vector3 StartPoint(MagicPathMove d, Vector3 dir, Vector3 node)
    {
        var start = d.FireFixed != Vector3.Zero ? d.FireFixed : node;
        float rotRad = d.DirRotZ * 3.1415927f / 180f, t;
        if (d.Rotation != 0) t = -(YawDegrees(dir) * 3.1415927f / 180f) + 3.1415927f - rotRad;
        else t = -rotRad;
        // Gamebryo's MakeZRotation(t) turns by -t counter-clockwise (SkillHit.Axes)
        float c = MathF.Cos(t), s = MathF.Sin(t);
        var o = d.FireOffset;
        return start + new Vector3(o.X * c + o.Y * s, -o.X * s + o.Y * c, o.Z);
    }

    /// <summary>GameObject::DirectionToYawDegrees @gameobject.cpp:679: the matrix (back x Z, back, Z) of back = -dir (z 0, unit),
    /// ToEulerAnglesXYZ's z = atan2(back.x, back.y) negated in degrees, then NormalizeDegrees (commonutil.cpp:1518: the int
    /// part modulo 360, made positive). Forward (sin r, -cos r) gives r.</summary>
    public static float YawDegrees(Vector3 dir)
    {
        dir.Z = 0;
        float len = dir.Length();
        if (len > 0) dir /= len;
        float z = MathF.Atan2(-dir.X, -dir.Y);
        int deg = (int)(-(z * 180f / 3.1415927f)) % 360;
        if (deg < 0) deg += 360;
        return deg;
    }

    /// <summary>Whether a <see cref="Tick"/> at <paramref name="nowMs"/> would fill, register or drop a magic.</summary>
    public bool AnyDue(int nowMs, int leadMs)
    {
        foreach (var m in live)
            if (Due(m, nowMs, leadMs, out bool expired) || expired) return true;
        return false;
    }

    /// <summary>One tick without objects: the registrations only (no fill finds a hit).</summary>
    public int Tick(int nowMs, int leadMs, in MagicOwner owner, TombTagPass pass) =>
        Tick(nowMs, leadMs, owner, default, null, pass, null, null, null);

    /// <summary>
    /// One tick of every magic at <paramref name="nowMs"/>: each one due runs its region fill (<see cref="Fill"/>) over
    /// <paramref name="objects"/> for <paramref name="owner"/>, its send going to <paramref name="sends"/>, then registers the
    /// area into <paramref name="pass"/> (when not null), and is dropped. <paramref name="record"/> is the owner's current cast
    /// record (PCBase::FUN_141673e10 finds a record by its cast id; an older cast's is gone). Returns the number registered.
    /// STILL-OPEN: the point group sees only the current cast's record; PCBase::FUN_141673e10 finds any record the owner
    /// still keeps by its cast id.
    /// </summary>
    public int Tick(int nowMs, int leadMs, in MagicOwner owner, ReadOnlySpan<HitCandidate> objects, CastRecord record,
        TombTagPass pass, List<SkillHitRequest> sends, Func<int> rand, MagicRayBlocked rayBlocked, bool pkMode = false)
    {
        int registered = 0;
        // in the order the magics were made (the client runs aTags in order, stateskill.cpp:1401-1410)
        for (int i = 0; i < live.Count;)
        {
            var m = live[i];
            if (!Due(m, nowMs, leadMs, out bool expired))
            {
                if (expired) live.RemoveAt(i); else i++;
                continue;
            }
            live.RemoveAt(i);
            var req = Fill(m, owner, objects, record != null && record.CastId == m.CastId ? record : null, rand, rayBlocked, pkMode);
            if (req != null) sends?.Add(req);
            if (pass != null && Register(m, owner, pass)) registered++;
        }
        return registered;
    }

    /// <summary>Whether the fill and the registration run at <paramref name="nowMs"/>; <paramref name="expired"/> when the lifeTime ran
    /// out before state 2 ran them (state 3: they never run).</summary>
    static bool Due(Magic m, int nowMs, int leadMs, out bool expired)
    {
        expired = false;
        int end = m.Start + m.Delay;
        if ((uint)end > (uint)nowMs) return (uint)nowMs >= (uint)(end - leadMs);
        int stateTick = end + m.Spawn;
        if ((uint)stateTick > (uint)nowMs) return false;   // state 1 until spawnTime passed
        if (m.Life != 0 && (uint)(stateTick + m.Life) <= (uint)nowMs) { expired = true; return false; }
        return true;
    }

    /// <summary>
    /// CMagic::FUN_141464d00 @0x141464d00 (magicdata.cpp:1750-1862), the region fill of a magic whose record is not an arrow
    /// one (FUN_141461bd0 :2125), once (m_n1CC), for an owner alive and not stunned:
    /// <list type="bullet">
    /// <item>StateSkill::FUN_1417e95a0 (stateskill.cpp:1148): the attack's area posed from the owner's position and facing now
    /// (zOffset 0), over the objects of the kind applyTarget with the context's flag 4 (the hidden ones too, m_n80); no shape
    /// 1-4 or no hit sends nothing.</item>
    /// <item>arrow rayPhysxTest: the owner itself kept, any other hit kept when its position is off the owner's in x/y and the
    /// ray there (groups 0x201) is clear; none kept leaves no hits.</item>
    /// <item>pointGroupID with the owner's record for the cast: the targets already in that group dropped.</item>
    /// <item>applyTarget bit 2: StateSkill::FUN_1417ea310 (:1774), the own party's PCs first when in a party.</item>
    /// <item>an arrow that is not nonTarget 1: only the record's own target, none sends nothing.</item>
    /// <item>the hits up to targetCount (CSkillSystem::FUN_141769410) go in the send, each then added to the point group;
    /// any hit runs the record's FUN_1417591b0 (<see cref="TagRecord.Hit"/>).</item>
    /// </list>
    /// STILL-OPEN: FUN_141467100's hit point on a target (the target's position here), the all-anchors flag (m_n534 0 for the
    /// own magics) and the owner's ridee rider.
    /// </summary>
    SkillHitRequest Fill(Magic m, in MagicOwner owner, ReadOnlySpan<HitCandidate> objects, CastRecord record, Func<int> rand,
        MagicRayBlocked rayBlocked, bool pkMode)
    {
        if (owner.Dead || owner.Stunned || !owner.LocalPlayer) return null;
        var a = m.Attack;
        if (a.Range.Shape is < 1 or > 4) return null;
        int self = -1;
        for (int i = 0; i < objects.Length; i++)
            if (objects[i].IsOwner) { self = i; break; }
        if (self < 0) return null;
        SkillHit.AreaPose(a, owner.Position, new Vector3(0, 0, owner.RotZ), out var pos, out var rot);
        var shape = SkillHitShape.Pose(a.Range, 0, pos, rot);
        SkillHit.Query(objects, a.Range.ApplyTarget | 4, a.Range.IgnoreNpcRanks, queried, pkMode);
        SkillHit.Detect(shape, pos, rot, a.Direction, a.Range.IncludeCaster != 0, false,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(queried), rand ?? (() => 0), detected);
        if (detected.Count == 0) return null;
        int ownerId = objects[self].Id;
        if (a.Arrow.RayPhysxTest != 0)
        {
            kept.Clear();
            foreach (int id in detected)
            {
                if (id == ownerId) { kept.Add(id); continue; }
                var delta = PositionOf(objects, id) - owner.Position;
                delta.Z = 0;
                float len = delta.Length();
                if (len > 0f && !(rayBlocked?.Invoke(owner.Position, delta / len, len) ?? false)) kept.Add(id);
            }
            detected.Clear();
            detected.AddRange(kept);
        }
        var group = record != null && a.PointGroupId != 0 ? record.TargetGroup(a.PointGroupId) : null;
        if (group != null)
            for (int i = detected.Count - 1; i >= 0; i--)
                if (group.Contains(detected[i])) detected.RemoveAt(i);
        if (detected.Count == 0) return null;
        long party = objects[self].PartyId;
        if ((a.Range.ApplyTarget & 2) != 0 && party != 0)
        {
            kept.Clear(); others.Clear();
            foreach (int id in detected)
            {
                int k = IndexOf(objects, id);
                if (k >= 0 && objects[k].Kind == HitKind.Pc && objects[k].PartyId == party) kept.Add(id); else others.Add(id);
            }
            if (kept.Count != 0) { detected.Clear(); detected.AddRange(kept); detected.AddRange(others); }
        }
        if (!a.Arrow.IsNonTargetOne)
        {
            bool found = detected.Contains(m.TargetId);
            detected.Clear();
            if (!found) return null;
            detected.Add(m.TargetId);
        }
        int nHits = detected.Count < a.TargetCount ? detected.Count : a.TargetCount;
        // the request keeps its target list: a new one per send
        var targets = new SkillHitTarget[nHits];
        for (int i = 0; i < nHits; i++)
        {
            targets[i] = new SkillHitTarget(detected[i], 0);
            if (record != null && a.PointGroupId != 0) record.AddTarget(a.PointGroupId, detected[i]);
        }
        var req = new SkillHitRequest(m.CastId, m.Serial, m.StartPoint, owner.Position, new Vector3(0, 0, YawOf(owner.RotZ)),
            m.AttackIndex, m.SendCount, targets);
        if (nHits > 0) m.Tag.Hit?.Invoke();
        return req;
    }

    static float YawOf(float rotZ)
    {
        SkillHit.Axes(rotZ, out var f, out _);
        return YawDegrees(f);
    }

    static int IndexOf(ReadOnlySpan<HitCandidate> objects, int id)
    {
        for (int i = 0; i < objects.Length; i++)
            if (objects[i].Id == id) return i;
        return -1;
    }

    static Vector3 PositionOf(ReadOnlySpan<HitCandidate> objects, int id)
    {
        int k = IndexOf(objects, id);
        return k >= 0 ? objects[k].Position : Vector3.Zero;
    }

    /// <summary>FUN_141462490's registration, one attempt. In the exe an owner that is not the local player makes
    /// FUN_141464410 return before m_n1CC is set, so the fill is tried again each tick; here the owner is always the local
    /// player (<see cref="MagicOwner.LocalPlayer"/> false only in the checks), so the magic is dropped instead.</summary>
    static bool Register(Magic m, in MagicOwner owner, TombTagPass pass)
    {
        if (owner.Dead || owner.Stunned || !owner.LocalPlayer) return false;
        var a = m.Attack;
        if (a.Range.Shape is < 1 or > 4) return false;
        SkillHit.AreaPose(a, owner.Position, new Vector3(0, 0, owner.RotZ), out var pos, out var rot);
        return pass.Register(m.Tag, SkillHitShape.Pose(a.Range, 0, pos, rot));
    }

    public void Clear() => live.Clear();
}
