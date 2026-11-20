using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Outbound InteractNpc (SendOp 0x32, commonfiles/opcodes.h:355): CNpc::SendInteractNpc (@0x141094730,
/// unit_14108e1e0_npcactorandvibrate.cpp:1439), sent by StateInteractionNpc::vfunc_8 (stateinteractdata.cpp:620)
/// once the interaction cast finishes. Wire: i32 npc object id.
/// </summary>
public sealed record InteractNpcRequest(int ObjectId) : IOutPacket
{
    public Outbound Op => Outbound.InteractNpc;
    public void Write(ByteWriter w) => w.WriteInt(ObjectId);
}

/// <summary>
/// Inbound InteractNpc (RecvOp 0x59), CSceneStateGame::OnInteractNpc (@0x141719990, scenestategame.cpp:3634):
/// i32 npc object id; when the npc is in the field, u8 talkable (FUN_14174a560 sets the npc's talkable flag, the
/// byte FieldAddNpc also reads, read by InteractNpcAction::FindInteractNpc). The byte is not read when the npc is
/// missing, so it is optional here.
/// </summary>
public readonly record struct InteractNpcMessage(int ObjectId, bool HasTalkable, bool Talkable)
{
    public static InteractNpcMessage Read(ByteReader r)
    {
        int id = r.ReadInt();
        if (r.Available <= 0) return new InteractNpcMessage(id, false, false);
        return new InteractNpcMessage(id, true, r.ReadByte() != 0);
    }
}
