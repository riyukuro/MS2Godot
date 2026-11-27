using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui;

namespace Ms2.Formats.World;

/// <summary>
/// The dungeon room tables of the client's common code (commonfiles/dungeonroom.cpp, dungeonrewardcoupon.cpp; unit 0x14031c510-0x140343e50):
/// Table/DungeonRoom.xml, DungeonSeason.xml, DungeonConfig.xml, DungeonRankReward.xml, DungeonRoundData.xml and DungeonRewardCoupon.xml
/// (GetXmlPath("Table", file), <see cref="TablePath"/>). The ctor (@0x14031f5d0) loads config, coupons, seasons, round groups, rooms and rank
/// rewards in that order; every loader reads the &lt;ms2&gt; root and takes a missing file or root as no rows. Attributes are read the way the client's
/// XmlGetAttr does (tinyxmlhelper.cpp, TinyXML 2.6): a missing or unreadable attribute leaves the value the ctor gave it
/// (<see cref="XmlAttr"/>).
/// <para>Rows of rooms, seasons, rank rewards and round groups come through FUN_14028faa0 (commonutil.cpp:1748, <see cref="FeatureElements.Collect(PackSet, XElement, string, string)"/>):
/// one row per key picked by locale and feature. Coupons are read directly: the rows IsElementAvailable keeps, a later one of an id replacing the earlier.</para>
/// STILL-OPEN: LoadReverseRaidConfig (@0x140322440) returns early when the launch option FUN_1406af020 is set; that option is not ported, so the
/// reverse raid rooms always load. The coupon's ticketTag is kept as its name; the client turns it into a number with GetTagByName
/// (<see cref="DungeonRewardCoupon.TicketTag"/>).
/// </summary>
public sealed class DungeonRoomTable
{
    /// <summary>The client's DungeonGroupType numbers the room list uses (uidungeonroomdialog.cpp): 1 and 2 are the random match groups, 3 the season rank rooms.</summary>
    public const int GroupMatchNormal = 1, GroupMatchHard = 2, GroupSeason = 3;

    readonly SortedDictionary<uint, DungeonRoomData> rooms = new();
    readonly List<DungeonRoomData> roomList = new();
    readonly Dictionary<int, SortedSet<DungeonRoomData>> byGroup = new();
    readonly SortedSet<DungeonRoomData> matchRooms = new(DungeonRoomData.Order);
    readonly SortedSet<DungeonRoomData> allRooms = new(DungeonRoomData.Order);
    readonly List<DungeonSeasonData> seasons = new();
    readonly SortedDictionary<uint, DungeonRankRequire> rankRequires = new();
    readonly SortedDictionary<uint, DungeonRankRewardBase> rankRewards = new();
    readonly SortedDictionary<uint, DungeonRoundGroup> roundGroups = new();
    readonly SortedDictionary<uint, DungeonRewardCoupon> coupons = new();
    readonly List<DungeonUnitedWeeklyReward> unitedWeekly = new();
    readonly List<uint> reverseRaidRooms = new();
    long reverseStartLocal;
    int reversePeriodDays;
    readonly ClientConstants constants;
    static readonly SortedSet<DungeonRoomData> NoRooms = new(DungeonRoomData.Order);

    /// <summary>One table file's rows: the &lt;ms2&gt; root, null for a missing file or root.</summary>
    public delegate XElement RootOf(string file);
    /// <summary>FUN_14028faa0: the kept child rows of a root by key.</summary>
    public delegate IEnumerable<XElement> Collector(XElement root, string child, string keyAttr);

    /// <summary>The tables of a data set; <paramref name="constants"/> are the client's constants (Common::Constants).</summary>
    public DungeonRoomTable(PackSet packs, ClientConstants constants)
        : this(file => RootFromPacks(packs, file), (root, child, key) => FeatureElements.Collect(packs, root, child, key),
               e => FeatureElements.IsAvailable(packs, e), constants) { }

    /// <summary>The tables from the xml itself (tests): <paramref name="root"/> gives a file's root, <paramref name="collect"/> and <paramref name="available"/>
    /// the feature and locale pick (every row, a later row of a key replacing the earlier, when null).</summary>
    public DungeonRoomTable(RootOf root, Collector collect, Func<XElement, bool> available, ClientConstants constants)
    {
        this.constants = constants ?? new ClientConstants(Array.Empty<KeyValuePair<string, string>>());
        collect ??= LastOfKey;
        available ??= _ => true;
        XElement Ms2(string file) => root(file) is { Name.LocalName: "ms2" } r ? r : null;   // a file whose root is not <ms2> gives no rows
        LoadConfig(Ms2("DungeonConfig.xml"));
        LoadCoupons(Ms2("DungeonRewardCoupon.xml"), available);
        LoadSeasons(Ms2("DungeonSeason.xml"), collect);
        LoadRoundGroups(Ms2("DungeonRoundData.xml"), collect);
        LoadRooms(Ms2("DungeonRoom.xml"), collect);
        LoadRankRewards(Ms2("DungeonRankReward.xml"), collect);
    }

    static XElement RootFromPacks(PackSet packs, string file)
    {
        string entry = TablePath.Resolve(packs, file);
        if (entry == null) return null;
        return Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", entry))?.Root;
    }

    static IEnumerable<XElement> LastOfKey(XElement root, string child, string key)
    {
        var kept = new List<XElement>();
        foreach (var e in root.Elements(child))
        {
            string k = (string)e.Attribute(key) ?? "";
            int at = kept.FindIndex(x => ((string)x.Attribute(key) ?? "") == k);
            if (at >= 0) kept.RemoveAt(at);
            kept.Add(e);
        }
        return kept;
    }

    /// <summary>LoadConfig: the first DungeonConfig, ReverseRaidConfig and UnitedWeeklyReward of the root.</summary>
    void LoadConfig(XElement root)
    {
        if (root == null) return;
        if (root.Element("DungeonConfig") is { } cfg) LoadDungeonConfig(cfg);
        if (root.Element("ReverseRaidConfig") is { } rr) LoadReverseRaidConfig(rr);
        if (root.Element("UnitedWeeklyReward") is { } uw) LoadUnitedWeeklyRewards(uw);
    }

