using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// One GameEventVariable (GameEventVariable::Read @0x14041f300, commonfiles/gameeventuservalue.cpp:78-83): the key (Key::Read
/// @0x14041f0a0: i32 type, i32 id), the value as a unicode string and the expiry as an i64 (CMsTime, server seconds).
/// The client keeps the value while the expiry is later than the server time (GameEventUserValue::Get @0x14041fc20).
/// </summary>
public readonly record struct GameEventVariable(int Type, int Id, string Value, long Time);

/// <summary>
/// Inbound GameEventUserValue (RecvOp 0xCB; GMS2 0xCD), CSceneStateGame::OnGameEventUserValue @0x141735410 (scenestategame.cpp:3902)
/// into FUN_140930fe0 @0x140930fe0 (unnamed/unit_14092f420_accountsessionstate.cpp:911): a u8 kind, then
/// 0 (the whole table): a u8 list; list 0 reads into DAT_143ab2fc0, list 1 into DAT_143ab3010 (GameEventUserValue::Read
///   @0x14041f7c0: the table is cleared, then an i32 count and that many variables);
/// 1 (one value): a u8 list, then one variable (ReadVariable @0x14041f850) stored under its key in that list, and
///   GameEventUserValueManager::Dispatch (@0x140da55a0) tells the listeners of the variable's type.
/// Any other kind, and any list other than 0 and 1, reads nothing more (the switch has no other case): the rest of the body stays
/// unread, so <see cref="List"/> is -1 for it and the end is not checked.
/// </summary>
public sealed record GameEventUserValueMessage(byte Kind, int List, IReadOnlyList<GameEventVariable> Variables)
{
    /// <summary>Whether this message replaces the whole table of its list (kind 0, list 0 or 1).</summary>
    public bool Replaces => Kind == 0 && List >= 0;

    public static GameEventUserValueMessage Read(ByteReader r)
    {
        byte kind = r.ReadByte();
        switch (kind)
        {
            case 0:
            {
                int list = r.ReadByte();
                if (list > 1) return new GameEventUserValueMessage(kind, -1, Array.Empty<GameEventVariable>());
                uint n = (uint)r.ReadInt();
                var vars = new List<GameEventVariable>();
                for (uint i = 0; i < n; i++) vars.Add(Variable(r));
                r.ExpectEnd("GameEventUserValue");
                return new GameEventUserValueMessage(kind, list, vars);
            }
            case 1:
            {
                int list = r.ReadByte();
                if (list > 1) return new GameEventUserValueMessage(kind, -1, Array.Empty<GameEventVariable>());
                var v = Variable(r);
                r.ExpectEnd("GameEventUserValue");
                return new GameEventUserValueMessage(kind, list, new[] { v });
            }
            default:
                return new GameEventUserValueMessage(kind, -1, Array.Empty<GameEventVariable>());
        }
    }

    static GameEventVariable Variable(ByteReader r)
    {
        int type = r.ReadInt(), id = r.ReadInt();
        string value = r.ReadUnicodeString();
        return new GameEventVariable(type, id, value, r.ReadLong());
    }
}
