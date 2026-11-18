// Layouts adapted from the Maple2 server (AGPL-3.0, github.com/MS2Community/Maple2) and checked against the restored client and recorded KMS2 traffic.
using System.Numerics;
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

public enum EquipSlot : sbyte {
    SK = 0, HR = 1, FA = 2, FD = 3, LH = 4, RH = 5, CP = 6, MT = 7, CL = 8, PA = 9, GL = 10, SH = 11,
    FH = 12, EY = 13, EA = 14, PD = 15, RI = 16, BE = 17, ER = 18, OH = 19, Unknown = 20,
}

/// <summary>An item record as CItem::ReadFromStream (item.cpp:2655) fills it.</summary>
public sealed class EquipItem {
    public int Id; public long Uid; public EquipSlot Slot; public int Rarity;
    public int Amount; public long CreationTime, ExpiryTime;
    public EquipColor Color;
    /// <summary>Hair: back length, back pos 1, 2 (x,y,z each), front length, front pos 1, 2. Decal: 4 floats. Cap: 4 vec3 + 1 float. Else empty.</summary>
    public float[] Appearance = Array.Empty<float>();
    /// <summary>Badge records only: the badge type (equip slot is meaningless then).</summary>
    public byte BadgeType;
    public bool IsBadge;
    /// <summary>The gem block's type (CItemGem::vfunc_4: the gem's CGemBase m_nType, what GetGem()->vfunc_1() returns); 0 without
    /// a gem.</summary>
    public byte GemType;
    /// <summary>Inventory records only: the bag slot (the record's i16 after the uid).</summary>
    public short InventorySlot;
    /// <summary>CItem m_60, m_88, m_9c, m_a0 (the stream's ints and longs after the times, item.cpp:2658-2664).
    /// RetrievableUntil: m_88 (CNetworkService::SendRetrieveFurnishingItem sends only while it is a future time).
    /// RemakeCount: m_9c (CItem::GetRemakeCount, item.cpp:357). HeaderInt60 has no named use yet.</summary>
    public int HeaderInt60, RemakeCount, RemainUses; public long RetrievableUntil;
    /// <summary>CItem m_a4 (GetLockState) and m_a8 (GetUnlockTime), item.cpp:2665-2666.</summary>
    public byte LockState; public long UnlockTime;
    /// <summary>CItem m_d8 (u16), m_d4 (bool), m_dc (item.cpp:2667-2669).</summary>
    public ushort GlamorForges; public bool HeaderFlagD4; public int GachaDismantleId;
    /// <summary>The UGC block was read: CItem has m_spUgc exactly when it reads it (both on HasCubeTextureMesh or
    /// FUN_140526010, item.cpp:2672 and :2774).</summary>
    public bool HasUgc;
    /// <summary>The UGC block's look (CItem::GetUgcLook); null without one.</summary>
    public UgcLook Ugc;
    /// <summary>CItemStatus (itemsocketsystemutil.cpp:1868): the option sets, the enchant and the limit break.</summary>
    public ItemOptions Options = new();
    public ItemEnchant Enchant = new();
    public ItemLimitBreak LimitBreak = new();
    /// <summary>CItemTransfer::vfunc_2 (itemsocketsystemutil.cpp:2711).</summary>
    public ItemTransfer Transfer = new();
    /// <summary>CItemSocket::ReadFromStream (itemsocket.cpp:314).</summary>
    public ItemSocket Socket = new();
    /// <summary>CItemCoupleInfo::ReadFromStream (item.cpp:2847): the partner's id (0 = none), name and a flag.</summary>
    public long CoupleId; public string CoupleName = ""; public bool CoupleFlag;
    /// <summary>CItem m_spBinding, CItemBinding::FUN_140634700 (itemsocketsystemutil.cpp:2592): the character id and name.</summary>
    public long BindingId; public string BindingName = "";
    /// <summary>CItem::m_2c (IsNew 0x1405268f0, SetNew 0x1405268d0; ctor default 0). Set only by OnItemInventory mode 7 (scenestategame.cpp:2844); cleared by InvenSlot::vfunc_14 0x141406a60 / vfunc_20 0x141408cd0 and the tab clear FUN_14199be00.</summary>
    public bool IsNew;
}

