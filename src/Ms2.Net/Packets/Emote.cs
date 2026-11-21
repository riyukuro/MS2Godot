using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// One learned emotion (dynamic action) as CSkillSystem::AddEmotion (skillslot.cpp:2790) stores it
/// (CSkillSystem::stEmotion, skillsystem.h:55): the emotion skill id and level, the expiry (0: never;
/// stEmotion::FUN_1417643d0, skillslot.cpp:1778, keeps it while expiry >= the server time), the sort time
/// (+0x30, the key of Unk_SsEmotionLess/Greater, skillslot.cpp:1787-1816), permanent and favorite.
/// </summary>
public readonly record struct EmoteEntry(int Id, ushort Level, bool Permanent, long Expire, long SortTime, bool Favorite);

/// <summary>
/// Inbound Emote (RecvOp 0x1F, commonfiles/opcodes.h:39), CSceneStateGame::OnEmote (@0x141712a90,
/// scenestategame.cpp:2533): a u8 mode, then
/// 0 (the whole list, which replaces the player's): i32 count; each: i32 id, i32 level (kept as u16), bool permanent,
///   i64 expiry, i64 sort time, bool favorite;
/// 1 (one learned): i32 id, i32 level, i64 expiry, i64 sort time; added as new, not permanent, not favorite, and
///   NotifyLearnEmotion(id) is posted;
/// 3 (a learn result): u8; 1 shows common string 0x858, 0 shows 0x857.
/// Other modes are read as null (OnEmote does nothing with them).
/// </summary>
public abstract record EmoteMessage
{
    public sealed record List(IReadOnlyList<EmoteEntry> Entries) : EmoteMessage;
    public sealed record Learn(EmoteEntry Entry) : EmoteMessage;
    public sealed record Result(byte Code) : EmoteMessage;

    public static EmoteMessage Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        switch (mode)
        {
            case 0:
            {
                uint n = (uint)r.ReadInt();
                var list = new List<EmoteEntry>();
                for (uint i = 0; i < n; i++)
                {
                    int id = r.ReadInt();
                    ushort level = (ushort)r.ReadInt();
                    bool permanent = r.ReadByte() != 0;
                    long expire = r.ReadLong();
                    long sort = r.ReadLong();
                    bool favorite = r.ReadByte() != 0;
                    list.Add(new EmoteEntry(id, level, permanent, expire, sort, favorite));
                }
                return new List(list);
            }
            case 1:
            {
                int id = r.ReadInt();
                ushort level = (ushort)r.ReadInt();
                long expire = r.ReadLong();
                long sort = r.ReadLong();
                return new Learn(new EmoteEntry(id, level, false, expire, sort, false));
            }
            case 3:
                return new Result(r.ReadByte());
        }
        return null;
    }
}

/// <summary>
/// Outbound Emote (SendOp 0x13, commonfiles/opcodes.h:325) mode 1, Unk_Inventory::SendEmotion (@0x140a65bb0,
/// inventory.cpp:1615): learn the emotion of an item (item type 24). Wire: u8 1, i64 item uid.
/// </summary>
public sealed record EmoteLearnRequest(long ItemUid) : IOutPacket
{
    public Outbound Op => Outbound.Emote;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteLong(ItemUid); }
}

/// <summary>
/// Outbound Emote mode 2, StateSkill (stateskill.cpp:1906): use an emotion skill. Wire: u8 2, i32 skill id, the
/// skill's parameter string (WriteUnicodeStringA: widened, then a unicode string).
/// </summary>
public sealed record EmoteUseRequest(int SkillId, string Param) : IOutPacket
{
    public Outbound Op => Outbound.Emote;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteInt(SkillId); w.WriteUnicodeString(Param ?? ""); }
}

/// <summary>
/// Outbound Emote mode 4, the dynamic action and couple action dialogs' onFavorite (uidynamicactiondialog.cpp:369,
/// uicoupleactiondialog.cpp:333). Wire: u8 4, i32 emotion id, i32 level, bool favorite.
/// </summary>
public sealed record EmoteFavoriteRequest(int Id, int Level, bool Favorite) : IOutPacket
{
    public Outbound Op => Outbound.Emote;
    public void Write(ByteWriter w) { w.WriteByte(4); w.WriteInt(Id); w.WriteInt(Level); w.WriteBool(Favorite); }
}
