using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound 0x11B (RecvOp::Lapenshard), CSceneStateGame::OnLapenshard @0x14173d2e0 (scenestategame.cpp:8743), by its u8 mode:
/// 0 the worn lapenshards, i32 count of { i32 slot, i32 item id } (ClearLapenshards first, then EquipLapenshard(slot, id, 1));
/// 1 one worn (Unk_LapenEquipped @0x1405b5dd0: i32 slot, i32 item id); 2 one removed (Unk_LapenUnequip @0x1405b5e70: i32 slot).
/// The dialog's upgrade tab reads the same packet in its vfunc_9: mode 4 the success rate (Unk_LapenPreview @0x1405b6360: i32 rate),
/// mode 5 the result (Unk_LapenUpgradeResult @0x1405b6220: i64 uid, i32 item id, i32 slot, bool success), mode 6 with no body.
/// Other modes are not read by the client.
/// </summary>
public abstract record LapenshardMessage {
    public sealed record Worn(IReadOnlyList<(int Slot, int ItemId)> Entries) : LapenshardMessage;
    public sealed record Equipped(int Slot, int ItemId) : LapenshardMessage;
    public sealed record Unequipped(int Slot) : LapenshardMessage;
    public sealed record Preview(int Rate) : LapenshardMessage;
    public sealed record UpgradeResult(long Uid, int ItemId, int Slot, bool Success) : LapenshardMessage;
    /// <summary>Mode 6: the upgrade tab's event 9, no body.</summary>
    public sealed record UpgradeDone : LapenshardMessage;

    public static LapenshardMessage Read(ByteReader r) {
        switch (r.ReadByte()) {
            case 0: {
                uint n = (uint) r.ReadInt();
                var list = new List<(int, int)>();
                for (uint i = 0; i < n; i++) { int slot = r.ReadInt(); list.Add((slot, r.ReadInt())); }
                return new Worn(list);
            }
            case 1: { int slot = r.ReadInt(); return new Equipped(slot, r.ReadInt()); }
            case 2: return new Unequipped(r.ReadInt());
            case 4: return new Preview(r.ReadInt());
            case 5: { long uid = r.ReadLong(); int item = r.ReadInt(); int slot = r.ReadInt(); return new UpgradeResult(uid, item, slot, r.ReadByte() != 0); }
            case 6: return new UpgradeDone();
        }
        return null;
    }
}

/// <summary>Outbound 0xAD (SendOp::Lapenshard) mode 1: UILapenshardSlot drop, SendLapenshard (Unk_LapenEquip::Write @0x1405b5cc0):
/// u8 1, i32 slot, i64 item uid.</summary>
public sealed record LapenshardEquip(int Slot, long Uid) : IOutPacket {
    public Outbound Op => Outbound.Lapenshard;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteInt(Slot); w.WriteLong(Uid); }
}

/// <summary>Outbound 0xAD mode 2: UILapenshardEquipTab::OnUnmountEquipSlot (Unk_LapenUnequip::Write @0x1405b5e40): u8 2, i32 slot.</summary>
public sealed record LapenshardUnequip(int Slot) : IOutPacket {
    public Outbound Op => Outbound.Lapenshard;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteInt(Slot); }
}

/// <summary>Outbound 0xAD mode 3: the upgrade tab asks for the success rate (Unk_LapenItem::Write @0x1405b5ec0): u8 3, i64 uid,
/// i32 item id, i32 slot.</summary>
public sealed record LapenshardPreviewRequest(long Uid, int ItemId, int Slot) : IOutPacket {
    public Outbound Op => Outbound.Lapenshard;
    public void Write(ByteWriter w) { w.WriteByte(3); w.WriteLong(Uid); w.WriteInt(ItemId); w.WriteInt(Slot); }
}

/// <summary>Outbound 0xAD mode 4: the upgrade tab adds fodder (Unk_LapenAddFodder::Write @0x1405b6280): u8 4, Unk_LapenItem, i32 amount.</summary>
public sealed record LapenshardAddFodder(long Uid, int ItemId, int Slot, int Amount) : IOutPacket {
    public Outbound Op => Outbound.Lapenshard;
    public void Write(ByteWriter w) { w.WriteByte(4); w.WriteLong(Uid); w.WriteInt(ItemId); w.WriteInt(Slot); w.WriteInt(Amount); }
}

/// <summary>Outbound 0xAD mode 5: the upgrade (Unk_LapenUpgrade::Write @0x1405b5fd0): u8 5, Unk_LapenItem, i32 count of
/// { i64 uid, i32 amount }, then the i32 tail (the tab's m_n_c0).</summary>
public sealed record LapenshardUpgrade(long Uid, int ItemId, int Slot, IReadOnlyList<(long Uid, int Amount)> Fodder, int Tail) : IOutPacket {
    public Outbound Op => Outbound.Lapenshard;
    public void Write(ByteWriter w) {
        w.WriteByte(5); w.WriteLong(Uid); w.WriteInt(ItemId); w.WriteInt(Slot); w.WriteInt(Fodder.Count);
        foreach (var (u, a) in Fodder) { w.WriteLong(u); w.WriteInt(a); }
        w.WriteInt(Tail);
    }
}
