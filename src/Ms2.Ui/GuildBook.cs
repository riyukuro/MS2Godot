using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>CGuildXmlData::GuildProperty's fundMax (f28) and donationMax (f30) of one guild level.</summary>
public readonly record struct GuildProperty(long FundMax, int DonationMax);

/// <summary>
/// The guild state the guild dialog reads, CGuildManager (client/ms2/guildmanager.cpp), with what its type switch
/// FUN_1413b6370 (:301) does on the Guild packets (0x8A) the port reads, and its checked sends (SendOp::Guild 0x4B). The
/// client's UI calls come out as events. The not-in-guild side: the guild id, this character's join requests
/// (m_joinRequestsForChar), the search results and the search propensity (m_468). The in-guild side: the guild info, the
/// join requests sent to the guild (m_joinRequestsForGuild), the members and the propensity (type 0x44); the member
/// sends (invite, expel, grade, master, notice, memo, propensity); the invite received (m_invite) and its answer; attend
/// and donate. STILL-OPEN: SendPacketThrottled's delay on the attend and donate sends (its body is not in the source, so
/// they go out at once); the other sends (the grade edit, the PvP, the house); the guild name tag (s2c 0x4d / 0x4e,
/// FUN_1413c7700 guildmanager.cpp:3108: the name shown over a player) is read and raised as <see cref="HeadGuildName"/>, but
/// the name tags take the guild line only from FieldAddUser, so a tag keeps its guild name until the player is added again.
/// </summary>
public sealed class GuildBook
{
    readonly List<GuildJoinRequest> myRequests = new();     // m_joinRequestsForChar
    readonly List<GuildJoinRequestForGuild> requests = new();   // m_joinRequestsForGuild
    readonly List<GuildSearchEntry> results = new();        // the search results (ClearSearchResults clears them)

    /// <summary>GetStringCommon: the common string of an id.</summary>
    public Func<int, string> Common = _ => "";
    /// <summary>GetStringCommonByKey: the common string of a key.</summary>
    public Func<string, string> CommonByKey = _ => "";
    /// <summary>FUN_140710590: the send (SendOp::Guild 0x4B).</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>CBanTextChecker::CheckGuildName (CommonString GetChecker): null when the name passes, else the message. The
    /// client skips the check without a checker; so does a null here.</summary>
    public Func<string, string> CheckName;
    /// <summary>CheckName's (@0x141276c80, uihelpers.cpp:859) CBanTextChecker::FUN_140225210: true when a character name
    /// is refused. The client skips the check without a checker; so does a null here.</summary>
    public Func<string, bool> BannedName;
    /// <summary>CBanTextChecker::FUN_1402259b0: true when a text holds a banned word (the notice check of FUN_1413b7c70).</summary>
    public Func<string, bool> BannedWord;
    /// <summary>CBanTextChecker::CheckContentText: null when a text passes, else the message.</summary>
    public Func<string, string> CheckContent;
    /// <summary>CBanTextChecker::CheckGuildGradeName: null when a grade name passes, else the message. The dialog's
    /// OnChangeGrade skips the check without a checker; so does a null here.</summary>
    public Func<string, string> CheckGradeName;
    /// <summary>The local player's level (PCBase vfunc_78), or null without a local player.</summary>
    public Func<int?> Level = () => null;
    /// <summary>The local player's name (CGuildManager::GetMyName), or null without a local player.</summary>
    public Func<string> PlayerName = () => null;
    /// <summary>Unk_GuildClientOptions::m_refuseGuildInvite (the community option disableGuildRequest): an invite received
    /// is refused at once.</summary>
    public Func<bool> RefuseInvite = () => false;
    /// <summary>The local player's GetPvPInfo()->HasPoint(): an invite received is refused at once. STILL-OPEN: the HUD has
    /// no PvP state yet, so false (as for the party invite).</summary>
    public Func<bool> PvpPoint = () => false;
    /// <summary>GetServerTime (seconds).</summary>
    public Func<long> ServerTime = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    /// <summary>CGuildXmlData::GetLevelByExp.</summary>
    public Func<int, int> LevelByExp = _ => 0;
    /// <summary>CGuildXmlData::GetProperty(level), or null without one.</summary>
    public Func<int, GuildProperty?> Property = _ => null;
    /// <summary>Client::Constants guildDonateMeso: the meso of one donation.</summary>
    public int DonateMeso = 10000;
    /// <summary>DAT_143ab2898: the local player's meso.</summary>
    public Func<long> Mesos = () => 0;
    /// <summary>CGameEventValueManager GuildFund()->GetRate(): the fund bonus of a donation (1/10000). STILL-OPEN: the game
    /// events are read but not kept, so 0.</summary>
    public Func<int> FundEventRate = () => 0;
    /// <summary>Client::Constants GuildNameLengthMin and GuildCreateMinLevel.</summary>
    public int NameLengthMin = 2, CreateMinLevel;

    /// <summary>ShowSystemNotice(text, 1, 1): a notice of the text, its type and whether it also goes to the chat log.</summary>
    public event Action<string, int, bool> Notice;
    /// <summary>FUN_1412751b0(text, 0xd): a chat log line of the kind.</summary>
    public event Action<string, int> ChatLine;
    /// <summary>PlaySystemSound(key).</summary>
    public event Action<string> Sound;
    /// <summary>ShowMessageBox(text, 0, null, ""): a message box.</summary>
    public event Action<string> Box;
    /// <summary>The invite box of type 4 (FUN_1413bdbb0): a yes / no box of FormatStringId(0x38c, inviter, guild name), tag
    /// "guildinvite"; see GuildInviteBox.</summary>
    public event Action<GuildInviteInfo> InviteBox;
    /// <summary>CloseDialog(0x4e4f): the guild dialog closes.</summary>
    public event Action DialogClose;
    /// <summary>The open guild dialog's FUN_14196d0c0(page): the search result page again (0 keeps the page).</summary>
    public event Action<int> SearchRefresh;
    /// <summary>The open guild dialog's FUN_14196d660: this character's join requests again.</summary>
    public event Action MyRequestsRefresh;
    /// <summary>The open guild dialog's FUN_14196da70: the requests for the guild again.</summary>
    public event Action RequestsForGuildRefresh;
    /// <summary>The open guild dialog's UpdateGuildInfo: the header (the member count) changed.</summary>
    public event Action GuildInfoRefresh;
    /// <summary>The open guild dialog's FUN_141973b20(name): one member's row again, then the master and the player's
    /// rights (the client calls it also when the row's member is gone from the list).</summary>
    public event Action<string> MemberRefresh;
    /// <summary>The open guild dialog's RemoveMember(name).</summary>
    public event Action<string> MemberRemoved;

    /// <summary>The open guild dialog's UpdateEventButtons (FUN_141968140, uiguilddialog.cpp:1247): the vs game registration changed (modes 0x76, 0x77).</summary>
    public event Action VsGameRefresh;
    /// <summary>The open guild dialog's FUN_141970900: the event rows again (mode 0x38).</summary>
    public event Action EventsRefresh;

    /// <summary>The open guild dialog's FUN_141971490 (the house part of the House page) after the house was upgraded (mode 0x39).</summary>
    public event Action HouseRefresh;
    /// <summary>The open guild dialog's FUN_141971ac0 (the npc part of the House page) after a guild npc was upgraded (modes 0x39, 0x3b).</summary>
    public event Action NpcRefresh;

    /// <summary>The open guild dialog's UpdateStoragePage (uiguilddialog.cpp:2640) after a storage entry changed (mode 0x6e).</summary>
    public event Action StorageRefresh;
    /// <summary>The open guild dialog's UpdateRaidScore (@0x141972fc0) and FUN_141973220 (@0x141973220) after the raid records were reset (mode 0x3e).</summary>
    public event Action RaidRefresh;
    /// <summary>OnPacketGuildExp (mode 0x32): the open guild dialog's UpdateGuildExp (@0x14196e0a0); true when the guild level
    /// changed, which also redraws the skill page (FUN_14196e590) and the npc info (FUN_141971ac0).</summary>
    public event Action<bool> ExpRefresh;
    /// <summary>Modes 0x33 (the fund) and 0x34 (the event fund): the open guild dialog's FUN_14196e2c0 (uiguilddialog.cpp:2081).</summary>
    public event Action FundRefresh;
    /// <summary>The open guild dialog's FUN_14196e590 (the guild skill page) after a member left or was expelled (modes 0x14,
    /// 0x21) or a guild skill changed (0x36, 0x37).</summary>
    public event Action SkillsRefresh;
    /// <summary>The open guild dialog's FUN_14196f830 (the personal skill page) after a personal skill was upgraded (0x37) or
    /// a pending skill use's effect came (<see cref="OwnEffectAdded"/>).</summary>
    public event Action PersonalSkillsRefresh;
    /// <summary>The open guild dialog's UpdateAttendBackground after the player's own contribution time (mode 0x24).</summary>
    public event Action AttendRefresh;
    /// <summary>The own contribution time (mode 0x24) also calls the game menu's FUN_1419381c0 (its guild entry's count).</summary>
    public event Action GameMenuGuildRefresh;
    /// <summary>The open guild dialog's SetGuildName (@0x141974190) after the guild was renamed (mode 0x22).</summary>
    public event Action<string> GuildNameRefresh;
    /// <summary>The open guild dialog's RefreshMembersOfGrade (@0x141973f60) after a grade was set (mode 0x1d).</summary>
    public event Action<int> GradeRefresh;
    /// <summary>The open guild dialog's SetGuildMarkFromCode (@0x141974120) after the mark changed (mode 0x1b).</summary>
    public event Action<string> MarkRefresh;
    /// <summary>PC::vfunc_129 (the guild name over a player's head) of the player of the name: modes 0x4d (set) and 0x4e (cleared,
    /// ""), and 0x22 for the local player.</summary>
    public event Action<string, string> HeadGuildName;
    /// <summary>The buddy list's FindByName: a buddy gets the buddy's own login notice, so the guild line is not printed (modes
    /// 0x17, 0x18).</summary>
    public Func<string, bool> IsBuddy = _ => false;
    /// <summary>CUIService::IsDialogOpen(0x4e4f) / FindGuildDialog(0x4e4f): the guild dialog is open (modes 0x5b, 0x26, 0x35).</summary>
    public Func<bool> DialogOpen = () => false;
    /// <summary>CStringTable::FUN_141812290(effectId, level, "name"): an additional effect's name (the guild skill lines).</summary>
    public Func<uint, ushort, string> EffectName = (_, _) => "";
    /// <summary>StringIdInterface's text (the notice texts): a changer as sent (modes 0x1b, 0x1d).</summary>
    public Func<StringIdText, string> IdText = t => t?.Text ?? "";
    /// <summary>FUN_140763e10: a number with the thousands separators.</summary>
    public Func<long, string> GroupText = n => NumberText.Group(n.ToString(System.Globalization.CultureInfo.InvariantCulture));
    /// <summary>CGuildManager m_470: the (effect id, effect level) of the skill uses sent with the dialog open (mode 0x5b) and
    /// not yet seen as an own effect.</summary>
    readonly HashSet<(int Id, int Level)> pendingSkillEffects = new();
    public IReadOnlyCollection<(int Id, int Level)> PendingSkillEffects => pendingSkillEffects;
    /// <summary>The open guild dialog's UpdateStorageHistory (@0x1419729d0) after the history came (mode 0x70) or was asked for again.</summary>
    public event Action StorageHistoryRefresh;
    /// <summary>CGuildStorage's history deque (f38, at most 0x32 lines, the oldest dropped first) and m_400: the history was received.</summary>
    readonly List<GuildStorageHistory> storageHistory = new();
    public IReadOnlyList<GuildStorageHistory> StorageHistory => storageHistory;
    public bool StorageHistoryReceived { get; private set; }
    /// <summary>The constant guildStorageGiftNeedPeriod (default 7, unit_1402ab440_constantsblocks.cpp:952).</summary>
    public Func<int> GuildStorageGiftNeedPeriod = () => 7;
    /// <summary>CItemData::GetSlotMax of an item id (FUN_14053f870 then GetSlotMax); null when there is no item data.
    /// STILL-OPEN: without it <see cref="SendStorageGift"/> sends nothing.</summary>
    public Func<int, int?> ItemSlotMax;
    /// <summary>CItemData::HasStringTag(0xd7) of an item id. STILL-OPEN: without it <see cref="SendStorageGift"/> sends nothing.</summary>
    public Func<int, bool> ItemHasStringTag0xd7;

