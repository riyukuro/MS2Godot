using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound UserSkinColor (RecvOp 0x30, commonfiles/opcodes.h:56): CSceneStateGame::OnUserSkinColor (@0x141717340,
/// scenestategame.cpp:3239-3258). Wire: i32 player object id, then two i32 colours (each 0xRRGGBB, FUN_141b6dea0). The client
/// ends after the second colour without a length check, so the body is read as that and nothing after it is looked at.
/// STILL-OPEN: the colours are kept as the raw ints; applying them to the player's model (PC::FUN_1416591e0 / FUN_141656780)
/// is the field renderer's.
/// </summary>
public sealed record UserSkinColor(int ObjectId, int Color0, int Color1)
{
    public static UserSkinColor Read(ByteReader r) => new(r.ReadInt(), r.ReadInt(), r.ReadInt());
}
