// Layout from the restored client's CGuildManager (client/ms2/guildmanager.cpp; RecvOp::Guild 0x8A): the type switch
// FUN_1413b6370 (:301) reads a u8 type and passes the rest to one handler per type. The two list entries are
// CSearchGuildJoin::ReadStream (@0x1404c8be0) and CGuildJoinRequestForChar::ReadStream (@0x1404c8150,
// commonfiles/guilddef_1.cpp). ReadUnicodeStringA is the same u16 + UTF-16 on the wire as ReadUnicodeString.
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// One guild search result, CSearchGuildJoin (ReadStream @0x1404c8be0): i64 the guild id (f18), the name (f20), the mark
/// (f28), the trophy counts (f38, Unk_IntTriple), i32 the member count (f44), i32 the capacity (f30), i32 the guild exp (f34),
/// i64 the master's character id (f48) and account id (f50), the master's name (f58).
/// </summary>
public sealed class GuildSearchEntry {
    public long GuildId;            // f18
    public string Name = "";        // f20
    public string Mark = "";        // f28
    public (int Combat, int Adventure, int Life) Trophy;   // f38
    public int MemberCount;         // f44
    public int Capacity;            // f30
    public int Exp;                 // f34
    public long MasterId;           // f48
    public long MasterAccountId;    // f50
    public string MasterName = "";  // f58

    /// <summary>Unk_IntTriple::Sum.</summary>
    public int TrophyTotal => Trophy.Combat + Trophy.Adventure + Trophy.Life;

    public static GuildSearchEntry Read(ByteReader r) => new() {
        GuildId = r.ReadLong(), Name = r.ReadUnicodeString(), Mark = r.ReadUnicodeString(),
        Trophy = (r.ReadInt(), r.ReadInt(), r.ReadInt()),
        MemberCount = r.ReadInt(), Capacity = r.ReadInt(), Exp = r.ReadInt(),
        MasterId = r.ReadLong(), MasterAccountId = r.ReadLong(), MasterName = r.ReadUnicodeString(),
    };
}

/// <summary>
/// One join request this character sent, CGuildJoinRequestForChar (ReadStream @0x1404c8150): i64 the request id (f18),
/// i64 the guild id (f20), the guild name (f28), i32 the guild exp (f30), the mark (f38), the trophy counts (f44), i32 the
/// member count (f50), i32 the capacity (f40), the master's name (f58), then i64 f60, f68 and f70 (their meaning is
/// STILL-OPEN; the dialog does not read them).
/// </summary>
public sealed class GuildJoinRequest {
    public long Id;                 // f18
    public long GuildId;            // f20
    public string GuildName = "";   // f28
    public int Exp;                 // f30
    public string Mark = "";        // f38
    public (int Combat, int Adventure, int Life) Trophy;   // f44
    public int MemberCount;         // f50
    public int Capacity;            // f40
    public string MasterName = "";  // f58
    public long Value60, Value68, Value70;

    /// <summary>Unk_IntTriple::Sum.</summary>
    public int TrophyTotal => Trophy.Combat + Trophy.Adventure + Trophy.Life;

    public static GuildJoinRequest Read(ByteReader r) => new() {
        Id = r.ReadLong(), GuildId = r.ReadLong(), GuildName = r.ReadUnicodeString(), Exp = r.ReadInt(),
        Mark = r.ReadUnicodeString(), Trophy = (r.ReadInt(), r.ReadInt(), r.ReadInt()),
        MemberCount = r.ReadInt(), Capacity = r.ReadInt(), MasterName = r.ReadUnicodeString(),
        Value60 = r.ReadLong(), Value68 = r.ReadLong(), Value70 = r.ReadLong(),
    };
}

/// <summary>
/// Guild (0x8A): u8 type, then per type:
/// <list type="bullet">
/// <item>0 OnPacketGuildInfo (@0x1413bd680): CGuildInfo::Read (commonfiles/guilddef_1.cpp:1232), whose first field is the
/// i64 guild id. Only the id is read here; the rest (name, mark, master, members, grades, skills, ...) is STILL-OPEN.</item>
/// <item>1 OnPacketCreateGuild (@0x1413bd8a0): u8 result; when 0, the guild name.</item>
/// <item>0x52 FUN_1413c5360 and 0x53 FUN_1413c5440: i64 guild id, the guild name (a join request was sent or cancelled).</item>
/// <item>0x56 FUN_1413c5840: i32 count, then per entry u8 has and, when set, a <see cref="GuildJoinRequest"/>.</item>
/// <item>0x57 OnPacketSearchResults (@0x1413c59e0): i32 count, then per entry u8 has and, when set, a
/// <see cref="GuildSearchEntry"/>.</item>
/// <item>0x30 OnPacketMyJoinRequestRemoved (@0x1413c5f90): the guild name, i64 the request id, u8 joined.</item>
/// </list>
/// The other types are not read here (STILL-OPEN).
/// </summary>
public sealed record GuildMessage(byte Type) {
    /// <summary>Type 0: the guild id; type 0x52/0x53: the guild id.</summary>
    public long GuildId { get; init; }
    /// <summary>Type 1: the result (0 = created).</summary>
    public int Result { get; init; }
    /// <summary>Types 1, 0x52, 0x53, 0x30: the guild name.</summary>
    public string Name { get; init; } = "";
    /// <summary>Type 0x30: the request id.</summary>
    public long RequestId { get; init; }
    /// <summary>Type 0x30: the request was answered by joining.</summary>
    public bool Joined { get; init; }
    /// <summary>Type 0x56.</summary>
    public IReadOnlyList<GuildJoinRequest> Requests { get; init; } = Array.Empty<GuildJoinRequest>();
    /// <summary>Type 0x57; <see cref="SearchCount"/> is the count as sent (0 shows the no-result box).</summary>
    public IReadOnlyList<GuildSearchEntry> Results { get; init; } = Array.Empty<GuildSearchEntry>();
    public int SearchCount { get; init; }

    public static GuildMessage Read(ByteReader r) {
        byte t = r.ReadByte();
        switch (t) {
            case 0: return new(t) { GuildId = r.ReadLong() };
            case 1: {
                int result = r.ReadByte();
                return result != 0 ? new(t) { Result = result } : new(t) { Name = r.ReadUnicodeString() };
            }
            case 0x52: case 0x53: return new(t) { GuildId = r.ReadLong(), Name = r.ReadUnicodeString() };
            case 0x56: {
                int n = r.ReadInt();
                var list = new List<GuildJoinRequest>();
                for (int i = 0; i < n; i++) if (r.ReadByte() != 0) list.Add(GuildJoinRequest.Read(r));
                return new(t) { Requests = list };
            }
            case 0x57: {
                int n = r.ReadInt();
                var list = new List<GuildSearchEntry>();
                for (int i = 0; i < n; i++) if (r.ReadByte() != 0) list.Add(GuildSearchEntry.Read(r));
                return new(t) { Results = list, SearchCount = n };
            }
            case 0x30: return new(t) { Name = r.ReadUnicodeString(), RequestId = r.ReadLong(), Joined = r.ReadByte() != 0 };
            default: return new(t);
        }
    }
}