    /// <summary>CGuildInfo::GetStorageItemCount (@0x1404d2db0): the count of (item id, rank), 0 when absent.</summary>
    public int StorageItemCount(int itemId, int rank) =>
        Info?.StorageItems.Find(i => i.ItemId == itemId && i.Rank == rank).Count ?? 0;

    /// <summary>CGuildInfo::SetStorageItem (@0x1404d2cc0): the entry of the key is replaced, a count of 0 removes it.</summary>
    void SetStorageItem(GuildStorageItem item)
    {
        if (Info == null) return;
        Info.StorageItems.RemoveAll(i => i.ItemId == item.ItemId && i.Rank == item.Rank);
        if (item.Count != 0) Info.StorageItems.Add(item);
    }

    /// <summary>CGuildManager::SendGuildMode112 (@0x1413bbb80, guildmanager.cpp:1095): the history not received yet is asked for; else
    /// the dialog's UpdateStorageHistory runs again.</summary>
    public void SendGuildMode112()
    {
        if (!StorageHistoryReceived) Send(new GuildStorageHistoryRequest());
        else StorageHistoryRefresh?.Invoke();
    }

    /// <summary>CGuildManager::FUN_1413bb490 (@0x1413bb490, guildmanager.cpp:1046): send a storage item to a member. Outside a guild
    /// nothing; not the master common 0x416; no receiver s_guild_err_empty_member; unknown member common 0x40d; count 0
    /// s_guild_err_empty_count; no item data nothing; over the slot max s_guild_err_storage_over_slot_max_count; more than stored
    /// s_guild_err_storage_not_enough_count; an item with the string tag 0xd7 only to oneself s_guild_err_storage_send_only_to_me.
    /// STILL-OPEN: SendPacketThrottled(0x6d, 1000)'s throttle is not ported (sent at once); the item data and tag hooks must be set.</summary>
    public bool SendStorageGift(GuildStorageItem item, string receiver)
    {
        if (!InGuild) return false;
        if (PlayerName() is not { } me || !IsMaster(me)) { NoticeId(0x416); return false; }
        if (receiver.Length == 0) return NoticeKey("s_guild_err_empty_member");
        if (FindMemberByName(receiver) == null) { NoticeId(0x40d); return false; }
        if (item.Count == 0) return NoticeKey("s_guild_err_empty_count");
        int stored = Math.Max(StorageItemCount(item.ItemId, item.Rank), 0);
        if (ItemSlotMax?.Invoke(item.ItemId) is not { } slotMax) return false;
        if ((uint) slotMax < (uint) item.Count) { Notice?.Invoke(ClientFormat.Format(CommonByKey("s_guild_err_storage_over_slot_max_count"), (uint) slotMax), 1, true); return false; }
        if ((uint) stored < (uint) item.Count) return NoticeKey("s_guild_err_storage_not_enough_count");
        if (ItemHasStringTag0xd7 == null) return false;
        if (ItemHasStringTag0xd7(item.ItemId) && receiver != me) return NoticeKey("s_guild_err_storage_send_only_to_me");
        Send(new GuildStorageGiftRequest(item, receiver));
        return true;
    }

    /// <summary>CGuildXmlData's guild events (LoadGuildEvent), for the notice of an opened event.</summary>
    public Ms2.Formats.World.GuildEventTable Events;
    /// <summary>CGuildXmlData's raid table (LoadGuildRaid).</summary>
    public Ms2.Formats.World.GuildRaidTable Raids;
    /// <summary>CSeasonDataTable's guild PvP category (SeasonData_GuildPvP.xml), the table CGuildInfo::UpdateSeason (@0x1404d31b0) reads.</summary>
    public SeasonDataTable Seasons;
    /// <summary>CMsTime::CalcZoneOffset, for the season's dates.</summary>
    public Func<long> ZoneOffset = () => Ms2.Net.MsTime.CalcZoneOffset;

    /// <summary>Unk_GuildSeason (guilddef_1.cpp:134-150): the season the guild's rating counts in, id, display name and the score where each of
    /// the seven grades starts (points[1..7]).</summary>
    public sealed class GuildSeason
    {
        public int Id;
        public string Name = "";
        public int[] Points = new int[8];
        /// <summary>The season's begin and end (the instants, UpdateChampionshipInfo's period text).</summary>
        public long Begin, End;

        /// <summary>GetRankPoints (@0x1404c60e0): 0 outside 1..7.</summary>
        public int GetRankPoints(int rank) => rank < 1 || rank >= 8 ? 0 : Points[rank];
        /// <summary>GetRankForScore (@0x1404c6110): the grade below the first one whose points are above the score, else 7.</summary>
        public int GetRankForScore(int score)
        {
            for (int i = 1; i < 8; i++) if (Points[i] > score) return i - 1;
            return 7;
        }
    }

    readonly GuildSeason season = new();

    /// <summary>CGuildInfo::GetSeason (@0x1404d3190) with UpdateSeason (@0x1404d31b0, guilddef_1.cpp:2209): no current season resets it; a changed
    /// season id copies the id, the display name and the seven grades.
    /// STILL-OPEN: Unk_GuildSeason::Reset's values (taken as zero); the begin and end are copied with the id (UpdateChampionshipInfo reads them).</summary>
    public GuildSeason GetSeason()
    {
        var cur = Seasons?.Current(ServerTime(), ZoneOffset());
        if (cur == null) { season.Id = 0; season.Name = ""; season.Begin = season.End = 0; Array.Clear(season.Points); return season; }
        if (season.Id != cur.Id)
        {
            season.Id = cur.Id; season.Name = cur.DisplayName;
            long zone = ZoneOffset(); season.Begin = cur.Start(zone); season.End = cur.End(zone);
            for (int i = 0; i < 7; i++) season.Points[i + 1] = cur.Grades[i];
        }
        return season;
    }

    // CGuildInfo f130 (PvpState), f134 (score), f138 (wins), f13c (losses): read from the guild info packet, set by modes 0x27 and 0x29.
    public int PvpState { get; private set; }
    public int Score { get; private set; }
    public int WinCount { get; private set; }
    public int LoseCount { get; private set; }

    /// <summary>The open guild dialog's UpdateChampionshipRating (@0x14196fd20, uiguilddialog.cpp:1815) after the state or the score changed (modes 0x27, 0x29).</summary>
    public event Action ChampionshipRatingRefresh;
    /// <summary>The open guild dialog's FUN_141974240 (the battle rows, :2806) after mode 0x4a, and FUN_141974640 (the season rows, :2860) after 0x4b
    /// or when the second request is not needed.</summary>
    public event Action PvpHistoryRefresh;
    public event Action PvpSeasonHistoryRefresh;

    /// <summary>CGuildManager's m_pvpHistory (kept to 10, the oldest dropped), m_pvpSeasonHistory, m_lastPvpHistoryId (the newest entry's time) and
    /// m_3e0 (the season history was asked for).</summary>
    readonly List<GuildPvpHistory> pvpHistory = new();
    readonly List<GuildPvpSeasonHistory> pvpSeasonHistory = new();
    public long LastPvpHistoryId { get; private set; }
    public bool PvpSeasonHistoryRequested { get; private set; }
    /// <summary>GetPvpHistoryNewestFirst / GetPvpSeasonHistoryNewestFirst (@0x1413bbf70, @0x1413bbfe0).</summary>
    public IEnumerable<GuildPvpHistory> PvpHistoryNewestFirst => Enumerable.Reverse(pvpHistory);
    public IEnumerable<GuildPvpSeasonHistory> PvpSeasonHistoryNewestFirst => Enumerable.Reverse(pvpSeasonHistory);

    /// <summary>RequestPvpHistoryAndSeason (@0x1413bbe80, guildmanager.cpp:1144): the battles after the last one held (op 0x4a); the first time the
    /// season history too (0x4b), later the dialog's season rows are drawn again from what is held.</summary>
    public void RequestPvpHistoryAndSeason()
    {
        Send(new GuildPvpHistoryRequest(LastPvpHistoryId));
        if (!PvpSeasonHistoryRequested)
        {
            Send(new GuildPvpSeasonHistoryRequest());
            PvpSeasonHistoryRequested = true;
        }
        else PvpSeasonHistoryRefresh?.Invoke();
    }
    /// <summary>CGuildXmlData's guild houses (LoadGuildHouse) and guild npcs (LoadGuildNpc).</summary>
    public Ms2.Formats.World.GuildHouseTable Houses;
    public Ms2.Formats.World.GuildNpcTable Npcs;
    /// <summary>CStringTable::FUN_141811980(table, id).</summary>
    public Func<int, long, string> StringTableText = (_, _) => "";

    /// <summary>GetGuildId: CGuildInfo's first field, 0 outside a guild.</summary>
    public long GuildId { get; private set; }

    /// <summary>CGuildManager's CGuildInfo (the header, members, grades and skills), or null outside a guild.</summary>
    public GuildInfo Info { get; private set; }

    /// <summary>GetGuildName.</summary>
    public string GuildName => Info?.Name ?? "";
    /// <summary>GetCapacity / GetMemberCount.</summary>
    public int Capacity => Info?.MaxMembers ?? 0;
    public int MemberCount => Info?.Members.Count ?? 0;

    /// <summary>GetMembers (@0x1404d02b0): the members in the order of CGuildInfo's name tree (f320, case-insensitive).</summary>
    public IReadOnlyList<GuildMember> Members =>
        Info == null ? Array.Empty<GuildMember>() : Info.Members.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>FindMemberByName.</summary>
    public GuildMember FindMemberByName(string name) =>
        Info?.Members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>GetMasterMember: the member whose id is the master's.</summary>
    public GuildMember MasterMember => Info?.Members.FirstOrDefault(m => m.CharId == Info.MasterCharId);

    /// <summary>GetGrade (@0x1404d0310): the grade of the index, or null.</summary>
    public GuildGrade GetGrade(int index) => Info?.Grades.FirstOrDefault(g => g.Grade == index);

    /// <summary>IsMaster (@0x1404d0510): the member of the name has the master's id.</summary>
    public bool IsMaster(string name) => FindMemberByName(name) is { } m && Info != null && m.CharId == Info.MasterCharId;

    /// <summary>GetMemberPermissions / HasMemberPermission (@0x1404d06a0): the member's grade's permission bits against a mask.</summary>
    public int MemberPermissions(string name) => FindMemberByName(name) is { } m ? GetGrade(m.Grade)?.Permissions ?? 0 : 0;
    public bool HasMemberPermission(string name, int mask) => (MemberPermissions(name) & mask) != 0;

    /// <summary>GetTotalTrophy, GetTotalItemLevel and the character level total (FUN_1404d0d00) over the members.</summary>
    public int TotalTrophy => Info == null ? 0 : Info.Members.Sum(m => m.TrophyTotal) + Info.F358;   // GetTotalTrophy adds f358
    public int TotalItemLevel => Info?.Members.Sum(m => m.ItemLevel) ?? 0;
    public int TotalCharLevel => Info?.Members.Sum(m => (int)(ushort)m.Level) ?? 0;

    /// <summary>GetMaxGradeIndex: the last grade's index (the grade count less one).</summary>
    public int MaxGradeIndex => (Info?.Grades.Count ?? 0) - 1;

    /// <summary>GetNotice: CGuildInfo's notice (s190).</summary>
    public string GuildNotice => Info?.Notice ?? "";

    /// <summary>GetHouseLevel / GetHouseTheme (@0x1404d1f90 / @0x1404d2030, guilddef_1.cpp:1961-1979): f168 and f16c, both 1 in Reset.</summary>
    public int HouseLevel => Info?.HouseLevel ?? 1;
    public int HouseTheme => Info?.HouseTheme ?? 1;

    /// <summary>GetNpcLevel (@0x1404d2990, guilddef_1.cpp:2097): the level of the guild's npc of the kind, 0 without one.</summary>
    public int NpcLevel(int kind) => Info?.Npcs.FirstOrDefault(n => n.Kind == kind).Level ?? 0;

    /// <summary>FUN_1404d1860 / GetPropensity: CGuildInfo f154 (the "listed in the search" flag the dialog's
    /// OnChageRegistSearch sets) and f158 (the propensity), read as b3 / v14 by CGuildInfo::Read and set by type 0x44.</summary>
    public int PropensityFocus => Info?.B3 ?? 0;
    public int Propensity => Info?.V14 ?? 0;

