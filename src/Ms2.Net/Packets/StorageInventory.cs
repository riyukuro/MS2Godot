// Layouts from the restored client's CStore::Dispatch (client/ms2/store.cpp, @0x140de3480) and its handlers; the store is the
// account bank (global DAT_143ab2d80) that the storage dialog (UI class 0x4e47) shows.
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound 0x22 (RecvOp::StorageInventory): the bank commands, by the type byte. Every handler of the client that carries an
/// id checks it against <c>CStore::Id()</c> first and returns with the body unread when it differs (an assert there); such a
/// packet reads as <see cref="IdMismatch"/>.
/// </summary>
public abstract record StorageMessage {
    public sealed record Clear : StorageMessage;                                                          // 0xb  OnClear   @0x140de3610
    public sealed record Begin(long Id, int Total) : StorageMessage;                                     // 4    OnBegin   @0x140de3640
    public sealed record ItemBatch(long Id, IReadOnlyList<EquipItem> Records) : StorageMessage;                 // 5    OnItems   @0x140de3740
    public sealed record Add(long Id, EquipItem Item) : StorageMessage;                                  // 0    OnAdd     @0x140de3870
    public sealed record Remove(long Id, long Uid) : StorageMessage;                                     // 1    OnRemove  @0x140de3910
    public sealed record CountSet(long Id, long Uid, int Amount) : StorageMessage;                          // 9    OnCount   @0x140de3a50
    public sealed record Currency(long Meso) : StorageMessage;                                           // 3    OnCurrency @0x140de3bc0
    public sealed record Extend(int Extended) : StorageMessage;                                          // 0xd  OnExtend  @0x140de3c60
    public sealed record Close : StorageMessage;                                                          // 7    OnClose   @0x140de3ce0
    public sealed record Swap(long Id, long UidA, short SlotA, long UidB, short SlotB) : StorageMessage; // 2    OnSwap    @0x140de3d40
    public sealed record ListLoad(long Id, IReadOnlyList<EquipItem> Records) : StorageMessage;                 // 8    OnList    @0x140de4030
    public sealed record Reset : StorageMessage;                                                          // 0xe  OnReset   @0x140de4320
    public sealed record Error(int Code) : StorageMessage;                                               // 0x10 OnError   @0x140de4360
    /// <summary>The id (or, for Currency and Extend, the non-zero store) did not match: the client asserts and reads nothing more.</summary>
    public sealed record IdMismatch(int Type, long Id) : StorageMessage;
    public sealed record Other(int Type) : StorageMessage;

    /// <summary>Reads one message. <paramref name="storeId"/> is the book's current id (CStore::Id()).</summary>
    public static StorageMessage Read(ByteReader r, long storeId = 0) => Read(r, storeId, ProtocolProfile.Kms2525);

    /// <summary>The same read for <paramref name="profile"/>: a GMS2 item record has the GMS2 item body (no add slot name).</summary>
    public static StorageMessage Read(ByteReader r, long storeId, ProtocolProfile profile) {
        int type = r.ReadByte();
        StorageMessage m;
        switch (type) {
            case 0xb: m = new Clear(); break;
            case 4: {
                long id = r.ReadLong(); if (id != storeId) return new IdMismatch(type, id);
                m = new Begin(id, (ushort) r.ReadShort()); break;
            }
            case 5: {
                long id = r.ReadLong(); if (id != storeId) return new IdMismatch(type, id);
                int n = (ushort) r.ReadShort();
                var l = ReadItems(r, n, profile, out bool whole);
                m = new ItemBatch(id, l); if (!whole) return m; break;
            }
            case 0: {
                long id = r.ReadLong(); if (id != storeId) return new IdMismatch(type, id);
                var it = ReadItem(r, profile);
                m = new Add(id, it); if (it == null) return m; break;
            }
            case 1: {
                long id = r.ReadLong(); if (id != storeId) return new IdMismatch(type, id);
                m = new Remove(id, r.ReadLong()); break;
            }
            case 9: {
                long id = r.ReadLong(); if (id != storeId) return new IdMismatch(type, id);
                m = new CountSet(id, r.ReadLong(), r.ReadInt()); break;
            }
            case 3: if (storeId != 0) return new IdMismatch(type, storeId); m = new Currency(r.ReadLong()); break;
            case 0xd: if (storeId != 0) return new IdMismatch(type, storeId); m = new Extend(r.ReadInt()); break;
            case 7: m = new Close(); break;
            case 2: {
                long id = r.ReadLong(); if (id != storeId) return new IdMismatch(type, id);
                long ua = r.ReadLong(); short sa = r.ReadShort(); long ub = r.ReadLong(); short sb = r.ReadShort();
                m = new Swap(id, ua, sa, ub, sb); break;
            }
            case 8: {
                long id = r.ReadLong(); if (id != storeId) return new IdMismatch(type, id);
                int n = (ushort) r.ReadShort();
                var l = ReadItems(r, n, profile, out bool whole);
                m = new ListLoad(id, l); if (!whole) return m; break;
            }
            case 0xe: m = new Reset(); break;
            case 0x10: m = new Error(r.ReadInt()); break;
            default: return new Other(type);
        }
        r.ExpectEnd("StorageInventory " + type);
        return m;
    }

