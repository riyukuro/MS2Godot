using System.Numerics;
using Ms2.Net.Packets;

namespace Ms2.Ui.Skills;

/// <summary>
/// Unk_InventorySkillRequest (skillslot.cpp:2888): what a key press asks the skill system to cast. The ctor @0x141763fa0 takes
/// the skill and level, a new cast id (GenerateUniqueId) and the server tick; motion 0, zero position and direction, rotation 0,
/// no key, b38 set, b39 / b3a clear, no item.
/// </summary>
public sealed class CastRequest
{
    public uint SkillId;
    public ushort Level;
    public long CastId;
    public int Tick;
    public byte Motion;
    public Vector3 Position, Direction;
    public float RotZ;
    /// <summary>m_n34: the key the cast came from (0 = none).</summary>
    public int Key;
    /// <summary>m_b38: the level has a sequence (HasSequenceName); the ctor sets it.</summary>
    public bool HasSequence = true;
    /// <summary>m_b39: skip the aim (CreateSkillRecord calls PCBase::vfunc_100 only when clear).</summary>
    public bool NoAim;
    /// <summary>m_b3a: copied to the record's +0xae, sent as the payload's flag.</summary>
    public bool Flag;
    /// <summary>m_n40: the quick slot item's uid.</summary>
    public long ItemUid;

    public CastRequest(uint skillId, ushort level, long castId, int tick)
    {
        SkillId = skillId; Level = level; CastId = castId; Tick = tick;
    }
}

/// <summary>CSkillRecord after Init @0x1417595d0 (skillrecord.cpp:175): the request's fields, the rotation (0, 0, rotZ).</summary>
public sealed class CastRecord
{
    public SkillData Skill;
    public SkillLevelData Level;
    public int Tick, SendTick;
    public long CastId;
    public byte Motion;
    public Vector3 Position, Direction, Rotation;
    /// <summary>+0x74: set only by the auto target (PCBase::vfunc_101); 0 otherwise.</summary>
    public float TargetDistance;
    /// <summary>m_bAE (request b3a), m_bAD (request b38).</summary>
    public bool Flag, HasSequence;
    /// <summary>m_nA4: the key; m_n1b8: the item uid; m_nA8: the skill the default skill stands in for.</summary>
    public int Key;
    public long ItemUid;
    public uint Origin;
    /// <summary>m_nSendCount: counted up only by CSkillRecord::SendSkill (the sync send, skillrecord.cpp:441); 0 for a new
    /// cast, sent with every damage send.</summary>
    public int SendCount;
    /// <summary>m_nAttackIndex: the attack StateSkill::FireCurrentAttack @0x1417f0d60 (stateskill.cpp:2264) fires last.</summary>
    public int AttackIndex;
    /// <summary>m_n98: set by the loopend of a continue round (stateskill.cpp:911); the queued press of such a record plays
    /// the loop again instead of a new cast (FUN_1417f11d0 :1589).</summary>
    public bool Charged;
    /// <summary>m_n9c: queued behind the running skill (StateSkill::SetChargeRecord :1494) until FUN_1417f11d0 lets it go
    /// (:1588); while set the record's attacks do not fire (FireAttacksAtPoint :2276).</summary>
    public bool QueuedCharge;

    /// <summary>GetCooldownKey: SkillLevelData::GetMotionKey of the record's motion.</summary>
    public ulong CooldownKey => Level.MotionKey(Motion);
}

/// <summary>The cast state CSkillSystem and the PC keep, and what the cast checks read of the body and the clock.</summary>
public sealed class CastWorld
{
    /// <summary>CSkillSystem m_fLastCastTime (a float) / m_nLastCastSkill: the last cast's game time and skill; the ctor
    /// (skillslot.cpp:1821) sets both 0.</summary>
    public float LastCastTime;
    public uint LastCastSkill;
    /// <summary>CMainSystem::GetGameTime in seconds.</summary>
    public double GameTime;
    /// <summary>
    /// The client constants m_kCastReuse (+0xbe18, float), m_kCastRepeat (+0xbe70) and m_kFlyCast.
    /// STILL-OPEN: their key names and compiled defaults are not recovered (the constants block does not name them), so the
    /// caller sets them; 0 here.
    /// </summary>
    public float CastReuse;
    public int CastRepeat, FlyCast;
    /// <summary>The body's (or the ridee's) IsJumping / IsRising.</summary>
    public bool Jumping, Rising;
    /// <summary>PCBase::vfunc_99(0) (pcbase.cpp:2544): falling for longer than 100 (the fall time) at speed over 0.3.</summary>
    public bool Falling;
    /// <summary>MyPC::GetCurStateId.</summary>
    public int StateId;
    /// <summary>The held quick slot item's count check (Unk_Inventory::HasItemCount(GetQuickSlotItemIdByKey(key), 1)).</summary>
    public Func<int, bool> KeyItemPresent = _ => true;
    /// <summary>
    /// PCBase::vfunc_100 (pcbase.cpp:2561), the aim of a new record. The caller ports what it can (position, move direction).
    /// </summary>
    public Action<CastRecord> Aim = _ => { };
}

