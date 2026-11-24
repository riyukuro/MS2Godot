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
    /// <summary>The record's cast: the Breakable request's tag values.</summary>
    public TagIdentity Id;
    public readonly HashSet<ulong> Entered = new();
    /// <summary>SkillTargetRecord::FUN_1417591b0 (skillrecord.cpp:143), run after a hit: an owner that is MyPC in the skill
    /// state (0x10) arms that state's pause from the tag. STILL-OPEN: StateSkill's ArmPauseFromTag is not ported, so the
    /// caller gets the call and does nothing yet.</summary>
    public Action Hit;
}

/// <summary>
/// The SkillTargetRecord values WriteBreakableInfo sends: the tag id (+0x18, the cast id), the skill (+0x38, GetId), the skill
/// level (+0x48, GetLevel), +0x58 the cast's motion and +0x5c its attack index.
/// </summary>
public readonly record struct TagIdentity(long CastId, int SkillId, short Level, byte Motion, byte Attack);

/// <summary>A field breakable the pass hit: the request it sent, the hit sound FUN_140ade030(1, 1) plays and where.</summary>
public readonly record struct BreakableHit(BreakableRequest Request, int HitSound, Vector3 Position);

/// <summary>Another PC's standing tomb, as the pass sees it: the PC's object id, whether it is hostile to the local player
/// (slot 122, see <see cref="TombTagPass.HostileInField"/>) and the tomb at its position and rotation.</summary>
public readonly record struct TombTarget(int ObjectId, bool Hostile, Tombstone Tomb, Vector3 Position, Vector3 RotDegrees);

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
/// while FUN_140addea0 holds, hittable and idle) and that is not hostile to the local player (slot 122 at +0x3d0, :9333:
/// PC::vfunc_122, <see cref="HostileInField"/>); with reactionArrow (FUN_140adf310)
/// the first area whose record has not entered this tomb and that intersects the tomb's box (CBaseCollision::Intersect of the
/// area and GetBox) is the hit: the record keeps the tomb's entity id, the hitter is 1 (the area level read there is dead),
/// FUN_140ade030(1, 1) plays the hit sound (VolumeMyPcToBreakableObject; the host plays it, RemoteLayer.PlayTagHitSound)
/// and starts the hit timer (not ported: the tomb's
/// state is the server's, and FUN_140addea0 does not read that timer for IsTombStone), the Tombstone packet
/// (0x2A: object id, hitter) goes out, then the record's FUN_1417591b0.</item>
/// </list>
/// m_3f0 is a tree keyed by the record pointer, so its order is the heap's; here it is the registration order.
/// The registrations come from the own cast's magics (<see cref="OwnTagMagics"/>); the magics of the 0x3d mode 0 cast records
/// (scenestategame.cpp:5509) are not the local player's (FUN_141464410), so they register nothing.
/// The field markers' loop runs first (<see cref="RunMarkers"/>).
/// STILL-OPEN: the movers' part (m_738).
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

    /// <summary>
    /// The local player's slot 122 test on another PC in a field of <paramref name="pkMode"/>: PC::vfunc_122 @0x14165f5d0
    /// (pc.cpp:3112) is hostile in a pk field, else PCBase::vfunc_122 (pcbase.cpp:2784) compares the teams, which here are the
    /// same; the rule SkillHit.FilterTarget uses for PCs.
    /// STILL-OPEN: the pk field's exceptions (the same guild id, FUN_141661bc0; either side's pvp on/off) and the team
    /// compare are not fed, as in LiveNameTags.
    /// </summary>
    public static bool HostileInField(int pkMode) => pkMode != 0;

    /// <summary>
    /// The named sensors' loop (worldservice_1.cpp:9262-9316), before the tombs' and with the same areas (not emptied here):
    /// each field marker in name order that can be hit (FUN_140addea0: hittable, the hit timer not running, idle) and has
    /// reactionArrow takes the first area whose record has not entered the marker's entity and that intersects its box.
    /// The record keeps the entity id, FUN_140ade030(1, 1) starts the hit timer (the host plays the hit sound), the Breakable
    /// request goes out (the marker's name and the record's <see cref="TagIdentity"/>), and the record's FUN_1417591b0 runs
    /// when the box has SkillPauseControllFeedback. The area level read there is dead. Hits go to <paramref name="hits"/>
    /// (cleared first). With no area registered nothing is read.
    /// </summary>
    public int RunMarkers(BreakableObjects markers, List<BreakableHit> hits)
    {
        hits.Clear();
        if (areas.Count == 0 || markers == null) return 0;
        var list = markers.Sorted;
        long now = markers.NowMs;
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (m.Entity == null || !m.CanBeHit(now) || !m.ReactionArrow) continue;   // the pass walks game objects (m_118, worldservice_1.cpp:9320): a marker without an entity is none
            var box = m.Box();
            foreach (var (tag, area) in areas)
            {
                if (tag.Entered.Contains(m.EntityId) || !area.Intersects(box)) continue;
                tag.Entered.Add(m.EntityId);
                markers.Hit(m);
                var id = tag.Id;
                hits.Add(new BreakableHit(new BreakableRequest(m.Name, id.CastId, id.SkillId, id.Level, id.Motion, id.Attack), m.HitSound,
                    new Vector3((float)m.Position.X, (float)m.Position.Y, (float)m.Position.Z)));
                if (m.SkillPauseFeedback) tag.Hit?.Invoke();
                break;
            }
        }
        return hits.Count;
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
            if (tomb == null || !tomb.CanBeHit || t.Hostile || !tomb.ReactionArrow) continue;
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
