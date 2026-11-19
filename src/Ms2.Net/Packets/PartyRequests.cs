using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// The party sends of the client (SendOp::Party 0x2C and the dungeon / club sends the party window makes). Strings go out with
// WriteUnicodeString (FUN_1401fbd30, unit_1401f9eb0_packetpayloadserializers.cpp:685: u16 length + UTF-16).

/// <summary>CPartyManager::SendPartyMode1 (partymanager.cpp:761): invite a character by name (u8 1, name).</summary>
public sealed record PartyInviteRequest(string Name) : IOutPacket {
    public Outbound Op => Outbound.Party;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteUnicodeString(Name ?? ""); }
}

/// <summary>CPartyManager::SendPartyMode2 @0x14162b950 (partymanager.cpp:743): the answer to an invite (u8 2), then
/// Unk_PartyJoinRequest (FUN_1401fc0b0): the invite's name, the answer as u8, the invite's kind. Answers: 1 accept, 9 decline,
/// 12 the box timed out, 20 (0x14) the refuse option, 21 (0x15) a PvP point or the box closed by CloseAll.</summary>
public sealed record PartyInviteAnswer(string Name, int Answer, int Kind) : IOutPacket {
    public const int Accept = 1, Decline = 9, TimedOut = 12, Refused = 0x14, Busy = 0x15;
    public Outbound Op => Outbound.Party;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteUnicodeString(Name ?? ""); w.WriteByte((byte)Answer); w.WriteInt(Kind); }
}

/// <summary>CPartyManager::SendPartyMode3 (partymanager.cpp:819): leave the party (u8 3).</summary>
public sealed record PartyLeaveRequest : IOutPacket {
    public Outbound Op => Outbound.Party;
    public void Write(ByteWriter w) => w.WriteByte(3);
}

/// <summary>CPartyManager::SendPartyMode4 (partymanager.cpp:717): expel a member (u8 4, the member's character id).</summary>
public sealed record PartyKickRequest(long CharacterId) : IOutPacket {
    public Outbound Op => Outbound.Party;
    public void Write(ByteWriter w) { w.WriteByte(4); w.WriteLong(CharacterId); }
}

/// <summary>UIPopUpMenu::OnMessage "changeChief" (uipartydialog.cpp:1886): hand the leader over (u8 0x11, the member's name).</summary>
public sealed record PartyLeaderChangeRequest(string Name) : IOutPacket {
    public Outbound Op => Outbound.Party;
    public void Write(ByteWriter w) { w.WriteByte(0x11); w.WriteUnicodeString(Name ?? ""); }
}

/// <summary>CPartyManager::SendPartyMode45 (partymanager.cpp:730): start a vote to kick (u8 0x2d, the member's character id).</summary>
public sealed record PartyVoteKickRequest(long CharacterId) : IOutPacket {
    public Outbound Op => Outbound.Party;
    public void Write(ByteWriter w) { w.WriteByte(0x2d); w.WriteLong(CharacterId); }
}

/// <summary>CPartyManager::SendPartyMode46 (partymanager.cpp:810): start a ready check (u8 0x2e).</summary>
/// <summary>PartyVoteCli::SendParty @0x140a89680: the vote's answer, u8 0x30, i32 vote id, bool yes (the vote page's
/// buttons, ASPartyVotePage::SendVote @0x141a74ac0; the ready box, PartyVoteReadyCli::OnMessageBoxResult @0x140a8a0b0).</summary>
public sealed record PartyVoteAnswer(int Id, bool Yes) : IOutPacket {
    public Outbound Op => Outbound.Party;
    public void Write(ByteWriter w) { w.WriteByte(0x30); w.WriteInt(Id); w.WriteBool(Yes); }
}

public sealed record PartyReadyCheckRequest : IOutPacket {
    public Outbound Op => Outbound.Party;
    public void Write(ByteWriter w) => w.WriteByte(0x2e);
}

/// <summary>CPartyManager::SendRoomDungeon (partymanager.cpp:800), from UIPartyDialog::OnEnterDungeonClick with 0
/// (uipartydialog.cpp:859): RoomDungeon u8 3, i32 value.</summary>
public sealed record RoomDungeonEnterRequest(int Value) : IOutPacket {
    public Outbound Op => Outbound.RoomDungeon;
    public void Write(ByteWriter w) { w.WriteByte(3); w.WriteInt(Value); }
}

/// <summary>UIPartyDialog::SendDungeonReward (uipartydialog.cpp:783), the give-up box's yes: DungeonReward u8 5.</summary>
public sealed record DungeonGiveUpRequest : IOutPacket {
    public Outbound Op => Outbound.DungeonReward;
    public void Write(ByteWriter w) => w.WriteByte(5);
}

/// <summary>CClubClientManager::FUN_140b15c10 (clubcli.cpp:890): create a club from the party (Club u8 1, name).</summary>
public sealed record ClubCreateRequest(string Name) : IOutPacket {
    public Outbound Op => Outbound.Club;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteUnicodeString(Name ?? ""); }
}