/// <summary>The client's cast request path: CKeyTable::GetSkillIdByKey, CSkillSystem::FUN_141764e30 / CreateSkillRecord /
/// FUN_141769b70, then CastGate.Why (FUN_14176a380).</summary>
public static class SkillCast
{
    /// <summary>
    /// StateSkill::GetMotionSpeedScale @0x1417ec0d0 (stateskill.cpp:1241): the record's motion's sequence speed through the
    /// owner's vfunc_98 (CLua::CalcPcAsiR of stat 0xd, pcbase.cpp:2278); 1 without a record or motion.
    /// STILL-OPEN: CalcPcAsiR and SkillMotionData::FUN_140808000's bypass are not ported; the sequence speed is used as is.
    /// </summary>
    public static float MotionSpeedScale(CastRecord r)
    {
        if (r?.Level == null || r.Motion >= r.Level.Motions.Count) return 1f;
        return r.Level.Motions[r.Motion].SequenceSpeed;
    }

    /// <summary>The attack key's action (CKeyTable::GetSkillIdByKey, keytable.cpp:1457).</summary>
    public const int AttackAction = 0x49401;

    /// <summary>
    /// CKeyTable::GetSkillIdByKey @0x14142dad0 for a key bound to the attack (keytable.cpp:1452): none without a player or
    /// while riding, skill 0x1312d01 in states 0x1b / 0x1c, else PC::GetNormalAttackSkillId.
    /// </summary>
    public static uint AttackKeySkill(SlotWorld w, CastWorld c, WeaponAction held, WeaponActionTable table, int rightHandSkill)
    {
        if (!w.HasPc || w.IsRiding) return 0;
        if (c.StateId is 0x1b or 0x1c) return 0x1312d01;
        return (uint)NormalAttackSkillId(held, table, rightHandSkill);
    }

    /// <summary>
    /// PC::GetNormalAttackSkillId @0x14165c570 (pc.cpp:2877): the held weapon action (else FindAction(0, 0)); when its
    /// normalAttack is 0, the right-hand item's skill (<paramref name="rightHandSkill"/>: the layer 5 item, else layer 0, slot 5;
    /// 0 without one) when there is an item, else 0.
    /// </summary>
    public static int NormalAttackSkillId(WeaponAction held, WeaponActionTable table, int rightHandSkill)
    {
        var a = held ?? table.FindAction(0, 0);
        if (a == null) return 0;
        if (a.NormalAttack == 0 && rightHandSkill != 0) return rightHandSkill;
        return a.NormalAttack;
    }

