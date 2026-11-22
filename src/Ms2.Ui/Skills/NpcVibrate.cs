using System.Numerics;

namespace Ms2.Ui.Skills;

/// <summary>
/// The shake of a hit npc: CNpcVibrate (CNpc +0xe18, unit_14108e1e0_npcactorandvibrate.cpp:417-466). A hit by the own
/// player arms it (<see cref="OnHit"/>, CNpc::vfunc_33) and the npc's update moves its drawn position by
/// <see cref="Update"/> each frame until the end time (CNpc update :2419-2424 sets the entity position property
/// 0xfd95ca52 to the moved point; the npc's own position, vfunc_18, is not changed). Holds no objects, so a hit and a
/// frame allocate nothing.
/// </summary>
public sealed class NpcVibrate
{
    /// <summary>m_fStart, m_fEnd: the game time the shake was armed at and ends at, in seconds.</summary>
    public float Start, End;
    /// <summary>m_fAmp: the shake's distance, in field units.</summary>
    public float Amp;
    /// <summary>m_bActive (IsActive @0x14108e280).</summary>
    public bool Active;

    /// <summary>CNpcVibrate::Start (@0x14108e2a0).</summary>
    public void Arm(float start, float end, float amp)
    {
        Start = start; End = end; Amp = amp; Active = true;
    }

    /// <summary>CDamageRecord::GetTotalDamage (@0x14175ac30, skillrecord.cpp:565): the sum of the record's hit values, as
    /// read (AddDamage keeps every pair, a miss too).</summary>
    public static long TotalDamage(Ms2.Net.Packets.Hit[] hits)
    {
        long total = 0;
        for (int k = 0; k < hits.Length; k++) total += hits[k].Damage;
        return total;
    }

    /// <summary>
    /// The shake part of CNpc::vfunc_33 (@0x141097c40, :1858-1870): a hit whose total (CDamageRecord::GetTotalDamage,
    /// skillrecord.cpp:565, the sum of the packet's hit values) is below 0, by the own player (owner kind 1,
    /// WorldService::FUN_141bd19d0), arms the shake for the row's damagedVibrateDuration with its damagedVibrateAmp times the
    /// attack's damagedVibrateRatio (the row's amp alone when the record has no attack). A duration or amp of 0 or less
    /// arms nothing. True when it armed.
    /// </summary>
    public bool OnHit(float now, long totalDamage, bool byLocalPlayer, float duration, float rowAmp, SkillAttackDamage attack)
    {
        if (totalDamage >= 0 || !byLocalPlayer) return false;
        float amp = rowAmp;
        if (attack != null) amp *= attack.DamagedVibrateRatio;
        if (!(0f < duration && 0f < amp)) return false;
        Arm(now, now + duration, amp);
        return true;
    }

    /// <summary>
    /// CNpcVibrate::Update (@0x14108e300): before the end time, <paramref name="pos"/> (z up) moves by a random unit
    /// direction in x/y times the amp: each axis is a random sign (GetRandomRange(2, 0) as 0 or 1, minus 0.5, times 2)
    /// times a random 0..1 (FUN_140768090), and the pair is unitized (a zero pair stays zero, efd::Point3::Unitize). At
    /// or after the end time it turns off and leaves the point alone. False when it is (or has just turned) off.
    /// </summary>
    public bool Update(float time, ref Vector3 pos, Random rnd)
    {
        if (!Active) return false;
        if (!(time < End)) { Active = false; return false; }
        float fx = ((float)rnd.Next(2) - 0.5f) * 2f;
        float fy = ((float)rnd.Next(2) - 0.5f) * 2f;
        var off = new Vector3(rnd.NextSingle() * fx, rnd.NextSingle() * fy, 0f);
        float len = off.Length();
        if (len > 1e-6f) off /= len; else off = Vector3.Zero;
        pos += off * Amp;
        return true;
    }
}