    /// <summary>CStore::ReadItem (@0x140de4690): i32 item id, i64 uid, i16 slot, i32 amount (the item's rarity argument), then the item body.
    /// Null for an id the item data does not know: the client's create fails, no body is read and the add is skipped.</summary>
    static EquipItem ReadItem(ByteReader r, ProtocolProfile profile) => ItemReader.ReadInventory(r, profile, false);

    static List<EquipItem> ReadItems(ByteReader r, int n, ProtocolProfile profile, out bool whole) {
        var l = new List<EquipItem>(n);
        whole = true;
        for (int i = 0; i < n; i++) {
            var it = ReadItem(r, profile);
            if (it == null) { whole = false; break; }   // the rest of the records is read as garbage by the client; stop
            l.Add(it);
        }
        return l;
    }
}

/// <summary>What CStore::OnError (@0x140de4360) shows for a code: a string-table id and the way to show it.</summary>
public readonly record struct StorageErrorText(StorageErrorKind Kind, int StringId, long? Number);

public enum StorageErrorKind {
    /// <summary>FUN_140d73bc0(id): the message-box table entry.</summary>
    Box,
    /// <summary>FUN_141275260(id, 1, n): a notice by string id.</summary>
    Notice,
    /// <summary>A message box of string <c>StringId</c> with the capacity (0xe) or the meso limit as its argument.</summary>
    BoxWithNumber,
    /// <summary>Code 0xf: CUIService::FUN_141add1f0 (no string id; the client's own dialog).</summary>
    Service,
}

/// <summary>
/// The account bank (the client's CStore global DAT_143ab2d80). Only <c>Id() == 0</c> is ever live: SetId calls Clear, which zeroes
/// the id (store.cpp unit_140de0f70.cpp:40), so every packet with another id is refused. The capacity starts at 0x24 (36) and
/// <see cref="Extend"/> adds the purchased slots.
/// </summary>
public sealed class StorageBook {
    /// <summary>SetId: the capacity of an unextended bank (store.cpp @0x140de27e0).</summary>
    public const int BaseCapacity = 0x24;
    /// <summary>CItem::SetCountClamped input and the 2,000,000,000 cap of OnError 0x14 / DepositMesos.</summary>
    public const long MesoLimit = 2_000_000_000;

    readonly Dictionary<long, EquipItem> items = new();
    EquipItem[] slots = new EquipItem[BaseCapacity];

    /// <summary>CStore::Id (@0x140de27c0); always 0 once Clear ran.</summary>
    public long Id => 0;
    public int Extended { get; private set; }
    /// <summary>CStore::Capacity (@0x140de1c30).</summary>
    public int Capacity { get; private set; } = BaseCapacity;
    public bool Ready { get; private set; }
    public int Received { get; private set; }
    /// <summary>The meso held in the bank (DAT_143ab28f8, set by OnCurrency).</summary>
    public long Meso { get; private set; }
    public int Count => items.Count;
    public IReadOnlyCollection<EquipItem> All => items.Values;

    /// <summary>The item's max stack (CItemData property slotMax). Without it an Count is stored as sent.</summary>
    public Func<int, uint> MaxStackOf { get; set; }
    /// <summary>CStore::Allowed (@0x140de2490) with m_type 0: every item is storable.</summary>
    public Func<int, bool> Allowed { get; set; }
    /// <summary>The client constant behind CStore::CurrentCapacity (m_44a8) added to 0x24: the most slots the bank can have. Null = unknown.</summary>
    public int? MaxExtended { get; set; }

