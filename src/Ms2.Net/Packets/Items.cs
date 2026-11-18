// Layouts adapted from the Maple2 server (AGPL-3.0, github.com/MS2Community/Maple2) and checked against the restored client and recorded KMS2 traffic.
using System.Numerics;
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

public enum EquipSlot : sbyte {
    SK = 0, HR = 1, FA = 2, FD = 3, LH = 4, RH = 5, CP = 6, MT = 7, CL = 8, PA = 9, GL = 10, SH = 11,
    FH = 12, EY = 13, EA = 14, PD = 15, RI = 16, BE = 17, ER = 18, OH = 19, Unknown = 20,
}

/// <summary>What the equip record keeps of an item; the long tail (stats, enchant, sockets, binding) is parsed and dropped.</summary>
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
    /// <summary>CItem::m_2c (IsNew 0x1405268f0, SetNew 0x1405268d0; ctor default 0). Set only by OnItemInventory mode 7 (scenestategame.cpp:2844); cleared by InvenSlot::vfunc_14 0x141406a60 / vfunc_20 0x141408cd0 and the tab clear FUN_14199be00.</summary>
    public bool IsNew;
}

/// <summary>The blocks CItem::ReadFromStream reads after the item status, in this order: ugc, gem (a badge), custom music score, pet.</summary>
[Flags] public enum ItemExtra { None = 0, Ugc = 1, Gem = 2, Music = 4, Pet = 8 }

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

    /// <summary>UgcItemLook (CUgcItemLook::Read): i64 id, file name, item name, u8, i32, i64 account, i64 character, author, i64 creation time, url, u8.</summary>
    public static void ReadUgcLook(ByteReader r) {
        r.ReadLong(); r.ReadUnicodeString(); r.ReadUnicodeString(); r.ReadByte(); r.ReadInt(); r.ReadLong(); r.ReadLong();
        r.ReadUnicodeString(); r.ReadLong(); r.ReadUnicodeString(); r.ReadByte();
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
        r.ReadInt(); // -1
        it.CreationTime = r.ReadLong();
        it.ExpiryTime = r.ReadLong();
        r.ReadLong();
        r.ReadInt(); // time changed option
        r.ReadInt(); // remain uses
        r.ReadBool(); // locked
        r.ReadLong(); // unlock time
        r.ReadShort(); // glamor forges
        r.ReadBool();
        r.ReadInt(); // gacha dismantle id

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

        // ItemStats: 5 option sets, then merge id
        for (int i = 0; i < 5; i++) {
            int basic = (ushort) r.ReadShort();
            for (int j = 0; j < basic; j++) { r.ReadShort(); r.ReadInt(); }
            int special = (ushort) r.ReadShort();
            for (int j = 0; j < special; j++) { r.ReadShort(); r.ReadInt(); }
        }
        r.ReadInt();

        // ItemEnchant
        r.ReadInt(); r.ReadInt(); r.ReadByte();
        r.ReadLong(); r.ReadInt(); r.ReadInt(); r.ReadBool(); // enchant history
        r.ReadInt();
        int ench = r.ReadByte();
        for (int i = 0; i < ench; i++) { r.ReadInt(); r.ReadInt(); }

        // ItemLimitBreak
        r.ReadInt();
        uint lb = (uint) r.ReadInt();
        for (uint i = 0; i < lb; i++) { r.ReadShort(); r.ReadInt(); }
        lb = (uint) r.ReadInt();
        for (uint i = 0; i < lb; i++) { r.ReadShort(); r.ReadInt(); }

        var extra = forceExtra ?? (badge ? ItemExtra.Gem : ExtraOf?.Invoke(it.Id)) ?? (LooksUgc(r) ? ItemExtra.Ugc : ItemExtra.None);
        if ((extra & ItemExtra.Ugc) != 0) {
            ReadUgcLook(r);
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

        // ItemTransfer
        r.ReadInt(); r.ReadBool(); r.ReadInt(); r.ReadInt(); r.ReadBool(); r.ReadBool();
        if (r.ReadBool()) { r.ReadLong(); r.ReadUnicodeString(); }

        // ItemSocket
        r.ReadByte();
        int unlocked = r.ReadByte();
        for (int i = 0; i < unlocked; i++) {
            if (!r.ReadBool()) continue;
            r.ReadInt();
            if (r.ReadBool()) { r.ReadLong(); r.ReadUnicodeString(); }
            if (r.ReadBool()) { r.ReadByte(); r.ReadLong(); }
        }

        // ItemCoupleInfo
        if (r.ReadLong() != 0) { r.ReadUnicodeString(); r.ReadBool(); }
        // ItemBinding
        r.ReadLong(); r.ReadUnicodeString();
    }

    static float[] ReadFloats(ByteReader r, int n) {
        var f = new float[n];
        for (int i = 0; i < n; i++) f[i] = r.ReadFloat();
        return f;
    }
}