    /// <summary>LoadDungeonConfig (@0x140322370): each MissionRank/group of a non-zero id loads into that id's rank table; a second group of the same id reuses the entry.</summary>
    void LoadDungeonConfig(XElement cfg)
    {
        if (cfg.Element("MissionRank") is not { } rank) return;
        foreach (var g in rank.Elements("group"))
        {
            uint id = 0;
            XmlAttr.UInt(g, "id", ref id);
            if (id == 0) continue;
            if (!rankRequires.TryGetValue(id, out var rr)) rankRequires[id] = rr = new DungeonRankRequire();
            rr.Load(g);
        }
    }

    /// <summary>LoadReverseRaidConfig (@0x140322440): startDate, periodDays and the dungeonID of each v child (non-zero only).</summary>
    void LoadReverseRaidConfig(XElement e)
    {
        XmlAttr.Time(e, "startDate", ref reverseStartLocal);
        XmlAttr.Int(e, "periodDays", ref reversePeriodDays);
        foreach (var v in e.Elements("v"))
        {
            uint id = 0;
            XmlAttr.UInt(v, "dungeonID", ref id);
            if (id != 0) reverseRaidRooms.Add(id);
        }
    }

    /// <summary>LoadUnitedWeeklyRewards (@0x140322530): each v child (DungeonUnitedWeeklyReward::Load always succeeds).</summary>
    void LoadUnitedWeeklyRewards(XElement e)
    {
        foreach (var v in e.Elements("v"))
        {
            var r = new DungeonUnitedWeeklyReward();
            XmlAttr.UInt(v, "rewardCount", ref r.RewardCount);
            XmlAttr.UInt(v, "rewardID", ref r.RewardId);
            XmlAttr.Int(v, "uiPosX", ref r.UiPosX);
            XmlAttr.Bool(v, "uiShowBg", ref r.UiShowBg);
            unitedWeekly.Add(r);
        }
    }

    /// <summary>LoadCoupons (@0x140322000, dungeonroom.cpp:1556): every available dungeonRewardCoupon, a later row of an id replacing the earlier.</summary>
    void LoadCoupons(XElement root, Func<XElement, bool> available)
    {
        if (root == null) return;
        foreach (var e in root.Elements("dungeonRewardCoupon"))
        {
            if (!available(e)) continue;
            var c = DungeonRewardCoupon.Load(e);
            coupons[c.Id] = c;
        }
    }

    /// <summary>LoadSeasons (@0x14031f6e0): the picked DungeonSeason rows by seasonID; one whose rewards are out of order is skipped.</summary>
    void LoadSeasons(XElement root, Collector collect)
    {
        if (root == null) return;
        foreach (var e in collect(root, "DungeonSeason", "seasonID"))
            if (DungeonSeasonData.Load(e) is { } s) seasons.Add(s);
    }

    /// <summary>LoadRoundGroups: the picked DungeonRoundGroup rows by id; id 0 is dropped.</summary>
    void LoadRoundGroups(XElement root, Collector collect)
    {
        if (root == null) return;
        foreach (var e in collect(root, "DungeonRoundGroup", "id"))
            if (DungeonRoundGroup.Load(e) is { } g) roundGroups[g.Id] = g;
    }

    /// <summary>LoadRankRewards: the picked DungeonRankReward rows by rewardID; one with an invalid rank is dropped.</summary>
    void LoadRankRewards(XElement root, Collector collect)
    {
        if (root == null) return;
        foreach (var e in collect(root, "DungeonRankReward", "rewardID"))
            if (DungeonRankRewardBase.Load(e) is { } r) rankRewards[r.RewardId] = r;
    }

    /// <summary>LoadRooms (@0x14031f8a0, dungeonroom.cpp:1054): the picked dungeonRoom rows by dungeonRoomID (a later row of an id replaces the earlier),
    /// then every room in id order: LinkRoom (@0x140321dc0) and SetInit.</summary>
    void LoadRooms(XElement root, Collector collect)
    {
        if (root == null) return;
        foreach (var e in collect(root, "dungeonRoom", "dungeonRoomID"))
        {
            var r = DungeonRoomData.Load(e, constants);
            rooms[r.Id] = r;
        }
        foreach (var r in rooms.Values) LinkRoom(r);
    }

    void LinkRoom(DungeonRoomData r)
    {
        r.CouponLink = GetCoupon(r.ExtraRewardTicketTableId);
        if (r.RoundId != 0) r.RoundGroup = GetRoundGroup(r.RoundId);
        roomList.Add(r);
        AddToSets(r);
    }

    /// <summary>AddToSets (@0x140321eb0): a room of a non-zero group goes into its group's set; groups 1 and 2 also into the match set; it goes into the set of all.</summary>
    void AddToSets(DungeonRoomData r)
    {
        if (r == null || r.GroupType == 0) return;
        GroupSet(r.GroupType).Add(r);
        if (r.GroupType == 1 || r.GroupType == 2) matchRooms.Add(r);
        allRooms.Add(r);
    }

    /// <summary>GetGroupSet (@0x140321fd0): operator[], an empty set for a group without rooms.</summary>
    SortedSet<DungeonRoomData> GroupSet(int group)
    {
        if (!byGroup.TryGetValue(group, out var s)) byGroup[group] = s = new SortedSet<DungeonRoomData>(DungeonRoomData.Order);
        return s;
    }

    // ---- lookups ----

    public int RoomCount => rooms.Count;
    /// <summary>GetRoomList (@0x140321710): every room, by id.</summary>
    public IReadOnlyList<DungeonRoomData> RoomList => roomList;
    /// <summary>GetRoom: the room of an id, null when none.</summary>
    public DungeonRoomData GetRoom(uint id) => rooms.TryGetValue(id, out var r) ? r : null;
    public DungeonRoomData GetRoom1003() => GetRoom(1003);
    public DungeonRoomData GetRoom1004() => GetRoom(1004);
    public DungeonRoomData GetRoom1007() => GetRoom(1007);

    /// <summary>GetRooms of a dungeon (dungeonroom.cpp:1250): the rooms of ids dungeonId*1000+1 .. dungeonId*1000+999, by id.</summary>
    public IEnumerable<DungeonRoomData> GetRoomsOfDungeon(uint dungeonId)
    {
        uint from = DungeonRoomData.MakeRoomId(dungeonId, 1), to = DungeonRoomData.MakeRoomId(dungeonId, 999);
        return rooms.Where(kv => kv.Key >= from && kv.Key <= to).Select(kv => kv.Value);
    }

