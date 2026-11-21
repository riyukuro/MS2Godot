using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>An owned chat emoticon pack: ChatEmoticonGroupInfo (chatemoticoncli.h), the group id and its expiry.</summary>
public readonly record struct ChatEmoticonGroup(uint Id, long Expire);

/// <summary>
/// Inbound ChatStamp (RecvOp 0x126, commonfiles/opcodes.h:293), CSceneStateGame::OnChatStamp (@0x14173e440,
/// scenestategame.cpp:5177) -> CChatEmoticonCli::DispatchPacket (@0x140a3a700, chatemoticoncli.cpp): a u8 type, then
/// 0 (OnEmoticonList @0x140a38fa0): u16 count, that many u32 ids (the favorites); u16 count, that many groups (u32 id, i64 expiry);
/// 1 (@0x140a39170, packs expired): i32 count, u32 group ids; i32 count, u32 favorite ids;
/// 2 (@0x140a39360, packs added by an item): i32 item id; i32 count, groups (u32 id, i64 expiry);
/// 3 (@0x140a39830): u32 emoticon id, unicode string window name, u8 flag (the messenger window types it);
/// 4 (@0x140a39940): u32 emoticon id, unicode string window name (the group messenger window sends its image);
/// 5 (@0x140a39720): u32 id, moved to the end of the favorites; 6 (@0x140a397b0): u32 id, removed from them;
/// 7 (@0x140a39a20): u8 result. Another type is read as null (the dispatcher does nothing).
/// </summary>
public abstract record ChatStampMessage
{
    public sealed record List(IReadOnlyList<uint> Favorites, IReadOnlyList<ChatEmoticonGroup> Groups) : ChatStampMessage;
    public sealed record Expired(IReadOnlyList<uint> Groups, IReadOnlyList<uint> Favorites) : ChatStampMessage;
    public sealed record Added(int ItemId, IReadOnlyList<ChatEmoticonGroup> Groups) : ChatStampMessage;
    public sealed record Typed(uint Id, string Window, byte Flag) : ChatStampMessage;
    public sealed record GroupSent(uint Id, string Window) : ChatStampMessage;
    public sealed record MovedToEnd(uint Id) : ChatStampMessage;
    public sealed record Removed(uint Id) : ChatStampMessage;
    public sealed record Result(byte Code) : ChatStampMessage;

    public static ChatStampMessage Read(ByteReader r)
    {
        byte type = r.ReadByte();
        ChatStampMessage m;
        switch (type)
        {
            case 0:
            {
                int ids = (ushort)r.ReadShort();
                var favorites = new List<uint>();
                for (int i = 0; i < ids; i++) favorites.Add((uint)r.ReadInt());
                int n = (ushort)r.ReadShort();
                var groups = new List<ChatEmoticonGroup>();
                for (int i = 0; i < n; i++) groups.Add(new ChatEmoticonGroup((uint)r.ReadInt(), r.ReadLong()));
                m = new List(favorites, groups);
                break;
            }
            case 1:
            {
                int n = r.ReadInt();
                var groups = new List<uint>();
                for (int i = 0; i < n; i++) groups.Add((uint)r.ReadInt());
                int ids = r.ReadInt();
                var favorites = new List<uint>();
                for (int i = 0; i < ids; i++) favorites.Add((uint)r.ReadInt());
                m = new Expired(groups, favorites);
                break;
            }
            case 2:
            {
                int item = r.ReadInt();
                int n = r.ReadInt();
                var groups = new List<ChatEmoticonGroup>();
                for (int i = 0; i < n; i++) groups.Add(new ChatEmoticonGroup((uint)r.ReadInt(), r.ReadLong()));
                m = new Added(item, groups);
                break;
            }
            case 3: m = new Typed((uint)r.ReadInt(), r.ReadUnicodeString(), r.ReadByte()); break;
            case 4: m = new GroupSent((uint)r.ReadInt(), r.ReadUnicodeString()); break;
            case 5: m = new MovedToEnd((uint)r.ReadInt()); break;
            case 6: m = new Removed((uint)r.ReadInt()); break;
            case 7: m = new Result(r.ReadByte()); break;
            default: return null;
        }
        r.ExpectEnd("ChatStamp");
        return m;
    }
}

/// <summary>
/// Outbound ChatSticker (SendOp 0xB7, commonfiles/opcodes.h:485) mode 1, CChatEmoticonCli::SendMessenger (@0x140a3a410,
/// chatemoticoncli.cpp): the dialog's OnLoadComplete asks for the list. Wire: u8 1, i32 count, the u32 ids of the packs whose
/// expiry has passed (now > expiry).
/// </summary>
public sealed record ChatStickerListRequest(IReadOnlyList<uint> ExpiredGroups) : IOutPacket
{
    public Outbound Op => Outbound.ChatSticker;
    public void Write(ByteWriter w)
    {
        w.WriteByte(1);
        w.WriteInt(ExpiredGroups.Count);
        foreach (uint id in ExpiredGroups) w.WriteInt((int)id);
    }
}

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
