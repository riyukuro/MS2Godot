using System.Collections.Generic;
using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// The state block CReactor reads for an add (FUN_140b79490, reactor.cpp) and an update (FUN_140b79a10): i32 the index into
/// the reactor's state list (CReactor::GetStateData @0x140b78c00, not a state id), i64 m_n150, u8 count of i64 players
/// (m_setPlayers, cleared first; a repeated id is kept once), i32 m_n158 (stored unsigned).
/// </summary>
public sealed record ReactorState(int StateIndex, long Value150, IReadOnlyList<long> Players, uint Value158)
{
    public static ReactorState Read(ByteReader r)
    {
        int idx = r.ReadInt();
        long v150 = r.ReadLong();
        int n = r.ReadByte();
        var players = new List<long>(n);
        for (int i = 0; i < n; i++)
        {
            long id = r.ReadLong();
            if (!players.Contains(id)) players.Add(id);   // std::set insert
        }
        return new ReactorState(idx, v150, players, (uint)r.ReadInt());
    }
}

/// <summary>A reactor CReactor::FUN_140b79490 reads (mode 0): i32 object id, ReadString name, i32 reactor id (the
/// reactor.xml row), vec3 position, vec3 rotation (degrees), then the <see cref="ReactorState"/> block.</summary>
public sealed record ReactorAdd(int ObjectId, string Name, int ReactorId, Vector3 Position, Vector3 Rotation, ReactorState State);

/// <summary>
/// Inbound Reactor (RecvOp 0x115, commonfiles/opcodes.h:276), CSceneStateGame::OnReactor (@0x14173c870,
/// scenestategame.cpp:6699-6770): u8 mode.
/// 0: a <see cref="ReactorAdd"/> (OnObjectCreated). 1: i32 object id, removed (WorldService::FUN_141bbbcf0(id, 0)).
/// 3: i32 object id and a <see cref="ReactorState"/> for that reactor. 4: i32 count of (i32 item id, u16 grade) for the
/// reward notice 0x4e5d (FUN_141abf6e0(itemId, grade); the whole list is skipped when that dialog is missing).
/// 5: i32 reactor id, the own player's StateReact create animation (vfunc_59(0x3d, StateReactData(id, "", ""), 0x9e, 1)).
/// Any other mode reads nothing. OnReactor never checks the end.
/// </summary>
public sealed record ReactorMessage(byte Mode, ReactorAdd Add, int ObjectId, ReactorState State,
                                    IReadOnlyList<(int ItemId, int Grade)> Rewards, int ReactorId)
{
    public static ReactorMessage Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        switch (mode)
        {
            case 0:
            {
                int objId = r.ReadInt();
                string name = r.ReadString();
                int reactorId = r.ReadInt();
                var pos = r.ReadVec3();
                var rot = r.ReadVec3();
                var add = new ReactorAdd(objId, name, reactorId, pos, rot, ReactorState.Read(r));
                return new ReactorMessage(mode, add, objId, add.State, null, reactorId);
            }
            case 1:
                return new ReactorMessage(mode, null, r.ReadInt(), null, null, 0);
            case 3:
            {
                int objId = r.ReadInt();
                return new ReactorMessage(mode, null, objId, ReactorState.Read(r), null, 0);
            }
            case 4:
            {
                int count = r.ReadInt();
                var list = new List<(int, int)>(count > 0 && count < 4096 ? count : 0);
                for (int i = 0; i < count; i++) list.Add((r.ReadInt(), (ushort)r.ReadShort()));
                return new ReactorMessage(mode, null, 0, null, list, 0);
            }
            case 5:
                return new ReactorMessage(mode, null, 0, null, null, r.ReadInt());
            default:
                return new ReactorMessage(mode, null, 0, null, null, 0);
        }
    }
}

/// <summary>
/// Outbound StateReact (SendOp 0xA9, commonfiles/opcodes.h:472), CReactor::SendStateReact (@0x140b799a0, reactor.cpp):
/// u8 2, i32 the reactor's object id.
/// </summary>
public sealed record StateReactRequest(int ObjectId) : IOutPacket
{
    public Outbound Op => Outbound.StateReact;

    public void Write(ByteWriter w)
    {
        w.WriteByte(2);
        w.WriteInt(ObjectId);
    }
}