/// <summary>CItemOption::Deserialize (itemoptiondata_1.cpp:242): 5 groups of stat and special options (id -> BaseOption
/// m_nValue, the one int BaseOption::Read reads), then m_n2f0.</summary>
public sealed class ItemOptions {
    public readonly Dictionary<int, int>[] Stat = { new(), new(), new(), new(), new() };
    public readonly Dictionary<int, int>[] Special = { new(), new(), new(), new(), new() };
    /// <summary>m_n2f0 (CItemOption::GetMergeId, itemoptiondata_1.cpp:89).</summary>
    public int MergeId;
}

/// <summary>CItemEnchantInfo::Read (itemenchantinfo.cpp:194): level, charge, version, the history (Unk_EnchantHistory::Read
/// :28), then the options (int id -> value).</summary>
public sealed class ItemEnchant {
    public int Level, Charge; public byte Version;
    /// <summary>HistoryEnabled: the history ctor sets enabled to true (itemenchantinfo.cpp:18); a read replaces it.</summary>
    public long HistoryItemId; public int Attempts, Failures; public bool HistoryEnabled = true; public int FailStack;
    public readonly Dictionary<int, int> Options = new();
}

/// <summary>UnlimitedEnchantInfo::ReadFromStream (unlimitedenchantinfo.cpp:101): level, stat options, special options.</summary>
public sealed class ItemLimitBreak {
    public int Level;
    public readonly Dictionary<int, int> Stat = new(), Special = new();
}

/// <summary>CItemTransfer::vfunc_2: rule, m_b38, count, max, m_b3F, m_b42, then the binding when present.</summary>
public sealed class ItemTransfer {
    /// <summary>BreakDisabled: m_b3F (CItemTransfer::CanBreak refuses; FUN_140635300 sets it). SocketTransferEnabled: m_b42
    /// (SetSocketTransferFlag; CanTransferSocket).</summary>
    public int Rule, Count, Max; public bool TransferFlag38, BreakDisabled, SocketTransferEnabled;
    public bool HasBinding; public long BindingId; public string BindingName = "";
}

/// <summary>CItemSocket: m_nMaxSlot and the slots (null = empty). A gem: TSocketGemStoneInfo::ReadFromStream (itemsocket.cpp:228).</summary>
public sealed class ItemSocket {
    public int MaxSlot;
    public readonly List<SocketGem> Gems = new();
}

/// <summary>TSocketGemStoneInfo: the gem id, the bind (TSocketGemStoneBind::Read :37) and the lock (TSocketGemStoneLock :51).</summary>
public sealed class SocketGem {
    public int GemId;
    public bool HasBind; public long BindCharacterId; public string BindName = "";
    public bool HasLock; public byte LockState; public long LockExpireTime;
}

/// <summary>The blocks CItem::ReadFromStream reads after the item status, in this order: ugc, gem (a badge), custom music score, pet.</summary>
[Flags] public enum ItemExtra { None = 0, Ugc = 1, Gem = 2, Music = 4, Pet = 8 }

/// <summary>CUgcItemLook (ugcdef.cpp:1551), the fields the item tooltip reads: m_18 the UGC id, m_48 the author and m_50
/// the creation time (ASTooltip::SetItemCreateTime).</summary>
public sealed class UgcLook {
    public long Uid; public string Author = ""; public long CreationTime;
    /// <summary>The look's state byte, the last one CUgcItemLook::Read (0x140893250) reads (m_n60); CUgcItemLook::FUN_140893520 tests it for 2.</summary>
    public byte State;
}

/// <summary>
/// Readers for the item records (CItem::ReadFromStream, item.cpp:2655). An equip record carries its slot byte, which picks the
/// body's appearance variant (HR, FD, CP); an inventory record takes it from <see cref="SlotOf"/>. The extra blocks (ugc, gem,
/// music, pet) come from <see cref="ExtraOf"/> when the item data is loaded; without it a ugc block is recognised from its bytes.
/// </summary>
public static class ItemReader {
    
    /// <summary>
    /// Optional override: tells which extra block an item carries, from the player's item table (the client decides by item data).
    /// When null, a UGC block is recognised from its bytes: it starts with an i64 id followed by that id as text, which no
    /// transfer block can look like. Pet and music blocks are never worn, so they are only read when this returns them.
    /// </summary>
    public static Func<int, ItemExtra?> ExtraOf;

    /// <summary>
    /// Tells whether the item id is in the item data. CItem::FUN_140523c40 (item.cpp:2759) fails for an unknown id, so an inventory
    /// record of it reads no body and is not added. Null = every id is taken as known.
    /// </summary>
    public static Func<int, bool> Known;

