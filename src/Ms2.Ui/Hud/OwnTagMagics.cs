using System.Numerics;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>The own player as a magic's owner sees it at a tick: the position and z rotation (degrees) the area is posed from,
/// whether the owner (or the rider of the owner's ridee) is the local player, and the dead and stunned tests.
/// <paramref name="WeaponMaterial"/> is the worn weapon's item data attackMaterial (+0x10c, CEquipment::FUN_1411e1860(5, 6, 0)),
/// 0 without one: the hit sound's attack material when the attack has none (CMagic::PlayHitEffects).
/// <paramref name="Sight"/> is the field's sight range and the local player's position, the end effects' distance cull.</summary>
public readonly record struct MagicOwner(Vector3 Position, float RotZ, bool LocalPlayer, bool Dead, bool Stunned, int WeaponMaterial = 0,
    FieldSight Sight = default);

/// <summary>One hit sound an own magic's fill plays (CEffectService::PlayHitSound, effectservice.cpp:1355): the attack and
/// defence materials the hit sound table pairs, at the hit object's position (client space), at <paramref name="Volume"/>.</summary>
public readonly record struct MagicHitSound(int Attack, int Defence, Vector3 At, float Volume = 1);

/// <summary>CPhysXHelper::RayCastFromPoint on the groups 0x201 (magicdata.cpp:1786): true when a ray from <paramref name="from"/>
/// along the unit <paramref name="dir"/> hits within <paramref name="length"/>.</summary>
public delegate bool MagicRayBlocked(Vector3 from, Vector3 dir, float length);

