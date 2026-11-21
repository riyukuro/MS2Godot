using System;
using System.Collections.Generic;
using Ms2.Formats.Model;

namespace Ms2.Ui.Skills;

/// <summary>
/// One cooldown of CSkillSystem's table (Unk_SkillCooltime, skillsystem.h:25; skillslot.cpp:1700-1770): start and end are server
/// ticks in milliseconds, the state is 1 once the end was announced.
/// </summary>
public sealed class SkillCooltime
{
    public ulong Key;
    /// <summary>m_fCombo: the combo window (FUN_1417664e0 only).</summary>
    public float Combo;
    /// <summary>m_bCombo: set by FUN_1417664e0's combo flag (a cooldown started without the reduce and without the Started event).</summary>
    public bool IsCombo;
    public uint Start, End;
    public int State;

    /// <summary>The ctor @0x141764100 / Set @0x141764230: the end is the start plus FUN_14028d260 (seconds * 1000, truncated).</summary>
    public void Set(uint start, float seconds) { Start = start; End = (uint)(int)(long)(seconds * 1000.0f) + Start; }
    /// <summary>IsOver @0x1417642c0.</summary>
    public bool IsOver(uint now) => now >= End;
    /// <summary>GetRemain @0x1417641a0: 0 once over, else FUN_14028d280 of the ticks left.</summary>
    public float Remain(uint now) => IsOver(now) ? 0f : (End - now) * 0.001f;
    /// <summary>GetTotal @0x141764200.</summary>
    public float Total => (End - Start) * 0.001f;
}

/// <summary>
/// CSkillSystem's cooldowns (m_mapCooltime, keyed by SkillData::GetCooldownKey) and charge uses (m_mapUses, keyed by
/// SkillData::GetRechargeId), as the cooldown packets and the slot state read them (skillslot.cpp).
/// <see cref="Started"/> / <see cref="Ended"/> stand for UIStatusEventDispatcher FUN_141b0e260 / FUN_141b0e2f0 with the key.
/// STILL-OPEN: the expiry's FUN_1417687e0 / FUN_141768a50 are not ported.
/// </summary>
public sealed class SkillCooltimes
{
    readonly Func<uint, SkillData> skills;
    readonly Dictionary<ulong, SkillCooltime> cooltimes = new();
    readonly Dictionary<uint, int> uses = new();
    readonly Dictionary<uint, uint> rechargeTicks = new();

    public event Action<ulong> Started, Ended;

    public SkillCooltimes(Func<uint, SkillData> skills) { this.skills = skills; }

    /// <summary>
    /// The ReduceRate of the owner's buffs for a cooldown group: a CSEReduceSkillGroupCooltimeCtx of the group passed through
    /// AdditionalCliList::FUN_140972380 (skillslot.cpp:2280-2283): the HUD feeds the own buff list's
    /// <see cref="AdditionalBuffList.ReduceSkillGroupCooltime"/>; 0 until then.
    /// </summary>
    public Func<uint, float> GroupReduceRate = _ => 0f;
    /// <summary>PC::FUN_1416628f0(0x3ea, id) (pc.cpp:2176, CEquipment::FUN_1411e13e0): the count of the skill stat option 0x3ea,
    /// the flat cooldown reduce; the HUD feeds the own buff list's <see cref="AdditionalBuffList.SkillInvokeOption"/>.</summary>
    public Func<uint, float> FlatReduce = _ => 0f;
    /// <summary>
    /// PC::FUN_1416628b0(0x3e9, id) (pc.cpp:2173, CEquipment::FUN_1411e1250): the cooldown reduce rate of the skill stat
    /// option 0x3e9; the HUD feeds the rate of the own buff list's <see cref="AdditionalBuffList.SkillInvokeOption"/>.
    /// </summary>
    public Func<uint, float> RateReduce = _ => 0f;
    /// <summary>
    /// DAT_143aea120 (skillslot.cpp:2350): CEquipment::GetAbilityRate(0x23) (equipment.cpp:731), the "skillCooldown" ability
    /// total over 1000, added to the rate before <see cref="RateReduce"/>. The client samples it at each cast use
    /// (CSkillSystem::FUN_141766cc0, skillslot.cpp:2343, from mypc.cpp:1208) and on equip and unequip (MyPC::vfunc_127 /
    /// vfunc_128, mypc.cpp:4892, :4905); the HUD sets it at the same points from the worn items and the own buffs.
    /// </summary>
    public float AbilityRate;

    public int Count => cooltimes.Count;

    public SkillCooltime Get(ulong key) => cooltimes.TryGetValue(key, out var c) ? c : null;

