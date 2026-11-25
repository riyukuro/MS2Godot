// Outbound FunctionCube (SendOp 0x2E, commonfiles/opcodes.h:351).
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// CFunctionCubeObject::vfunc_8 (@0x1412265d0, functioncubeobject.cpp:608-621), a kind 3 or 9 cube taken into use by the
/// action key: FunctionCube u8 5, i32 1, then the one use record FUN_1401fc610 (@0x1401fc610,
/// packetpayloadserializers.cpp:825-829) writes: unicode id, u8 (the record's count, 1).
/// <para>GMS2: the use is mode 4 (the GMS2 server's numbers are one lower from the use on, as in
/// <see cref="FunctionCubeReply.KindOf"/>) with no count: unicode id, u8, as the upstream Maple2 server's FunctionCubeHandler
/// reads it. STILL-OPEN: no GMS2 recording of the request.</para>
/// </summary>
public sealed record FunctionCubeUseRequest(string CubeId) : IProfileOutPacket
{
    public Outbound Op => Outbound.FunctionCube;

    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);

    public void Write(ByteWriter w, ProtocolProfile profile)
    {
        if (profile.IsGms2)
        {
            w.WriteByte(4);
        }
        else
        {
            w.WriteByte(5);
            w.WriteInt(1);
        }
        w.WriteUnicodeString(CubeId ?? "");
        w.WriteByte(1);
    }
}

/// <summary>
/// UIObjectStoreDialog::vfunc_67 (@0x141a72860, uiobjectstoredialog.cpp:122-136), the object store window closed:
/// FunctionCube u8 7, then the store key (the cube id) as a unicode string (WriteUnicodeStringA).
/// <para>GMS2: the same close is mode 6 (GMS2 UIObjectStoreDialog close @0x140e00380 writes 0x2E, then u8 6 at
/// 0x140e003d0, then the key at +0x188), one lower as the use is. Neither server reads it.</para>
/// </summary>
public sealed record FunctionCubeStoreCloseRequest(string Key) : IProfileOutPacket
{
    public Outbound Op => Outbound.FunctionCube;

    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);

    public void Write(ByteWriter w, ProtocolProfile profile)
    {
        w.WriteByte((byte)(profile.IsGms2 ? 6 : 7));
        w.WriteUnicodeString(Key ?? "");
    }
}
