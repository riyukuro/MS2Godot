using System.Collections.Generic;
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>One entry of Breakable (0x4F): the object's name, its state and the three sync values (CBreakableObject
/// m_nSyncA/B/C, set by FUN_140adeaa0, breakableobject.cpp:349-354).</summary>
public sealed record BreakableEntry(string Name, byte State, byte SyncA, int SyncB, int SyncC);

/// <summary>
/// Inbound Breakable (RecvOp 0x4F, commonfiles/opcodes.h:87), CSceneStateGame::OnBreakable (@0x1417212d0,
/// scenestategame.cpp:3667-3694): u8 mode; mode 1 is one entry, mode 0 an i32 count of entries, any other mode none.
/// Each entry: ReadString (ASCII) name, u8 state, u8, i32, i32.
/// </summary>
public sealed record BreakableUpdate(byte Mode, IReadOnlyList<BreakableEntry> Entries)
{
    public static BreakableUpdate Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        int count = mode == 1 ? 1 : mode == 0 ? r.ReadInt() : 0;
        var list = new List<BreakableEntry>(count > 0 && count < 4096 ? count : 0);
        for (int i = 0; i < count; i++)
            list.Add(new BreakableEntry(r.ReadString(), r.ReadByte(), r.ReadByte(), r.ReadInt(), r.ReadInt()));
        // Another mode reads nothing past the mode (:3670-3674). No ExpectEnd: OnBreakable never checks the end, trailing
        // bytes are ignored.
        return new BreakableUpdate(mode, list);
    }
}

/// <summary>
/// One lift of Liftable (0x73). The client's locals are named the other way round: the i32 goes to
/// CLiftableObject::SetKind and the u8 to SetState (scenestategame.cpp:11966-11971, :11999-12001).
/// QuestA/B go to FUN_1414477f0 (the quest mask m_maskA), QuestC/D to FUN_141447850 (m_maskB); Flag to FUN_141447aa0
/// (m_bC2).
/// </summary>
public sealed record LiftableEntry(string Name, bool Enabled, int Kind, byte State,
                                   string QuestA = null, string QuestB = null, string QuestC = null, string QuestD = null,
                                   bool Flag = false);

/// <summary>
/// Inbound Liftable (RecvOp 0x73, commonfiles/opcodes.h:123), CSceneStateGame::OnLiftable (@0x141729250,
/// scenestategame.cpp:11941-12020): u8 mode.
/// <list type="bullet">
/// <item>0 (:11946-11992): i32 count; each: ReadString name, u8 enabled, i32 kind, u8 state, ReadUnicodeStringA x4, bool.
/// The list is the whole set: the field's lifts not named in it are removed.</item>
/// <item>2 (:11994-12003): ReadString name, u8 enabled, i32 kind, u8 state.</item>
/// <item>3 (:12005, WorldService::FUN_141bc5420, worldservice_1.cpp:4429-4447): a new lift without an entity: ReadString
/// (slot 7) name, i32 kind, ReadUnicodeStringA (slot 6) x4, bool.</item>
/// <item>4 (:12008-12011): ReadString name, the lift to remove.</item>
/// </list>
/// Another mode reads nothing past the mode.
/// </summary>
public sealed record LiftableUpdate(byte Mode, IReadOnlyList<LiftableEntry> Entries, string Name = null)
{
    public const byte ModeList = 0, ModeUpdate = 2, ModeAdd = 3, ModeRemove = 4;

    public static LiftableUpdate Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        var list = new List<LiftableEntry>();
        string name = null;
        switch (mode)
        {
            case ModeList:
                int count = r.ReadInt();
                for (int i = 0; i < count; i++)
                {
                    string n = r.ReadString();
                    bool enabled = r.ReadByte() != 0;
                    int kind = r.ReadInt();
                    byte state = r.ReadByte();
                    string a = r.ReadUnicodeString(), b = r.ReadUnicodeString(), c = r.ReadUnicodeString(), d = r.ReadUnicodeString();
                    list.Add(new LiftableEntry(n, enabled, kind, state, a, b, c, d, r.ReadBool()));
                }
                break;
            case ModeUpdate:
            {
                string n = r.ReadString();
                bool enabled = r.ReadByte() != 0;
                int kind = r.ReadInt();
                list.Add(new LiftableEntry(n, enabled, kind, r.ReadByte()));
                break;
            }
            case ModeAdd:
            {
                string n = r.ReadString();
                int kind = r.ReadInt();
                string a = r.ReadUnicodeString(), b = r.ReadUnicodeString(), c = r.ReadUnicodeString(), d = r.ReadUnicodeString();
                // CLiftableObject::Init (liftableobject.cpp:167-175) clears m_bC0 and sets state 0
                list.Add(new LiftableEntry(n, false, kind, 0, a, b, c, d, r.ReadBool()));
                break;
            }
            case ModeRemove:
                name = r.ReadString();
                break;
            default:
                return new LiftableUpdate(mode, list);
        }
        // no ExpectEnd: OnLiftable never checks the end, trailing bytes are ignored
        return new LiftableUpdate(mode, list, name);
    }
}