/// <summary>CPhysXHelper::RayCastFromPoint on the groups 0x201 with its hit point: true and <paramref name="hit"/> when a ray
/// from <paramref name="from"/> along the unit <paramref name="dir"/> hits within <paramref name="length"/>.</summary>
/// <summary>
/// One effect an arrow magic's end inserts (CEffectService::InsertEffect, CMagic::FUN_14145f570 @0x14145f570,
/// magicdata.cpp:1080-1098): the effect file at <paramref name="At"/> (client space) turned by the rotation whose columns are
/// <paramref name="AxisX"/>, <paramref name="AxisY"/> and <paramref name="AxisZ"/> (client space). <paramref name="Along"/> is
/// the kind 1 transform (FUN_141467f60 of the flight's direction), else kind 0 (the magic scene's rotation).
/// </summary>
public readonly record struct MagicEndEffect(string File, bool Along, Vector3 At, Vector3 AxisX, Vector3 AxisY, Vector3 AxisZ);

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
/// Piercing arrows (bounceType 3 and 4) keep flying through their hits, forget them each piercing interval and send its
/// splash (<see cref="SkillSplashRequest"/>); bounceType 4 then flies back to the owner (state 5) and ends there.
/// STILL-OPEN: for an arrow record, the magic control flight (FUN_141464490 is m_n538, the skill's magicControl enable; with it
/// FUN_14145ec50 runs FUN_1414607e0, which follows the cast's CSkillMagicControlGuide): reachable in the client, left out on
/// purpose, as the port makes no magic for a skill with magicControl enable, so the branch is 0 for every magic made here; the bounce-off's effect for an owner other than the local player; the links of a record other than an arrow one,
/// and another owner's magics other than arrow ones (<see cref="AddRemote"/>: their fill and registration do nothing for it,
/// so they are not made), the latency maximum (0 here: the time sync's latency is not kept),
/// CFunctionCubeService::SetTagArea and MyPC::FUN_14152e0c0 after a fill are not
/// ported.
/// </summary>
public sealed class OwnTagMagics
{
    internal sealed class Magic
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
        // flag (m_n26C), the ray stop (m_n1C8), the fill done (m_n1CC), the targets hit (m_map1D8), the targets the fill skips
        // (m_map220: the same ones, but a piercing interval forgets them), the last piercing interval (m_n268), the last box
        // (m_spVal), the target record's hit index (m_n84), the owner's object id (the random seeds) and the invincible kind of
        // the last fill's hits (m_n528)
        public bool Arrow;
        public MagicPathMove Row;
        public int State, StateTick, LastTick, HoldUntil;
        public Vector3 Current, Last, Target, Predicted, Dir, DefaultDir, AxisX, AxisY, AxisZ;
        // the magic scene's rotation columns (m_spScene's SetRotate by UpdateLookAtRotation); identity until the first one
        public Vector3 SceneX = Vector3.UnitX, SceneY = Vector3.UnitY, SceneZ = Vector3.UnitZ;
        public float Remain, Total;
        // the row's vel as the magic's own copy keeps it (m_data.m_fVelocity): the bounce-off raises it
        public float Velocity;
        public bool Curve, RayStopped, Filled, HasArea;
        public HashSet<int> Hits, Excluded;
        public HitBox Area;
        public int HitIndex, OwnerId, PierceTick, Invincible;
        // a bounce chain's next record (SkillTargetRecord m_spLinked) and, for a link, the wait for the previous one's end (its
        // m_nStartTick 0, magicdata.cpp:758); Abandoned: the previous one is gone without starting it
        public Magic Linked;
        public bool Waiting, Abandoned;
    }

    readonly List<Magic> live = new();
    readonly List<HitCandidate> queried = new();
    readonly List<int> detected = new(), kept = new(), others = new();
    /// <summary>The hit effect throttle's state of the objects (FUN_14146c990), the field's one when the caller shares it
    /// (every FUN_14146c990 caller reads the object's state); its own instance by default.</summary>
    public HitEffectThrottle HitEffects { get; set; } = new();

    /// <summary>The magics that may still fill or register.</summary>
    public int Count => live.Count;

    /// <summary>The flight of the magic at <paramref name="index"/> (for the checks): its current and last points, state and target.</summary>
    internal (Vector3 Current, Vector3 Last, int State, Vector3 Target) Flight(int index) =>
        (live[index].Current, live[index].Last, live[index].State, live[index].Target);

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
    /// <paramref name="ownerPosition"/> its position (the start ray), <paramref name="rayCast"/> the physics ray and
    /// <paramref name="objects"/> the objects the record's target is found in (UpdateTargetPosition).</remarks>
    public int Add(SkillData skill, SkillAttackData attack, CastRecord r, IReadOnlyList<SkillDamageTarget> tags, int startMs,
        MagicPathTable paths, Func<string, Vector3> nodePosition, Action onHit = null, TagIdentity id = default,
        int ownerId = 0, Vector3 ownerPosition = default, MagicRayCast rayCast = null, ReadOnlySpan<HitCandidate> objects = default)
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
            var m = Make(attack, d, r, t.Serial, t.TargetId, t.Hit, startMs, dir, nodePosition, onHit, id);
            if (arrow) InitArrow(m, d, dir, ownerId, ownerPosition, rayCast, startMs, objects);
            live.Add(m);
            n++;
            if (!arrow) continue;
            // the bounce chain's links (stateskill.cpp:1356-1367): each a model whose caster is the record before it; with no
            // row for a link it has no model, so it and the links after it never start
            var prev = m;
            foreach (var link in t.Links)
            {
                var ld = paths.Get(attack.MagicPathId, link.Hit);
                if (ld == null) break;
                var lm = Make(attack, ld, r, link.Serial, link.TargetId, link.Hit, 0, dir, nodePosition, onHit, id);
                lm.Waiting = true;
                InitArrow(lm, ld, dir, ownerId, ownerPosition, rayCast, startMs, objects);
                live.Add(lm);
                prev.Linked = lm;
                prev = lm;
                n++;
            }
        }
        return n;
    }

    /// <summary>
    /// FUN_14171c1d0's models (scenestategame.cpp:5569-5588) for another owner's records <paramref name="tags"/> of the attack
    /// <paramref name="attack"/>: CMagicModel::Init(tag, prev, ptStart, 1, bServer, 0) for each root, then for each link of its
    /// chain. The tag id <paramref name="castId"/> and attack index <paramref name="attackIndex"/> are the packet's, the start
    /// <paramref name="start"/> its ptStart (m_n530 1: FUN_1414609e0 takes no fire node, and the start ray runs from ptStart,
    /// m_ptAc) and <paramref name="dir"/> its direction (each record's m_ptDir).
    /// A root starts at <paramref name="nowMs"/> when <paramref name="server"/> (bServer: its m_nStartTick the server clock),
    /// else it waits (start tick 0) for SetTargetStartTicks (<see cref="SetStart"/>); a link waits for the end of the one
    /// before it, as an own link does. With the owner's record for the cast (spCast), each root's model joins its list:
    /// <paramref name="pending"/> gets (the attack index, the magic), the magic null when the root has none.
    /// Only an arrow record (attackMethodType 1) of a skill without magic control makes a magic here: the fill and the
    /// registration of any other do nothing for an owner other than the local player.
    /// Returns the number kept.
    /// </summary>
    internal int AddRemote(SkillData skill, SkillAttackData attack, long castId, byte attackIndex, Vector3 start, Vector3 dir,
        IReadOnlyList<SkillDamageTarget> tags, bool server, int nowMs, MagicPathTable paths, int ownerId, MagicRayCast rayCast,
        ReadOnlySpan<HitCandidate> objects, List<(int Attack, Magic Model)> pending)
    {
        if (tags == null) return 0;
        bool arrow = attack != null && paths != null && attack.AttackMethodType == 1 && (skill?.MagicControlEnable ?? 0) == 0;
        int n = 0;
        foreach (var t in tags)
        {
            Magic m = null;
            var d = arrow ? paths.Get(attack.MagicPathId, t.Hit) : null;
            if (d != null)
            {
                m = Make(attack, d, castId, 0, attackIndex, start, t.Serial, t.TargetId, t.Hit, server ? nowMs : 0, dir, null, null, default);
                m.Waiting = !server;
                InitArrow(m, d, dir, ownerId, start, rayCast, nowMs, objects);
                live.Add(m);
                n++;
                var prev = m;
                foreach (var link in t.Links)
                {
                    var ld = paths.Get(attack.MagicPathId, link.Hit);
                    if (ld == null) break;
                    var lm = Make(attack, ld, castId, 0, attackIndex, start, link.Serial, link.TargetId, link.Hit, 0, dir, null, null, default);
                    lm.Waiting = true;
                    InitArrow(lm, ld, dir, ownerId, start, rayCast, nowMs, objects);
                    live.Add(lm);
                    prev.Linked = lm;
                    prev = lm;
                    n++;
                }
            }
            pending?.Add((attackIndex, m));
        }
        return n;
    }

    /// <summary>SetTargetStartTicks' store (stateskill.cpp:1370) into a model's tag: the start tick <paramref name="tick"/>,
    /// which ends a root's wait.</summary>
    internal static void SetStart(Magic m, int tick)
    {
        m.Start = tick;
        m.Waiting = false;
    }

    /// <summary>CMagicModel::Init's CMagic::Init (magicdata.cpp:2605, :672) for the record with <paramref name="serial"/>,
    /// <paramref name="targetId"/> and hit index <paramref name="hit"/> (its row <paramref name="d"/>).</summary>
    static Magic Make(SkillAttackData attack, MagicPathMove d, CastRecord r, long serial, int targetId, int hit, int startMs,
        Vector3 dir, Func<string, Vector3> nodePosition, Action onHit, TagIdentity id) =>
        Make(attack, d, r?.CastId ?? 0, r?.SendCount ?? 0, (byte)(r?.AttackIndex ?? 0), r?.Position ?? Vector3.Zero, serial, targetId,
            hit, startMs, dir, nodePosition, onHit, id);

    /// <summary>The same with the record's cast id, send count and attack index given, and the start point's
    /// <paramref name="fallback"/> when <paramref name="nodePosition"/> is null.</summary>
    static Magic Make(SkillAttackData attack, MagicPathMove d, long castId, int sendCount, byte attackIndex, Vector3 fallback,
        long serial, int targetId, int hit, int startMs, Vector3 dir, Func<string, Vector3> nodePosition, Action onHit,
        TagIdentity id) => new()
    {
        Tag = new TagRecord { BrokenOffence = attack.BrokenOffence, Hit = onHit, Id = id },
        Attack = attack, Start = startMs,
        Delay = MagicPathTable.Ms(d.DelayTime), Spawn = MagicPathTable.Ms(d.SpawnTime), Life = MagicPathTable.Ms(d.LifeTime),
        CastId = castId, Serial = serial, TargetId = targetId, SendCount = sendCount, AttackIndex = attackIndex,
        StartPoint = StartPoint(d, dir, nodePosition?.Invoke(d.FireNode) ?? fallback),
        DelayZero = MathF.Abs(d.DelayTime) < 1e-6f, IgnoreCancelAtSpawn = d.IgnoreCancelAtSpawnTime,
        Bounce2 = attack.Arrow.BounceType == 2, HitIndex = hit,
    };

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
    /// direction turned by the fire turn times distance, plus controlEndOffsetValue; then UpdateTargetPosition
    /// (<see cref="UpdateTarget"/>: the record's target in <paramref name="objects"/> at <paramref name="nowMs"/>, the aim at
    /// it).</item>
    /// <item>rayPhysxTest without ignorePhysxTestInitPosition: the ray from the start pulled back to the owner's x/y along the
    /// owner-to-start line; a hit moves the start (and the target) 5 back from it and stops the flight there (m_n1C8).</item>
    /// <item>The total time the start-to-target length over vel (0 at vel 0); the predicted point from the row's control values
    /// picked by Unk_Random(tagId + owner, tagSub + owner, their product times owner) (FUN_14145c030), along the direction,
    /// m_matRotation's z and x columns; the curve (m_n26C) when the total time is above 0, the predicted point is off the
    /// start and its x or z pick is not 0.</item>
    /// </list>
    /// </summary>
    void InitArrow(Magic m, MagicPathMove d, Vector3 dir, int ownerId, Vector3 ownerPosition, MagicRayCast rayCast, int nowMs,
        ReadOnlySpan<HitCandidate> objects)
    {
        m.Arrow = true;
        m.Row = d;
        m.Velocity = d.Velocity;
        m.Hits = new HashSet<int>();
        m.Excluded = new HashSet<int>();
        m.OwnerId = ownerId;
        m.DefaultDir = dir;
        float yaw = -(YawDegrees(dir) * 3.1415927f / 180f);
        m.AxisX = Turn(yaw, Vector3.UnitX);
        m.AxisY = Turn(yaw, Vector3.UnitY);
        m.AxisZ = Vector3.UnitZ;
        var start = m.StartPoint;
        m.Target = start + Turn(FireTurn(d, dir), d.Direction) * d.Distance + d.ControlEnd;
        m.Last = m.Current = start;
        UpdateTarget(m, nowMs, objects);
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
        SetCurve(m, start);
    }

    /// <summary>CMagic::FUN_14145d9d0 @0x14145d9d0 (magicdata.cpp:712) from <paramref name="start"/>: the total and remaining
    /// time, the predicted point and the curve flag (<see cref="InitArrow"/>).</summary>
    static void SetCurve(Magic m, Vector3 start)
    {
        var d = m.Row;
        int ownerId = m.OwnerId;
        float length = (m.Target - start).Length();
        m.Total = m.Velocity != 0f ? length / m.Velocity : 0f;
        m.Remain = m.Total;
        int sx = unchecked((int)m.CastId + ownerId), sy = unchecked((int)m.Serial + ownerId), sz = unchecked(sx * sy * ownerId);
        var rand = new DropRandom((uint)sx, (uint)sy, (uint)sz);
        float rx = RandomFloat(ref rand), ry = RandomFloat(ref rand), rz = RandomFloat(ref rand);
        var range = d.Control1 - d.Control0;
        var r = new Vector3(d.Control0.X + range.X * rx, d.Control0.Y + range.Y * ry, d.Control0.Z + range.Z * rz);
        m.Predicted = start + m.Dir * r.Y * length + m.AxisZ * r.Z + m.AxisX * r.X;
        m.Curve = m.Total > 0f && start != m.Predicted && (r.X != 0f || r.Z != 0f);
    }

    /// <summary>
    /// CMagic::UpdateTargetPosition (magicdata.cpp:1459) at the start of each flight: the target entity (GetTargetEntity) is the
    /// owner in state 5, else the record's target (m_nCasterId) when not 0; it is followed unless traceTargetDuration is set and
    /// ran out since the state tick (state 2 on). The followed target's point is the collision part centre nearest the current
    /// point (FUN_141467100), its z at most the start's z plus the attack range's height and add z (and controlEndOffsetValue's
    /// z when a control value is set); the remaining time then the current-to-target length over vel. Then
    /// <see cref="Frame"/>. Returns whether there is a target entity.
    /// STILL-OPEN: the object's body box centre stands in for its nearest collision part (FUN_141467100), and the row's
    /// m_n34 branch (traceTargetOffsetPos, PCBase::FUN_141678710) and the position plus extent fallback, both for a target
    /// without collision parts, are not ported. They are reachable in the client; here every object has a body box centre, so
    /// neither can run (unreachable because of the stand-in, not shown dead).
    /// </summary>
    static bool UpdateTarget(Magic m, int nowMs, ReadOnlySpan<HitCandidate> objects)
    {
        int self = TargetEntity(m, objects);
        bool entity = self >= 0;
        var d = m.Row;
        bool track = !(d.TraceTargetDuration != 0f && m.State >= 2
            && (uint)(m.StateTick + MagicPathTable.Ms(d.TraceTargetDuration)) < (uint)nowMs);
        if (self >= 0 && track)
        {
            var p = objects[self].Body.Center;
            var range = m.Attack.Range;
            float cap = m.StartPoint.Z + range.Height + range.Add.Z;
            if (d.Control0 != Vector3.Zero || d.Control1 != Vector3.Zero || d.ControlEnd != Vector3.Zero) cap += d.ControlEnd.Z;
            if (p.Z > cap) p.Z = cap;
            m.Target = p;
            m.Remain = m.Velocity != 0f ? (m.Target - m.Current).Length() / m.Velocity : 0f;
        }
        Frame(m);
        return entity;
    }

    /// <summary>CMagic::GetTargetEntity @0x141464360: the owner in state 5, else the record's target (m_nCasterId) when not 0;
    /// the index of that object in <paramref name="objects"/>, -1 when it is not there.</summary>
    static int TargetEntity(Magic m, ReadOnlySpan<HitCandidate> objects)
    {
        if (m.State != 5 && m.TargetId == 0) return -1;
        for (int i = 0; i < objects.Length; i++)
            if (m.State == 5 ? objects[i].IsOwner : objects[i].Id == m.TargetId) return i;
        return -1;
    }

    /// <summary>
    /// CMagic::FUN_141460700 @0x141460700, the bounceType 4 turn back (state 5) with the owner present: the fill again
    /// (m_n1CC 0) with the hit and skipped targets forgotten, the start the current point, the target the owner
    /// (<see cref="UpdateTarget"/>), and the flight's time and curve again (FUN_14145d9d0).
    /// STILL-OPEN: the record's hit set the client clears here is not kept.
    /// </summary>
    static void TurnBack(Magic m, int nowMs, ReadOnlySpan<HitCandidate> objects)
    {
        m.Filled = false;
        m.Hits.Clear();
        m.Excluded.Clear();
        m.StartPoint = m.Current;
        UpdateTarget(m, nowMs, objects);
        SetCurve(m, m.StartPoint);
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
    /// unit; then the scene's rotation (<see cref="LookAt"/>, UpdateLookAtRotation(0)).
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
        LookAt(m.Row.LookAtType, m.Dir, m.Current, m.Last, m.DefaultDir, out m.SceneX, out m.SceneY, out m.SceneZ);
    }

    /// <summary>
    /// CMagic::UpdateLookAtRotation @0x141461720 (magicdata.cpp:1526) with flag 0: the scene's rotation columns (right, axis, up)
    /// of axis = -direction for lookAtType 0 (and any other type), -direction with z 0 unitized for 1, -(current - last)
    /// unitized for 2; the record's direction when that is exactly zero; right = axis x UNIT_Z unit (UNIT_X and -UNIT_X for the
    /// axis exactly UNIT_Z and -UNIT_Z), up = right x axis unit. Unlike <see cref="Frame"/> the compares are exact.
    /// </summary>
    internal static void LookAt(int lookAtType, Vector3 dir, Vector3 current, Vector3 last, Vector3 defaultDir,
        out Vector3 x, out Vector3 y, out Vector3 z)
    {
        var axis = -dir;
        if (lookAtType == 1) { axis.Z = 0; axis = SkillHit.Unitize(axis); }
        else if (lookAtType == 2) axis = -SkillHit.Unitize(current - last);
        if (axis == Vector3.Zero) axis = defaultDir;
        Columns(axis, out x, out z);
        y = axis;
    }

    /// <summary>The right and up columns of the rotations whose y column is <paramref name="axis"/> (FUN_141467f60 and
    /// UpdateLookAtRotation, exact compares): right = axis x UNIT_Z unit (UNIT_X and -UNIT_X for UNIT_Z and -UNIT_Z), up =
    /// right x axis unit.</summary>
    static void Columns(Vector3 axis, out Vector3 right, out Vector3 up)
    {
        if (axis == Vector3.UnitZ) right = Vector3.UnitX;
        else if (axis == -Vector3.UnitZ) right = -Vector3.UnitX;
        else right = SkillHit.Unitize(Vector3.Cross(axis, Vector3.UnitZ));
        up = SkillHit.Unitize(Vector3.Cross(right, axis));
    }

    /// <summary>
    /// FUN_141467f60 @0x141467f60 (magicdata.cpp:2158): the kind 1 end effect's rotation columns. Identity for a direction
    /// exactly zero; else the columns (right, direction, up) of <see cref="Columns"/>, unless their efd ToEulerAnglesXYZ
    /// (FUN_142085840: y = -asin of the row 0 z, clamped to [-1, 1]; inside (-pi/2, pi/2) z = atan2(row 0 y, row 0 x), else z 0)
    /// gives z above 0: then the x-mirror (FUN_14146aa10 m[0][0] = -1) times MakeZRotation(2 pi - z), rows (cos, sin, 0),
    /// (-sin, cos, 0), (0, 0, 1). That drops the pitch and keeps the y column on the flight's side.
    /// </summary>
    internal static void AlongAxes(Vector3 dir, out Vector3 x, out Vector3 y, out Vector3 z)
    {
        x = Vector3.UnitX; y = Vector3.UnitY; z = Vector3.UnitZ;
        if (dir == Vector3.Zero) return;
        Columns(dir, out var right, out var up);
        x = right; y = dir; z = up;
        const float HalfPi = 1.5707964f;
        float r02 = up.X;
        float ay = -(r02 > -1f ? (1f > r02 ? MathF.Asin(r02) : HalfPi) : -HalfPi);
        float az = ay < HalfPi && ay > -HalfPi ? MathF.Atan2(dir.X, right.X) : 0f;
        if (!(0f < az)) return;
        float a = 6.2831855f - az, c = MathF.Cos(a), s = MathF.Sin(a);
        // flip * rot: row 0 negated, so rows (-c, -s, 0), (-s, c, 0), (0, 0, 1)
        x = new Vector3(-c, -s, 0);
        y = new Vector3(-s, c, 0);
        z = Vector3.UnitZ;
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
                if (m.Waiting ? m.Abandoned : (uint)nowMs >= (uint)(m.Start + m.Delay)) return true;
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
    /// <paramref name="rayCast"/> is its flight's ray and <paramref name="splashes"/> gets its piercing intervals' splashes.
    /// </summary>
    public int Tick(int nowMs, int leadMs, in MagicOwner owner, ReadOnlySpan<HitCandidate> objects, CastRecord record,
        TombTagPass pass, List<SkillHitRequest> sends, Func<int> rand, MagicRayBlocked rayBlocked, bool pkMode = false,
        MagicRayCast rayCast = null, List<SkillSplashRequest> splashes = null, List<MagicEndEffect> endEffects = null,
        List<MagicHitSound> hitSounds = null)
    {
        int registered = 0;
        // in the order the magics were made (the client runs aTags in order, stateskill.cpp:1401-1410)
        for (int i = 0; i < live.Count;)
        {
            var m = live[i];
            if (m.Arrow)
            {
                if (TickArrow(m, nowMs, owner, objects, pass, sends, splashes, endEffects, hitSounds, rand, rayCast, pkMode, ref registered)) live.RemoveAt(i);
                else i++;
                continue;
            }
            if (!Due(m, nowMs, leadMs, out bool expired))
            {
                if (expired) live.RemoveAt(i); else i++;
                continue;
            }
            live.RemoveAt(i);
            var req = Fill(m, owner, objects, record != null && record.CastId == m.CastId ? record : null, rand, rayBlocked, pkMode, hitSounds);
            if (req != null) sends?.Add(req);
            if (pass != null && Register(m, owner, pass)) registered++;
        }
        return registered;
    }

    /// <summary>
    /// CMagic::FUN_14145de80 @0x14145de80 (magicdata.cpp:755) for an arrow record: nothing before start + delay; state 0 to 1
    /// (the state tick start + delay), 1 to 2 once spawnTime passed (the state tick moves by it), both in the same tick; in
    /// state 2 a lifeTime that ran out ends it (state 3, <see cref="End"/>: dropped), else the flight
    /// (<see cref="Fly"/>), the arrow fill (FUN_141461bd0 then <see cref="ArrowFill"/>), the registration of its box
    /// (FUN_141462490) and the end check (FUN_141463fb0). The turn back of bounceType 4 (state 5, FUN_141460700) ticks the
    /// same way. m_nLastTick is the tick's time at the end. True when dropped.
    /// </summary>
    bool TickArrow(Magic m, int nowMs, in MagicOwner owner, ReadOnlySpan<HitCandidate> objects, TombTagPass pass,
        List<SkillHitRequest> sends, List<SkillSplashRequest> splashes, List<MagicEndEffect> endEffects,
        List<MagicHitSound> hitSounds, Func<int> rand, MagicRayCast rayCast, bool pkMode, ref int registered)
    {
        if (m.Waiting)
        {
            if (m.Abandoned && m.Linked != null) m.Linked.Abandoned = true;
            return m.Abandoned;
        }
        int begin = m.Start + m.Delay;
        if ((uint)nowMs < (uint)begin) return false;
        if (m.LastTick == 0) m.LastTick = nowMs;
        if ((uint)m.LastTick > (uint)nowMs) return false;
        if (m.State == 0)
        {
            // FUN_14145e960: the piercing interval counts from here (m_n268)
            m.State = 1;
            m.StateTick = begin;
            m.LastTick = begin;
            m.PierceTick = nowMs;
        }
        if (m.State == 1 && (uint)(m.StateTick + m.Spawn) <= (uint)nowMs)
        {
            m.State = 2;
            m.StateTick += m.Spawn;
            m.LastTick = m.StateTick;
        }
        if (m.State is 2 or 5)
        {
            if (m.Life != 0 && (uint)(m.StateTick + m.Life) <= (uint)nowMs)
            {
                End(m, owner, splashes, endEffects, lifeEnd: true, nowMs, objects);
                return true;
            }
            Fly(m, nowMs, objects, rayCast);
            if (!m.Filled)
            {
                var req = ArrowFill(m, nowMs, owner, objects, splashes, endEffects, hitSounds, rand, pkMode);
                if (req != null) sends?.Add(req);
            }
            // FUN_141462490: the attack's nonTarget 1 or brokenOffence, the owner the local player; the box every tick
            var a = m.Attack;
            if (pass != null && m.HasArea && owner.LocalPlayer && (a.Arrow.IsNonTargetOne || a.BrokenOffence != 0)
                && pass.Register(m.Tag, new SkillHitShape { Shape = 1, Box = m.Area }))
                registered++;
            if (m.Remain <= 0f)
            {
                // FUN_141463fb0: moveEndHoldDuration once, then bounceType 4 turns back unless it bounced off (state 5, the
                // hold not set again, so the turn back ends at its arrival), else the end (state 3, FUN_14145f570: dropped)
                int hold = m.Row.MoveEndHoldDuration;
                if (m.HoldUntil == 0 && hold > 0) m.HoldUntil = nowMs + hold;
                else if ((uint)m.HoldUntil < (uint)nowMs)
                {
                    if (m.State != 2 || m.Attack.Arrow.BounceType != 4 || m.Invincible == 2)
                    {
                        End(m, owner, splashes, endEffects, lifeEnd: false, nowMs, objects);
                        return true;
                    }
                    m.StateTick = nowMs;
                    m.State = 5;
                    TurnBack(m, nowMs, objects);
                }
            }
        }
        m.LastTick = nowMs;
        return false;
    }

    /// <summary>FUN_14028d280: milliseconds to seconds.</summary>
    static float Seconds(int ms) => ms * 0.001f;

    /// <summary>
    /// CMagic::FUN_14145ec50 @0x14145ec50 (magicdata.cpp:1094), the flight, for a magic without magic control (m_n538 0) and
    /// vel not nearly 0, after UpdateTargetPosition (<see cref="UpdateTarget"/>):
    /// <list type="bullet">
    /// <item>A curve (m_n26C): t the seconds since the state tick over the total; at 1 the target, else catmullrom set the
    /// D3DXVec3CatmullRom through (start, start, predicted, target) to controlRate and (start, predicted, target, target)
    /// after, or the Bezier point (GetBezierPoint) with the predicted point as the control; the remaining time total x (1 - t).</item>
    /// <item>Else the seconds since the last tick along the direction at vel, the target once that reaches the remaining time.</item>
    /// <item>The owner a PC (kind 1 or 3) with time left: the 2D distance from the start (the point before this step) at
    /// distance, or (not nonTarget 1, no target, nonTargetMoveDistance set) at nonTargetMoveDistance, ends the flight.</item>
    /// <item>A step that moves, not in state 5: after the start ray's hit (m_n1C8) the start, else with rayPhysxTest the ray
    /// (groups 0x201) from the current point along the direction for the step + 1; a hit stops 5 back from it (m_n1C8).</item>
    /// </list>
    /// Then CMagic::SetPosition and, with time left, the frame again.
    /// </summary>
    static void Fly(Magic m, int nowMs, ReadOnlySpan<HitCandidate> objects, MagicRayCast rayCast)
    {
        var d = m.Row;
        if (NearlyEqual(m.Velocity, 0f)) return;
        bool entity = UpdateTarget(m, nowMs, objects);
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
                p = m.Current + m.Dir * m.Velocity * step;
            }
        }
        if (!NearlyEqual(m.Remain, 0f))
        {
            float moved = Distance2D(m.Current, start);
            if (moved >= d.Distance) m.Remain = 0f;
            else if (d.NonTargetMoveDistance != 0f && !m.Attack.Arrow.IsNonTargetOne && !entity
                && d.NonTargetMoveDistance <= moved)
                m.Remain = 0f;
        }
        float delta = (p - m.Current).Length();
        if (delta > 0f && m.State != 5)
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
    /// <item>The bounce count (at least 1) caps the hits. bounceType 3 and 4 (piercing) stop once it is reached, else run the
    /// piercing interval (<see cref="PierceInterval"/>); bounceType 2 waits for the flight's end.</item>
    /// <item>StateSkill::FUN_1417e9d20: the arrow's collision box (x, the last-to-current length + y, z; collisionAdd's y and
    /// z behind and below) posed at the last point by m_matRotation over the objects of applyTarget | 4, the hit ones
    /// excluded, the attack's sort; the box kept for the registration (m_spVal) hit or not.</item>
    /// <item>Not nonTarget: only the record's target; the hits up to the count left go in the send (mode 1 sub 1: last,
    /// current, m_matRotation's y column), each with the hits before it, then kept and skipped.</item>
    /// <item>Piercing: piercingAttackMaxTargetCount (not 0) caps the skipped targets plus the new hits.</item>
    /// <item>The count reached: the fill is done (m_n1CC), and with a count of 1, not piercing, the flight ends at the current
    /// point.</item>
    /// </list>
    /// <item>nonTarget 1 with the attack's stateHitIgnore: the PCs in that state are dropped (none left: a send with no
    /// target).</item>
    /// <item>A PC among the sent hits whose invincible kind is 2 (the first PC with a kind not 0 decides) turns the arrow aside
    /// (<see cref="BounceOff"/>) instead of the end above.</item>
    /// </summary>
    SkillHitRequest ArrowFill(Magic m, int nowMs, in MagicOwner owner, ReadOnlySpan<HitCandidate> objects,
        List<SkillSplashRequest> splashes, List<MagicEndEffect> endEffects, List<MagicHitSound> hitSounds, Func<int> rand, bool pkMode)
    {
        var a = m.Attack;
        int mode = a.Arrow.BounceType, max = a.Arrow.BounceCount > 1 ? a.Arrow.BounceCount : 1;
        bool pierce = mode is 3 or 4;
        if (pierce)
        {
            if (max <= m.Hits.Count) return null;
            PierceInterval(m, nowMs, splashes, endEffects);
        }
        else if (mode == 2 && !NearlyEqual(m.Remain, 0f)) return null;
        var col = a.Arrow.Collision;
        var size = new Vector3(col.X, (m.Current - m.Last).Length() + col.Y, col.Z);
        var add = new Vector3(0f, a.Arrow.CollisionAdd.Y, a.Arrow.CollisionAdd.Z);
        m.Area = HitBox.PoseAxes(size, add, m.Last, m.AxisX, m.AxisY, m.AxisZ);
        m.HasArea = true;
        var shape = new SkillHitShape { Shape = 1, Box = m.Area };
        var dir = SkillHit.Unitize(m.AxisX);
        SkillHit.Query(objects, a.Range.ApplyTarget | 4, a.Range.IgnoreNpcRanks, queried, pkMode);
        for (int i = queried.Count - 1; i >= 0; i--)
            if (m.Excluded.Contains(queried[i].Id)) queried.RemoveAt(i);
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
        else if (a.Arrow.IsNonTargetOne && a.StateHitIgnore != 0)
        {
            // the PCs in that state are dropped; none left empties the list (the send still goes, with no target)
            for (int i = detected.Count - 1; i >= 0; i--)
            {
                int k = IndexOf(detected[i]);
                if (k >= 0 && queried[k].Kind == HitKind.Pc && queried[k].StateId == a.StateHitIgnore) detected.RemoveAt(i);
            }
        }
        int left = max - m.Hits.Count;
        int nHits = detected.Count < left ? detected.Count : left;
        if (nHits < 0) nHits = 0;
        int cap = m.Row.PiercingAttackMaxTargetCount;
        if (pierce && cap != 0 && m.Excluded.Count + nHits > cap) nHits = cap > m.Excluded.Count ? cap - m.Excluded.Count : 0;
        // m_n528: the first PC (PCBase) among the sent hits with GetInvincible not 0
        int invincible = 0;
        for (int i = 0; i < nHits; i++)
        {
            int k = IndexOf(detected[i]);
            if (k >= 0 && queried[k].Kind == HitKind.Pc && queried[k].Invincible != 0) { invincible = queried[k].Invincible; break; }
        }
        m.Invincible = invincible;
        SkillHitRequest req = null;
        if (owner.LocalPlayer)
        {
            // PlayHitEffects before the send (magicdata.cpp:1955)
            HitSounds(m, owner, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(queried), nHits, hitSounds);
            // the request keeps its target list: a new one per send
            var targets = new SkillHitTarget[nHits];
            for (int i = 0; i < nHits; i++)
            {
                targets[i] = new SkillHitTarget(detected[i], (byte)m.Hits.Count);
                m.Hits.Add(detected[i]);
                m.Excluded.Add(detected[i]);
            }
            req = new SkillHitRequest(m.CastId, m.Serial, m.Last, m.Current, m.AxisY, m.AttackIndex, m.SendCount, targets);
        }
        // FUN_141464490 (the magic control flight) is 0: a skill with magicControl enable makes no arrow magic here
        if (invincible == 2) BounceOff(m, rand ?? (() => 0), nowMs, owner, objects, endEffects);
        else if (m.Hits.Count >= max)
        {
            m.Filled = true;
            if (!pierce && max == 1)
            {
                m.Target = m.Current;
                m.Remain = 0f;
            }
        }
        return req;
    }

    /// <summary>The index in the queried objects of the object <paramref name="id"/>, -1 when it is not there.</summary>
    int IndexOf(int id)
    {
        for (int i = 0; i < queried.Count; i++)
            if (queried[i].Id == id) return i;
        return -1;
    }

    /// <summary>
    /// The arrow fill's bounce-off (magicdata.cpp:1980) off a PC whose invincible kind is 2: the fill done (m_n1CC); the side
    /// the reverse direction turned by MakeZRotation(FUN_14109ace0() * (FUN_141198810() * pi/4 + pi/4)), the first a CRT
    /// rand() in [-1, 1], the second one in [0, 1] (called in that order), unit; vel 1.2 times; the target the current point
    /// plus the side at that vel for the time left; no curve (m_n26C).
    /// Then, with CContentShutdown 0x73 off, the damage's effectHit (row +0x188) at the current point, not turned, into
    /// <paramref name="endEffects"/> for a local owner when the record's target (m_nCasterId, FindObjectById: among
    /// <paramref name="objects"/>) lets it through the hit effect throttle (<see cref="HitEffectThrottle"/>, its key the
    /// effectHit's number m_n180). The local owner's hit sounds were played before the send, so PlayHitEffects is not called
    /// again (FUN_141464410).
    /// STILL-OPEN: for an owner other than the local player, the optimizer's hide, the sight range, IsUpdateEnabled's check and
    /// the hit sounds after the effect; the shut-down branch (0x73 on, no throttle); the effect's IgnoreOptimalHideDamageHitEffect
    /// flag (m_b19c, only read by the optimizer's hide of another owner's effect).
    /// </summary>
    void BounceOff(Magic m, Func<int> rand, int nowMs, in MagicOwner owner, ReadOnlySpan<HitCandidate> objects,
        List<MagicEndEffect> endEffects)
    {
        m.Filled = true;
        float sign = -1f + 2f * rand() / 32767f;
        float unit = rand() / 32767f;
        float angle = sign * (unit * 1.5707964f * 0.5f + 0.7853982f);
        var side = SkillHit.Unitize(Turn(angle, -m.Dir));
        m.Velocity *= 1.2f;
        m.Target = m.Current + side * m.Velocity * m.Remain;
        m.Curve = false;
        if (endEffects == null || !owner.LocalPlayer || m.TargetId == 0) return;
        bool found = false;
        foreach (ref readonly var o in objects)
            if (o.Id == m.TargetId) { found = true; break; }
        if (found && HitEffects.TryPlay(m.TargetId, m.Attack.EffectHit, (uint)nowMs))
            endEffects.Add(new MagicEndEffect(m.Attack.EffectHit, false, m.Current, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ));
    }

    /// <summary>
    /// CMagic::FUN_141466da0 @0x141466da0 (magicdata.cpp:2402), a piercing arrow's interval: once piercingAttackInterval (not 0)
    /// passed since the last one (m_n268), the skipped targets are forgotten and, for an attack with a cubeMagicPathID, the
    /// splash goes out (<see cref="SkillSplashRequest"/>: the record's cast id, attack index, send count and hit index, the
    /// current point and m_matRotation's euler angles in degrees). Before the splash, an owner the optimizer does not hide (the
    /// local player always) plays the arrow's effectInvoke (row +0x160) at the current point, not turned, into
    /// <paramref name="endEffects"/> (magicdata.cpp:2389-2399).
    /// </summary>
    static void PierceInterval(Magic m, int nowMs, List<SkillSplashRequest> splashes, List<MagicEndEffect> endEffects)
    {
        uint interval = m.Row.PiercingAttackInterval;
        if (interval == 0 || (uint)m.PierceTick + interval > (uint)nowMs) return;
        m.Excluded.Clear();
        m.PierceTick = nowMs;
        if (endEffects != null && m.Attack.Arrow.EffectInvoke.Length != 0)
            endEffects.Add(new MagicEndEffect(m.Attack.Arrow.EffectInvoke, false, m.Current, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ));
        if (m.Attack.CubeMagicPathId != 0)
            splashes?.Add(new SkillSplashRequest(m.CastId, m.AttackIndex, m.SendCount, m.HitIndex, m.Current, EulerDegrees(m)));
    }

    /// <summary>
    /// CMagic::FUN_14145f570 @0x14145f570 (magicdata.cpp:1031), an arrow magic's end:
    /// <list type="bullet">
    /// <item>Its effects (<see cref="EndEffects"/>) into <paramref name="endEffects"/>.</item>
    /// <item>An explosion (the row's explosionByDestroy, else a ray stop (m_n1C8) or a fill done (m_n1CC)) of an attack with a
    /// cubeMagicPathID sends the splash (<see cref="SkillSplashRequest"/>, the same fields as a piercing interval's) when
    /// FUN_141464410 holds (the owner the local player).</item>
    /// </list>
    /// The client first looks up a cast record of the magic's serial (CMagicService::FindCast @0x14146ce30); with one it plays that
    /// record's hit at the end point (FUN_14146b440) instead of these effects. The map is filled only in FUN_14146b440's
    /// bTracked 0 branch (magicdata.cpp:2760), and both calls of it in the exe (0x14145fa75 here, 0x14171d532 the 0x3d mode 1
    /// hits) pass 1, so the lookup never finds one and is not ported.
    /// Then the bounce chain's next record (m_spLinked) starts from the current point (<see cref="Restart"/>) when its model is
    /// there (a waiting link here) and its target is among <paramref name="objects"/>; otherwise it never starts (here it is
    /// dropped, with the links after it).
    /// </summary>
    static void End(Magic m, in MagicOwner owner, List<SkillSplashRequest> splashes, List<MagicEndEffect> endEffects, bool lifeEnd,
        int nowMs, ReadOnlySpan<HitCandidate> objects)
    {
        // ShouldCullByDistance (magicdata.cpp:2263): never for the local player's own magic, else the current point farther
        // than the sight range from the local player
        if (endEffects != null && (owner.LocalPlayer || !owner.Sight.IsOutside(m.Current))) EndEffects(m, lifeEnd, endEffects);
        var next = m.Linked;
        if (next != null && next.Waiting && !next.Abandoned)
        {
            bool there = false;
            foreach (ref readonly var o in objects)
                if (o.Id == next.TargetId && next.TargetId != 0) { there = true; break; }
            if (there) Restart(next, m.Current, nowMs, objects);
            else next.Abandoned = true;
        }
        bool explosion = m.Row.ExplosionByDestroy || m.RayStopped || m.Filled;
        if (owner.LocalPlayer && m.Attack.CubeMagicPathId != 0 && explosion)
            splashes?.Add(new SkillSplashRequest(m.CastId, m.AttackIndex, m.SendCount, m.HitIndex, m.Current, EulerDegrees(m)));
    }

    /// <summary>
    /// CMagic::FUN_14145ddd0 @0x14145ddd0 (magicdata.cpp:743) and the start tick (magicdata.cpp:1147): the start, last and current
    /// points become <paramref name="at"/> (its path points cleared, SetPosition), the target is taken again
    /// (<see cref="UpdateTarget"/>) and the record starts at <paramref name="nowMs"/>, so its delay counts from there. The flight
    /// time, predicted point and curve stay those of the Add (FUN_14145d9d0 is not run again).
    /// </summary>
    static void Restart(Magic m, Vector3 at, int nowMs, ReadOnlySpan<HitCandidate> objects)
    {
        m.StartPoint = at;
        m.Last = m.Current = at;
        UpdateTarget(m, nowMs, objects);
        m.Start = nowMs;
        m.Waiting = false;
    }

    /// <summary>
    /// FUN_14145f570's effects (magicdata.cpp:1057-1145), the owner not hidden by the optimizer (FUN_14161ce60 0: an own magic's
    /// owner is shown) and not culled by distance (ShouldCullByDistance, magicdata.cpp:2263: false for the local player's own,
    /// relation 1, or without a sight range or local player; else true when the current point is farther than the range from
    /// the local player; the caller tests it with <see cref="MagicOwner.Sight"/>):
    /// <list type="bullet">
    /// <item>The arrow's explosion with the damage's effectHit not empty: effectHit, kind 1.</item>
    /// <item>At the end check (m_n52C 0): effectDestroy, kind 1, after a ray stop (m_n1C8), else effectRemain, kind 0; each
    /// only when not empty.</item>
    /// <item>At the lifeTime's end (<paramref name="lifeEnd"/>, m_n52C 1): effectRemain, kind 0, even when empty (the insert of
    /// an empty name finds no effect; it is not added here).</item>
    /// </list>
    /// Kind 1 turns by FUN_141467f60 (<see cref="AlongAxes"/> of the direction); kind 0 by the magic scene's rotation
    /// (<see cref="LookAt"/>).
    /// STILL-OPEN: the client tests relation 1 by the owner's object kind; a ridee the local player rides is taken as the local
    /// player here. The beam and link magics' culls (magicdata.cpp:1293, :1664) and UpdateCulling (:2254) are not ported.
    /// </summary>
    static void EndEffects(Magic m, bool lifeEnd, List<MagicEndEffect> endEffects)
    {
        var a = m.Attack;
        AlongAxes(m.Dir, out var ax, out var ay, out var az);
        if (a.Arrow.Explosion != 0 && a.EffectHit.Length != 0) endEffects.Add(new MagicEndEffect(a.EffectHit, true, m.Current, ax, ay, az));
        string file;
        bool along;
        if (!lifeEnd && m.RayStopped) { file = a.Arrow.EffectDestroy; along = true; }
        else { file = a.Arrow.EffectRemain; along = false; }
        if (file.Length == 0) return;
        endEffects.Add(along ? new MagicEndEffect(file, true, m.Current, ax, ay, az)
            : new MagicEndEffect(file, false, m.Current, m.SceneX, m.SceneY, m.SceneZ));
    }

    /// <summary>FUN_140a0b1f0: m_matRotation's ToEulerAnglesXYZ times -57.2957764, that is the angles of the matrix (AxisX,
    /// AxisY, AxisZ) in degrees.</summary>
    static Vector3 EulerDegrees(Magic m)
    {
        const float Deg = 57.2957764f, Edge = 1.5707964f - 1e-3f;
        float sy = Math.Clamp(m.AxisZ.X, -1f, 1f);
        float y = MathF.Asin(sy), x, z;
        if (MathF.Abs(y) < Edge)
        {
            x = MathF.Atan2(-m.AxisZ.Y, m.AxisZ.Z);
            z = MathF.Atan2(-m.AxisY.X, m.AxisX.X);
        }
        else if (y <= -Edge)
        {
            x = -MathF.Atan2(m.AxisX.Y, m.AxisY.Y);
            y = -1.5707964f;
            z = 0f;
        }
        else
        {
            x = MathF.Atan2(m.AxisX.Y, m.AxisY.Y);
            y = 1.5707964f;
            z = 0f;
        }
        return new Vector3(x, y, z) * Deg;
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
        MagicRayBlocked rayBlocked, bool pkMode, List<MagicHitSound> hitSounds = null)
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
        // PlayHitEffects before the send (magicdata.cpp:1838)
        HitSounds(m, owner, objects, nHits, hitSounds);
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

    /// <summary>
    /// CMagic::PlayHitEffects @0x141464790 (magicdata.cpp:1697) for the local owner (FUN_141bd19d0 kind 1): the attack material
    /// is the attack's (row +0x1a0), 0 taking the owner's worn weapon's (<see cref="MagicOwner.WeaponMaterial"/>, the owner a
    /// PC); each of the first <paramref name="nHits"/> detected hits but the own PC plays a hit sound at its position, an npc
    /// with its defence material, any other object with 1, each at <see cref="HitVolume"/>. No owner object plays none.
    /// STILL-OPEN: a pet is taken as not a CNpc (QueryInterface 0x6200028 is not checked).
    /// </summary>
    /// <summary>The hit sounds' volume: the client constant VolumeMyPcToNpc (Client::Constants +0x13a0, read at each hit,
    /// magicdata.cpp:1721); the host sets it from constants.xml.</summary>
    public float HitVolume = 1f;

    void HitSounds(Magic m, in MagicOwner owner, ReadOnlySpan<HitCandidate> from, int nHits, List<MagicHitSound> into)
    {
        if (into == null) return;
        int attack = m.Attack.AttackMaterial;
        if (attack == 0) attack = owner.WeaponMaterial;
        for (int i = 0; i < nHits && i < detected.Count; i++)
        {
            int k = IndexOf(from, detected[i]);
            if (k < 0 || from[k].IsOwner) continue;
            into.Add(new MagicHitSound(attack, from[k].Kind == HitKind.Npc ? from[k].DefenceMaterial : 1, from[k].Position, HitVolume));
        }
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
            // a waiting record (start tick 0) is in state 0, so ignoreCancelAtSpawnTime does not keep it
            if (!m.Waiting && m.IgnoreCancelAtSpawn && (uint)nowMs >= (uint)(m.Start + m.Delay)) continue;
            if (m.Linked != null) m.Linked.Abandoned = true;
            live.RemoveAt(i);
            n++;
        }
        return n;
    }

    public void Clear() => live.Clear();
}