    /// <summary>GetGuildLevel (CGuildInfo::GetGuildLevel, guilddef_1.cpp:1876): GetLevelByExp of the guild exp.</summary>
    public int GuildLevel => LevelByExp(Info?.Exp ?? 0);

    /// <summary>CGuildInfo::GetMaxFund (@0x1404d1e90): the property's fundMax cut to an int, 0 without a property.</summary>
    public int MaxFund => Property(GuildLevel) is { } p ? (int)p.FundMax : 0;

    /// <summary>IsNotRecentDayChange (@0x14086ace0, timeutil.cpp:344): 1 for no time; 0 for a time not before now; 0 for one
    /// at most a day before now on the same calendar day (CMsTime, the server's zone); else 1.</summary>
    public int IsNotRecentDayChange(long time)
    {
        if (time != 0)
        {
            long now = ServerTime();
            long diff = now - time;
            if (diff <= 0) return 0;
            if (diff <= 86400)
            {
                DateTime a = Ms2.Net.MsTime.LocalTm(time), b = Ms2.Net.MsTime.LocalTm(now);
                int dayA = a.Year * 10000 + a.Month * 100 + a.Day, dayB = b.Year * 10000 + b.Month * 100 + b.Day;
                if (dayA == 0 || dayB == 0) return 0;
                if (dayA == dayB) return 0;
            }
        }
        return 1;
    }

    /// <summary>CGuildMember::FUN_1404cb3e0: today's donations, 0 when the last one (ff8) was on another day.</summary>
    public int DonationToday(GuildMember m) => IsNotRecentDayChange(m.Ff8) == 1 ? 0 : m.Ff0;

    public GuildMember Me => PlayerName() is { } me ? FindMemberByName(me) : null;

    /// <summary>CanAttend (@0x1413b8460): in a guild, a member, and the last attendance (fd8) on another day.</summary>
    public bool CanAttend => InGuild && Me is { } m && IsNotRecentDayChange(m.Fd8) != 0;

    /// <summary>GetGuildNotificationCount (@0x1413bd560, guildmanager.cpp:1471): the game menu's guild count, 1 for CanAttend
    /// plus the join requests sent to the guild.</summary>
    public int NotificationCount => (CanAttend ? 1 : 0) + requests.Count;

    /// <summary>SendGuildMode15 (@0x1413b8230, guildmanager.cpp:617): attend. Joined today (fc8) is the notice
    /// s_guild_err_can_next_day; attended today is s_guild_err_attend_already.</summary>
    public bool Attend()
    {
        if (!InGuild || Me is not { } m) return false;
        if (IsNotRecentDayChange(m.Fc8) == 0) { Notice?.Invoke(CommonByKey("s_guild_err_can_next_day"), 1, true); return false; }
        if (!CanAttend) { Notice?.Invoke(CommonByKey("s_guild_err_attend_already"), 1, true); return false; }
        Send(new GuildAttendRequest());
        return true;
    }

    /// <summary>FUN_1413b8540 (@0x1413b8540, guildmanager.cpp:650): donate <paramref name="amount"/> times guildDonateMeso.
    /// Joined today is s_guild_err_can_next_day; past the property's donationMax s_guild_err_donateMax; the fund (the cost
    /// plus the event bonus) past GetMaxFund s_guild_err_donate_guildFundMax; too little meso s_guild_err_no_money.</summary>
    public bool Donate(int amount)
    {
        if (!InGuild || amount <= 0 || Me is not { } m) return false;
        if (IsNotRecentDayChange(m.Fc8) == 0) { Notice?.Invoke(CommonByKey("s_guild_err_can_next_day"), 1, true); return false; }
        if (Property(GuildLevel) is not { } property) return false;
        if (DonationToday(m) + amount > property.DonationMax) { Notice?.Invoke(CommonByKey("s_guild_err_donateMax"), 1, true); return false; }
        int bonusRate = FundEventRate();
        long cost = (long)DonateMeso * amount;
        long fund = cost;
        fund = (long)(fund + fund * ((float)bonusRate / 10000.0f));     // the C++ compound add, in float
        long guildFund = Info?.Fund ?? 0, guildFundMax = MaxFund;
        if (guildFund + fund > guildFundMax) { Notice?.Invoke(CommonByKey("s_guild_err_donate_guildFundMax"), 1, true); return false; }
        if (Mesos() < cost) { Notice?.Invoke(CommonByKey("s_guild_err_no_money"), 1, true); return false; }
        Send(new GuildDonateRequest(amount));
        return true;
    }

    /// <summary>CGuildXmlData's guildBuff.xml rows (GetSkill), or null when the data is not loaded.</summary>
    public GuildSkillTable SkillTable;
    /// <summary>CGameEventValueGuildSkillSale GetRate(): the guild skill sale of the game events (1/BonusRateScale).
    /// STILL-OPEN: the game events are read but not kept, so 0.</summary>
    public Func<int> GuildSkillSaleRate = () => 0;
    /// <summary>CGameEventValueGuildPersonalSkillSale GetRate(). STILL-OPEN: as <see cref="GuildSkillSaleRate"/>.</summary>
    public Func<int> GuildPersonalSkillSaleRate = () => 0;
    /// <summary>Unk_GuildGameEventMgr::GetBonusRateScale; the donate bonus above divides by the same 10000.</summary>
    public const int BonusRateScale = 10000;

    /// <summary>CGuildInfo::GetSkillLevel (@0x1404d26f0): the level of the guild's skill record, 0 without one.</summary>
    public int SkillLevel(int id) => Info?.Skills.FirstOrDefault(s => s.Id == id)?.Level ?? 0;

    /// <summary>CGuildInfo::GetSkillCost (@0x1404d2510): the row's cost, 0x7fffffff without the row.</summary>
    public int SkillCost(int id, int level) => SkillTable?.Get(id, level) is { } s ? s.Cost : 0x7fffffff;

    /// <summary>CGuildManager::GetSkillCostWithEventDiscount (@0x1413bd050): 0 without the row; the event skills take no sale.</summary>
    public int SkillCostWithEventDiscount(int id, int level)
    {
        if (SkillTable?.Get(id, level) is not { } s) return 0;
        long cost = SkillCost(id, level);
        int discount = s.IsEvent ? 0 : GuildSkillSaleRate();
        cost -= cost * discount / BonusRateScale;
        return (int)cost;
    }

    /// <summary>CGuildManager::GetPersonalSkillDiscountedCost (@0x1413bd1a0): 0x7fffffff without the row or for a guild skill.
    /// The client takes the row's f28, the cost.</summary>
    public int PersonalSkillDiscountedCost(int id, int level)
    {
        if (SkillTable?.Get(id, level) is not { } s || !s.IsPersonal) return 0x7fffffff;
        long cost = s.Cost;
        cost -= cost * GuildPersonalSkillSaleRate() / BonusRateScale;
        return (int)cost;
    }

    /// <summary>CGuildSkill::GetRemainingDuration (@0x1404cbb00, guilddef_1.cpp:891): 0 unused or past, else the seconds left.</summary>
    public int RemainingDuration(GuildSkillState rec)
    {
        if (rec.Time == 0) return 0;
        if (SkillTable?.Get(rec.Id, rec.Level) is not { } s) return 0;
        long until = (long)s.Duration + rec.Time, now = ServerTime();
        return until < now ? 0 : (int)(until - now);
    }

    /// <summary>CGuildSkill::GetRemainingCooldown (@0x1404cbbf0): 0x7fffffff without the row, 0 once past.</summary>
    public int RemainingCooldown(GuildSkillState rec)
    {
        if (SkillTable?.Get(rec.Id, rec.Level) is not { } s) return 0x7fffffff;
        long until = (long)s.Cooltime + rec.Time, now = ServerTime();
        return now > until ? 0 : (int)(until - now);
    }

    /// <summary>CGuildSkill::IsUsable (@0x1404cbcd0): no duration and no cooldown left.</summary>
    public bool SkillUsable(GuildSkillState rec) => RemainingDuration(rec) <= 0 && RemainingCooldown(rec) <= 0;

    /// <summary>FUN_1404d27b0 (@0x1404d27b0, on CGuildManager::GetInstance(), uiguilddialog.cpp:2396): the custom event of an id, null when the guild has none.</summary>
    public (int Id, int Time)? CustomEvent(int id)
    {
        foreach (var e in Info?.CustomEvents ?? new()) if (e.Id == id) return e;
        return null;
    }

    /// <summary>CGuildCustomEvent::IsActive (@0x1404cbee0, guilddef_1.cpp:957): the time (unsigned) plus 30 seconds is after the server time.</summary>
    public bool IsEventActive((int Id, int Time) e) => (uint)e.Time + 0x1e > ServerTime();

    /// <summary>The guild's registration for the vs game (CGuildManager f354, the guild info's flag); the "another game exists" id is F350.</summary>
    public bool VsGameRegistered => Info?.F354 == true;

    /// <summary>CGuildManager::SendGuildMode115 (@0x1413bd2e0, guildmanager.cpp:1425-1440): register for the vs game. Already registered
    /// sends nothing; another game existing shows the box of key s_guild_vsGame_another_game_exist (FUN_140d73ff0, "system message box
    /// by string key", scenestategame.cpp:6097; STILL-OPEN: its body is not read, the common string table is assumed); without the
    /// member right 0x400 the notice of common 0x415 (FUN_141275260(0x415, 1, 1)); else the send of mode 0x73.</summary>
    public bool SendGuildMode115()
    {
        if (VsGameRegistered) return false;
        if ((Info?.F350 ?? 0) != 0) { Box?.Invoke(CommonByKey("s_guild_vsGame_another_game_exist")); return false; }
        if (!HasMemberPermission(PlayerName() ?? "", 0x400)) { NoticeId(0x415); return false; }
        Send(new GuildVsGameRegisterRequest());
        return true;
    }

    /// <summary>CGuildManager::SendGuildMode116 (@0x1413bd3f0, :1443-1456): unregister from the vs game. Not registered sends nothing;
    /// without the member right 0x400 the notice of common 0x415; else the send of mode 0x74.</summary>
    public bool SendGuildMode116()
    {
        if (!VsGameRegistered) return false;
        if (!HasMemberPermission(PlayerName() ?? "", 0x400)) { NoticeId(0x415); return false; }
        Send(new GuildVsGameUnregisterRequest());
        return true;
    }

    bool NoticeKey(string key) { Notice?.Invoke(CommonByKey(key), 1, true); return false; }

    /// <summary>FUN_1413b8fe0 (@0x1413b8fe0, guildmanager.cpp:743): use a guild skill. A non-master gets common 0x416; the guild
    /// level, the fund (the event fund for an event skill), the record and IsUsable follow. STILL-OPEN: the "recall" skill's member
    /// count box (0xb5c) and CCashCallManager::CanRecall are not ported, so the recall skill is refused without a send.</summary>
    public bool UseSkill(int id)
    {
        if (!InGuild) return false;
        if (PlayerName() is not { } me || !IsMaster(me)) { Notice?.Invoke(Common(0x416), 1, true); return false; }
        int level = SkillLevel(id);
        if (SkillTable?.Get(id, level) is not { } s) return false;
        if (GuildLevel < s.RequireLevel) return NoticeKey("s_guild_err_not_enough_guild_level");
        int fund = s.IsEvent ? Info.EventFund : Info.Fund;
        if (fund < SkillCostWithEventDiscount(id, level))
            return NoticeKey(s.IsEvent ? "s_guild_err_not_enough_guild_event_fund" : "s_guild_err_not_enough_guild_fund");
        if (Info.Skills.FirstOrDefault(r => r.Id == id) is not { } rec) return false;
        if (!SkillUsable(rec)) return NoticeKey("s_guild_err_cannot_use_skill");
        if (s.FunctionName == "recall") return false;
        Send(new GuildSkillUseRequest(id));
        return true;
    }

    /// <summary>The local player's effect list asked with bAll set (FUN_14096fe60(id, level, 0, true, 1), guildmanager.cpp:807): any
    /// record of the effect id at the level, enabled or not. Unset means no list, so no effect is up.</summary>
    public Func<uint, ushort, bool> HasOwnEffect;

