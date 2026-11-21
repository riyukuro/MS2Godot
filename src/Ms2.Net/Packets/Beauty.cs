// Layouts from the restored client's readers and senders, not the server: UIBeautyShopDialog::OnBeautyShopPacket
// (uibeautyshopdialog.cpp:1510), CBeautyShopDataCli::Read (@0x1406e5420), CBeautyShopItem::Read (@0x1406e37f0),
// TShopPaymentAttr::Read (@0x1406dc2c0), and every SendOp::Beauty site.
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>TShopPaymentAttr::Read: payment type (byte), payment item id (i32), amount (i32, widened to unsigned), icon tag (CInPacket::ReadString, a narrow string).</summary>
public sealed record BeautyPayment(int Type, int ItemId, long Amount, string IconTag) {
    public static BeautyPayment Read(ByteReader r) => new(r.ReadByte(), r.ReadInt(), (uint) r.ReadInt(), r.ReadString());
}

/// <summary>CBeautyShopItem::Read: item id (i32), sale tag (byte), required level (i16), achieve id (i32), achieve grade (byte), the price.</summary>
public sealed record BeautyShopItem(int ItemId, int SaleTag, int RequireLevel, int AchieveId, int AchieveGrade, BeautyPayment Payment) {
    public static BeautyShopItem Read(ByteReader r) =>
        new(r.ReadInt(), r.ReadByte(), (ushort) r.ReadShort(), r.ReadInt(), r.ReadByte(), BeautyPayment.Read(r));
}

/// <summary>
/// CBeautyShopDataCli::Read: shop id, category, display coupon id, a reserved flag (byte == 1; no reader in the restored
/// client, m_b24), return coupon id, the sale coupon's item tag (m_n2c, UIBeautyShopDialog::IsCouponSaleActive: the coupon
/// sale is on while the player holds an item with this tag), the worn-item check flag (m_b30, FUN_1418cbd30: with no list
/// entry picked, an item counts only when it differs from the worn one), the price, the recolor price.
/// </summary>
public sealed record BeautyShopData(int ShopId, int Category, int DisplayCouponId, bool ReservedFlag, int ReturnCouponId, int SaleCouponTag, bool WornItemCheck,
    BeautyPayment Payment, BeautyPayment RecolorPayment) {
    public static BeautyShopData Read(ByteReader r) =>
        new(r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadByte() == 1, r.ReadInt(), r.ReadInt(), r.ReadBool(), BeautyPayment.Read(r), BeautyPayment.Read(r));
}

/// <summary>
/// Inbound 0x31 (RecvOp Beauty), UIBeautyShopDialog::OnBeautyShopPacket: a mode byte.
/// 0: the shop (kind byte at +0x290, <see cref="BeautyShopData"/>, slot count byte, saved style count byte, i64 sale context, i16 count, the item rows);
/// 8: a result code (i32); 9: item id (i32) and count (i32), a notice when both are positive;
/// 0xb: old and new item id (i32 each); 0xc: coupon id (i32), mailed byte (1: sent by mail); 0x10 and 0x11 carry nothing.
/// Other modes read nothing. Modes 0 and 9 act before the dialog's gate (vfunc_55 returning true ends the handler); modes
/// 8, 0xb, 0xc, 0x10 and 0x11 act only after it, and each of them first resets m_2d8 (the buy-pending flag) to 0.
/// </summary>
public sealed record BeautyShopPacket(int Mode) {
    public int Kind, SlotCount, SavedStyles; public long Context;
    public BeautyShopData Shop; public BeautyShopItem[] Items = System.Array.Empty<BeautyShopItem>();
    public int Result, ItemId, Count, OldItemId, NewItemId, CouponId; public bool Mailed;

    /// <summary>Mode 9 shows its notice (string 0x996 with the item's name) only for an item and a positive count.</summary>
    public bool ShowsItemNotice => Mode == 9 && ItemId != 0 && (uint) Count > 0;

