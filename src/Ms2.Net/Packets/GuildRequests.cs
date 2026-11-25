using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// The guild dialog's sends (CGuildManager, client/ms2/guildmanager.cpp): SendOp::Guild 0x4B by mode. Each record writes
// the KMS mode through GuildModes.Send, which gives the GMS2 client's number on a GMS2 session. The client builds each
// one as COutPacket(SendOp::Guild) and sends it with FUN_140710590. The checks before each send are in GuildBook.

/// <summary>FUN_1413b6d10 (:405): create a guild (u8 1, the name).</summary>
public sealed record GuildCreateRequest(string Name) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(1, profile)); w.WriteUnicodeString(Name); }
}

/// <summary>SendGuildMode82 (@0x1413bc4a0): ask to join a guild (u8 0x52, i64 guild id).</summary>
public sealed record GuildJoinRequestSend(long GuildId) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x52, profile)); w.WriteLong(GuildId); }
}

/// <summary>SendGuildMode83 (@0x1413bc540): cancel a join request (u8 0x53, i64). The dialog's OnRequestCancelJoin passes
/// the request's id (requestSn), though the client names the parameter a guild id.</summary>
public sealed record GuildJoinCancelRequest(long Id) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x53, profile)); w.WriteLong(Id); }
}

/// <summary>SendGuildMode86 (@0x1413bc6a0): ask for this character's join requests (u8 0x56), answered by type 0x56.</summary>
public sealed record GuildMyRequestsRequest : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) => w.WriteByte(GuildModes.Send(0x56, profile));
}

/// <summary>SendGuildMode87 (@0x1413bc710): search guilds by propensity (u8 0x57, then CGuildJoinRequestSchCond::Write
/// @0x1404c8560: i32 the focus f18, i32 f1c, which the client sets to 1), answered by type 0x57.</summary>
public sealed record GuildSearchRequest(int Focus, int Value = 1) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x57, profile)); w.WriteInt(Focus); w.WriteInt(Value); }
}

/// <summary>SendGuildMode88 (@0x1413bc860): search guilds by name (u8 0x58, the name).</summary>
public sealed record GuildSearchByNameRequest(string Name) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x58, profile)); w.WriteUnicodeString(Name); }
}

/// <summary>SendGuildMode84 (@0x1413bc5b0): answer a join request sent to the guild (u8 0x54, i64 the request id, u8
/// accept). The accept page hands the movie both the request id ("requestSn") and the character id ("charSn") and the
/// movie passes back the request id.</summary>
public sealed record GuildRespondRequest(long Id, bool Accept) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x54, profile)); w.WriteLong(Id); w.WriteByte(Accept ? (byte)1 : (byte)0); }
}

/// <summary>SendGuildMode85 (@0x1413bc630): ask for the join requests sent to the guild (u8 0x55, no body), answered by
/// type 0x55. The client sends it from a member with the invite right when the local player is complete.</summary>
public sealed record GuildListRequestsRequest : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) => w.WriteByte(GuildModes.Send(0x55, profile));
}

/// <summary>SendGuildMode5 (@0x1413b74f0, :484): answer the invite received (u8 5, the invite as read, u8 accept).</summary>
public sealed record GuildInviteAnswerRequest(GuildInviteInfo Invite, bool Accept) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(5, profile)); Invite.Write(w); w.WriteByte(Accept ? (byte)1 : (byte)0); }
}

/// <summary>SendGuildMode15 (@0x1413b8230): attend (u8 0xf, no body).</summary>
public sealed record GuildAttendRequest : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) => w.WriteByte(GuildModes.Send(0xf, profile));
}

/// <summary>CGuildManager::FUN_1413b8540 (@0x1413b8540): donate (u8 0x71, i32 the donation count).</summary>
public sealed record GuildDonateRequest(int Amount) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x71, profile)); w.WriteInt(Amount); }
}

/// <summary>CGuildManager::FUN_1413b8fe0 (@0x1413b8fe0, guildmanager.cpp:743): use a guild skill (u8 0x5a, i32 skill id).</summary>
public sealed record GuildSkillUseRequest(int SkillId) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x5a, profile)); w.WriteInt(SkillId); }
}

/// <summary>CGuildManager::FUN_1413b96e0 (@0x1413b96e0): use a personal guild skill (u8 0x5b, i32 skill id).</summary>
public sealed record GuildPersonalSkillUseRequest(int SkillId) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x5b, profile)); w.WriteInt(SkillId); }
}

/// <summary>CGuildManager::FUN_1413b9ac0 (@0x1413b9ac0): upgrade a guild skill (u8 0x5c, i32 skill id).</summary>
public sealed record GuildSkillUpgradeRequest(int SkillId) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x5c, profile)); w.WriteInt(SkillId); }
}