    /// <summary>FUN_1413b96e0 (@0x1413b96e0, guildmanager.cpp:795-812): use a personal skill: the guild level, then the meso
    /// (s_err_lack_meso), the local player, then s_guild_err_cannot_use_skill while the skill's effect is already on the player.</summary>
    public bool UsePersonalSkill(int id)
    {
        if (!InGuild) return false;
        int level = SkillLevel(id);
        if (SkillTable?.Get(id, level) is not { } s || !s.IsPersonal) return false;
        if (GuildLevel < s.RequireLevel) return NoticeKey("s_guild_err_not_enough_guild_level");
        if (Mesos() < PersonalSkillDiscountedCost(id, level)) return NoticeKey("s_err_lack_meso");
        if (Level() == null) return false;     // GetLocalPlayer() == 0
        if (HasOwnEffect?.Invoke((uint)s.AdditionalEffectId, (ushort)s.AdditionalEffectLevel) == true) return NoticeKey("s_guild_err_cannot_use_skill");
        Send(new GuildPersonalSkillUseRequest(id));
        return true;
    }

    /// <summary>FUN_1413b9ac0 (@0x1413b9ac0, :820): upgrade a skill to the next level: both rows, the master, the guild level
    /// (s_guild_err_need_guild_level with it), the fund (the event fund for an event skill) against the next row's upgrade cost.
    /// STILL-OPEN: SendPacketThrottled's 1000 ms delay (key 0x5c).</summary>
    public bool UpgradeSkill(int id)
    {
        if (!InGuild) return false;
        int level = SkillLevel(id);
        if (SkillTable?.Get(id, level) is not { } cur) return false;
        if (SkillTable.Get(id, level + 1) is not { } next) return NoticeKey("s_guild_err_max_level");
        if (PlayerName() is not { } me || !IsMaster(me)) { Notice?.Invoke(Common(0x416), 1, true); return false; }
        if (next.RequireLevel > GuildLevel)
        {
            Notice?.Invoke(ClientFormat.Format(CommonByKey("s_guild_err_need_guild_level"), next.RequireLevel), 1, true);
            return false;
        }
        int fund = cur.IsEvent ? Info.EventFund : Info.Fund;
        if (next.UpgradeCost > fund)
            return NoticeKey(cur.IsEvent ? "s_guild_err_not_enough_guild_event_fund" : "s_guild_err_not_enough_guild_fund");
        Send(new GuildSkillUpgradeRequest(id));
        return true;
    }

    /// <summary>GetTotalRaidCount (@0x1404d0ee0, guilddef_1.cpp:1713): the sum of every member's raid counts (CGuildMemberRaid::GetTotalCount).</summary>
    public int TotalRaidCount => Info?.Members.Sum(m => m.RaidTotal) ?? 0;

    /// <summary>IsInGuild (@0x1413b6160): the guild id above 0 (unsigned).</summary>
    public bool InGuild => GuildId != 0;

    /// <summary>m_invite: the last invite received (type 4) to this character, until its result (type 5) clears it.</summary>
    public GuildInviteInfo ReceivedInvite { get; private set; }

    /// <summary>HasGuildInvite (@0x1413b61a0): not in a guild and m_invite set.</summary>
    public bool HasInvite => !InGuild && ReceivedInvite != null;

    /// <summary>ClearGuildInvite (@0x1413b6110).</summary>
    public void ClearInvite() => ReceivedInvite = null;

    /// <summary>SendGuildMode5 (@0x1413b74f0, :484): the answer to the invite, sent only outside a guild with an invite. The
    /// invite stays until its result (type 5).</summary>
    public void AnswerInvite(bool accept)
    {
        if (InGuild || !HasInvite) return;
        Send(new GuildInviteAnswerRequest(ReceivedInvite, accept));
    }

    /// <summary>GetSearchPropensity / SetSearchPropensity (guildmanager.h:116): m_468, 0 in the ctor.</summary>
    public int SearchPropensity { get; set; }

    /// <summary>m_joinRequestsForChar: the join requests this character sent, in the order read.</summary>
    public IReadOnlyList<GuildJoinRequest> MyRequests => myRequests;

    /// <summary>m_joinRequestsForGuild (FUN_1413c7f80): the join requests sent to the guild, in the order read.</summary>
    public IReadOnlyList<GuildJoinRequestForGuild> RequestsForGuild => requests;

    /// <summary>The search results of the last type 0x57.</summary>
    public IReadOnlyList<GuildSearchEntry> SearchResults => results;

    /// <summary>FUN_1413c8000 (@0x1413c8000): the request of this character to the guild, or null.</summary>
    public GuildJoinRequest FindRequestByGuild(long guildId) => myRequests.FirstOrDefault(r => r.GuildId == guildId);

    /// <summary>ClearSearchResults: the guild dialog's close clears the results.</summary>
    public void ClearSearchResults() => results.Clear();

    /// <summary>FUN_1413b6370: one Guild packet.</summary>
    /// <summary>OnPacketVsGameTicket (@0x1413c4420, guildmanager.cpp:2653), mode 0x76 and the tail of 0x3d and 0x3e: f354 = the
    /// flag; with the notify flag the line of the register (3 when registered) or unregister ticket key, type 3; then the
    /// dialog's UpdateEventButtons.</summary>
    void VsGameTicket(GuildMessage m) {
        if (Info != null) Info.F354 = m.Accepted;
        if (m.Notify) ChatLine?.Invoke(CommonByKey(m.Accepted ? "s_guild_notify_vsGame_register_ticket" : "s_guild_notify_vsGame_unregister_ticket"), 3);
        VsGameRefresh?.Invoke();
    }

