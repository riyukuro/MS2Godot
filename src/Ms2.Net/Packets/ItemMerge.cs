// Layouts from the restored client: CSceneStateGame::OnItemFusion (scenestategame.cpp:5906, RecvOp 0x74), the
// UIItemMergeOptionDialog readers FUN_1419d0ef0 / FUN_1419d11b0 / FUN_1419d12f0 (uiitemmergeoptiondialog.cpp:799-860),
// CItemMergeOptionProxy::Read (itemmergeoptiontable.cpp:245), CIngredientContainer::Read (commonstruct_wa.cpp:255) and the
// Request* writers (uiitemmergeoptiondialog.cpp:972-1022, uiitemmergerevertdialog.cpp:155; SendOp 0x3F).
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>CIngredientInfo::FUN_14026b410 (commonstruct_wa.cpp:137): three i32 (m_a, m_b, m_c).</summary>
public readonly record struct MergeIngredient(int A, int B, int C) {
    public static MergeIngredient Read(ByteReader r) => new(r.ReadInt(), r.ReadInt(), r.ReadInt());
}

/// <summary>An option's range: i16 key, i32 min, i32 max (CItemMergeOptionProxy::Read).</summary>
public readonly record struct MergeOptionRange(ushort Key, int Min, int Max);

/// <summary>CItemMergeOptionProxy::Read (itemmergeoptiontable.cpp:245): i64 meso, u8 count + ingredients, u32 count + i32
/// values, u32 count + normal ranges, u32 count + special ranges.</summary>
public sealed record MergeOptionProxy(long Meso, MergeIngredient[] Ingredients, int[] Values,
    MergeOptionRange[] Normal, MergeOptionRange[] Special) {
    public static MergeOptionProxy Read(ByteReader r) {
        long meso = r.ReadLong();
        var ing = new MergeIngredient[r.ReadByte()];
        for (int i = 0; i < ing.Length; i++) ing[i] = MergeIngredient.Read(r);
        var values = new int[(int)Math.Min((uint)r.ReadInt(), (uint)(r.Available / 4))];
        for (int i = 0; i < values.Length; i++) values[i] = r.ReadInt();
        return new(meso, ing, values, Ranges(r), Ranges(r));
    }

    static MergeOptionRange[] Ranges(ByteReader r) {
        var a = new MergeOptionRange[(int)Math.Min((uint)r.ReadInt(), (uint)(r.Available / 10))];
        for (int i = 0; i < a.Length; i++) a[i] = new((ushort)r.ReadShort(), r.ReadInt(), r.ReadInt());
        return a;
    }
}

/// <summary>
/// Inbound 0x74 (OnItemFusion): u8 mode, then
/// 0: u8 sub (0 = phase 1; else u32 id, sub 2 = missing item, otherwise i64 uid, i32 normal, i32 special, i32 value);
/// 1: u32 count + i64 material uids; 2: <see cref="MergeOptionProxy"/>; 4: i32 normal, i32 special, i32 value;
/// 5: i16 error, when 0 an i64 uid and the item body (CItem::ReadFromStream, left to the caller);
/// 6: i64 uid and the item body (left to the caller). After modes 5 and 6 the reader sits after the uid.
/// </summary>
public sealed record ItemMergePacket(byte Mode) {
    public byte Sub { get; init; }
    public int ItemId { get; init; }
    public long Uid { get; init; }
    public int Normal { get; init; }
    public int Special { get; init; }
    public int Value { get; init; }
    public long[] Materials { get; init; }
    public MergeOptionProxy Proxy { get; init; }
    public ushort Error { get; init; }

    public static ItemMergePacket Read(ByteReader r) {
        byte mode = r.ReadByte();
        var p = new ItemMergePacket(mode);
        switch (mode) {
            case 0:
                byte sub = r.ReadByte();
                if (sub == 0) return p with { Sub = sub };
                int id = r.ReadInt();
                if (sub == 2) return p with { Sub = sub, ItemId = id };
                return p with { Sub = sub, ItemId = id, Uid = r.ReadLong(), Normal = r.ReadInt(), Special = r.ReadInt(), Value = r.ReadInt() };
            case 1:
                var m = new long[(int)Math.Min((uint)r.ReadInt(), (uint)(r.Available / 8))];
                for (int i = 0; i < m.Length; i++) m[i] = r.ReadLong();
                return p with { Materials = m };
            case 2: return p with { Proxy = MergeOptionProxy.Read(r) };
            case 4: return p with { Normal = r.ReadInt(), Special = r.ReadInt(), Value = r.ReadInt() };
            case 5:
                ushort err = (ushort)r.ReadShort();
                return err == 0 ? p with { Error = err, Uid = r.ReadLong() } : p with { Error = err };
            case 6: return p with { Uid = r.ReadLong() };
            default: return p;
        }
    }
}

/// <summary>Outbound 0x3F (SendOp::ItemMerge): u8 mode then the mode's fields. 0 info (RequestInfo); 1 selection (i64 item);
/// 2 preview, 3 merge, 7 material (i64 item, i64 material); 4 choice (u8); 6 revert (i64 item, i64 uid).</summary>
public sealed record ItemMergeRequest(byte Mode, long ItemUid = 0, long OtherUid = 0, byte Choice = 0) : IOutPacket {
    public Outbound Op => Outbound.ItemMerge;
    public static ItemMergeRequest Info() => new(0);
    public static ItemMergeRequest Selection(long item) => new(1, item);
    public static ItemMergeRequest Preview(long item, long material) => new(2, item, material);
    public static ItemMergeRequest Merge(long item, long material) => new(3, item, material);
    public static ItemMergeRequest ChoiceOf(byte choice) => new(4, Choice: choice);
    public static ItemMergeRequest Revert(long item, long uid) => new(6, item, uid);
    public static ItemMergeRequest Material(long item, long material) => new(7, item, material);

    public void Write(ByteWriter w) {
        w.WriteByte(Mode);
        switch (Mode) {
            case 1: w.WriteLong(ItemUid); break;
            case 2: case 3: case 6: case 7: w.WriteLong(ItemUid); w.WriteLong(OtherUid); break;
            case 4: w.WriteByte(Choice); break;
        }
    }
}

/// <summary>Outbound 0x22 (SendOp::NpcTalk) as UIItemMergeOptionDialog::RequestResultTalk (uiitemmergeoptiondialog.cpp:1028) sends it:
/// with a talk open u8 6, i32 the talk npc id, i32 the client constant (m_talk), i16 0xca, i16 the talk id; without one u8 4,
/// i16 0xca, i16 the talk id.</summary>
public sealed record ItemMergeResultTalkRequest(bool InTalk, int TalkNpcId, int TalkConstant, short TalkId) : IOutPacket {
    public Outbound Op => Outbound.NpcTalk;
    public void Write(ByteWriter w) {
        if (InTalk) { w.WriteByte(6); w.WriteInt(TalkNpcId); w.WriteInt(TalkConstant); }
        else w.WriteByte(4);
        w.WriteShort(0xca);
        w.WriteShort(TalkId);
    }
}
