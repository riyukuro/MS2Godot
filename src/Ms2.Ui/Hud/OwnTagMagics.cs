using System.Numerics;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>The own player as a magic's owner sees it at a tick: the position and z rotation (degrees) the area is posed from,
/// whether the owner (or the rider of the owner's ridee) is the local player, and the dead and stunned tests.</summary>
public readonly record struct MagicOwner(Vector3 Position, float RotZ, bool LocalPlayer, bool Dead, bool Stunned);

/// <summary>CPhysXHelper::RayCastFromPoint on the groups 0x201 (magicdata.cpp:1786): true when a ray from <paramref name="from"/>
/// along the unit <paramref name="dir"/> hits within <paramref name="length"/>.</summary>
public delegate bool MagicRayBlocked(Vector3 from, Vector3 dir, float length);

/// <summary>CPhysXHelper::RayCastFromPoint on the groups 0x201 with its hit point: true and <paramref name="hit"/> when a ray
/// from <paramref name="from"/> along the unit <paramref name="dir"/> hits within <paramref name="length"/>.</summary>
public delegate bool MagicRayCast(Vector3 from, Vector3 dir, float length, out Vector3 hit);

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
/// An attackMethodType 1 magic (an arrow record) flies instead (<see cref="TickArrow"/>): it does nothing before start + delay,
/// then each tick in state 2 runs the flight (FUN_14145ec50), the arrow fill (FUN_141465b30: the box swept from the last point to
/// the current one, its hits sent), the registration of that box every tick, and the end check (FUN_141463fb0), which drops
/// the magic once the flight ended (and moveEndHoldDuration passed).
/// When the own skill state ends, <see cref="CancelCaster"/> drops the cast's delay-0 magics that have not fired.
/// STILL-OPEN: for an arrow record, the target tracking (UpdateTargetPosition with a target entity, the record's target id
/// not 0), the magic control flight (FUN_141464490, a skill with magicControl enable not 0: those make no arrow magic here),
/// the bounce and piercing modes (bounceType 3 and 4: FUN_141466da0, piercingAttackMaxTargetCount, state 5), the PC state
/// filter (the row's +0x200), the invincible bounce-off (m_n528 2), the hit effects and the end splash; the linked records, the
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
        // CanCancel's reads of the row: delayTime nearly 0 (IsNearlyEqual, commonutil.cpp:669), ignoreCancelAtSpawnTime, and
        // the attack's arrow bounceType 2
        public bool DelayZero, IgnoreCancelAtSpawn, Bounce2;

        // the arrow record's flight (CMagic): its row, state (m_nState), state and last ticks (m_nStateTick, m_nLastTick), the
        // move end hold (m_nD0), the points (m_ptCurrent, m_ptLast, m_ptTarget, m_ptPredicted), the direction (m_ptDirection),
        // the record's direction (m_ptDefaultDir), m_matRotation's columns, the remaining and total flight seconds, the curve
        // flag (m_n26C), the ray stop (m_n1C8), the fill done (m_n1CC), the targets hit (m_map1D8, m_map220: the same while no
        // bounce is ported) and the last box (m_spVal)
        public bool Arrow;
        public MagicPathMove Row;
        public int State, StateTick, LastTick, HoldUntil;
        public Vector3 Current, Last, Target, Predicted, Dir, DefaultDir, AxisX, AxisY, AxisZ;
        public float Remain, Total;
        public bool Curve, RayStopped, Filled, HasArea;
        public HashSet<int> Hits;
        public HitBox Area;
    }

    readonly List<Magic> live = new();
    readonly List<HitCandidate> queried = new();
    readonly List<int> detected = new(), kept = new(), others = new();

    /// <summary>The magics that may still fill or register.</summary>
    public int Count => live.Count;

    /// <summary>The flight of the magic at <paramref name="index"/> (for the checks): its current and last points and state.</summary>
    internal (Vector3 Current, Vector3 Last, int State) Flight(int index) => (live[index].Current, live[index].Last, live[index].State);

    /// <summary>The magics without a cast record (no region send, the registration only).</summary>
    public int Add(SkillData skill, SkillAttackData attack, IReadOnlyList<SkillDamageTarget> tags, int startMs, MagicPathTable paths,
        Action onHit = null, TagIdentity id = default) => Add(skill, attack, null, tags, startMs, paths, null, onHit, id);

    /// <summary>
    /// FUN_1417ecce0's models for the attack <paramref name="attack"/> of <paramref name="skill"/> fired at
    /// <paramref name="startMs"/> by the record <paramref name="r"/> with the target records <paramref name="tags"/> (one magic
    /// each, its row picked by the record's index <see cref="SkillDamageTarget.Hit"/>). Returns the number kept.
    /// <paramref name="onHit"/> is each tag's <see cref="TagRecord.Hit"/> and <paramref name="id"/> its
    /// <see cref="TagRecord.Id"/>, the cast's values. <paramref name="nodePosition"/> is the owner's
    /// vf36_GetNodePosition (the owner's position when null): the start point (<see cref="StartPoint"/>).
    /// </summary>
    /// <remarks>An arrow record (attackMethodType 1) is set up by CMagic::Init's FUN_1414609e0 and FUN_14145d9d0
    /// (<see cref="InitArrow"/>): <paramref name="ownerId"/> is the owner's object id (the random seeds),
    /// <paramref name="ownerPosition"/> its position (the start ray) and <paramref name="rayCast"/> the physics ray.</remarks>
    public int Add(SkillData skill, SkillAttackData attack, CastRecord r, IReadOnlyList<SkillDamageTarget> tags, int startMs,
        MagicPathTable paths, Func<string, Vector3> nodePosition, Action onHit = null, TagIdentity id = default,
        int ownerId = 0, Vector3 ownerPosition = default, MagicRayCast rayCast = null)
    {
        if (attack == null || tags == null || paths == null) return 0;
        int control = skill?.MagicControlEnable ?? 0;
        bool arrow = attack.AttackMethodType == 1;
        if (control == 2 || (arrow && control != 0)) return 0;
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
            var m = new Magic
            {
                Tag = new TagRecord { BrokenOffence = attack.BrokenOffence, Hit = onHit, Id = id },
                Attack = attack, Start = startMs,
                Delay = MagicPathTable.Ms(d.DelayTime), Spawn = MagicPathTable.Ms(d.SpawnTime), Life = MagicPathTable.Ms(d.LifeTime),
                CastId = r?.CastId ?? 0, Serial = t.Serial, TargetId = t.TargetId, SendCount = r?.SendCount ?? 0,
                AttackIndex = (byte)(r?.AttackIndex ?? 0),
                StartPoint = StartPoint(d, dir, nodePosition?.Invoke(d.FireNode) ?? r?.Position ?? Vector3.Zero),
                DelayZero = MathF.Abs(d.DelayTime) < 1e-6f, IgnoreCancelAtSpawn = d.IgnoreCancelAtSpawnTime,
                Bounce2 = attack.Arrow.BounceType == 2,
            };
            if (arrow) InitArrow(m, d, dir, ownerId, ownerPosition, rayCast);
            live.Add(m);
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
        return start + Turn(FireTurn(d, dir), d.FireOffset);
    }

    /// <summary>FUN_1414609e0's matRot angle: fYaw + pi - dirRotZ with rotation set, else -dirRotZ.</summary>
    static float FireTurn(MagicPathMove d, Vector3 dir)
    {
        float rotRad = d.DirRotZ * 3.1415927f / 180f;
        return d.Rotation != 0 ? -(YawDegrees(dir) * 3.1415927f / 180f) + 3.1415927f - rotRad : -rotRad;
    }

    /// <summary>efd::Matrix3::MakeZRotation(<paramref name="t"/>) times <paramref name="v"/>: Gamebryo's z rotation turns by
    /// -t counter-clockwise (SkillHit.Axes).</summary>
    static Vector3 Turn(float t, Vector3 v)
    {
        float c = MathF.Cos(t), s = MathF.Sin(t);
        return new Vector3(v.X * c + v.Y * s, -v.X * s + v.Y * c, v.Z);
    }

    /// <summary>
    /// CMagic::FUN_1414609e0 and FUN_14145d9d0 (magicdata.cpp:1397, :712) for an arrow record:
    /// <list type="bullet">
    /// <item>m_matRotation MakeZRotation(DirectionToNegYawRadians(<paramref name="dir"/>)); the target the start plus the row's
    /// direction turned by the fire turn times distance, plus controlEndOffsetValue; then UpdateTargetPosition (no target
    /// entity: FUN_1414614d0, <see cref="Frame"/>).</item>
    /// <item>rayPhysxTest without ignorePhysxTestInitPosition: the ray from the start pulled back to the owner's x/y along the
    /// owner-to-start line; a hit moves the start (and the target) 5 back from it and stops the flight there (m_n1C8).</item>
    /// <item>The total time the start-to-target length over vel (0 at vel 0); the predicted point from the row's control values
    /// picked by Unk_Random(tagId + owner, tagSub + owner, their product times owner) (FUN_14145c030), along the direction,
    /// m_matRotation's z and x columns; the curve (m_n26C) when the total time is above 0, the predicted point is off the
    /// start and its x or z pick is not 0.</item>
    /// </list>
    /// </summary>
    void InitArrow(Magic m, MagicPathMove d, Vector3 dir, int ownerId, Vector3 ownerPosition, MagicRayCast rayCast)
    {
        m.Arrow = true;
        m.Row = d;
        m.Hits = new HashSet<int>();
        m.DefaultDir = dir;
        float yaw = -(YawDegrees(dir) * 3.1415927f / 180f);
        m.AxisX = Turn(yaw, Vector3.UnitX);
        m.AxisY = Turn(yaw, Vector3.UnitY);
        m.AxisZ = Vector3.UnitZ;
        var start = m.StartPoint;
        m.Target = start + Turn(FireTurn(d, dir), d.Direction) * d.Distance + d.ControlEnd;
        m.Last = m.Current = start;
        Frame(m);
        if (m.Attack.Arrow.RayPhysxTest != 0 && !d.IgnorePhysxTestInitPosition)
        {
            var delta = start - ownerPosition;
            delta.Z = 0;
            float len = delta.Length();
            if (len > 0f)
            {
                delta /= len;
                if (rayCast != null && rayCast(start - delta * len, delta, len, out var hit))
                {
                    start = hit - delta * 5f;
                    m.StartPoint = start;
                    m.Target = start;
                    SetPosition(m, start);
                    m.RayStopped = true;
                }
            }
        }
        float length = (m.Target - start).Length();
        m.Total = d.Velocity != 0f ? length / d.Velocity : 0f;
        m.Remain = m.Total;
        int sx = unchecked((int)m.CastId + ownerId), sy = unchecked((int)m.Serial + ownerId), sz = unchecked(sx * sy * ownerId);
        var rand = new DropRandom((uint)sx, (uint)sy, (uint)sz);
        float rx = RandomFloat(ref rand), ry = RandomFloat(ref rand), rz = RandomFloat(ref rand);
        var range = d.Control1 - d.Control0;
        var r = new Vector3(d.Control0.X + range.X * rx, d.Control0.Y + range.Y * ry, d.Control0.Z + range.Z * rz);
        m.Predicted = start + m.Dir * r.Y * length + m.AxisZ * r.Z + m.AxisX * r.X;
        m.Curve = m.Total > 0f && start != m.Predicted && (r.X != 0f || r.Z != 0f);
    }

    /// <summary>FUN_1407680d0: a float in [0, 1) from the generator's next value's low 23 bits.</summary>
    static float RandomFloat(ref DropRandom rand) =>
        BitConverter.Int32BitsToSingle((int)((rand.Next() & 0x7fffff) | 0x3f800000)) - 1f;

    /// <summary>CMagic::SetPosition @magicdata.cpp:2194: the current point becomes the last.</summary>
    static void SetPosition(Magic m, Vector3 p)
    {
        m.Last = m.Current;
        m.Current = p;
    }

    /// <summary>
    /// CMagic::FUN_1414614d0 @0x1414614d0 (magicdata.cpp:1497): unless the target is the current point, the direction the unit
    /// current-to-target and m_matRotation the columns (right, axis, up) of axis = -direction (the record's direction when that
    /// is nearly zero), right = axis x UNIT_Z unit (UNIT_X and -UNIT_X for the axis nearly UNIT_Z and -UNIT_Z), up = right x axis
    /// unit.
    /// </summary>
    static void Frame(Magic m)
    {
        if (m.Target == m.Current) return;
        m.Dir = SkillHit.Unitize(m.Target - m.Current);
        var axis = -m.Dir;
        if (NearlyEqual(axis, Vector3.Zero)) axis = m.DefaultDir;
        Vector3 right;
        if (NearlyEqual(axis, Vector3.UnitZ)) right = Vector3.UnitX;
        else if (NearlyEqual(axis, -Vector3.UnitZ)) right = -Vector3.UnitX;
        else right = SkillHit.Unitize(Vector3.Cross(axis, Vector3.UnitZ));
        m.AxisX = right;
        m.AxisY = axis;
        m.AxisZ = SkillHit.Unitize(Vector3.Cross(right, axis));
    }

    /// <summary>IsNearlyEqual (commonutil.cpp:669): |a - b| below 1e-6.</summary>
    static bool NearlyEqual(float a, float b) => MathF.Abs(a - b) < 1e-06f;

    /// <summary>IsNearlyEqualPoint3 (commonutil.cpp:685): each component nearly equal.</summary>
    static bool NearlyEqual(Vector3 a, Vector3 b) => NearlyEqual(a.X, b.X) && NearlyEqual(a.Y, b.Y) && NearlyEqual(a.Z, b.Z);

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
        {
            if (m.Arrow)
            {
                if ((uint)nowMs >= (uint)(m.Start + m.Delay)) return true;
                continue;
            }
            if (Due(m, nowMs, leadMs, out bool expired) || expired) return true;
        }
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
    /// An arrow magic ticks by <see cref="TickArrow"/> (no lead: it starts at start + delay) and stays until its flight ends;
    /// <paramref name="rayCast"/> is its flight's ray.
    /// </summary>
    public int Tick(int nowMs, int leadMs, in MagicOwner owner, ReadOnlySpan<HitCandidate> objects, CastRecord record,
        TombTagPass pass, List<SkillHitRequest> sends, Func<int> rand, MagicRayBlocked rayBlocked, bool pkMode = false,
        MagicRayCast rayCast = null)
    {
        int registered = 0;
        // in the order the magics were made (the client runs aTags in order, stateskill.cpp:1401-1410)
        for (int i = 0; i < live.Count;)
        {
            var m = live[i];
            if (m.Arrow)
            {
                if (TickArrow(m, nowMs, owner, objects, pass, sends, rand, rayCast, pkMode, ref registered)) live.RemoveAt(i);
                else i++;
                continue;
            }
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

    /// <summary>
    /// CMagic::FUN_14145de80 @0x14145de80 (magicdata.cpp:755) for an arrow record: nothing before start + delay; state 0 to 1
    /// (the state tick start + delay), 1 to 2 once spawnTime passed (the state tick moves by it), both in the same tick; in
    /// state 2 a lifeTime that ran out ends it (state 3, the end splash FUN_14145f570 not ported: dropped), else the flight
    /// (<see cref="Fly"/>), the arrow fill (FUN_141461bd0 then <see cref="ArrowFill"/>), the registration of its box
    /// (FUN_141462490) and the end check (FUN_141463fb0). m_nLastTick is the tick's time at the end. True when dropped.
    /// </summary>
    bool TickArrow(Magic m, int nowMs, in MagicOwner owner, ReadOnlySpan<HitCandidate> objects, TombTagPass pass,
        List<SkillHitRequest> sends, Func<int> rand, MagicRayCast rayCast, bool pkMode, ref int registered)
    {
        int begin = m.Start + m.Delay;
        if ((uint)nowMs < (uint)begin) return false;
        if (m.LastTick == 0) m.LastTick = nowMs;
        if ((uint)m.LastTick > (uint)nowMs) return false;
        if (m.State == 0)
        {
            m.State = 1;
            m.StateTick = begin;
            m.LastTick = begin;
        }
        if (m.State == 1 && (uint)(m.StateTick + m.Spawn) <= (uint)nowMs)
        {
            m.State = 2;
            m.StateTick += m.Spawn;
            m.LastTick = m.StateTick;
        }
        if (m.State == 2)
        {
            if (m.Life != 0 && (uint)(m.StateTick + m.Life) <= (uint)nowMs) return true;
            Fly(m, nowMs, rayCast);
            if (!m.Filled)
            {
                var req = ArrowFill(m, owner, objects, rand, pkMode);
                if (req != null) sends?.Add(req);
            }
            // FUN_141462490: the attack's nonTarget 1 or brokenOffence, the owner the local player; the box every tick
            var a = m.Attack;
            if (pass != null && m.HasArea && owner.LocalPlayer && (a.Arrow.IsNonTargetOne || a.BrokenOffence != 0)
                && pass.Register(m.Tag, new SkillHitShape { Shape = 1, Box = m.Area }))
                registered++;
            if (m.Remain <= 0f)
            {
                // FUN_141463fb0: moveEndHoldDuration once, then the end (state 3: dropped)
                int hold = m.Row.MoveEndHoldDuration;
                if (m.HoldUntil == 0 && hold > 0) m.HoldUntil = nowMs + hold;
                else if ((uint)m.HoldUntil < (uint)nowMs) return true;
            }
        }
        m.LastTick = nowMs;
        return false;
    }

    /// <summary>FUN_14028d280: milliseconds to seconds.</summary>
    static float Seconds(int ms) => ms * 0.001f;

    /// <summary>
    /// CMagic::FUN_14145ec50 @0x14145ec50 (magicdata.cpp:1094), the flight, for a magic without magic control (m_n538 0) and
    /// vel not nearly 0, after UpdateTargetPosition (no target entity: <see cref="Frame"/>):
    /// <list type="bullet">
    /// <item>A curve (m_n26C): t the seconds since the state tick over the total; at 1 the target, else catmullrom set the
    /// D3DXVec3CatmullRom through (start, start, predicted, target) to controlRate and (start, predicted, target, target)
    /// after, or the Bezier point (GetBezierPoint) with the predicted point as the control; the remaining time total x (1 - t).</item>
    /// <item>Else the seconds since the last tick along the direction at vel, the target once that reaches the remaining time.</item>
    /// <item>The owner a PC (kind 1 or 3) with time left: the 2D distance from the start (the point before this step) at
    /// distance, or (not nonTarget 1, no target, nonTargetMoveDistance set) at nonTargetMoveDistance, ends the flight.</item>
    /// <item>A step that moves: after the start ray's hit (m_n1C8) the start, else with rayPhysxTest the ray (groups 0x201)
    /// from the current point along the direction for the step + 1; a hit stops 5 back from it (m_n1C8).</item>
    /// </list>
    /// Then CMagic::SetPosition and, with time left, the frame again.
    /// </summary>
    static void Fly(Magic m, int nowMs, MagicRayCast rayCast)
    {
        var d = m.Row;
        if (NearlyEqual(d.Velocity, 0f)) return;
        Frame(m);
        var start = m.StartPoint;
        Vector3 p;
        if (m.Curve)
        {
            float t = Seconds(nowMs - m.StateTick) / m.Total;
            if (t >= 1f)
            {
                m.Remain = 0f;
                p = m.Target;
            }
            else
            {
                if (d.CatmullRom != 0)
                    p = t >= d.ControlRate
                        ? CatmullRom(start, m.Predicted, m.Target, m.Target, (t - d.ControlRate) / (1f - d.ControlRate))
                        : CatmullRom(start, start, m.Predicted, m.Target, t / d.ControlRate);
                else p = Bezier(start, m.Predicted, m.Target, t);
                m.Remain = m.Total * (1f - t);
            }
        }
        else
        {
            float step = Seconds(nowMs - m.LastTick);
            if (step >= m.Remain)
            {
                p = m.Target;
                m.Remain = 0f;
            }
            else
            {
                m.Remain -= step;
                p = m.Current + m.Dir * d.Velocity * step;
            }
        }
        if (!NearlyEqual(m.Remain, 0f))
        {
            float moved = Distance2D(m.Current, start);
            if (moved >= d.Distance) m.Remain = 0f;
            else if (d.NonTargetMoveDistance != 0f && !m.Attack.Arrow.IsNonTargetOne && m.TargetId == 0
                && d.NonTargetMoveDistance <= moved)
                m.Remain = 0f;
        }
        float delta = (p - m.Current).Length();
        if (delta > 0f)
        {
            if (m.RayStopped)
            {
                p = start;
                m.Target = start;
                m.Remain = 0f;
            }
            else if (m.Attack.Arrow.RayPhysxTest != 0 && rayCast != null && rayCast(m.Current, m.Dir, delta + 1f, out var hit))
            {
                p = hit - m.Dir * 5f;
                m.Target = p;
                m.Remain = 0f;
                m.RayStopped = true;
            }
        }
        SetPosition(m, p);
        if (m.Remain > 0f) Frame(m);
    }

    static float Distance2D(Vector3 a, Vector3 b)
    {
        float x = a.X - b.X, y = a.Y - b.Y;
        return MathF.Sqrt(x * x + y * y);
    }

    /// <summary>D3DXVec3CatmullRom.</summary>
    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float s)
    {
        float s2 = s * s, s3 = s2 * s;
        return 0.5f * (2f * p1 + (p2 - p0) * s + (2f * p0 - 5f * p1 + 4f * p2 - p3) * s2 + (3f * p1 - p0 - 3f * p2 + p3) * s3);
    }

    /// <summary>GetBezierPoint (commonutil.cpp): the line from start to end when the control is either, else the quadratic.</summary>
    static Vector3 Bezier(Vector3 start, Vector3 control, Vector3 end, float t)
    {
        if (start == control || end == control) return start + (end - start) * t;
        float u = 1f - t;
        return u * u * start + 2f * t * u * control + t * t * end;
    }

    /// <summary>
    /// CMagic::FUN_141465b30 @0x141465b30 (magicdata.cpp:1950), the arrow fill, under FUN_141461bd0 (magicControl enable 2 and
    /// m_n1CC skip it):
    /// <list type="bullet">
    /// <item>bounceType 2 waits for the flight's end; the bounce count (at least 1) caps the hits.</item>
    /// <item>StateSkill::FUN_1417e9d20: the arrow's collision box (x, the last-to-current length + y, z; collisionAdd's y and
    /// z behind and below) posed at the last point by m_matRotation over the objects of applyTarget | 4, the hit ones
    /// excluded, the attack's sort; the box kept for the registration (m_spVal) hit or not.</item>
    /// <item>Not nonTarget: only the record's target; the hits up to the count left go in the send (mode 1 sub 1: last,
    /// current, m_matRotation's y column), each with the hits before it, then kept.</item>
    /// <item>The count reached: the fill is done (m_n1CC), and with a count of 1 the flight ends at the current point.</item>
    /// </list>
    /// STILL-OPEN: bounceType 3 and 4 (FUN_141466da0, the piercing cap), the PC state filter (+0x200) and the invincible
    /// bounce-off (m_n528).
    /// </summary>
    SkillHitRequest ArrowFill(Magic m, in MagicOwner owner, ReadOnlySpan<HitCandidate> objects, Func<int> rand, bool pkMode)
    {
        var a = m.Attack;
        int mode = a.Arrow.BounceType, max = a.Arrow.BounceCount > 1 ? a.Arrow.BounceCount : 1;
        if (mode is 3 or 4) return null;
        if (mode == 2 && !NearlyEqual(m.Remain, 0f)) return null;
        var col = a.Arrow.Collision;
        var size = new Vector3(col.X, (m.Current - m.Last).Length() + col.Y, col.Z);
        var add = new Vector3(0f, a.Arrow.CollisionAdd.Y, a.Arrow.CollisionAdd.Z);
        m.Area = HitBox.PoseAxes(size, add, m.Last, m.AxisX, m.AxisY, m.AxisZ);
        m.HasArea = true;
        var shape = new SkillHitShape { Shape = 1, Box = m.Area };
        var dir = SkillHit.Unitize(m.AxisX);
        SkillHit.Query(objects, a.Range.ApplyTarget | 4, a.Range.IgnoreNpcRanks, queried, pkMode);
        for (int i = queried.Count - 1; i >= 0; i--)
            if (m.Hits.Contains(queried[i].Id)) queried.RemoveAt(i);
        SkillHit.Detect(shape, m.Last, dir, Vector3.Cross(dir, Vector3.UnitZ), a.Direction, a.Range.IncludeCaster != 0, false,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(queried), rand ?? (() => 0), detected);
        if (detected.Count == 0) return null;
        if (!a.Arrow.IsNonTargetOne && a.Arrow.NonTarget == 0)
        {
            bool found = detected.Contains(m.TargetId);
            detected.Clear();
            if (!found) return null;
            detected.Add(m.TargetId);
        }
        if (!owner.LocalPlayer) return null;
        int left = max - m.Hits.Count;
        int nHits = detected.Count < left ? detected.Count : left;
        if (nHits < 0) nHits = 0;
        // the request keeps its target list: a new one per send
        var targets = new SkillHitTarget[nHits];
        for (int i = 0; i < nHits; i++)
        {
            targets[i] = new SkillHitTarget(detected[i], (byte)m.Hits.Count);
            m.Hits.Add(detected[i]);
        }
        var req = new SkillHitRequest(m.CastId, m.Serial, m.Last, m.Current, m.AxisY, m.AttackIndex, m.SendCount, targets);
        if (m.Hits.Count >= max)
        {
            m.Filled = true;
            if (max == 1)
            {
                m.Target = m.Current;
                m.Remain = 0f;
            }
        }
        return req;
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

    /// <summary>
    /// CMagicService::RemoveCasterModels(<paramref name="castId"/>, 0, 0) @0x14146c6c0 (magicdata.cpp:2950), from StateSkill's
    /// exits vfunc_5 and vfunc_7 (stateskill.cpp:520, :582): the cast's magics that CMagicModel::CanCancel @0x141467dd0
    /// (:2574) lets go are dropped without end effects. CanCancel: the cast id matches, the attack's arrow bounceType is not 2,
    /// not state 1 with ignoreCancelAtSpawnTime, state 0 or 1, and delayTime nearly 0. A kept magic other than an arrow one has
    /// not fired, so its state is 0 or 1 (CMagic::FUN_14145de80 goes on to state 2 and fires in the same tick,
    /// magicdata.cpp:772-785); it is in state 1 once a tick saw start + delay, which the client ticks every frame: from
    /// start + delay on. A flying arrow (state 2) stays.
    /// Returns the number dropped.
    /// </summary>
    public int CancelCaster(long castId, int nowMs)
    {
        int n = 0;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var m = live[i];
            if (m.CastId != castId || m.Bounce2 || !m.DelayZero || m.State >= 2) continue;
            if (m.IgnoreCancelAtSpawn && (uint)nowMs >= (uint)(m.Start + m.Delay)) continue;
            live.RemoveAt(i);
            n++;
        }
        return n;
    }

    public void Clear() => live.Clear();
}