    /// <summary>GetFirstRoom (@0x140320870): GetRoomIndices of the dungeon (the rooms of ids MakeDungeonRoomId(d,1)..(d,999), in id order), then the room of
    /// the first index; null when the dungeon has none.</summary>
    public DungeonRoomData GetFirstRoom(uint dungeonId) => GetRoomsOfDungeon(dungeonId).FirstOrDefault();

    /// <summary>GetLastRoom: the same with the last index; null when the dungeon has none.</summary>
    public DungeonRoomData GetLastRoom(uint dungeonId) => GetRoomsOfDungeon(dungeonId).LastOrDefault();

    /// <summary>GetCoupon (@0x140322260): the coupon of an id, null for 0 or an unknown id.</summary>
    public DungeonRewardCoupon GetCoupon(uint id) => id != 0 && coupons.TryGetValue(id, out var c) ? c : null;
    /// <summary>GetRoundGroup (@0x1403222f0): null when unknown.</summary>
    public DungeonRoundGroup GetRoundGroup(uint id) => roundGroups.TryGetValue(id, out var g) ? g : null;
    /// <summary>GetRankReward (@0x140321b00): null when unknown.</summary>
    public DungeonRankRewardBase GetRankReward(uint id) => rankRewards.TryGetValue(id, out var r) ? r : null;

    /// <summary>The coupon a room uses (@0x14031e8a0): the table's room of unionRewardID when set (null when it is missing), else its own.</summary>
    public DungeonRewardCoupon GetCoupon(DungeonRoomData room)
    {
        if (room.UnionRewardId != 0) return GetRoom(room.UnionRewardId)?.CouponLink;
        return room.CouponLink;
    }

    /// <summary>GetCouponBaseItemID / GetCouponTicketTag / CanBuyCouponInMeratMarket / GetCouponMaxExtraCount (@0x14031e110-0x14031e340): 0 or false without a coupon.</summary>
    public uint CouponBaseItemId(DungeonRoomData room) => GetCoupon(room)?.BaseItemId ?? 0;
    public bool CanBuyCouponInMeratMarket(DungeonRoomData room) => GetCoupon(room)?.CanBuyInMeratMarket ?? false;
    public int CouponMaxExtraCount(DungeonRoomData room) => (int)(GetCoupon(room)?.MaxExtraCount ?? 0);

    /// <summary>GetExtraRewardTicketCost (@0x14031e2b0): extraRewardTicketBaseCount times the coupon's baseRate at the count clamped into the list; 0 without a coupon.
    /// STILL-OPEN: a coupon with no baseRate rows is indexed out of range in the client; here it costs 0.</summary>
    public uint ExtraRewardTicketCost(DungeonRoomData room, uint count)
    {
        var c = room.CouponLink;
        if (c == null || c.BaseRate.Count == 0) return 0;
        int i = (int)Math.Min(count, (uint)(c.BaseRate.Count - 1));
        return room.ExtraRewardTicketBaseCount * c.BaseRate[i];
    }

    /// <summary>GetRank (@0x140321730): the rank of a score in the room's rank table; -1 without the table or for a negative score.</summary>
    public int GetRank(DungeonRoomData room, short score)
    {
        if (!rankRequires.TryGetValue(room.RankTableId, out var rr)) return -1;
        if (score < 0) return -1;
        return rr.GetRank(score);
    }

    /// <summary>GetMaxScore (@0x1403217e0): 0 without the table.</summary>
    public short GetMaxScore(DungeonRoomData room) => rankRequires.TryGetValue(room.RankTableId, out var rr) ? rr.MaxScore : (short)0;
    /// <summary>GetRankRequire (@0x140321880): the room's rank table, null when none.</summary>
    public DungeonRankRequire GetRankRequire(DungeonRoomData room) => rankRequires.TryGetValue(room.RankTableId, out var rr) ? rr : null;

    /// <summary>IsSeasonRankRoom (@0x14031e480): group 3 at or above the constant DungeonSeasonRankMinLevel.</summary>
    public bool IsSeasonRankRoom(DungeonRoomData room) =>
        room.GroupType == GroupSeason && room.LimitPlayerLevel >= constants.Int("DungeonSeasonRankMinLevel", 60);

    // ---- room lists ----

    /// <summary>GetGroupRooms (@0x140321160): the group's whole set in DungeonRoomLess order.</summary>
    public List<DungeonRoomData> GetGroupRooms(int group) => new(byGroup.TryGetValue(group, out var s) ? s : NoRooms);

    /// <summary>GetRoomsForLevel (@0x140320c30): from the first room at or below (gear score, level) in descending order, the rooms the level may enter.</summary>
    public List<DungeonRoomData> GetRoomsForLevel(ushort level, uint gearScore) => Pick(allRooms, level, gearScore, 0);

    /// <summary>GetMatchRooms (@0x140320da0): the same over the match rooms (groups 1 and 2), stopping after the constant DungeonMatchRecommendPickCount picks when it is non-zero.</summary>
    public List<DungeonRoomData> GetMatchRooms(ushort level, uint gearScore) => Pick(matchRooms, level, gearScore, PickCount);

    /// <summary>GetGroupMatchRooms (@0x140320f80): the same over one group's rooms.</summary>
    public List<DungeonRoomData> GetGroupMatchRooms(int group, ushort level, uint gearScore) =>
        Pick(byGroup.TryGetValue(group, out var s) ? s : NoRooms, level, gearScore, PickCount);

    uint PickCount => (uint)constants.Int("DungeonMatchRecommendPickCount", 6);

    /// <summary>std::lower_bound over the reverse of the set with DungeonRoomLess's (gear score, level) key compare (@0x140335cd0, reversed): the first room, in
    /// descending order, that is not above the key; then each room the level may enter (level not below limitPlayerLevel), up to <paramref name="max"/> when non-zero.</summary>
    static List<DungeonRoomData> Pick(SortedSet<DungeonRoomData> set, ushort level, uint gearScore, uint max)
    {
        var res = new List<DungeonRoomData>();
        bool started = false;
        uint n = 0;
        foreach (var r in set.Reverse())
        {
            if (!started)
            {
                bool above = r.GearScore != gearScore ? r.GearScore > gearScore : r.LimitPlayerLevel != level && r.LimitPlayerLevel > level;
                if (above) continue;
                started = true;
            }
            if (level < r.LimitPlayerLevel) continue;
            res.Add(r);
            n++;
            if (max != 0 && n >= max) break;
        }
        return res;
    }

