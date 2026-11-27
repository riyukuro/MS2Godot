using System;
using Godot;

namespace Ms2Godot.Net;

/// <summary>
/// The buddy badge's BuddyBadge packets (out 0xA4, in 0x10E): the couple effect system's sends (SetEffectMode, the newcomer
/// and the name request) and OnBuddyBadge's show, hide and partner name, which the field's PCs (NpcEmoticons) run.
/// </summary>
public partial class LiveSession
{
    /// <summary>CNetworkService::OnBuddyBadge (@0x14153f6c0, networkservice.cpp:1187-1202): each read message.</summary>
    public event Action<Ms2.Net.Packets.BuddyBadgeMessage> BuddyBadgeReceived;

    /// <summary>The last BuddyBadge received and sent (for the live checks).</summary>
    public string LastBuddyBadgeIn { get; private set; } = "";
    public string LastBuddyBadgeOut { get; private set; } = "";
    public int BuddyBadgesSent { get; private set; }

    void OnBuddyBadge(Ms2.Net.Packets.BuddyBadgeMessage m)
    {
        LastBuddyBadgeIn = m.ToString();
        if (LogPackets || LiveCheck) GD.Print($"live: BuddyBadge received, {m}");
        BuddyBadgeReceived?.Invoke(m);
    }

    /// <summary>An own BuddyBadge send (FUN_140710590). False offline.</summary>
    public bool SendBuddyBadge(Ms2.Net.Wire.IOutPacket p)
    {
        if (game == null) return false;
        Send(p);
        LastBuddyBadgeOut = p.ToString();
        BuddyBadgesSent++;
        if (LogPackets || LiveCheck) GD.Print($"live: BuddyBadge sent, {p}");
        return true;
    }

    /// <summary>A kept effect at a client space point with no actor (FUN_1411a49c0 with owner 0, the identity rotation and
    /// scale 1): its id, 0 when it did not spawn.</summary>
    public int SpawnKeptEffectAt(string effect, System.Numerics.Vector3 at) => remote?.SpawnKeptEffectAt(effect, at) ?? 0;
}
