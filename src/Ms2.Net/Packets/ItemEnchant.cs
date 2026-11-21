using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// The item enchant dialog's packets: UIItemEnchantDialog (client ms2/uiitemenchantdialog.cpp, UI id 0x4e92).

/// <summary>CIngredientInfo::FUN_14026b410 (commonstruct_wa.cpp:137): item id, tag key, count (3 x i32).</summary>
public readonly record struct EnchantIngredient(int ItemId, int Tag, int Count);

/// <summary>The fail-stack rates the ophelia (mode 1) answer and the mode 7 packet carry: m_228 (base rate), m_22c, m_230
/// (protection rate), m_234, m_238 (floats).</summary>
public readonly record struct EnchantRates(float Base, float R22C, float Protect, float R234, float R238);

/// <summary>Inbound 0x97, CSceneStateGame::OnItemEnchant (scenestategame.cpp:6101, @0x141732B20), by the mode byte.</summary>
public abstract record ItemEnchantMessage {
    /// <summary>Mode 5, UIItemEnchantDialog::OnEnchantInfoPacket (@0x1419c1f20). ExpMode is the i16 the packet carries (the
    /// dialog closes when it differs from its own m_expMode); the rest follows only when it matches. Rates, Protector0/1,
    /// FailStackMode apply to ExpMode 1 (ophelia); ExpMode 2 (peachy) has base and m_23c = 100.</summary>
    public sealed record Info(short ExpMode, long Uid, IReadOnlyList<EnchantIngredient> Ingredients, ushort N2B0,
        IReadOnlyDictionary<int, int> Options, EnchantRates Rates, long Protector0, long Protector1, byte FailStackMode,
        int ResultItemId, short ResultRank, int ResultCount) : ItemEnchantMessage;
    /// <summary>Mode 6 (FUN_1419c3950): the item's enchant charge changed.</summary>
    public sealed record ChargeChanged(long Uid, int Charge) : ItemEnchantMessage;
    /// <summary>Mode 7 (FUN_1419c3b90): fail stack, m_2bc, the stack item uids and the five rates.</summary>
    public sealed record FailStackInfo(int FailStack, int N2BC, IReadOnlyList<long> Uids, EnchantRates Rates) : ItemEnchantMessage;
    /// <summary>Mode 8 (OnFailStackPacket): m_2bc and the stack item uids.</summary>
    public sealed record FailStackList(int N2BC, IReadOnlyList<long> Uids) : ItemEnchantMessage;
    /// <summary>Mode 9: the charge was refunded (notice s_itemenchant_enchant_exp_refund_ok).</summary>
    public sealed record Refunded : ItemEnchantMessage;
    /// <summary>Mode 10: success. The item body is re-read and then its options. Item is null for an unknown uid (the rest is not parsed).</summary>
    public sealed record Success(long Uid, EquipItem Item, IReadOnlyDictionary<int, int> Options) : ItemEnchantMessage;
    /// <summary>Mode 11: failure. The item body is re-read, then a, b, c (the reward page: item id, count, meso) and the fail type.</summary>
    public sealed record Failure(long Uid, EquipItem Item, int A, int B, long C, int FailType) : ItemEnchantMessage;
    /// <summary>Mode 12: an error code (1 -> notice 0x94d, 2 -> 0x94f, 3 -> 0x950, else 0x94c with the code).</summary>
    public sealed record Error(short Code) : ItemEnchantMessage;
    /// <summary>Mode 15: an item body re-read.</summary>
    public sealed record ItemUpdate(long Uid, EquipItem Item) : ItemEnchantMessage;
    /// <summary>Mode 16: refresh the ingredient counts (no body).</summary>
    public sealed record IngredientCounts : ItemEnchantMessage;
    public sealed record Other(int Mode) : ItemEnchantMessage;

    /// <summary>CIngredientContainer::Read (commonstruct_wa.cpp:255): u8 count, then each ingredient.</summary>
    public static List<EnchantIngredient> ReadIngredients(ByteReader r) {
        int n = r.ReadByte();
        var l = new List<EnchantIngredient>(n);
        for (int i = 0; i < n; i++) l.Add(new EnchantIngredient(r.ReadInt(), r.ReadInt(), r.ReadInt()));
        return l;
    }

