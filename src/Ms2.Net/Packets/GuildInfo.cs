// Layout from commonfiles/guilddef_1.cpp: CGuildInfo::Read (@0x1404cdda0), ReadMembers (@0x1404ce790), ReadGrades
// (@0x1404cea10), ReadSkills, CGuildMember::ReadStream (@0x1404cb6c0), Unk_GuildMemberData::ReadStream (@0x1404c9bd0) and
// FUN_1404c9fe0, CGuildGrade::ReadPacket (@0x1404c71d0), CGuildMemberRaid::ReadStream (@0x1404cb070). Unk_IntTriple is 3 i32.
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>One guild grade (rank): u8 grade, the name, i32 permission bits (valid: grade 0..5, permissions 0..4095).</summary>
public sealed record GuildGrade(int Grade, string Name, int Permissions) {
    public static GuildGrade Read(ByteReader r) => new(r.ReadByte(), r.ReadUnicodeString(), r.ReadInt());
}

/// <summary>One guild skill: i32 id, i32 level, i64 the activation time (0 = not timed).</summary>
public sealed record GuildSkillState(int Id, int Level, long Time) {
    public static GuildSkillState Read(ByteReader r) => new(r.ReadInt(), r.ReadInt(), r.ReadLong());
}

/// <summary>
/// One member, CGuildMember::ReadStream: u8 mode (fb0), u8 grade (fb4); mode 3 is the full record (i64 id, the member data,
/// the memo, i64 fc8/fd0/fd8, i32 fe0/fe4/fe8/fec/ff0, i64 ff8, the raid counts, u8 f158), else i64 id and the short member
/// data (name, i32 f14, i32 f18, i16 level, the code).
/// </summary>
public sealed class GuildMember {
    public int Mode, Grade;
    public int TrophyTotal => Triple.Item1 + Triple.Item2 + Triple.Item3;
    public int RaidTotal => Raid.Values.Sum();
    public long Id;
    // Unk_GuildMemberData
    public long FirstId, CharId; public string Name = ""; public int F10, F14, JobCode, Level, ItemLevel, MapId, Channel; public string Code = "";
    public (int, int, int) Triple; public int F38, F3C, F40; public long F48;
    public string Memo = "";
    public long Fc8, LastSeen, Fd8; public int Fe0, Fe4, Contribution, TotalContribution, Ff0; public long Ff8;
    public Dictionary<int, int> Raid = new();
    public int Offline = 1;

    /// <summary>
    /// Unk_GuildMemberData::ReadStream (@0x1404c9bd0, guilddef_1.cpp:620 ReadPacket): i64, i64, name, u8, i32, i32, i16, i32, i32,
    /// u16, the code, i32 x3, i64, then the three i32 of the triple.
    /// </summary>
    public static void ReadData(ByteReader r, GuildMember m) {
        m.FirstId = r.ReadLong(); m.CharId = r.ReadLong(); m.Name = r.ReadUnicodeString(); m.F10 = r.ReadByte();
        m.F14 = r.ReadInt(); m.JobCode = r.ReadInt(); m.Level = r.ReadShort(); m.ItemLevel = r.ReadInt(); m.MapId = r.ReadInt();
        m.Channel = (ushort)r.ReadShort(); m.Code = r.ReadUnicodeString(); m.F38 = r.ReadInt(); m.F3C = r.ReadInt();
        m.F40 = r.ReadInt(); m.F48 = r.ReadLong(); m.Triple = (r.ReadInt(), r.ReadInt(), r.ReadInt());
    }

    public static GuildMember Read(ByteReader r) {
        var m = new GuildMember { Mode = r.ReadByte(), Grade = r.ReadByte() };
        if (m.Mode == 3) {
            m.Id = r.ReadLong();
            ReadData(r, m);
            m.Memo = r.ReadUnicodeString(); m.Fc8 = r.ReadLong(); m.LastSeen = r.ReadLong(); m.Fd8 = r.ReadLong();
            m.Fe0 = r.ReadInt(); m.Fe4 = r.ReadInt(); m.Contribution = r.ReadInt(); m.TotalContribution = r.ReadInt(); m.Ff0 = r.ReadInt();
            m.Ff8 = r.ReadLong();
            int n = r.ReadInt();
            for (int i = 0; i < n; i++) { int k = r.ReadInt(); m.Raid[k] = r.ReadInt(); }
            m.Offline = r.ReadByte();
        } else {
            m.Id = r.ReadLong();
            m.Name = r.ReadUnicodeString(); m.F14 = r.ReadInt(); m.JobCode = r.ReadInt(); m.Level = r.ReadShort(); m.Code = r.ReadUnicodeString();
        }
        return m;
    }
}