    /// <summary>FUN_141765de0 @0x141765de0 (:1877): every cooldown's end cleared (no event), then every charge use dropped.</summary>
    public void ResetAll()
    {
        foreach (var c in cooltimes.Values) c.End = 0;
        uses.Clear();
    }

    /// <summary>FUN_141765e70 @0x141765e70 (:1888): the skill's cooldown ends now.</summary>
    public void ClearSkill(uint skillId)
    {
        if (skills(skillId) is { } s) ClearKey(s.CooldownKey);
    }

    /// <summary>FUN_141765f60 @0x141765f60 (:1905): the key's cooldown ends now, announced first.</summary>
    public void ClearKey(ulong key)
    {
        if (!cooltimes.TryGetValue(key, out var c)) return;
        Ended?.Invoke(c.Key);
        c.End = 0;
    }

    /// <summary>FUN_141766900 @0x141766900 (:2004): a new cooldown of the key to the end tick; nothing once the end has passed.</summary>
    public void SetEnd(ulong key, uint end, uint now)
    {
        if (now >= end) return;
        cooltimes[key] = new SkillCooltime { Key = key, End = end };
        Started?.Invoke(key);
    }

    /// <summary>FUN_1417669c0 @0x1417669c0 (:2019): a new cooldown of the skill's key from the start for the seconds.</summary>
    public void SetDuration(uint start, uint skillId, float seconds)
    {
        if (skills(skillId) is not { } s) return;
        var c = new SkillCooltime { Key = s.CooldownKey };
        c.Set(start, seconds);
        cooltimes[c.Key] = c;
        Started?.Invoke(c.Key);
    }

    /// <summary>
    /// FUN_1417664e0 @0x1417664e0 (skillslot.cpp:2266): the own cast's cooldown of the skill's key from the start tick. A skill
    /// with charges still cooling keeps its cooldown; else the key's cooldown is replaced. Without the combo flag the group
    /// reduce of the buffs and the cooldown-reduce stats come off; a cooldown that comes to 0 takes the combo window instead,
    /// and only a cooldown left is set (the Started event only without the combo flag).
    /// A skill with ignoreReduceCooldown (:2288) keeps the stats out; the group reduce still comes off.
    /// </summary>
    public void Start(uint start, uint skillId, float cool, float combo, bool comboFlag, uint now)
    {
        if (skills(skillId) is not { } s) return;
        if (s.RechargeMaxCount > 0 && Remain(skillId, false, now) > 0f) return;
        cooltimes.Remove(s.CooldownKey);
        float extra = 0f;
        if (!comboFlag)
        {
            float reduce = 0f;
            if (s.CooldownGroupId != 0) reduce = GroupReduceRate(s.CooldownGroupId);
            float flat = 0f, rate = reduce;
            if (!s.IgnoreReduceCooldown)
            {
                flat += FlatReduce(skillId);
                rate += AbilityRate + RateReduce(skillId);
            }
            cool = (cool - flat) - cool * rate < 0f ? 0f : (cool - flat) - cool * rate;
        }
        if (NearlyZero(cool)) extra = combo;
        cool += extra;
        if (NearlyZero(cool)) return;
        var c = new SkillCooltime { Key = s.CooldownKey, Combo = extra, IsCombo = comboFlag };
        c.Set(start, cool);
        cooltimes[c.Key] = c;
        if (!comboFlag) Started?.Invoke(c.Key);
    }

