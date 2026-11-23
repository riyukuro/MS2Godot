// Inbound 0x23 FurnishingStorage: CSceneStateGame::OnFurnishingStorage (@0x141715bd0, scenestategame.cpp:2734).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound 0x23 (OnFurnishingStorage, scenestategame.cpp:2734-2815): the housing storage list. u8 mode, then:
/// <list type="bullet">
/// <item>1: clear the table. No body.</item>
/// <item>2: i32 count (the client reads it and does nothing with it).</item>
/// <item>3: add. i32 item id, i64 uid, u8 rarity, i32 slot (the client stores it as the item's slot), then the item body
/// (<see cref="ItemReader.ReadItem(ByteReader, int, int, ProtocolProfile)"/>). An id the item data does not know reads no body
/// (CItem::FUN_140523c40 fails, :2749), so <see cref="Item"/> is null.</item>
/// <item>4: remove. i64 uid.</item>
/// <item>5: purchase notice. i64 uid, i32 value (the client shows it as an item event; no stock change).</item>
/// <item>7: update. i64 uid, i32 amount.</item>
/// <item>8: end of list. The GMS2 server sends it; the client has no case for it and ignores it. No body.</item>
/// </list>
/// Any other mode reads nothing more. The wire is the same on KMS and GMS2 apart from the item body (<paramref name="profile"/>).
/// </summary>
public sealed record FurnishingStorage(byte Mode, int Count, int ItemId, long Uid, byte Rarity, int Slot, EquipItem Item, int Amount)
{
    public static FurnishingStorage Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    public static FurnishingStorage Read(ByteReader r, ProtocolProfile profile)
    {
        byte mode = r.ReadByte();
        switch (mode)
        {
            case 1:
            case 8:
                r.ExpectEnd("FurnishingStorage " + mode);
                return new FurnishingStorage(mode, 0, 0, 0, 0, 0, null, 0);
            case 2:
            {
                int count = r.ReadInt();
                r.ExpectEnd("FurnishingStorage count");
                return new FurnishingStorage(mode, count, 0, 0, 0, 0, null, 0);
            }
            case 3:
            {
                int itemId = r.ReadInt();
                long uid = r.ReadLong();
                byte rarity = r.ReadByte();
                int slot = r.ReadInt();
                if (ItemReader.Known != null && !ItemReader.Known(itemId))
                    return new FurnishingStorage(mode, 0, itemId, uid, rarity, slot, null, 0);   // the client reads no body
                var item = ItemReader.ReadItem(r, itemId, rarity, profile);
                item.Uid = uid;
                r.ExpectEnd("FurnishingStorage add");
                return new FurnishingStorage(mode, 0, itemId, uid, rarity, slot, item, 0);
            }
            case 4:
            {
                long uid = r.ReadLong();
                r.ExpectEnd("FurnishingStorage remove");
                return new FurnishingStorage(mode, 0, 0, uid, 0, 0, null, 0);
            }
            case 5:
            case 7:
            {
                long uid = r.ReadLong();
                int amount = r.ReadInt();
                r.ExpectEnd("FurnishingStorage amount");
                return new FurnishingStorage(mode, 0, 0, uid, 0, 0, null, amount);
            }
            default:
                return new FurnishingStorage(mode, 0, 0, 0, 0, 0, null, 0);
        }
    }
}
