using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound DungeonWaiting (UIDungeonLobby::OnRoomPacket @0x140e17930, uidungeonlobby.cpp:68): i32 dungeon room id, i32 the second value the
/// lobby shows as its sub title (the lobby's size). The client reads the two and leaves the rest of the body unread, so there is no ExpectEnd.
/// </summary>
public sealed record DungeonWaiting(int RoomId, int Size) {
    public static DungeonWaiting Read(ByteReader r) => new(r.ReadInt(), r.ReadInt());
}
