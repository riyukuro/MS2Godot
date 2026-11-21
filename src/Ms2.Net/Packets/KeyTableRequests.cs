using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// SendOp::KeyTable 0x3A (commonfiles/opcodes.h:363): the quick slot edits, one sub-mode byte first
// (unnamed/unit_14092f420_accountsessionstate.cpp).

/// <summary>
/// Mode 3, a skill or item put on a quick slot: FUN_14092f690 @0x14092f690 (:352) writes u8 3, i16 the quick slot page,
/// i32 skill id, i32 item id, i64 item uid, i32 the slot index.
/// </summary>
public sealed record KeyTableSetQuickSlotRequest(short Page, int SkillId, int ItemId, long ItemUid, int Slot) : IOutPacket
{
    public Outbound Op => Outbound.KeyTable;

    public void Write(ByteWriter w)
    {
        w.WriteByte(3);
        w.WriteShort(Page); w.WriteInt(SkillId); w.WriteInt(ItemId); w.WriteLong(ItemUid); w.WriteInt(Slot);
    }
}

/// <summary>
/// Mode 5, a quick slot emptied: SendKeyTableMode5 @0x14092fef0 (:481) writes u8 5, i16 the quick slot page, i32 skill id,
/// i64 item uid (no item id, no slot index).
/// </summary>
public sealed record KeyTableRemoveQuickSlotRequest(short Page, int SkillId, long ItemUid) : IOutPacket
{
    public Outbound Op => Outbound.KeyTable;

    public void Write(ByteWriter w)
    {
        w.WriteByte(5);
        w.WriteShort(Page); w.WriteInt(SkillId); w.WriteLong(ItemUid);
    }
}
