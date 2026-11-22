using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// The bank's sends: SendOp::RequestItemStorage 0x18 by mode (u8 first), as client/ms2/store.cpp builds them.

/// <summary>Mode 0, FUN_140de2dc0 (@0x140de2dc0, store.cpp:588): move count of the inventory item into a bank slot.</summary>
public sealed record StorageDepositRequest(long StoreId, long Uid, short Slot, int Count) : IOutPacket {
    public Outbound Op => Outbound.RequestItemStorage;
    public void Write(ByteWriter w) { w.WriteByte(0); w.WriteLong(StoreId); w.WriteLong(Uid); w.WriteShort(Slot); w.WriteInt(Count); }
}

/// <summary>Mode 1, FUN_140de31f0 (@0x140de31f0, store.cpp:625): move count of the bank item into an inventory slot.</summary>
public sealed record StorageWithdrawRequest(long StoreId, long Uid, short Slot, int Count) : IOutPacket {
    public Outbound Op => Outbound.RequestItemStorage;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteLong(StoreId); w.WriteLong(Uid); w.WriteShort(Slot); w.WriteInt(Count); }
}

/// <summary>Mode 2, StoreSlot::vfunc_18 (@0x141809460, storeslot.cpp:314): a bank item dropped on another bank slot.</summary>
public sealed record StorageMoveRequest(long StoreId, long Uid, short ToSlot) : IOutPacket {
    public Outbound Op => Outbound.RequestItemStorage;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteLong(StoreId); w.WriteLong(Uid); w.WriteShort(ToSlot); }
}

/// <summary>Mode 3, CStore::RequestAction (@0x140de2c40): meso into (1) or out of (0) the bank.</summary>
public sealed record StorageMesoRequest(long StoreId, byte Action, long Amount) : IOutPacket {
    public Outbound Op => Outbound.RequestItemStorage;
    public void Write(ByteWriter w) { w.WriteByte(3); w.WriteLong(StoreId); w.WriteByte(Action); w.WriteLong(Amount); }
}

/// <summary>Mode 6, CStore::RequestExtend (@0x140de2ce0): buy more slots; sent only while the store id is 0.</summary>
public sealed record StorageExtendRequest(long StoreId) : IOutPacket {
    public Outbound Op => Outbound.RequestItemStorage;
    public void Write(ByteWriter w) { w.WriteByte(6); w.WriteLong(StoreId); }
}

/// <summary>Mode 8, CStore::RequestClose (@0x140de2bd0).</summary>
public sealed record StorageCloseRequest(long StoreId) : IOutPacket {
    public Outbound Op => Outbound.RequestItemStorage;
    public void Write(ByteWriter w) { w.WriteByte(8); w.WriteLong(StoreId); }
}

/// <summary>Mode 0xc, CStore::RequestLoad (@0x140de2b50): the LoadRequest is the store id.</summary>
public sealed record StorageLoadRequest(long StoreId) : IOutPacket {
    public Outbound Op => Outbound.RequestItemStorage;
    public void Write(ByteWriter w) { w.WriteByte(0xc); w.WriteLong(StoreId); }
}

/// <summary>Mode 0xf, CStore::RequestSort (@0x140de2d60): no body.</summary>
public sealed record StorageSortRequest : IOutPacket {
    public Outbound Op => Outbound.RequestItemStorage;
    public void Write(ByteWriter w) => w.WriteByte(0xf);
}

/// <summary>
/// The client side of the bank's sends: each method runs the client's checks in its order and sends only when they pass.
/// Nothing in the book or the inventory changes here; the server's StorageInventory packets do that.
/// STILL-OPEN: Unk_Inventory::FUN_140a661e0 is a constant 1 in the client (no check); CItem::FUN_14052a580 (the item's binding
/// against the character, <see cref="CanMove"/>) is unread and defaults to refused, so a withdraw cannot be sent unchecked; the transfer-binding boxes of
/// StoreSlot::vfunc_26 and the count-input boxes (ShowUseStoreCountInput / ShowSwapStoreCountInput) are not ported, so an item
/// they would ask about is not sent.
/// </summary>
public sealed class StorageSender {
    readonly StorageBook book;
    public StorageSender(StorageBook book) { this.book = book; }

    /// <summary>The connection.</summary>
    public Action<IOutPacket> Send;
    /// <summary>The notice or box the client shows (FUN_141275260 / Unk_InvNotice / FUN_140d73bc0).</summary>
    public Action<StorageErrorText> Notify;
    /// <summary>DAT_143ab2898: the meso in the inventory.</summary>
    public Func<long> InventoryMeso = () => 0;
    /// <summary>Unk_Inventory::FindItemByUid.</summary>
    public Func<long, EquipItem> FindInventoryItem = _ => null;
    /// <summary>The local player exists and is not dead (FUN_141b11490's second half).</summary>
    public Func<bool> PlayerAlive = () => true;
    /// <summary>CItem::FUN_14052a580(DAT_143ab2828), see the class note.</summary>
    public Func<EquipItem, bool> CanMove = _ => false;   // unread check: a withdraw is refused until a real check is wired
    /// <summary>CItemData::IsScoreItem and the instrument manager's playing score uid (FUN_140c8dab0).</summary>
    public Func<int, bool> IsScoreItem = _ => false;
    public Func<long> PlayingScoreUid = () => 0;
    /// <summary>CItem::IsPetItem and CPetManager::GetSummonUid.</summary>
    public Func<int, bool> IsPetItem = _ => false;
    public Func<long> SummonUid = () => 0;
    /// <summary>The s_item_err_binditem_by_playing_score / _by_summon_pet system notices.</summary>
    public Action<bool> BindNotice;
    /// <summary>The configured limits of one meso move (the StoreDepositMax / StoreWithdrawMax constants).</summary>
    public long DepositMax = long.MaxValue, WithdrawMax = long.MaxValue;
    /// <summary>ExpandSlots' platform test (vfunc_40(StoreExpandPrice1Row) == 1); false asks the service dialog instead.</summary>
    public Func<bool> PlatformCanBuy = () => true;
    /// <summary>CUIService::FUN_141add1f0: the client's own purchase dialog (code 0xf of the error packet).</summary>
    public event Action ServiceRequested;

