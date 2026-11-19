using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>UIPopUpMenu::SendRequestHome (uipartydialog.cpp:3385, @0x141a86a50), the menu's Invite to My Home: RequestHome u8 1 and the
/// character's name (WriteUnicodeString).</summary>
public sealed record HomeInviteRequest(string Name) : IOutPacket {
    public Outbound Op => Outbound.RequestHome;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteUnicodeString(Name ?? ""); }
}

/// <summary>UIPopUpMenu::SendRequestCube (uipartydialog.cpp:3424, @0x141a86bd0), the menu's furnishing rights grant: RequestCube
/// u8 0x39 and the name. The client narrows the name to the ANSI code page (FUN_14016a600) and WriteUnicodeStringA widens it
/// back (FUN_14016a730, packet.h:66) before the u16 length + UTF-16 write; a game name round-trips unchanged, so it is written
/// as it is.</summary>
public sealed record FurnishingRightsRequest(string Name) : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x39); w.WriteUnicodeString(Name ?? ""); }
}