    public void Apply(GuildMessage m)
    {
        switch (m.Type)
        {
            case 0:
                // OnPacketGuildInfo (:1525): Read, then both request lists are cleared. Read keeps the header, the members,
                // the grades and the skills; a packet only the id of it reads leaves no Info.
                if (InGuild) ResetGuild();
                GuildId = m.GuildId;
                Info = m.Info;
                myRequests.Clear();
                requests.Clear();
                nextRefreshTime = ServerTime();   // m_nextRefreshTime = GetServerTime() (:1546)
                StorageHistoryReceived = false;   // m_400 = false after the Reset (guildmanager.cpp:253)
                storageHistory.Clear();
                if (Info != null)
                {
                    PvpState = Info.PvpState;
                    if (Info.Counters.Length >= 3) { Score = Info.Counters[0]; WinCount = Info.Counters[1]; LoseCount = Info.Counters[2]; }
                }
                break;
            case 1:
                // OnPacketCreateGuild (@0x1413bd8a0): a result is ShowGuildError(1, result, 0); else the sound, the box
                // FormatStringId(0x389, name) and CloseDialog(0x4e4f).
                if (m.Result != 0) { ShowGuildError(1, m.Result, 0); break; }
                Sound?.Invoke("PC_GuildMake");
                Box?.Invoke(ClientFormat.Format(Common(0x389), m.Name));
                DialogClose?.Invoke();
                break;
            case 0x52:
            case 0x53:
                // FUN_1413c5360 / FUN_1413c5440: not in a guild; a guild id above 0 is the chat line
                // FormatStringId(0x3ce / 0x3cf, name) of kind 0xd, then SendGuildMode86.
                if (InGuild) break;
                if (m.GuildId != 0)                 // unsigned > 0
                {
                    ChatLine?.Invoke(ClientFormat.Format(Common(m.Type == 0x52 ? 0x3ce : 0x3cf), m.Name), 0xd);
                    SendMyRequests();
                }
                break;
            case 0x56:
                // FUN_1413c5840: not in a guild; the requests replaced, then the dialog's FUN_14196d0c0(0) and FUN_14196d660.
                if (InGuild) break;
                myRequests.Clear();
                myRequests.AddRange(m.Requests);
                SearchRefresh?.Invoke(0);
                MyRequestsRefresh?.Invoke();
                break;
            case 0x57:
                // OnPacketSearchResults (@0x1413c59e0): not in a guild; the results replaced; a count of 0 is the box of
                // common 0x3ca; then the dialog's FUN_14196d0c0(1).
                if (InGuild) break;
                results.Clear();
                if (m.SearchCount == 0) Box?.Invoke(Common(0x3ca));
                results.AddRange(m.Results);
                SearchRefresh?.Invoke(1);
                break;
            case 0x30:
            {
                // OnPacketMyJoinRequestRemoved (@0x1413c5f90): not in a guild; the first request of the id is removed;
                // joined closes the dialog, else FUN_14196d0c0(0) and FUN_14196d660.
                if (InGuild) break;
                int i = myRequests.FindIndex(r => r.Id == m.RequestId);
                if (i >= 0) myRequests.RemoveAt(i);
                if (m.Joined) DialogClose?.Invoke();
                else { SearchRefresh?.Invoke(0); MyRequestsRefresh?.Invoke(); }
                break;
            }
            case 2:
                // FUN_1413bda00 (@0x1413bda00): a result is ShowGuildError(2, result, 0); else FUN_1413b5f90 (the guild is
                // gone), the box of common 0x38a and CloseDialog(0x4e4f).
                if (m.Result != 0) { ShowGuildError(2, m.Result, 0); break; }
                ResetGuild();
                Box?.Invoke(Common(0x38a));
                DialogClose?.Invoke();
                break;
            case 3:
                // FUN_1413bdaf0 (@0x1413bdaf0): the invite went out, the box FormatStringId(0x38b, guild name).
                Box?.Invoke(ClientFormat.Format(Common(0x38b), m.Name));
                break;
            case 4:
            {
                // FUN_1413bdbb0 (:1587): in a guild, ignored; an invite to another name (IsEqualNoCase with GetMyName) is
                // dropped; the refuse option or a PvP point answer no at once; else the box, then the sound.
                if (InGuild) break;
                ReceivedInvite = m.Invite;
                if (!string.Equals(ReceivedInvite?.Invitee, PlayerName() ?? "", StringComparison.OrdinalIgnoreCase)) { ReceivedInvite = null; break; }
                if (RefuseInvite() || PvpPoint()) { AnswerInvite(false); break; }
                InviteBox?.Invoke(ReceivedInvite);
                Sound?.Invoke("System_GuildInvitation");
                break;
            }
            case 5:
                // OnPacketInviteResult (@0x1413be020): a result of 1 is the line 0x38f and CloseDialog(0x4e4f), else the line
                // 0x390, both of the invite's guild name; then ClearGuildInvite.
                if (m.Result == 1)
                {
                    ChatLine?.Invoke(ClientFormat.Format(Common(0x38f), m.Invite.GuildName), 0xd);
                    DialogClose?.Invoke();
                }
                else ChatLine?.Invoke(ClientFormat.Format(Common(0x390), m.Invite.GuildName), 0xd);
                ClearInvite();
                break;
            case 6:
                // FUN_1413be150 (@0x1413be150): the inviter's side; a refusal is the line 0x392 / 0x393 / 0x394 of the name by
                // its reason, an empty line (none shown) for another reason.
                if (!m.Accepted)
                {
                    int id = m.Reason switch { 1 => 0x392, 2 => 0x393, 3 => 0x394, _ => 0 };
                    ChatLine?.Invoke(id == 0 ? "" : ClientFormat.Format(Common(id), m.Name), 0xd);
                }
                break;
            case 7:
                // FUN_1413be350 (@0x1413be350): no body; the guild is gone, the dialog closes and the line 0x395 names the
                // guild it just left.
                if (!InGuild) break;
                string left = GuildName;
                ResetGuild();
                DialogClose?.Invoke();
                ChatLine?.Invoke(ClientFormat.Format(Common(0x395), left), 0xd);
                break;
            case 0x12:
            {
                // OnPacketMemberJoined (@0x1413bfaf0): the member is added (or updated) by name; a full record (mode 3)
                // prints the line 0x3a8 when the notice byte is set and draws the dialog again: UpdateGuildInfo,
                // FUN_141973b20 of the name and the guild skill page FUN_14196e590 (:1945).
                if (!InGuild) break;
                if (FindMemberByName(m.Applicant) is { } old) Info.Members.Remove(old);
                if (m.Member is { } member) Info.Members.Add(member);
                if (m.Member?.Mode == 3)
                {
                    if (m.Notify) ChatLine?.Invoke(ClientFormat.Format(Common(0x3a8), m.Name, m.Applicant), 0xd);
                    GuildInfoRefresh?.Invoke();
                    MemberRefresh?.Invoke(m.Applicant);
                    SkillsRefresh?.Invoke();
                }
                break;
            }
            case 0x13:
            {
                // OnPacketMemberLeft (@0x1413bfe50): the member of the name is removed with the line 0x3a6, but only
                // when such a member is; the dialog's UpdateGuildInfo, RemoveMember and the guild skill page FUN_14196e590
                // run whatever was found.
                if (!InGuild) break;
                if (FindMemberByName(m.Name) is { } gone) { Info.Members.Remove(gone); ChatLine?.Invoke(ClientFormat.Format(Common(0x3a6), m.Name), 0xd); }
                GuildInfoRefresh?.Invoke();
                MemberRemoved?.Invoke(m.Name);
                SkillsRefresh?.Invoke();
                break;
            }
            case 0x54:
                // OnPacketJoinRequestAnswer (@0x1413c5520): the line 0x3d0 (accepted) or 0x3d1 (rejected) of the name.
                if (!InGuild) break;
                if (m.GuildId != 0)       // unsigned > 0
                    ChatLine?.Invoke(ClientFormat.Format(Common(m.Accepted ? 0x3d0 : 0x3d1), m.Applicant), 0xd);
                break;
            case 0x55:
                // OnPacketJoinRequestsForGuild (@0x1413c5690, :2909): the list replaced, then the dialog's FUN_14196da70 and
                // the game menu's FUN_1419381c0 (the guild count).
                if (!InGuild) break;
                requests.Clear();
                requests.AddRange(m.GuildRequests);
                RequestsForGuildRefresh?.Invoke();
                GameMenuGuildRefresh?.Invoke();
                break;
            case 0x2d:
                // OnPacketJoinRequestReceived (@0x1413c6460, :3079): the request added, then the dialog's FUN_14196da70 and
                // the game menu's FUN_1419381c0.
                if (!InGuild) break;
                requests.Add(m.NewRequest);
                RequestsForGuildRefresh?.Invoke();
                GameMenuGuildRefresh?.Invoke();
                break;
            case 0x2e:
            {
                // OnPacketJoinRequestWithdrawn (@0x1413c6580, :3102): the first request of the id is removed, then the
                // dialog's FUN_14196da70 and the game menu's FUN_1419381c0.
                if (!InGuild) break;
                int i = requests.FindIndex(r => r.Id == m.RequestId);
                if (i >= 0) requests.RemoveAt(i);
                RequestsForGuildRefresh?.Invoke();
                GameMenuGuildRefresh?.Invoke();
                break;
            }
            case 0xf:
                // OnPacketAttendComplete (@0x1413bebc0, :1772): in a guild, the box and the line s_guild_attend_complete.
                if (!InGuild) break;
                Box?.Invoke(CommonByKey("s_guild_attend_complete"));
                ChatLine?.Invoke(CommonByKey("s_guild_attend_complete"), 0xd);
                break;
            case 0x71:
                // OnPacketMyDonation (@0x1413bece0, :1782): in a guild, the own member's donation count and time, then the
                // line s_guild_donate_complete.
                if (!InGuild || Me is not { } donor) break;
                donor.Ff0 = m.DonationCount;
                donor.Ff8 = m.DonationTime;
                ChatLine?.Invoke(CommonByKey("s_guild_donate_complete"), 0xd);
                break;
            case 0x76:
                VsGameTicket(m);
                break;
            case 0x3d:
                // OnPacketWeeklyContributionReset (@0x1413c41b0, :2620): every member's weekly contribution (fe8) is 0 and the open
                // dialog's FUN_141973b20 runs per member; the case then falls into OnPacketVsGameTicket (guildmanager.cpp:367-369).
                if (Info != null)
                    foreach (var member in Info.Members) {
                        member.Contribution = 0;
                        MemberRefresh?.Invoke(member.Name);
                    }
                if (m.Ticket) VsGameTicket(m);   // an empty body (the retail GMS2 server's) fails the ticket read after the reset
                break;
            case 0x75:
                // OnPacketVsGameMatchCanceled (@0x1413c4580, :2670): f354 = 0 and the notice s_guild_notify_vsGame_match_canceled
                // (FUN_1412752d0(key, 1); the notice of the key with type 1 is assumed).
                if (Info != null) Info.F354 = false;
                Notice?.Invoke(CommonByKey("s_guild_notify_vsGame_match_canceled"), 1, true);
                break;
            case 0x77:
                // FUN_1413c45e0 (@0x1413c45e0, :2679): f350 = the id, f354 = 0, then the dialog's UpdateEventButtons. STILL-OPEN: the
                // yes / no box s_guild_notify_vsGame_open (30 s timer, callback SendGuildMode120) is not shown: its message box type
                // (FUN_140d73f20) is not ported, and the send of mode 120 does nothing.
                if (Info != null) { Info.F350 = m.Value; Info.F354 = false; }
                VsGameRefresh?.Invoke();
                break;
            case 0x4f:
                // FUN_1413c4e20 (@0x1413c4e20, guildmanager.cpp:2780): ShowGuildError(kind, code, arg) of the read values.
                ShowGuildError(m.ErrorKind, m.ErrorCode, m.ErrorArg);
                break;
            case 0x38:
            {
                // FUN_1413c3780 (@0x1413c3780, :2531): the guild's custom event of the id is overwritten by the one read (none: the
                // packet ends), the dialog rebuilds the rows, then the line s_guild_notify_open_event_user of the user and the event's
                // map name (no such guild event: no line).
                int i = Info?.CustomEvents.FindIndex(e => e.Id == m.EventId) ?? -1;
                if (i < 0) break;
                Info.CustomEvents[i] = (m.EventId, m.EventTime);
                EventsRefresh?.Invoke();
                if (Events?.Get(m.EventId) is not { } ev) break;
                ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_notify_open_event_user"), m.Name, StringTableText(3, ev.MapCode)), 0xd);
                break;
            }
            case 0x39:
                // FUN_1413c39e0 (@0x1413c39e0, guildmanager.cpp:2554): the house level and theme are set, the dialog's House page
                // rebuilds its house part and its npc part, then (the house of that level and theme found) the line
                // s_guild_notify_house_upgrade of the user, the level and the theme's name (string table 0x36), type 0xd.
                // STILL-OPEN: UIGuildHouseUpgradeDialog (0x4f2e)'s FUN_140e39da0 refresh is not ported.
                if (Info != null) { Info.HouseLevel = m.HouseLevel; Info.HouseTheme = m.HouseTheme; }
                HouseRefresh?.Invoke();
                NpcRefresh?.Invoke();
                if (Houses?.Get(m.HouseLevel, m.HouseTheme) is { } house)
                    ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_notify_house_upgrade"), m.Name, m.HouseLevel, StringTableText(0x36, house.StringKey)), 0xd);
                break;
            case 0x3b:
            {
                // FUN_1413c3ef0 (@0x1413c3ef0, :2603): AddNpc (the npc of that kind is replaced), the dialog's House page rebuilds its npc
                // part, then (the npc row of that kind and level found) the line s_guild_notify_upgrade_npc of the user, the npc's name
                // (string table 0x3f) and the level, type 0xd.
                if (Info != null)
                {
                    int i = Info.Npcs.FindIndex(n => n.Kind == m.NpcKind);
                    if (i >= 0) Info.Npcs[i] = (m.NpcKind, m.NpcLevel); else Info.Npcs.Add((m.NpcKind, m.NpcLevel));
                }
                NpcRefresh?.Invoke();
                if (Npcs?.Get(m.NpcKind, m.NpcLevel) is { } npc)
                    ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_notify_upgrade_npc"), m.Name, StringTableText(0x3f, npc.StringKey), m.NpcLevel), 0xd);
                break;
            }
            case 0x25:
            {
                // OnPacketMemberRaidCount (@0x1413c0e70, guildmanager.cpp:2120): in a guild, the member of the name has the raid's count set
                // (CGuildMemberRaid::SetRaidCount, guilddef_1.cpp:761); no such member: nothing. The dialog is not told.
                if (!InGuild) break;
                if (FindMemberByName(m.Name) is { } who) who.Raid[m.RaidId] = m.Value;
                break;
            }
            case 0x27:
                // FUN_1413c2860 (@0x1413c2860, guildmanager.cpp:2372): the state is set, the dialog's UpdateChampionshipRating runs.
                PvpState = m.PvpState;
                ChampionshipRatingRefresh?.Invoke();
                break;
            case 0x29:
            {
                // FUN_1413c2c60 (@0x1413c2c60, :2416): score, wins and losses set, UpdateChampionshipRating, and a changed grade is a line
                // (s_guild_notify... 0x3b8 with the grade's name from string table 0x2d, type 0xd).
                int old = Score;
                Score = m.Score; WinCount = m.Wins; LoseCount = m.Losses;
                ChampionshipRatingRefresh?.Invoke();
                var sn = GetSeason();
                int oldTier = sn.GetRankForScore(old), newTier = sn.GetRankForScore(Score);
                if (oldTier != newTier)
                    ChatLine?.Invoke(ClientFormat.Format(Common(0x3b8), StringTableText(0x2d, newTier)), 0xd);
                break;
            }
            case 0x4a:
                // OnPacketPvpHistory (@0x1413c5000, :2810): each entry appended (the oldest dropped past 10), the last id the newest's time; no guild check.
                foreach (var h in m.PvpHistory)
                {
                    pvpHistory.Add(h);
                    if (pvpHistory.Count > 10) pvpHistory.RemoveAt(0);
                    LastPvpHistoryId = h.Time;
                }
                PvpHistoryRefresh?.Invoke();
                break;
            case 0x4b:
                // OnPacketPvpSeasonHistory (@0x1413c51c0, :2831): the list is replaced.
                pvpSeasonHistory.Clear();
                pvpSeasonHistory.AddRange(m.PvpSeasonHistory);
                PvpSeasonHistoryRefresh?.Invoke();
                break;
            case 0x3e:
                // FUN_1413c4300 (@0x1413c4300, :2637): every member's raid counts are cleared (CGuildMemberRaid::Clear), then the dialog's
                // UpdateRaidScore and FUN_141973220 run. Unlike the 0x25 packet this one has no in-guild check.
                if (Info != null) foreach (var member in Info.Members) member.Raid.Clear();
                RaidRefresh?.Invoke();
                if (m.Ticket) VsGameTicket(m);   // the case falls through (guildmanager.cpp:368-369); an empty body has no ticket
                break;
            case 0x6e:
                // FUN_1413c3e50 (@0x1413c3e50, :2590): SetStorageItem, m_400 cleared (the history is asked for again), the dialog's
                // UpdateStoragePage.
                SetStorageItem(m.StorageItem);
                StorageHistoryReceived = false;
                StorageRefresh?.Invoke();
                break;
            case 0x6d:
                // OnPacketStorageGiftResult (@0x1413c5c00): in a guild, result 0 the line s_guild_storage_gift of the receiver (type 0xd);
                // result 0x36 the notice s_guild_storage_not_enough_gift_need_period of the constant guildStorageGiftNeedPeriod.
                if (!InGuild) break;
                if (m.Result == 0) ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_storage_gift"), m.Name), 0xd);
                else if (m.Result == 0x36) Notice?.Invoke(ClientFormat.Format(CommonByKey("s_guild_storage_not_enough_gift_need_period"), GuildStorageGiftNeedPeriod()), 1, true);
                break;
            case 0x70:
                // OnPacketStorageHistory (@0x1413c5dc0, :2963): in a guild, m_400 set, the deque replaced by the lines (AddStorageHistory
                // keeps the last 0x32), the dialog's UpdateStorageHistory.
                if (!InGuild) break;
                StorageHistoryReceived = true;
                storageHistory.Clear();
                foreach (var h in m.StorageHistory) storageHistory.Add(h);
                if (storageHistory.Count > 0x32) storageHistory.RemoveRange(0, storageHistory.Count - 0x32);
                StorageHistoryRefresh?.Invoke();
                break;
            case 0x32:
            {
                // OnPacketGuildExp (@0x1413c4aa0, guildmanager.cpp:2723): the level before, the exp (f15c), the level after; the
                // open dialog (0x4e4f) updates the exp bar and, on a level change, the skill page and the npc. No in-guild check.
                if (Info == null) break;
                int before = GuildLevel;
                Info.Exp = m.Value;
                ExpRefresh?.Invoke(GuildLevel != before);
                break;
            }
            case 0x33:
            case 0x34:
                // FUN_1413c4b60 (:2739) the fund (f160) and FUN_1413c4bd0 (:2748) the event fund (f164); then FUN_14196e2c0.
                if (Info == null) break;
                if (m.Type == 0x33) Info.Fund = m.Value; else Info.EventFund = m.Value;
                FundRefresh?.Invoke();
                break;
            case 0x44:
                // FUN_1413bf2a0 (guildmanager.cpp:1855): in a guild, the propensity focus and value.
                if (!InGuild || Info == null) break;
                Info.B3 = m.Focus;
                Info.V14 = m.Value;
                break;
            case 0x2f:
            {
                // FUN_1413c6180 (@0x1413c6180, :3057): the first request of the id is removed, the dialog draws the list
                // again (FUN_14196da70), the game menu its guild count (FUN_1419381c0), and the line 0x3bd (accepted) or
                // 0x3be (rejected) names the guild and the applicant.
                if (!InGuild) break;
                int i = requests.FindIndex(r => r.Id == m.RequestId);
                if (i >= 0) requests.RemoveAt(i);
                RequestsForGuildRefresh?.Invoke();
                GameMenuGuildRefresh?.Invoke();
                ChatLine?.Invoke(ClientFormat.Format(Common(m.Accepted ? 0x3bd : 0x3be), m.Name, m.Applicant), 0xd);
                break;
            }
            case 0x8:
            case 0x3f:
            case 0x41:
            case 0xa:
                // FUN_1413be400 (:1667), FUN_1413bee70 (:1799), FUN_1413beff0 (:1822) and FUN_1413be7e0 (:1716): in a guild, the
                // body read and nothing else done.
                break;
            case 0x9:
                // FUN_1413be460 (:1676): the guild is gone; an empty name is the line 0x3aa, else FormatStringId(0x3ab, name),
                // kind 0xd; then Reset and CloseDialog(0x4e4f).
                if (!InGuild) break;
                ChatLine?.Invoke(m.Name.Length == 0 ? Common(0x3aa) : ClientFormat.Format(Common(0x3ab), m.Name), 0xd);
                ResetGuild();
                DialogClose?.Invoke();
                break;
            case 0xb:
            {
                // FUN_1413be670 (:1698): the member of the name is removed (vfunc_1), with no line and no dialog call. The
                // GMS2 client's case 11 (@0x140a92589) is the same: FindMemberByName, AddPreloadPlayer, then the removal.
                if (!InGuild) break;
                if (FindMemberByName(m.Name) is { } gone) Info.Members.Remove(gone);
                break;
            }
            case 0xc:
            {
                // OnPacketMemberRenamed (:1726): a member of the old name is renamed (RenameMember); else the first join
                // request for the guild of the old name is. The open dialog removes the old row and draws the new one, or
                // draws the request list again.
                if (!InGuild) break;
                if (FindMemberByName(m.Name) is { } member)
                {
                    member.Name = m.NewName;
                    MemberRemoved?.Invoke(m.Name);
                    MemberRefresh?.Invoke(m.NewName);
                    break;
                }
                int i = requests.FindIndex(r => r?.Name == m.Name);
                if (i >= 0) { requests[i].Name = m.NewName; RequestsForGuildRefresh?.Invoke(); }
                break;
            }
            case 0x40:
                // FUN_1413beed0 (:1808): result 1 is the box 0x397.
                if (!InGuild) break;
                if (m.Result == 1) Box?.Invoke(Common(0x397));
                break;
            case 0x42:
                // OnPacketExpandCapacity (:1831): SetMaxMemberCount, then the box FormatStringId(0x39f, capacity).
                if (!InGuild || Info == null) break;
                Info.MaxMembers = m.Value;
                Box?.Invoke(ClientFormat.Format(Common(0x39f), m.Value));
                break;
            case 0x43:
                // OnPacketGradeChanged (:1842): the grade read but not stored; the box FormatStringId(0x398, GetGradeName(index)).
                if (!InGuild) break;
                Box?.Invoke(ClientFormat.Format(Common(0x398), GetGrade(m.Grade)?.Name ?? ""));
                break;
            case 0x5a:
            {
                // FUN_1413bf340 (:1867): the skill's row at the guild's level; the line s_guild_skill_use_skill with the
                // additional effect's name (FUN_141812290(f34, f38, "name")).
                if (!InGuild) break;
                if (SkillTable?.Get(m.Value, SkillLevel(m.Value)) is not { } s) break;
                ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_skill_use_skill"), EffectName((uint) s.AdditionalEffectId, (ushort) s.AdditionalEffectLevel)), 0xd);
                break;
            }
            case 0x5b:
            {
                // FUN_1413bf540 (:1880): with the guild dialog open, the skill's (effect id, effect level) is kept in m_470
                // until the effect comes to the player (FUN_1413bce40, OwnEffectAdded).
                if (!InGuild || !DialogOpen()) break;
                if (SkillTable?.Get(m.Value, SkillLevel(m.Value)) is not { } s) break;
                pendingSkillEffects.Add((s.AdditionalEffectId, s.AdditionalEffectLevel));
                break;
            }
            case 0x61:
                // OnPacketGainExpFund (:1895): s_guild_gain_exp_fund, s_guild_gain_exp or s_guild_gain_fund with the numbers
                // grouped (FUN_140763e10), kind 0xd.
                if (!InGuild) break;
                if (m.Value > 0 && m.Value2 > 0) ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_gain_exp_fund"), GroupText(m.Value), GroupText(m.Value2)), 0xd);
                else if (m.Value > 0) ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_gain_exp"), GroupText(m.Value)), 0xd);
                else if (m.Value2 > 0) ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_gain_fund"), GroupText(m.Value2)), 0xd);
                break;
            case 0x63:
            {
                // OnPacketOpenEvent (:1911): the event's map name (string table 3) in s_guild_open_event.
                if (!InGuild) break;
                if (Events?.Get(m.EventId) is not { } ev) break;
                ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_open_event"), StringTableText(3, ev.MapCode)), 0xd);
                break;
            }
            case 0x14:
            case 0x21:
            {
                // OnPacketMemberExpelled (:1977) / FUN_1413c0250 (:1998): a member of the name is removed with the line
                // FormatStringId(0x3a9, expeller, name) / (0x3a6, name); the open dialog's UpdateGuildInfo, RemoveMember and
                // the guild skill page (FUN_14196e590) run whatever was found.
                if (!InGuild) break;
                if (FindMemberByName(m.Name) is { } gone)
                {
                    Info.Members.Remove(gone);
                    ChatLine?.Invoke(m.Type == 0x14 ? ClientFormat.Format(Common(0x3a9), m.By, m.Name) : ClientFormat.Format(Common(0x3a6), m.Name), 0xd);
                }
                GuildInfoRefresh?.Invoke();
                MemberRemoved?.Invoke(m.Name);
                SkillsRefresh?.Invoke();
                break;
            }
            case 0x17:
            case 0x18:
            {
                // FUN_1413c0430 (:2018) online, FUN_1413c0630 (:2038) offline with the login time: f158 0 / 1 (and fd0); a
                // non-buddy is the line 0x3ac / 0x3ad (buddies get the buddy list's own notice); then the member's row.
                if (!InGuild || FindMemberByName(m.Name) is not { } member) break;
                member.Offline = m.Type == 0x17 ? 0 : 1;
                if (m.Type == 0x18) member.LastSeen = m.Time;
                if (!IsBuddy(m.Name)) ChatLine?.Invoke(ClientFormat.Format(Common(m.Type == 0x17 ? 0x3ac : 0x3ad), m.Name), 0xd);
                MemberRefresh?.Invoke(m.Name);
                break;
            }
            case 0x15:
            {
                // OnPacketMemberGradeChanged (:2060): the member's grade (fb4); with the grade known, the line 0x3af (own, and
                // SendGuildMode85 when the own grade may see the requests and none are kept) or 0x3ae (another member).
                if (!InGuild) break;
                if (FindMemberByName(m.Name) is { } member)
                {
                    member.Grade = m.Grade;
                    if (GetGrade(m.Grade) is { } grade)
                    {
                        if (string.Equals(m.Name, PlayerName(), StringComparison.OrdinalIgnoreCase))
                        {
                            ChatLine?.Invoke(ClientFormat.Format(Common(0x3af), grade.Name), 0xd);
                            if (HasMemberPermission(PlayerName(), 2) && requests.Count == 0) ListRequests();
                        }
                        else ChatLine?.Invoke(ClientFormat.Format(Common(0x3ae), m.Name, grade.Name), 0xd);
                    }
                }
                MemberRefresh?.Invoke(m.Name);
                break;
            }
            case 0x16:
                // OnPacketSetMemberMessage (:2088): the member's memo (fb8), then the row.
                if (!InGuild) break;
                if (FindMemberByName(m.Name) is { } memoMember) memoMember.Memo = m.Text;
                MemberRefresh?.Invoke(m.Name);
                break;
            case 0x24:
                // OnPacketMemberContribution (:2103): SetMemberContribution (fd8, the last check-in time CanAttend reads); for
                // the player, the game menu's FUN_1419381c0 and the dialog's UpdateAttendBackground.
                if (!InGuild) break;
                if (FindMemberByName(m.Name) is { } attendMember) attendMember.Fd8 = m.Contribution;
                if (PlayerName() == m.Name)
                {
                    GameMenuGuildRefresh?.Invoke();
                    AttendRefresh?.Invoke();
                }
                break;
            case 0x19:
            {
                // OnPacketMasterChanged (:2136): both members must be; old grade 1, new grade 0, SetMaster with the new one's
                // ids and name; the line 0x3b1 (the player is the master) or FormatStringId(0x3b0, new); the dialog's
                // UpdateGuildInfo and both rows.
                if (!InGuild || FindMemberByName(m.Name) is not { } oldMaster || FindMemberByName(m.NewName) is not { } newMaster) break;
                oldMaster.Grade = 1;
                newMaster.Grade = 0;
                Info.MasterFirstId = newMaster.FirstId; Info.MasterCharId = newMaster.CharId; Info.MasterName = newMaster.Name;
                ChatLine?.Invoke(string.Equals(m.NewName, PlayerName(), StringComparison.OrdinalIgnoreCase) ? Common(0x3b1) : ClientFormat.Format(Common(0x3b0), m.NewName), 0xd);
                GuildInfoRefresh?.Invoke();
                MemberRefresh?.Invoke(m.NewName);
                MemberRefresh?.Invoke(m.Name);
                break;
            }
            case 0x1a:
                // OnPacketNoticeChanged (:2166): kind 0 the tag line (f58, FUN_1404cd030); kind 1 the notice (SetNotice), the
                // dialog's UpdateGuildInfo and FormatStringId(0x3b2, name).
                if (!InGuild || Info == null) break;
                if (m.Kind == 0) Info.Tag = m.Text;
                else if (m.Kind == 1)
                {
                    Info.Notice = m.Text;
                    GuildInfoRefresh?.Invoke();
                    ChatLine?.Invoke(ClientFormat.Format(Common(0x3b2), m.Name), 0xd);
                }
                break;
            case 0x22:
                // OnPacketGuildRenamed (:2200): SetGuildName, FormatStringId(0x3b4, name), the dialog's SetGuildName and the
                // name over the local player's head (PC::vfunc_129).
                if (!InGuild || Info == null) break;
                Info.Name = m.Name;
                ChatLine?.Invoke(ClientFormat.Format(Common(0x3b4), m.Name), 0xd);
                GuildNameRefresh?.Invoke(m.Name);
                if (PlayerName() is { } own) HeadGuildName?.Invoke(own, m.Name);
                break;
            case 0x1c:
                // FUN_1413c1840 (:2216): SetMaxMemberCount, the dialog's UpdateGuildInfo and FUN_14196e2c0, the line
                // FormatStringId(0x3b5, value).
                if (!InGuild || Info == null) break;
                Info.MaxMembers = m.Value;
                GuildInfoRefresh?.Invoke();
                FundRefresh?.Invoke();
                ChatLine?.Invoke(ClientFormat.Format(Common(0x3b5), m.Value), 0xd);
                break;
            case 0x1d:
            {
                // OnPacketGradeUpdated (:2232): the grade at the index must be; it takes the read grade's fields, the dialog
                // draws the members of the grade (RefreshMembersOfGrade) and the player of that grade gets the line 0x3a7.
                if (!InGuild || Info == null) break;
                int i = Info.Grades.FindIndex(g => g.Grade == m.Grade);
                if (i < 0 || m.GradeEntry == null) break;
                Info.Grades[i] = m.GradeEntry;
                GradeRefresh?.Invoke(m.Grade);
                if (Me is { } me && me.Grade == m.Grade) ChatLine?.Invoke(ClientFormat.Format(Common(0x3a7), me.Name), 0xd);
                break;
            }
            case 0x1e:
                // FUN_1413c1c50 (:2253): FUN_1404d17e0(focus, value), the propensity.
                if (!InGuild || Info == null) break;
                Info.B3 = m.Focus;
                Info.V14 = m.Value;
                break;
            case 0x1b:
                // FUN_1413c15b0 (:2185): the mark code (f48), FormatStringId(0x3b3, changer's StringIdInterface text) and the dialog's SetGuildMarkFromCode.
                if (!InGuild || Info == null) break;
                Info.Code = m.Code;
                ChatLine?.Invoke(ClientFormat.Format(Common(0x3b3), m.Changer == null ? "" : IdText(m.Changer)), 0xd);
                MarkRefresh?.Invoke(m.Code);
                break;
            case 0x6b:
                // FUN_1413c3110 (:2476): FUN_1404d3600, the guild's own trophies (f358). No in-guild check and no dialog call.
                if (Info == null) break;
                Info.F358 = m.Value;
                break;
            case 0xe:
                // FUN_1413beb10 (:1761): the member's data f20 (the item level); no dialog call.
                if (!InGuild) break;
                if (FindMemberByName(m.Name) is { } levelMember) levelMember.ItemLevel = m.Value;
                break;
            case 0x1f:
            {
                // OnPacketMemberDataChanged (:2287): the member's data f24 (the map), then the row.
                if (!InGuild || FindMemberByName(m.Name) is not { } member) break;
                member.MapId = m.Value;
                MemberRefresh?.Invoke(m.Name);
                break;
            }
            case 0x20:
            {
                // FUN_1413c1d30 (:2266): the member's data replaced; a changed trophy triple also runs UpdateGuildInfo.
                if (!InGuild || m.Member == null || FindMemberByName(m.Name) is not { } member) break;
                bool changed = member.Triple != m.Member.Triple;
                CopyMemberData(m.Member, member);
                if (changed) GuildInfoRefresh?.Invoke();
                MemberRefresh?.Invoke(m.Name);
                break;
            }
            case 0x28:
            {
                // FUN_1413c28e0 (:2383): the matching sounds (s_strGuildBattleMatchingSuccess / ...Notice / ...Fail, :94-97), then ShowSystemNotice(0x3bf matched / 0x3c0 not, 1, 1). The arena
                // join prompt (the option m_promptArenaJoin) is STILL-OPEN. No in-guild check.
                if (m.Accepted) { Sound?.Invoke("guildBattle_MatchingSuccess"); Sound?.Invoke("system_notice_GuildBattleMatching01"); }
                else Sound?.Invoke("guildBattle_MatchingFail");
                Notice?.Invoke(Common(m.Accepted ? 0x3bf : 0x3c0), 1, true);
                break;
            }
            case 0x2a:
                // OnPacketBattleResult (:2441): FormatStringId(0x3bb won / 0x3bc lost, guild), an empty name "Unknown".
                ChatLine?.Invoke(ClientFormat.Format(Common(m.Accepted ? 0x3bb : 0x3bc), m.Name.Length < 1 ? "Unknown" : m.Name), 0xd);
                break;
            case 0x2b:
                // OnPacketBattleApplied (:2457): FormatStringId(0x3b9, guild) and the apply sound.
                ChatLine?.Invoke(ClientFormat.Format(Common(0x3b9), m.Name), 0xd);
                Sound?.Invoke("guildBattle_Apply");
                break;
            case 0x2c:
                // OnPacketGuildBattleNotice (:2467): FormatStringId(0x3ba, guild).
                ChatLine?.Invoke(ClientFormat.Format(Common(0x3ba), m.Name), 0xd);
                break;
            case 0x36:
            case 0x37:
            {
                // FUN_1413c3170 (:2485) a skill used / FUN_1413c3460 (:2508) a skill upgraded: the kept skill of the id
                // (FUN_1404d2640) takes the read one, or nothing happens. 0x36: the guild skill page, then with a user the
                // line s_guild_notify_use_skill_user. 0x37: with the skill's row, the guild or personal page and the line
                // s_guild_notify_upgrade_skill(user, name, level). No in-guild check.
                if (Info == null || m.Skill == null) break;
                int i = Info.Skills.FindIndex(s => s.Id == m.Skill.Id);
                if (i < 0) break;
                Info.Skills[i] = m.Skill;
                var row = SkillTable?.Get(m.Skill.Id, m.Skill.Level);
                if (m.Type == 0x36)
                {
                    SkillsRefresh?.Invoke();
                    if (m.Name.Length > 0 && row != null)
                        ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_notify_use_skill_user"), m.Name, EffectName((uint) row.AdditionalEffectId, (ushort) row.AdditionalEffectLevel)), 0xd);
                    break;
                }
                if (row == null) break;
                if (row.IsGuild) SkillsRefresh?.Invoke();
                else if (row.IsPersonal) PersonalSkillsRefresh?.Invoke();
                ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_notify_upgrade_skill"), m.Name, EffectName((uint) row.AdditionalEffectId, (ushort) row.AdditionalEffectLevel), m.Skill.Level), 0xd);
                break;
            }
            case 0x7a:
                // FUN_1413c4870 (:2698): only UIPvPGuildTeamResultDialog (0x4ec7)'s SetVsGameResult reads it; that dialog is
                // not ported (STILL-OPEN), so the read guilds are not shown.
                break;
            case 0x7b:
                // OnPacketVsGameReward (:2707): kind 1 s_guild_notify_vsGame_reward_exp, kind 2 ..._fund, grouped, chat kind 3.
                if (m.Kind == 1) ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_notify_vsGame_reward_exp"), GroupText(m.Value)), 3);
                else if (m.Kind == 2) ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_notify_vsGame_reward_fund"), GroupText(m.Value)), 3);
                break;
            case 0x4c:
                // OnPacketExpelledOfflineMembers (:2758): each entry of the reason "expeled" is FormatStringId(0x3ab, name).
                foreach (var e in m.Expelled)
                    if (e.Reason == "expeled") ChatLine?.Invoke(ClientFormat.Format(Common(0x3ab), e.Name), 0xd);
                break;
            case 0x47:
                // FUN_1413c4dd0 (:2773): FUN_140d73bc0(0x228), the box of the common string 0x228.
                Box?.Invoke(Common(0x228));
                break;
            case 0x50:
                // OnRemainTimeNotice (:2791): the system notice 0x421 (FUN_141275260(0x421, 1, 1)); the remaining time line
                // (FUN_140a2fcd0(begin, end)) is STILL-OPEN, its body is not in the source.
                Notice?.Invoke(Common(0x421), 1, true);
                break;
            case 0x4d:
            case 0x4e:
                // FUN_1413c7700 (:3108) / FUN_1413c7810 (:3121): the guild name over the head of the player of the name, or "".
                HeadGuildName?.Invoke(m.Name, m.Type == 0x4d ? m.Text : "");
                break;
            case 0x26:
                // FUN_1413c23d0 (:2329): only with the dialog open; each member's fe0 / fe4, then its row.
                if (!DialogOpen()) break;
                foreach (var v in m.MemberValues)
                    if (FindMemberByName(v.Name) is { } vm) { vm.Fe0 = v.Fe0; vm.Fe4 = v.Fe4; MemberRefresh?.Invoke(v.Name); }
                break;
            case 0x35:
                // FUN_1413c2590 (:2351): only with the dialog open; the member's weekly (fe8) and total (fec) contribution and
                // its row; a gain of the player's is the line s_guild_gain_contribution.
                if (!DialogOpen()) break;
                if (FindMemberByName(m.Name) is { } cm) { cm.Contribution = m.Weekly; cm.TotalContribution = m.Total; MemberRefresh?.Invoke(m.Name); }
                if (m.Value > 0 && string.Equals(m.Name, PlayerName(), StringComparison.OrdinalIgnoreCase))
                    ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_gain_contribution"), m.Value), 0xd);
                break;
            case 0x3a:
                // FUN_1413c3c40 (:2576): i64 the character, the user, i32 the poster, the text; vfunc_2 keeps them and
                // UpdateGuildPoster(poster, 1) draws the guild house's poster (both STILL-OPEN: no guild house poster in the
                // port). A named user is the line s_guild_notify_change_poster, kind 0xd. No in-guild check.
                if (m.Name.Length > 0) ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_notify_change_poster"), m.Name), 0xd);
                break;
            case 0x23:
                // OnPacketMemberAchievement (:2303): the line 0x3b6 / 0x3b7 with FUN_140d2d8a0(achieve, grade, text, date),
                // whose body is not in the source (STILL-OPEN), so nothing is shown.
                break;
        }
    }