    public static BeautyShopPacket Read(ByteReader r) {
        var p = new BeautyShopPacket(r.ReadByte());
        switch (p.Mode) {
            case 0:
                p.Kind = r.ReadByte();
                p.Shop = BeautyShopData.Read(r);
                p.SlotCount = r.ReadByte(); p.SavedStyles = r.ReadByte();
                p.Context = r.ReadLong();
                int n = (ushort) r.ReadShort();
                p.Items = new BeautyShopItem[n];
                for (int i = 0; i < n; i++) p.Items[i] = BeautyShopItem.Read(r);
                break;
            case 8: p.Result = r.ReadInt(); break;
            case 9: p.ItemId = r.ReadInt(); p.Count = r.ReadInt(); break;
            case 0xb: p.OldItemId = r.ReadInt(); p.NewItemId = r.ReadInt(); break;
            case 0xc: p.CouponId = r.ReadInt(); p.Mailed = r.ReadByte() == 1; break;
        }
        return p;
    }
}

/// <summary>Mode 8 result codes OnBeautyShopPacket tells apart (MessageBoxOk = 7 shows the common string 0x273 box; every other non-zero code shows the error box, common string 0x272).</summary>
public static class BeautyResult {
    public const int Ok = 0, NotEnough = 2, MessageBoxOk = 7, ColorError = 0xc, SlotMax = 0x13, Silent = 0x12, Unavailable = 0x14;
}

/// <summary>Base of the outbound SendOp::Beauty requests: the mode byte then the body.</summary>
public abstract record BeautyRequest(byte Mode) : IOutPacket {
    public Outbound Op => Outbound.Beauty;
    public void Write(ByteWriter w) { w.WriteByte(Mode); WriteBody(w); }
    protected abstract void WriteBody(ByteWriter w);
}

/// <summary>Mode 0 (UITalkCinematic, uitalkcinematic.cpp:1388-1394): npc object id (i32), shop mode (byte). The client sends the
/// packet only when the shop mode is non-zero, so <see cref="Create"/> returns null for 0; the byte is then always written.</summary>
public sealed record BeautyEnterRequest(int NpcId, byte ShopMode) : BeautyRequest(0) {
    public static BeautyEnterRequest Create(int npcId, byte shopMode) => shopMode != 0 ? new BeautyEnterRequest(npcId, shopMode) : null;
    protected override void WriteBody(ByteWriter w) { w.WriteInt(NpcId); w.WriteByte(ShopMode); }
}

/// <summary>Mode 0x17, CSceneStateGame::SendBeauty (scenestategame.cpp:8692 @0x14173c630, mode byte at :8710): item uid (i64),
/// sent from the ItemUse mode 6 request (<see cref="BeautyReq"/>); FUN_140710590 then only hands the packet to the send queue.</summary>
public sealed record BeautyOpenRequest(long Uid) : BeautyRequest(0x17) {
    protected override void WriteBody(ByteWriter w) => w.WriteLong(Uid);
}

/// <summary>Mode 3, Customize::Equipment::SendAdded: shop id, free-try byte, item id, sale context (i64), the item's extra body (<paramref name="WriteItemBody"/>, CItem::WriteItemBody).</summary>
public sealed record BeautyAddedRequest(byte ShopId, byte FreeTry, int ItemId, long Context, Action<ByteWriter> WriteItemBody) : BeautyRequest(3) {
    protected override void WriteBody(ByteWriter w) { w.WriteByte(ShopId); w.WriteByte(FreeTry); w.WriteInt(ItemId); w.WriteLong(Context); WriteItemBody?.Invoke(w); }
}

/// <summary>Mode 4, Customize::Equipment::SendRemoved: item uid (i64).</summary>
public sealed record BeautyRemovedRequest(long Uid) : BeautyRequest(4) {
    protected override void WriteBody(ByteWriter w) => w.WriteLong(Uid);
}

/// <summary>Mode 5, Customize::Equipment::SendModified: shop id, free-try byte, item uid (i64), the item's extra body.</summary>
public sealed record BeautyModifiedRequest(byte ShopId, byte FreeTry, long Uid, Action<ByteWriter> WriteItemBody) : BeautyRequest(5) {
    protected override void WriteBody(ByteWriter w) { w.WriteByte(ShopId); w.WriteByte(FreeTry); w.WriteLong(Uid); WriteItemBody?.Invoke(w); }
}