    // CItemEventDispatcher vfunc_8..12 as the store raises them (store.cpp), numbered by what the slot bag does with them.
    public event Action<EquipItem> ItemAdded;        // ReadItem
    public event Action<EquipItem> ItemRemoved;      // OnRemove
    public event Action<EquipItem> ItemChanged;      // OnCount, OnSwap
    public event Action ListLoaded;                  // OnList
    public event Action BecameReady;                 // OnBegin with no items, OnItems when all arrived
    public event Action<long> MesoChanged;           // OnCurrency (NotifyStatusChanged(10, 0))
    public event Action OpenRequested;               // CUIService::OpenDialog(0x4e47): OnBegin, OnReset
    public event Action CloseReceived;               // OnClose
    public event Action<int> ErrorReceived;          // OnError, the code
    public event Action Expanded;                   // OnExtend applied (the dialog's slot count text)

    public EquipItem Find(long uid) => items.TryGetValue(uid, out var i) ? i : null;
    /// <summary>CStore::Slot (@0x140de1af0): null outside the slots.</summary>
    public EquipItem Slot(int slot) => (uint) slot < (uint) slots.Length ? slots[slot] : null;
    public int SlotCount => slots.Length;

    /// <summary>CStore::Clear (unit_140de0f70.cpp:34): drops everything, keeps the capacity.</summary>
    public void Clear() {
        Extended = 0; Ready = false; Received = 0;
        items.Clear();
        slots = new EquipItem[Capacity];
    }

    /// <summary>CStore::Reset (@0x140de2a00): Clear, not ready, SetId(0, 0) (capacity back to 0x24), Clear.</summary>
    public void Reset() {
        Clear(); Ready = false;
        Capacity = BaseCapacity; Clear();
    }

    /// <summary>CStore::Extend (@0x140de28a0): the slots only grow.</summary>
    public void Extend(int extended) {
        Extended = extended; Capacity = extended + BaseCapacity;
        if (slots.Length < Capacity) Array.Resize(ref slots, Capacity);
    }

    /// <summary>CStore::CurrentCapacity (@0x140de29b0) for the bank: the constant plus 0x24. Null while the constant is unknown.</summary>
    public int? CurrentCapacity => MaxExtended + BaseCapacity;

    /// <summary>CStore::WithinCapacity (@0x140de2970): Capacity &gt;= CurrentCapacity. True while the limit is unknown, so no expand is offered.</summary>
    public bool WithinCapacity => CurrentCapacity is not int max || Capacity >= max;

    /// <summary>CStore::Add (@0x140de11a0): 0 added; 0x11 duplicate uid or occupied slot; 0xd slot out of range or no empty slot. A slot of -1 takes the first empty one.</summary>
    public int TryAdd(EquipItem it) {
        if (items.ContainsKey(it.Uid)) return 0x11;
        int slot = it.InventorySlot;
        if (slot >= slots.Length) return 0xd;
        if (slot != -1) {
            if (slot < 0) return 0xd;   // the client indexes the array with it; out of range is not a defined path
            if (slots[slot] != null) return 0x11;
        } else {
            int empty = EmptySlot();
            if (empty == -1) return 0xd;
            slot = empty; it.InventorySlot = (short) slot;
        }
        items[it.Uid] = it;
        slots[slot] = it;
        return 0;
    }

    void RemoveItem(EquipItem it) {
        items.Remove(it.Uid);
        if ((uint) it.InventorySlot < (uint) slots.Length) slots[it.InventorySlot] = null;
    }

    /// <summary>CStore::EmptySlot (@0x140de20a0): the first empty slot or -1.</summary>
    public int EmptySlot() { for (int i = 0; i < slots.Length; i++) if (slots[i] == null) return i; return -1; }

    /// <summary>CStore::FreeTypeCount (@0x140de1950): the room left in the stacks of an item id.</summary>
    public int FreeTypeCount(int itemId) {
        if (MaxStackOf == null) return 0;
        int free = 0;
        foreach (var o in slots) if (o != null && o.Id == itemId) free += (int) Math.Max(0, (long) MaxStackOf(o.Id) - o.Amount);
        return free;
    }

    /// <summary>CStore::AvailableSlot (@0x140de21a0): 0 when a stackable item fits into the existing stacks, else the first empty slot or -1.</summary>
    public int AvailableSlot(EquipItem it, int count) {
        if (it != null && MaxStackOf != null && MaxStackOf(it.Id) > 1 && FreeTypeCount(it.Id) >= count) return 0;
        return EmptySlot();
    }

