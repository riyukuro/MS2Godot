using Ms2.Net.Packets;

namespace Ms2.Net.Replay;

/// <summary>What <see cref="ActorCast.Sync"/> made of a SkillSync.</summary>
public enum CastSyncResult
{
    /// <summary>The packet starts a new cast (CSkillRecord::Init then PCBase::FUN_141673b40, the cast's motion plays).</summary>
    NewCast,
    /// <summary>The packet updates the running cast (the record's rotation, vfunc_22's move, StateSkill::FUN_1417ec630).</summary>
    Update,
}

/// <summary>
/// One actor's current cast as the skill packets test it: PCBase::vfunc_103's record (its m_nSkillUid and m_nMotion) and
/// whether the actor is in the skill state (GetCurStateId() == 0x10). OnSkillUse @0x141717b40 (scenestategame.cpp:3297),
/// OnSkillSync @0x141718150 (:3361) and OnSkillCancel @0x141718680 (:3409) in their order.
/// STILL-OPEN: the record is kept after the state ends in the client (vfunc_103 still gives it); here only its uid and motion are.
/// </summary>
public struct ActorCast
{
    public const int SkillState = 0x10;

    public bool HasCast;
    public long Uid;
    public int Motion;
    /// <summary>The actor is in StateSkill (state 0x10).</summary>
    public bool InSkillState;
    /// <summary>StateSkill::FUN_1417ec630's flags of the last update (stateskill.cpp:1419; m_bCharging is the first).</summary>
    public bool Charging, ChargeStart;

    /// <summary>OnSkillUse (:3323-3341): a new record of the uid and motion, FUN_141673b40 enters the skill state.</summary>
    public void Use(long uid, int motion)
    {
        HasCast = true; Uid = uid; Motion = motion; InSkillState = true;
        Charging = ChargeStart = false;
    }

    /// <summary>
    /// OnSkillSync (:3378-3404): no record, another uid, another motion or not in the skill state starts the packet's cast
    /// (<see cref="Use"/>); else the cast is updated, and StateSkill::FUN_1417ec630(A, B) takes the flags in the skill state.
    /// </summary>
    public CastSyncResult Sync(SkillSync s)
    {
        if (!HasCast || Uid != s.CastUid || Motion != s.Motion || !InSkillState)
        {
            Use(s.CastUid, s.Motion);
            return CastSyncResult.NewCast;
        }
        Charging = s.A; ChargeStart = s.B;
        return CastSyncResult.Update;
    }

    /// <summary>OnSkillCancel (:3415-3417): the record of the uid in the skill state leaves it (State::vfunc_7(1)). True when it did.</summary>
    public bool Cancel(long uid)
    {
        if (!HasCast || Uid != uid || !InSkillState) return false;
        InSkillState = false;
        return true;
    }

    /// <summary>The skill state ended on its own (its motion finished).</summary>
    public void End() => InSkillState = false;
}
