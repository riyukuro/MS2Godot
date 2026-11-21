// Layouts from the restored client's CItemBreakManager (commonfiles/itembreakmanager.cpp): OnItemDismantlePacket :373
// (RecvOp 0x96) and the SendItemDismantleMode* writers (:321, :338, :429, :444, :447, :451; SendOp 0x5A). The reader is
// Unk_BreakInput (itembreakmanager.h:9): vfunc_1 u8, vfunc_2 u16, vfunc_3 u32, vfunc_4 i64.
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>One slot of mode 1 / 6: the break list index (u16), the item uid (i64) and the amount (u32).</summary>
public readonly record struct DismantleSlot(ushort Index, long Uid, int Amount);

/// <summary>
/// Inbound 0x96 (OnItemDismantlePacket): u8 mode, then
/// 1: i64 uid, u16 index, u32 count (one slot set); 2: i64 uid (the slot cleared); 3: u8 type, then for type 1 the packet goes
/// on to UIItemBreakDialog::vfunc_84 (uiinventorydialog.cpp:2720): u32 count and per entry i32 item id, i32 amount (the
/// donation dialog's vfunc_84, :3000, reads nothing, so type 2 ends after the type);
/// 4: u32 result (the dismantle's outcome notice); 5: the preview list (UIItemBreakDialog::FUN_1419a44d0): u32 count and per
/// entry u32 item id, u32 low, u32 high; 6: u32 count and per slot u16 index, i64 uid, u32 amount.
/// </summary>
public sealed record ItemDismantlePacket(byte Mode) {
    public long Uid { get; init; }
    public DismantleSlot[] Slots { get; init; }
    public byte Type { get; init; }
    public int Result { get; init; }
    public DismantlePreview[] Previews { get; init; }
    /// <summary>Mode 3, type 1: the reward rows (item id, amount) of the finished break; rows with amount 0 are kept here and skipped by the dialog.</summary>
    public DismantleReward[] Rewards { get; init; }

    /// <summary>A u32 count bounded by the bytes left (each entry takes at least <paramref name="entryBytes"/>), so a bad count cannot allocate.</summary>
    static int ReadCount(ByteReader r, int entryBytes) => (int)Math.Min((uint)r.ReadInt(), (uint)(r.Available / entryBytes));

    public static ItemDismantlePacket Read(ByteReader r) {
        byte mode = r.ReadByte();
        var p = new ItemDismantlePacket(mode);
        switch (mode) {
            case 1:
                long uid = r.ReadLong(); ushort idx = (ushort)r.ReadShort(); int count = r.ReadInt();
                return p with { Uid = uid, Slots = new[] { new DismantleSlot(idx, uid, count) } };
            case 2: return p with { Uid = r.ReadLong() };
            case 3:
                byte type = r.ReadByte();
                if (type != 1) return p with { Type = type };
                var rw = new DismantleReward[ReadCount(r, 8)];
                for (int i = 0; i < rw.Length; i++) rw[i] = new(r.ReadInt(), r.ReadInt());
                return p with { Type = type, Rewards = rw };
            case 4: return p with { Result = r.ReadInt() };
            case 5:
                var pv = new DismantlePreview[ReadCount(r, 12)];
                for (int i = 0; i < pv.Length; i++) pv[i] = new(r.ReadInt(), r.ReadInt(), r.ReadInt());
                return p with { Previews = pv };
            case 6:
                var s = new DismantleSlot[ReadCount(r, 14)];
                for (int i = 0; i < s.Length; i++) s[i] = new((ushort)r.ReadShort(), r.ReadLong(), r.ReadInt());
                return p with { Slots = s };
            default: return p;
        }
    }
}

/// <summary>One reward row of a finished break (UIItemBreakDialog::vfunc_84): the item id and the amount.</summary>
public readonly record struct DismantleReward(int ItemId, int Amount);

/// <summary>One preview row (BreakPreview::FUN_140530920): the item id, then low and high amounts.</summary>
public readonly record struct DismantlePreview(int ItemId, int Low, int High);

/// <summary>Outbound 0x5A (SendOp::ItemDismantle): u8 mode then 0: u8 type (SendItemDismantleMode0); 1: i32 arg, i64 uid,
/// i32 count (Mode1); 2: i64 uid (remove); 3: nothing (Mode3); 6: u8 a, u8 b (Mode6).</summary>
public sealed record ItemDismantleRequest(byte Mode, int Arg = 0, long Uid = 0, int Count = 0, byte B = 0) : IOutPacket {
    public Outbound Op => Outbound.ItemDismantle;
    public static ItemDismantleRequest SetMode(byte type) => new(0, Arg: type);
    public static ItemDismantleRequest AddItem(long uid, int count, int arg) => new(1, arg, uid, count);
    public static ItemDismantleRequest RemoveItem(long uid) => new(2, Uid: uid);
    public static ItemDismantleRequest Start() => new(3);
    public static ItemDismantleRequest Options(byte a, byte b) => new(6, a, B: b);

    public void Write(ByteWriter w) {
        w.WriteByte(Mode);
        switch (Mode) {
            case 0: w.WriteByte((byte)Arg); break;
            case 1: w.WriteInt(Arg); w.WriteLong(Uid); w.WriteInt(Count); break;
            case 2: w.WriteLong(Uid); break;
            case 6: w.WriteByte((byte)Arg); w.WriteByte(B); break;
        }
    }
}
