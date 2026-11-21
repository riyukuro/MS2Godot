using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// The guild dialog's sends (CGuildManager, client/ms2/guildmanager.cpp): SendOp::Guild 0x4B by mode. The client builds each
// one as COutPacket(SendOp::Guild) and sends it with FUN_140710590. The checks before each send are in GuildBook.

/// <summary>FUN_1413b6d10 (:405): create a guild (u8 1, the name).</summary>
public sealed record GuildCreateRequest(string Name) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteUnicodeString(Name); }
}

/// <summary>SendGuildMode82 (@0x1413bc4a0): ask to join a guild (u8 0x52, i64 guild id).</summary>
public sealed record GuildJoinRequestSend(long GuildId) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(0x52); w.WriteLong(GuildId); }
}

/// <summary>SendGuildMode83 (@0x1413bc540): cancel a join request (u8 0x53, i64). The dialog's OnRequestCancelJoin passes
/// the request's id (requestSn), though the client names the parameter a guild id.</summary>
public sealed record GuildJoinCancelRequest(long Id) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(0x53); w.WriteLong(Id); }
}

/// <summary>SendGuildMode86 (@0x1413bc6a0): ask for this character's join requests (u8 0x56), answered by type 0x56.</summary>
public sealed record GuildMyRequestsRequest : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => w.WriteByte(0x56);
}

/// <summary>SendGuildMode87 (@0x1413bc710): search guilds by propensity (u8 0x57, then CGuildJoinRequestSchCond::Write
/// @0x1404c8560: i32 the focus f18, i32 f1c, which the client sets to 1), answered by type 0x57.</summary>
public sealed record GuildSearchRequest(int Focus, int Value = 1) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(0x57); w.WriteInt(Focus); w.WriteInt(Value); }
}

/// <summary>SendGuildMode88 (@0x1413bc860): search guilds by name (u8 0x58, the name).</summary>
public sealed record GuildSearchByNameRequest(string Name) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(0x58); w.WriteUnicodeString(Name); }
}

/// <summary>SendGuildMode84 (@0x1413bc5b0): answer a join request sent to the guild (u8 0x54, i64 the request id, u8
/// accept). The accept page hands the movie both the request id ("requestSn") and the character id ("charSn") and the
/// movie passes back the request id.</summary>
public sealed record GuildRespondRequest(long Id, bool Accept) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(0x54); w.WriteLong(Id); w.WriteByte(Accept ? (byte)1 : (byte)0); }
}

/// <summary>SendGuildMode85 (@0x1413bc630): ask for the join requests sent to the guild (u8 0x55, no body), answered by
/// type 0x55. The client sends it from a member with the invite right when the local player is complete.</summary>
public sealed record GuildListRequestsRequest : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => w.WriteByte(0x55);
}

/// <summary>SendGuildMode5 (@0x1413b74f0, :484): answer the invite received (u8 5, the invite as read, u8 accept).</summary>
public sealed record GuildInviteAnswerRequest(GuildInviteInfo Invite, bool Accept) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(5); Invite.Write(w); w.WriteByte(Accept ? (byte)1 : (byte)0); }
}

/// <summary>SendGuildMode15 (@0x1413b8230): attend (u8 0xf, no body).</summary>
public sealed record GuildAttendRequest : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => w.WriteByte(0xf);
}

/// <summary>CGuildManager::FUN_1413b8540 (@0x1413b8540): donate (u8 0x71, i32 the donation count).</summary>
public sealed record GuildDonateRequest(int Amount) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(0x71); w.WriteInt(Amount); }
}

/// <summary>SendGuildMode7 (@0x1413b75a0): leave the guild (u8 7, no body). The client sends it only from a member who
/// is not the master.</summary>
public sealed record GuildLeaveRequest : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => w.WriteByte(7);
}

/// <summary>SendGuildMode2 (@0x1413b7020): disband the guild (u8 2, no body). The client sends it only from the master
/// of a one-member guild.</summary>
public sealed record GuildBreakRequest : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => w.WriteByte(2);
}

/// <summary>FUN_1413b71c0 (@0x1413b71c0, :454): invite a character to the guild (u8 3, the name).</summary>
public sealed record GuildInviteRequest(string Name) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(3); w.WriteUnicodeString(Name); }
}

/// <summary>SendGuildMode8 (@0x1413b76c0): expel a member (u8 8, the name).</summary>
public sealed record GuildExpelRequest(string Name) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(8); w.WriteUnicodeString(Name); }
}

/// <summary>SendGuildMode10 (@0x1413b78c0): set a member's grade (u8 10, the name, u8 the grade).</summary>
public sealed record GuildMemberGradeRequest(string Name, int Grade) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(10); w.WriteUnicodeString(Name); w.WriteByte((byte)Grade); }
}

/// <summary>SendGuildMode63 (@0x1413b7ad0): hand the master over to a member (u8 0x3f, the name).</summary>
public sealed record GuildChangeMasterRequest(string Name) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(0x3f); w.WriteUnicodeString(Name); }
}

/// <summary>FUN_1413b7c70 (@0x1413b7c70): a guild text (u8 0x40, u8 the kind, the text). The dialog's OnChangeNotify
/// sends kind 1, the notice.</summary>
public sealed record GuildTextRequest(int Kind, string Text) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(0x40); w.WriteByte((byte)Kind); w.WriteUnicodeString(Text); }
}

/// <summary>SendGuildMode13 (@0x1413b80d0): this member's memo (u8 0xd, the text).</summary>
public sealed record GuildMemoRequest(string Text) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(0xd); w.WriteUnicodeString(Text); }
}

/// <summary>SendGuildMode68 (@0x1413b8e60): the guild's propensity (u8 0x44, u8 the focus, i32 the value). The client
/// names the two CGuildManager f154 (the "listed in the search" flag of OnChageRegistSearch) and f158 (GetPropensity).</summary>
public sealed record GuildPropensityRequest(int Focus, int Value) : IOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) { w.WriteByte(0x44); w.WriteByte((byte)Focus); w.WriteInt(Value); }
}
