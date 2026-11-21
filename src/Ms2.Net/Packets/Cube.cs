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
/// (scenestategame.cpp:9876): a u8 mode. Read are the two held-item modes; other modes are read as null.
/// <list type="bullet">
/// <item>0x11 (:10145-10167): u8 error; error 0: i32 pc id, i32 cube key, i32 item id, i32 value (the cube's weapon value
/// m_n1c, then the pc holds the item); otherwise i32 the character the build notice is for.</item>
/// <item>0x12 (:10176-10192): u8 error; then i32 id, which with error 0 is the pc whose held item is let go
/// (PC::FUN_14165f1f0(0, 0)) and otherwise the character the build notice is for.</item>
/// </list>
/// </summary>
public sealed record ResponseCubeHeldItem(byte Mode, byte Error, int Id, int CubeKey = 0, int ItemId = 0, int Value = 0)
{
    public const byte ModeHold = 0x11;
    public const byte ModeRelease = 0x12;

    public static ResponseCubeHeldItem Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        if (mode is not (ModeHold or ModeRelease)) return null;
        byte error = r.ReadByte();
        if (mode == ModeHold && error == 0)
        {
            int id = r.ReadInt(), cube = r.ReadInt(), item = r.ReadInt(), value = r.ReadInt();
            return new ResponseCubeHeldItem(mode, error, id, cube, item, value);
        }
        return new ResponseCubeHeldItem(mode, error, r.ReadInt());
    }
}