    /// <summary>
    /// MyPC::FUN_141525ff0 @0x141525ff0 (mypc.cpp:2539), called after the cast's send (MyPC::vfunc_102, :1232) with the
    /// sequence <paramref name="seq"/> the record's motion plays (null: no motion, no actor or no such sequence, :2556-2562).
    /// - A cooldown the row does not show (condition useCoolDownShowUI 0) is scaled by StateSkill::GetMotionSpeedScale and
    ///   takes the combo flag.
    /// - Else the sequence's "combo" text key (FUN_141b6fee0: the sequence's length when it has no such key) over the speed is
    ///   the combo window. A combo row (an output skill, or comboSkill) cools the swap skill instead for the "combo" time
    ///   ("charging_combo" for a charging skill; loopstart to loopend times the motion's loopCount for a charging continue
    ///   skill), less the elapsed time, with the combo flag, and stops there when the swap skill is the cast's own; a continue
    ///   skill without a cooldown cools to its "loopend" key with the combo flag.
    /// - A cooldown of 0 takes the combo window; less the time the running skill state had already played; started from the
    ///   record's send tick (<see cref="Start"/>).
    /// STILL-OPEN: the record's m_n8c and DAT_143ae0fbc returns, and the charging continue skill's m_n98 return (:2588), are not
    /// kept: nothing here sets them (the record's ctor leaves m_n98 0, skillrecord.cpp:165).
    /// </summary>
    public void StartCast(CastRecord r, KfAnimation seq, float elapsed, uint now)
    {
        bool comboFlag = false;
        float cool = r.Level.Cooldown;
        float comboTime = 0f;
        var d = r.Skill;
        if (!NearlyZero(cool) && !d.Condition.UseCoolDownShowUI)
        {
            cool /= SkillCast.MotionSpeedScale(r);
            comboFlag = true;
        }
        else if (seq != null && r.Motion < r.Level.Motions.Count)
        {
            var motion = r.Level.Motions[r.Motion];
            float speed = SkillCast.MotionSpeedScale(r);
            comboTime = KeyTime(seq, "combo") / speed;
            uint swapTo = d.ComboSwapSkill;
            if (swapTo != 0 || d.ComboSkill)
            {
                float swapCool = KeyTime(seq, "combo") / speed;
                if (d.ChargingSkill && !d.Continue)
                {
                    swapCool = KeyTime(seq, "charging_combo") / speed;
                }
                else if (d.ChargingSkill && d.Continue)
                {
                    float start = KeyTime(seq, "loopstart") / speed;
                    float end = KeyTime(seq, "loopend") / speed;
                    swapCool = end - start;
                    swapCool *= motion.LoopCount;
                }
                swapCool = swapCool - elapsed < 0f ? 0f : swapCool - elapsed;
                Start((uint)r.SendTick, swapTo, swapCool, 0f, true, now);
                if (swapTo == r.Level.Skill.Id) return;
            }
            else if (d.Continue && NearlyZero(cool))
            {
                cool = KeyTime(seq, "loopend") / speed;
                comboFlag = true;
            }
        }
        if (NearlyZero(cool) && comboTime > 0f)
        {
            cool = comboTime;
            comboFlag = true;
        }
        cool = cool - elapsed < 0f ? 0f : cool - elapsed;
        Start((uint)r.SendTick, d.Id, cool, comboTime, comboFlag, now);
    }

    /// <summary>
    /// FUN_141b6fee0 @0x141b6fee0 (util.cpp:802) with a divisor of 1: the text key's time (NiSequenceData::GetKeyTimeAt, the
    /// first key of that name), else the sequence's length.
    /// </summary>
    public static float KeyTime(KfAnimation seq, string key)
    {
        if (seq == null) return -float.MaxValue;
        foreach (var (t, text) in seq.TextKeys)
            if (string.Equals(text, key, StringComparison.Ordinal)) return (float)t;
        return (float)seq.Duration;
    }

    /// <summary>
    /// FUN_141765fe0 @0x141765fe0 (:1918): the seconds left on the skill's key, else on any key of its cooldown group; a cooldown
    /// with a combo window does not count when <paramref name="ignoreCombo"/>.
    /// </summary>
    public float Remain(uint skillId, bool ignoreCombo, uint now) =>
        Find(skillId, ignoreCombo, now, out var c, out _) ? c.Remain(now) : 0f;

    /// <summary>FUN_141766230 @0x141766230 (:1956): the same search, with the cooldown's total and whether a group key gave it.</summary>
    public bool Remain(uint skillId, bool ignoreCombo, uint now, out float remain, out float total, out bool byGroup)
    {
        remain = total = 0; byGroup = false;
        if (!Find(skillId, ignoreCombo, now, out var c, out byGroup)) return false;
        remain = c.Remain(now); total = c.Total;
        return true;
    }

