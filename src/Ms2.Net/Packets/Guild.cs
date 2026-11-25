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
/// One join request sent to a guild, CGuildJoinRequestForGuild (ReadStream @0x1404c7ad0): i64 the request id (f18), i64 the
/// guild id (f20), i64 the applicant's account id (f28), i64 the applicant's character id (f30 — the accept page names it
/// "charSn" and pictures it), the name (f38), the profile picture code (f40, ReadUnicodeStringA: the same wire as
/// ReadUnicodeString), i32 the job code (f48), i32 the job (f4c), i32 the level (f50), the trophy counts (f54,
/// Unk_IntTriple) and i64 the time (f60, not shown).
/// </summary>
public sealed class GuildJoinRequestForGuild {
    public long Id;                  // f18
    public long GuildId;             // f20
    public long AccountId;           // f28
    public long CharId;              // f30
    public string Name = "";         // f38
    public string Picture = "";      // f40
    public int JobCode;              // f48
    public int Job;                  // f4c
    public int Level;                // f50
    public (int Combat, int Adventure, int Life) Trophy;   // f54
    public long Time;                // f60

    /// <summary>Unk_IntTriple::Sum.</summary>
    public int TrophyTotal => Trophy.Combat + Trophy.Adventure + Trophy.Life;

    public static GuildJoinRequestForGuild Read(ByteReader r) => new() {
        Id = r.ReadLong(), GuildId = r.ReadLong(), AccountId = r.ReadLong(), CharId = r.ReadLong(),
        Name = r.ReadUnicodeString(), Picture = r.ReadUnicodeString(),
        JobCode = r.ReadInt(), Job = r.ReadInt(), Level = r.ReadInt(),
        Trophy = (r.ReadInt(), r.ReadInt(), r.ReadInt()), Time = r.ReadLong(),
    };
}

/// <summary>Reads for the GMS2 lists (upstream GuildApplication.WriteTo, ListAppliedGuilds, ListGuilds): the same fields as the KMS entries in another order.</summary>
public static class GuildGms2Lists {
    public static GuildJoinRequestForGuild ReadApplication(ByteReader r) {
        var e = new GuildJoinRequestForGuild { Id = r.ReadLong(), GuildId = r.ReadLong(), CharId = r.ReadLong(), AccountId = r.ReadLong(), Name = r.ReadUnicodeString(), Picture = r.ReadUnicodeString() };
        e.Job = r.ReadInt(); e.JobCode = r.ReadInt(); e.Level = r.ReadInt();
        e.Trophy = (r.ReadInt(), r.ReadInt(), r.ReadInt()); e.Time = r.ReadLong();
        return e;
    }

    public static GuildJoinRequest ReadApplied(ByteReader r) => new() {
        Id = r.ReadLong(), GuildId = r.ReadLong(), GuildName = r.ReadUnicodeString(), Mark = r.ReadUnicodeString(),
        Trophy = (r.ReadInt(), r.ReadInt(), r.ReadInt()), MemberCount = r.ReadInt(), Capacity = r.ReadInt(), MasterName = r.ReadUnicodeString(),
        Value60 = r.ReadLong(), Value68 = r.ReadLong(), Value70 = r.ReadLong(),
    };