/// <summary>
/// CGuildInfo::Read up to the grades and skills (members, grades and skills are what the dialog's pages show). The custom
/// events, the guild npcs, the npc shop and the storage that follow are not read (STILL-OPEN). Field names follow the
/// client's: id (f18), code (the mark), b1 the capacity (f50), s190 the notice, s188 (f188), the master ids (f48, f50) and name,
/// the creation time (f40), then the nine i32 + the pvp state byte etc. kept as <see cref="Counters"/>.
/// </summary>
public sealed class GuildInfo {
    public long Id; public string Name = "", Code = "";
    public int MaxMembers; public string Tag = "", Notice = "";
    public long MasterFirstId, MasterCharId; public string MasterName = ""; public long CreateTime;
    public int PvpState; public int[] Counters = Array.Empty<int>();   // v6..v13 (8 i32)
    public int B3, V14, Exp, Fund, EventFund; public bool F354; public int F350;
    public List<GuildMember> Members = new();
    public List<GuildGrade> Grades = new();
    public List<GuildSkillState> Skills = new();
    /// <summary>CGuildCustomEvent (CGuildInfo::ReadCustomEvents @0x1404cef10): i32 id, i32 time.</summary>
    public List<(int Id, int Time)> CustomEvents = new();
    /// <summary>The two i32 of CGuildInfo::FUN_1404cf1e0 (@0x1404cf1e0, guilddef_1.cpp:1398) ahead of its string records: the house level (f168,
    /// GetHouseLevel) and the house theme (f16c, GetHouseTheme); both 1 in Reset (guilddef_1.cpp:1058).</summary>
    public int HouseLevel = 1, HouseTheme = 1;
    /// <summary>The string records of FUN_1404cf1e0: i32 key, unicode string, i64, unicode string (FUN_1404d4060).</summary>
    public List<(int Key, string Text, long Value, string Text2)> Records = new();
    /// <summary>CGuildNpc (ReadNpcs @0x1404cf490): i32 kind, i32 level.</summary>
    public List<(int Kind, int Level)> Npcs = new();
    /// <summary>CGuildNpcShopMgr::Read (@0x1404d8e80) shops: u16 id, i64 time, products (i32 item, u8 rank, i32 price, i32 count).</summary>
    public List<(int Id, long Time, List<(int Item, int Rank, int Price, int Count)> Products)> Shops = new();
    /// <summary>CGuildStorage::ReadPacket (@0x1404cc210) entries: ItemEntity (i32 id, u16 rank, i32 count, 4 bool).</summary>
    public int StorageCount;

    public static GuildInfo Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    /// <summary>
    /// GMS2: the upstream Guild.WriteTo. The header after the creation time is u8, i32 1000, seven i32, u8, i32 focus, i32 experience, i32 funds, bool, i32, then
    /// the members (same record as KMS), the ranks, u8 buffs (i32 id, i32 level, i64 expiry), u8 events (i32, i32), i32 house rank, i32 theme, i32 posters
    /// (i32, string, i64, string), u8 npcs (i32, i32), bool, i32 bank items (14 bytes), i32, string, 2 i64, 7 i32.
    /// </summary>
    public static GuildInfo Read(ByteReader r, ProtocolProfile profile) {
        if (profile.IsGms2) return ReadGms2(r);
        return ReadKms(r);
    }

