using System.Collections.Generic;
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound MessengerBrowserStamp (RecvOp 0x12C, commonfiles/opcodes.h:297; GMS2 0x12E), CSceneStateGame::OnMessengerBrowserStamp
/// (@0x14173f0c0, scenestategame.cpp:6842-6855): u32 a count, then each u16 a chat type the chat windows' target lists hide
/// (UIMessengerBrowser::HideType). The handler reads nothing without the messenger browser (0x4e61) and never tests for
/// the packet's end.
/// </summary>
public sealed record MessengerBrowserStamp(IReadOnlyList<int> HiddenTypes)
{
    public static MessengerBrowserStamp Read(ByteReader r)
    {
        uint count = (uint)r.ReadInt();
        var types = new List<int>();
        for (uint i = 0; i < count; i++) types.Add((ushort)r.ReadShort());
        return new MessengerBrowserStamp(types);
    }

    public override string ToString() => $"MessengerBrowserStamp {{ HiddenTypes = [{string.Join(", ", HiddenTypes)}] }}";
}
