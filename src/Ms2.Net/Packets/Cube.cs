using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>PC::SendRequestCube (@0x14165f170, pc.cpp:1929-1937): with an item held, the item is let go and RequestCube
/// (0x37) is sent with u8 0x12 and nothing else.</summary>
public sealed record HeldItemReleaseRequest : IOutPacket
{
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) => w.WriteByte(0x12);
}

/// <summary>
/// Inbound ResponseCube (RecvOp 0x69, commonfiles/opcodes.h:113), CSceneStateGame::OnResponseCube
/// (scenestategame.cpp:9876): a u8 mode. Only mode 0x12 (:10176-10192) is read: u8 error; then i32 id, which with error 0
/// is the pc whose held item is let go (PC::FUN_14165f1f0(0, 0)) and otherwise the character the build notice is for.
/// Other modes are read as null.
/// </summary>
public sealed record ResponseCubeHeldItem(byte Error, int Id)
{
    public const byte Mode = 0x12;

    public static ResponseCubeHeldItem Read(ByteReader r)
    {
        if (r.ReadByte() != Mode) return null;
        byte error = r.ReadByte();
        return new ResponseCubeHeldItem(error, r.ReadInt());
    }
}
