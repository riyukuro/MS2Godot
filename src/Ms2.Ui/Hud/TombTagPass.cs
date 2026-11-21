using System.Numerics;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// A skill target record as a tag area's key (SkillTargetRecord, the 0x3d mode 0 cast record): its attack's brokenOffence
/// (GetAttackData +0x40) and the tomb entity ids its area has hit (the record's entered map).
/// </summary>
public sealed class TagRecord
{
    public int BrokenOffence;
    public readonly HashSet<ulong> Entered = new();
    /// <summary>SkillTargetRecord::FUN_1417591b0 (skillrecord.cpp:143), run after a hit: an owner that is MyPC in the skill
    /// state (0x10) arms that state's pause from the tag. STILL-OPEN: StateSkill's ArmPauseFromTag is not ported, so the
    /// caller gets the call and does nothing yet.</summary>
    public Action Hit;
}

/// <summary>Another PC's standing tomb, as the pass sees it: the PC's object id, whether the local player owns it
/// (GameObject::IsOwnedBy, vfunc 122) and the tomb at its position and rotation.</summary>
public readonly record struct TombTarget(int ObjectId, bool OwnedByLocal, Tombstone Tomb, Vector3 Position, Vector3 RotDegrees);

/// <summary>A tomb the pass hit: the packet it sent and the hit sound FUN_140ade030(1, 1) plays.</summary>
public readonly record struct TombHit(TombstoneRequest Request, int HitSound, Vector3 Position);

/// <summary>
/// The tomb part of WorldService::FUN_141bc3560 (worldservice_1.cpp:9172, the world tick, :9823) over the tag areas
/// WorldService::FUN_141bc3470 (:4207) registered this tick (m_3f0, emptied at the end of every pass, :9392).
/// <list type="bullet">
/// <item>Registration (FUN_141bc3470): an area and a record whose attack has a brokenOffence; the same record replaces its
/// area. The one caller is CMagic::FUN_141462490 (magicdata.cpp:1594): the attack's flag or brokenOffence set, the delay due
/// (m_n1D0 0) and the owner, or the rider of the owner's ridee, the local player.</item>
/// <item>The pass (:9319-9375), with any area registered: each PC whose sensor is its tomb (PC::FUN_14165ca50: the tomb only
/// while FUN_140addea0 holds, hittable and idle) and that the local player does not own; with reactionArrow (FUN_140adf310)
/// the first area whose record has not entered this tomb and that intersects the tomb's box (CBaseCollision::Intersect of the
/// area and GetBox) is the hit: the record keeps the tomb's entity id, the hitter is 1 (the area level read there is dead),
/// FUN_140ade030(1, 1) plays the hit sound (VolumeMyPcToBreakableObject) and starts the hit timer, the Tombstone packet
/// (0x2A: object id, hitter) goes out, then the record's FUN_1417591b0.</item>
/// </list>
/// m_3f0 is a tree keyed by the record pointer, so its order is the heap's; here it is the registration order.
/// STILL-OPEN: the registrations: CMagic and CMagicModel (made by the 0x3d mode 0 cast records, scenestategame.cpp:5509) are
/// not ported, so nothing registers an area in the game yet; PlayHitSound and the movers' part (m_738) are not ported.
/// </summary>
public sealed class TombTagPass
{
    readonly List<(TagRecord Tag, SkillHitShape Area)> areas = new();

    /// <summary>The areas registered since the last pass.</summary>
    public int Count => areas.Count;

    /// <summary>FUN_141bc3470: false (nothing kept) without an attack brokenOffence.</summary>
    public bool Register(TagRecord tag, in SkillHitShape area)
    {
        if (tag == null || tag.BrokenOffence == 0) return false;
        for (int i = 0; i < areas.Count; i++)
            if (areas[i].Tag == tag) { areas[i] = (tag, area); return true; }
        areas.Add((tag, area));
        return true;
    }

    /// <summary>The pass over <paramref name="tombs"/>: each hit goes to <paramref name="hits"/> (cleared first); the areas are
    /// emptied. Returns the number of hits.</summary>
    public int Run(ReadOnlySpan<TombTarget> tombs, List<TombHit> hits)
    {
        hits.Clear();
        if (areas.Count == 0) return 0;
        foreach (var t in tombs)
        {
            var tomb = t.Tomb;
            if (tomb == null || !tomb.CanBeHit || t.OwnedByLocal || !tomb.ReactionArrow) continue;
            var box = tomb.Box(t.Position, t.RotDegrees);
            foreach (var (tag, area) in areas)
            {
                if (tag.Entered.Contains(tomb.EntityId) || !area.Intersects(box)) continue;
                tag.Entered.Add(tomb.EntityId);
                hits.Add(new TombHit(new TombstoneRequest(t.ObjectId, 1), tomb.HitSound, t.Position));
                tag.Hit?.Invoke();
                break;
            }
        }
        areas.Clear();
        return hits.Count;
    }
}
