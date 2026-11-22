using System.Numerics;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// The other players' magics from SkillDamage mode 0 (CSceneStateGame::FUN_14171c1d0 @0x14171c1d0, scenestategame.cpp:5511)
/// and the start ticks their cast records give them. Per owner: its magics (<see cref="OwnTagMagics"/>, ticked as an owner
/// other than the local player), its current cast record (the PCBase list's, which PlayCastStartEffect replaces before the
/// state is entered again, stateskill.cpp:1820) and the record of its running skill state (StateSkill m_spRecord: the one the
/// state was entered with, so the exit of a state entered again runs on the old record).
/// A record keeps its attacks' fire ticks (CSkillRecord m_mapC0) and its models waiting for them (m_listModels, the
/// m_magics StateSkill reads); SetTargetStartTicks (stateskill.cpp:1370) walks the models in order, sets each one's start tick
/// from its attack's tick, returns at the first attack without one, and empties the list once all are set.
/// STILL-OPEN: the record's m_nA0 (FUN_14171c1d0 takes a record only when it is 0) and vfunc_3's stopped-PC test are not
/// read; FUN_141673ce0 returning no record (the state then not entered) is not followed by the remote's states.
/// </summary>
public sealed class RemoteTagMagics
{
    sealed class Record
    {
        public long Uid;
        public int Motion;
        public readonly Dictionary<int, int> Ticks = new();
        public readonly List<(int Attack, OwnTagMagics.Magic Model)> Models = new();
    }

    sealed class Owner
    {
        public readonly OwnTagMagics Magics = new();
        public Record Current, State;
    }

    readonly Dictionary<int, Owner> owners = new();

    /// <summary>The magics of <paramref name="ownerId"/> still kept (0 for an unknown owner).</summary>
    public int Count(int ownerId) => owners.TryGetValue(ownerId, out var o) ? o.Magics.Count : 0;

    /// <summary>Whether any owner keeps a magic.</summary>
    public bool Any
    {
        get
        {
            foreach (var o in owners.Values) if (o.Magics.Count != 0) return true;
            return false;
        }
    }

    /// <summary>The models of the owner's skill state record still waiting for a start tick (for the checks).</summary>
    public int Pending(int ownerId) => owners.TryGetValue(ownerId, out var o) && o.State != null ? o.State.Models.Count : 0;

    /// <summary>The owner's magics (for the checks).</summary>
    internal OwnTagMagics MagicsOf(int ownerId) => owners.TryGetValue(ownerId, out var o) ? o.Magics : null;

    Owner Get(int ownerId)
    {
        if (!owners.TryGetValue(ownerId, out var o)) owners[ownerId] = o = new Owner();
        return o;
    }

    /// <summary>
    /// PCBase::FUN_141673ce0 from PlayCastStartEffect: the owner's record list replaced by a new record of the cast
    /// <paramref name="uid"/> and motion <paramref name="motion"/> unless a record with both is there. True when replaced.
    /// </summary>
    public bool BeginCast(int ownerId, long uid, int motion)
    {
        var o = Get(ownerId);
        if (o.Current != null && o.Current.Uid == uid && o.Current.Motion == motion) return false;
        o.Current = new Record { Uid = uid, Motion = motion };
        return true;
    }

    /// <summary>StateSkill's entry: its record is the owner's current one.</summary>
    public void SkillEntered(int ownerId)
    {
        var o = Get(ownerId);
        o.State = o.Current;
    }

