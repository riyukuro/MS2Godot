// Layouts from the restored client: UIItemRemakeOptionDialog::vfunc_79 (@0x14129daa0, RecvOp 200) and its readers
// FUN_1412a2c50 / FUN_14129f940 / FUN_1412a30f0 / FUN_1412a3330 / FUN_1412a3460 / OnRemakeError
// (unit_14129cfa0_itemremakeoptiondialog.cpp:321-1180), the CCItemRemakeOptionPacket streams (itemremakescrolldata.cpp:456-626)
// and the dialog's sends (SendOp::ChangeAttributes).
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound ChangeAttributes (UIItemRemakeOptionDialog::vfunc_79): u8 kind, then
/// 6 (the previous result, FUN_1412a2c50): u8 result; 0 ends; else i32 the item id; result 2 ends; else i64 the uid to find,
///   then ApplyOptionClient: i64 uid and the new item body (CItem::ReadFromStream, left to the caller);
/// 4 (the item is accepted, FUN_14129f940): i64 uid, CIngredientContainer (u8 count, i32 id, i32 tag, i32 count each);
/// 1 (the remake result, FUN_1412a30f0) and 2 (the applied item, FUN_1412a3330): ApplyOptionClient, i64 uid and the body
///   (left to the caller);
/// 5 (OnRemakeError): u8 flag, i32 code;
/// 8 (the debug resend, FUN_1412a3460): nothing.
/// After kinds 1, 2 and 6 with a body, the reader sits after <see cref="ApplyUid"/>.
/// </summary>
public sealed record ChangeAttributesPacket(byte Kind) {
    /// <summary>Kind 6: the result byte (0 none, 2 the previous item is gone, else there is a result).</summary>
    public byte Result { get; init; }
    /// <summary>Kind 6: the item id the "previous option item not exist" text names.</summary>
    public int ItemId { get; init; }
    /// <summary>Kind 6: the uid the client looks up (FUN_1419be700); kind 4: the accepted item.</summary>
    public long Uid { get; init; }
    /// <summary>Kinds 1, 2 and 6: ApplyOptionBase::m_uid, read before the item body.</summary>
    public long ApplyUid { get; init; }
    /// <summary>True when the item body follows in the reader (kinds 1 and 2, and kind 6 past its early returns).</summary>
    public bool HasBody { get; init; }
    public List<EnchantIngredient> Ingredients { get; init; }
    /// <summary>Kind 5.</summary>
    public byte Flag { get; init; }
    public int Code { get; init; }

    public static ChangeAttributesPacket Read(ByteReader r) {
        byte kind = r.ReadByte();
        var p = new ChangeAttributesPacket(kind);
        switch (kind) {
            case 6:
                byte result = r.ReadByte();
                if (result == 0) return p with { Result = result };
                int id = r.ReadInt();
                if (result == 2) return p with { Result = result, ItemId = id };
                return p with { Result = result, ItemId = id, Uid = r.ReadLong(), ApplyUid = r.ReadLong(), HasBody = true };
            case 4: return p with { Uid = r.ReadLong(), Ingredients = ItemEnchantMessage.ReadIngredients(r) };
            case 1: case 2: return p with { ApplyUid = r.ReadLong(), HasBody = true };
            case 5: return p with { Flag = r.ReadByte(), Code = r.ReadInt() };
            default: return p;
        }
    }
}

/// <summary>A locked option of a remake: u8 the option's index, i16 its value (RemakeOption::vfunc_1).</summary>
public readonly record struct RemakeLock(byte Index, short Value);

/// <summary>
/// Outbound ChangeAttributes: u8 mode then the mode's fields.
/// 0 remake (SendRemakeRequest @0x1412a2380, RemakeOption::vfunc_1): i64 uid, i64 0, u8 count, then u8 index and i16 value each;
/// 2 apply the new options (OnClickApply @0x14129ed90, ApplyOptionBase): i64 uid; 3 keep the old ones: u8 1;
/// 4 select the item (SendChangeAttributesMode4): i64 uid; 6 the dialog opened (OnLoadComplete @0x14129d2c0): u8 1.
/// GMS2: the GMS2 client's 0x76 sends are modes 0 (writer @0x140391d80: the KMS layout), 2 (@0x140392410: i64), 5 (@0x140391fe0)
/// and 6 (@0x140392300: i64, i32, the KMS mode 8 ForceFill); the upstream server's ChangeAttributesHandler reads the same four.
/// It has no keep, select or opened send, so modes 3, 4 and 6 are not sent on GMS2 (a GMS2 6 is ForceFill with an i64 and an i32).
/// STILL-OPEN: the GMS2 dialog's own flow (its mode 5 send and its receive kinds 1, 2, 4, 6 and 9, UIItemRemakeOptionDialog
/// vfunc_79 @0x1409eaaf0) is not ported; GMS2 uses the KMS flow minus those sends, with its own open (state 1, input on).
/// </summary>
public sealed record ChangeAttributesRequest(byte Mode, long Uid = 0, RemakeLock[] Locks = null) : IProfileOutPacket {
    public Outbound Op => Outbound.ChangeAttributes;
    public static ChangeAttributesRequest Remake(long uid, params RemakeLock[] locks) => new(0, uid, locks);
    public static ChangeAttributesRequest Apply(long uid) => new(2, uid);
    public static ChangeAttributesRequest Keep() => new(3);
    public static ChangeAttributesRequest Select(long uid) => new(4, uid);
    public static ChangeAttributesRequest Opened() => new(6);

    public bool SentOn(ProtocolProfile profile) => !profile.IsGms2 || Mode is 0 or 2;

    public void Write(ByteWriter w, ProtocolProfile profile) => Write(w);

    public void Write(ByteWriter w) {
        w.WriteByte(Mode);
        switch (Mode) {
            case 0:
                w.WriteLong(Uid);
                w.WriteLong(0);
                var locks = Locks ?? Array.Empty<RemakeLock>();
                w.WriteByte((byte)locks.Length);
                foreach (var l in locks) { w.WriteByte(l.Index); w.WriteShort(l.Value); }
                break;
            case 2: case 4: w.WriteLong(Uid); break;
            case 3: case 6: w.WriteByte(1); break;
        }
    }
}
