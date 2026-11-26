using Godot;
using Ms2.Net;
using Ms2.Net.Wire;

namespace Ms2Godot.Net;

/// <summary>
/// The game connection's packets drained in the frame (_Process): each is handled on its own (<see cref="Handle"/>'s catch),
/// so a packet whose read or handler throws is warned with its opcode, size and reader (<see cref="InboundFault"/>) and the
/// next packet is handled in the same frame. The catch costs nothing on a packet that does not throw.
/// </summary>
public partial class LiveSession
{
    /// <summary>The packets whose handling threw (a check's count).</summary>
    public int PacketFaults { get; private set; }

    /// <summary>The last such packet's warning.</summary>
    public string LastPacketFault { get; private set; }

    /// <summary>The packets that came since the last frame, in arrival order.</summary>
    void DrainGame()
    {
        while (game != null && game.Incoming.TryDequeue(out var p)) { Handle(p); SyncDeadFlag(); }
    }

    /// <summary>Handle's catch: the packet's warning (its handling stopped at the throw; the session goes on).</summary>
    void PacketFault(RawPacket p, ProtocolProfile profile, System.Exception ex)
    {
        PacketFaults++;
        LastPacketFault = "live: " + InboundFault.Describe(p, profile, ex);
        GD.PushWarning(LastPacketFault);
    }
}
