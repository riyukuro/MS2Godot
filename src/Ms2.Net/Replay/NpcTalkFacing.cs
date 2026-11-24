using System.Numerics;

namespace Ms2.Net.Replay;

/// <summary>
/// The talk branch of CNpc::ApplySyncBlock (unit_14108e1e0_npcactorandvibrate.cpp:986-1006) and the look it ends in,
/// PCBase::LookAtPoint / SetLookDirection (pcbase.cpp:1352-1388).
/// </summary>
public static class NpcTalkFacing
{
    /// <summary>StateTalkNpc's phase (m_n10) while a talk is open: the npc plays its talking animation.</summary>
    public const byte Talking = 0x48;
    /// <summary>StateTalkNpc's phase without an open talk: the npc plays its idle animation.</summary>
    public const byte Idle = 0x49;
    /// <summary>The talk state (StateTalk / StateTalkNpc vfunc_1).</summary>
    public const byte TalkState = 0x16;

    /// <summary>What a sync block with a new serial does on the talk branch.</summary>
    public enum Step
    {
        /// <summary>Not the talk branch, or the branch's vfunc_59: the block's state is entered as any other.</summary>
        Enter,
        /// <summary>The block changes nothing more (the branch returns).</summary>
        Keep,
        /// <summary>The npc looks at the local player (LookAtPoint(local player, 0)); the state stays.</summary>
        LookAtPlayer,
    }

    /// <summary>
    /// A block of <paramref name="state"/> 0x16 on an npc whose current state is <paramref name="current"/> in phase
    /// <paramref name="phase"/> (FUN_14166cc80, the current state's m_n10), with UITalkManager::GetTalking() being
    /// <paramref name="talking"/>: phase 0x49 without a talk returns (:987-997); phase 0x48 with a talk looks at the local
    /// player when the row has lookAtMyPCWhenTalking, and returns either way (:998-1005); else the state is entered again
    /// (vfunc_59). A phase outside the talk state is taken as no talk phase.
    /// STILL-OPEN: the 0x49 return's look at CQuestManager's portal summon target (IsPortalSummonRequestActive, :989-995)
    /// and state 0x38's (:1007-1014) are not followed: no portal summon request is kept.
    /// </summary>
    public static Step OnBlock(byte state, byte current, byte phase, bool talking, bool lookAtMyPcWhenTalking)
    {
        if (state != TalkState || current != TalkState) return Step.Enter;
        if (phase == Idle && !talking) return Step.Keep;
        if (phase == Talking && talking) return lookAtMyPcWhenTalking ? Step.LookAtPlayer : Step.Keep;
        return Step.Enter;
    }

    /// <summary>StateTalkNpc::vfunc_4 (statetalk.cpp:265-283): the phase entering the talk state sets.</summary>
    public static byte EnterPhase(bool talking) => talking ? Talking : Idle;

    /// <summary>
    /// The target yaw (degrees) PCBase::LookAtPoint(<paramref name="target"/>, 0) gives an actor at <paramref name="from"/>,
    /// both in client space: SetLookDirection's kDir = -(target - from), z 0, unit; the matrix columns (kDir x Z, that x Z,
    /// Z); ToEulerAnglesXYZ's z = atan2(m[0][1], m[0][0]) = atan2(fx, -fy) of f = target - from, times 180 / pi;
    /// NormalizeDegrees, taken as [0, 360) (pcbase.cpp:708; the turn wraps the gap, so the range does not change it). A zero
    /// direction gives 0. The npc turns to it at its rotation speed (bTurn 0: no snap).
    /// </summary>
    public static float LookYaw(Vector3 from, Vector3 target)
    {
        float fx = target.X - from.X, fy = target.Y - from.Y;
        if (fx == 0f && fy == 0f) return 0f;
        float deg = MathF.Atan2(fx, -fy) * 180.0f / 3.1415927f;
        deg %= 360f;
        return deg < 0f ? deg + 360f : deg;
    }
}
