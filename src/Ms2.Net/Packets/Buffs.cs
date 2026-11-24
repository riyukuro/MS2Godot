// Layout from the restored client's CSceneStateGame::OnBuff (scenestategame.cpp:7591, 0x141720480), AdditionalCli::FUN_14096be10
// (additionalcli_1.cpp:131) and AdditionalCliList::DecodeModifyPacket (additionalcli_2.cpp:278).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>Unk_AdditionalState: the start and end server ticks, the effect id and level, the overlap count and the enabled flag.</summary>
public readonly record struct BuffState(uint StartTick, uint EndTick, uint Id, ushort Level, int OverlapCount, byte Enabled)
{
    public static BuffState Read(ByteReader r) =>
        new(unchecked((uint)r.ReadInt()), unchecked((uint)r.ReadInt()), unchecked((uint)r.ReadInt()), unchecked((ushort)r.ReadShort()), r.ReadInt(), r.ReadByte());
}

/// <summary>
/// Inbound 0x47 (OnBuff): a u8 mode, then the owner header (Unk_AdditionalOwner: the owner object id, the effect's key, the
/// caster object id).
/// - Mode 0, add: the state and the value (Unk_AdditionalValue: the i64 shield).
/// - Mode 1, remove: the header only.
/// - Mode 2, modify: u32 flags; &amp;1 the state, &amp;2 the value.
/// The FieldAddUser buff list carries mode 0's body without the mode byte (<see cref="FromAdd"/>).
/// </summary>
public sealed record BuffPacket(byte Mode, int OwnerObjectId, int Key, int CasterObjectId, uint Flags, BuffState? State, long? Shield)
{
    public const byte Add = 0, Remove = 1, Modify = 2;
    /// <summary>An add from the FieldAddUser list (DecodeListPacket sets the effect's m_b40, additionalcli_2.cpp:730).</summary>
    public bool FromList { get; init; }
    public const uint ModifyState = 1, ModifyValue = 2;

    public static BuffPacket Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        int owner = r.ReadInt(), key = r.ReadInt(), caster = r.ReadInt();
        switch (mode)
        {
            case Add:
            {
                var state = BuffState.Read(r);
                return new(mode, owner, key, caster, 0, state, r.ReadLong());
            }
            case Modify:
            {
                uint flags = unchecked((uint)r.ReadInt());
                BuffState? state = (flags & ModifyState) != 0 ? BuffState.Read(r) : null;
                long? shield = (flags & ModifyValue) != 0 ? r.ReadLong() : null;
                return new(mode, owner, key, caster, flags, state, shield);
            }
            default: return new(mode, owner, key, caster, 0, null, null);
        }
    }

    /// <summary>One FieldAddUser buff as an add.</summary>
    public static BuffPacket FromAdd(Buff b) =>
        new(Add, b.OwnerObjectId, b.BuffObjectId, b.CasterObjectId, 0,
            new BuffState(unchecked((uint)b.StartTick), unchecked((uint)b.EndTick), unchecked((uint)b.SkillId), unchecked((ushort)b.Level), b.OverlapCount, b.Enabled),
            b.Shield);
}