    public static GuildSearchEntry ReadGuild(ByteReader r) => new() {
        GuildId = r.ReadLong(), Name = r.ReadUnicodeString(), Mark = r.ReadUnicodeString(), Trophy = (r.ReadInt(), r.ReadInt(), r.ReadInt()),
        MemberCount = r.ReadInt(), Capacity = r.ReadInt(), Exp = r.ReadInt(),   // the focus
        MasterAccountId = r.ReadLong(), MasterId = r.ReadLong(), MasterName = r.ReadUnicodeString(),
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
/// A guild invite, CGuildInviteInfo (ReadStream @0x1404c9230, Write @0x1404c9160, commonfiles/guilddef_1.cpp:572): i64 the
/// guild id (f18), the guild name (f20), the mark (f28, ReadUnicodeStringA; CGuildInfo::FUN_1404ccdb0 when the server
/// fills it), the inviter's name (f30) and the invitee's name (f38).
/// </summary>
public sealed class GuildInviteInfo {
    public long GuildId;            // f18
    public string GuildName = "";   // f20
    public string Mark = "";        // f28
    public string Inviter = "";     // f30
    public string Invitee = "";     // f38

    public static GuildInviteInfo Read(ByteReader r) => new() {
        GuildId = r.ReadLong(), GuildName = r.ReadUnicodeString(), Mark = r.ReadUnicodeString(),
        Inviter = r.ReadUnicodeString(), Invitee = r.ReadUnicodeString(),
    };

    public void Write(ByteWriter w) {
        w.WriteLong(GuildId); w.WriteUnicodeString(GuildName); w.WriteUnicodeString(Mark);
        w.WriteUnicodeString(Inviter); w.WriteUnicodeString(Invitee);
    }
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
/// <item>0x55 OnPacketJoinRequestsForGuild (@0x1413c5690): i32 count, then per entry u8 has and, when set, a
/// <see cref="GuildJoinRequestForGuild"/>.</item>
/// <item>0x2d OnPacketJoinRequestReceived (@0x1413c6460): one <see cref="GuildJoinRequestForGuild"/> (no count).</item>
/// <item>0x2e OnPacketJoinRequestWithdrawn (@0x1413c6580): i64 the request id.</item>
/// <item>0x2f FUN_1413c6180: the requestor's name, the applicant's name, u8 accepted, i64 the request id.</item>
/// <item>0x54 OnPacketJoinRequestAnswer (@0x1413c5520): i64 the applicant's character id, their name, u8 accepted.</item>
/// <item>0x12 OnPacketMemberJoined (@0x1413bfaf0): the inviter's name, the member's name, u8 notify, then
/// <see cref="GuildMember.Read"/> of the member (mode 3: the whole record).</item>
/// <item>0x13 OnPacketMemberLeft (@0x1413bfe50): the name.</item>
/// <item>2 FUN_1413bda00: u8 result (0 = disbanded).</item>
/// <item>3 FUN_1413bdaf0: the guild name (the invite went out).</item>
/// <item>4 FUN_1413bdbb0 (:1587): a <see cref="GuildInviteInfo"/> (the invite received).</item>
/// <item>5 OnPacketInviteResult (@0x1413be020): a <see cref="GuildInviteInfo"/>, u8 the result (1 = joined).</item>
/// <item>6 FUN_1413be150: the invitee's name, u8 accepted, u8 the reason of a refusal.</item>
/// <item>7 FUN_1413be350: no body.</item>
/// <item>0xf OnPacketAttendComplete (@0x1413bebc0): no body.</item>
/// <item>0x71 OnPacketMyDonation (@0x1413bece0): i32 the donations today, i64 the time of the last one (the member's
/// ff0 / ff8).</item>
/// <item>0x38 FUN_1413c3780 (:2531): string the user, CGuildCustomEvent (i32 id, i32 time).</item>
/// <item>0x76 OnPacketVsGameTicket (:2653): u8 registered (f354), u8 notify.</item>
/// <item>0x75 OnPacketVsGameMatchCanceled (:2670): no body.</item>
/// <item>0x77 FUN_1413c45e0 (:2679): i32 the other game id (f350).</item>
/// <item>0x4f FUN_1413c4e20 (:2780): u8 the kind, u8 the error code, i32 the argument (ShowGuildError).</item>
/// <item>0x44 FUN_1413bf2a0 (guildmanager.cpp:1855): u8 the propensity focus, i32 its value (CGuildInfo f154 / f158).</item>
/// </list>
/// The other types are not read here (STILL-OPEN).
/// </summary>
public sealed record GuildMessage(byte Type) {
    /// <summary>Type 0: the guild id; types 0x52/0x53 and 0x54: an id.</summary>
    public long GuildId { get; init; }
    /// <summary>Type 0: the guild as far as it could be read (null when the packet holds only the id).</summary>
    public GuildInfo Info { get; init; }
    /// <summary>Types 1 and 2: the result (0 = created / disbanded); type 5: 1 when the invite was joined.</summary>
    public int Result { get; init; }
    /// <summary>Types 1, 3, 6 (the invitee), 0x52, 0x53, 0x30, 0x2f (the requestor), 0x12 (the inviter), 0x13: the name.</summary>
    public string Name { get; init; } = "";
    /// <summary>Types 0x30, 0x2e and 0x2f: the request id.</summary>
    public long RequestId { get; init; }
    /// <summary>Type 0x30: the request was answered by joining.</summary>
    public bool Joined { get; init; }
    /// <summary>Type 0x56.</summary>
    public IReadOnlyList<GuildJoinRequest> Requests { get; init; } = Array.Empty<GuildJoinRequest>();
    /// <summary>Type 0x57; <see cref="SearchCount"/> is the count as sent (0 shows the no-result box).</summary>
    public IReadOnlyList<GuildSearchEntry> Results { get; init; } = Array.Empty<GuildSearchEntry>();
    public int SearchCount { get; init; }
    /// <summary>Type 0x71: the donation count and its time.</summary>
    public int DonationCount { get; init; }
    public long DonationTime { get; init; }
    /// <summary>Type 0x55: the join requests sent to the guild.</summary>
    public IReadOnlyList<GuildJoinRequestForGuild> GuildRequests { get; init; } = Array.Empty<GuildJoinRequestForGuild>();
    /// <summary>Type 0x2d: the one request that arrived.</summary>
    public GuildJoinRequestForGuild NewRequest { get; init; }
    /// <summary>Types 6, 0x2f and 0x54: the answer.</summary>
    public bool Accepted { get; init; }
    /// <summary>Types 0x2f, 0x54 and 0x12: the applicant's (the new member's) name.</summary>
    public string Applicant { get; init; } = "";
    /// <summary>Type 0x12: the member that joined.</summary>
    public GuildMember Member { get; init; }
    /// <summary>Type 0x12: the notice line is wanted.</summary>
    public bool Notify { get; init; }
    /// <summary>Type 0x38: the custom event (id and time) a member opened; <see cref="Name"/> is the member. Type 0x76: the registration
    /// flag (<see cref="Accepted"/>) and the notice flag (<see cref="Notify"/>). Type 0x77: the other game id is <see cref="Value"/>.</summary>
    public int EventId { get; init; }
    public int EventTime { get; init; }
    /// <summary>Type 0x39 (FUN_1413c39e0 @0x1413c39e0, guildmanager.cpp:2554): the new house level and theme; <see cref="Name"/> is the member
    /// who upgraded. Type 0x3b (FUN_1413c3ef0 @0x1413c3ef0, :2603): the upgraded npc's kind and level.</summary>
    public int HouseLevel { get; init; }
    public int HouseTheme { get; init; }
    public int NpcKind { get; init; }
    public int NpcLevel { get; init; }
    public int RaidId { get; init; }
    /// <summary>Type 0x27 (FUN_1413c2860 @0x1413c2860, guildmanager.cpp:2372): the new PvP state (u8). Type 0x29 (FUN_1413c2c60 @0x1413c2c60, :2416): the score, wins and losses.</summary>
    public int PvpState { get; init; }
    public int Score { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    /// <summary>Type 0x4a (OnPacketPvpHistory @0x1413c5000, :2810): the battles, those flagged present.</summary>
    public IReadOnlyList<GuildPvpHistory> PvpHistory { get; init; } = Array.Empty<GuildPvpHistory>();
    /// <summary>Type 0x4b (OnPacketPvpSeasonHistory @0x1413c51c0, :2831): the finished seasons, those flagged present.</summary>
    public IReadOnlyList<GuildPvpSeasonHistory> PvpSeasonHistory { get; init; } = Array.Empty<GuildPvpSeasonHistory>();
    /// <summary>Type 0x6e (FUN_1413c3e50 @0x1413c3e50, guildmanager.cpp:2590): the storage entry that changed.</summary>
    public GuildStorageItem StorageItem { get; init; }
    /// <summary>Type 0x70 (OnPacketStorageHistory @0x1413c5dc0, :2963): the history lines.</summary>
    public IReadOnlyList<GuildStorageHistory> StorageHistory { get; init; } = Array.Empty<GuildStorageHistory>();
    /// <summary>Type 0x44: the guild's propensity focus (f154) and value (f158).</summary>
    public int Focus { get; init; }
    public int Value { get; init; }
    /// <summary>Type 0x4f: ShowGuildError's kind, code and argument.</summary>
    public int ErrorKind { get; init; }
    public int ErrorCode { get; init; }
    public int ErrorArg { get; init; }
    /// <summary>Types 4 and 5: the invite.</summary>
    public GuildInviteInfo Invite { get; init; }
    /// <summary>Type 6: the reason of a refusal (1 to 3 have a line).</summary>
    public int Reason { get; init; }
    /// <summary>Type 0x18: the member's login time. Type 0x3a: the poster id is <see cref="Value"/>.</summary>
    public long Time { get; init; }
    /// <summary>Types 0x3a (the notice text) and 0x41 (the string the client discards).</summary>
    public string Text { get; init; } = "";
    /// <summary>Type 0x1b: the mark code; type 0x3a: the character id is <see cref="GuildId"/>.</summary>
    public string Code { get; init; } = "";
    /// <summary>Type 0x1b: who changed the mark.</summary>
    public StringIdText Changer { get; init; }
    /// <summary>Type 0x23: achievement id, grade and kind (u16); type 0x20: the member's data.</summary>
    public int AchieveId { get; init; }
    public int Grade { get; init; }
    public int Kind { get; init; }

    public static GuildMessage Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    public static GuildMessage Read(ByteReader r, ProtocolProfile profile) {
        var m = ReadBody(r, profile);
        // Types 7 and 0xf have no body, the others end with their fields.
        if (m.Type is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 0xf or 0x12 or 0x13 or 0x71 or 0x2d or 0x2e or 0x2f or 0x30 or 0x44 or 0x75 or 0x76 or 0x77 or 0x38 or 0x39 or 0x3b or 0x25 or 0x3e or 0x27 or 0x29 or 0x4a or 0x4b or 0x6d or 0x6e or 0x70 or 0x52 or 0x53 or 0x54 or 0x55 or 0x56 or 0x57) r.ExpectEnd($"Guild type {m.Type}");
        return m;
    }

    static GuildMessage ReadBody(ByteReader r, ProtocolProfile profile) {
        // The KMS type: a GMS2 session's type goes through GuildModes first, so every case below is the KMS number.
        byte t = GuildModes.Receive(r.ReadByte(), profile);
        if (profile.IsGms2 && t is 0x55 or 0x56 or 0x57 or 0x2d) {
            // GMS2 lists (GMS2 83, 84, 85): the applications to the own guild, the own applications, the guild search; and
            // GMS2 45, the one application that arrived. The same fields as the KMS entries in another order.
            if (t == 0x2d) return new(t) { NewRequest = GuildGms2Lists.ReadApplication(r) };
            int n = r.ReadInt();
            switch (t) {
                case 0x55: { var list = new List<GuildJoinRequestForGuild>(); for (int i = 0; i < n; i++) if (r.ReadByte() != 0) list.Add(GuildGms2Lists.ReadApplication(r)); return new(t) { GuildRequests = list }; }
                case 0x56: { var list = new List<GuildJoinRequest>(); for (int i = 0; i < n; i++) if (r.ReadByte() != 0) list.Add(GuildGms2Lists.ReadApplied(r)); return new(t) { Requests = list }; }
                default: { var list = new List<GuildSearchEntry>(); for (int i = 0; i < n; i++) if (r.ReadByte() != 0) list.Add(GuildGms2Lists.ReadGuild(r)); return new(t) { Results = list, SearchCount = n }; }
            }
        }
        switch (t) {
            case 0: { var info = GuildInfo.Read(r, profile); r.ExpectEnd("Guild info"); return new(t) { GuildId = info.Id, Info = info }; }
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
            case 0x55: {
                int n = r.ReadInt();
                var list = new List<GuildJoinRequestForGuild>();
                for (int i = 0; i < n; i++) if (r.ReadByte() != 0) list.Add(GuildJoinRequestForGuild.Read(r));
                return new(t) { GuildRequests = list };
            }
            case 0x2d: return new(t) { NewRequest = GuildJoinRequestForGuild.Read(r) };
            case 0x2e: return new(t) { RequestId = r.ReadLong() };
            case 0x2f: return new(t) { Name = r.ReadUnicodeString(), Applicant = r.ReadUnicodeString(), Accepted = r.ReadByte() != 0, RequestId = r.ReadLong() };
            case 0x54: return new(t) { GuildId = r.ReadLong(), Applicant = r.ReadUnicodeString(), Accepted = r.ReadByte() != 0 };
            case 0x12: {
                string inviter = r.ReadUnicodeString(), name = r.ReadUnicodeString();
                bool notify = r.ReadByte() != 0;
                return new(t) { Name = inviter, Applicant = name, Member = GuildMember.Read(r), Notify = notify };
            }
            case 0x13: return new(t) { Name = r.ReadUnicodeString() };
            case 2: return new(t) { Result = r.ReadByte() };
            case 3: return new(t) { Name = r.ReadUnicodeString() };
            case 4: return new(t) { Invite = GuildInviteInfo.Read(r) };
            case 5: return new(t) { Invite = GuildInviteInfo.Read(r), Result = r.ReadByte() };
            case 6: return new(t) { Name = r.ReadUnicodeString(), Accepted = r.ReadByte() == 1, Reason = r.ReadByte() };
            case 0x44: return new(t) { Focus = r.ReadByte(), Value = r.ReadInt() };
            case 0x71: return new(t) { DonationCount = r.ReadInt(), DonationTime = r.ReadLong() };
            // FUN_1413beb10 (guildmanager.cpp:1761)
            case 0xe: return Done(new(t) { Name = r.ReadUnicodeString(), Value = r.ReadInt() }, r);
            // FUN_1413beff0 (:1822): the string is read and dropped
            case 0x41: return Done(new(t) { Text = r.ReadUnicodeString() }, r);
            // FUN_1413c0630 (:2038)
            case 0x18: return Done(new(t) { Name = r.ReadUnicodeString(), Time = r.ReadLong() }, r);
            // FUN_1413c15b0 (:2185): StringIdInterface(packet), then the mark code
            case 0x1b: return Done(new(t) { Changer = StringIdText.Read(r), Code = r.ReadUnicodeString() }, r);
            // FUN_1413c1d30 (:2266): name, Unk_GuildMemberData::ReadPacket
            case 0x20: { string name = r.ReadUnicodeString(); var m = new GuildMember { Mode = 3 }; GuildMember.ReadData(r, m); return Done(new(t) { Name = name, Member = m }, r); }
            // OnPacketMemberDataChanged (:2280): name, Unk_GuildMemberData::FUN_1404c95f0 (one i32)
            case 0x1f: return Done(new(t) { Name = r.ReadUnicodeString(), Value = r.ReadInt() }, r);
            // OnPacketMemberAchievement (:2303)
            case 0x23: return Done(new(t) { Name = r.ReadUnicodeString(), AchieveId = r.ReadInt(), Grade = r.ReadInt(), Kind = (ushort) r.ReadShort() }, r);
            // FUN_1413c3c40 (:2576)
            case 0x3a: return Done(new(t) { GuildId = r.ReadLong(), Name = r.ReadUnicodeString(), Value = r.ReadInt(), Text = r.ReadUnicodeString() }, r);
            // FUN_1413c3780 (:2531): the user, then CGuildCustomEvent::ReadPacket (guilddef_1.cpp:951)
            case 0x38: return Done(new(t) { Name = r.ReadUnicodeString(), EventId = r.ReadInt(), EventTime = r.ReadInt() }, r);
            // FUN_1413c39e0 (:2554): the user, the house level, the house theme
            case 0x39: return Done(new(t) { Name = r.ReadUnicodeString(), HouseLevel = r.ReadInt(), HouseTheme = r.ReadInt() }, r);
            // FUN_1413c3ef0 (:2603): the user, then CGuildNpc::ReadPacket (guilddef_1.cpp:1036: i32 kind, i32 level)
            case 0x3b: return Done(new(t) { Name = r.ReadUnicodeString(), NpcKind = r.ReadInt(), NpcLevel = r.ReadInt() }, r);
            // OnPacketMemberRaidCount (@0x1413c0e70, guildmanager.cpp:2120): the member's name, the raid id, the count
            case 0x25: return Done(new(t) { Name = r.ReadUnicodeString(), RaidId = r.ReadInt(), Value = r.ReadInt() }, r);
            // FUN_1413c2860 (:2372): u8 state
            case 0x27: return Done(new(t) { PvpState = r.ReadByte() }, r);
            // FUN_1413c2c60 (:2416): i32 score, wins, losses
            case 0x29: return Done(new(t) { Score = r.ReadInt(), Wins = r.ReadInt(), Losses = r.ReadInt() }, r);
            // OnPacketPvpHistory (:2810) and OnPacketPvpSeasonHistory (:2831): i32 count, per entry u8 present (1), then the entry
            case 0x4a:
            {
                int n = r.ReadInt(); var list = new List<GuildPvpHistory>();
                for (int i = 0; i < n; i++) if (r.ReadByte() == 1) list.Add(GuildPvpHistory.Read(r));
                return Done(new(t) { PvpHistory = list }, r);
            }
            case 0x4b:
            {
                int n = r.ReadInt(); var list = new List<GuildPvpSeasonHistory>();
                for (int i = 0; i < n; i++) if (r.ReadByte() == 1) list.Add(GuildPvpSeasonHistory.Read(r));
                return Done(new(t) { PvpSeasonHistory = list }, r);
            }
            // FUN_1413c4300 (@0x1413c4300, :2637): the raid records were reset, the packet has no body
            case 0x3e: return Done(new(t), r);
            // FUN_1413c3e50 (:2590): ItemEntity::Read
            case 0x6e: return Done(new(t) { StorageItem = GuildStorageItem.Read(r) }, r);
            // OnPacketStorageGiftResult (@0x1413c5c00, :2985): u8 result, and for 0 the receiver's name
            case 0x6d: { int res = r.ReadByte(); return Done(new(t) { Result = res, Name = res == 0 ? r.ReadUnicodeString() : "" }, r); }
            // OnPacketStorageHistory (@0x1413c5dc0, :2963): i32 count, per line from, to, ItemEntity, i64 time
            case 0x70:
            {
                int n = r.ReadInt(); var list = new List<GuildStorageHistory>();
                for (int i = 0; i < n; i++) list.Add(new(GuildStorageParty.Read(r), GuildStorageParty.Read(r), GuildStorageItem.Read(r), r.ReadLong()));
                return Done(new(t) { StorageHistory = list }, r);
            }
            // OnPacketVsGameTicket (:2653)
            case 0x76: return Done(new(t) { Accepted = r.ReadByte() != 0, Notify = r.ReadByte() != 0 }, r);
            // OnPacketVsGameMatchCanceled (:2670): no body
            case 0x75: return new(t);
            // FUN_1413c45e0 (:2679)
            case 0x77: return Done(new(t) { Value = r.ReadInt() }, r);
            // FUN_1413c4e20 (@0x1413c4e20, :2780): u8 the kind, u8 the code, i32 the argument, then ShowGuildError
            case 0x4f: return Done(new(t) { ErrorKind = r.ReadByte(), ErrorCode = r.ReadByte(), ErrorArg = r.ReadInt() }, r);
            default: return new(t);
        }
    }

    static GuildMessage Done(GuildMessage m, ByteReader r) { r.ExpectEnd($"Guild type {m.Type}"); return m; }
}
