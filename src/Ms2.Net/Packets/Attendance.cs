using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound Attendance (RecvOp 0xA4; GMS2 0xA6). The client's UIAttendGiftDialog registers it in its constructor (@0x14102b4d0,
/// uiattendgiftdialog.cpp:201) and OnAttendancePacket (@0x14102EBB0, :345-354) reads a u8 kind, then
/// 5 (RefreshAll, @0x14102ec60): no body;
/// 6 (the started events, FUN_14102ec90 @0x14102ec90 into CMyCharacter::FUN_140d9f1c0 @0x140d9f1c0 and FUN_140d9fc80
///   @0x140d9fc80, notifydebugstate.cpp:133-150): i32 count; each: i32 event id, i64 time (the end time of an item event the
///   user started); each is set in the character's map (a repeated id replaces, ids not in the packet stay);
/// 7 (select an event, FUN_14102ecc0 @0x14102ecc0, :363-372): u32 event id;
/// 9 (an error, FUN_14102ed90 @0x14102ed90, :603-614): u8 result.
/// Any other kind reads nothing (the switch has no other case) and the rest of the body stays unread.
/// </summary>
public abstract record AttendanceMessage
{
    /// <summary>Kind 5.</summary>
    public sealed record RefreshAll : AttendanceMessage;
    /// <summary>Kind 6: (event id, end time) pairs in packet order.</summary>
    public sealed record StartedEvents(IReadOnlyList<(int EventId, long EndTime)> Events) : AttendanceMessage;
    /// <summary>Kind 7: the event the server asks the dialog to show.</summary>
    public sealed record SelectEvent(uint EventId) : AttendanceMessage;
    /// <summary>Kind 9: the result code (1 lackMoney, 2 lackMerat, 3 hasNotCoupon, 5 item_attend_already_used, 6 item_attend_not_found_evnet; any other asserts in the client).</summary>
    public sealed record Error(byte Result) : AttendanceMessage;
    /// <summary>A kind the client's switch does not handle.</summary>
    public sealed record Other(byte Kind) : AttendanceMessage;

    public static AttendanceMessage Read(ByteReader r)
    {
        byte kind = r.ReadByte();
        AttendanceMessage m;
        switch (kind)
        {
            case 5: m = new RefreshAll(); break;
            case 6:
            {
                uint n = (uint)r.ReadInt();
                var list = new List<(int, long)>();
                for (uint i = 0; i < n; i++) list.Add((r.ReadInt(), r.ReadLong()));
                m = new StartedEvents(list);
                break;
            }
            case 7: m = new SelectEvent((uint)r.ReadInt()); break;
            case 9: m = new Error(r.ReadByte()); break;
            default: return new Other(kind);
        }
        r.ExpectEnd("Attendance");
        return m;
    }
}

/// <summary>SendOp::Attendance (0x66; GMS2 0x67) mode 2 (uiattendgiftdialog.cpp:419, OnLoadComplete @0x14102bb00): the dialog loaded.
/// The client sends it only when the user value table's bool 100 (FUN_140482280(100)) is true.</summary>
public sealed record AttendanceWindowOpened : IOutPacket
{
    public Outbound Op => Outbound.Attendance;
    public void Write(ByteWriter w) => w.WriteByte(2);
}

/// <summary>SendOp::Attendance mode 3 (FUN_14102f080 @0x14102f080, :375-378): the merat pay attend was chosen.</summary>
public sealed record AttendanceMeratPay : IOutPacket
{
    public Outbound Op => Outbound.Attendance;
    public void Write(ByteWriter w) => w.WriteByte(3);
}

/// <summary>SendOp::Attendance mode 4 (OnAttendanceMsgBox @0x14102d4e0, :466-480): the early participation answered yes: i32 event id,
/// i64 coupon item uid (0 when the box's checkbox was not checked).</summary>
public sealed record AttendanceEarlyParticipation(int EventId, long CouponUid) : IOutPacket
{
    public Outbound Op => Outbound.Attendance;
    public void Write(ByteWriter w) { w.WriteByte(4); w.WriteInt(EventId); w.WriteLong(CouponUid); }
}

/// <summary>SendOp::Attendance mode 8 (SendSelectedEventRequest @0x14102f0e0, :381-388): the selected event's id.</summary>
public sealed record AttendanceSelectEvent(int EventId) : IOutPacket
{
    public Outbound Op => Outbound.Attendance;
    public void Write(ByteWriter w) { w.WriteByte(8); w.WriteInt(EventId); }
}