    /// <summary>CEnchantOption::ReadOptions (unit_14058d5c0_enchantoption.cpp:23): i32 count, each u16 key and the i32 BaseOption value.</summary>
    public static Dictionary<int, int> ReadOptions(ByteReader r) {
        int n = r.ReadInt();
        var d = new Dictionary<int, int>();
        for (int i = 0; i < n; i++) { int k = (ushort) r.ReadShort(); d[k] = r.ReadInt(); }
        return d;
    }

    static EnchantRates ReadRates(ByteReader r) => new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat());

    static List<long> ReadUids(ByteReader r) {
        int n = r.ReadInt();
        var l = new List<long>(Math.Max(0, Math.Min(n, 64)));
        for (int i = 0; i < n; i++) l.Add(r.ReadLong());
        return l;
    }

    /// <summary>UIItemEnchantDialog::UpdateItemFromPacket: the body re-read into the item found by uid; null when unknown.</summary>
    static EquipItem ReadItemBody(ByteReader r, long uid, Func<long, EquipItem> find) {
        var it = find(uid);
        if (it == null) return null;
        var copy = new EquipItem { Id = it.Id, Uid = it.Uid, Rarity = it.Rarity, Slot = it.Slot, InventorySlot = it.InventorySlot };
        ItemReader.ReadBodyInto(r, copy);
        return copy;
    }

    /// <summary>Reads one message. <paramref name="find"/> gives the inventory item of an uid (modes 10, 11 and 15 read its body).</summary>
    public static ItemEnchantMessage Read(ByteReader r, Func<long, EquipItem> find) {
        int mode = r.ReadByte();
        switch (mode) {
            case 5: {
                short exp = r.ReadShort(); long uid = r.ReadLong();
                var ing = ReadIngredients(r); ushort n2b0 = (ushort) r.ReadShort(); var opt = ReadOptions(r);
                EnchantRates rates = default; long p0 = 0, p1 = 0; byte fs = 0; int rid = 0; short rank = 0; int cnt = 0;
                if (exp == 1) {
                    rates = ReadRates(r); p0 = r.ReadLong(); p1 = r.ReadLong(); fs = r.ReadByte();
                    rid = r.ReadInt(); rank = r.ReadShort(); cnt = r.ReadInt();
                } else if (exp == 2) {
                    rates = new EnchantRates(100, 0, 0, 0, 0);   // m_228 = 100, m_23c = 100
                    rid = r.ReadInt(); rank = r.ReadShort(); cnt = r.ReadInt();
                }
                return new Info(exp, uid, ing, n2b0, opt, rates, p0, p1, fs, rid, rank, cnt);
            }
            case 6: { long uid = r.ReadLong(); return new ChargeChanged(uid, r.ReadInt()); }
            case 7: { int fs = r.ReadInt(); int n2bc = r.ReadInt(); var u = ReadUids(r); return new FailStackInfo(fs, n2bc, u, ReadRates(r)); }
            case 8: { int n2bc = r.ReadInt(); return new FailStackList(n2bc, ReadUids(r)); }
            case 9: return new Refunded();
            case 10: {
                long uid = r.ReadLong(); var it = ReadItemBody(r, uid, find);
                return new Success(uid, it, it == null ? new Dictionary<int, int>() : ReadOptions(r));
            }
            case 11: {
                long uid = r.ReadLong(); var it = ReadItemBody(r, uid, find);
                if (it == null) return new Failure(uid, null, 0, 0, 0, 0);
                return new Failure(uid, it, r.ReadInt(), r.ReadInt(), r.ReadLong(), r.ReadInt());
            }
            case 12: return new Error(r.ReadShort());
            case 15: { long uid = r.ReadLong(); return new ItemUpdate(uid, ReadItemBody(r, uid, find)); }
            case 16: return new IngredientCounts();
            default: return new Other(mode);
        }
    }
}

/// <summary>Mode 0 (OnLoadComplete @0x1419bf410): the dialog loaded.</summary>
public sealed record ItemEnchantLoadRequest : IOutPacket {
    public Outbound Op => Outbound.RequestItemEnchant;
    public void Write(ByteWriter w) => w.WriteByte(0);
}

