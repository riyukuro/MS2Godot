using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>Outbound SmartPush 0x83 (CSmartPush::vfunc_5, smartpush.cpp:322): i32 the push id, i32 its m_n1c, i32 0, bool accept.</summary>
public sealed record SmartPushAnswer(int Id, int N1c, bool Accept) : IOutPacket {
    public Outbound Op => Outbound.SmartPush;
    public void Write(ByteWriter w) { w.WriteInt(Id); w.WriteInt(N1c); w.WriteInt(0); w.WriteBool(Accept); }
}
