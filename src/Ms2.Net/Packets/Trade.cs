// Layout from the restored client's CTradeManagerEx (client/ms2/trademanagerex_1.cpp): OnTrade (@0x14185bd20, :336) reads
// RecvOp::Trade 0x82, and the SendTradeMode* functions (:242-334) write SendOp::Trade 0x49. Paths are relative to the main
// repo src/ms2/.
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Trade (0x82): u8 code (OnTrade :338), then per code what its handler reads:
/// 0 OnTradeRequest (:385): the name, i64 the requester's character id;
/// 1 OnTradeRequestFailed (:433): u8 the result, the name, i32 an item id (FUN_141811980(1, id) names it for 0x14 / 0x15),
/// i32 a level (kept as a u16);
/// 2 OnTradeRequestSent (:425): nothing;
/// 4 FUN_14185cc60 (:519): the name (the partner declined);
/// 5 OnTradeStarted (:527): i64 the partner's character id;
/// 6 OnTradeFinished (:544): u8 1 when the trade went through;
/// 8 OnTradeItemAdded (:561): u8 mine, i32 the item id, i64 the uid, i32 the rarity, i32 the item's slot (SetSlot: the
/// trade item table keys my items by it, :154), i32 the amount (SetCountClamped), i32 the trade slot, then the item body (ReadFromStream; nothing more when FUN_140523c40 fails);
/// 9 OnTradeItemRemoved (:592): u8 mine, i32 the trade slot, i64 the uid;
/// 10 FUN_14185d4f0 (:612): u8 the side (1 = mine), i64 the mesos;
/// 11 OnTradeConfirm (:623) and 12 FUN_14185d610 (:635): u8 mine;
/// 13 FUN_14185d720 (:654) and 14 FUN_14185d750 (:660): nothing.
/// The handlers of 8-13 read only while the trade dialog is open (the caller drops the message otherwise).
/// </summary>
public sealed record TradeMessage(byte Code) {
    public string Name { get; init; } = "";
    /// <summary>Codes 0 and 5: the other character's id; codes 8 and 9: the item uid.</summary>
    public long Id { get; init; }
    /// <summary>Code 1: the result; code 6: 1 = accepted; codes 8, 9, 11, 12: 1 = my side; code 10: the side.</summary>
    public byte Flag { get; init; }
    /// <summary>Code 1: the item id of results 0x14 / 0x15.</summary>
    public int ItemId { get; init; }
    /// <summary>Code 1: the level of results 0x12-0x15 (ReadInt32 cast to unsigned short, :438).</summary>
    public ushort Level { get; init; }
    /// <summary>Codes 8 and 9: the trade slot.</summary>
    public int TradeSlot { get; init; }
    /// <summary>Code 10: the side's mesos.</summary>
    public long Meso { get; init; }
    /// <summary>Code 8: the item (its Amount and InventorySlot, the item's slot, set from the record); null when the item data does not know
    /// the id (FUN_140523c40 fails: the client closes the dialog, vfunc_67, :577).</summary>
    public EquipItem Item { get; init; }

