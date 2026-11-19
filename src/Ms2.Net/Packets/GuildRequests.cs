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
