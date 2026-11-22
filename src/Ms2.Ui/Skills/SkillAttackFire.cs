using System;
using System.Collections.Generic;
using System.Numerics;
using Ms2.Net.Packets;

namespace Ms2.Ui.Skills;

/// <summary>
/// GameObject::GenerateSerialId @0x14123cec0 (gameobject.cpp:703): the object id in the high 32 bits over one counter shared by
/// every object (a function static, counted up before use).
/// </summary>
public sealed class SerialIds
{
    uint counter;

    public long Next(int objectId) => (long)(((ulong)(uint)objectId << 32) | ++counter);
}

/// <summary>An own cast's attacks as its motion reaches their points, and the damage send each one makes.</summary>
public static class SkillAttackFire
{
    /// <summary>CSkillRecord::GetAttackDataAt (skillrecord.cpp:370): SkillLevelData::GetAttack of the record's motion; null past the end.</summary>
    public static SkillAttackData AttackAt(CastRecord r, int attack)
    {
        var motions = r?.Level?.Motions;
        if (motions == null || r.Motion >= motions.Count || attack < 0) return null;
        var list = motions[r.Motion].Attacks;
        return attack < list.Count ? list[attack] : null;
    }

    /// <summary>
    /// StateSkill::FireFirstAttack @0x1417ea920 (stateskill.cpp:1844), which MyPC::vfunc_102 (mypc.cpp:1219-1226) runs for a
    /// started cast whose level has no sequence (no skill state): with the record's current attack (GetCurrentAttackData, its
    /// m_nAttackIndex) FUN_1417ecce0 fires attack 0. The attack to fire, -1 for a level with a sequence or no current attack.
    /// </summary>
    public static int FirstAttack(CastRecord r)
    {
        if (r?.Level == null || r.Level.HasSequenceName || AttackAt(r, r.AttackIndex) == null) return -1;
        return 0;
    }

    /// <summary>
    /// StateSkill::FireAttacksAtPoint @0x1417f0f10 (stateskill.cpp:2272): from the current attack, every attack in a row whose
    /// point is set and equal to the first's fires (FireCurrentAttack, m_nAttack++). The count it fires; 0 without a point.
    /// The caller has already checked the text key against the current attack's point (StateSkill::TextKeyEvent,
    /// stateskill.cpp:980). STILL-OPEN: the record's m_n9c stop and the loop flags m_b111 / m_bLoop are not kept.
    /// </summary>
    public static int AttacksAtPoint(CastRecord r, int attack)
    {
        string point = null;
        int n = 0;
        for (;;)
        {
            var a = AttackAt(r, attack + n);
            if (a == null || string.IsNullOrEmpty(a.Point) || (point != null && point != a.Point)) return n;
            point = a.Point;
            n++;
        }
    }

    /// <summary>
    /// StateSkill::FUN_1417ecce0 @0x1417ecce0 (stateskill.cpp:1326) for an own attack: the direction
    /// (GameObject::RotationToDirection of the record's rotation, gameobject.cpp:669), the area pick
    /// (StateSkill::PickAttackTargets @0x1417f3180 then FUN_1417e95a0, the caster at <paramref name="ownerPos"/> facing
    /// <paramref name="ownerRotZ"/>) and the mode 1 sub-mode 0 send with the record's cast id, position and send count.
    /// <paramref name="zOffset"/> is CSkillRecord::FUN_141759ad0. Null when the attack is past the motion's end.
    /// The area pick first queries the objects the caster may target (<see cref="SkillHit.Query"/>, kind applyTarget, npc
    /// kinds applyTargetIgnoreNpcRanks: FUN_1417e95a0, stateskill.cpp:1176-1185) into <paramref name="queried"/>; an attack
    /// whose range is no shape 1-4 has no collision (CSkillCollisionManager::GetCached @0x141754580), so PickAttackTargets
    /// returns before any record and the send goes without targets. <paramref name="pkMode"/> is the field's pk mode
    /// (PC::vfunc_122's hostility).
    /// The record keeps the attack as its m_nAttackIndex (FireCurrentAttack, stateskill.cpp:2264).
    /// STILL-OPEN: the castTarget kind (FUN_1417e95a0's m_n60, 0 here) and the context's extra kind flags.
    /// STILL-OPEN: class 10's direction (FUN_141489940: -UNIT_X through the record's full euler rotation), the magic models,
    /// SetTargetStartTicks and the record's attack ticks (m_mapC0) are not ported; the send is made whenever the caller is the
    /// own player (vfunc_108).
    /// </summary>
    public static SkillDamageRequest Fire(CastRecord r, int attack, Vector3 ownerPos, float ownerRotZ, float zOffset,
        ReadOnlySpan<HitCandidate> objects, Func<int> rand, Func<long> serial, List<HitCandidate> queried, List<int> hits,
        bool pkMode = false)
    {
        var a = AttackAt(r, attack);
        if (a == null) return null;
        r.AttackIndex = attack;
        SkillHit.Axes(r.Rotation.Z, out var dir, out _);
        // the request keeps its target list: a new one per send
        var tags = new List<SkillDamageTarget>();
        List<int> picked = null;
        bool collision = true;
        if (!a.Arrow.IsNonTargetOne)
        {
            collision = a.Range.Shape is >= 1 and <= 4;
            if (collision)
            {
                var rot = new Vector3(0, 0, ownerRotZ);
                SkillHit.AreaPose(a, ownerPos, rot, out var pos, out var areaRot);
                var shape = SkillHitShape.Pose(a.Range, zOffset, pos, areaRot);
                SkillHit.Query(objects, a.Range.ApplyTarget, a.Range.IgnoreNpcRanks, queried, pkMode);
                SkillHit.Detect(shape, pos, areaRot, a.Direction, a.Range.IncludeCaster != 0, false,
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(queried), rand, hits);
                picked = hits;
            }
        }
        if (collision)
            SkillHit.PickTargets(a, AttackAt(r, r.AttackIndex)?.AttackMethodType ?? 0, picked, rand, serial, tags, objects, pkMode);
        return new SkillDamageRequest(r.CastId, (byte)attack, r.Position, dir, r.SendCount, tags);
    }
}