    /// <summary>CStore::Allowed (@0x140de2490): with the bank's type 0 every item.</summary>
    public bool IsStorable(int itemId) => Allowed?.Invoke(itemId) ?? true;

    /// <summary>CItem::SetCountClamped (0x140526c70): an unsigned compare with the max stack, so a negative count becomes the max.</summary>
    int Clamp(int itemId, int count) {
        if (MaxStackOf?.Invoke(itemId) is not uint max) return count;
        return (uint) count < max ? count : (int) max;
    }

    /// <summary>Applies one message as the client's handlers do.</summary>
    public void Apply(StorageMessage m) {
        switch (m) {
            case StorageMessage.Clear: Clear(); break;
            case StorageMessage.Begin x:
                if (x.Id == 0) OpenRequested?.Invoke();
                Received = x.Total;
                if (x.Total == 0) { Ready = true; BecameReady?.Invoke(); }
                break;
            case StorageMessage.ItemBatch x:
                foreach (var it in x.Records) AddRead(it);
                if (Count == Received) { Ready = true; Received = 0; BecameReady?.Invoke(); }
                break;
            case StorageMessage.Add x: AddRead(x.Item); Ready = true; break;
            case StorageMessage.Remove x: {
                var it = Find(x.Uid); if (it == null) break;
                RemoveItem(it); ItemRemoved?.Invoke(it); break;
            }
            case StorageMessage.CountSet x: {
                var it = Find(x.Uid); if (it == null) break;
                it.Amount = Clamp(it.Id, x.Amount); ItemChanged?.Invoke(it); break;
            }
            case StorageMessage.Currency x: Meso = x.Meso; MesoChanged?.Invoke(x.Meso); break;
            case StorageMessage.Extend x: Extend(x.Extended); Expanded?.Invoke(); break;
            case StorageMessage.Close: CloseReceived?.Invoke(); break;
            case StorageMessage.Swap x: {
                var moved = new EquipItem[2];
                (long uid, short slot)[] ends = { (x.UidA, x.SlotA), (x.UidB, x.SlotB) };
                for (int i = 0; i < 2; i++) {
                    if (ends[i].uid == 0) continue;
                    moved[i] = Find(ends[i].uid); if (moved[i] == null) continue;
                    RemoveItem(moved[i]); moved[i].InventorySlot = ends[i].slot;
                }
                foreach (var it in moved) if (it != null) TryAdd(it);
                foreach (var it in moved) if (it != null) ItemChanged?.Invoke(it);
                break;
            }
            case StorageMessage.ListLoad x: foreach (var it in x.Records) TryAdd(it); ListLoaded?.Invoke(); break;
            case StorageMessage.Reset: Reset(); OpenRequested?.Invoke(); break;
            case StorageMessage.Error x: ErrorReceived?.Invoke(x.Code); break;
        }
    }

    /// <summary>CStore::ReadItem's tail: Add (its result is not looked at) and the added event.</summary>
    void AddRead(EquipItem it) {
        if (it == null) return;
        TryAdd(it);
        ItemAdded?.Invoke(it);
    }

    /// <summary>The text CStore::OnError (@0x140de4360) shows for a code; <paramref name="capacity"/> is CurrentCapacity (code 0xe).</summary>
    public static StorageErrorText ErrorText(int code, int capacity = 0) => code switch {
        10 => new(StorageErrorKind.Box, 0xeb, null),
        0xc => new(StorageErrorKind.Notice, 0xf1, null),   // FUN_141275260(0xf1, 1, 0): the only notice whose third argument is 0 (its meaning is not in the restored source)
        0xd => new(StorageErrorKind.Notice, 0xf2, null),
        0xe => new(StorageErrorKind.BoxWithNumber, 0x2b0, capacity),
        0xf => new(StorageErrorKind.Service, 0, null),
        0x10 => new(StorageErrorKind.Notice, 0xfb, null),
        0x11 => new(StorageErrorKind.Notice, 0xfc, null),
        0x12 => new(StorageErrorKind.Notice, 0x2b1, null),
        0x13 => new(StorageErrorKind.Notice, 0x2b2, null),
        0x14 => new(StorageErrorKind.Notice, 0x2b3, (int) MesoLimit),
        0x15 => new(StorageErrorKind.Notice, 0xa5e, null),
        0x16 => new(StorageErrorKind.Notice, 0xff, null),
        _ => new(StorageErrorKind.Notice, 0x2af, code),
    };
}
