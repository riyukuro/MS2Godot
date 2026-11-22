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

/// <summary>UIHomeOptionDialog::SendRequestCubeMode49 (@0x140fa9bd0, uihomeoptiondialog.cpp:276): RequestCube u8 0x31, nothing else (the home's visitors are sent out).</summary>
public sealed record HomeKickUsersRequest() : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x31); }
}

/// <summary>UIHomeInsideOptionDialog's send (FUN_140c280d0 @0x140c280d0, uihomeinsideoptiondialog.cpp:200-233): RequestCube u8 mode (0x33 background,
/// 0x34 light, 0x36 camera), u8 the combo's selected index.</summary>
public sealed record HomeInsideOptionRequest(byte Mode, byte Index) : IOutPacket {
    public const byte Background = 0x33, Light = 0x34, Camera = 0x36;
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(Mode); w.WriteByte(Index); }
}

/// <summary>UIHomeMenuDialog::SendRequestCube (@0x140c2b3e0, uihomemenudialog.cpp:759): RequestCube u8 0x1f, nothing else (the home is demolished).</summary>
public sealed record HomeDemolishRequest() : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x1f); }
}

/// <summary>MyPC::SendCraftModeOff (@0x141530200, mypc.cpp:4501): RequestSetCraftMode u8 0, u8 0.</summary>
public sealed record CraftModeOffRequest() : IOutPacket {
    public Outbound Op => Outbound.RequestSetCraftMode;
    public void Write(ByteWriter w) { w.WriteByte(0); w.WriteByte(0); }
}

/// <summary>UIHomeSaveSlotDialog's design save (OnSaveNameEntered @0x140faa7d0, SendRequestCubeMode46 @0x140faadb0): RequestCube u8 0x2e, i32 slot, unicode name.</summary>
public sealed record HomeDesignSaveRequest(int Slot, string Name) : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x2e); w.WriteInt(Slot); w.WriteUnicodeString(Name ?? ""); }
}

/// <summary>OnLoadConfirmed's direct design load (@0x140faac30, taken when the plot is in design mode or a GM level 50 up meets an admin only group): RequestCube u8 0x2f, i32 slot.</summary>
public sealed record HomeDesignLoadDirectRequest(int Slot) : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x2f); w.WriteInt(Slot); }
}

/// <summary>SendRequestCubeMode35 (@0x140ee8360, uiugccubebilldialog.cpp:264), the ask for a design's bill: RequestCube u8 0x23, i32 cube id (the slot).</summary>
public sealed record HomeDesignBillRequest(int Slot) : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x23); w.WriteInt(Slot); }
}

/// <summary>The blueprint save (OnBlueprintSaveNameEntered @0x140faaeb0, SendRequestCubeMode64 @0x140fab420): RequestCube u8 0x40, i32 slot, unicode name.</summary>
public sealed record HomeBlueprintSaveRequest(int Slot, string Name) : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x40); w.WriteInt(Slot); w.WriteUnicodeString(Name ?? ""); }
}

/// <summary>SendRequestCubeMode65 (@0x140fab310): RequestCube u8 0x41, i32 slot.</summary>
public sealed record HomeBlueprintLoadRequest(int Slot) : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x41); w.WriteInt(Slot); }
}

/// <summary>UIUGCCubeBillDialog::SendRequestCube (@0x140ee80a0, uiugccubebilldialog.cpp:243-249): the purchase of a cube, RequestCube u8 0x30, i32 cube id.</summary>
public sealed record CubeBuyRequest(int CubeId) : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x30); w.WriteInt(CubeId); }
}

/// <summary>UIUGCCubeBillDialog::SendRequestCube (@0x140ee80a0, uiugccubebilldialog.cpp:250-255): the purchase of a blueprint, RequestCube u8 0x46, i64 the blueprint item's uid.</summary>
public sealed record BlueprintBuyRequest(long BlueprintUid) : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x46); w.WriteLong(BlueprintUid); }
}

/// <summary>SendRequestCubeMode66 (@0x140ee86f0, uiugccubebilldialog.cpp:311-320): the ask for a blueprint's bill, RequestCube u8 0x42, i64 the blueprint's uid, bool true.</summary>
public sealed record BlueprintBillAskRequest(long BlueprintUid) : IOutPacket {
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) { w.WriteByte(0x42); w.WriteLong(BlueprintUid); w.WriteBool(true); }
}
