using Godot;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>
/// The Attendance packet (RecvOp 0xA4, GMS2 0xA6) into the attend gift dialog's data (UIAttendGiftDialog::OnAttendancePacket
/// @0x14102EBB0, uiattendgiftdialog.cpp:345-354). The client registers the handler in the dialog's constructor (:201) and the
/// dialog is a DT dialog the game scene keeps (FindDTDialog&lt;UIAttendGiftDialog&gt;(0x4eb8), gameeventcli.cpp:406), so the packet is
/// handled whether the window is shown or not: kind 6 sets the character's times (CMyCharacter m_times); kinds 5, 7 and 9 act on
/// the dialog, so they are handed to it through <see cref="AttendanceReceived"/>.
/// STILL-OPEN: when the game scene builds the DT dialog (the packets before that have no handler).
/// </summary>
public partial class LiveSession
{
    /// <summary>Kinds 5, 7 and 9 of the Attendance packet for the open attend gift dialog.</summary>
    public event Action<AttendanceMessage> AttendanceReceived;

    void OnAttendance(AttendanceMessage m)
    {
        if (m is AttendanceMessage.StartedEvents s) Attend.SetTimes(s.Events);
        GD.Print($"live: Attendance {m}");
        AttendanceReceived?.Invoke(m);
    }
}
