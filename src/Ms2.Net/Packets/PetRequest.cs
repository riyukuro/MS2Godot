using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>Outbound RequestPet u8 0 + i64 the pet item's uid: the summon. StateSummon::vfunc_8 @0x1417f7b60 (statesummon.cpp:326-337)
/// sends it when the cast finished, the item is a pet item with a PetDataTable row and the state is not a transfer (m_b18 0);
/// the uid goes through WriteSummonUid @0x1401fea90 (one i64).</summary>
public sealed record PetSummonRequest(long ItemUid) : IOutPacket
{
    public const byte Mode = 0;

    public Outbound Op => Outbound.RequestPet;

    public void Write(ByteWriter w)
    {
        w.WriteByte(Mode);
        w.WriteLong(ItemUid);
    }
}

/// <summary>Outbound RequestPet u8 1 + i64 the summoned pet item's uid: the unsummon. MyPC::SendRequestPet @0x14152b800
/// (mypc.cpp:3199-3211) returns without a send when the item is not in the inventory (FindItemByUid), else writes mode 1 and the
/// uid through FUN_1401feb20 (one i64).</summary>
public sealed record PetUnsummonRequest(long ItemUid) : IOutPacket
{
    public const byte Mode = 1;

    public Outbound Op => Outbound.RequestPet;

    public void Write(ByteWriter w)
    {
        w.WriteByte(Mode);
        w.WriteLong(ItemUid);
    }
}

/// <summary>Outbound RequestPet u8 3 + i64 the new pet item's uid: the transfer from the summoned pet to another one.
/// StateSummon::vfunc_8 @0x1417f7b60 (statesummon.cpp:316-325) sends it in place of mode 0 when the state data's transfer flag
/// (m_b18, set by FUN_14152b8d0) is on; the uid goes through FUN_1401febb0 (one i64).</summary>
public sealed record PetTransferRequest(long ItemUid) : IOutPacket
{
    public const byte Mode = 3;

    public Outbound Op => Outbound.RequestPet;

    public void Write(ByteWriter w)
    {
        w.WriteByte(Mode);
        w.WriteLong(ItemUid);
    }
}

/// <summary>Outbound RequestPet u8 0xf + bool the new auto attack state. CPetManager::SendRequestPet @0x140c8b340
/// (petmanager.cpp:1118-1126) flips m_nAuto first and sends the flipped value (m_nAuto == 1).</summary>
public sealed record PetAttackRequest(bool Auto) : IOutPacket
{
    public const byte Mode = 0xf;

    public Outbound Op => Outbound.RequestPet;

    public void Write(ByteWriter w)
    {
        w.WriteByte(Mode);
        w.WriteBool(Auto);
    }
}

/// <summary>Outbound RequestPetInventory u8 7: the summoned pet's bag load. CPetInventory::SendRequestPetInventoryMode7 @0x140c88100
/// (petmanager.cpp:481-486), sent by OnFieldAddPet when the pet's owner is the local pc (scenestategame.cpp:9713-9731).</summary>
public sealed record PetInventoryLoadRequest : IOutPacket
{
    public const byte Mode = 7;

    public Outbound Op => Outbound.RequestPetInventory;

    public void Write(ByteWriter w) => w.WriteByte(Mode);
}