    /// <summary>Unk_GuildMemberData's copy (mode 0x20, FUN_1413c1d30): the fields <see cref="GuildMember.ReadData"/> reads.</summary>
    static void CopyMemberData(GuildMember from, GuildMember to)
    {
        to.FirstId = from.FirstId; to.CharId = from.CharId; to.Name = from.Name; to.F10 = from.F10; to.F14 = from.F14;
        to.JobCode = from.JobCode; to.Level = from.Level; to.ItemLevel = from.ItemLevel; to.MapId = from.MapId; to.Channel = from.Channel;
        to.Code = from.Code; to.F38 = from.F38; to.F3C = from.F3C; to.F40 = from.F40; to.F48 = from.F48; to.Triple = from.Triple;
    }

    /// <summary>FUN_1413bce40 (additionalcli_2.cpp:216, :301): an additional effect came to the player. In a guild, an effect
    /// kept by mode 0x5b is dropped from m_470 and is the line s_guild_skill_use_skill with the effect's name (kind 0xd), and the
    /// open dialog redraws its personal skill page (FUN_14196f830). Returns whether the effect was a kept one.</summary>
    public bool OwnEffectAdded(int effectId, int level)
    {
        if (!InGuild || !pendingSkillEffects.Remove((effectId, level))) return false;
        ChatLine?.Invoke(ClientFormat.Format(CommonByKey("s_guild_skill_use_skill"), EffectName((uint) effectId, (ushort) level)), 0xd);
        PersonalSkillsRefresh?.Invoke();
        return true;
    }

