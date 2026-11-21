using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Outbound 0xC0 (SendOp::PersonalInfoAgreement, commonfiles/opcodes.h:493): i32 group, i32 flags. CPersonalInfoAgreement::SendAgreement
/// (@0x141261dd0, gameoptionstruct.cpp:3445-3452) writes the group it was asked for (the option page passes the mode 2 or 4,
/// gameoptionpersonalinfoagreement.cpp:161,205) and then the whole flag word.
/// </summary>
public sealed record PersonalInfoAgreement(int Group, int Flags) : IOutPacket {
    public Outbound Op => Outbound.PersonalInfoAgreement;
    public void Write(ByteWriter w) { w.WriteInt(Group); w.WriteInt(Flags); }
    public static PersonalInfoAgreement Read(ByteReader r) { var p = new PersonalInfoAgreement(r.ReadInt(), r.ReadInt()); r.ExpectEnd("PersonalInfoAgreement"); return p; }
}
