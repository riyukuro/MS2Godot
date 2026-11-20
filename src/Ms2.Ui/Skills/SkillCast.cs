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

/// <summary>CSkillRecord after Init @0x141759700 (skillrecord.cpp:175): the request's fields, the rotation (0, 0, rotZ).</summary>
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
}
