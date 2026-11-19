using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// The mail list's sends (UIMailListDialog, client/ms2/uimaillistdialog.cpp): SendOp::Mail 0x2D by mode. The client builds
// each one as COutPacket(SendOp::Mail) and sends it with FUN_140710590.

/// <summary>SendMailMode0 (@0x1419e90d0, uimaillistdialog.cpp:804): ask for the mails (u8 0). The dialog sends it at load
/// and one second after a Mail mode 0xe while it is open.</summary>
public sealed record MailListRequest : IOutPacket {
    public Outbound Op => Outbound.Mail;
    public void Write(ByteWriter w) => w.WriteByte(0);
}

/// <summary>SendMailMode2 (@0x1419e61c0, uimaillistdialog.cpp:555): mark a mail read (u8 2, i64 mail id); sent when a mail
/// whose read time is 0 is selected.</summary>
public sealed record MailReadRequest(long Id) : IOutPacket {
    public Outbound Op => Outbound.Mail;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteLong(Id); }
}

/// <summary>SendMailMode11 (@0x1419e6290, uimaillistdialog.cpp:571): take everything a mail holds (u8 0xb, i64 mail id),
/// the items, money and benefits; sent only when the mail has something to take (CMailInfo::FUN_140690b80). An item and
/// money send.</summary>
public sealed record MailCollectRequest(long Id) : IOutPacket {
    public Outbound Op => Outbound.Mail;
    public void Write(ByteWriter w) { w.WriteByte(0xb); w.WriteLong(Id); }
}

/// <summary>
/// Delete mails (u8 0xd, i32 count, count × i64 mail id): FUN_1419e7940 (uimaillistdialog.cpp:1133) for the checked list,
/// SendMailMode13 (:1161) and FUN_1419e6410 (:1011) for one mail (count 1).
/// </summary>
public sealed record MailDeleteRequest(IReadOnlyList<long> Ids) : IOutPacket {
    public Outbound Op => Outbound.Mail;
    public void Write(ByteWriter w) => MailIds.Write(w, 0xd, Ids);
}

/// <summary>SendMailMode18 (@0x1419e7640, uimaillistdialog.cpp:1096): mark the checked mails read (u8 0x12, i32 count,
/// count × i64 mail id).</summary>
public sealed record MailReadListRequest(IReadOnlyList<long> Ids) : IOutPacket {
    public Outbound Op => Outbound.Mail;
    public void Write(ByteWriter w) => MailIds.Write(w, 0x12, Ids);
}

/// <summary>SendMailMode19 (@0x1419e77c0, uimaillistdialog.cpp:1114): take everything the checked mails hold (u8 0x13, i32
/// count, count × i64 mail id). An item and money send.</summary>
public sealed record MailCollectListRequest(IReadOnlyList<long> Ids) : IOutPacket {
    public Outbound Op => Outbound.Mail;
    public void Write(ByteWriter w) => MailIds.Write(w, 0x13, Ids);
}

static class MailIds {
    /// <summary>Encode1(mode), Encode4(count), then Encode8 of each id.</summary>
    public static void Write(ByteWriter w, byte mode, IReadOnlyList<long> ids) {
        w.WriteByte(mode);
        w.WriteInt(ids.Count);
        foreach (var id in ids) w.WriteLong(id);
    }
}