/// <summary>SendGuildMode7 (@0x1413b75a0): leave the guild (u8 7, no body). The client sends it only from a member who
/// is not the master.</summary>
public sealed record GuildLeaveRequest : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) => w.WriteByte(GuildModes.Send(7, profile));
}

/// <summary>SendGuildMode2 (@0x1413b7020): disband the guild (u8 2, no body). The client sends it only from the master
/// of a one-member guild.</summary>
public sealed record GuildBreakRequest : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) => w.WriteByte(GuildModes.Send(2, profile));
}

/// <summary>FUN_1413b71c0 (@0x1413b71c0, :454): invite a character to the guild (u8 3, the name).</summary>
public sealed record GuildInviteRequest(string Name) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(3, profile)); w.WriteUnicodeString(Name); }
}

/// <summary>SendGuildMode8 (@0x1413b76c0): expel a member (u8 8, the name).</summary>
public sealed record GuildExpelRequest(string Name) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(8, profile)); w.WriteUnicodeString(Name); }
}

/// <summary>SendGuildMode10 (@0x1413b78c0): set a member's grade (u8 10, the name, u8 the grade).</summary>
public sealed record GuildMemberGradeRequest(string Name, int Grade) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(10, profile)); w.WriteUnicodeString(Name); w.WriteByte((byte)Grade); }
}

/// <summary>SendGuildMode63 (@0x1413b7ad0): hand the master over to a member (u8 0x3f, the name).</summary>
public sealed record GuildChangeMasterRequest(string Name) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x3f, profile)); w.WriteUnicodeString(Name); }
}

/// <summary>FUN_1413b7c70 (@0x1413b7c70): a guild text (u8 0x40, u8 the kind, the text). The dialog's OnChangeNotify
/// sends kind 1, the notice.</summary>
public sealed record GuildTextRequest(int Kind, string Text) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x40, profile)); w.WriteByte((byte)Kind); w.WriteUnicodeString(Text); }
}

/// <summary>SendGuildMode13 (@0x1413b80d0): this member's memo (u8 0xd, the text).</summary>
public sealed record GuildMemoRequest(string Text) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0xd, profile)); w.WriteUnicodeString(Text); }
}

/// <summary>SendGuildMode67 (@0x1413b8c40): one grade's name and rights (u8 0x43, u8 the index, then CGuildGrade::Write:
/// u8 the grade, the name, i32 the rights).</summary>
public sealed record GuildGradeRequest(int Index, GuildGrade Grade) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) {
        w.WriteByte(GuildModes.Send(0x43, profile)); w.WriteByte((byte)Index);
        w.WriteByte((byte)Grade.Grade); w.WriteUnicodeString(Grade.Name); w.WriteInt(Grade.Permissions);
    }
}

/// <summary>SendGuildMode68 (@0x1413b8e60): the guild's propensity (u8 0x44, u8 the focus, i32 the value). The client
/// names the two CGuildManager f154 (the "listed in the search" flag of OnChageRegistSearch) and f158 (GetPropensity).</summary>
public sealed record GuildPropensityRequest(int Focus, int Value) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x44, profile)); w.WriteByte((byte)Focus); w.WriteInt(Value); }
}

/// <summary>CGuildManager::SendGuildMode115 (@0x1413bd2e0, guildmanager.cpp:1436): register for the vs game (u8 0x73).</summary>
public sealed record GuildVsGameRegisterRequest : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x73, profile)); }
}

/// <summary>CGuildManager::SendGuildMode116 (@0x1413bd3f0, :1452): unregister from the vs game (u8 0x74).</summary>
public sealed record GuildVsGameUnregisterRequest : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x74, profile)); }
}

/// <summary>CGuildManager::FUN_1413bb490 (@0x1413bb490, guildmanager.cpp:1046): send a storage item to a member (u8 0x6d, ItemEntity, the receiver's name).</summary>
public sealed record GuildStorageGiftRequest(GuildStorageItem Item, string Receiver) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x6d, profile)); Item.Write(w); w.WriteUnicodeString(Receiver); }
}

/// <summary>CGuildManager::SendGuildMode112 (@0x1413bbb80, :1095): ask for the storage history (u8 0x70).</summary>
public sealed record GuildStorageHistoryRequest : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x70, profile)); }
}

/// <summary>CGuildManager::RequestPvpHistoryAndSeason (@0x1413bbe80, guildmanager.cpp:1144): the battles after the id (u8 0x4a, i64).</summary>
public sealed record GuildPvpHistoryRequest(long LastId) : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x4a, profile)); w.WriteLong(LastId); }
}

/// <summary>The same function's second send, the first time only: the season history (u8 0x4b).</summary>
public sealed record GuildPvpSeasonHistoryRequest : IProfileOutPacket {
    public Outbound Op => Outbound.Guild;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(GuildModes.Send(0x4b, profile)); }
}
