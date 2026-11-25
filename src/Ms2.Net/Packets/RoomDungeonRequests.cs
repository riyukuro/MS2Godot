using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// The outbound packets of UIDungeonRoomInfo (uidungeonroomdialog.cpp): SendOp::RoomDungeon (0x2B on GMS2) with the modes the room
// panel sends, and the two party search modes of its search button. FUN_140710590 sends each one.

/// <summary>UIDungeonRoomInfo::RequestEntry @0x1412b6250 (uidungeonroomdialog.cpp:1702): RoomDungeon u8 2, u32 room id, bool party,
/// u32 portal id (m_pOwner->GetPortalId), bool use ticket.</summary>
public sealed record RoomDungeonEntryRequest(uint RoomId, bool Party, uint PortalId, bool UseTicket) : IOutPacket {
    public Outbound Op => Outbound.RoomDungeon;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteInt((int) RoomId); w.WriteBool(Party); w.WriteInt((int) PortalId); w.WriteBool(UseTicket); }
}

/// <summary>UIDungeonRoomInfo::OnUseExtraRewardItem @0x1412b5410 (:1497), the yes of the extra reward box: RoomDungeon u8 8, u32 room id.</summary>
public sealed record RoomDungeonUseExtraRewardRequest(uint RoomId) : IOutPacket {
    public Outbound Op => Outbound.RoomDungeon;
    public void Write(ByteWriter w) { w.WriteByte(8); w.WriteInt((int) RoomId); }
}

/// <summary>UIDungeonRoomInfo::RequestFindHelper @0x1412b5610 (:1556): RoomDungeon u8 0xf, u32 room id.</summary>
public sealed record RoomDungeonFindHelperRequest(uint RoomId) : IOutPacket {
    public Outbound Op => Outbound.RoomDungeon;
    public void Write(ByteWriter w) { w.WriteByte(0xf); w.WriteInt((int) RoomId); }
}

/// <summary>UIDungeonRoomInfo::RequestFindMember @0x1412b5680 (:1566): RoomDungeon u8 0x10, u32 room id.</summary>
public sealed record RoomDungeonFindMemberRequest(uint RoomId) : IOutPacket {
    public Outbound Op => Outbound.RoomDungeon;
    public void Write(ByteWriter w) { w.WriteByte(0x10); w.WriteInt((int) RoomId); }
}

/// <summary>UIDungeonRoomInfo::RequestChangeExpert @0x1412b56f0 (:1576): RoomDungeon u8 0x11, u32 room id.</summary>
public sealed record RoomDungeonChangeExpertRequest(uint RoomId) : IOutPacket {
    public Outbound Op => Outbound.RoomDungeon;
    public void Write(ByteWriter w) { w.WriteByte(0x11); w.WriteInt((int) RoomId); }
}

/// <summary>UIDungeonRoomInfo::RequestLeaveDungeon @0x1412b5760 (:1586): RoomDungeon u8 1.</summary>
public sealed record RoomDungeonLeaveRequest : IOutPacket {
    public Outbound Op => Outbound.RoomDungeon;
    public void Write(ByteWriter w) => w.WriteByte(1);
}

/// <summary>UIDungeonRoomInfo::OnFavoriteMark @0x1412b5320 (:1482): RoomDungeon u8 0x18, u32 room id, bool selected.</summary>
public sealed record RoomDungeonFavoriteRequest(uint RoomId, bool Selected) : IOutPacket {
    public Outbound Op => Outbound.RoomDungeon;
    public void Write(ByteWriter w) { w.WriteByte(0x18); w.WriteInt((int) RoomId); w.WriteBool(Selected); }
}

/// <summary>UIDungeonRoomInfo::OnPartySearch @0x1412b4330 (:1320), the search button not selected: Party u8 0x21, u32 room id.</summary>
public sealed record PartyDungeonSearchStartRequest(uint RoomId) : IOutPacket {
    public Outbound Op => Outbound.Party;
    public void Write(ByteWriter w) { w.WriteByte(0x21); w.WriteInt((int) RoomId); }
}

/// <summary>UIDungeonRoomInfo::OnPartySearch (:1335), the search button selected: Party u8 0x22, u8 Unk_ClubBuffTypeOf(group type).</summary>
public sealed record PartyDungeonSearchCancelRequest(byte ClubBuffType) : IOutPacket {
    public Outbound Op => Outbound.Party;
    public void Write(ByteWriter w) { w.WriteByte(0x22); w.WriteByte(ClubBuffType); }
}
