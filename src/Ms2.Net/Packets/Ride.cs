using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Outbound RequestRide mode 1 (SendOp 0x40, commonfiles/opcodes.h:369), MyPC::SendRequestRideMode1 (@0x14152c280,
/// mypc.cpp:3566) with a RideOffActionReact (rideonaction.cpp:759): u8 1, the action's u8 type 20 and u8 argument
/// (RideOffAction::vfunc_1 @0x14077e520), then the reactor's data id and name (RideOffActionReact::vfunc_1 @0x140780ba0,
/// WriteUnicodeStringA: widened, then a unicode string). ReactorAction::vfunc_2 (actionkeyobject.cpp:2915) sends it in place
/// of the react cast when the own pc rides.
/// </summary>
public sealed record RideOffReactRequest(int ReactorId, string Name) : IOutPacket
{
    public const byte ActionType = 20;

    public Outbound Op => Outbound.RequestRide;

    public void Write(ByteWriter w)
    {
        w.WriteByte(1);
        w.WriteByte(ActionType);
        w.WriteByte(0);     // m_nArg: RideOffActionReact(0)
        w.WriteInt(ReactorId);
        w.WriteUnicodeString(Name ?? "");
    }
}
