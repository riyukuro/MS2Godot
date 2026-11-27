using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound GmCommand (RecvOp 0x106, commonfiles/opcodes.h:263; GMS2 0x108), CSceneStateGame::OnGmCommand (@0x14173c090,
/// scenestategame.cpp:5159-5168): u8 a mode; only mode 1 has a reader, FUN_14173c0f0 (@0x14173c0f0, :8562-8576), the community
/// ticker line: three unicode strings, the address (narrowed, FUN_14016a600), the shown text and a third the client reads and
/// drops. The reader runs only with feature 0x88 on: without it the strings stay unread (<see cref="Read"/> takes the flag).
/// </summary>
public sealed record GmCommand(byte Mode, string Url, string Text, string Extra)
{
    public const byte PopupText = 1;
    /// <summary>FUN_14173c0f0's feature gate (0x88, "WebBrowserPopup").</summary>
    public const int PopupTextFeature = 0x88;

    /// <summary>OnGmCommand: the mode, then mode 1's strings when <paramref name="popupFeature"/> (feature 0x88) is on.</summary>
    public static GmCommand Read(ByteReader r, bool popupFeature)
    {
        byte mode = r.ReadByte();
        if (mode != PopupText || !popupFeature) return new GmCommand(mode, null, null, null);
        string url = r.ReadUnicodeString(), text = r.ReadUnicodeString(), extra = r.ReadUnicodeString();
        return new GmCommand(mode, url, text, extra);
    }

    public bool IsPopupText => Mode == PopupText && Text != null;

    public override string ToString() => IsPopupText ? $"mode 1, text \"{Text}\", url \"{Url}\"" : $"mode {Mode}";
}