/// <summary>Mode 6, Customize::Equipment::SendColors: shop id (byte), the two colours (i32 each, FUN_1414239e0 slots 0 and 1), free-try byte.</summary>
public sealed record BeautyColorsRequest(byte ShopId, int Color0, int Color1, byte FreeTry) : BeautyRequest(6) {
    protected override void WriteBody(ByteWriter w) { w.WriteByte(ShopId); w.WriteInt(Color0); w.WriteInt(Color1); w.WriteByte(FreeTry); }
}

/// <summary>Mode 7, UIBeautyShopDialog buy: sale event id (i32), coupon-sale flag (byte), sale context (i64).</summary>
public sealed record BeautyBuyRequest(int SaleEventId, bool CouponSale, long Context) : BeautyRequest(7) {
    protected override void WriteBody(ByteWriter w) { w.WriteInt(SaleEventId); w.WriteByte((byte) (CouponSale ? 1 : 0)); w.WriteLong(Context); }
}

/// <summary>Mode 10 (0xa), UIBeautyListDialog / UIGameMenuDialog: the list kind (i16; the kinds seen are inferred from the callers, not from a table).</summary>
public sealed record BeautyListRequest(short Kind) : BeautyRequest(10) {
    protected override void WriteBody(ByteWriter w) => w.WriteShort(Kind);
}

/// <summary>Mode 0xc, UIBeautyShopDialog (slot change): the slot (byte).</summary>
public sealed record BeautySlotRequest(byte Slot) : BeautyRequest(0xc) {
    protected override void WriteBody(ByteWriter w) => w.WriteByte(Slot);
}

/// <summary>Modes 0x10 (save the worn hair into a slot, SendBeautyMode16) and 0x11 (into a new slot after extending, SendBeautyMode17): item uid (i64).</summary>
public sealed record BeautySaveStyleRequest(long Uid, bool NewSlot) : BeautyRequest((byte) (NewSlot ? 0x11 : 0x10)) {
    protected override void WriteBody(ByteWriter w) => w.WriteLong(Uid);
}

/// <summary>Mode 0x12, SendBeautyMode18 (delete a saved style): item uid (i64), slot index (byte).</summary>
public sealed record BeautyDeleteStyleRequest(long Uid, byte Index) : BeautyRequest(0x12) {
    protected override void WriteBody(ByteWriter w) { w.WriteLong(Uid); w.WriteByte(Index); }
}

/// <summary>Mode 0x13, SendBeautyMode19 (extend the style slots): no body.</summary>
public sealed record BeautyExtendStylesRequest() : BeautyRequest(0x13) {
    protected override void WriteBody(ByteWriter w) { }
}

/// <summary>Mode 0x15, UIBeautyShopStyleDialog (apply a saved style): item uid (i64), the item's slot (byte).</summary>
public sealed record BeautyApplyStyleRequest(long Uid, byte Slot) : BeautyRequest(0x15) {
    protected override void WriteBody(ByteWriter w) { w.WriteLong(Uid); w.WriteByte(Slot); }
}

/// <summary>One dyed item of <see cref="BeautyColoringRequest"/>: ChangeColorItem::FUN_1402000a0 writes shop flag, free-try flag, flag1a (0), value1c (0), value20 (0), uid, item id, then the extra body.</summary>
public sealed record BeautyColorItem(bool ShopFlag, bool FreeTry, long Uid, int ItemId, Action<ByteWriter> WriteExtra) {
    internal void Write(ByteWriter w) {
        w.WriteByte((byte) (ShopFlag ? 1 : 0)); w.WriteByte((byte) (FreeTry ? 1 : 0)); w.WriteByte(0);
        w.WriteInt(0); w.WriteLong(0); w.WriteLong(Uid); w.WriteInt(ItemId);
        WriteExtra?.Invoke(w);
    }
}

/// <summary>
/// Mode 0x16, UIItemColoringDialog (@0x1419b0700): CCItemColoringPacket::ChangeColors (@0x140200950) writes the count (byte), then
/// per item a presence byte (always 1 here) and the <see cref="BeautyColorItem"/>.
/// </summary>
public sealed record BeautyColoringRequest(BeautyColorItem[] Items) : BeautyRequest(0x16) {
    protected override void WriteBody(ByteWriter w) {
        w.WriteByte((byte) Items.Length);
        foreach (var it in Items) { w.WriteByte(1); it.Write(w); }
    }
}
