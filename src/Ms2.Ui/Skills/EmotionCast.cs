using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2.Ui.Skills;

/// <summary>
/// The own cast of an emotion skill: StateSkill::FUN_1417ea570 @0x1417ea570 (stateskill.cpp:1807-1816) hands a skill with
/// IsEmotion to StateSkill::FUN_1417eaa50 @0x1417eaa50 (:1875-1993), which never starts the skill state (it returns 0, so
/// MyPC::vfunc_102 neither enters 0x10 nor fires a first attack, mypc.cpp:1207-1227) and sends no mode 0 cast.
/// Main type 0xf, the couple emotion (:1914-1990), is <see cref="CoupleEmotionCast.Use"/>.
/// </summary>
public static class EmotionCast
{
    /// <summary>gostate.h: StateEmotion and StateEmotionIdle.</summary>
    public const int StateEmotion = 0xb, StateEmotionIdle = 0xf;
    /// <summary>skilldata mainType of a social emotion (the dynamic action dialog's) and of a couple emotion.</summary>
    public const int MainTypeEmotion = 0xe, MainTypeCouple = 0xf;

    /// <summary>What FUN_1417eaa50 did with a main type 0xe record.</summary>
    public readonly record struct Outcome(bool Handled, bool Enter, EmoteUseRequest Send);

    /// <summary>
    /// FUN_1417eaa50's main type 0xe branch (stateskill.cpp:1878-1913) for the own pc: nothing while the body (the ridee's when
    /// riding) is jumping (:1883-1884) or on water (PCBase::FUN_141675ed0, the hit report's entity of kind 8, :1889-1890), nor
    /// in states 5, 8, 9, 0xd, 0x10, 0x1d, 0x1e (fall, ladder, rope, hit, skill, climb, :1891-1902). Otherwise
    /// PCBase::FUN_14166ed70 (<see cref="Enters"/>) and, for a non-zero id, Emote mode 2 with the id and the skill's paramStr
    /// (:1904-1910); *pResult = 1 either way (<see cref="Outcome.Handled"/>: the cast's cooldown, FUN_141525ff0, mypc.cpp:1230).
    /// <paramref name="runningAnim"/> is StateEmotion's m_strAnim when the pc is in 0xb (FUN_14167f950), else null.
    /// </summary>
    public static Outcome Use(SkillData skill, bool jumping, bool onWater, int stateId, string runningAnim)
    {
        if (skill == null || skill.MainType != MainTypeEmotion) return default;
        if (jumping || onWater) return default;
        if (stateId is 5 or 8 or 9 or 0xd or 0x10 or 0x1d or 0x1e) return default;
        bool enter = Enters(skill, stateId, runningAnim);
        var send = skill.Id != 0 ? new EmoteUseRequest((int)skill.Id, skill.ParamStr) : null;
        return new Outcome(true, enter, send);
    }

    /// <summary>PCBase::FUN_14166ed70 @0x14166ed70 (pcbase.cpp:3674-3685): no row, or StateEmotion already playing the
    /// row's paramStr, keeps the state; else vfunc_59(0xb, StateEmotionData(id), 0, 1).</summary>
    public static bool Enters(SkillData skill, int stateId, string runningAnim) =>
        skill != null && !(stateId == StateEmotion && runningAnim == skill.ParamStr);

    /// <summary>PCBase::GetEmotionAnimation @0x14166f790 (pcbase.cpp:1559-1570): the body animation of kind
    /// <paramref name="kind"/> (0 anim, 1 idleAnim, 2-4 the bore animations) of the pc's emotion group's emotion of that name;
    /// null without a group, the emotion or an animation of that kind.</summary>
    public static string Animation(EmotionGroup group, string name, int kind)
    {
        if (group?.Find(name) is not { } e) return null;
        var a = e.Animation(kind);
        return a.Length == 0 ? null : a;
    }
}

/// <summary>
/// StateEmotionIdle's sub state and bore timer (stateemotionidle.cpp:657-803): m_n10 is 0x6f for the idle animation (kind 1)
/// and 0x70 + n for a bore animation (kind 2 + n); vfunc_9 plays the sub's kind each frame without a restart, and
/// EndOfSequence (:832-840) goes back to 0x6f.
/// </summary>
public sealed class EmotionIdleSub
{
    public const int Idle = 0x6f;
    long timerEnd = -1;            // m_timer: the end in ms, -1 when not running

    /// <summary>m_n10, the sync record's sub state.</summary>
    public int Sub { get; private set; } = Idle;

    /// <summary>vfunc_4 (:657-661): the timer reset, m_n10 = 0x6f.</summary>
    public void Enter()
    {
        timerEnd = -1;
        Sub = Idle;
    }

    /// <summary>vfunc_9 (:806-829): the animation kind of the sub (1 for 0x6f and any other).</summary>
    public int Kind => Sub switch { 0x70 => 2, 0x71 => 3, 0x72 => 4, _ => 1 };

    /// <summary>
    /// The end of vfunc_8 (:777-802): an expired timer is reset and, when EmotionBoreAnimProbability (s_nChance) is at least
    /// GetRandomRange(100, 0), the bore animations of the emotion are counted (kinds 2-4 that exist) and with any, m_n10 =
    /// 0x70 + FUN_140767ff0() % count (the kind is 2 + the pick, whether or not that one exists); a stopped timer starts with
    /// BoreWaitingTick (ms). <paramref name="rand"/> is the client's s_random draw.
    /// </summary>
    public void Tick(long nowMs, int delayMs, int chance, System.Func<uint> rand, System.Func<int, bool> hasKind)
    {
        if (timerEnd >= 0 && nowMs >= timerEnd)
        {
            timerEnd = -1;
            if ((uint)chance >= rand() % 100)
            {
                uint count = 0;
                for (int k = 2; k <= 4; k++) if (hasKind(k)) count++;
                if (count != 0) Sub = (int)(rand() % count) + 0x70;
            }
        }
        else if (timerEnd < 0) timerEnd = nowMs + delayMs;
    }

    /// <summary>EndOfSequence (:832-840): a bore animation's end goes back to 0x6f.</summary>
    public void EndOfSequence()
    {
        if (Sub != Idle) Sub = Idle;
    }
}