    /// <summary>UgcItemLook (CUgcItemLook::Read, ugcdef.cpp:1551): i64 id, file name, item name, u8, i32, i64 account, i64 character, author, i64 creation time, url, u8.</summary>
    public static UgcLook ReadUgcLook(ByteReader r) {
        var look = new UgcLook { Uid = r.ReadLong() };
        r.ReadUnicodeString(); r.ReadUnicodeString(); r.ReadByte(); r.ReadInt(); r.ReadLong(); r.ReadLong();
        look.Author = r.ReadUnicodeString(); look.CreationTime = r.ReadLong();
        r.ReadUnicodeString(); look.State = r.ReadByte();
        return look;
    }

    /// <summary>A pet item as FieldAddUser carries it: i32 id, i64 uid, i32 rarity, then the item body with a pet block.</summary>
    public static EquipItem ReadPetItem(ByteReader r) {
        var it = new EquipItem { Id = r.ReadInt(), Uid = r.ReadLong(), Rarity = r.ReadInt(), Slot = EquipSlot.Unknown };
        ReadBody(r, it, EquipSlot.Unknown, forceExtra: ItemExtra.Pet);
        return it;
    }

    static bool LooksUgc(ByteReader r) {
        if (r.Available < 10) return false;
        var b = r.Buffer; int p = r.Position;
        long id = BitConverter.ToInt64(b, p);
        int len = BitConverter.ToUInt16(b, p + 8);
        if (r.Available < 10 + len * 2) return false;
        string text = new string(Enumerable.Range(0, len).Select(i => (char) BitConverter.ToUInt16(b, p + 10 + i * 2)).ToArray());
        // the file name of the ugc: a uuid (8-4-4-4-12) or, for a blank look, the id as text
        if (len == 36) return text[8] == '-' && text[13] == '-' && text[18] == '-' && text[23] == '-';
        return text == id.ToString();
    }

    /// <summary>
    /// Tells the first model slot of an item, from the item data (itemmodel slots after the itemdata preset). The body's appearance
    /// variant follows it (hair HR, decal FD, cap CP; CItemData::CreateExtraData). Null = every body is read without one.
    /// </summary>
    public static Func<int, EquipSlot> SlotOf;

    /// <summary>
    /// An inventory record (OnItemInventory modes 0, 6, 8): i32 id, i64 uid, i16 bag slot, i32 rarity, then the item body.
    /// Null for an id the item data does not know: no body is read (the client's item create fails there).
    /// </summary>
    public static EquipItem ReadInventory(ByteReader r) {
        var it = new EquipItem { Id = r.ReadInt(), Uid = r.ReadLong(), InventorySlot = r.ReadShort(), Rarity = r.ReadInt() };
        if (Known != null && !Known(it.Id)) return null;
        it.Slot = SlotOf?.Invoke(it.Id) ?? EquipSlot.Unknown;
        ReadBody(r, it, it.Slot);
        return it;
    }

    /// <summary>The body alone of a known item (OnItemInventory mode 0xe re-reads it into the item found by uid).</summary>
    public static void ReadBodyInto(ByteReader r, EquipItem it) {
        ReadBody(r, it, it.Slot);
    }

    /// <summary>
    /// An item made from its id and rarity and then read (CItem::FUN_140523c40 + ReadFromStream, as the npc shop's products,
    /// npcshopproductbase.cpp:186): the body alone, its first model slot from <see cref="SlotOf"/>.
    /// </summary>
    public static EquipItem ReadItem(ByteReader r, int id, int rarity) {
        var it = new EquipItem { Id = id, Rarity = rarity, Slot = SlotOf?.Invoke(id) ?? EquipSlot.Unknown };
        ReadBody(r, it, it.Slot);
        return it;
    }

