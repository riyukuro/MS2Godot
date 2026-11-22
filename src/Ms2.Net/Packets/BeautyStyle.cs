// Layout from the restored client's own handler, UIBeautyShopStyleDialog::FUN_140f05a80 @0x140f05a80
// (uibeautyshopstyledialog.cpp:1205), not the server.
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>One saved style of mode 0xf: the item (id, uid, creation time, then its body read by CItem::ReadProxyFromPacket, item.cpp:806) and its style slot (i32, CItem::SetSlot).</summary>
public sealed record BeautyStyleItem(int StyleSlot, EquipItem Item);

/// <summary>
/// Inbound 0x31 as the style dialog (UIBeautyShopStyleDialog, uibeautyshopstyledialog.cpp:1205) reads it: a mode byte.
/// 2: the shop mode (byte, m_26c) and the shop (<see cref="BeautyShopData"/>, m_1b8.Read); 0xd: the saved list is cleared (nothing read);
/// 0xe: the expected style count (u16, m_294), the list is reserved and marked as arrived (m_260);
/// 0x14: the extend type (byte) and the extended slot count (u16); 0xf: a count (u16), then per style the item id (i32), uid (i64),
/// style slot (i32), creation time (i64) and the item body; the dialog starts InitData when the list reaches the expected count;
/// 0x10 and 0x11: the saved item's uid (i64), the new uid (i64), the slot (byte) and the time (i64), read only after the gate
/// (<c>vfunc_55() || m_30 == 0</c> returns early, so a closed dialog reads nothing: every packet is its own buffer, so reading them all is harmless);
/// 0x12: the removed uid (i64); 8: the result code (i32); 0x15 reads nothing.
/// Modes 0x14, 0xf and 0x10-0x15, 8 act on the movie (updateItems, updateItem, removeItemAndAdd, notices and boxes).
/// </summary>
public sealed record BeautyStylePacket(int Mode) {
    public int ShopMode; public BeautyShopData Shop;
    public int ExpectedStyles;
    public int ExtendType, ExtendedSlots;
    public BeautyStyleItem[] Styles = System.Array.Empty<BeautyStyleItem>();
    public long Uid, NewUid, Time; public int Slot;
    public int Result;

    public static BeautyStylePacket Read(ByteReader r) {
        var p = new BeautyStylePacket(r.ReadByte());
        switch (p.Mode) {
            case 2: p.ShopMode = r.ReadByte(); p.Shop = BeautyShopData.Read(r); break;
            case 0xe: p.ExpectedStyles = (ushort) r.ReadShort(); break;
            case 0x14: p.ExtendType = r.ReadByte(); p.ExtendedSlots = (ushort) r.ReadShort(); break;
            case 0xf: {
                int n = (ushort) r.ReadShort();
                p.Styles = new BeautyStyleItem[n];
                for (int i = 0; i < n; i++) {
                    int id = r.ReadInt(); long uid = r.ReadLong(); int slot = r.ReadInt(); long time = r.ReadLong();
                    var it = ItemReader.ReadItem(r, id, 1);   // CItem::FUN_140523c40(id, 1, 0), then ReadProxyFromPacket
                    it.Uid = uid; it.CreationTime = time;
                    p.Styles[i] = new BeautyStyleItem(slot, it);
                }
                break;
            }
            case 0x10: case 0x11: p.Uid = r.ReadLong(); p.NewUid = r.ReadLong(); p.Slot = r.ReadByte(); p.Time = r.ReadLong(); break;
            case 0x12: p.Uid = r.ReadLong(); break;
            case 8: p.Result = r.ReadInt(); break;
        }
        return p;
    }
}
