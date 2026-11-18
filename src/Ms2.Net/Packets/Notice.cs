// Layouts from the restored client's readers, checked against the server fork's writers.
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// A StringIdInterface text as packets carry it (FUN_14084f390 @0x14084f390, stringidinterfacebase.cpp:195): bool formatted,
/// i32 kind, then formatted: i32 string id, i32 count and count unicode arguments; else a unicode text.
/// </summary>
public sealed record StringIdText(bool Formatted, int Kind, int Id, string[] Args, string Text) {
    public static StringIdText Read(ByteReader r) {
        bool formatted = r.ReadBool();
        int kind = r.ReadInt();
        if (!formatted) return new StringIdText(false, kind, 0, Array.Empty<string>(), r.ReadUnicodeString());
        int id = r.ReadInt();
        uint count = (uint)r.ReadInt();
        var args = new string[count];
        for (uint i = 0; i < count; i++) args[i] = r.ReadUnicodeString();
        return new StringIdText(true, kind, id, args, "");
    }
}

/// <summary>
/// Inbound 0x71 (CSceneStateGame::OnNotice, scenestategame.cpp:9216; decoder Unk_1407a7d20 @0x1407a7d20,
/// unit_1407a4f20_packetserializers.cpp:761): u8 kind, u16 flags, the text, and a u16 when flags has 0x10. A kind 4 notice
/// whose flags pick the top layer line (0x200 and none of 2, 0x20, 4, 0x10) is followed by an i32 OnNotice reads itself.
/// No ExpectEnd: the client ignores trailing bytes (the server fork writes its 0x200 value after a 4 or 0x10 too).
/// </summary>
public sealed record NoticeMessage(byte Kind, ushort Flags, StringIdText Text, ushort Arg, int TopLayerArg) {
    public static NoticeMessage Read(ByteReader r) {
        byte kind = r.ReadByte();
        ushort flags = (ushort)r.ReadShort();
        var text = StringIdText.Read(r);
        ushort arg = (flags & 0x10) != 0 ? (ushort)r.ReadShort() : (ushort)0;
        int top = kind == 4 && (flags & 0x200) != 0 && (flags & (2 | 0x20 | 4 | 0x10)) == 0 ? r.ReadInt() : 0;
        return new NoticeMessage(kind, flags, text, arg, top);
    }
}
