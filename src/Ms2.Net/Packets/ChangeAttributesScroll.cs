// Layouts from the restored client: UIItemRemakeScrollDialog::FUN_140f59230 (@0x140f59230, RecvOp 201) and its readers
// FUN_140f5a7e0 / FUN_140f5a9c0 / OnRemakeError (uiitemremakescrolldialog.cpp:356-580), the dialog's sends
// (SendRemakeRequest :516, ApplySelectedOption :702) and the CCItemRemakeOptionScrollPacket stream
// (itemremakescrolldata.cpp:625-680).
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound ChangeAttributesScroll (UIItemRemakeScrollDialog::FUN_140f59230): u8 type, then
/// 0 (the scroll is used): i64 the scroll's uid (the client reads it only while the window is shut, :363);
/// 2 (the new roll, FUN_140f5a7e0) and 3 (the applied item, FUN_140f5a9c0): ApplyOptionClient, i64 uid and the item body
///   (CItem::ReadFromStream into a clone of the target, left to the caller);
/// 4 (OnRemakeError): u8 flag, i32 code.
/// After types 2 and 3 the reader sits after <see cref="Uid"/>.
/// </summary>
public sealed record ChangeAttributesScrollPacket(byte Type) {
    /// <summary>Type 0: the scroll; types 2 and 3: ApplyOptionBase::m_uid, read before the item body.</summary>
    public long Uid { get; init; }
    /// <summary>True when the item body follows in the reader (types 2 and 3).</summary>
    public bool HasBody { get; init; }
    /// <summary>Type 4.</summary>
    public byte Flag { get; init; }
    public int Code { get; init; }

    public static ChangeAttributesScrollPacket Read(ByteReader r) {
        byte type = r.ReadByte();
        var p = new ChangeAttributesScrollPacket(type);
        switch (type) {
            case 0: return p with { Uid = r.ReadLong() };
            case 2: case 3: return p with { Uid = r.ReadLong(), HasBody = true };
            case 4: return p with { Flag = r.ReadByte(), Code = r.ReadInt() };
            default: return p;
        }
    }
}

/// <summary>
/// Outbound ChangeAttributesScroll: u8 kind then the kind's fields.
/// 1 remake (SendRemakeRequest @0x140f59ad0, RemakeOption::vfunc_1 @0x140617a90): i64 scroll uid, i64 item uid,
///   i64 ingredient uid, bool locked, u8 count, then u8 index and i16 value each;
/// 3 apply the new roll (ApplySelectedOption @0x140f5b670, ApplyOptionBase::vfunc_1): i64 uid.
/// </summary>
public sealed record ChangeAttributesScrollRequest(byte Kind, long ScrollUid = 0, long ItemUid = 0, long IngredientUid = 0,
                                                   bool Locked = false, RemakeLock[] Locks = null) : IOutPacket {
    public Outbound Op => Outbound.ChangeAttributesScroll;
    public static ChangeAttributesScrollRequest Remake(long scrollUid, long itemUid, long ingredientUid, bool locked,
                                                       params RemakeLock[] locks) =>
        new(1, scrollUid, itemUid, ingredientUid, locked, locks);
    public static ChangeAttributesScrollRequest Apply(long uid) => new(3, ItemUid: uid);

    public void Write(ByteWriter w) {
        w.WriteByte(Kind);
        switch (Kind) {
            case 1:
                w.WriteLong(ScrollUid);
                w.WriteLong(ItemUid);
                w.WriteLong(IngredientUid);
                w.WriteBool(Locked);
                var locks = Locks ?? Array.Empty<RemakeLock>();
                w.WriteByte((byte)locks.Length);
                foreach (var l in locks) { w.WriteByte(l.Index); w.WriteShort(l.Value); }
                break;
            case 3: w.WriteLong(ItemUid); break;
        }
    }
}
