using System;
using System.Collections.Generic;

namespace Ms2.Ui.Skills;

/// <summary>
/// The own player's key pick for a cast and the key-down skill's hold limit: MyPC::FUN_141526730 @0x141526730
/// (mypc.cpp:1767-1859) with its map of since when each bound key is down (m_map17c0), and the key-down skill's first press
/// (m_n15ec, m_n15f0) in MyPC::vfunc_104 (:1711-1726) and MyPC::vfunc_53 (:2998-3004). 0 B per call once the map holds the
/// held keys.
/// STILL-OPEN: CKeyTable's short keys (IsShortKey, AddShortKey) are not loaded (no key is short here), and the bound keys come
/// in key order (the client's m_keys is a CAtlMap, hash order).
/// </summary>
public sealed class HeldKeyPoll
{
    readonly Dictionary<int, uint> since = new();
    int downKey;      // m_n15ec
    uint downTick;    // m_n15f0

    /// <summary>The keys held since (m_map17c0), for a test.</summary>
    public int HeldCount => since.Count;

    /// <summary>
    /// FUN_141526730: with any key down (MS2InputService::HasKeyHeldOrGamePad, ms2inputservice.cpp:446; no game pad here)
    /// each bound key down gets the tick it went down (GetTickCount) and each one up leaves the map, else the map empties;
    /// then the first bound key just pressed whose skill has a row; then with <paramref name="auto"/> the bound keys with a
    /// skill and a row: an item kind, recharge or immediateActive skill that is no key-down skill counts only when just
    /// pressed, any other while down, and of those the one that went down last wins (the largest tick), else the first.
    /// <paramref name="skillOf"/> gives a key's skill row (GetSkillIdByKey, then the skill table), null for none. 0 when
    /// no key is picked.
    /// </summary>
    public int Pick(List<int> bound, bool anyKeyDown, Func<int, bool> isDown, Func<int, bool> wasPressed,
                    Func<int, SkillData> skillOf, bool auto, uint tickCount)
    {
        if (anyKeyDown)
        {
            for (int i = 0; i < bound.Count; i++)
            {
                int k = bound[i];
                if (isDown(k)) { if (!since.ContainsKey(k)) since[k] = tickCount; }
                else if (since.Count != 0) since.Remove(k);
            }
        }
        else since.Clear();
        for (int i = 0; i < bound.Count; i++)
            if (wasPressed(bound[i]) && skillOf(bound[i]) != null) return bound[i];
        if (!auto) return 0;
        int slot = 0;
        uint longest = 0;
        for (int i = 0; i < bound.Count; i++)
        {
            int k = bound[i];
            if (!isDown(k) && !wasPressed(k)) continue;   // neither counts (the order of the reads does not change the pick)
            var s = skillOf(k);
            if (s == null) continue;
            bool press = s.IsItemKind || s.RechargeMaxCount != 0 || s.ImmediateActive != 0;
            bool down = press && !s.KeyIsDown ? wasPressed(k) : isDown(k);
            if (!down) continue;
            if (since.TryGetValue(k, out uint t) && t > longest) { slot = k; longest = t; }
            if (slot == 0) slot = k;
        }
        return slot;
    }

    /// <summary>
    /// MyPC::vfunc_104 (mypc.cpp:1711-1726) for a key-down skill's key: a new key starts its hold at
    /// <paramref name="serverNow"/>; the same key held past <paramref name="maxTicks"/> (KeyIsDownSkill_MaxDurationTick,
    /// 30000 by default) refuses the request (true). Key 0 (a click) and other skills pass.
    /// </summary>
    public bool HeldTooLong(SkillData s, int key, uint serverNow, uint maxTicks)
    {
        if (s == null || !s.KeyIsDown || key == 0) return false;
        if (downKey != key) { downTick = serverNow; downKey = key; return false; }
        return downTick != 0 && serverNow > downTick && serverNow - downTick > maxTicks;
    }

    /// <summary>MyPC::vfunc_53 (mypc.cpp:2998-3004), each update: the hold's key let go outside the skill state ends the
    /// hold. <paramref name="isDown"/> is asked only for the hold's key.</summary>
    public void Release(Func<int, bool> isDown, bool inSkillState)
    {
        if (downKey != 0 && downTick != 0 && !inSkillState && !isDown(downKey)) { downKey = 0; downTick = 0; }
    }
}
