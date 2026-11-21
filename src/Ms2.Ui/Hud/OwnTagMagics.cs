using System.Numerics;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>The own player as a magic's owner sees it at a tick: the position and z rotation (degrees) the area is posed from,
/// whether the owner (or the rider of the owner's ridee) is the local player, and the dead and stunned tests.</summary>
public readonly record struct MagicOwner(Vector3 Position, float RotZ, bool LocalPlayer, bool Dead, bool Stunned);

/// <summary>
/// The own cast's magics as far as they register tag areas for <see cref="TombTagPass"/>: the CMagicModel each target record of a
/// fired attack gets (StateSkill::FUN_1417ecce0 @0x1417ecce0, stateskill.cpp:1347: CMagicModel::Init(tag, 0, ZERO, 0, 0, 0)), its
/// start tick the attack's tick (SetTargetStartTicks from m_mapC0, :1370) and its CMagicData the attack's magicPathID row at the
/// record's index (FUN_14145c300 of m_n84, magicdata.cpp:2614; an unknown id makes no magic).
/// <list type="bullet">
/// <item>The tick (CMagic::FUN_14145de80 @0x14145de80, magicdata.cpp:755), for a magic without attackMethodType 1
/// (SkillTargetRecord::FUN_141759090 0): before start + delay it runs the region fill and the registration
/// (FUN_141461bd0, FUN_141462490); after it, the state goes 0, 1, then 2 once spawnTime passed, where a lifeTime that ran out
/// goes to 3 without them, else it runs them.</item>
/// <item>Both are due once now reaches start + delay - CMainSystem::FUN_1414734a0 (the latency maximum of the last time sync,
/// the lead of <see cref="Tick"/>). The region fill (FUN_141464d00, magicdata.cpp:1750) runs once (m_n1CC):
/// with the owner alive and not stunned, FUN_1417e95a0 poses the attack's area from the owner's position and facing then
/// (CFillTargetContext m_n80 4) and keeps it (m_spVal); an attack without a shape 1-4 has no collision and keeps none. The
/// registration (FUN_141462490, magicdata.cpp:1594) runs once (m_n1D0) for a local owner and an attack with brokenOffence
/// (or the flag at +0x100, which registers nothing without brokenOffence) and hands the area to WorldService::FUN_141bc3470,
/// which drops a missing area.</item>
/// <item>A skill whose magicControl enable is 2 (m_n538) makes no region fill, so it registers nothing.</item>
/// </list>
/// So a magic is kept here only while it may still register, and dropped after its one attempt.
/// STILL-OPEN: attackMethodType 1 (the arrow fill FUN_141465b30 and the registration every tick), the linked records, the
/// cancel of a delay-0 magic when the skill state ends (CMagicService::RemoveCasterModels / CMagicModel::CanCancel), the
/// latency maximum (0 here: the time sync's latency is not kept), CFunctionCubeService::SetTagArea and the magic's own
/// flight, effects and hits are not ported.
/// </summary>
public sealed class OwnTagMagics
{
    sealed class Magic
    {
        public TagRecord Tag;
        public SkillAttackData Attack;
        public int Start, Delay, Spawn, Life;
    }

    readonly List<Magic> live = new();

    /// <summary>The magics that may still register.</summary>
    public int Count => live.Count;

    /// <summary>
    /// FUN_1417ecce0's models for the attack <paramref name="attack"/> of <paramref name="skill"/> fired at
    /// <paramref name="startMs"/> with the target records <paramref name="tags"/> (one magic each, its row picked by the record's
    /// index <see cref="SkillDamageTarget.Hit"/>). Returns the number kept. <paramref name="onHit"/> is each tag's
    /// <see cref="TagRecord.Hit"/>.
    /// </summary>
    public int Add(SkillData skill, SkillAttackData attack, IReadOnlyList<SkillDamageTarget> tags, int startMs, MagicPathTable paths,
        Action onHit = null)
    {
        if (attack == null || tags == null || paths == null) return 0;
        if (attack.BrokenOffence == 0 || attack.AttackMethodType == 1 || (skill?.MagicControlEnable ?? 0) == 2) return 0;
        int n = 0;
        foreach (var t in tags)
        {
            var d = paths.Get(attack.MagicPathId, t.Hit);
            if (d == null) continue;
            live.Add(new Magic
            {
                Tag = new TagRecord { BrokenOffence = attack.BrokenOffence, Hit = onHit },
                Attack = attack, Start = startMs,
                Delay = MagicPathTable.Ms(d.DelayTime), Spawn = MagicPathTable.Ms(d.SpawnTime), Life = MagicPathTable.Ms(d.LifeTime),
            });
            n++;
        }
        return n;
    }

    /// <summary>
    /// One tick of every magic at <paramref name="nowMs"/>: each one due runs its region fill for <paramref name="owner"/> and
    /// registers the area into <paramref name="pass"/>, then is dropped. Returns the number registered.
    /// </summary>
    public int Tick(int nowMs, int leadMs, in MagicOwner owner, TombTagPass pass)
    {
        int registered = 0;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var m = live[i];
            if (!Due(m, nowMs, leadMs, out bool expired))
            {
                if (expired) live.RemoveAt(i);
                continue;
            }
            live.RemoveAt(i);
            if (Register(m, owner, pass)) registered++;
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

    /// <summary>FUN_141464d00's fill then FUN_141462490's registration.</summary>
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
