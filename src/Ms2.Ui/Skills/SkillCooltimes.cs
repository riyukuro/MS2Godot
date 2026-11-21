using System;
using System.Collections.Generic;

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

    public event Action<ulong> Started, Ended;

    public SkillCooltimes(Func<uint, SkillData> skills) { this.skills = skills; }

    /// <summary>
    /// The ReduceRate of the owner's buffs for a cooldown group: a CSEReduceSkillGroupCooltimeCtx of the group passed through
    /// Unk_SsBuffs::FUN_140972380 (skillslot.cpp:2280-2283). STILL-OPEN: no buff state feeds it, so 0.
    /// </summary>
    public Func<uint, float> GroupReduceRate = _ => 0f;
    /// <summary>Unk_SsPc::FUN_1416628f0(0x3ea, id): the flat cooldown reduce of the special options. STILL-OPEN: 0.</summary>
    public Func<uint, float> FlatReduce = _ => 0f;
    /// <summary>
    /// DAT_143aea120 (the ability rate 0x23, skillslot.cpp:2350) plus Unk_SsPc::FUN_1416628b0(0x3e9, id): the cooldown reduce
    /// rate. STILL-OPEN: 0.
    /// </summary>
    public Func<uint, float> RateReduce = _ => 0f;

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
    /// STILL-OPEN: SkillData::m_bIgnoreReduceCooldown (:2288) has no loader here, so the stats always apply.
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
            flat += FlatReduce(skillId);
            rate += RateReduce(skillId);
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
    /// MyPC::FUN_141525ff0 @0x141525ff0 (mypc.cpp:2539), called after the cast's send (MyPC::vfunc_102, :1232): the level's
    /// cooldown, scaled by StateSkill::GetMotionSpeedScale with the combo flag when the row does not show it
    /// (condition useCoolDownShowUI 0); a cooldown of 0 takes the combo window; less the time the running skill state had
    /// already played; started from the record's send tick (<see cref="Start"/>).
    /// STILL-OPEN: the record's m_n8c and DAT_143ae0fbc returns (both 0 here), and the shown cooldown's text-key path
    /// (the sequence's combo / charging_combo / loopstart-loopend keys, the swap skill's FUN_1417664e0 and the continue
    /// skill's loopend) are not ported, so the combo window is 0.
    /// </summary>
    public void StartCast(CastRecord r, float elapsed, uint now)
    {
        bool comboFlag = false;
        float cool = r.Level.Cooldown;
        float comboTime = 0f;
        if (!NearlyZero(cool) && !r.Skill.Condition.UseCoolDownShowUI)
        {
            cool /= SkillCast.MotionSpeedScale(r);
            comboFlag = true;
        }
        if (NearlyZero(cool) && comboTime > 0f)
        {
            cool = comboTime;
            comboFlag = true;
        }
        cool = cool - elapsed < 0f ? 0f : cool - elapsed;
        Start((uint)r.SendTick, r.Skill.Id, cool, comboTime, comboFlag, now);
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
