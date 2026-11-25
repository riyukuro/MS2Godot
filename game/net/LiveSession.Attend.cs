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

    /// <summary>The live run's raw GameEvent add (kind 0) of one AttendGift event (gameeventcontext.cpp:424-443): the server's debug
    /// packet command sends it, as no test server's data holds a running attendance event.</summary>
    internal string AttendEventBytes(int eventId, long begin, long end, int giftA, int giftB)
    {
        var w = new Maple2.PacketLib.Tools.ByteWriter();
        w.WriteShort((short)game.Profile.Number(Ms2.Net.Inbound.GameEvent));
        w.WriteByte(0); w.WriteInt(1);
        w.WriteUnicodeString("AttendGift");
        w.WriteInt(eventId); w.WriteLong(begin); w.WriteLong(end);
        w.WriteUnicodeString("Attend"); w.WriteString("");
        w.WriteBool(false); w.WriteBool(false);
        w.WriteInt(0);
        w.WriteByte(0); w.WriteInt(0);
        w.WriteByte(0);
        w.WriteInt(2);
        foreach (int item in new[] { giftA, giftB }) { w.WriteInt(item); w.WriteShort(1); w.WriteInt(1); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); }
        return string.Join(" ", w.ToArray().Select(b => b.ToString("X2")));
    }

    /// <summary>The live run's raw Attendance kind 7 (select event): the dialog opens on it.</summary>
    internal string AttendSelectBytes(int eventId)
    {
        var w = new Maple2.PacketLib.Tools.ByteWriter();
        w.WriteShort((short)game.Profile.Number(Ms2.Net.Inbound.Attendance));
        w.WriteByte(7); w.WriteInt(eventId);
        return string.Join(" ", w.ToArray().Select(b => b.ToString("X2")));
    }

    /// <summary>The window's requests (Attendance modes 2, 3, 4 and 8).</summary>
    public void SendAttendance(Ms2.Net.Wire.IOutPacket packet) => Send(packet);

    void OnAttendance(AttendanceMessage m)
    {
        if (m is AttendanceMessage.StartedEvents s) Attend.SetTimes(s.Events);
        if (LiveCheck) GD.Print($"live: Attendance {m}");
        AttendDialog.OnAttendance(m);
        AttendanceReceived?.Invoke(m);
    }
}