    /// <summary>FUN_140de2dc0 (mode 0). Returns whether it sent.</summary>
    public bool Deposit(long uid, int slot, int count) {
        var it = FindInventoryItem(uid);
        if (it == null) return false;
        if (IsScoreItem(it.Id) && PlayingScoreUid() == it.Uid) { BindNotice?.Invoke(true); return false; }
        if (IsPetItem(it.Id) && SummonUid() == uid) { BindNotice?.Invoke(false); return false; }
        Send?.Invoke(new StorageDepositRequest(book.Id, uid, (short) slot, count));
        return true;
    }

    /// <summary>CItem::FUN_14052a580 (item.cpp:1674): an item with no binding (FUN_14052d1c0 == 0) passes, a bound one only for the
    /// character it is bound to (FUN_140634790).</summary>
    public static bool BindingAllows(EquipItem it, long selfId) => it.BindingId == 0 || it.BindingId == selfId;

    /// <summary>FUN_140de31f0 (mode 1): an item the bank holds is checked for the character binding first; an unknown uid is sent.</summary>
    public bool Withdraw(long uid, int slot, int count) {
        var it = book.Find(uid);
        if (it != null && !CanMove(it)) return false;
        Send?.Invoke(new StorageWithdrawRequest(book.Id, uid, (short) slot, count));
        return true;
    }

    /// <summary>StoreSlot::vfunc_18 with a bank slot as the dragged one: onto itself nothing, else mode 2.</summary>
    public bool MoveSlot(int fromSlot, int toSlot) {
        var it = book.Slot(fromSlot);
        if (it == null || fromSlot == toSlot) return false;
        Send?.Invoke(new StorageMoveRequest(book.Id, it.Uid, (short) toSlot));
        return true;
    }

    /// <summary>FUN_141b11490: the meso and expand buttons work only at store id 0 with the player alive.</summary>
    public bool CanStartMeso() => book.Id == 0 && PlayerAlive();

    /// <summary>UIStoreDialog::DepositMesos (@0x141b11190): true closes the input box (sent), false keeps it.</summary>
    public bool DepositMesos(long amount) {
        long inv = InventoryMeso();
        if (inv < amount) { Notify?.Invoke(new StorageErrorText(StorageErrorKind.Notice, 0x2b6, null)); return false; }
        if (amount <= 0 || amount > DepositMax) {
            if (amount == 0) Notify?.Invoke(new StorageErrorText(StorageErrorKind.Notice, 0x2b2, null));
            else Notify?.Invoke(new StorageErrorText(StorageErrorKind.Notice, 0xee, Math.Min(DepositMax, inv)));
            return false;
        }
        if (book.Meso + amount > StorageBook.MesoLimit) { Notify?.Invoke(new StorageErrorText(StorageErrorKind.Notice, 0x2b3, StorageBook.MesoLimit)); return false; }
        Send?.Invoke(new StorageMesoRequest(book.Id, 1, amount));
        return true;
    }

    /// <summary>UIStoreDialog::WithdrawMesos (@0x141b11350).</summary>
    public bool WithdrawMesos(long amount) {
        if (book.Meso < amount) { Notify?.Invoke(new StorageErrorText(StorageErrorKind.Notice, 0x2b5, null)); return false; }
        if (amount <= 0 || amount > WithdrawMax) {
            if (amount == 0) Notify?.Invoke(new StorageErrorText(StorageErrorKind.Notice, 0x2b4, null));
            else Notify?.Invoke(new StorageErrorText(StorageErrorKind.Notice, 0xee, Math.Min(WithdrawMax, book.Meso)));
            return false;
        }
        Send?.Invoke(new StorageMesoRequest(book.Id, 0, amount));
        return true;
    }

    /// <summary>UIStoreDialog::ExpandSlots (@0x141b11520): the platform buys it (mode 6) or the service dialog opens.</summary>
    public void ExpandSlots() {
        if (PlatformCanBuy()) RequestExtend(); else ServiceRequested?.Invoke();
    }

    /// <summary>CStore::RequestExtend: only at store id 0.</summary>
    public void RequestExtend() { if (book.Id == 0) Send?.Invoke(new StorageExtendRequest(book.Id)); }
    public void RequestLoad() => Send?.Invoke(new StorageLoadRequest(book.Id));
    public void RequestClose() => Send?.Invoke(new StorageCloseRequest(book.Id));
    public void RequestSort() => Send?.Invoke(new StorageSortRequest());
}