    /// <summary>FUN_1413b5f90 (@0x1413b5f90, guildmanager.cpp:236-255), which the game UI scene's leave calls (UISceneGame::vfunc_49,
    /// uiscenegame.cpp:213): in a guild, its Reset, the pvp histories and m_3e0 / m_400 cleared; outside one nothing.</summary>
    public void LeaveGame()
    {
        if (!InGuild) return;
        ResetGuild();
        StorageHistoryReceived = false;
        nextRefreshTime = 0;
    }

    /// <summary>m_nextRefreshTime: the last day check's server time, set by the guild info (:1546) and by Update, 0 after
    /// LeaveGame.</summary>
    long nextRefreshTime, lastDayCheck;

    /// <summary>FUN_1413b5e80 (@0x1413b5e80, guildmanager.cpp:218), run each world tick (worldservice_1.cpp:9830): in a guild,
    /// when IsNotRecentDayChange(m_nextRefreshTime) the game menu's guild count (FUN_1419381c0) and the guild dialog's
    /// UpdateAttendBackground are drawn again and m_nextRefreshTime becomes the server time. Its m_noticeAttendance part waits
    /// for IsGuildChampionshipOpen, which is never open here. The check runs once per server second (the same second gives
    /// the same answer).</summary>
    public void Update()
    {
        if (!InGuild) return;
        long now = ServerTime();
        if (now == lastDayCheck) return;
        lastDayCheck = now;
        if (IsNotRecentDayChange(nextRefreshTime) == 0) return;
        GameMenuGuildRefresh?.Invoke();
        AttendRefresh?.Invoke();
        nextRefreshTime = now;
    }

    /// <summary>FUN_1413b5f90's Reset: the guild state is gone (the request lists are kept, as the client keeps them).</summary>
    void ResetGuild()
    {
        GuildId = 0;
        Info = null;
        storageHistory.Clear();   // the history deque belongs to the guild's storage, which goes with the CGuildInfo
        PvpState = Score = WinCount = LoseCount = 0;
        pvpHistory.Clear(); pvpSeasonHistory.Clear(); LastPvpHistoryId = 0; PvpSeasonHistoryRequested = false;
    }

