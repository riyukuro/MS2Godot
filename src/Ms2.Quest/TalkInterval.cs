namespace Ms2.Quest;

/// <summary>
/// UITalkManager::IsTalkIntervalElapsed (@0x141b211d0, uitalkcinematic.cpp:1728-1733): a new npc talk may start only once
/// Common::Constants TalkCooldown (clientconstants.h +0x3730, Define default 1000, unit_1402ab440_constantsblocks.cpp:890)
/// milliseconds have passed since the last talk ended. ResetTalk (:1507-1538) stamps m_nLastTick = GetTickCountMs2() at
/// the end of every talk; the ctor starts it at 0 (:953), which against the system tick count reads as long ago.
/// Asked by the talk key (TalkAction::vfunc_1, actionkeyobject.cpp:1375), the talk start (UITalkManager::FUN_141b1fcf0,
/// uitalkcinematic.cpp:1407) and the mouse's npc pick (CMouseInteractNpc::FindTarget, mouseinteract.cpp:316).
/// </summary>
public sealed class TalkInterval
{
    /// <summary>The constant's key and its Define default.</summary>
    public const string CooldownKey = "TalkCooldown";
    public const int DefaultCooldownMs = 1000;

    uint lastEnd;
    bool ended;

    /// <summary>ResetTalk's m_nLastTick = GetTickCountMs2().</summary>
    public void TalkEnded(uint nowMs)
    {
        lastEnd = nowMs;
        ended = true;
    }

    /// <summary>GetTickCountMs2() - m_nLastTick &lt; cooldown (unsigned) gives 0; else 1. No talk ended yet: elapsed.</summary>
    public bool Elapsed(uint nowMs, int cooldownMs) => !ended || unchecked(nowMs - lastEnd) >= unchecked((uint)cooldownMs);
}
