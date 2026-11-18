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
    public static ItemMessage Read(ByteReader r, Func<long, EquipItem> find) {
        int mode = r.ReadByte();
        switch (mode) {
            case 0: return new Add(ItemReader.ReadInventory(r));
            case 1: return new Remove(r.ReadLong());
            case 2: return new Amount(r.ReadLong(), r.ReadInt());
            case 3: return new Move(r.ReadLong(), r.ReadShort(), r.ReadLong(), r.ReadShort());
            case 6: return new Load(ItemList(r));
            case 7: { long uid = r.ReadLong(); int arg = r.ReadInt(); return new NotifyNew(uid, arg, r.ReadUnicodeString()); }
            case 8: { int tab = r.ReadInt(); return new LoadTab(tab, ItemList(r)); }
            case 10: return new ExpandComplete();
            case 11: return new Reset(r.ReadInt());
            case 12: { int tab = r.ReadByte(); return new ExpandCount(tab, r.ReadInt()); }
            case 13: {
                int code = r.ReadInt();
                if (code is 0x28 or 0x29) { int id = r.ReadInt(); return new Error(code, id, (ushort) r.ReadShort()); }
                return new Error(code, 0, 0);
            }
            case 14: {
                long uid = r.ReadLong();
                if (uid == 0) return new Update(0, null);
                var it = find(uid);
                if (it == null) return new Update(uid, null);       // the client skips an unknown uid; the rest of the packet is unread
                var copy = new EquipItem { Id = it.Id, Uid = it.Uid, Rarity = it.Rarity, Slot = it.Slot, InventorySlot = it.InventorySlot };
                ItemReader.ReadBodyInto(r, copy);
                return new Update(uid, copy);
            }
            default: return new Other(mode);
        }
    }

    static List<EquipItem> ItemList(ByteReader r) {
        int n = (ushort) r.ReadShort();
        var l = new List<EquipItem>(n);
        for (int i = 0; i < n; i++) l.Add(ItemReader.ReadInventory(r));
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
    public IReadOnlyDictionary<int, int> ExtraSlots => extra;
    readonly Dictionary<int, int> extra = new();
    public Func<int, int> TabOf { get; set; }
    /// <summary>After a message that changed the book: the item uid, or 0 when many changed.</summary>
    public event Action<long> Changed;
    public event Action<int, int, int> ErrorReceived;   // code, item id, value
    public event Action<long, int> NotifiedNew;

    public void Apply(ItemMessage m) {
        switch (m) {
            case ItemMessage.Add x: items[x.Item.Uid] = x.Item; Changed?.Invoke(x.Item.Uid); break;
            case ItemMessage.Load x: foreach (var i in x.Items) items[i.Uid] = i; Changed?.Invoke(0); break;
            case ItemMessage.LoadTab x: foreach (var i in x.Items) items[i.Uid] = i; Changed?.Invoke(0); break;
            case ItemMessage.Remove x: if (items.Remove(x.Uid)) Changed?.Invoke(x.Uid); break;
            case ItemMessage.Amount x: if (items.TryGetValue(x.Uid, out var a)) { a.Amount = Math.Max(0, x.Count); Changed?.Invoke(x.Uid); } break;
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
            case ItemMessage.ExpandCount x: extra[x.Tab] = x.Count; Changed?.Invoke(0); break;
            case ItemMessage.NotifyNew x: if (items.ContainsKey(x.Uid)) NotifiedNew?.Invoke(x.Uid, x.Arg); break;
            case ItemMessage.Error x: ErrorReceived?.Invoke(x.Code, x.ItemId, x.Value); break;
        }
    }
}
