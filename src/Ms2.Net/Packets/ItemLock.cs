using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// The item lock dialog's packets: CItemLockManager (client unnamed/unit_1406125d0_itemlockmanager.cpp, UI id 0x4f08).

/// <summary>Inbound 0xDB, CItemLockManager::OnItemLockPacket (@0x140612850), by the mode byte.</summary>
public abstract record ItemLockMessage {
    /// <summary>Mode 1: an item took a bag slot (0..0x11).</summary>
    public sealed record Locked(long Uid, int Slot) : ItemLockMessage;
    /// <summary>Mode 2: an item left the bag.</summary>
    public sealed record Removed(long Uid) : ItemLockMessage;
    /// <summary>Mode 3: the items' bodies re-read (their lock fields changed) and each leaves the bag. Item is null for an unknown uid.</summary>
    public sealed record Applied(IReadOnlyList<(long Uid, EquipItem Item)> Items) : ItemLockMessage;
    /// <summary>Mode 4: the answer code (1 and 3 have their own notices).</summary>
    public sealed record Result(int Code) : ItemLockMessage;
    public sealed record Other(int Mode) : ItemLockMessage;

    /// <summary>Reads one message. <paramref name="find"/> gives the inventory item of an uid: mode 3 needs it for the body layout.</summary>
    public static ItemLockMessage Read(ByteReader r, Func<long, EquipItem> find) {
        int mode = r.ReadByte();
        switch (mode) {
            case 1: { long uid = r.ReadLong(); return new Locked(uid, (ushort) r.ReadShort()); }
            case 2: return new Removed(r.ReadLong());
            case 3: {
                int n = r.ReadByte();
                var l = new List<(long, EquipItem)>(n);
                for (int i = 0; i < n; i++) {
                    long uid = r.ReadLong();
                    var it = find(uid);
                    if (it == null) { l.Add((uid, null)); break; }   // the client reads no body for an unknown uid; the rest is not parsed
                    var copy = new EquipItem { Id = it.Id, Uid = it.Uid, Rarity = it.Rarity, Slot = it.Slot, InventorySlot = it.InventorySlot };
                    ItemReader.ReadBodyInto(r, copy);
                    l.Add((uid, copy));
                }
                return new Applied(l);
            }
            case 4: return new Result(r.ReadInt());
            default: return new Other(mode);
        }
    }
}

/// <summary>Mode 0 (SendRequestItemLockMode0, @0x1406126f0): the dialog opened, closed or the bag was dropped.</summary>
public sealed record ItemLockResetRequest : IOutPacket {
    public Outbound Op => Outbound.RequestItemLock;
    public void Write(ByteWriter w) => w.WriteByte(0);
}

/// <summary>Mode 1 (FUN_140612e90, @0x140612e90): put an item in the bag; Mode is the dialog's tab (0 lock, 1 unlock).</summary>
public sealed record ItemLockSelectRequest(byte Mode, long Uid) : IOutPacket {
    public Outbound Op => Outbound.RequestItemLock;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteByte(Mode); w.WriteLong(Uid); }
}

/// <summary>Mode 2 (FUN_1406130e0, @0x1406130e0): take an item out of the bag.</summary>
public sealed record ItemLockRemoveRequest(long Uid) : IOutPacket {
    public Outbound Op => Outbound.RequestItemLock;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteLong(Uid); }
}

/// <summary>Mode 3 (SendRequestItemLockMode3, @0x140612e10): confirm lock or unlock of the bag.</summary>
public sealed record ItemLockConfirmRequest(byte Mode) : IOutPacket {
    public Outbound Op => Outbound.RequestItemLock;
    public void Write(ByteWriter w) { w.WriteByte(3); w.WriteByte(Mode); }
}

/// <summary>
/// CItemLockManager's state and the client's checks before each send. Only OnItemLockPacket with the dialog open
/// (UIItemLockDialog 0x4f08) changes the bag.
/// STILL-OPEN: the lock bag's slot refresh is the host's (<see cref="Changed"/>); the notices are ids for the host to show.
/// </summary>
public sealed class ItemLockBook {
    public const int Slots = 0x12;
    /// <summary>The notice ids FUN_141275260 shows (a system message); <see cref="SelectedNotice"/> is the string key
    /// s_itemlock_selected_item shown through ShowSystemNotice.</summary>
    public const int NoticeLocked = 0xc45, NoticeCannotLock = 0xc46, NoticeNotLocked = 0xc47, NoticeResult3 = 0x43;
    public const int SelectedNotice = -1;

    readonly long[] locked = new long[Slots];
    readonly HashSet<long> pending = new();