    /// <summary>GetBossRankingRooms (@0x140321210): the rooms of the group with a bossRanking, in id order, each put into a list by gear score: before the first room with a
    /// larger gear score; at an equal one before it when that room's id is larger, else after it.</summary>
    public List<DungeonRoomData> GetBossRankingRooms(int group)
    {
        var res = new List<DungeonRoomData>();
        foreach (var r in rooms.Values)
        {
            if (r.BossRanking == 0 || r.GroupType != group) continue;
            bool inserted = false;
            for (int i = 0; i < res.Count; i++)
            {
                var o = res[i];
                if (o.GearScore > r.GearScore) { res.Insert(i, r); inserted = true; break; }
                if (o.GearScore == r.GearScore)
                {
                    res.Insert(o.Id > r.Id ? i : i + 1, r);
                    inserted = true;
                    break;
                }
            }
            if (!inserted) res.Add(r);
        }
        return res;
    }

    // ---- seasons ----

    public IReadOnlyList<DungeonSeasonData> Seasons => seasons;
    /// <summary>GetSeasonId (@0x1403214a0): the id of the first season whose period holds the time, 0 when none.</summary>
    public short GetSeasonId(long now, long zoneOffset) => GetCurrentSeason(now, zoneOffset)?.SeasonId ?? 0;
    /// <summary>GetCurrentSeason (@0x140321550): the first season in load order whose period holds the time.</summary>
    public DungeonSeasonData GetCurrentSeason(long now, long zoneOffset) => seasons.FirstOrDefault(s => s.IsInPeriod(now, zoneOffset));
    /// <summary>GetSeason (@0x140321630): the first season of the id.</summary>
    public DungeonSeasonData GetSeason(short id) => seasons.FirstOrDefault(s => s.SeasonId == id);

    // ---- reverse raid and united weekly rewards ----

    /// <summary>GetReverseRaidRoomID (@0x140321910): 0 without rooms, before startDate or without a period; else the room at (whole days since startDate / periodDays) mod the room count.</summary>
    public uint GetReverseRaidRoomId(long now, long zoneOffset)
    {
        if (reverseRaidRooms.Count == 0) return 0;
        long start = reverseStartLocal == 0 ? 0 : reverseStartLocal + zoneOffset;
        if (start > now) return 0;
        if (reversePeriodDays <= 0) return 0;
        int days = (int)((now - start) / 86400);
        return reverseRaidRooms[days / reversePeriodDays % reverseRaidRooms.Count];
    }

    public int UnitedWeeklyRewardCount => unitedWeekly.Count;
    /// <summary>GetUnitedWeeklyReward (@0x140321bf0): null out of range.</summary>
    public DungeonUnitedWeeklyReward GetUnitedWeeklyReward(int i) => i >= 0 && i < unitedWeekly.Count ? unitedWeekly[i] : null;
    /// <summary>FindUnitedWeeklyReward (@0x140321c90): the first one for a reward count.</summary>
    public DungeonUnitedWeeklyReward FindUnitedWeeklyReward(uint rewardCount) => unitedWeekly.FirstOrDefault(r => r.RewardCount == rewardCount);
}

/// <summary>One DungeonRoom.xml row (RTTI DungeonRoom, 0x208 bytes; Load @0x14031d350, dungeonroom.cpp:350-447). A field the row does not give keeps the ctor's zero or false.</summary>
public sealed class DungeonRoomData
{
    public uint Id;
    public int GroupType;
    public ushort DungeonLevel;
    public bool LimitEnter;
    public int ClearKillNpc, SortOrder, CooldownValue;
    public uint RewardCount, SubRewardCount;
    public bool IsAccountReward;
    public uint UnionRewardId, RoundId;
    public long RewardExp;
    public float RewardExpRate;
    public long RewardMeso;
    public List<uint> RewardLimitedDropBoxIds = new(), RewardLimitedDropBoxIds2 = new(), RewardDropBoxIds = new();
    public uint SeasonRankRewardId;
    public int ScoreBonusId;
    public uint ExtraRewardTicketTableId, ExtraRewardTicketBaseCount, LobbyFieldId;
    public List<uint> FieldIds = new();
    public int DurationSeconds;
    public bool IsExpireTimeOut;
    /// <summary>Parse_14021ab60 (commonenum.cpp:841): clock 1, gauge 2, else 0.</summary>
    public int TimerType;
    public uint MinUserCount, MaxUserCount, GearScore;
    public int LimitAchieveId;
    public ushort LimitPlayerLevel;
    /// <summary>limitDayOfWeeks: the listed indices as bits (Unk_Bitset8::Set).</summary>
    public byte LimitDayOfWeeks;
    public bool LimitVip, IsLimitTime, LimitMesoRevival, LimitMeratRevival, LimitRecommendWeapon;
    public List<uint> LimitClearDungeon = new(), LimitAdditionalEffects = new();
    public uint DefaultRevivalLimitCount;
    public string DungeonBanner = "", BossIcon = "";
    public int IsTimeUp;
    public bool IsUseBossIcon;
    /// <summary>ParseBossRankingType: kill 1, damage 2, killrumble 3, multikill 4, colosseum 5, else 0.</summary>
    public int BossRankingType;
    public uint BossRanking;
    public string OpenPeriodTag = "";
    /// <summary>openPeriodDate as CMsTime seconds (<see cref="SeasonDataTable.Time"/>), 0 when not given.</summary>
    public long OpenPeriodDateLocal;
    public bool IsRandomField, IsPartyOnly, IsChangeMaxUser;
    public uint PlayerCountFactorId;
    public int CustomMonsterLevel;
    public bool ChaosDamageMeter, IsMoveOutToBackupField;
    public int DungeonHelperRequireClearCount;
    public List<uint> FindHelperRewardDropBoxIds = new();
    public bool IsUseRandomMatch, IsDisableRandomMatch, IsLeaveAfterCloseReward;
    public List<uint> PartyMissions = new(), UserMissions = new();
    public uint RankTableIdRaw, RepresentId, DungeonScaleStatId;
    /// <summary>m_pRoundGroup and m_pCoupon, linked by the table (LinkRoom).</summary>
    public DungeonRoundGroup RoundGroup;
    public DungeonRewardCoupon CouponLink;

