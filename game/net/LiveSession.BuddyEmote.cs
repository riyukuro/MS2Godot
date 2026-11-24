using Godot;

namespace Ms2Godot.Net;

/// <summary>
/// The couple emotions' BuddyEmote packets (out 0xA3, in 0x10D). The invite (mode 0) comes from the own couple emotion cast
/// (StateSkill::FUN_1417eaa50, stateskill.cpp:1982-1986).
/// </summary>
public partial class LiveSession
{
    /// <summary>An own BuddyEmote send. False offline.</summary>
    public bool SendBuddyEmote(Ms2.Net.Wire.IOutPacket p)
    {
        if (game == null) return false;
        Send(p);
        if (LogPackets) GD.Print($"live: BuddyEmote sent, {p}");
        return true;
    }
}
