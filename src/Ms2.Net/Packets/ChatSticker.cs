using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Outbound ChatSticker (SendOp 0xB7, commonfiles/opcodes.h:485), the chat window's emoticon sends. UIMessenger::SendEmoticon
/// (@0x141a0fe20, uimessenger.cpp:1416-1436): u8 3, i32 emoticon id, unicode string the window's name (GetName), u8 the
/// selected chat type (FUN_141a144a0). UIMessenger::SetEmoticonFavorite (@0x141a10490, :1477-1497): u8 5 to add a favorite
/// or 6 to remove one, then i32 the emoticon id.
/// </summary>
public sealed record ChatStickerRequest(byte Mode, int EmoticonId, string Window = "", byte ChatType = 0) : IOutPacket
{
    public const byte Use = 3, Favorite = 5, Unfavorite = 6;

    public Outbound Op => Outbound.ChatSticker;

    public static ChatStickerRequest Send(int emoticonId, string window, int chatType) => new(Use, emoticonId, window, (byte)chatType);
    public static ChatStickerRequest SetFavorite(int emoticonId, bool favorite) => new(favorite ? Favorite : Unfavorite, emoticonId);

    public void Write(ByteWriter w)
    {
        w.WriteByte(Mode);
        w.WriteInt(EmoticonId);
        if (Mode != Use) return;
        w.WriteUnicodeString(Window);
        w.WriteByte(ChatType);
    }
}
