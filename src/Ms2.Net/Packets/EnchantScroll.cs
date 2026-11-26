using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// The item enchant scroll window's packets: UIEnchantScrollDialog (client ms2/uienchantscrolldialog.cpp, UI id 0x4ee0) and its
// EnchantScrollSlot (enchantscrollslot.cpp). Inbound RecvOp EnchantScroll, outbound SendOp EnchantScroll.

/// <summary>
/// One option row of a preview (FUN_140f3d5b0 / FUN_140f3db20, uienchantscrolldialog.cpp:360 / :400): the stat key (u16) and
/// a BaseOption whose value is the rate in thousandths (BaseOption::GetRate @0x140266e10: value / 1000; HasValue
/// @0x140266ce0: value != 0).
/// <para>GMS2 rows carry an f32 rate before the i32 (the retail sniff: u16 0x1b, f32 0.352, i32 0). The GMS2 client's
/// BaseOption is not decompiled. STILL-OPEN: here a GMS2 row has a value when either the rate or the i32 is not 0, and
/// its rate is the f32 plus i32 / 1000.</para>
/// </summary>
public readonly record struct EnchantScrollRow(int Key, int Value, float WireRate) {
    public bool HasValue => Value != 0 || WireRate != 0;
    public float Rate => WireRate + Value / 1000.0f;
}

/// <summary>What a mode 1 read needs from the dialog and the inventory (FUN_140f3bd10 reads the rows only when both items
/// are in the inventory and, for types 1 and 2, the item is not already at the target level).</summary>
public sealed class EnchantScrollReadContext {
    /// <summary>The open scroll (m_nScrollUid).</summary>
    public long ScrollUid;
    /// <summary>m_nTargetLevel.</summary>
    public int TargetLevel;
    /// <summary>g_143ab2a90.FindItemByUid: the inventory item of an uid, null for none.</summary>
    public Func<long, EquipItem> Find = _ => null;
}

/// <summary>Inbound EnchantScroll, CSceneStateGame::OnEnchantScroll (scenestategame.cpp:8081, @0x141734950), by the mode byte.</summary>
public abstract record EnchantScrollMessage {
    /// <summary>
    /// Mode 0, FUN_140f3b920 (uienchantscrolldialog.cpp:199): the scroll uid, its type (u16: 1 enchant, 2 restore, 3 random,
    /// 5 repair), disableBreak, the target level, the fail rate (read as i32 into an unsigned member, then / 100), the level
    /// range (two i16), the equip types and the ranks (each i32 count and i32s), and for type 3 the random range.
    /// </summary>
    public sealed record Open(long ScrollUid, int Type, bool DisableBreak, int TargetLevel, uint FailRate, ushort LevelMin,
        ushort LevelMax, IReadOnlyList<int> EquipTypes, IReadOnlyList<int> Ranks, int RandomMin, int RandomMax) : EnchantScrollMessage;

    /// <summary>
    /// Mode 1, FUN_140f3bd10 (:227): the item put on (i64) and the preview type (u16; the switch reads it, not m_nType).
    /// Shown is false when the scroll or the item is not in the inventory: the client returns with the rest unread (:233).
    /// Rows: types 1 and 2 with the item below or above the target level, the level rows (FUN_140f3d5b0); type 3 the low
    /// rows and High the high rows (FUN_140f3db20); otherwise none read.
    /// </summary>
    public sealed record Preview(long ItemUid, int Type, bool Shown, bool RowsRead, IReadOnlyList<EnchantScrollRow> Rows,
        IReadOnlyList<EnchantScrollRow> High) : EnchantScrollMessage;

    /// <summary>Mode 3, FUN_140f3c6c0 (:268): the result code (u16). Code 0: the item uid and its body (ReadFromStream); Item is
    /// null when the uid is not in the inventory (the body is then unread, :277). Other codes carry nothing.</summary>
    public sealed record Result(int Code, long ItemUid, EquipItem Item) : EnchantScrollMessage;

    /// <summary>Mode 4 (scenestategame.cpp:8100): the reward list for UIRewardNotify (i32 count, then i32 item id and u16 per
    /// entry), then the mastery dialog's refresh. STILL-OPEN: neither is ported; the list is read and dropped.</summary>
    public sealed record Reward(IReadOnlyList<(int ItemId, int Count)> Items) : EnchantScrollMessage;

    /// <summary>Another mode: the client reads nothing more.</summary>
    public sealed record Other(int Mode) : EnchantScrollMessage;

    /// <summary>Reads one message on KMS.</summary>
    public static EnchantScrollMessage Read(ByteReader r, EnchantScrollReadContext ctx) => Read(r, ctx, ProtocolProfile.Kms2525);

