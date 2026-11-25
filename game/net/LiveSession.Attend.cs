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

    /// <summary>
    /// The dialog's persistent part (<see cref="Ms2.Ui.AttendGiftState"/>): the events it holds, the request and the selection, made
    /// with the session as the client's DT dialog is made with the game scene. The window (a controller the HUD makes) hooks itself to it.
    /// </summary>
    public Ms2.Ui.AttendGiftState AttendDialog => attendDialog ??= new Ms2.Ui.AttendGiftState(Attend, UserValues, () => ServerTime, EventString);
    Ms2.Ui.AttendGiftState attendDialog;

    /// <summary>The server's game event strings (stringgameevent.xml) by key: an event's name.</summary>
    public static Func<string, string> EventStringOf;
    string EventString(string key) => EventStringOf?.Invoke(key) ?? key;

    /// <summary>Unk_Inventory::FUN_140a5e2e0 (inventory.cpp:650): the uid of the bag's first usable item of an id, 0 for none (the pay box's coupon).</summary>
    public long FirstItemUid(int itemId)
    {
        if (ItemDataOf(0) == null && itemData == null) return 0;
        return Ms2.Ui.Items.InventoryCount.FirstUidById(Items.All, itemData, itemId, CharacterId, ServerTime);
    }

    /// <summary>The window's requests (Attendance modes 2, 3, 4 and 8).</summary>
    public void SendAttendance(Ms2.Net.Wire.IOutPacket packet) => Send(packet);

    void OnAttendance(AttendanceMessage m)
    {
        if (m is AttendanceMessage.StartedEvents s) Attend.SetTimes(s.Events);
        GD.Print($"live: Attendance {m}");
        AttendDialog.OnAttendance(m);
        AttendanceReceived?.Invoke(m);
    }
}