/// <summary>Mode 1 (SendRequestItemEnchantMode1 @0x1419c3fd0): select the item to enchant. ExpMode is 1 (ophelia) or 2 (peachy).</summary>
public sealed record ItemEnchantSelectRequest(byte ExpMode, long Uid) : IOutPacket {
    public Outbound Op => Outbound.RequestItemEnchant;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteByte(ExpMode); w.WriteLong(Uid); }
}

/// <summary>Mode 2 (SendRequestItemEnchantMode2 @0x1419c5de0): select an equip ingredient (Flag 1 once the player confirmed an enchanted one).</summary>
public sealed record ItemEnchantIngredientRequest(long Uid, byte Flag) : IOutPacket {
    public Outbound Op => Outbound.RequestItemEnchant;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteLong(Uid); w.WriteByte(Flag); }
}

/// <summary>Mode 3: the fail stack count, one up or down from the dialog's (m_failStack +- 1).</summary>
public sealed record ItemEnchantFailStackRequest(int FailStack) : IOutPacket {
    public Outbound Op => Outbound.RequestItemEnchant;
    public void Write(ByteWriter w) { w.WriteByte(3); w.WriteInt(FailStack); }
}

/// <summary>Mode 4 (SendRequestItemEnchantMode4 @0x1419c7ce0): enchant the ophelia way. Other is the protector uid (m_210 for
/// protector 1, m_220 for 2, else 0).</summary>
public sealed record ItemEnchantRequest(long Uid, long Other) : IOutPacket {
    public Outbound Op => Outbound.RequestItemEnchant;
    public void Write(ByteWriter w) { w.WriteByte(4); w.WriteLong(Uid); w.WriteLong(Other); }
}

/// <summary>Mode 6 (SendRequestItemEnchantMode6 @0x1419c0c70): enchant the peachy (exp) way.</summary>
public sealed record ItemEnchantExpRequest(long Uid) : IOutPacket {
    public Outbound Op => Outbound.RequestItemEnchant;
    public void Write(ByteWriter w) { w.WriteByte(6); w.WriteLong(Uid); }
}

/// <summary>Mode 15 (onForceEnchant) and mode 19: the GM debug buttons (gated by the client's level 0x32 constant).</summary>
public sealed record ItemEnchantDebugRequest(byte Mode) : IOutPacket {
    public Outbound Op => Outbound.RequestItemEnchant;
    public void Write(ByteWriter w) => w.WriteByte(Mode);
}

/// <summary>Debug modes 16 (uid, i32 0 or 10), 17 and 18 (uid): the debug buttons. Extra is written for mode 16 only.</summary>
public sealed record ItemEnchantDebugUidRequest(byte Mode, long Uid, int Extra) : IOutPacket {
    public Outbound Op => Outbound.RequestItemEnchant;
    public void Write(ByteWriter w) { w.WriteByte(Mode); w.WriteLong(Uid); if (Mode == 0x10) w.WriteInt(Extra); }
}

/// <summary>The NpcTalk companion every OnSelectItem sends (uiitemenchantdialog.cpp:746): with a talk active u8 6, i32 npc id,
/// i32 the client constant (m_69a0), i16 1; else u8 4, i16 1; then i64 the item uid. Sent on 0x22 (NpcTalk).</summary>
public sealed record ItemEnchantTalkRequest(bool Talking, int NpcId, int Constant, long Uid) : IOutPacket {
    public Outbound Op => Outbound.NpcTalk;
    public void Write(ByteWriter w) {
        if (Talking) { w.WriteByte(6); w.WriteInt(NpcId); w.WriteInt(Constant); w.WriteShort(1); }
        else { w.WriteByte(4); w.WriteShort(1); }
        w.WriteLong(Uid);
    }
}

/// <summary>Mode 9 (SendRequestItemEnchant @0x1419028d0, uicommon.cpp:1993): the player confirmed the enchant experience refund.</summary>
public sealed record ItemEnchantRefundRequest(long Uid) : IOutPacket {
    public Outbound Op => Outbound.RequestItemEnchant;
    public void Write(ByteWriter w) { w.WriteByte(9); w.WriteLong(Uid); }
}
