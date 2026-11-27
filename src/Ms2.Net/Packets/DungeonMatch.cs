using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound DungeonMatch (CSceneStateGame::OnDungeonMatch @0x14173e4b0, scenestategame.cpp:5184): u8 mode; mode 0 is OnDungeonMatchError
/// (@0x140e146f0, uidungeonhelper.cpp:333): u8 error, i32 parameter, unicode name. The client's switch has no other mode and leaves the
/// rest of the body unread, so <see cref="Read"/> gives null for one (no ExpectEnd there, as in the client).
/// </summary>
public sealed record DungeonMatchError(int Error, int Param, string Name) {
    public static DungeonMatchError Read(ByteReader r) {
        if (r.ReadByte() != 0) return null;
        var m = new DungeonMatchError(r.ReadByte(), r.ReadInt(), r.ReadUnicodeString());
        r.ExpectEnd("DungeonMatch");
        return m;
    }
}
