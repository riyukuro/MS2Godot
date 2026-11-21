using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>One GameEventShutdownInteractObjectId (commonfiles/gameevent.cpp:4254-4335): the event id, then the
/// (field id, interact object id) pairs it shuts down.</summary>
public sealed record ShutdownInteractObjectEvent(int Id, IReadOnlyList<(uint FieldId, int ObjectId)> Objects);

/// <summary>
/// Inbound GameEvent (RecvOp 0xB2, commonfiles/opcodes.h:185), GameEventManagerCli::Dispatch (@0x140ceab50,
/// gameeventmanagercli.cpp:77): a u8 kind, then
/// 0 (Packet0, the list) and 1 (Packet1, added): i32 count; each: the type name as a unicode string (Create makes
///   GameEvent{name}Cli or GameEvent{name}, :248-255), then that type's vfunc_4 body; each is Added (:211-220);
/// 2 (Packet2, removed): i32 count; each: i32 event id (:171-185);
/// 3 (Packet3, replaced): as 0, each Updated (:236-242).
/// Every body starts with GameEvent::vfunc_4's i32 id (gameeventbase.cpp:196-199).
/// Only GameEventShutdownInteractObjectId is read (vfunc_4, gameevent.cpp:4306-4317: i32 count; each: u32 field id,
/// i32 object id). The other types' bodies are not ported, so the reader stops at the first entry of another type and
/// sets Truncated; the entries after it are not read.
/// STILL-OPEN: the other GameEvent types' vfunc_4 bodies (gameevent.cpp).
/// </summary>
public sealed record GameEventMessage(byte Kind, IReadOnlyList<ShutdownInteractObjectEvent> Shutdowns, IReadOnlyList<int> RemovedIds,
    bool Truncated)
{
    public const string ShutdownInteractObjectName = "ShutdownInteractObjectId";

    public static GameEventMessage Read(ByteReader r)
    {
        byte kind = r.ReadByte();
        var shutdowns = new List<ShutdownInteractObjectEvent>();
        var removed = new List<int>();
        bool truncated = false;
        switch (kind)
        {
            case 0:
            case 1:
            case 3:
            {
                int n = r.ReadInt();
                for (int i = 0; i < n; i++)
                {
                    string name = r.ReadUnicodeString();
                    if (name != ShutdownInteractObjectName)
                    {
                        truncated = true;     // a type whose body is not ported: its length is unknown, stop here
                        break;
                    }
                    shutdowns.Add(ReadShutdown(r));
                }
                break;
            }
            case 2:
            {
                int n = r.ReadInt();
                for (int i = 0; i < n; i++) removed.Add(r.ReadInt());
                break;
            }
        }
        return new GameEventMessage(kind, shutdowns, removed, truncated);
    }

    static ShutdownInteractObjectEvent ReadShutdown(ByteReader r)
    {
        int id = r.ReadInt();
        int n = r.ReadInt();
        var objects = new List<(uint, int)>();
        for (int i = 0; i < n; i++)
        {
            uint field = (uint)r.ReadInt();
            int obj = r.ReadInt();
            objects.Add((field, obj));
        }
        return new ShutdownInteractObjectEvent(id, objects);
    }
}
