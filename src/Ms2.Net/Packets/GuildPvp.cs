using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// CGuildPVPHistoryData (guilddef_1.cpp): one guild battle of the PvP history. ReadStream (@0x1404c6cb0, :218) reads, in this order,
/// i32 f18, i32 f1c, u8 f20 (the result flag: 1 is a loss for the first guild when no round was fought), i64 f28 and i64 f30 (the two guild ids),
/// string f38 and string f40 (their names), four i32 (f48..f54), five rounds of (string f58, string f80, u8 fa8 'W' or 'L'; the two strings
/// are the "id,mark:id,mark" lists of the first and second guild's members), i64 fb0 and i64 fb8 (the entry's id and time, the id the next request
/// carries).
/// </summary>
public sealed class GuildPvpHistory {
    public int F18, F1c, Result;
    public long FirstGuildId, SecondGuildId;
    public string FirstGuildName = "", SecondGuildName = "";
    public int[] F48 = new int[4];
    public string[] FirstMembers = new string[5], SecondMembers = new string[5];
    public byte[] RoundResults = new byte[5];
    public long Fb0, Time;

    public static GuildPvpHistory Read(ByteReader r) {
        var h = new GuildPvpHistory { F18 = r.ReadInt(), F1c = r.ReadInt(), Result = r.ReadByte(), FirstGuildId = r.ReadLong(), SecondGuildId = r.ReadLong(),
            FirstGuildName = r.ReadUnicodeString(), SecondGuildName = r.ReadUnicodeString() };
        for (int i = 0; i < 4; i++) h.F48[i] = r.ReadInt();
        for (int i = 0; i < 5; i++) { h.FirstMembers[i] = r.ReadUnicodeString(); h.SecondMembers[i] = r.ReadUnicodeString(); h.RoundResults[i] = r.ReadByte(); }
        h.Fb0 = r.ReadLong(); h.Time = r.ReadLong();
        return h;
    }
}

/// <summary>
/// CGuildPVPSeasonHistoryData (guilddef_2.cpp): one finished season of the guild. FUN_1404d4680 (@0x1404d4680, :313) reads i32 f18, i64 f20,
/// i64 f28, string f30 (the season's name, what the history page shows), i32 f38, i32 f3c (the rank), i32 f40 (the grade), string f48 (ReadUnicodeStringA),
/// then i32 f50, f54 (wins), f58 (losses), f5c, f60, f64, f68.
/// </summary>
public sealed class GuildPvpSeasonHistory {
    public int F18, Rank, Grade, F38, F50, Wins, Losses, F5c, F60, F64, F68;
    public long F20, F28;
    public string SeasonName = "", F48 = "";

    public static GuildPvpSeasonHistory Read(ByteReader r) {
        var h = new GuildPvpSeasonHistory { F18 = r.ReadInt(), F20 = r.ReadLong(), F28 = r.ReadLong(), SeasonName = r.ReadUnicodeString(), F38 = r.ReadInt(), Rank = r.ReadInt(), Grade = r.ReadInt(), F48 = r.ReadUnicodeString() };
        h.F50 = r.ReadInt(); h.Wins = r.ReadInt(); h.Losses = r.ReadInt(); h.F5c = r.ReadInt(); h.F60 = r.ReadInt(); h.F64 = r.ReadInt(); h.F68 = r.ReadInt();
        return h;
    }
}