    /// <summary>DungeonRoomLess (@0x140335d90): gear score, level, sort order, id, ascending.</summary>
    public static readonly IComparer<DungeonRoomData> Order = Comparer<DungeonRoomData>.Create((a, b) =>
    {
        if (a.GearScore != b.GearScore) return a.GearScore.CompareTo(b.GearScore);
        if (a.LimitPlayerLevel != b.LimitPlayerLevel) return a.LimitPlayerLevel.CompareTo(b.LimitPlayerLevel);
        if (a.SortOrder != b.SortOrder) return a.SortOrder.CompareTo(b.SortOrder);
        return a.Id.CompareTo(b.Id);
    });

    /// <summary>
    /// The groupType attribute. The KMS build reads it as a number (XmlValue, dungeonroom.cpp:378) and the KMS2 and restore tables hold numbers.
    /// The GMS2 table holds the group's name ("normal", "raid", ...), which XmlValueByName (@0x140179060, additionalactionadditional.cpp:471)
    /// turns into the number with Parse_140218c20 (commonenum.cpp:449, <see cref="DungeonCategoryTable.GroupTypeByName"/>).
    /// STILL-OPEN: the GMS2 client's own DungeonRoom::Load is not recovered; a name is read by the same name table, a number as the KMS build does.
    /// </summary>
    static int ReadGroupType(XElement e, int current)
    {
        string s = XmlAttr.Str(e, "groupType");
        if (s == null) return current;
        string t = s.TrimStart();
        if (t.Length > 0 && (char.IsAsciiDigit(t[0]) || t[0] == '-' || t[0] == '+'))
        {
            XmlAttr.Int(e, "groupType", ref current);
            return current;
        }
        return DungeonCategoryTable.GroupTypeByName(s);
    }

