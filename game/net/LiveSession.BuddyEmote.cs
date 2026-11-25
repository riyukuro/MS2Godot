using System;
using Godot;

namespace Ms2Godot.Net;

/// <summary>
/// The couple emotions' BuddyEmote packets (out 0xA3, in 0x10D). The invite (mode 0) comes from the own couple emotion cast
/// (StateSkill::FUN_1417eaa50, stateskill.cpp:1982-1986).
/// </summary>
public partial class LiveSession
{
    /// <summary>CNetworkService::OnBuddyEmote (@0x14153e280): each read message (null for a mode the reader has no case for,
    /// which the client's switch also skips). The HUD runs CoupleEmotionInbound.Handle on it.</summary>
    public event Action<Ms2.Net.Packets.BuddyEmoteMessage> BuddyEmoteReceived;

    /// <summary>MyPC::vfunc_121's case 0x13 (mypc.cpp:3529-3535): after a ride off answered with a
    /// RideOffActionCoupleEmotion, RequestCoupleEmotion(the asker, the emotion).</summary>
    public event Action<long, int> CoupleAfterRideOff;

    void OnBuddyEmote(Ms2.Net.Packets.BuddyEmoteMessage m)
    {
        if (LogPackets || LiveCheck) GD.Print($"live: BuddyEmote received, {m}");
        if (m != null) BuddyEmoteReceived?.Invoke(m);
    }

    /// <summary>The other player of an object id (FindPCById) for the couple state.</summary>
    public Ms2.Ui.Skills.CouplePeer CouplePeerByObject(int objectId) => remote?.CouplePeerByObject(objectId);
    /// <summary>The partner's feet, state, sub and animation index.</summary>
    public CouplePartnerView? CouplePartnerOf(int objectId) => remote?.CouplePartnerOf(objectId);
    /// <summary>A scripted live run's line on another player (<see cref="RemoteLayer.WatchLine"/>).</summary>
    public string RemoteWatchLine(int objectId) => remote?.WatchLine(objectId) ?? "-";
    /// <summary>The other player's gender, -1 when none.</summary>
    public int GenderOf(int objectId) => remote?.GenderOf(objectId) ?? -1;

    /// <summary>An own BuddyEmote send. False offline.</summary>
    public bool SendBuddyEmote(Ms2.Net.Wire.IOutPacket p)
    {
        if (game == null) return false;
        Send(p);
        if (LogPackets) GD.Print($"live: BuddyEmote sent, {p}");
        return true;
    }
}
