using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui;

/// <summary>What WorldService::FUN_141bc80c0 reads before it sends a channel change.</summary>
public sealed class ChannelChangeState
{
    /// <summary>GetLocalPlayer() != null.</summary>
    public bool HasPlayer;
    /// <summary>GetCurrentChannel (@0x14092f640): DAT_1439f4a7c, or DAT_1439f4a80 while that is 0.</summary>
    public int Current;
    /// <summary>FUN_14092f660 (DAT_1439f4a7c == 0): the server announced no channel in ServerEnter.</summary>
    public bool NoServerChannel;
    /// <summary>FUN_140930b10 (DAT_143ab2878 != 0): the field's service type, set by RequestFieldEnter, is not 0.</summary>
    public bool ServiceTypeSet;
    /// <summary>PCBase::vfunc_65 (m_unk_160, the emotion SetEmotion stores) == 1. STILL-OPEN: the emotion is not tracked
    /// here, so it stays false.</summary>
    public bool EmoteActive;
    /// <summary>PCBase::IsDead == 1.</summary>
    public bool Dead;
}

/// <summary>
/// WorldService::FUN_141bc80c0 @0x141bc80c0 (worldservice_1.cpp:4936), the channel change request: the local checks, their
/// notices (FUN_141275260(id, 1, 1)), and the Channel packet. Every caller (the minimap's and the system menu's
/// OnChangeChannel, the MoveChannel user command, the message box answer) goes through it.
/// </summary>
public static class ChannelChange
{
    /// <summary>The string id of the notice when no channel change is allowed (no channel from the server, or a field service type).</summary>
    public const int NoticeNotAllowed = 0x62a;
    /// <summary>The notice while an emote is active (PCBase::vfunc_65 == 1).</summary>
    public const int NoticeEmote = 0x62b;
    /// <summary>The notice while the player is dead.</summary>
    public const int NoticeDead = 0x62c;

    /// <summary>
    /// False (nothing sent) with no player or when the channel is the current one; false with a notice for the other
    /// refusals; otherwise <paramref name="prepare"/> (MyPC::SendFunctionCube) runs, the packet goes through
    /// <paramref name="send"/> and the result is true. The server answers with a GameToGame move.
    /// </summary>
    public static bool Request(int channel, ChannelChangeState s, Action<int> notice, Action prepare, Action<IOutPacket> send)
    {
        if (!s.HasPlayer) return false;
        if (s.Current == channel) return false;
        if (s.NoServerChannel || s.ServiceTypeSet) { notice(NoticeNotAllowed); return false; }
        if (s.EmoteActive) { notice(NoticeEmote); return false; }
        if (s.Dead) { notice(NoticeDead); return false; }
        prepare();
        send(new ChannelRequest((short)channel));
        return true;
    }
}