    public static DungeonRoomData Load(XElement e, ClientConstants constants)
    {
        var r = new DungeonRoomData();
        XmlAttr.UInt(e, "dungeonRoomID", ref r.Id);
        XmlAttr.UShort(e, "dungeonLevel", ref r.DungeonLevel);
        XmlAttr.Bool(e, "limitEnter", ref r.LimitEnter);
        r.GroupType = ReadGroupType(e, r.GroupType);
        XmlAttr.Int(e, "clearKillNpc", ref r.ClearKillNpc);
        XmlAttr.Int(e, "cooldownValue", ref r.CooldownValue);
        XmlAttr.Int(e, "sortOrder", ref r.SortOrder);
        XmlAttr.UInt(e, "rewardCount", ref r.RewardCount);
        XmlAttr.UInt(e, "subRewardCount", ref r.SubRewardCount);
        XmlAttr.Bool(e, "isAccountReward", ref r.IsAccountReward);
        XmlAttr.UInt(e, "unionRewardID", ref r.UnionRewardId);
        XmlAttr.UInt(e, "roundID", ref r.RoundId);
        XmlAttr.Long(e, "rewardExp", ref r.RewardExp);
        XmlAttr.Float(e, "rewardExpRate", ref r.RewardExpRate);
        XmlAttr.Long(e, "rewardMeso", ref r.RewardMeso);
        XmlAttr.Int(e, "scoreBonusId", ref r.ScoreBonusId);
        XmlAttr.UIntList(e, "rewardLimitedDropBoxIds", r.RewardLimitedDropBoxIds);
        XmlAttr.UIntList(e, "rewardLimitedDropBoxIds2", r.RewardLimitedDropBoxIds2);
        XmlAttr.UInt(e, "seasonRankRewardID", ref r.SeasonRankRewardId);
        r.RewardDropBoxIds = new List<uint>(r.RewardLimitedDropBoxIds);
        r.RewardDropBoxIds.AddRange(r.RewardLimitedDropBoxIds2);
        XmlAttr.UInt(e, "extraRewardTicketTableID", ref r.ExtraRewardTicketTableId);
        XmlAttr.UInt(e, "extraRewardTicketBaseCount", ref r.ExtraRewardTicketBaseCount);
        XmlAttr.UInt(e, "lobbyFieldID", ref r.LobbyFieldId);
        XmlAttr.UIntList(e, "fieldIDs", r.FieldIds);
        XmlAttr.Int(e, "durationSeconds", ref r.DurationSeconds);
        XmlAttr.Bool(e, "isExpireTimeOut", ref r.IsExpireTimeOut);
        if (XmlAttr.Str(e, "timerType") is { } tt)
            r.TimerType = tt.Equals("Clock", StringComparison.OrdinalIgnoreCase) ? 1 : tt.Equals("Gauge", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        XmlAttr.UInt(e, "minUserCount", ref r.MinUserCount);
        XmlAttr.UInt(e, "maxUserCount", ref r.MaxUserCount);
        XmlAttr.UInt(e, "gearScore", ref r.GearScore);
        XmlAttr.Int(e, "limitAchieveID", ref r.LimitAchieveId);
        XmlAttr.UShort(e, "limitPlayerLevel", ref r.LimitPlayerLevel);
        if (XmlAttr.Str(e, "limitDayOfWeeks") is { } days)
            foreach (var d in XmlAttr.Tokens(days)) { uint i = (uint)XmlAttr.Atoi(d); if (i < 8) r.LimitDayOfWeeks |= (byte)(1 << (int)i); }
        XmlAttr.Bool(e, "limitVIP", ref r.LimitVip);
        XmlAttr.Bool(e, "isLimitTime", ref r.IsLimitTime);
        XmlAttr.Bool(e, "limitMesoRevival", ref r.LimitMesoRevival);
        XmlAttr.Bool(e, "limitMeratRevival", ref r.LimitMeratRevival);
        XmlAttr.UIntList(e, "limitClearDungeon", r.LimitClearDungeon);
        XmlAttr.UIntList(e, "limitAdditionalEffects", r.LimitAdditionalEffects);
        XmlAttr.Bool(e, "limitRecommendWeapon", ref r.LimitRecommendWeapon);
        r.DungeonBanner = XmlAttr.Str(e, "dungeonBanner") ?? "";
        r.BossIcon = XmlAttr.Str(e, "bossIcon") ?? "";
        XmlAttr.Int(e, "isTimeUp", ref r.IsTimeUp);
        XmlAttr.Bool(e, "isUseBossIcon", ref r.IsUseBossIcon);
        if (XmlAttr.Str(e, "BossRankingType") is { } br) r.BossRankingType = ParseBossRankingType(br);
        XmlAttr.UInt(e, "BossRanking", ref r.BossRanking);
        r.OpenPeriodTag = XmlAttr.Str(e, "openPeriodTag") ?? "";
        XmlAttr.Time(e, "openPeriodDate", ref r.OpenPeriodDateLocal);
        XmlAttr.Bool(e, "isRandomField", ref r.IsRandomField);
        XmlAttr.Bool(e, "isPartyOnly", ref r.IsPartyOnly);
        XmlAttr.Bool(e, "isChangeMaxUser", ref r.IsChangeMaxUser);
        XmlAttr.UInt(e, "playerCountFactorID", ref r.PlayerCountFactorId);
        XmlAttr.Int(e, "customMonsterLevel", ref r.CustomMonsterLevel);
        XmlAttr.Bool(e, "chaosDamageMeter", ref r.ChaosDamageMeter);
        XmlAttr.Bool(e, "isMoveOutToBackupField", ref r.IsMoveOutToBackupField);
        XmlAttr.UInt(e, "defaultRevivalLimitCount", ref r.DefaultRevivalLimitCount);
        XmlAttr.Int(e, "dungeonHelperRequireClearCount", ref r.DungeonHelperRequireClearCount);
        XmlAttr.UIntList(e, "findHelperRewardDropBoxIds", r.FindHelperRewardDropBoxIds);
        XmlAttr.Bool(e, "isUseRandomMatch", ref r.IsUseRandomMatch);
        XmlAttr.Bool(e, "isDisableRandomMatch", ref r.IsDisableRandomMatch);
        XmlAttr.Bool(e, "isLeaveAfterCloseReward", ref r.IsLeaveAfterCloseReward);
        XmlAttr.UIntList(e, "partyMissions", r.PartyMissions);
        XmlAttr.UIntList(e, "userMissions", r.UserMissions);
        XmlAttr.UInt(e, "rankTableID", ref r.RankTableIdRaw);
        XmlAttr.UInt(e, "representID", ref r.RepresentId);
        XmlAttr.UInt(e, "dungeonScaleStatID", ref r.DungeonScaleStatId);
        // ApplyConstants (@0x14031e840)
        if (constants.Int("DisableSoloPlayHighLevelDungeon", 0) != 0 && r.GroupType == 1 && r.LimitPlayerLevel >= 50) r.IsPartyOnly = true;
        if (r.BossRankingType == 0 && r.BossRanking != 0) r.BossRankingType = 1;   // "use kill type as default"
        return r;
    }

    /// <summary>ParseBossRankingType (dungeonroom.cpp, case-insensitive).</summary>
    public static int ParseBossRankingType(string s) => s.ToLowerInvariant() switch
    {
        "kill" => 1, "damage" => 2, "killrumble" => 3, "multikill" => 4, "colosseum" => 5, _ => 0,
    };

    public static uint MakeRoomId(uint dungeonId, uint index) => dungeonId * 1000 + index;
    /// <summary>GetDungeonID (@0x14031de20): id / 1000.</summary>
    public uint DungeonId => Id / 1000;
    /// <summary>GetRoomIndex (@0x14031de50): id % 1000.</summary>
    public uint RoomIndex => Id % 1000;
    /// <summary>GetRewardRoomID (@0x14031ddf0).</summary>
    public uint RewardRoomId => UnionRewardId != 0 ? UnionRewardId : Id;
    /// <summary>HasEnterRequirement (@0x14031de80).</summary>
    public bool HasEnterRequirement => LimitPlayerLevel != 0 || GearScore != 0 || LimitAchieveId != 0 || LimitClearDungeon.Count > 0 || LimitRecommendWeapon;
    /// <summary>GetFirstFieldID (@0x14031df20).</summary>
    public uint FirstFieldId => FieldIds.Count == 0 ? 0 : FieldIds[0];
    /// <summary>GetLimitType (@0x14031df70): 2 for limitEnter, else 1.</summary>
    public int LimitType => LimitEnter ? 2 : 1;
    /// <summary>GetCooldownType (@0x14031dfa0): 2 without a cooldown value, else 1.</summary>
    public int CooldownType => CooldownValue == 0 ? 2 : 1;
    /// <summary>GetMaxUserCount (@0x14031e030): maxUserCount, or with isChangeMaxUser the count clamped into 2..maxUserCount.</summary>
    public uint GetMaxUserCount(uint users) => !IsChangeMaxUser ? MaxUserCount : Math.Min(Math.Max(users, 2u), MaxUserCount);
    /// <summary>HasExtraRewardTicket (@0x14031e0d0).</summary>
    public bool HasExtraRewardTicket => ExtraRewardTicketTableId != 0;
    /// <summary>HasMissions (@0x14031e1d0).</summary>
    public bool HasMissions => PartyMissions.Count > 0 || UserMissions.Count > 0;
    /// <summary>GetMissions (@0x14031e3e0): the party missions, then the user missions.</summary>
    public List<uint> Missions() { var l = new List<uint>(PartyMissions); l.AddRange(UserMissions); return l; }
    /// <summary>HasRoundGroup (@0x14031e530).</summary>
    public bool HasRoundGroup => RoundGroup != null;
    /// <summary>IsSingleRound (@0x14031e570): not group 7 and without a round group.</summary>
    public bool IsSingleRound => GroupType != 7 && !HasRoundGroup;
    /// <summary>GetRankTableID (@0x14031e610): rankTableID when set, else by group type (1 to 1, 2 to 2, 3 to 3, 4 to 4, 8 to 6, 12 to 5, else 0).</summary>
    public uint RankTableId => RankTableIdRaw != 0 ? RankTableIdRaw : GroupType switch { 1 => 1u, 2 => 2u, 3 => 3u, 4 => 4u, 8 => 6u, 12 => 5u, _ => 0u };

    /// <summary>IsDungeonLevelInRange (@0x14031e720): within 5 levels of the dungeon's, or both at 50 or more with the level not above the dungeon's.</summary>
    public static bool IsDungeonLevelInRange(ushort level, ushort dungeonLevel)
    {
        int d = level - dungeonLevel;
        if (d >= -5 && d <= 5) return true;
        return dungeonLevel >= 50 && level >= 50 && level <= dungeonLevel;
    }
}

/// <summary>DungeonRewardCoupon (dungeonrewardcoupon.cpp): id, ticketTag, baseItemID, maxExtraCount, the baseRate rows' v and canBuyInMeratMarket (true unless given).</summary>
public sealed class DungeonRewardCoupon
{
    public uint Id, BaseItemId, MaxExtraCount;
    /// <summary>The ticketTag name; the client resolves it with GetTagByName. STILL-OPEN: resolved where the tag table is read (Ms2.Ui).</summary>
    public string TicketTag = "";
    public List<uint> BaseRate = new();
    public bool CanBuyInMeratMarket = true;