    static GuildInfo ReadGms2(ByteReader r) {
        var g = new GuildInfo { Id = r.ReadLong(), Name = r.ReadUnicodeString(), Code = r.ReadUnicodeString(), MaxMembers = r.ReadByte() };
        g.Tag = r.ReadUnicodeString(); g.Notice = r.ReadUnicodeString();
        g.MasterFirstId = r.ReadLong(); g.MasterCharId = r.ReadLong(); g.MasterName = r.ReadUnicodeString(); g.CreateTime = r.ReadLong();
        r.ReadByte(); r.ReadInt();
        g.Counters = new int[7]; for (int i = 0; i < 7; i++) g.Counters[i] = r.ReadInt();
        g.B3 = r.ReadByte(); g.V14 = r.ReadInt();   // the focus
        g.Exp = r.ReadInt(); g.Fund = r.ReadInt();
        g.F354 = r.ReadBool(); g.F350 = r.ReadInt();
        int n = r.ReadByte(); for (int i = 0; i < n; i++) g.Members.Add(GuildMember.Read(r));
        n = r.ReadByte(); for (int i = 0; i < n; i++) g.Grades.Add(GuildGrade.Read(r));
        n = r.ReadByte(); for (int i = 0; i < n; i++) g.Skills.Add(GuildSkillState.Read(r));
        n = r.ReadByte(); for (int i = 0; i < n; i++) g.CustomEvents.Add((r.ReadInt(), r.ReadInt()));
        g.HouseLevel = r.ReadInt(); g.HouseTheme = r.ReadInt();
        n = r.ReadInt(); for (int i = 0; i < n; i++) { r.ReadInt(); r.ReadUnicodeString(); r.ReadLong(); r.ReadUnicodeString(); }
        n = r.ReadByte(); for (int i = 0; i < n; i++) g.Npcs.Add((r.ReadInt(), r.ReadInt()));
        if (r.ReadBool()) ReadShops(r, g);
        g.StorageCount = r.ReadInt();
        for (int i = 0; i < g.StorageCount; i++) { r.ReadInt(); r.ReadShort(); r.ReadInt(); r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte(); }
        r.ReadInt(); r.ReadUnicodeString(); r.ReadLong(); r.ReadLong();
        for (int i = 0; i < 7; i++) r.ReadInt();
        return g;
    }

    static GuildInfo ReadKms(ByteReader r) {
        var g = new GuildInfo { Id = r.ReadLong(), Name = r.ReadUnicodeString(), Code = r.ReadUnicodeString(), MaxMembers = r.ReadByte() };
        g.Tag = r.ReadUnicodeString(); g.Notice = r.ReadUnicodeString();
        g.MasterFirstId = r.ReadLong(); g.MasterCharId = r.ReadLong(); g.MasterName = r.ReadUnicodeString(); g.CreateTime = r.ReadLong();
        g.PvpState = r.ReadByte();
        g.Counters = new int[8]; for (int i = 0; i < 8; i++) g.Counters[i] = r.ReadInt();
        g.B3 = r.ReadByte(); g.V14 = r.ReadInt(); int v15 = r.ReadInt(), v16 = r.ReadInt(), v17 = r.ReadInt();
        g.Exp = v15; g.Fund = v16; g.EventFund = v17;
        g.F354 = r.ReadBool(); g.F350 = r.ReadInt();
        int n = r.ReadByte(); for (int i = 0; i < n; i++) g.Members.Add(GuildMember.Read(r));
        n = r.ReadByte(); for (int i = 0; i < n; i++) g.Grades.Add(GuildGrade.Read(r));
        n = r.ReadByte(); for (int i = 0; i < n; i++) g.Skills.Add(GuildSkillState.Read(r));
        ReadTail(r, g);
        return g;
    }

    /// <summary>
    /// The end of CGuildInfo::Read (@0x1404cdda0, guilddef_1.cpp:1232): ReadCustomEvents, FUN_1404cf1e0, ReadNpcs, ReadNpcShop
    /// (a bool, then CGuildNpcShopMgr::Read) and the storage (i32 count of ItemEntity).
    /// </summary>
    static void ReadShops(ByteReader r, GuildInfo g) {
        int n = (ushort) r.ReadShort();
        for (int i = 0; i < n; i++) {
            if (!r.ReadBool()) continue;
            int id = (ushort) r.ReadShort(); long time = r.ReadLong(); var products = new List<(int, int, int, int)>();
            int pc = (ushort) r.ReadShort();
            for (int k = 0; k < pc; k++) if (r.ReadBool()) products.Add((r.ReadInt(), r.ReadByte(), r.ReadInt(), r.ReadInt()));
            g.Shops.Add((id, time, products));
        }
    }

    static void ReadTail(ByteReader r, GuildInfo g) {
        int n = r.ReadByte(); for (int i = 0; i < n; i++) g.CustomEvents.Add((r.ReadInt(), r.ReadInt()));
        g.HouseLevel = r.ReadInt(); g.HouseTheme = r.ReadInt();
        n = r.ReadInt(); for (int i = 0; i < n; i++) g.Records.Add((r.ReadInt(), r.ReadUnicodeString(), r.ReadLong(), r.ReadUnicodeString()));
        n = r.ReadByte(); for (int i = 0; i < n; i++) g.Npcs.Add((r.ReadInt(), r.ReadInt()));
        if (r.ReadBool()) ReadShops(r, g);
        g.StorageCount = r.ReadInt();
        for (int i = 0; i < g.StorageCount; i++) { r.ReadInt(); r.ReadShort(); r.ReadInt(); r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte(); }
    }
}
