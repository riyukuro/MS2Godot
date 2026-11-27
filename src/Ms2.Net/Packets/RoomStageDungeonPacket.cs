using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound RoomStageDungeon (KMS 0xFF, GMS2 0x101; CNetworkService::OnRoomStageDungeon @0x14153e180, networkservice.cpp:1082): u8 kind.
/// Kind 0 reads i32 room id, u8 stage, bool flag and creates the player's stage (CMyCharacter::CreateStage @0x140d9f400); kind 1 reads nothing
/// more and clears it (ClearStage @0x140d9f4b0). Any other kind does nothing, and the client does not look at bytes left in the body.
/// </summary>
public sealed record RoomStageDungeonPacket(bool Create, int RoomId, byte Stage, bool Flag) {
    /// <summary>The packet, null for a kind the client ignores.</summary>
    public static RoomStageDungeonPacket Read(ByteReader r) {
        switch (r.ReadByte()) {
            case 0: { int room = r.ReadInt(); byte stage = r.ReadByte(); bool flag = r.ReadBool(); return new(true, room, stage, flag); }
            case 1: return new(false, 0, 0, false);
            default: return null;
        }
    }
}
