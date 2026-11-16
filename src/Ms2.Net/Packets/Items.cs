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
}

/// <summary>Which extra block follows the limit break part of an item body (CItem::ReadFromStream picks it from item data).</summary>
public enum ItemExtra { None, Ugc, Pet, Music }

/// <summary>
/// Reader for the equip record: i32 id, i64 uid, i8 slot, i32 rarity, then the item body (CItem::ReadFromStream).
/// The body's appearance variant depends on the item's first slot (HR, FD, CP) which the server takes from item data;
/// the record's slot byte carries it for equips, so no item table is needed. Ugc/pet/music extra blocks are
/// not expected on worn gear and are rejected as a layout error rather than guessed.
/// </summary>
public static class ItemReader {
    
    /// <summary>
    /// Optional override: tells which extra block an item carries, from the player's item table (the client decides by item data).
    /// When null, a UGC block is recognised from its bytes: it starts with an i64 id followed by that id as text, which no
    /// transfer block can look like. Pet and music blocks are never worn, so they are only read when this returns them.
    /// </summary>
    public static Func<int, ItemExtra?> ExtraOf;

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
            int basic = r.ReadShort();
            for (int j = 0; j < basic; j++) { r.ReadShort(); r.ReadInt(); }
            int special = r.ReadShort();
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
        int lb = r.ReadInt();
        for (int i = 0; i < lb; i++) { r.ReadShort(); r.ReadInt(); }
        lb = r.ReadInt();
        for (int i = 0; i < lb; i++) { r.ReadShort(); r.ReadInt(); }

        if (badge) {
            // ItemBadge: u8 1, u8 type, i32 id; transparency: 10 bools; pet skin: i32
            r.ReadByte();
            byte type = r.ReadByte();
            r.ReadInt();
            if (type == 1) { for (int i = 0; i < 10; i++) r.ReadBool(); }
            else if (type == 11) r.ReadInt();
        }

        switch (forceExtra ?? (badge ? ItemExtra.None : ExtraOf?.Invoke(it.Id)) ?? (LooksUgc(r) ? ItemExtra.Ugc : ItemExtra.None)) {
            case ItemExtra.Ugc:
                ReadUgcLook(r);
                // ItemBlueprint: i64, 3 x i32, i64, i32 type, i64, i64, string
                r.ReadLong(); r.ReadInt(); r.ReadInt(); r.ReadInt(); r.ReadLong(); r.ReadInt(); r.ReadLong(); r.ReadLong(); r.ReadUnicodeString();
                break;
            case ItemExtra.Pet:
                r.ReadUnicodeString(); r.ReadLong(); r.ReadInt(); r.ReadInt(); r.ReadBool();
                break;
            case ItemExtra.Music:
                r.ReadInt(); r.ReadByte(); r.ReadUnicodeString(); r.ReadUnicodeString(); r.ReadInt(); r.ReadLong(); r.ReadBool(); r.ReadLong(); r.ReadLong();
                break;
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