    /// <summary>FUN_1413b6d10 (:405): create a guild. False in a guild or with an invite; a name shorter than
    /// GuildNameLengthMin is the notice s_guild_err_invalide_create_name; a failed CheckGuildName is its message; no local
    /// player is false; a level (u16) not above GuildCreateMinLevel is ShowGuildError(1, 0x13, 0); else the send.</summary>
    public bool Create(string name)
    {
        name ??= "";
        if (InGuild || HasInvite) return false;
        if (name.Length < NameLengthMin) { Notice?.Invoke(CommonByKey("s_guild_err_invalide_create_name"), 1, true); return false; }
        if (CheckName?.Invoke(name) is { } message) { Notice?.Invoke(message, 1, true); return false; }
        if (Level() is not { } level) return false;
        if ((ushort)level <= CreateMinLevel) { ShowGuildError(1, 0x13, 0); return false; }
        Send(new GuildCreateRequest(name));
        return true;
    }

    /// <summary>SendGuildMode82 (@0x1413bc4a0): ask to join; 10 requests already is ShowGuildError(0x52, 0x25, 0).</summary>
    public void Join(long guildId)
    {
        if (myRequests.Count >= 10) { ShowGuildError(0x52, 0x25, 0); return; }
        Send(new GuildJoinRequestSend(guildId));
    }

    /// <summary>SendGuildMode83 (@0x1413bc540): cancel a join request.</summary>
    public void CancelJoin(long id) => Send(new GuildJoinCancelRequest(id));

    /// <summary>SendGuildMode84 (@0x1413bc5b0): answer a join request sent to the guild (the id is the request's, the
    /// movie hands it back as its requestSn).</summary>
    public void Respond(long id, bool accept) => Send(new GuildRespondRequest(id, accept));

    /// <summary>SendGuildMode85 (@0x1413bc630): in a guild, ask for the join requests sent to the guild. The client asks
    /// from a member with the invite right when the local player is complete.</summary>
    public void ListRequests()
    {
        if (!InGuild) return;
        Send(new GuildListRequestsRequest());
    }

    /// <summary>HasMemberPermission by the character id (the client asks by the local player's name).</summary>
    public bool HasMemberPermissionOf(long charId, int mask) =>
        Info?.Members.FirstOrDefault(m => m.CharId == charId) is { } me && ((GetGrade(me.Grade)?.Permissions ?? 0) & mask) != 0;

    /// <summary>SendGuildMode7 (@0x1413b75a0): leave the guild. The client sends it only from a member who is not the
    /// master; the master gets the notice of common 0x396 instead.</summary>
    public void Leave()
    {
        if (!InGuild) return;
        string myName = PlayerName();
        if (string.IsNullOrEmpty(myName)) return;
        if (IsMaster(myName)) { Notice?.Invoke(Common(0x396), 1, true); return; }
        Send(new GuildLeaveRequest());
    }

    /// <summary>SendGuildMode2 (@0x1413b7020): disband the guild. The client sends it only from the master of a
    /// one-member guild; a guild with members gets the box of common 0x40e first (FUN_140d73bc0 is ShowMessageBox
    /// (GetStringCommon(id)), unit_140d73aa0_messageboxfactory.cpp:84), a member who is not the master nothing.</summary>
    public bool Break()
    {
        if (!InGuild) return false;
        string myName = PlayerName();
        if (string.IsNullOrEmpty(myName)) return false;
        if (MemberCount > 1) { Box?.Invoke(Common(0x40e)); return false; }
        if (!IsMaster(myName)) return false;
        Send(new GuildBreakRequest());
        return true;
    }

    /// <summary>ShowSystemNotice(text, 1, 1) for the guild dialog.</summary>
    public void ShowNotice(string text) => Notice?.Invoke(text, 1, true);

    /// <summary>FUN_141275260 (@0x141275260): the notice of a common string, none when the string is empty.</summary>
    void NoticeId(int id)
    {
        string text = Common(id);
        if (text.Length > 0) Notice?.Invoke(text, 1, true);
    }

    /// <summary>CheckContentText of the client's FUN_1413b7c70 and SendGuildMode13: a refused text is its notice.</summary>
    bool ContentPasses(string text)
    {
        if (CheckContent?.Invoke(text) is not { } message) return true;
        Notice?.Invoke(message, 1, true);
        return false;
    }

    /// <summary>FUN_1413b71c0 (@0x1413b71c0, :454): invite a character. No local player is false; CheckName(name, true)
    /// refuses the own name (10: the notice of common 0x38d) and a banned one (common 0x38e); outside a guild false;
    /// without the invite right (2) the notice of common 0x415; a full guild the box of common 0x40f (FUN_140d73bc0);
    /// else the sound PC_InviteSuccess and the send.</summary>
    public bool Invite(string name)
    {
        name ??= "";
        string myName = PlayerName();
        if (string.IsNullOrEmpty(myName)) return false;
        bool self = string.Equals(name, myName, StringComparison.OrdinalIgnoreCase);
        if (self || BannedName?.Invoke(name) == true) { NoticeId(self ? 0x38d : 0x38e); return false; }
        if (!InGuild) return false;
        if (!HasMemberPermission(myName, 2)) { NoticeId(0x415); return false; }
        if (MemberCount >= Capacity) { Box?.Invoke(Common(0x40f)); return false; }
        Sound?.Invoke("PC_InviteSuccess");
        Send(new GuildInviteRequest(name));
        return true;
    }

    /// <summary>SendGuildMode8 (@0x1413b76c0): expel a member — not the own name, in a guild, with the expel right (4),
    /// and not the master.</summary>
    public void Expel(string name)
    {
        name ??= "";
        string myName = PlayerName();
        if (string.IsNullOrEmpty(myName)) return;
        if (string.Equals(myName, name, StringComparison.OrdinalIgnoreCase)) return;
        if (!InGuild) return;
        if (!HasMemberPermission(myName, 4)) return;
        if (IsMaster(name)) return;
        Send(new GuildExpelRequest(name));
    }

    /// <summary>SendGuildMode10 (@0x1413b78c0): set a member's grade — a name of 2 to 0x18 characters (else the client
    /// asserts), in a guild, a grade above 0 and not past GetMaxGradeIndex, with the grade right (0x20).</summary>
    public void SetMemberGrade(string name, int grade)
    {
        name ??= "";
        if (name.Length < 2 || name.Length > 0x18) return;
        string myName = PlayerName();
        if (string.IsNullOrEmpty(myName)) return;
        if (!InGuild) return;
        if (grade <= 0 || grade > MaxGradeIndex) return;
        if (!HasMemberPermission(myName, 0x20)) return;
        Send(new GuildMemberGradeRequest(name, grade));
    }

    /// <summary>SendGuildMode63 (@0x1413b7ad0): hand the master over — not the own name, in a guild, from the master.</summary>
    public void ChangeMaster(string name)
    {
        name ??= "";
        string myName = PlayerName();
        if (string.IsNullOrEmpty(myName)) return;
        if (string.Equals(myName, name, StringComparison.OrdinalIgnoreCase)) return;
        if (!InGuild) return;
        if (!IsMaster(myName)) return;
        Send(new GuildChangeMasterRequest(name));
    }

    /// <summary>FUN_1413b7c70 (@0x1413b7c70): a guild text of a kind. In a guild; kind 0 at most 1000 characters from the
    /// master; kind 1 (the notice) at most 300 from a member with the notice right (8) and no banned word; then
    /// CheckContentText (a refusal is its notice) and the send.</summary>
    public bool SendText(int kind, string text)
    {
        text ??= "";
        string myName = PlayerName();
        if (string.IsNullOrEmpty(myName)) return false;
        if (!InGuild) return false;
        if (kind == 0)
        {
            if (text.Length > 1000 || !IsMaster(myName)) return false;
        }
        else if (kind == 1)
        {
            if (text.Length > 300 || !HasMemberPermission(myName, 8)) return false;
            if (BannedWord?.Invoke(text) == true) return false;
        }
        if (!ContentPasses(text)) return false;
        Send(new GuildTextRequest(kind, text));
        return true;
    }

    /// <summary>SendGuildMode13 (@0x1413b80d0): the own memo — in a guild, at most 0x14 characters, CheckContentText.</summary>
    public bool SetMemo(string text)
    {
        text ??= "";
        if (!InGuild) return false;
        if (text.Length > 0x14) return false;
        if (!ContentPasses(text)) return false;
        Send(new GuildMemoRequest(text));
        return true;
    }

    /// <summary>SendGuildMode67 (@0x1413b8c40): a grade's name and rights. CGuildGrade::IsValid (the grade 0..5, the rights
    /// 0..4095), the grade at the index, the own name, in a guild, from the master, the index 0..5 and at most one past
    /// GetMaxGradeIndex.</summary>
    public void SetGrade(int index, GuildGrade grade)
    {
        if (grade == null || grade.Grade < 0 || grade.Grade >= 6 || grade.Permissions < 0 || grade.Permissions > 4095) return;
        if (grade.Grade != index) return;
        string myName = PlayerName();
        if (string.IsNullOrEmpty(myName)) return;
        if (!InGuild) return;
        if (!IsMaster(myName)) return;
        if (index < 0 || index >= 6) return;
        if (index > MaxGradeIndex + 1) return;
        Send(new GuildGradeRequest(index, grade));
    }

    /// <summary>SendGuildMode68 (@0x1413b8e60): the guild's propensity — in a guild, not the one it has, with the invite
    /// right (2).</summary>
    public void SetPropensity(int focus, int value)
    {
        string myName = PlayerName();
        if (string.IsNullOrEmpty(myName)) return;
        if (!InGuild) return;
        if (PropensityFocus == focus && Propensity == value) return;
        if (!HasMemberPermission(myName, 2)) return;
        Send(new GuildPropensityRequest(focus, value));
    }

    /// <summary>SendGuildMode86 (@0x1413bc6a0): not in a guild, ask for this character's join requests.</summary>
    public void SendMyRequests()
    {
        if (InGuild) return;
        Send(new GuildMyRequestsRequest());
    }

    /// <summary>SendGuildMode87 (@0x1413bc710): not in a guild, search by the propensity. Without one, the box of common
    /// 0x3c9 unless <paramref name="anyFocus"/>, which searches with 0x7fffffff.</summary>
    public void Search(bool anyFocus)
    {
        if (InGuild) return;
        int focus = SearchPropensity;
        if (focus == 0)
        {
            if (!anyFocus) { Box?.Invoke(Common(0x3c9)); return; }
            focus = 0x7fffffff;
        }
        Send(new GuildSearchRequest(focus, 1));
    }

    /// <summary>SendGuildMode88 (@0x1413bc860): not in a guild, search by name. Shorter than 2 is the notice
    /// s_guild_err_too_short_to_search; a failed CheckGuildName is its message.</summary>
    public bool SearchByName(string name)
    {
        name ??= "";
        if (InGuild) return false;
        if (name.Length < 2) { Notice?.Invoke(CommonByKey("s_guild_err_too_short_to_search"), 1, true); return false; }
        if (CheckName?.Invoke(name) is { } message) { Notice?.Invoke(message, 1, true); return false; }
        Send(new GuildSearchByNameRequest(name));
        return true;
    }

    /// <summary>ShowGuildError (@0x1413c66f0, :3230): the notice (ShowSystemNotice(text, 1, 1)) of an error code.</summary>
    public void ShowGuildError(int kind, int code, int arg) => Notice?.Invoke(ErrorText(code, arg), 1, true);

    /// <summary>ShowGuildError's text of a code.</summary>
    public string ErrorText(int code, int arg) => code switch
    {
        1 => Common(0x403),
        >= 3 and <= 8 => Common(0x401 + code),
        >= 0xa and <= 0x12 => Common(0x400 + code),
        0x13 => ClientFormat.Format(Common(0x413), CreateMinLevel),   // BlockFormat<int>(0x413, GuildCreateMinLevel)
        >= 0x14 and <= 0x1c => Common(0x400 + code),
        0x1e => Common(0x41d),
        0x20 => Common(0x41e),
        0x21 => Common(0x41f),
        0x29 => Common(0x424),
        >= 0x22 and <= 0x27 => Common(0x3a0 + code - 0x22),
        0x2a => CommonByKey("s_guild_err_not_enough_guild_level"),
        0x2b => CommonByKey("s_guild_err_not_enough_guild_fund"),
        0x2c => CommonByKey("s_guild_err_cannot_use_skill"),
        0x2d => CommonByKey("s_guild_err_too_fast_to_search_by_name"),
        0x2e => Common(0x3c2),
        0x2f => CommonByKey("s_guild_err_isNotVsGameTime"),
        0x30 => ClientFormat.Format(CommonByKey("s_guild_err_requireOnlineUserCount"), arg),   // FUN_14028dfa0<int>
        _ => Common(0x420),
    };
}