    /// <summary>
    /// FUN_14171c1d0 for the owner <paramref name="d"/>.OwnerId at <paramref name="nowMs"/>: spCast is the owner's current
    /// record when its uid and motion are the packet's (PCBase::FUN_141673bf0(tagId, srcLevel)); without it only a bServer
    /// packet makes magics, and they join no record. <paramref name="skill"/> and <paramref name="attack"/> are the packet's
    /// skill level and attack (CMagicModel::Init fails without them, for every record alike). The caller drops an absent
    /// owner (FUN_1414609e0 returns at once without it). Returns the number of magics made.
    /// </summary>
    public int OnDamage(SkillDamage d, SkillData skill, SkillAttackData attack, MagicPathTable paths, int nowMs,
        MagicRayCast rayCast, ReadOnlySpan<HitCandidate> objects)
    {
        if (d.Mode != 0 || d.Projectiles == null || skill == null || attack == null) return 0;
        var o = Get(d.OwnerId);
        var cast = o.Current != null && o.Current.Uid == d.CastUid && o.Current.Motion == d.Motion ? o.Current : null;
        if (cast == null && !d.FromServer) return 0;
        var start = new Vector3(d.Position.X, d.Position.Y, d.Position.Z);
        return o.Magics.AddRemote(skill, attack, d.CastUid, d.AttackPoint, start, d.MagicDirection, d.Projectiles, d.FromServer, nowMs,
            paths, d.OwnerId, rayCast, objects, cast?.Models);
    }

    /// <summary>FUN_1417ecce0 for an owner other than the local player (stateskill.cpp:1316): the state record's tick of the
    /// attack <paramref name="attack"/> becomes <paramref name="nowMs"/>, then SetTargetStartTicks (FireCurrentAttack and
    /// FireFirstAttack call it after).</summary>
    public void AttackFired(int ownerId, int attack, int nowMs)
    {
        if (!owners.TryGetValue(ownerId, out var o) || o.State == null) return;
        o.State.Ticks[attack] = nowMs;
        SetTargetStartTicks(o.State);
    }

    /// <summary>
    /// StateSkill::vfunc_5 (stateskill.cpp:512-535): RemoveCasterModels of the state record's cast
    /// (<see cref="OwnTagMagics.CancelCaster"/>), then, with models still waiting, every one's attack tick becomes
    /// <paramref name="nowMs"/> and SetTargetStartTicks runs. Returns the number of magics dropped.
    /// </summary>
    public int SkillExited(int ownerId, int nowMs)
    {
        if (!owners.TryGetValue(ownerId, out var o) || o.State == null) return 0;
        var r = o.State;
        int n = o.Magics.CancelCaster(r.Uid, nowMs);
        if (r.Models.Count != 0)
        {
            foreach (var (attack, _) in r.Models) r.Ticks[attack] = nowMs;
            SetTargetStartTicks(r);
        }
        return n;
    }

    /// <summary>
    /// One tick of the owner's magics: StateSkill::vfunc_3's SetTargetStartTicks while <paramref name="inSkill"/> and models
    /// wait, then the magics' tick (<see cref="OwnTagMagics.Tick(int, int, in MagicOwner, ReadOnlySpan{HitCandidate}, CastRecord, TombTagPass, List{SkillHitRequest}, Func{int}, MagicRayBlocked, bool, MagicRayCast, List{SkillSplashRequest}, List{MagicEndEffect}, List{MagicHitSound})"/>)
    /// over <paramref name="objects"/> (the owner the <see cref="HitCandidate.IsOwner"/> one), their end effects into
    /// <paramref name="endEffects"/>. Nothing is sent, registered or sounded for this owner.
    /// </summary>
    public void Tick(int ownerId, bool inSkill, int nowMs, in MagicOwner owner, ReadOnlySpan<HitCandidate> objects, Func<int> rand,
        MagicRayCast rayCast, List<MagicEndEffect> endEffects, bool pkMode = false)
    {
        if (!owners.TryGetValue(ownerId, out var o) || o.Magics.Count == 0) return;
        if (inSkill && o.State != null && o.State.Models.Count != 0) SetTargetStartTicks(o.State);
        o.Magics.Tick(nowMs, 0, owner, objects, null, null, null, rand, null, pkMode, rayCast, null, endEffects, null);
    }

    /// <summary>The owner left the field: its records and magics go.</summary>
    public void Remove(int ownerId) => owners.Remove(ownerId);

    public void Clear() => owners.Clear();

    static void SetTargetStartTicks(Record r)
    {
        foreach (var (attack, model) in r.Models)
        {
            if (!r.Ticks.TryGetValue(attack, out int tick)) return;
            if (model != null) OwnTagMagics.SetStart(model, tick);
        }
        r.Models.Clear();
    }
}
