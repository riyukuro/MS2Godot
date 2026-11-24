// Inbound 0x19 FieldEntrance: CSceneStateGame::OnFieldEntrance (@0x14170fc70, scenestategame.cpp:2366).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound 0x19 (OnFieldEntrance, scenestategame.cpp:2366-2385): the dungeon enter answer. u8 result, then:
/// <list type="bullet">
/// <item>0: <c>CMyCharacter::OnDungeonEnterStatePacket</c> (@0x140d9f710, notifydebugstate.cpp:244): i32 count, then count x
/// (i32 dungeon id, u8 state). Each pair is stored by id (the table keeps ids a later packet does not name), then the
/// NotifyDungeonEnterState is posted (<see cref="Entries"/>).</item>
/// <item>1: a notice, no body (ShowSystemNotice(GetStringCommon(0x99d), 1, 1)).</item>
/// <item>2: u8 type, i32 id: ShowEnterLimitMessage(id, type) (@0x1412b9870): <see cref="Type"/> is the reason (12 to 18),
/// <see cref="Id"/> the dungeon room.</item>
/// </list>
/// Any other result reads nothing more (the client has no branch for it). The wire is the same on KMS and GMS2.
/// </summary>
public sealed record FieldEntrance(byte Result, IReadOnlyList<DungeonEnterEntry> Entries, byte Type, int Id)
{
    public static FieldEntrance Read(ByteReader r)
    {
        byte result = r.ReadByte();
        switch (result)
        {
            case 0:
            {
                int count = r.ReadInt();
                if (count < 0 || count > r.Available / 5) throw new InvalidDataException($"FieldEntrance count {count}");
                var list = new List<DungeonEnterEntry>(count);
                for (int i = 0; i < count; i++) list.Add(new DungeonEnterEntry(r.ReadInt(), r.ReadByte()));
                r.ExpectEnd("FieldEntrance state");
                return new FieldEntrance(result, list, 0, 0);
            }
            case 2:
            {
                byte type = r.ReadByte();
                int id = r.ReadInt();
                r.ExpectEnd("FieldEntrance limit");
                return new FieldEntrance(result, Array.Empty<DungeonEnterEntry>(), type, id);
            }
            default:
                // result 1 has no body; an unknown result is not read at all
                return new FieldEntrance(result, Array.Empty<DungeonEnterEntry>(), 0, 0);
        }
    }
}

/// <summary>One (dungeon id, enter state) pair of the OnDungeonEnterStatePacket.</summary>
public readonly record struct DungeonEnterEntry(int DungeonId, byte State);
