// Layouts from the restored client's CSceneStateGame::OnItemInventory (scenestategame.cpp:2806, 0x141713fb0) and FUN_141715860 (mode 8),
// checked against the server fork's ItemInventoryPacket writers (Maple2.Server.Game/Packets/ItemInventoryPacket.cs).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>Inbound 0x21: the item inventory (g_143ab2a90) commands, by the mode byte.</summary>
public abstract record ItemMessage {
    public sealed record Add(EquipItem Item) : ItemMessage;                                   // mode 0
    public sealed record Remove(long Uid) : ItemMessage;                                      // 1
    public sealed record Amount(long Uid, int Count) : ItemMessage;                           // 2
    public sealed record Move(long UidA, short SlotA, long UidB, short SlotB) : ItemMessage;  // 3: an uid of 0 is an empty slot
    public sealed record Load(IReadOnlyList<EquipItem> Items) : ItemMessage;                  // 6
    public sealed record NotifyNew(long Uid, int Arg, string Name) : ItemMessage;             // 7
    public sealed record LoadTab(int Tab, IReadOnlyList<EquipItem> Items) : ItemMessage;      // 8
    public sealed record ExpandComplete : ItemMessage;                                        // 10
    public sealed record Reset(int Tab) : ItemMessage;                                        // 11 (0xb): RemoveItemsInCategory
    public sealed record ExpandCount(int Tab, int Count) : ItemMessage;                       // 12 (0xc): SetExtraSlots
    public sealed record Error(int Code, int ItemId, int Value) : ItemMessage;                // 13 (0xd); ItemId/Value only for codes 0x28 and 0x29
    public sealed record Update(long Uid, EquipItem Item) : ItemMessage;                      // 14 (0xe): the item body re-read; Item is null when the uid is unknown
    public sealed record Other(int Mode) : ItemMessage;

    /// <summary>Reads one message. <paramref name="find"/> tells the item of an uid (mode 0xe needs its id for the body layout).</summary>
    public static ItemMessage Read(ByteReader r, Func<long, EquipItem> find) => Read(r, find, ProtocolProfile.Kms2525);

    /// <summary>
    /// The GMS2 command numbers (upstream ItemInventoryPacket.Command) are mapped to the KMS2 mode each one stands for:
    /// 7 Load (6), 8 NotifyNew (7), 10 LoadTab (8), 12 ExpandComplete (10), 13 Reset (11), 14 ExpandCount (12), 15 Error (13),
    /// 16 UpdateItem (14); 0 to 3 are the same. Item bodies read through the GMS2 item layout, and Add carries a unicode
    /// equip slot after the rarity. Any other GMS2 command is <see cref="Other"/>.
    /// </summary>
    public static ItemMessage Read(ByteReader r, Func<long, EquipItem> find, ProtocolProfile profile) {
        int mode = r.ReadByte();
        bool gms2 = profile.IsGms2;
        if (gms2) mode = mode switch { 7 => 6, 8 => 7, 10 => 8, 12 => 10, 13 => 11, 14 => 12, 15 => 13, 16 => 14, <= 3 => mode, _ => 100 + mode };
        switch (mode) {
            case 0: return new Add(ItemReader.ReadInventory(r, profile, true));
            case 1: return new Remove(r.ReadLong());
            case 2: return new Amount(r.ReadLong(), r.ReadInt());
            case 3: return new Move(r.ReadLong(), r.ReadShort(), r.ReadLong(), r.ReadShort());
            case 6: return new Load(ItemList(r, profile));
            case 7: { long uid = r.ReadLong(); int arg = r.ReadInt(); return new NotifyNew(uid, arg, r.ReadUnicodeString()); }
            case 8: { int tab = r.ReadInt(); if (tab < 0) { r.ReadShort(); return new LoadTab(tab, new List<EquipItem>()); } return new LoadTab(tab, ItemList(r, profile)); } // FUN_141715860 returns on a negative tab without reading records
            case 10: return new ExpandComplete();
            case 11: return new Reset(r.ReadInt());
            case 12: { int tab = r.ReadByte(); return new ExpandCount(tab, r.ReadInt()); }
            case 13: {
                int code = r.ReadInt();
                // GMS2: upstream ItemInventoryPacket.Error writes the code alone; no recorded error carries 40/41
                if (!profile.IsGms2 && code is 0x28 or 0x29) { int id = r.ReadInt(); return new Error(code, id, (ushort) r.ReadShort()); }
                return new Error(code, 0, 0);
            }
            case 14: {
                long uid = r.ReadLong();
                if (uid == 0) return new Update(0, null);
                var it = find(uid);
                if (it == null) return new Update(uid, null);       // the client skips an unknown uid; the rest of the packet is unread
                var copy = new EquipItem { Id = it.Id, Uid = it.Uid, Rarity = it.Rarity, Slot = it.Slot, InventorySlot = it.InventorySlot };
                ItemReader.ReadBodyInto(r, copy, profile);
                return new Update(uid, copy);
            }
            default: return new Other(gms2 && mode >= 100 ? mode - 100 : mode);
        }
    }

