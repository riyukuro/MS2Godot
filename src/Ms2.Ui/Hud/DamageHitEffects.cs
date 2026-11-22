using System.Numerics;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// An object of the field as the damage's hit effect reads it, in client space: its position (vf18), its rotation z in degrees
/// (vf20, GameObject::GetForwardDirection), its extent z (PCBase::vfunc_37 @0x14166bcc0, pcbase.cpp:958: vfunc_43's height,
/// 0 here, plus half its box collision's height, CBoxCollision::vfunc_6 m_ptBox.z) and its kind (FUN_14146c990's nType: 1 the
/// local player, 3 another PC, 0 anything else).
/// </summary>
public readonly record struct DamageHitObject(Vector3 Position, float RotZ, float ExtentZ, int Kind);

/// <summary>
/// One effect the damage's hit inserts (CEffectService::InsertEffect from CMagicService::FUN_14146b440): the file at
/// <paramref name="At"/> turned by the columns <paramref name="AxisX"/>, <paramref name="AxisY"/>, <paramref name="AxisZ"/>
/// (client space) when <paramref name="ActorId"/> is 0, else on that actor (SetActor, no transform). Its object kind
/// (m_nObject) is <paramref name="CasterKind"/>.
/// </summary>
public readonly record struct DamageHitEffect(string File, int ActorId, Vector3 At, Vector3 AxisX, Vector3 AxisY, Vector3 AxisZ,
    int CasterKind);

/// <summary>Finds the field's object <paramref name="id"/> (WorldService::FindObjectById).</summary>
public delegate bool DamageHitFind(int id, out DamageHitObject o);

/// <summary>
/// The hit effects of a mode 1 skill damage (CSceneStateGame::FUN_14171cdf0 @0x14171cdf0, scenestategame.cpp:5606). Per target
/// a CDamageRecord; when the target is an object, the hit point and direction start as the packet's, FUN_14145e1a0 aims them
/// unless attackMethodType is 1 (FUN_14175acc0), FUN_141467f60 turns them into the transform
/// (<see cref="OwnTagMagics.AlongAxes"/>), and CMagicService::FUN_14146b440 (magicdata.cpp:2736) with bTracked 1 and
/// CContentShutdown 0x73 off plays them: for a record with hits, FUN_14146c990 (magicdata.cpp:2991) gives the pair
/// (first, second) and the owner's kind; first plays the damage's effectHit at the transform, second its effectRemain on
/// the target (the same kind).
/// FUN_14146c990 (pCaster the hit object; the owner need not be in the field, its kind is then 0): second always; first when the effectHit has a number (an empty name has none) and the
/// object's hit effect throttle lets it through (<see cref="HitEffectThrottle"/>, the target's state).
/// STILL-OPEN: FUN_14146c990's optimizer hide of the owner (FUN_14161ce60), the sight range for an owner other than the
/// local player (field property 6, not ported), IsUpdateEnabled's check (taken as on), the effects' IgnoreOptimalHide flags;
/// the npc owner's property tag 0x11 (the record's hit point and the aim's attach); the shut-down branch (0x73 on).
/// </summary>
public static class DamageHitEffects
{
    /// <summary>
    /// The effects of <paramref name="d"/> into <paramref name="into"/> (in the client's order), at <paramref name="nowMs"/>.
    /// <paramref name="damage"/> is the packet's attack damage (null: CDamageRecord::Init fails or the attack is missing,
    /// nothing plays); <paramref name="direction"/> the attack's "direction" (m_n38) and <paramref name="moveDistance"/> its
    /// motion's "movedistance" (m_fB8), the aim's inputs. <paramref name="rand"/> is the CRT rand (0..32767) the aim's
    /// FUN_14109ace0 reads. Returns the number added.
    /// </summary>
    public static int Records(SkillDamage d, SkillAttackDamage damage, int direction, float moveDistance, DamageHitFind find,
        HitEffectThrottle throttle, uint nowMs, Func<int> rand, List<DamageHitEffect> into)
    {
        if (d.Mode != 1 || damage == null || d.Targets == null) return 0;
        int n = into.Count;
        var packetPos = d.Position.ToVector3();
        var packetDir = d.DirectionShort.ToVector3();
        foreach (var t in d.Targets)
        {
            if (!find(t.TargetId, out var target)) continue;
            var pos = packetPos;
            var dir = packetDir;
            bool hasOwner = find(d.OwnerId, out var owner);
            if (damage.AttackMethodType != 1)
                Aim(target, hasOwner, owner, direction, moveDistance, rand, ref pos, ref dir);
            // FUN_14146b440: the record keeps every hit (AddDamage); none, nothing
            if (t.Hits == null || t.Hits.Length == 0) continue;
            // FUN_14146c990 null-tests only the hit object; the kind is FUN_141bd19d0(owner), 0 without one
            int kind = hasOwner ? owner.Kind : 0;
            if (throttle.TryPlay(t.TargetId, damage.EffectHit, nowMs))
            {
                OwnTagMagics.AlongAxes(dir, out var ax, out var ay, out var az);
                into.Add(new DamageHitEffect(damage.EffectHit, 0, pos, ax, ay, az, kind));
            }
            if (damage.EffectRemain.Length != 0)
                into.Add(new DamageHitEffect(damage.EffectRemain, t.TargetId, target.Position, Vector3.UnitX, Vector3.UnitY,
                    Vector3.UnitZ, kind));
        }
        return into.Count - n;
    }