    bool Find(uint skillId, bool ignoreCombo, uint now, out SkillCooltime found, out bool byGroup)
    {
        found = null; byGroup = false;
        if (skills(skillId) is not { } s) return false;
        if (cooltimes.TryGetValue(s.CooldownKey, out var c) && !(ignoreCombo && 0f < c.Combo) && !NearlyZero(c.Remain(now)))
        {
            found = c;
            return true;
        }
        if (s.CooldownGroupId == 0) return false;
        foreach (var g in cooltimes.Values)
        {
            if (ignoreCombo && 0f < g.Combo) continue;
            if ((g.Key >> 32) == s.CooldownGroupId && !NearlyZero(g.Remain(now)))
            {
                found = g; byGroup = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>IsNearlyEqual @0x14028d470 (commonutil.cpp:669) against 0: closer than 1e-6.</summary>
    static bool NearlyZero(float f) => MathF.Abs(f) < 1e-06f;

    /// <summary>
    /// CSkillSystem::Update @0x141766af0 (:2311), the cooldown part: an announced cooldown is dropped 10 s past its end, one that
    /// is over is announced and marked.
    /// </summary>
    public void Update(uint now)
    {
        foreach (var (key, c) in cooltimes)
        {
            if (c.State != 0)
            {
                if (c.End + 10000 < now) cooltimes.Remove(key);
            }
            else if (c.IsOver(now))
            {
                Ended?.Invoke(c.Key);
                c.State = 1;
            }
        }
    }

    /// <summary>
    /// OnSkillUseFailed's cooldown part (@0x1417187b0, scenestategame.cpp:3449-3466): a positive cooldown starts under the
    /// row's recharge id (when it has one) from the packet's start tick, else no uses clears the skill; then the uses are set.
    /// The mode 5 / 6 cancel of the own state comes first in the client and is the caller's.
    /// </summary>
    public void Apply(Ms2.Net.Packets.SkillUseFailed f)
    {
        uint skillId = (uint)f.SkillId;
        if (f.Cooldown > 0.0f)
        {
            if (skills(skillId) is { } row && row.RechargeId != 0) skillId = row.RechargeId;
            SetDuration((uint)f.StartTick, skillId, f.Cooldown);
        }
        else if (f.Uses == 0)
        {
            ClearSkill(skillId);
        }
        SetRechargeUses(skillId, f.Uses);
    }

    /// <summary>
    /// OnSkillCooldown @0x14172f030 (scenestategame.cpp:7856): per entry an end of 0 clears the key, else the key cools to the
    /// end; the uses go under the key's low word (FUN_1417692f0). STILL-OPEN: the closing FUN_1419f37f0(5) UI refresh.
    /// </summary>
    public void Apply(Ms2.Net.Packets.SkillCooldown c, uint now)
    {
        foreach (var e in c.Entries)
        {
            ulong key = (ulong)e.Key;
            if (e.EndTick == 0) ClearKey(key);
            else SetEnd(key, (uint)e.EndTick, now);
            SetRechargeUses((uint)key, e.Uses);
        }
    }

    /// <summary>SetRechargeUsesById @0x141768530 / SetRechargeUses @0x1417685c0 (:2645): under the skill's recharge id.</summary>
    public void SetRechargeUses(uint skillId, int count)
    {
        if (skills(skillId) is { } s) uses[s.RechargeId] = count;
    }

    /// <summary>
    /// CSkillSystem::OnRechargeSkillUsed @0x141768900 (skillslot.cpp:2722), the end of an own cast (MyPC::vfunc_102, :1233): a
    /// charge skill counts one more use and keeps the tick of the use (FUN_141768d40 @0x141768d40, :2772).
    /// STILL-OPEN: the closing FUN_1419f37f0(5) UI refresh.
    /// </summary>
    public void OnRechargeSkillUsed(SkillData s, uint now)
    {
        if (s == null || s.RechargeMaxCount == 0) return;
        uses[s.RechargeId] = RechargeUses(s) + 1;
        rechargeTicks[s.RechargeId] = now;
    }

    /// <summary>GetRechargeTick @0x141768dd0 (:2780): the last use's tick under the recharge id, 0 without one.</summary>
    public uint RechargeTick(SkillData s) => s != null && rechargeTicks.TryGetValue(s.RechargeId, out var t) ? t : 0;

    /// <summary>
    /// CanUseRechargeSkill @0x141768bd0 (:2757): a charge skill with uses left, once defaultRechargingCooldownTime has passed
    /// since the last use (FUN_14028d260, truncated ms).
    /// </summary>
    public bool CanUseRechargeSkill(SkillData s, uint now)
    {
        if (s.RechargeMaxCount == 0) return false;
        if (RechargeRemain(s) == 0) return false;
        int last = (int)RechargeTick(s);
        int gap = (int)(long)(s.Condition.DefaultRechargingCooldownTime * 1000.0f);
        return now >= (uint)(last + gap);
    }

    /// <summary>GetRechargeUses @0x141768680 (:2669).</summary>
    public int RechargeUses(SkillData s) => s != null && uses.TryGetValue(s.RechargeId, out var n) ? n : 0;

    /// <summary>GetRechargeRemainCount @0x141768770 (:2688): the max less the uses, 0 without charges.</summary>
    public int RechargeRemain(SkillData s)
    {
        if (s == null || s.RechargeMaxCount == 0) return 0;
        uint used = (uint)RechargeUses(s);
        return s.RechargeMaxCount <= used ? 0 : (int)(s.RechargeMaxCount - used);
    }
}