    /// <summary>
    /// The body <see cref="ReadBody"/> reads for an item with every block empty and no extra block (no UGC, gem, music or
    /// pet; not a hair, decal or cap), the amount set (tests and offline fixtures).
    /// </summary>
    public static void WriteEmptyBody(ByteWriter w, int amount) {
        w.WriteInt(amount); w.WriteInt(0); w.WriteLong(0); w.WriteLong(0); w.WriteLong(0); w.WriteInt(0); w.WriteInt(0);
        w.WriteByte(0); w.WriteLong(0); w.WriteShort(0); w.WriteBool(false); w.WriteInt(0);
        for (int i = 0; i < 3; i++) w.WriteInt(0);          // the colour: three rgba
        w.WriteInt(0); w.WriteInt(0);                        // its index and palette
        for (int i = 0; i < 5; i++) { w.WriteShort(0); w.WriteShort(0); }   // the option sets
        w.WriteInt(0);
        w.WriteInt(0); w.WriteInt(0); w.WriteByte(0); w.WriteLong(0); w.WriteInt(0); w.WriteInt(0); w.WriteBool(false); w.WriteInt(0); w.WriteByte(0);   // the enchant
        w.WriteInt(0); w.WriteInt(0); w.WriteInt(0);         // the limit break
        w.WriteInt(0); w.WriteBool(false); w.WriteInt(0); w.WriteInt(0); w.WriteByte(0); w.WriteByte(0); w.WriteBool(false);   // the transfer
        w.WriteByte(0); w.WriteByte(0);                      // the socket
        w.WriteLong(0);                                      // no couple
        w.WriteLong(0); w.WriteUnicodeString("");            // the binding
    }

    public static EquipItem ReadEquip(ByteReader r) {
        var it = new EquipItem { Id = r.ReadInt(), Uid = r.ReadLong(), Slot = (EquipSlot) r.ReadByte(), Rarity = r.ReadInt() };
        ReadBody(r, it, it.Slot);
        return it;
    }

    /// <summary>ItemPutOn (CSceneStateGame::OnItemPutOn): i32 object id, i32 item id, i64 uid, u8 slot, i32 rarity, u8 kind, then the item body. Worn by another player, or the local one.</summary>
    public static (int ObjectId, byte Kind, EquipItem Item) ReadPutOn(ByteReader r) {
        int obj = r.ReadInt();
        var it = new EquipItem { Id = r.ReadInt(), Uid = r.ReadLong(), Slot = (EquipSlot) r.ReadByte(), Rarity = r.ReadInt() };
        byte kind = r.ReadByte();
        ReadBody(r, it, it.Slot);
        return (obj, kind, it);
    }

    public static EquipItem ReadBadge(ByteReader r) {
        var it = new EquipItem { IsBadge = true, BadgeType = r.ReadByte(), Id = r.ReadInt(), Uid = r.ReadLong(), Rarity = r.ReadInt() };
        ReadBody(r, it, EquipSlot.Unknown, badge: true);
        return it;
    }