    /// <summary>OnTrade's read: every known code is read to its end (ExpectEnd), except code 8 with an item the item data
    /// does not know, whose body is left unread as the client leaves it (it returns at :577); other codes are not read
    /// past the code.</summary>
    public static TradeMessage Read(ByteReader r) {
        byte c = r.ReadByte();
        TradeMessage m;
        switch (c) {
            case 0: m = new(c) { Name = r.ReadUnicodeString(), Id = r.ReadLong() }; break;
            case 1: m = new(c) { Flag = r.ReadByte(), Name = r.ReadUnicodeString(), ItemId = r.ReadInt(), Level = (ushort)r.ReadInt() }; break;
            case 2: case 13: case 14: m = new(c); break;
            case 4: m = new(c) { Name = r.ReadUnicodeString() }; break;
            case 5: m = new(c) { Id = r.ReadLong() }; break;
            case 6: m = new(c) { Flag = r.ReadByte() }; break;
            case 8: {
                byte mine = r.ReadByte();
                int id = r.ReadInt();
                long uid = r.ReadLong();
                int rarity = r.ReadInt();
                int itemSlot = r.ReadInt();                // nCount (:569) -> SetSlot (:582), the table slot
                int amount = r.ReadInt();                  // nSlotInItem (:570) -> SetCountClamped (:581)
                int slot = r.ReadInt();                    // nSlot (:571) -> SetTradeSlotItem
                if (ItemReader.Known != null && !ItemReader.Known(id))
                    // FUN_140523c40 fails: the client closes the dialog and returns (:577) with the item body unread, so
                    // there is no end to check here.
                    return new(c) { Flag = mine, Id = uid, TradeSlot = slot };
                var it = ItemReader.ReadItem(r, id, rarity);
                it.Uid = uid;                              // SetItemUid after ReadFromStream (:580)
                it.Amount = amount;
                it.InventorySlot = (short)itemSlot;        // CItem's slot (GetSlot), here the trade table's
                m = new(c) { Flag = mine, Id = uid, TradeSlot = slot, Item = it };
                break;
            }
            case 9: m = new(c) { Flag = r.ReadByte(), TradeSlot = r.ReadInt(), Id = r.ReadLong() }; break;
            case 10: m = new(c) { Flag = r.ReadByte(), Meso = r.ReadLong() }; break;
            case 11: case 12: m = new(c) { Flag = r.ReadByte() }; break;
            default: return new(c);
        }
        r.ExpectEnd($"Trade code {c}");
        return m;
    }
}

/// <summary>
/// SendOp::Trade 0x49: u8 mode, then per mode what its SendTradeMode* writes (trademanagerex_1.cpp):
/// 0 SendTradeMode0 (@0x14185b870): i64 the character id to ask;
/// 2 SendTradeMode2 (@0x14185bc40): i64 the requester (the request box was shown);
/// 3 / 4 FUN_14185b8e0 (@0x14185b8e0): i64 the requester, 3 accept, 4 decline;
/// 7 SendTradeMode7 (@0x14185b970): nothing (the dialog closed);
/// 8 SendTradeMode8 (@0x14185b9d0): i64 the item uid, i32 the amount, i32 the trade slot;
/// 9 SendTradeMode9 (@0x14185ba70): i64 the item uid, i32 the trade slot;
/// 10 SendTradeMode10 (@0x14185baf0): i64 the mesos;
/// 11 SendTradeMode11 (@0x14185bb80) lock and 13 SendTradeMode13 (@0x14185bbe0) confirm: nothing;
/// 14 SendTradeMode14 (@0x14185bcb0): i64 the requester (a message box was already up).
/// </summary>
public sealed record TradeRequest(byte Mode, long Id = 0, int Amount = 0, int Slot = 0) : IOutPacket {
    public Outbound Op => Outbound.Trade;

    public static TradeRequest Ask(long characterId) => new(0, characterId);
    public static TradeRequest Shown(long requester) => new(2, requester);
    public static TradeRequest Answer(long requester, bool accept) => new(accept ? (byte)3 : (byte)4, requester);
    public static TradeRequest Close() => new(7);
    public static TradeRequest AddItem(long uid, int amount, int slot) => new(8, uid, amount, slot);
    public static TradeRequest RemoveItem(long uid, int slot) => new(9, uid, 0, slot);
    public static TradeRequest SetMeso(long meso) => new(10, meso);
    public static TradeRequest Lock() => new(11);
    public static TradeRequest Confirm() => new(13);
    public static TradeRequest Busy(long requester) => new(14, requester);

    public void Write(ByteWriter w) {
        w.WriteByte(Mode);
        switch (Mode) {
            case 0: case 2: case 3: case 4: case 10: case 14: w.WriteLong(Id); break;
            case 8: w.WriteLong(Id); w.WriteInt(Amount); w.WriteInt(Slot); break;
            case 9: w.WriteLong(Id); w.WriteInt(Slot); break;
        }
    }
}