    static List<EquipItem> ItemList(ByteReader r, ProtocolProfile profile) {
        int n = (ushort) r.ReadShort();
        var l = new List<EquipItem>(n);
        for (int i = 0; i < n; i++) {
            var it = ItemReader.ReadInventory(r, profile, false);
            if (it == null) break;      // an unknown item id: the client's create fails and the rest of the list is not read
            l.Add(it);
        }
        return l;
    }
}

/// <summary>
/// The item inventory a live session builds from the item packets (the client's g_143ab2a90 as far as the HUD needs it).
/// Items are kept by uid; <see cref="TabOf"/> (item id to client tab) lets a Reset drop one tab, without it Reset only reports.
/// </summary>
public sealed class ItemBook {
    readonly Dictionary<long, EquipItem> items = new();
    public IReadOnlyCollection<EquipItem> All => items.Values;
    public EquipItem Get(long uid) => items.TryGetValue(uid, out var i) ? i : null;
    /// <summary>The summed amount of the items with <paramref name="id"/> and <paramref name="rarity"/> across every tab (the
    /// reactor item condition, CReactor::CanUse). 0 B.</summary>
    public int CountOf(int id, int rarity) {
        int n = 0;
        foreach (var it in items.Values) if (it.Id == id && it.Rarity == rarity) n += it.Amount;
        return n;
    }
    public IReadOnlyDictionary<int, int> ExtraSlots => extra;
    readonly Dictionary<int, int> extra = new();
    public Func<int, int> TabOf { get; set; }
    /// <summary>The item's max stack (CItemData property slotMax, CItem::GetSlotMax). Without it an Amount is stored as sent.</summary>
    public Func<int, uint> MaxStackOf { get; set; }
    /// <summary>The base slot count of a tab (FUN_140a66510). Without it the slot range and the shrink of SetExtraSlots are not enforced.</summary>
    public Func<int, int> BaseSlots { get; set; }
    public const int Tabs = 15;
    /// <summary>After a message that changed the book: the item uid, or 0 when many changed.</summary>
    public event Action<long> Changed;
    public event Action<int, int, int> ErrorReceived;   // code, item id, value
    public event Action<long, int> NotifiedNew;

    public void Apply(ItemMessage m) {
        switch (m) {
            case ItemMessage.Add x: if (x.Item != null && TryAdd(x.Item) == 0) Changed?.Invoke(x.Item.Uid); break;
            case ItemMessage.Load x: foreach (var i in x.Items) TryAdd(i); Changed?.Invoke(0); break;
            case ItemMessage.LoadTab x: foreach (var i in x.Items) TryAdd(i); Changed?.Invoke(0); break;
            case ItemMessage.Remove x: if (items.Remove(x.Uid)) Changed?.Invoke(x.Uid); break;
            case ItemMessage.Amount x: if (items.TryGetValue(x.Uid, out var a)) { a.Amount = Clamp(a.Id, x.Count); Changed?.Invoke(x.Uid); } break;
            case ItemMessage.Move x: {
                // both items leave the bag, take their new slot and come back; an uid of 0 is an empty slot
                var a1 = x.UidA != 0 ? Get(x.UidA) : null; var b1 = x.UidB != 0 ? Get(x.UidB) : null;
                if (a1 != null) a1.InventorySlot = x.SlotA;
                if (b1 != null) b1.InventorySlot = x.SlotB;
                Changed?.Invoke(0); break;
            }
            case ItemMessage.Update x: if (x.Item != null) { items[x.Item.Uid] = x.Item; Changed?.Invoke(x.Uid); } break;
            case ItemMessage.Reset x:
                if (TabOf != null) foreach (var i in items.Values.Where(i => TabOf(i.Id) == x.Tab).ToList()) items.Remove(i.Uid);
                Changed?.Invoke(0); break;
            case ItemMessage.ExpandCount x: SetExtraSlots(x.Tab, x.Count); Changed?.Invoke(0); break;
            case ItemMessage.NotifyNew x: if (items.TryGetValue(x.Uid, out var nw)) { nw.IsNew = true; if (!string.IsNullOrEmpty(x.Name)) nw.NotifiedName = x.Name; NotifiedNew?.Invoke(x.Uid, x.Arg); Changed?.Invoke(x.Uid); } break;
            case ItemMessage.Error x: ErrorReceived?.Invoke(x.Code, x.ItemId, x.Value); break;
        }
    }

