// Layouts from the restored client's readers (CSceneStateGame::OnBindItem @0x14173b420, OnItemUse @0x141739910), not the server.
using Maple2.PacketLib.Tools;

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
/// OnActiveCodiTab, mode 6 SendBeauty, mode 7 only marks dialog 0x4e28 dirty.
/// STILL-OPEN: modes 4 (the item's function xml notify attributes), 5 and 6 read nothing more here (the packet ends with the mode).
/// </summary>
public sealed record ItemUseNotice(int Mode, int NoticeStringId, int ItemId) {
    public const int FirstNoticeId = 0x2a2;

    /// <summary>True for the modes that mark the game-menu dialog 0x4e28 dirty (4 and 7).</summary>
    public bool MarksDialogDirty => Mode is 4 or 7;

    public static ItemUseNotice Read(ByteReader r) {
        int mode = r.ReadByte();
        return mode switch {
            >= 0 and <= 3 => new ItemUseNotice(mode, FirstNoticeId + mode, 0),
            4 => new ItemUseNotice(mode, 0, r.ReadInt()),
            _ => new ItemUseNotice(mode, 0, 0),
        };
    }
}
