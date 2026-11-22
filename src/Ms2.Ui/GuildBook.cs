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
/// they go out at once); the other sends (the grade edit, the PvP, the house) and the rest of the in-guild updates; the guild name tag (s2c 0x4d, FUN_1413c7700 guildmanager.cpp:3108: the name shown over
/// a player), which the server also sends after a leave or disband to clear the own tag — not read, so the own tag keeps
/// the guild name until a relogin; the game menu's guild badge (UIGameMenuDialog::FUN_1419381c0, the notify count of
/// GetGuildNotificationCount: the check-in and the requests for the guild).
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
    public int TotalTrophy => Info?.Members.Sum(m => m.TrophyTotal) ?? 0;
    public int TotalItemLevel => Info?.Members.Sum(m => m.ItemLevel) ?? 0;
    public int TotalCharLevel => Info?.Members.Sum(m => (int)(ushort)m.Level) ?? 0;

    /// <summary>GetMaxGradeIndex: the last grade's index (the grade count less one).</summary>
    public int MaxGradeIndex => (Info?.Grades.Count ?? 0) - 1;

    /// <summary>GetNotice: CGuildInfo's notice (s190).</summary>
    public string GuildNotice => Info?.Notice ?? "";

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

    /// <summary>FUN_1413b96e0 (@0x1413b96e0): use a personal skill: the guild level, then the meso (s_err_lack_meso). STILL-OPEN:
    /// the local player's existing buff check (AdditionalCli) is not ported, so a skill whose buff is up is not refused here.</summary>
    public bool UsePersonalSkill(int id)
    {
        if (!InGuild) return false;
        int level = SkillLevel(id);
        if (SkillTable?.Get(id, level) is not { } s || !s.IsPersonal) return false;
        if (GuildLevel < s.RequireLevel) return NoticeKey("s_guild_err_not_enough_guild_level");
        if (Mesos() < PersonalSkillDiscountedCost(id, level)) return NoticeKey("s_err_lack_meso");
        if (Level() == null) return false;     // GetLocalPlayer() == 0
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
                // also draws the dialog again and prints the line 0x3a8 when the notice byte is set.
                if (!InGuild) break;
                if (FindMemberByName(m.Applicant) is { } old) Info.Members.Remove(old);
                if (m.Member is { } member) Info.Members.Add(member);
                if (m.Member?.Mode == 3)
                {
                    if (m.Notify) ChatLine?.Invoke(ClientFormat.Format(Common(0x3a8), m.Name, m.Applicant), 0xd);
                    GuildInfoRefresh?.Invoke();
                    MemberRefresh?.Invoke(m.Applicant);
                }
                break;
            }
            case 0x13:
            {
                // OnPacketMemberLeft (@0x1413bfe50): the member of the name is removed with the line 0x3a6, but only
                // when such a member is; the dialog's UpdateGuildInfo and RemoveMember run whatever was found.
                if (!InGuild) break;
                if (FindMemberByName(m.Name) is { } gone) { Info.Members.Remove(gone); ChatLine?.Invoke(ClientFormat.Format(Common(0x3a6), m.Name), 0xd); }
                GuildInfoRefresh?.Invoke();
                MemberRemoved?.Invoke(m.Name);
                break;
            }
            case 0x54:
                // OnPacketJoinRequestAnswer (@0x1413c5520): the line 0x3d0 (accepted) or 0x3d1 (rejected) of the name.
                if (!InGuild) break;
                if (m.GuildId != 0)       // unsigned > 0
                    ChatLine?.Invoke(ClientFormat.Format(Common(m.Accepted ? 0x3d0 : 0x3d1), m.Applicant), 0xd);
                break;
            case 0x55:
                // OnPacketJoinRequestsForGuild (@0x1413c5690): the list replaced, then the dialog's FUN_14196da70.
                if (!InGuild) break;
                requests.Clear();
                requests.AddRange(m.GuildRequests);
                RequestsForGuildRefresh?.Invoke();
                break;
            case 0x2d:
                // OnPacketJoinRequestReceived (@0x1413c6460): the request added, then the dialog's FUN_14196da70.
                if (!InGuild) break;
                requests.Add(m.NewRequest);
                RequestsForGuildRefresh?.Invoke();
                break;
            case 0x2e:
            {
                // OnPacketJoinRequestWithdrawn (@0x1413c6580): the first request of the id is removed, then the dialog's
                // FUN_14196da70.
                if (!InGuild) break;
                int i = requests.FindIndex(r => r.Id == m.RequestId);
                if (i >= 0) requests.RemoveAt(i);
                RequestsForGuildRefresh?.Invoke();
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
            case 0x44:
                // FUN_1413bf2a0 (guildmanager.cpp:1855): in a guild, the propensity focus and value.
                if (!InGuild || Info == null) break;
                Info.B3 = m.Focus;
                Info.V14 = m.Value;
                break;
            case 0x2f:
            {
                // FUN_1413c6180 (@0x1413c6180): the first request of the id is removed, the dialog draws the list again
                // and the line 0x3bd (accepted) or 0x3be (rejected) names the guild and the applicant.
                if (!InGuild) break;
                int i = requests.FindIndex(r => r.Id == m.RequestId);
                if (i >= 0) requests.RemoveAt(i);
                RequestsForGuildRefresh?.Invoke();
                ChatLine?.Invoke(ClientFormat.Format(Common(m.Accepted ? 0x3bd : 0x3be), m.Name, m.Applicant), 0xd);
                break;
            }
        }
    }

    /// <summary>FUN_1413b5f90's Reset: the guild state is gone (the request lists are kept, as the client keeps them).</summary>
    void ResetGuild()
    {
        GuildId = 0;
        Info = null;
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
