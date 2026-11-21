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

    public static BindItem Read(ByteReader r, Func<long, EquipItem> findBag, Func<long, EquipItem> findWorn) {
        var b = new BindItem { Uid = r.ReadLong() };
        var it = findBag(b.Uid);
        if (it == null) { it = findWorn(b.Uid); b.Worn = it != null; }
        if (it == null) return b;
        ItemReader.ReadBodyInto(r, it);
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
/// STILL-OPEN: the notifyDlg dialogs' own show (FUN_141924680, UITimeRunEventDialog) and dialog 0x4e28's dirty mark of modes 4 and 7, the beauty dialog 0x4eee open of mode 6.
/// </summary>
public sealed record ItemUseNotice(int Mode, int NoticeStringId, int ItemId, CodiTab Codi = null, BeautyReq Beauty = null) {
    public const int FirstNoticeId = 0x2a2;

    /// <summary>True for the modes that mark the game-menu dialog 0x4e28 dirty (4 and 7).</summary>
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