    public static DungeonRewardCoupon Load(XElement e)
    {
        var c = new DungeonRewardCoupon();
        XmlAttr.UInt(e, "id", ref c.Id);
        c.TicketTag = XmlAttr.Str(e, "ticketTag") ?? "";
        XmlAttr.UInt(e, "baseItemID", ref c.BaseItemId);
        XmlAttr.UInt(e, "maxExtraCount", ref c.MaxExtraCount);
        foreach (var b in e.Elements("baseRate")) { uint v = 0; XmlAttr.UInt(b, "v", ref v); c.BaseRate.Add(v); }
        XmlAttr.Bool(e, "canBuyInMeratMarket", ref c.CanBuyInMeratMarket);
        return c;
    }
}

/// <summary>DungeonRankRequire (@0x14031ea30): a rank table's maxScore and its rising rank scores.</summary>
public sealed class DungeonRankRequire
{
    public short MaxScore;
    public readonly List<short> Scores = new();

    /// <summary>Load: maxScore, then each rank child's score; a score not above the previous (the first needs more than -1) ends the list, keeping what was read.</summary>
    public void Load(XElement e)
    {
        XmlAttr.Short(e, "maxScore", ref MaxScore);
        int prev = -1;
        foreach (var c in e.Elements("rank"))
        {
            int score = 0;
            XmlAttr.Int(c, "score", ref score);
            if (prev >= score) return;
            prev = score;
            Scores.Add((short)score);
        }
    }

    /// <summary>GetRank (@0x14031ebb0): from the last score down, the first one the score reaches gives its index + 1; else 0.</summary>
    public int GetRank(short score)
    {
        for (int i = Scores.Count - 1; i >= 0; i--) if (score >= Scores[i]) return i + 1;
        return 0;
    }
    public int Count => Scores.Count;
    /// <summary>GetScore (@0x14031ec70): 0 out of range.</summary>
    public short GetScore(int i) => i >= 0 && i < Scores.Count ? Scores[i] : (short)0;
}

/// <summary>DungeonRankRewardBase (@0x14031cb30): the reward of ranks 1..5 of a rewardID.</summary>
public sealed class DungeonRankRewardBase
{
    public uint RewardId;
    public int MaxRank;
    readonly (uint ItemId, int SystemMailId)[] items = new (uint, int)[6];

    /// <summary>Load: null when a v child gives a rank outside 1..5 (the reward is dropped).</summary>
    public static DungeonRankRewardBase Load(XElement e)
    {
        var r = new DungeonRankRewardBase();
        XmlAttr.UInt(e, "rewardID", ref r.RewardId);
        foreach (var v in e.Elements("v"))
        {
            int rank = 0;
            XmlAttr.Int(v, "rank", ref rank);
            if (rank <= 0 || rank > 5) return null;
            if (r.MaxRank < rank) r.MaxRank = rank;
            uint item = 0; int mail = 0;
            XmlAttr.UInt(v, "itemID", ref item);
            XmlAttr.Int(v, "systemMailID", ref mail);
            r.items[rank] = (item, mail);
        }
        return r;
    }

    /// <summary>GetItemId (@0x14031cd10): 0 for a rank outside the six entries.</summary>
    public uint GetItemId(int rank) => (uint)rank < 6 ? items[rank].ItemId : 0;
    /// <summary>GetSystemMailId (@0x14031cd70).</summary>
    public int GetSystemMailId(int rank) => (uint)rank < 6 ? items[rank].SystemMailId : 0;
}

/// <summary>DungeonSeason (@0x14031ed90): a season's period, mail, weekday mask and rewards by required score.</summary>
public sealed class DungeonSeasonData
{
    public short SeasonId;
    public uint MaxSeasonScore;
    public int SystemMailId, DayOfWeeks;
    /// <summary>startTime and endTime as CMsTime seconds (<see cref="SeasonDataTable.Time"/>).</summary>
    public long StartLocal, EndLocal;
    public readonly List<Reward> Rewards = new();

    /// <summary>DungeonSeasonItemGift (@0x14031e9a0): the ItemEntity's itemID, grade and count, and requireScore.</summary>
    public sealed record Reward(uint ItemId, int Grade, uint Count, uint RequireScore);

    /// <summary>Load: null when more than two rewards are not in strictly falling requireScore order (the season is skipped).</summary>
    public static DungeonSeasonData Load(XElement e)
    {
        var s = new DungeonSeasonData();
        XmlAttr.Short(e, "seasonID", ref s.SeasonId);
        XmlAttr.UInt(e, "maxSeasonScore", ref s.MaxSeasonScore);
        XmlAttr.Int(e, "systemMailID", ref s.SystemMailId);
        XmlAttr.Int(e, "dayOfWeeks", ref s.DayOfWeeks);
        XmlAttr.Time(e, "startTime", ref s.StartLocal);
        XmlAttr.Time(e, "endTime", ref s.EndLocal);
        foreach (var c in e.Elements("Reward"))
        {
            uint item = 0, count = 0, req = 0; int grade = 0;
            XmlAttr.UInt(c, "itemID", ref item);
            XmlAttr.Int(c, "grade", ref grade);
            XmlAttr.UInt(c, "count", ref count);
            XmlAttr.UInt(c, "requireScore", ref req);
            s.Rewards.Add(new Reward(item, grade, count, req));
        }
        if (s.Rewards.Count > 2)
            for (int i = 0; i < s.Rewards.Count - 1; i++)
                if (s.Rewards[i].RequireScore <= s.Rewards[i + 1].RequireScore) return null;
        return s;
    }

