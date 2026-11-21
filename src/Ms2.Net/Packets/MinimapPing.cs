using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound MinimapPing (RecvOp 0x121, commonfiles/opcodes.h:288), CSceneStateGame::OnMinimapPing (@0x14173de10,
/// scenestategame.cpp:6806-6815): i64 uid, u8 type (0 red, 1 blue), vec3 position (three floats). The client hands them to
/// the minimap dialog's AddPing.
/// </summary>
public sealed record MinimapPingMessage(long Uid, byte Type, Vector3 Position)
{
    public static MinimapPingMessage Read(ByteReader r)
    {
        var m = new MinimapPingMessage(r.ReadLong(), r.ReadByte(), r.ReadVec3());
        r.ExpectEnd("MinimapPing");
        return m;
    }
}

/// <summary>
/// Outbound MinimapPing (SendOp 0xB1, commonfiles/opcodes.h:480), UIMinimapDialog::SendMinimapPing (@0x141a45020,
/// uiminimapentity.cpp:2545-2564): i64 the local pc's character id (PCBase::FUN_141656a80, pc.cpp:1489), u8 kind (0 red,
/// 1 blue), then the point on the minimap (EncodeVector3, three floats; z is 0).
/// </summary>
public sealed record MinimapPingRequest(long CharacterId, byte Kind, Vector3 Position) : IOutPacket
{
    public Outbound Op => Outbound.MinimapPing;

    public void Write(ByteWriter w)
    {
        w.WriteLong(CharacterId);
        w.WriteByte(Kind);
        w.WriteFloat(Position.X); w.WriteFloat(Position.Y); w.WriteFloat(Position.Z);
    }
}