    /// <summary>The dialog's tab: 0 lock, 1 unlock (m_mode).</summary>
    public byte Mode { get; set; }
    /// <summary>The dialog exists (Unk_LockDialog(0x4f08)): packets are ignored without it.</summary>
    public bool DialogOpen { get; set; }
    public Func<long, EquipItem> Find { get; set; } = _ => null;
    /// <summary>CItem::CanBeItemLocked, which needs the item data (type, PC bang, vip, wedding).</summary>
    public Func<EquipItem, bool> CanBeLocked { get; set; } = _ => false;
    public Action<IOutPacket> Send { get; set; }
    /// <summary>A notice of the lock path: the id (see the constants) and, for <see cref="SelectedNotice"/>, nothing else; for
    /// a result code that is neither 1 nor 3 the id is 0xc45 with the code as the argument.</summary>
    public event Action<int, int> Notice;
    /// <summary>The bag changed (BindItemBag).</summary>
    public event Action BagChanged;
    /// <summary>An inventory slot needs a redraw (RefreshItemSlot) for this uid.</summary>
    public event Action<long> ItemRefresh;
    /// <summary>A mode 3 body replaced an inventory item: the host stores it.</summary>
    public Action<EquipItem> StoreItem { get; set; }

    public long this[int slot] => slot >= 0 && slot < Slots ? locked[slot] : 0;
    public bool HasLockedItem { get { foreach (var u in locked) if (u != 0) return true; return false; } }
    public bool Contains(long uid) => uid != 0 && Array.IndexOf(locked, uid) >= 0;

    public void SendReset() {            // SendRequestItemLockMode0
        pending.Clear();
        Array.Clear(locked);
        Send?.Invoke(new ItemLockResetRequest());
    }

    /// <summary>FUN_140612e90: the item goes to the bag. Refused (nothing sent) when already in it, or the tab's test fails.</summary>
    public bool Select(EquipItem item) {
        if (item == null) return false;
        if (Contains(item.Uid)) { Notice?.Invoke(SelectedNotice, 0); return false; }
        if (Mode == 0) {
            if (!CanBeLocked(item)) { Notice?.Invoke(NoticeCannotLock, 0); return false; }
        } else if (Mode == 1) {
            if (item.LockState != 1) { Notice?.Invoke(NoticeNotLocked, 0); return false; }   // CItem::IsItemLocked
        }
        Send?.Invoke(new ItemLockSelectRequest(Mode, item.Uid));
        return true;
    }

    /// <summary>FUN_1406130e0: always sent.</summary>
    public void Remove(long uid) => Send?.Invoke(new ItemLockRemoveRequest(uid));

    public void Confirm() => Send?.Invoke(new ItemLockConfirmRequest(Mode));

    /// <summary>CanAddItem (the item slot display): the tab's test and the uid not in the bag.</summary>
    public bool CanAdd(EquipItem item) {
        if (item == null) return false;
        if (Mode == 0 ? !CanBeLocked(item) : Mode == 1 && item.LockState != 1) return false;
        return !Contains(item.Uid);
    }

    /// <summary>RequestUnlockMissingItems (the dialog's per frame when not visible): a mode 2 for each bag uid whose item is gone, once.</summary>
    public void RequestUnlockMissing() {
        foreach (var uid in locked) {
            if (uid == 0 || Find(uid) != null || pending.Contains(uid)) continue;
            Send?.Invoke(new ItemLockRemoveRequest(uid));
            pending.Add(uid);
        }
    }

    void RemoveLocked(long uid) {
        int i = Array.IndexOf(locked, uid);
        if (i >= 0) locked[i] = 0;
        pending.Remove(uid);
    }

    public void Apply(ItemLockMessage m) {
        if (!DialogOpen) return;
        switch (m) {
            case ItemLockMessage.Locked x:
                if (x.Slot < 0 || x.Slot >= Slots) return;
                if (Find(x.Uid) != null) { locked[x.Slot] = x.Uid; BagChanged?.Invoke(); }
                ItemRefresh?.Invoke(x.Uid);
                break;
            case ItemLockMessage.Removed x:
                RemoveLocked(x.Uid); BagChanged?.Invoke(); ItemRefresh?.Invoke(x.Uid);
                break;
            case ItemLockMessage.Applied x:
                foreach (var (uid, item) in x.Items) {
                    if (item != null) StoreItem?.Invoke(item);
                    RemoveLocked(uid);
                    if (item != null) ItemRefresh?.Invoke(uid);
                }
                BagChanged?.Invoke();
                break;
            case ItemLockMessage.Result x:
                if (x.Code == 1) Notice?.Invoke(NoticeLocked, 0);
                else if (x.Code == 3) Notice?.Invoke(NoticeResult3, 0);
                else Notice?.Invoke(NoticeLocked, x.Code);
                BagChanged?.Invoke(); // the result refreshes the bag and the lock buttons
                break;
        }
    }
}
