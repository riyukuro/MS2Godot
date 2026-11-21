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
/// back (FUN_14016a730, packet.h:66) before the u16 length + UTF-16 write. The round trip keeps only the characters of the
/// client's code page (DAT_1439f4840, CP949 on KMS2: all Hangul and ASCII; any other character comes back as '?'); game
/// names hold only such characters, so the name is written as it is.</summary>
public sealed record FurnishingRightsRequest(string Name) : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x39); w.WriteUnicodeString(Name ?? ""); }
}

/// <summary>MyPC::SendHouseSetting (@0x141530720, mypc.cpp:4375-4384): RequestCube u8 0x18, u8 kind (0 public, 1 password, 2 private),
/// the value (the password, else empty; WriteUnicodeString) and u8 flag. Not sent without the local home's map info (GetUGCMapInfo).</summary>
public sealed record HouseSettingRequest(byte Kind, string Value, bool Flag) : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x18); w.WriteByte(Kind); w.WriteUnicodeString(Value ?? ""); w.WriteByte((byte) (Flag ? 1 : 0)); }
}

/// <summary>UIHomeOptionDialog::SendRequestCubeMode42 (@0x140fa9ad0, uihomeoptiondialog.cpp:254): RequestCube u8 0x2a, u8 row, bool on.</summary>
public sealed record HomeOptionRowSwitchRequest(byte Row, bool On) : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x2a); w.WriteByte(Row); w.WriteByte((byte) (On ? 1 : 0)); }
}

/// <summary>UIHomeOptionDialog::SendRequestCubeMode43 (@0x140fa9b50, uihomeoptiondialog.cpp:265): RequestCube u8 0x2b, u8 row, u8 level.</summary>
public sealed record HomeOptionRowLevelRequest(byte Row, byte Level) : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x2b); w.WriteByte(Row); w.WriteByte(Level); }
}
