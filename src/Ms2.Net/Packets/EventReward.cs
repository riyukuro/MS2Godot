// The EventReward send (SendOp::EventReward 0x97) as UICommunityDialog's OnVIPGMClick (client/ms2/uibuddyeventlistener.cpp,
// vfunc_28 @0x1418d3d70) writes it.
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>EventReward: u8 mode, u8 value. OnVIPGMClick sends mode 5, value 1; what the server does with it is
/// STILL-OPEN.</summary>
public sealed record EventRewardRequest(byte Mode, byte Value) : IOutPacket {
    public Outbound Op => Outbound.EventReward;
    public void Write(ByteWriter w) { w.WriteByte(Mode); w.WriteByte(Value); }
}