    /// <summary>Reads one message for a protocol profile (GMS2: the f32 per row and the item body layout). Every path the client
    /// reads to its end ends with ExpectEnd; the paths it leaves unread return without it.</summary>
    public static EnchantScrollMessage Read(ByteReader r, EnchantScrollReadContext ctx, ProtocolProfile profile) {
        int mode = r.ReadByte();
        switch (mode) {
            case 0: {
                long scroll = r.ReadLong();
                int type = (ushort) r.ReadShort();
                bool disableBreak = r.ReadByte() != 0;
                int target = r.ReadInt();
                uint fail = (uint) r.ReadInt();
                ushort min = (ushort) r.ReadShort(), max = (ushort) r.ReadShort();
                var types = ReadInts(r);
                var ranks = ReadInts(r);
                int rmin = 0, rmax = 0;
                if (type == 3) { rmin = r.ReadInt(); rmax = r.ReadInt(); }
                r.ExpectEnd("EnchantScroll open");
                return new Open(scroll, type, disableBreak, target, fail / 100, min, max, types, ranks, rmin, rmax);
            }
            case 1: {
                long uid = r.ReadLong();
                int type = (ushort) r.ReadShort();
                var none = Array.Empty<EnchantScrollRow>();
                var item = ctx.Find(uid);
                if (item == null || ctx.Find(ctx.ScrollUid) == null) return new Preview(uid, type, false, false, none, none);   // :233
                switch (type) {
                    case 1:
                    case 2:
                        if (item.Enchant.Level == ctx.TargetLevel) return new Preview(uid, type, true, false, none, none);       // :245, unread
                        var rows = ReadRows(r, profile);
                        r.ExpectEnd("EnchantScroll preview");
                        return new Preview(uid, type, true, true, rows, none);
                    case 3: {
                        var low = ReadRows(r, profile);
                        var high = ReadRows(r, profile);
                        r.ExpectEnd("EnchantScroll preview");
                        return new Preview(uid, type, true, true, low, high);
                    }
                    case 5:
                        r.ExpectEnd("EnchantScroll preview");
                        return new Preview(uid, type, true, false, none, none);
                    default:
                        return new Preview(uid, type, true, false, none, none);   // the switch has no case: nothing more read
                }
            }
            case 3: {
                int code = (ushort) r.ReadShort();
                if (code != 0) { r.ExpectEnd("EnchantScroll result"); return new Result(code, 0, null); }
                long uid = r.ReadLong();
                var it = ctx.Find(uid);
                if (it == null) return new Result(0, uid, null);   // :277, the body unread
                var item = ItemReader.ReadBodyCopy(r, it, profile);   // spItem->ReadFromStream, into the bag's item
                r.ExpectEnd("EnchantScroll result");
                return new Result(0, uid, item);
            }
            case 4: {
                int n = r.ReadInt();
                var l = new List<(int, int)>(Math.Max(0, Math.Min(n, 64)));
                for (int i = 0; i < n; i++) l.Add((r.ReadInt(), (ushort) r.ReadShort()));
                // No ExpectEnd: the client reads the list only when RewardNotify 0x4e5d exists and otherwise leaves the body
                // unread (OnEnchantScroll, scenestategame.cpp:8102), so it never checks the end here.
                return new Reward(l);
            }
            default: return new Other(mode);
        }
    }

    static List<int> ReadInts(ByteReader r) {
        int n = r.ReadInt();
        var l = new List<int>(Math.Max(0, Math.Min(n, 64)));
        for (int i = 0; i < n; i++) l.Add(r.ReadInt());
        return l;
    }

    /// <summary>i32 count, then per row u16 key and the i32 value (GMS2: u16 key, f32 rate, i32 value). The client keeps the rows
    /// in a CAtlMap (SetAt: a later key replaces the earlier one); the order the dialog shows them in is the controller's.</summary>
    public static List<EnchantScrollRow> ReadRows(ByteReader r, ProtocolProfile profile) {
        uint n = (uint) r.ReadInt();
        var l = new List<EnchantScrollRow>((int) Math.Min(n, 64u));
        bool gms2 = profile != null && profile.IsGms2;
        for (uint i = 0; i < n; i++) {
            int key = (ushort) r.ReadShort();
            float rate = gms2 ? r.ReadFloat() : 0;
            l.Add(new EnchantScrollRow(key, r.ReadInt(), rate));
        }
        return l;
    }
}

/// <summary>Mode 1, EnchantScrollSlot::vfunc_18 (enchantscrollslot.cpp:95): an inventory item dropped on the slot, with the
/// scroll uid (the slot's owner).</summary>
public sealed record EnchantScrollPutRequest(long ScrollUid, long ItemUid) : IOutPacket {
    public Outbound Op => Outbound.EnchantScroll;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteLong(ScrollUid); w.WriteLong(ItemUid); }
}

/// <summary>Mode 2, UIEnchantScrollDialog::vfunc_28 "onEnchant" (uienchantscrolldialog.cpp:157): enchant the item with the scroll.</summary>
public sealed record EnchantScrollEnchantRequest(long ScrollUid, long ItemUid) : IOutPacket {
    public Outbound Op => Outbound.EnchantScroll;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteLong(ScrollUid); w.WriteLong(ItemUid); }
}
