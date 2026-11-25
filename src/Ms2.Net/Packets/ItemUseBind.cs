// Layouts from the restored client's readers (CSceneStateGame::OnBindItem @0x14173b420, OnItemUse @0x141739910), not the server.
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound 0x103, CSceneStateGame::OnBindItem (scenestategame.cpp:8542): the uid (i64), then the item found by it in the bag
/// (g_143ab2a90.FindItemByUid) or else among the local player's worn equips (PC::GetEquipByUid) reads its record again
/// (CItem::ReadFromStream: <see cref="ItemReader.ReadBodyInto"/>). An unknown uid, or no local player, reads nothing.
/// </summary>
public sealed class BindItem {
    public long Uid; public bool Found, Worn; public EquipItem Item;

    public static BindItem Read(ByteReader r, Func<long, EquipItem> findBag, Func<long, EquipItem> findWorn) => Read(r, findBag, findWorn, ProtocolProfile.Kms2525);

    /// <summary>The same read for <paramref name="profile"/>: the found item's body in the GMS2 layout on GMS2.</summary>
    public static BindItem Read(ByteReader r, Func<long, EquipItem> findBag, Func<long, EquipItem> findWorn, ProtocolProfile profile) {
        var b = new BindItem { Uid = r.ReadLong() };
        var it = findBag(b.Uid);
        if (it == null) { it = findWorn(b.Uid); b.Worn = it != null; }
        if (it == null) return b;
        ItemReader.ReadBodyInto(r, it, profile);
        b.Found = true; b.Item = it;
        return b;
    }
}

/// <summary>
/// Inbound 0xAB, CSceneStateGame::OnItemUse (scenestategame.cpp:8603): a mode byte. Modes 0-3 show the notice box
/// of the common string 0x2a2..0x2a5 (FUN_140d73bc0(id, 0, 0, "")); mode 4 reads an item id and shows the item's notifyDlg dialog or
/// notifyMsg box (string 0x5f6 with the item's name, a line break, the message) and marks dialog 0x4e28 dirty; mode 5 is
/// OnActiveCodiTab, mode 6 SendBeauty (<see cref="BeautyReq"/>), mode 7 only marks dialog 0x4e28 dirty.
/// Mode 5 reads <see cref="CodiTab"/> (FUN_1407a8250 @0x1407a8250: actor int, start, end, expire i64, silent byte).
/// STILL-OPEN: the notifyDlg dialogs' own show (FUN_141924680, UITimeRunEventDialog), the beauty dialog 0x4eee open of mode 6.
/// </summary>
public sealed record ItemUseNotice(int Mode, int NoticeStringId, int ItemId, CodiTab Codi = null, BeautyReq Beauty = null) {
    public const int FirstNoticeId = 0x2a2;

    /// <summary>True for the modes that mark the game-menu dialog 0x4e28 dirty (4 and 7, scenestategame.cpp:8642-8644, :8654-8656).</summary>
    public bool MarksDialogDirty => Mode is 4 or 7;

    public static ItemUseNotice Read(ByteReader r) {
        int mode = r.ReadByte();
        return mode switch {
            >= 0 and <= 3 => new ItemUseNotice(mode, FirstNoticeId + mode, 0),
            4 => new ItemUseNotice(mode, 0, r.ReadInt()),
            5 => new ItemUseNotice(mode, 0, 0, CodiTab.Read(r)),   // OnActiveCodiTab: FUN_1407a8250 (0x1407a8250) reads int, i64, i64, i64, byte
            6 => new ItemUseNotice(mode, 0, 0, null, BeautyReq.Read(r)),
            _ => new ItemUseNotice(mode, 0, 0),
        };
    }
}

/// <summary>Unk_SSGBeautyReq as FUN_1407a8340 @0x1407a8340 reads it (scenestategame.cpp:1121): m_nType (int), m_nUid (i64).</summary>
public sealed record BeautyReq(int Type, long Uid) {
    public static BeautyReq Read(ByteReader r) => new(r.ReadInt(), r.ReadLong());
}

/// <summary>
/// Inbound ItemScript, CSceneStateGame::OnItemScript @0x14173e510 (scenestategame.cpp:6829): only mode 4 is read, an i32 item id
/// the reward notify (0x4e5d) shows as a cash coupon (UIRewardNotify::FUN_141ac1d10); any other mode is ignored here. The server's
/// other modes (the lulu, pet and gacha boxes, the treasure map) are not read by this handler.
/// </summary>
public static class ItemScriptCoupon {
    public const int Mode = 4;

