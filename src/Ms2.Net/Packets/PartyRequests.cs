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
