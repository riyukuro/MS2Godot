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
/// i32 object id). The other types' bodies are not ported, so after one of them the reader cannot step to the next
/// entry the way the client does; it looks for the next entry whose name is ShutdownInteractObjectId (a u16 length 24
/// followed by that name in UTF-16) and reads on from there. Truncated is set when entries were passed over that way.
/// </summary>
public sealed record GameEventMessage(byte Kind, IReadOnlyList<ShutdownInteractObjectEvent> Shutdowns, IReadOnlyList<int> RemovedIds,
    bool Truncated)
{
    public const string ShutdownInteractObjectName = "ShutdownInteractObjectId";

    static readonly byte[] Marker = BuildMarker();

    static byte[] BuildMarker()
    {
        var b = new byte[2 + ShutdownInteractObjectName.Length * 2];
        b[0] = (byte)ShutdownInteractObjectName.Length;
        for (int i = 0; i < ShutdownInteractObjectName.Length; i++)
            b[2 + i * 2] = (byte)ShutdownInteractObjectName[i];
        return b;
    }

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
                        truncated = true;
                        break;
                    }
                    shutdowns.Add(ReadShutdown(r));
                }
                if (truncated)
                {
                    var rest = r.Buffer.AsSpan(r.Position, r.Available);
                    int at;
                    while ((at = rest.IndexOf(Marker)) >= 0)
                    {
                        var tail = new ByteReader(rest.Slice(at + Marker.Length).ToArray());
                        ShutdownInteractObjectEvent e;
                        try { e = ReadShutdown(tail); }
                        catch (Exception) { break; }      // a look-alike that runs past the end
                        shutdowns.Add(e);
                        rest = rest.Slice(at + Marker.Length + tail.Position);
                    }
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