    /// <summary>CItem::SetCountClamped (0x140526c70): an unsigned compare with the max stack, so a negative count becomes the max.</summary>
    int Clamp(int itemId, int count) {
        if (MaxStackOf?.Invoke(itemId) is not uint max) return count;
        return (uint) count < max ? count : (int) max;
    }

    /// <summary>Unk_Inventory::AddItem (0x140a5d120): 0 added; 17 duplicate uid or occupied slot; 29 slot out of range. The old item stays on a refusal.</summary>
    public int TryAdd(EquipItem it) {
        if (items.ContainsKey(it.Uid)) return 17;
        if (TabOf != null && it.InventorySlot != -1) {
            int tab = TabOf(it.Id);
            if (BaseSlots != null && (it.InventorySlot < 0 || it.InventorySlot >= BaseSlots(tab) + (extra.TryGetValue(tab, out var e) ? e : 0))) return 29;
            foreach (var o in items.Values) if (o.InventorySlot == it.InventorySlot && TabOf(o.Id) == tab) return 17;
        }
        items[it.Uid] = it;
        return 0;
    }

    /// <summary>Unk_Inventory::SetExtraSlots (0x140a5cb80): tabs outside 0-14 are ignored; a smaller capacity removes the items past it.</summary>
    void SetExtraSlots(int tab, int count) {
        if (tab < 0 || tab >= Tabs) return;
        if (BaseSlots != null && TabOf != null) {
            int cap = BaseSlots(tab) + count;
            foreach (var i in items.Values.Where(i => TabOf(i.Id) == tab && (uint) i.InventorySlot >= (uint) cap).ToList()) items.Remove(i.Uid);
        }
        extra[tab] = count;
    }
}

/// <summary>
/// Inbound 0x20, CSceneStateGame::OnItemPutOnOrOff (scenestategame.cpp:2575, 0x141712fb0). Modes 2 and 3 only make the client send its own
/// equip (Unk_Inventory::FUN_140a621c0) or unequip (RequestUnequipItem) request for the uid; mode 5 moves a worn item (CEquipment::FUN_1411e4150).
/// The client ignores trailing bytes and any other mode.
/// </summary>
public abstract record ItemPutOnOrOff {
    public sealed record Equip(long Uid) : ItemPutOnOrOff;                                  // mode 2
    public sealed record Unequip(long Uid) : ItemPutOnOrOff;                                // 3
    /// <summary>Mode 5: the player (object id) moves a worn item to <paramref name="Slot"/>; <paramref name="Type"/> is the layer byte FUN_140222f60 maps.</summary>
    public sealed record MoveWorn(int ObjectId, long Uid, byte Slot, byte Type) : ItemPutOnOrOff;
    public sealed record Other(int Mode) : ItemPutOnOrOff;

    public static ItemPutOnOrOff Read(ByteReader r) {
        int mode = r.ReadByte();
        return mode switch {
            2 => new Equip(r.ReadLong()),
            3 => new Unequip(r.ReadLong()),
            5 => new MoveWorn(r.ReadInt(), r.ReadLong(), r.ReadByte(), r.ReadByte()),
            _ => new Other(mode),
        };
    }
}
