using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound BuddyBadge (RecvOp 0x10E, commonfiles/opcodes.h:270; GMS2 0x110), CNetworkService::OnBuddyBadge (@0x14153f6c0,
/// networkservice.cpp:1187-1202): u8 kind, i64 a character id (the PC found by FUN_141bd1530; none, nothing more is read), then
/// kind 0 and 2: that PC's couple effect shown (PC::FUN_141663620); kind 1: hidden (PC::FUN_141663680);
/// kind 3: i64 an item uid and a unicode name: when the PC's worn type 7 gem (GetItemByGemType(7)) has that uid, its
/// couple name becomes the name (CItem::SetCoupleName).
/// Another kind reads nothing more and does nothing. The handler never tests for the packet's end.
/// </summary>
public sealed record BuddyBadgeMessage(byte Kind, long CharacterId, long ItemUid, string Name)
{
    public const byte Show = 0, Hide = 1, ShowToNewcomer = 2, PartnerName = 3;

    public static BuddyBadgeMessage Read(ByteReader r)
    {
        byte kind = r.ReadByte();
        long id = r.ReadLong();
        if (kind != PartnerName) return new BuddyBadgeMessage(kind, id, 0, null);
        long uid = r.ReadLong();
        return new BuddyBadgeMessage(kind, id, uid, r.ReadUnicodeString());
    }
}

/// <summary>
/// Outbound BuddyBadge (SendOp 0xA4, commonfiles/opcodes.h:467; GMS2 0xA5) mode 0 or 1, CoupleEffectSystem::SetEffectMode
/// (@0x140b0f3a0, unit_140b0e5f0_coupleeffectsystem.cpp): u8 the mode (0 the effect on, 1 off), i64 the couple's character id
/// (CoupleEffectSystem::GetCharacterId: the badge opener's).
/// </summary>
public sealed record BuddyBadgeModeRequest(byte Mode, long CharacterId) : IOutPacket
{
    public Outbound Op => Outbound.BuddyBadge;
    public void Write(ByteWriter w) { w.WriteByte(Mode); w.WriteLong(CharacterId); }
}

/// <summary>
/// Outbound BuddyBadge mode 2, CoupleEffectSystem::FUN_140b0ead0 (@0x140b0ead0) when a player enters the field while the own
/// linked badge's effect is on: u8 2, i64 the couple's character id, i64 the newcomer's character id.
/// </summary>
public sealed record BuddyBadgeNewcomerRequest(long CharacterId, long NewcomerId) : IOutPacket
{
    public Outbound Op => Outbound.BuddyBadge;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteLong(CharacterId); w.WriteLong(NewcomerId); }
}

/// <summary>
/// Outbound BuddyBadge mode 3, CoupleEffectSystem::SendStartRequest (@0x140b0f260): the badge's partner name differs from the
/// partner's name now. Wire: u8 3, i64 the partner's character id.
/// </summary>
public sealed record BuddyBadgeNameRequest(long PartnerId) : IOutPacket
{
    public Outbound Op => Outbound.BuddyBadge;
    public void Write(ByteWriter w) { w.WriteByte(3); w.WriteLong(PartnerId); }
}