    /// <summary>
    /// MyPC::FUN_141526c80 @0x141526c80 (mypc.cpp:2633): the skill the level data's changeSkill block swaps the request to, at
    /// the same level. With a running cast (<see cref="SlotWorld.RunningSkill"/>; with feature 0xca only one that is not a
    /// charging skill and has a combo output, Unk_SkillBlockA4::FUN_140803040) the first change skill the running skill's combo
    /// turns into another (GetOutputSkill) is taken; else the first change skill whose effect check is on
    /// (<see cref="SlotWorld.HasEffect"/> with the check's compare sign). True when the id or level was set.
    /// Ported callers: the own press (MyPC::vfunc_104, mypc.cpp:1728, <see cref="OwnSkill"/>) and the compact pick
    /// (skillslot.cpp:2488). STILL-OPEN, callers not ported: MyPC::FUN_141526730 (mypc.cpp:1827), FUN_141525480
    /// (mypc.cpp:2382), StateSkill::FUN_1417f0570 (stateskill.cpp:2188, the auto change), StateSkill::FUN_1417ef4a0
    /// (stateskill.cpp:3162) and Unk_Inventory::FUN_140a635f0 (inventory.cpp:1390, the item skill).
    /// </summary>
    public static bool ChangeSkill(SlotWorld w, ref uint skillId, ref ushort level)
    {
        var lv = w.Tables.Skills.GetSkillLevelData(skillId, level);
        if (lv == null) return false;
        var ch = lv.Skill.Change;
        int n = ch.CheckEffectId.Count;
        if (n == 0) return false;
        var cast = w.RunningSkill();
        if (cast != null && (!w.IsFeatureEnabled(0xca) || (!cast.ChargingSkill && cast.ComboSwapSkill != 0)))
        {
            for (int i = 0; i < n; i++)
            {
                uint next = ch.ChangeSkillAt(i);
                if (next == cast.OutputSkill(next)) continue;
                skillId = next;
                level = lv.Level;
                return true;
            }
        }
        for (int i = 0; i < n; i++)
        {
            if (!w.HasEffect(ch.CheckEffectId[i], ch.CheckEffectLevel[i], ch.CheckEffectOverlapCount[i], ch.CheckEffectCompareFunc[i]))
                continue;
            skillId = ch.ChangeSkillAt(i);
            level = lv.Level;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The own press's skill (mypc.cpp:1728-1729): <see cref="ChangeSkill"/>, then the request's m_b38 from the skill and
    /// level it lands on. The client's request keeps the ctor's true (Unk_InventorySkillRequest @0x141763fa0,
    /// skillslot.cpp:2894); the port takes the landed level's HasSequenceName (true when there is no row), which is the
    /// same for every sequence skill, so a swap from a skill with no sequence to one with a sequence casts.
    /// </summary>
    public static bool OwnSkill(SlotWorld w, ref uint skillId, ref ushort level, out bool hasSequence)
    {
        bool changed = ChangeSkill(w, ref skillId, ref level);
        hasSequence = w.Tables.Skills.GetSkillLevelData(skillId, level)?.HasSequenceName ?? true;
        return changed;
    }

    /// <summary>The result of a request: the record, or the reason (0 with no record: a refusal the client shows no code for).</summary>
    public readonly record struct Result(CastRecord Record, int Why, string Notice);

    /// <summary>
    /// CSkillSystem::FUN_141764e30 @0x141764e30 (skillslot.cpp:2084): not learnt → notice s_err_not_learn_skill; the key's quick
    /// slot item (STILL-OPEN: the quick slot item path, the level check and s_err_not_enough_level; a key with an item uid gives
    /// nothing here); CreateSkillRecord; when that fails and the skill has a default skill, a reason 6 / 7 / 2 casts the default
    /// (useDefaultSkillID, or the normal attack when there is none or the reason is 2 and useDefaultSkill is set) at its level 1,
    /// with the record's origin the asked skill.
    /// </summary>
    public static Result Request(SlotWorld w, CastWorld c, CastRequest req, Func<int, int> normalAttack, Func<int, long> keyItemUid = null)
    {
        if (w.Jobs == null || !w.Jobs.IsLearned(req.SkillId)) return new(null, 0, "s_err_not_learn_skill");
        if (keyItemUid != null && keyItemUid(req.Key) != 0) return new(null, 0, null);
        var r = CreateRecord(w, c, req);
        if (r.Record != null) return r;
        var level = CastGate.LevelData(w, req.SkillId);
        if (level == null) return r;
        var skill = level.Skill;
        if (skill.UseDefaultSkillId == 0 && !skill.UseDefaultSkill) return r;
        int why = Check(w, c, level, req);
        if (why == 0 || (why != 6 && why != 7 && why != 2)) return r;
        uint origin = 0;
        uint def = skill.UseDefaultSkillId;
        if (def == 0 || why == 2)
        {
            if (!skill.UseDefaultSkill) return r;
            def = (uint)normalAttack(0);
            origin = req.SkillId;
        }
        var defLevel = w.Tables.GetSkillData(def)?.GetLevelData(1);
        if (defLevel == null) return r;
        req.SkillId = def;
        req.Level = defLevel.Level;
        req.HasSequence = defLevel.HasSequenceName;
        var d = CreateRecord(w, c, req);
        if (d.Record != null) d.Record.Origin = origin;
        return d;
    }

    /// <summary>
    /// CSkillSystem::CreateSkillRecord @0x141765570: the request's level row; a sequence request goes to the active cast's
    /// output skill and the jump skill (GetCastSkillId) at the highest level up to the request's (FUN_14176b7c0); then
    /// FUN_141769b70 (a reason: the client's FUN_141769440 notice, null), else the record (Init) and the aim when b39 is clear.
    /// </summary>
    public static Result CreateRecord(SlotWorld w, CastWorld c, CastRequest req)
    {
        var level = w.Tables.Skills.GetSkillLevelData(req.SkillId, req.Level);
        if (level == null) return new(null, 0, null);
        if (req.HasSequence)
        {
            uint bas = w.OutputSkill(level.Skill.Id);
            if (bas != level.Skill.Id && (level = HighestLevel(w, bas, level.Level)) == null) return new(null, 0, null);
            uint jump = CastSkillId(c, level.Skill);
            if (jump != level.Skill.Id && (level = HighestLevel(w, jump, level.Level)) == null) return new(null, 0, null);
        }
        int why = Check(w, c, level, req);
        if (why != 0) return new(null, why, null);
        req.SkillId = level.Skill.Id;
        req.Level = level.Level;
        var rec = new CastRecord
        {
            Skill = level.Skill, Level = level, Tick = req.Tick, SendTick = req.Tick, CastId = req.CastId, Motion = req.Motion,
            Position = req.Position, Direction = req.Direction, Rotation = new Vector3(0, 0, req.RotZ),
            Flag = req.Flag, HasSequence = req.HasSequence, Key = req.Key, ItemUid = req.ItemUid,
        };
        if (!req.NoAim) c.Aim(rec);
        return new(rec, 0, null);
    }

    /// <summary>
    /// PCBase::vfunc_100 @0x141672910 (pcbase.cpp:2561-2603) on a record cast later than its press (StateSkill's m_spNext at
    /// the end step, stateskill.cpp:2939, and at the chain, :3170): m_k50, the Skill packet's position (FUN_1417ebc00), is the
    /// owner's position now (vfunc_18, :2602); m_k5c and m_k68 are aimed again (vfunc_105, :2593) as the press aims them, the
    /// facing <paramref name="rotZ"/> (degrees) as the direction and the rotation (0, 0, <paramref name="rotZ"/>).
    /// In the skill state (:2575-2591), a continue skill (kinds continue, +0x34) cast again under the running record
    /// <paramref name="running"/> of the same skill keeps that record's direction and rotation instead, with stateAttr
    /// moveType (+0x88) 0, or 2 with feature 0xca (<paramref name="feature0xca"/>); the position is still the owner's now.
    /// The auto target (vfunc_101, :2598-2599) is <see cref="AutoTarget"/>, which the caller runs after this.
    /// </summary>
    public static void Aim(CastRecord r, Vector3 position, float rotZ, CastRecord running, bool feature0xca)
    {
        r.Position = position;
        var d = r.Skill;
        bool keep = d.MoveType == 0 || (feature0xca && d.MoveType == 2);
        if (keep && running != null && running.Skill.Id == d.Id && d.Continue)
        {
            r.Direction = running.Direction;
            r.Rotation = running.Rotation;
            return;
        }
        float rad = rotZ * (MathF.PI / 180f);
        r.Direction = new Vector3(MathF.Sin(rad), -MathF.Cos(rad), 0);
        r.Rotation = new Vector3(0, 0, rotZ);
    }

    static readonly Func<int> NoRand = () => 0;

    /// <summary>
    /// PCBase::vfunc_101 @0x141672de0 (pcbase.cpp:2609-2690), which vfunc_100 runs for a skill with autoTargeting (+0x74)
    /// outside the camera modes 9 and 10 (:2598, the port has neither): with the record's current attack
    /// (<see cref="SkillAttackFire.AttackAt"/> at its motion and attack index) a cylinder of radius autoTargetingMaxDistance
    /// (+0x164; about 0: the attack range's distance, endWidth for a hole cylinder, SkillLevelData::GetAttackRangeValue) and
    /// height 150 (autoTargetingMaxHeight +0x168 when set, the owner's z lowered by half of it) picks the objects of the
    /// range's applyTarget kind (no npc ranks, no caster, sort 3) around the owner position plus the range offset turned
    /// with the owner's facing <paramref name="ownerRotZ"/> (not for attackMethodType 1), turned by the facing plus
    /// rangeZRotateDegree (<see cref="SkillHit.AreaPose"/>; DetectWithEulerRotation poses the cylinder at that centre).
    /// Of the hits inside autoTargetingMaxDegree (+0x160) around the record's rotation, autoTargetingType (+0x78) picks
    /// (FUN_141679800 / FUN_141679ad0, :3747 / :3808): 0 the most ahead, 3 the least ahead, 1 the nearest, 2 the farthest; a
    /// dead PC only when no living hit is left. The record's rotation turns to the pick: (0, 0, z) with z the yaw of the
    /// direction to it (forward (sin z, -cos z)), cut to a whole degree in [0, 360) (FUN_140291bc0, NormalizeDegrees casts to
    /// int). With autoTargetUseMove (+0x16c) the target distance (+0x74) is the distance to it, less 100 above 100.
    /// Returns whether a target was picked; <paramref name="yaw"/> is then the new facing, which the caller sets with n = 1
    /// (SetTargetYaw, :2681: the press, stateskill.cpp:881) and not with n = 0 (the later casts, :2939 / :3170).
    /// GameObject::vfunc_30 (the nearest anchor box centre) is the object's position here: the port has no anchor boxes.
    /// 0 B per call: <paramref name="queried"/> and <paramref name="hits"/> are the caller's.
    /// </summary>
    public static bool AutoTarget(CastRecord r, Vector3 ownerPos, float ownerRotZ, ReadOnlySpan<HitCandidate> objects,
        List<HitCandidate> queried, List<int> hits, bool pkMode, out float yaw)
    {
        yaw = 0;
        var d = r?.Skill;
        if (d == null || d.AutoTargeting == 0) return false;
        var a = SkillAttackFire.AttackAt(r, r.AttackIndex);
        if (a == null) return false;
        float radius = d.AutoTargetingMaxDistance;
        if (MathF.Abs(radius) < 1e-06f) radius = a.Range.Shape == 4 ? a.Range.EndWidth : a.Range.Distance;
        var pos = ownerPos;
        float height = 150f;
        if (MathF.Abs(d.AutoTargetingMaxHeight) >= 1e-06f)
        {
            height = d.AutoTargetingMaxHeight;
            pos.Z -= height * 0.5f;
        }
        SkillHit.AreaPose(a, pos, new Vector3(0, 0, ownerRotZ), out var center, out var rot);
        var shape = new SkillHitShape { Shape = 2, CylinderCenter = center, Radius = radius, Height = height };
        SkillHit.Query(objects, a.Range.ApplyTarget, 0, queried, pkMode);
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(queried);
        SkillHit.Detect(shape, center, rot, 3, false, false, span, NoRand, hits);
        if (hits.Count == 0) return false;
        uint mode = d.AutoTargetingType;
        if (mode > 3) return false;
        SkillHit.Axes(r.Rotation.Z, out var aim, out _);   // GameObject::RotationToDirection
        float cone = MathF.Cos(d.AutoTargetingMaxDegree / 2f * 3.1415927f / 180f);
        bool byDot = mode == 0 || mode == 3;
        int best = -1, bestDead = -1;
        float fBest = -1f, fBestDead = -1f;
        for (int i = 0; i < hits.Count; i++)
        {
            int k = IndexOf(span, hits[i]);
            if (k < 0) continue;
            ref readonly var o = ref span[k];
            var to = o.Position - ownerPos;
            to.Z = 0;
            float len = to.Length();
            to = len > 1e-06f ? to / len : Vector3.Zero;
            float dot = Vector3.Dot(aim, to);
            if (dot < cone) continue;
            float v = byDot ? dot : Vector3.Distance(o.Position, ownerPos);
            bool dead = o.Kind == HitKind.Pc && o.Dead;
            ref float f = ref dead ? ref fBestDead : ref fBest;
            // 0 and 2 keep the largest, 3 and 1 the smallest (the first, or while the best is still about -1)
            bool pick = mode == 0 || mode == 2 ? f < v : f > v || MathF.Abs(f + 1f) < 1e-06f;
            if (!pick) continue;
            f = v;
            if (dead) bestDead = k; else best = k;
        }
        if (best < 0) best = bestDead;
        if (best < 0) return false;
        var target = span[best].Position;
        var t = target - ownerPos;
        t.Z = 0;
        float tl = t.Length();
        t = tl > 1e-06f ? t / tl : Vector3.Zero;
        // the matrix (right, front, Z) of look = -t: right = (-t.y, t.x), front = (t.x, t.y); ToEulerAnglesXYZ z = atan2(m01, m00)
        float z = MathF.Atan2(t.X, -t.Y) * 180f / 3.1415927f;
        int deg = (int)z % 360;
        if (deg < 0) deg += 360;
        yaw = deg;
        r.Rotation = new Vector3(0, 0, deg);
        if (d.AutoTargetUseMove != 0)
        {
            float dist = Vector3.Distance(target, ownerPos);
            r.TargetDistance = dist > 100f ? dist - 100f : dist;
        }
        return true;
    }

    static int IndexOf(ReadOnlySpan<HitCandidate> objects, int id)
    {
        for (int i = 0; i < objects.Length; i++)
            if (objects[i].Id == id) return i;
        return -1;
    }

    /// <summary>FUN_14176b7c0 (skillslot.cpp:3343): the skill's highest level row at or below <paramref name="level"/>.</summary>
    static SkillLevelData HighestLevel(SlotWorld w, uint id, ushort level)
    {
        var s = w.Tables.GetSkillData(id);
        if (s == null) return null;
        for (int n = level; n > 0; n--)
            if (s.GetLevelData(n) is { } l) return l;
        return null;
    }

    /// <summary>GetCastSkillId (skillslot.cpp:2041): the skill's jump skill while jumping up or falling, else the skill.</summary>
    public static uint CastSkillId(CastWorld c, SkillData d) => (c.Jumping && c.Rising) || c.Falling ? d.Jump : d.Id;

    /// <summary>
    /// CSkillSystem::FUN_141769b70 @0x141769b70 (skillslot.cpp:3009), the cast reason (0 = castable):
    /// - 1 when the request's b38 is not the level's sequence flag;
    /// - a sequence skill: 1 while falling and not jumping up (jumping at all with FlyCast), unless the action is 0x1e / 0x1d /
    ///   0x20; any other skill: 1 within CastReuse of the last cast (with CastRepeat, only for the same skill);
    /// - main type 5 with cube state 4: 0x12;
    /// - an item kind: 1 when the key's item is gone (STILL-OPEN: the recovery effect checks 8 / 9 / 10 need the additional
    ///   effect recovery block, not ported);
    /// - then FUN_14176a380 (<see cref="CastGate.Why"/>, casting allowed).
    /// </summary>
    public static int Check(SlotWorld w, CastWorld c, SkillLevelData level, CastRequest req)
    {
        var d = level.Skill;
        if (req.HasSequence != level.HasSequenceName) return 1;
        if (level.HasSequenceName)
        {
            bool flag = c.Jumping && c.Rising;
            if (c.FlyCast != 0) flag = c.Jumping;
            if (!flag && c.Falling && d.ActionKind is not (0x1e or 0x1d or 0x20)) return 1;
        }
        else
        {
            double limit = c.LastCastTime + c.CastReuse;
            if (limit >= c.GameTime)
            {
                if (c.CastRepeat == 0) return 1;
                if (c.LastCastSkill == d.Id) return 1;
            }
        }
        if (d.MainType == 5 && w.CubeState(4)) return 0x12;
        if (d.IsItemKind && req.Key != 0 && !c.KeyItemPresent(req.Key)) return 1;
        return CastGate.Why(w, level, castAllowed: true);
    }

    /// <summary>
    /// MyPC::vfunc_102 @0x141521fc0 (mypc.cpp:1152-1166), the first check of an own cast after FUN_141525580: the seconds left
    /// on the skill (FUN_141765fe0 with combo windows counted) refuse it unless the charges allow a use now
    /// (CanUseRechargeSkill); in the skill state (0x10) the press is queued behind the running cast instead
    /// (StateSkill::SetChargeRecord), else it is dropped. Either way nothing is sent.
    /// </summary>
    public static CoolGate CooldownGate(SkillCooltimes c, CastRecord r, bool inSkillState, uint now)
    {
        float cool = c.Remain(r.Skill.Id, false, now);
        bool anyTime = c.CanUseRechargeSkill(r.Skill, now);
        if (anyTime || !(cool > 0.0f)) return CoolGate.Cast;
        return inSkillState ? CoolGate.Queued : CoolGate.Refused;
    }

    /// <summary>
    /// StateSkill::FUN_1417f11d0 @0x1417f11d0 (stateskill.cpp:1578), run by the skill state's update (vfunc_3, :623): the
    /// press queued behind the running skill (SetChargeRecord, mypc.cpp:1162) goes when its charge use is ready
    /// (CanUseRechargeSkill) or its cooldown has come to 0 (IsNearlyEqual).
    /// </summary>
    public static bool QueuedReady(SkillCooltimes c, CastRecord queued, uint now) =>
        c.CanUseRechargeSkill(queued.Skill, now) || SkillCooltimes.NearlyZero(c.Remain(queued.Skill.Id, false, now));

    /// <summary>CSkillSystem::FUN_141766cc0 (skillslot.cpp:2343), after a cast: a skill without a sequence records the game time
    /// (as a float) and its id for the cast reuse check.</summary>
    public static void Casted(CastWorld c, CastRecord r)
    {
        if (r.Level.HasSequenceName) return;
        c.LastCastTime = (float)c.GameTime;
        c.LastCastSkill = r.Skill.Id;
    }

    /// <summary>The mode 0 send of a record (StateSkill::FUN_1417ebc00, stateskill.cpp:2079): a fresh tick, the elapsed time.
    /// STILL-OPEN: the attached target (m_nB0 / m_strB8, feature 174) is never set here.</summary>
    public static SkillCastRequest Send(CastRecord r, int serverTick, int elapsed, bool holdAttackFeature)
    {
        r.Tick = serverTick;
        r.SendTick = serverTick;
        return new SkillCastRequest(r.CastId, serverTick, (int)r.Skill.Id, (short)r.Level.Level, r.Motion, r.Position, r.Direction,
            r.Rotation, r.TargetDistance, elapsed, r.Flag, r.ItemUid, (long)r.CooldownKey, holdAttackFeature);
    }

    /// <summary>CSkillRecord::SendSkill @0x14175a430 (skillrecord.cpp:441): a continue round's mode 3, the send tick taken
    /// (m_nSendTick) and the send count up (m_nSendCount++). The record keeps no target map here (m_targets.RemoveAll).</summary>
    public static SkillRoundRequest SendSkill(CastRecord r, int serverTick)
    {
        r.SendTick = serverTick;
        r.SendCount++;
        return new SkillRoundRequest(r.CastId, serverTick);
    }

    /// <summary>
    /// StateSkill::SendChargeState @0x1417ec2f0 (stateskill.cpp:1271), from the skill state's update (vfunc_8 :724): the mode 2
    /// sync when the record's rotation, the owner's position, the charging or the loop flag differs from the last one sent
    /// (<see cref="ChargeSync"/>, which takes the new values); null otherwise.
    /// </summary>
    public static SkillSyncRequest SyncIfChanged(ChargeSync sent, CastRecord r, Vector3 ownerPosition, bool charging, bool loop)
    {
        if (!sent.Take(r.Rotation, ownerPosition, charging, loop)) return null;
        return new SkillSyncRequest(r.CastId, (int)r.Skill.Id, (short)r.Level.Level, r.Motion, r.Position, r.Direction, r.Rotation,
            ownerPosition, charging, loop, r.TargetDistance);
    }
}

/// <summary>The outcome of <see cref="SkillCast.CooldownGate"/>: cast, queued in the skill state (vfunc_102 returns true), refused.</summary>
public enum CoolGate { Cast, Queued, Refused }

/// <summary>StateSkill's last sent sync: m_ptSentRot, m_ptSentPos, m_bSentCharging, m_bSentLoop.</summary>
public sealed class ChargeSync
{
    Vector3 rot, pos;
    bool charging, loop;

    /// <summary>
    /// The state's enter (StateSkill::vfunc_2, stateskill.cpp:484-487): the owner's rotation, a zero position and both flags
    /// off, so the first update sends unless the owner stands at the origin.
    /// </summary>
    public void Reset(Vector3 ownerRotation) { rot = ownerRotation; pos = Vector3.Zero; charging = false; loop = false; }

    /// <summary>A continue round's loopend (stateskill.cpp:899): m_bSentCharging off, so the next charging sync goes.</summary>
    public void ClearCharging() => charging = false;

    /// <summary>SendChargeState's test (:1275-1281): true when any value differs, and then the values are kept as sent.</summary>
    public bool Take(Vector3 recordRotation, Vector3 ownerPosition, bool charging, bool loop)
    {
        if (rot == recordRotation && pos == ownerPosition && this.charging == charging && this.loop == loop) return false;
        rot = recordRotation; pos = ownerPosition; this.charging = charging; this.loop = loop;
        return true;
    }
}