    /// <summary>
    /// The hit effects of a splash (mode 6, CSceneStateGame::OnSplashDamage, scenestategame.cpp:5728) or region skill
    /// (mode 5, OnRegionSkillDamage, scenestategame.cpp:7768) damage: per record whose hit object is in the field,
    /// CMagicService::FUN_14146bc80 @0x14146bc80 (magicdata.cpp:2844) with CContentShutdown 0x73 off. The record's caster is the
    /// object hit and its owner the packet's (mode 6 has none: 0); FUN_14146c990 gives (first, second) and the owner's kind
    /// (FUN_141bd19d0, 0 without an object). first plays the effectHit at the hit object's position with no rotation (scale 1,
    /// DAT_143a09600), through the object's throttle; second the effectRemain on the object. A record without hits plays
    /// alike (bc80 does not read them). The caller drops a mode 5 packet whose region skill object is gone.
    /// STILL-OPEN: an npc with property tag 0x1d takes the record's hit point (mode 5: the nearest anchor box's center to the
    /// origin; mode 6: the packet's point), the region object's PlayHitEffect, vf35_End, the shut-down branch (0x73 on).
    /// </summary>
    public static int SplashRecords(SkillDamage d, SkillAttackDamage damage, DamageHitFind find, HitEffectThrottle throttle,
        uint nowMs, List<DamageHitEffect> into)
    {
        if (d.Mode is not (5 or 6) || damage == null || d.Targets == null) return 0;
        int n = into.Count;
        int kind = d.Mode == 5 && find(d.OwnerId, out var owner) ? owner.Kind : 0;
        foreach (var t in d.Targets)
        {
            if (!find(t.TargetId, out var target)) continue;
            if (throttle.TryPlay(t.TargetId, damage.EffectHit, nowMs))
                into.Add(new DamageHitEffect(damage.EffectHit, 0, target.Position, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, kind));
            if (damage.EffectRemain.Length != 0)
                into.Add(new DamageHitEffect(damage.EffectRemain, t.TargetId, target.Position, Vector3.UnitX, Vector3.UnitY,
                    Vector3.UnitZ, kind));
        }
        return into.Count - n;
    }

    /// <summary>
    /// FUN_14145e1a0 @0x14145e1a0 (magicdata.cpp:848) from the target (nFrom) to the owner (nTo): the point starts at the
    /// target's position plus its extent. With the owner: the height is the smaller extent z times 1 + FUN_14109ace0() * 0.2
    /// (a CRT rand in [-1, 1]) above the target's position, and the direction by the attack's "direction": 3 the packet's
    /// when movedistance is not 0, else the owner's forward, unit; 4 the same negated; 2 -((target - owner) x up), unit; 1
    /// (target - owner) x up, unit; else the owner's forward. Then the target's vf38_Trace (GameObject::vfunc_38, a pick of
    /// its own model along the direction within 2000) moves the point onto its model; without a hit it stays.
    /// STILL-OPEN: the pick (the point stays above the target's position) and the npc caster's tag 0x11 attach branch.
    /// </summary>
    internal static void Aim(in DamageHitObject target, bool hasOwner, in DamageHitObject owner, int direction, float moveDistance,
        Func<int> rand, ref Vector3 pos, ref Vector3 dir)
    {
        pos = target.Position + new Vector3(0, 0, target.ExtentZ);
        if (!hasOwner) return;
        float height = MathF.Min(target.ExtentZ, owner.ExtentZ);
        height *= 1f + (-1f + 2f * rand() / 32767f) * 0.2f;
        pos = target.Position + new Vector3(0, 0, height);
        SkillHit.Axes(owner.RotZ, out var forward, out _);
        switch (direction)
        {
            case 3:
                dir = SkillHit.Unitize(moveDistance != 0f ? dir : forward);
                break;
            case 4:
                dir = SkillHit.Unitize(moveDistance != 0f ? -dir : -forward);
                break;
            case 2:
                dir = -UnitCross(target.Position - owner.Position, Vector3.UnitZ);
                break;
            case 1:
                dir = UnitCross(target.Position - owner.Position, Vector3.UnitZ);
                break;
            default:
                dir = SkillHit.Unitize(forward);
                break;
        }
    }

    /// <summary>efd::Point3::UnitCross: the cross product, unit (zero stays zero).</summary>
    static Vector3 UnitCross(Vector3 a, Vector3 b) => SkillHit.Unitize(Vector3.Cross(a, b));
}