    static void ReadBody(ByteReader r, EquipItem it, EquipSlot slot, bool badge = false, ItemExtra? forceExtra = null) {
        it.Amount = r.ReadInt();
        it.HeaderInt60 = r.ReadInt();
        it.CreationTime = r.ReadLong();
        it.ExpiryTime = r.ReadLong();
        it.RetrievableUntil = r.ReadLong();
        it.RemakeCount = r.ReadInt();
        it.RemainUses = r.ReadInt();
        it.LockState = r.ReadByte();
        it.UnlockTime = r.ReadLong();
        it.GlamorForges = (ushort) r.ReadShort();
        it.HeaderFlagD4 = r.ReadBool();
        it.GachaDismantleId = r.ReadInt();

        it.Color = r.ReadEquipColor();
        switch (slot) {
            case EquipSlot.HR:
                it.Appearance = ReadFloats(r, 1 + 3 + 3 + 1 + 3 + 3);
                break;
            case EquipSlot.FD:
                it.Appearance = ReadFloats(r, 4);
                break;
            case EquipSlot.CP:
                it.Appearance = ReadFloats(r, 3 * 4 + 1);
                break;
        }

        // CItemStatus::ReadFromStream: the options, the enchant, the limit break
        // a re-read starts over as the client's do (ClearAll, Reset, the clears, RemoveAll)
        var o = it.Options = new ItemOptions();
        for (int i = 0; i < 5; i++) {
            int basic = (ushort) r.ReadShort();
            for (int j = 0; j < basic; j++) { int id = (ushort) r.ReadShort(); o.Stat[i][id] = r.ReadInt(); }
            int special = (ushort) r.ReadShort();
            for (int j = 0; j < special; j++) { int id = (ushort) r.ReadShort(); o.Special[i][id] = r.ReadInt(); }
        }
        o.MergeId = r.ReadInt();

        var e = it.Enchant = new ItemEnchant();
        e.Level = r.ReadInt(); e.Charge = r.ReadInt(); e.Version = r.ReadByte();
        e.HistoryItemId = r.ReadLong(); e.Attempts = r.ReadInt(); e.Failures = r.ReadInt(); e.HistoryEnabled = r.ReadBool(); e.FailStack = r.ReadInt();
        int ench = r.ReadByte();
        for (int i = 0; i < ench; i++) { int id = r.ReadInt(); e.Options[id] = r.ReadInt(); }

        var lb = it.LimitBreak = new ItemLimitBreak();
        lb.Level = r.ReadInt();
        uint n = (uint) r.ReadInt();
        for (uint i = 0; i < n; i++) { int id = (ushort) r.ReadShort(); lb.Stat[id] = r.ReadInt(); }
        n = (uint) r.ReadInt();
        for (uint i = 0; i < n; i++) { int id = (ushort) r.ReadShort(); lb.Special[id] = r.ReadInt(); }

        var extra = forceExtra ?? (badge ? ItemExtra.Gem : ExtraOf?.Invoke(it.Id)) ?? (LooksUgc(r) ? ItemExtra.Ugc : ItemExtra.None);
        it.HasUgc = (extra & ItemExtra.Ugc) != 0;
        if (it.HasUgc) {
            it.Ugc = ReadUgcLook(r);
            // ItemBlueprint: i64, 3 x i32, i64, i32 type, i64, i64, string
            r.ReadLong(); r.ReadInt(); r.ReadInt(); r.ReadInt(); r.ReadLong(); r.ReadInt(); r.ReadLong(); r.ReadLong(); r.ReadUnicodeString();
        }
        if ((extra & ItemExtra.Gem) != 0) {
            // CItemGem::vfunc_4 0x1405a7920: bool has; u8 type, i32 gem id, then the gem (type 1 transparency: 10 bytes, type 11 pet skin: i32)
            if (r.ReadBool()) {
                byte type = r.ReadByte();
                it.GemType = type;
                r.ReadInt();
                if (type == 1) { for (int i = 0; i < 10; i++) r.ReadByte(); }
                else if (type == 11) r.ReadInt();
            }
        }
        if ((extra & ItemExtra.Music) != 0) {
            // CItemCustomMusicScore::ReadFromStream 0x140632620: i32, i32, string, string, i32, i64, u8, i64, i64
            r.ReadInt(); r.ReadInt(); r.ReadUnicodeString(); r.ReadUnicodeString(); r.ReadInt(); r.ReadLong(); r.ReadByte(); r.ReadLong(); r.ReadLong();
        }
        if ((extra & ItemExtra.Pet) != 0) {
            // item.cpp:2655: string, i64, i32, i32 pet level, u8
            r.ReadUnicodeString(); r.ReadLong(); r.ReadInt(); r.ReadInt(); r.ReadBool();
        }

        var t = it.Transfer;
        t.Rule = r.ReadInt(); t.TransferFlag38 = r.ReadBool(); t.Count = r.ReadInt(); t.Max = r.ReadInt(); t.BreakDisabled = r.ReadByte() != 0; t.SocketTransferEnabled = r.ReadByte() != 0;
        // the binding is only assigned when present (vfunc_2 keeps an earlier one otherwise)
        if (r.ReadBool()) { t.HasBinding = true; t.BindingId = r.ReadLong(); t.BindingName = r.ReadUnicodeString(); }

        var so = it.Socket = new ItemSocket();
        so.MaxSlot = r.ReadByte();
        int count = r.ReadByte();
        for (int i = 0; i < count; i++) {
            if (!r.ReadBool()) { so.Gems.Add(null); continue; }
            var g = new SocketGem { GemId = r.ReadInt() };
            if (g.HasBind = r.ReadBool()) { g.BindCharacterId = r.ReadLong(); g.BindName = r.ReadUnicodeString(); }
            if (g.HasLock = r.ReadBool()) { g.LockState = r.ReadByte(); g.LockExpireTime = r.ReadLong(); }
            so.Gems.Add(g);
        }

        // the couple's name and flag are only read for a partner (an earlier name stays otherwise)
        if ((it.CoupleId = r.ReadLong()) != 0) { it.CoupleName = r.ReadUnicodeString(); it.CoupleFlag = r.ReadBool(); }
        it.BindingId = r.ReadLong(); it.BindingName = r.ReadUnicodeString();
    }

    static float[] ReadFloats(ByteReader r, int n) {
        var f = new float[n];
        for (int i = 0; i < n; i++) f[i] = r.ReadFloat();
        return f;
    }
}
