using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// RecallScroll (RecvOp 0xBC, GMS2 0xBE): CSceneStateGame::OnRecallScroll @0x141734530 (scenestategame.cpp:8038): u8 mode, u8 type
/// (the notify slot: 0 guild, 1 party, 2 wedding). A mode other than 0 is a recall notice: the caller's wide name, three i32 (the
/// second is the field the caller is in; the first and third are read and not used) and the i64 expiry (server time, seconds).
/// Mode 0 closes the type's notice and reads nothing more. The client does not check the type's range here. Both profiles use this
/// layout (no retail packet of it in the KMS2 or GMS2 sniffs).
/// </summary>
public abstract record RecallScrollPacket(byte Type) {
    /// <summary>A mode other than 0: open the type's notice until <paramref name="Expiry"/>.</summary>
    public sealed record Open(byte Mode, byte Type, string Name, int Unused8, int FieldId, int Unused10, long Expiry)
        : RecallScrollPacket(Type);
    /// <summary>Mode 0: close the type's notice.</summary>
    public sealed record Close(byte Type) : RecallScrollPacket(Type);

    public static RecallScrollPacket Read(ByteReader r) {
        byte mode = r.ReadByte(), type = r.ReadByte();
        if (mode == 0) { r.ExpectEnd("RecallScroll close"); return new Close(type); }
        string name = r.ReadUnicodeString();
        int a = r.ReadInt(), field = r.ReadInt(), c = r.ReadInt();
        var open = new Open(mode, type, name, a, field, c, r.ReadLong());
        r.ExpectEnd("RecallScroll open");
        return open;
    }

    /// <summary>The system notice OnRecallScroll shows with an open (type 3): type 0 common string 0xb64, type 1 0xb63, type 2
    /// s_cash_recall_wedding_notice by key; another type an empty text.</summary>
    public static (int Id, string Key) NoticeText(byte type) => type switch {
        0 => (0xb64, null),
        1 => (0xb63, null),
        2 => (0, "s_cash_recall_wedding_notice"),
        _ => (0, null),
    };
}

/// <summary>UIMessageBox::HandleRecallscrollconfirmButton @0x1419fcf60 (uimessagebox.cpp:1641), the confirm box's OK on a recall
/// notice: u8 the slot, the caller's wide name, i32 the field (the box's arguments 0 to 2).</summary>
public sealed record RecallScrollAccept(int Slot, string Name, int FieldId) : IOutPacket {
    public Outbound Op => Outbound.RecallScroll;
    public void Write(ByteWriter w) { w.WriteByte((byte)Slot); w.WriteUnicodeString(Name ?? ""); w.WriteInt(FieldId); }
}