    /// <summary>The item id of a mode 4 packet; false for any other mode.</summary>
    public static bool TryRead(ByteReader r, out int itemId) {
        itemId = 0;
        if (r.Available < 1 || r.ReadByte() != Mode) return false;
        itemId = r.ReadInt();
        return true;
    }
}

/// <summary>One item of an <see cref="ItemScriptBoxResult"/>: id, count, rank (ItemEntity::SetRank) and whether it is special.</summary>
public sealed record ItemScriptBoxItem(int ItemId, int Count, int Rank, bool Special);

/// <summary>
/// Inbound ItemScript (0xF5 on KMS) as the open box dialogs read it in their packet hook (vfunc_79, the dialog open and loaded):
/// a mode byte; mode 5 (UIItemGachaBoxDialog @0x140c403e0, uiitemgachaboxdialog.cpp:186) and mode 0 (UIItemLulluBoxDialog
/// @0x140e9b670, uiitemlulluboxdialog.cpp:206) read an int count, then per item id, count, rank (ints) and a special bool.
/// Each dialog ignores the other modes (it does not read past the mode byte).
/// </summary>
public sealed record ItemScriptBoxResult(int Mode, IReadOnlyList<ItemScriptBoxItem> Items) {
    public const int LulluMode = 0, GachaMode = 5;

    /// <summary>The result of a mode 0 or 5 packet; null for any other mode.</summary>
    public static ItemScriptBoxResult TryRead(ByteReader r) {
        if (r.Available < 1) return null;
        int mode = r.ReadByte();
        if (mode is not (LulluMode or GachaMode)) return null;
        int n = r.ReadInt();
        var items = new List<ItemScriptBoxItem>(Math.Max(0, Math.Min(n, 64)));
        for (int i = 0; i < n; i++) items.Add(new ItemScriptBoxItem(r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadBool()));
        return new ItemScriptBoxResult(mode, items);
    }
}

/// <summary>
/// Outbound TreasureMap 0x9F sub 2 (UIItemTreasureMapDialog::SendTreasureMapMode2 @0x140c55ba0, uiitemtreasuremapdialog.cpp:233,
/// and OnSubmitClick's back from a result, :258): u8 2, i64 the map's uid, bool the free appraise. The server answers with the
/// cost (<see cref="ItemScriptTreasureMap"/> sub 2).
/// </summary>
public sealed record TreasureMapCostRequest(long Uid, bool Free) : IOutPacket {
    public Outbound Op => Outbound.TreasureMap;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteLong(Uid); w.WriteBool(Free); }
}

/// <summary>
/// Inbound ItemScript (0xF5 on KMS) as UIItemTreasureMapDialog::vfunc_79 (@0x140c55960, uiitemtreasuremapdialog.cpp:174) reads it,
/// the dialog open and loaded: a sub byte; sub 2 an i64 uid and an i64 cost (OnAppraiseCost); sub 3 a bool, true followed by
/// an i32 item id and an i32 rank (ShowAppraisedItem), false ends the appraise (back to the select page). Other subs are
/// ignored (nothing read past the byte).
/// </summary>
public abstract record ItemScriptTreasureMap {
    public const int CostSub = 2, ResultSub = 3;

    /// <summary>Sub 2: the appraise cost of the map; a negative cost cannot be appraised.</summary>
    public sealed record Cost(long Uid, long Amount) : ItemScriptTreasureMap;
    /// <summary>Sub 3: the appraised item (Ok), or the end of the appraise (not Ok, no item).</summary>
    public sealed record Result(bool Ok, int ItemId, int Rank) : ItemScriptTreasureMap;

    /// <summary>The message of a sub 2 or 3 packet; null for any other sub.</summary>
    public static ItemScriptTreasureMap TryRead(ByteReader r) {
        if (r.Available < 1) return null;
        int sub = r.ReadByte();
        if (sub == CostSub) return new Cost(r.ReadLong(), r.ReadLong());
        if (sub != ResultSub) return null;
        bool ok = r.ReadBool();
        return ok ? new Result(true, r.ReadInt(), r.ReadInt()) : new Result(false, 0, 0);
    }
}