    /// <summary>IsInPeriod (@0x14031f030): TimeRange::IsInRange, start at or before the time and the time before the end (the local seconds plus the zone offset, 0 staying 0).</summary>
    public bool IsInPeriod(long now, long zoneOffset)
    {
        long start = StartLocal == 0 ? 0 : StartLocal + zoneOffset, end = EndLocal == 0 ? 0 : EndLocal + zoneOffset;
        return start <= now && now < end;
    }

    /// <summary>GetReward (@0x14031f060): the first reward the score reaches, null when none.</summary>
    public Reward GetReward(uint score) => Rewards.FirstOrDefault(r => r.RequireScore <= score);
}

/// <summary>DungeonRoundGroup (dungeonroom.cpp:995): the rounds of a multi-round room.</summary>
public sealed class DungeonRoundGroup
{
    public uint Id;
    public readonly List<(uint RewardId, uint GearScore)> Rounds = new();

    /// <summary>Load: null for id 0.</summary>
    public static DungeonRoundGroup Load(XElement e)
    {
        var g = new DungeonRoundGroup();
        XmlAttr.UInt(e, "id", ref g.Id);
        if (g.Id == 0) return null;
        foreach (var v in e.Elements("v"))
        {
            uint reward = 0, gear = 0;
            XmlAttr.UInt(v, "rewardID", ref reward);
            XmlAttr.UInt(v, "gearScore", ref gear);
            g.Rounds.Add((reward, gear));
        }
        return g;
    }

    /// <summary>GetRewardIds (GetRoundRewardIds @0x14031e5b0): the rewardIDs of the rounds from..to-1, to capped at the count.</summary>
    public List<uint> GetRewardIds(int from, int to)
    {
        var l = new List<uint>();
        for (int i = from; i < Math.Min(to, Rounds.Count); i++) l.Add(Rounds[i].RewardId);
        return l;
    }
}

/// <summary>DungeonUnitedWeeklyReward (@0x14031f140): the united weekly reward row.</summary>
public sealed class DungeonUnitedWeeklyReward
{
    public uint RewardCount, RewardId;
    public int UiPosX;
    public bool UiShowBg;
}

/// <summary>
/// XmlGetAttr (tinyxmlhelper.cpp 60-230): TinyXML 2.6's Query*Attribute, the target left as it is when the attribute is missing or does not parse.
/// Integers take sscanf "%d" (a leading number; none fails), unsigned and short targets the int cast; 64-bit ones _atoi64; floats sscanf "%lf";
/// bools true/yes/1 and false/no/0 in any case (an empty or other text fails). STILL-OPEN: a value that overflows int is wrapped here, the C runtime's behaviour is undefined.
/// </summary>
static class XmlAttr
{
    public static string Str(XElement e, string name) => (string)e.Attribute(name);

    static bool ScanInt(string s, out int v)
    {
        v = 0;
        if (s == null) return false;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        int start = i;
        long r = 0;
        for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++) r = unchecked(r * 10 + (s[i] - '0'));
        if (i == start) return false;
        v = unchecked((int)(neg ? -r : r));
        return true;
    }

    public static void Int(XElement e, string name, ref int v) { if (ScanInt(Str(e, name), out var x)) v = x; }
    public static void UInt(XElement e, string name, ref uint v) { if (ScanInt(Str(e, name), out var x)) v = unchecked((uint)x); }
    public static void Short(XElement e, string name, ref short v) { if (ScanInt(Str(e, name), out var x)) v = unchecked((short)x); }
    public static void UShort(XElement e, string name, ref ushort v) { if (ScanInt(Str(e, name), out var x)) v = unchecked((ushort)x); }

    /// <summary>_atoi64 / _strtoui64: the leading number, 0 without one, for any present attribute.</summary>
    public static void Long(XElement e, string name, ref long v)
    {
        string s = Str(e, name);
        if (s == null) return;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        long r = 0;
        for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++) r = unchecked(r * 10 + (s[i] - '0'));
        v = neg ? -r : r;
    }

    public static void Float(XElement e, string name, ref float v)
    {
        string s = Str(e, name);
        if (s == null) return;
        s = s.TrimStart();
        int end = 0;
        if (end < s.Length && (s[end] == '-' || s[end] == '+')) end++;
        int digits = 0;
        while (end < s.Length && char.IsAsciiDigit(s[end])) { end++; digits++; }
        if (end < s.Length && s[end] == '.') { end++; while (end < s.Length && char.IsAsciiDigit(s[end])) { end++; digits++; } }
        if (digits == 0) return;
        if (end < s.Length && (s[end] == 'e' || s[end] == 'E'))
        {
            int x = end + 1;
            if (x < s.Length && (s[x] == '-' || s[x] == '+')) x++;
            if (x < s.Length && char.IsAsciiDigit(s[x])) { while (x < s.Length && char.IsAsciiDigit(s[x])) x++; end = x; }
        }
        if (double.TryParse(s.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) v = (float)d;
    }

    public static void Bool(XElement e, string name, ref bool v)
    {
        string s = Str(e, name);
        if (string.IsNullOrEmpty(s)) return;
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s == "1") v = true;
        else if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s.Equals("no", StringComparison.OrdinalIgnoreCase) || s == "0") v = false;
    }

    /// <summary>A CMsTime attribute (FUN_140718950): <see cref="SeasonDataTable.Time"/>; an empty or missing text leaves it.</summary>
    public static void Time(XElement e, string name, ref long v)
    {
        string s = Str(e, name);
        if (!string.IsNullOrEmpty(s)) v = SeasonDataTable.Time(s);
    }

    /// <summary>CString::Tokenize(",") : the non-empty parts.</summary>
    public static string[] Tokens(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public static int Atoi(string s) => ScanInt(s, out var v) ? v : 0;

    /// <summary>XmlTokenList of unsigned (0x140178770): the comma separated values as numbers (a part without a number gives 0).</summary>
    public static void UIntList(XElement e, string name, List<uint> list)
    {
        string s = Str(e, name);
        if (s == null) return;
        foreach (var t in Tokens(s)) list.Add(unchecked((uint)Atoi(t)));
    }
}
