using Ms2.Avm2;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Net.Packets;
using ReservedWords = Ms2.Formats.World.ReservedWords;

namespace Ms2.Ui.Hud;

/// <summary>
/// The C++ side of the guild dialog, UIGuildDialog (client/ms2/uiguilddialog.cpp; UI id 0x4e4f), for its movie uiguilddialog.
/// The state is <see cref="GuildBook"/> (CGuildManager). Outside a guild: the Join page (the search by propensity or by
/// name, the join requests and their cancel, this character's requests) and the Create page (the create box). In a guild:
/// the Info and Member pages, the accept page (the requests sent to the guild), the leave and disband boxes, the member
/// management (the invite, expel, grade and master change, the notice, the own memo, the propensity and the search flag), the grade edit (OnChangeGrade) and the rank buttons (the ranking dialog 0x4f24 and the web browser 0x4e6a are
/// events: neither is in this client).
/// The pages are made on first use (EnsurePageCreated) and laid on the movie root.
/// STILL-OPEN: the other in-guild pages (House, Storage, Raid, ChampionShip,
/// ChampionShipHistroy) and their callbacks (PvP, mail, house and mark), the character card of the master and expel boxes
/// (CMsgBoxData::SetCharacterInfo), the master popup
/// (OnSearchGuildMasterPopupMenu, 0x4e39) as an event, the block check of OnClickCreateGuild (CBlockManager
/// FindBlockedAction(7)), the web guide button (UIDialog::BindWebGuideButton, uidialog.cpp:997), the championship icon
/// (CGameRule::IsGuildChampionshipOpen), the event buttons of the 0x8a handler FUN_141968140 (modes 0x76/0x77), the
/// requests for the guild in the game menu's notify count (UIGameMenuDialog::FUN_1419381c0) and the tab settings
/// (vfunc_54 / vfunc_53, "Histroy" lastTabIndex).
/// </summary>
public sealed class GuildDialogController : UiController, IMessageBoxListener
{
    public override string Movie => "uiguilddialog";
    protected override string EntityMethod => "showGuildDialog";
    /// <summary>GetTypeName (@0x1419625d0).</summary>
    public override string TypeName => "GuildDialog";

    /// <summary>The UI id (eUIClassID 0x4e4f).</summary>
    public const int UiId = 0x4e4f;
    /// <summary>IsFeatureEnabled(0x110): the championship icon of OnLoadComplete.</summary>
    public const int ChampionshipFeature = 0x110;
    /// <summary>The search page's rows (FUN_14196d0c0: 9 a page).</summary>
    public const int SearchPageSize = 9;

    public GuildBook Book { get; }

    int tab;                    // m_nTab (0 in the ctor)
    int searchPage = 1;         // m_nSearchPage (1 in the ctor)
    int acceptPage = 1;         // m_nAcceptPage (1 in the ctor)
    object createPage;          // m_createPage
    object joinPage;            // m_joinPage
    object infoPage;            // m_infoPage
    object memberPage;          // m_memberPage
    object acceptRequestPage;   // m_acceptRequestPage
    object skillPage;           // m_skillPage
    object skillPage2;          // m_skillPage2
    object eventPage;           // m_eventPage
    object managePage;          // m_managePage
    object housePage;           // m_housePage
    object storagePage;         // m_storagePage
    object raidPage;            // m_raidPage
    object championshipPage;    // m_championshipPage
    object pvpHistoryPage;      // m_pvpHistoryPage
    bool loadingSeasons;        // m_bLoading: the season rows wait for the page's first info
    object eventVsGame;         // m_eventVsGame
    object eventRegisterButton, eventUnregisterButton, eventImageArea, eventLoader;   // m_button368, m_button378, m_eventImageArea, m_eventLoader
    /// <summary>m_requests (a multimap of the tick an open event stops being shown → the event id): kept in key order, equal keys in the order added.</summary>
    readonly List<(uint Expire, int Id)> requests = new();
    readonly SortedSet<int> activeSkills = new();   // m_1b0
    long lastSkillInfoSecond = long.MinValue;       // the server second UpdateSkillInfo last pushed
    int memberSort;             // m_nMemberSort (0 in the ctor)
    string masterTarget = "";   // m_strName: the member the changeMaster box names
    string expelTarget = "";    // m_strDesc: the member the expelMember box names

    /// <summary>GetStringCommon.</summary>
    public Func<int, string> Common = _ => "";
    /// <summary>GetStringCommonByKey.</summary>
    public Func<string, string> CommonByKey = _ => "";
    /// <summary>CGuildXmlData::GetLevelByExp.</summary>
    public Func<int, int> LevelByExp = _ => 0;
    /// <summary>Client::Constants GuildCreatePrice, GuildNameLengthMin, GuildNameLengthMax.</summary>
    public int CreatePrice = 1000, NameLengthMin = 2, NameLengthMax = 10;
    /// <summary>The local player's name (PCBase vfunc_77), or null without a local player.</summary>
    public Func<string> PlayerName = () => null;
    /// <summary>CFeatureManager::IsFeatureEnabled.</summary>
    public Func<int, bool> FeatureEnabled = _ => false;
    /// <summary>CStringTable::FUN_141811980(6, job): the job name; (3, map): the map name.</summary>
    public Func<int, string> JobName = id => id.ToString(), MapName = id => id.ToString();
    /// <summary>GetServerTime (seconds).</summary>
    public Func<long> ServerTime = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    /// <summary>The local player's character id (PCBase FUN_141656a80), or null without one.</summary>
    public Func<long?> PlayerCharId = () => null;
    /// <summary>FUN_141028be0(charId, code, 1, ""): the profile image path.</summary>
    public Func<long, string, string> ImagePath = (_, _) => "";
    /// <summary>CGuildXmlData::GetAccumExp and GetMaxLevel.</summary>
    public Func<int, int> AccumExp = _ => 0;
    public Func<int> MaxLevel = () => 0;
    /// <summary>FUN_140763e10: a number with the thousands separators.</summary>
    public Func<long, string> GroupText = n => Ms2.Ui.NumberText.Group(n.ToString());
    /// <summary>Client::Constants guildDonateMeso.</summary>
    public int DonateMeso = 10000;
    /// <summary>CReservedWordTable::ReplaceValueWords.</summary>
    public Func<string, string> ReplaceValueWords = s => s;
    /// <summary>CStringTable::FUN_141812290(effectId, level, tag): an additional effect's "name" or "tooltipDescription".</summary>
    public Func<uint, ushort, string, string> EffectText = (_, _, _) => "";
    /// <summary>The icon of an additional effect's level (SkillDataTableCli::FUN_140aafef0 then AdditionalLevelData::GetIcon); null without the row.</summary>
    public Func<uint, ushort, string> EffectIcon = (_, _) => null;
    /// <summary>FormatTimeSpan(CTimeSpan(seconds), true, true, true, false) (timeutil.cpp:371): days, hours and minutes.</summary>
    public Func<long, string> TimeSpanText = _ => "";

    /// <summary>The event table (CGuildXmlData's GuildEvent rows).</summary>
    public GuildEventTable Events;
    /// <summary>CStringTable::FUN_141811980(table, id): the string of a numbered table (3 = mapName, 0x26 = GuildEventDesc); "" when unknown.</summary>
    public Func<int, long, string> StringTableText = (_, _) => "";
    /// <summary>CFieldDataCli::Find(mapCode)->vfunc_2: the field's capacity, null when the field is unknown.</summary>
    public Func<int, int?> FieldCapacity = _ => null;
    /// <summary>GetTickCountMs2.</summary>
    public Func<uint> TickCount = () => (uint)Environment.TickCount64;

    /// <summary>FUN_14151f200: the box on show closes before the use box shows (uiguilddialog.cpp:1105).</summary>
    public Action NextBox = MessageBoxQueue.Next;
    /// <summary>FUN_140d73c60: show a message box.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;

    /// <summary>The group type the guild's dungeon button sets (SetGroupType(6), uiguilddialog.cpp:1234-1241).</summary>
    public const int DungeonRoomGroupType = 6;

    /// <summary>
    /// onOpenDungeonDialog (uiguilddialog.cpp:1230-1243): OpenDialog(0x4ebd) on the dungeon room window, then, when it exists,
    /// SetGroupType(6). The host opens it with the group type given; it sets no portal id and no room.
    /// STILL-OPEN: FUN_141924630, the guard of the refresh before SetGroupType (vfunc_68), is not recovered (treated as true).
    /// </summary>
    public Action<int> OpenDungeonRoomDialog;

    /// <summary>OnSearchGuildMasterPopupMenu: the popup 0x4e39 at (x, y) for the master's name (Unk_PopupMenuInfo(x, y, 3,
    /// 0x3f3fe), SetGuildInfo from FindSearchResultByName). STILL-OPEN.</summary>
    public event Action<float, float, string> PopupRequested;
    /// <summary>OnGuildMemberPopupMenu (:811): a member's right click, the popup 0x4e39 at (x, y) for the member found by
    /// name (UIPopUpMenu::PopUpMenuFromGuildMember, <see cref="PopupMenuController.OpenForGuildMember"/>).</summary>
    public event Action<float, float, GuildMember> MemberPopupRequested;
    /// <summary>IsFeatureEnabled(0x3f): the rank buttons open the ranking dialog 0x4f24.</summary>
    public const int RankingDialogFeature = 0x3f;
    /// <summary>IsFeatureEnabled(0x3b): without the dialog, the rank buttons open a maview page.</summary>
    public const int RankPageFeature = 0x3b;
    /// <summary>The page urls of the constructor (:276-278): m_strRankUrl, m_strRealtimeRankUrl, m_strPvpRankUrl.</summary>
    public const string RankUrl = "maview:/Rank/Guild", RealtimeRankUrl = "maview:/Rank/Guild?tp=realtime",
        PvpRankUrl = "maview:/Rank/GuildPVP?tp=realtime";
    /// <summary>The rank buttons with feature 0x3f: UIGameRankingDialog (FUN_140b2d030(0x4f24)) SelectCategory(category, 0)
    /// and OpenDialog(0x4f24); category 1 the guild ranks, 5 the guild PvP ranks.</summary>
    public event Action<int> RankingRequested;
    /// <summary>The rank buttons with feature 0x3b only: OpenRankPage / OpenRealtimeRankPage / OpenPvpRankPage
    /// (@0x141975500 / @0x1419754b0 / @0x141975550), Navigate(url, 1) on the web browser 0x4e6a.</summary>
    public event Action<string> RankPageRequested;

    /// <summary>OnClickOpenMayview (:718), OnOpenGuildRank (:786), OnViewPvpRank (:981): the ranking dialog with feature
    /// 0x3f, else the page with feature 0x3b; each returns 1.</summary>
    bool OpenRanks(int category, string url)
    {
        if (FeatureEnabled(RankingDialogFeature)) RankingRequested?.Invoke(category);
        else if (FeatureEnabled(RankPageFeature)) RankPageRequested?.Invoke(url);
        return true;
    }

    public GuildDialogController(GuildBook book) => Book = book;

    /// <summary>m_nTab.</summary>
    public int Tab => tab;
    /// <summary>m_nSearchPage.</summary>
    public int SearchPage => searchPage;
    /// <summary>The Join page (m_joinPage) once made.</summary>
    public object JoinPage => joinPage;
    /// <summary>The Create page (m_createPage) once made.</summary>
    public object CreatePage => createPage;
    /// <summary>The Info page (m_infoPage) once made.</summary>
    public object InfoPage => infoPage;
    /// <summary>The Member page (m_memberPage) once made.</summary>
    public object MemberPage => memberPage;
    /// <summary>The accept page (m_acceptRequestPage) once made.</summary>
    public object AcceptRequestPage => acceptRequestPage;
    /// <summary>m_skillPage.</summary>
    public object SkillPage => skillPage;
    /// <summary>m_skillPage2.</summary>
    public object SkillPage2 => skillPage2;

    bool NoMovie => Host?.Entity == null;

    /// <summary>OnLoadComplete (@0x141961ab0, :371).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        Book.SearchRefresh -= OnSearchRefresh;
        Book.SearchRefresh += OnSearchRefresh;
        Book.MyRequestsRefresh -= BuildSearchMy;
        Book.MyRequestsRefresh += BuildSearchMy;
        Book.RequestsForGuildRefresh -= OnRequestsForGuild;
        Book.RequestsForGuildRefresh += OnRequestsForGuild;
        Book.GuildInfoRefresh -= OnGuildInfoRefresh;
        Book.GuildInfoRefresh += OnGuildInfoRefresh;
        Book.VsGameRefresh -= UpdateEventButtons;
        Book.VsGameRefresh += UpdateEventButtons;
        Book.EventsRefresh -= BuildEvents;
        Book.EventsRefresh += BuildEvents;
        Book.HouseRefresh -= SetHouseInfo;
        Book.HouseRefresh += SetHouseInfo;
        Book.NpcRefresh -= SetNpcInfo;
        Book.NpcRefresh += SetNpcInfo;
        Book.StorageRefresh -= UpdateStoragePage;
        Book.StorageRefresh += UpdateStoragePage;
        Book.StorageHistoryRefresh -= UpdateStorageHistory;
        Book.StorageHistoryRefresh += UpdateStorageHistory;
        Book.RaidRefresh -= UpdateRaid;
        Book.RaidRefresh += UpdateRaid;
        Book.ChampionshipRatingRefresh -= UpdateChampionshipRating;
        Book.ChampionshipRatingRefresh += UpdateChampionshipRating;
        Book.PvpHistoryRefresh -= UpdatePvpHistory;
        Book.PvpHistoryRefresh += UpdatePvpHistory;
        Book.PvpSeasonHistoryRefresh -= UpdatePvpSeasonHistory;
        Book.PvpSeasonHistoryRefresh += UpdatePvpSeasonHistory;
        Book.MemberRefresh -= OnMemberRefresh;
        Book.MemberRefresh += OnMemberRefresh;
        Book.MemberRemoved -= OnMemberRemoved;
        Book.MemberRemoved += OnMemberRemoved;
        Book.ExpRefresh -= OnExpRefresh;
        Book.ExpRefresh += OnExpRefresh;
        Book.FundRefresh -= OnFundRefresh;
        Book.FundRefresh += OnFundRefresh;
        if (!NoMovie)
        {
            InitializeGuildDialog();
            if (Book.InGuild)
            {
                if (Host.Child("m_category") is { } bar)
                {
                    Host.Set(bar, "focused", 1);                // SetFocused(1)
                    Host.Set(bar, "selectedIndex", tab);        // SetSelectedIndex(m_nTab)
                    Host.Invoke(bar, "invalidate");             // vfunc_24(1): invalidate, then validateNow
                    Host.Invoke(bar, "validateNow");
                    Host.Invoke(Host.Entity, "onCategoryChange");
                }
            }
            else Book.Search(true);                             // SendGuildMode87(1)
            if (FeatureEnabled(ChampionshipFeature) && Host.Child("m_championshipIcon") is { } icon)
                Host.SetVisible(icon, false);                   // IsGuildChampionshipOpen && IsInGuild; STILL-OPEN: never open
        }
        OnRequestsForGuild();                                   // FUN_14196da70: setAcceptMemberCount(the requests' count)
    }

    /// <summary>vfunc_67 (:297): ClosePopup on the root, UIDialog's close, ClearSearchResults. The pages stay
    /// until the movie is dropped (vfunc_26 clears them).</summary>
    public override void Close()
    {
        if (!NoMovie) Host.Invoke(Host.Entity, "ClosePopup");
        base.Close();
        Book.SearchRefresh -= OnSearchRefresh;
        Book.MyRequestsRefresh -= BuildSearchMy;
        Book.RequestsForGuildRefresh -= OnRequestsForGuild;
        Book.GuildInfoRefresh -= OnGuildInfoRefresh;
        Book.VsGameRefresh -= UpdateEventButtons;
        Book.EventsRefresh -= BuildEvents;
        Book.HouseRefresh -= SetHouseInfo;
        Book.NpcRefresh -= SetNpcInfo;
        Book.StorageRefresh -= UpdateStoragePage;
        Book.StorageHistoryRefresh -= UpdateStorageHistory;
        Book.RaidRefresh -= UpdateRaid;
        Book.ChampionshipRatingRefresh -= UpdateChampionshipRating;
        Book.PvpHistoryRefresh -= UpdatePvpHistory;
        Book.PvpSeasonHistoryRefresh -= UpdatePvpSeasonHistory;
        Book.MemberRefresh -= OnMemberRefresh;
        Book.MemberRemoved -= OnMemberRemoved;
        Book.ExpRefresh -= OnExpRefresh;
        Book.FundRefresh -= OnFundRefresh;
        Book.ClearSearchResults();
        createPage = null;
        joinPage = null;
        infoPage = null;
        memberPage = null;
        managePage = null;
        housePage = null;
        storagePage = null;
        raidPage = null;
        championshipPage = null;
        pvpHistoryPage = null;
        loadingSeasons = false;
        acceptRequestPage = null;
    }

    /// <summary>The loaded movie's setupCategory takes the championship flag (the KMS movie, whose client calls it). The GMS2
    /// movie's takes none and its configUI calls it.</summary>
    public bool MovieTakesCategoryFlag =>
        !NoMovie && Host.Get(Host.Entity, "setupCategory") is AsFunction f && Convert.ToInt32(Host.Get(f, "length")) >= 1;

    /// <summary>InitializeGuildDialog (:2945).</summary>
    void InitializeGuildDialog()
    {
        // FUN_1419260d0("m_webGuideButton", 0) = UIDialog::BindWebGuideButton: STILL-OPEN (WebGuideTable)
        // SetupCategory: IsGuildChampionshipOpen && IsInGuild (the setupCategory call @0x14196b2b9). The GMS2 client has no such call
        // (InitializeGuildDialog @0x140d65034 goes from the web guide button to showGuildDialog @0x140d65097, and its exe has no
        // setupCategory string): its movie's configUI calls setupCategory() itself, with no argument. The movie decides, not
        // the session or the locale: a KMS server can run with GMS2 data and the reverse.
        if (MovieTakesCategoryFlag) Host.Invoke(Host.Entity, "setupCategory", false);
        Host.Invoke(Host.Entity, "showGuildDialog", Book.InGuild ? 1 : 0); // ShowGuildDialog
        SetMasterInfo();
        SetGradeInfo();
        BuildMembers();                                                     // FUN_141975620
        SetPlayer();                                                        // FUN_14196b3a0
        UpdateAttendBackground();
    }

    /// <summary>UpdateAttendBackground (@0x141973a70, :2163): m_attendBG shown while CGuildManager::CanAttend.
    /// STILL-OPEN: the client also calls it on a day change (FUN_1413b5e80) and on the own type 0x24, not read yet.</summary>
    void UpdateAttendBackground()
    {
        if (Host.Child("m_attendBG") is { } attend) Host.SetVisible(attend, Book.CanAttend);
    }

    /// <summary>OpenCategoryPage (@0x141969040): HideAllPages, the page made, then ShowCategoryPage.</summary>
    public void OpenCategoryPage(string category)
    {
        if (NoMovie) return;
        HideAllPages();
        if (category == "Join") CreateJoinPage();
        else if (category == "Create") EnsurePageCreated("guild_create_page", ref createPage);   // CreateCreatePage
        else if (category == "Info") CreateInfoPage();
        else if (category == "Member") CreateMemberPage();
        else if (category == "AcceptMember") CreateAcceptRequestPage();
        else if (category == "Skill") CreateSkillPage();
        else if (category == "MemberSkill") CreateMemberSkillPage();
        else if (category == "Event") CreateEventPage();
        else if (category == "Manager") CreateManagePage();
        else if (category == "House") CreateHousePage();
        else if (category == "Storage") CreateStoragePage();
        else if (category == "Raid") CreateRaidPage();
        else if (category == "ChampionShip") CreateChampionshipPage();
        else if (category == "ChampionShipHistroy") CreatePvpHistoryPage();
        if (category == "Join") SetPageVisible(joinPage, true);              // ShowCategoryPage (@0x141969290)
        else if (category == "Create") SetPageVisible(createPage, true);
        else if (category == "Info") SetPageVisible(infoPage, true);
        else if (category == "Member") SetPageVisible(memberPage, true);
        else if (category == "AcceptMember") SetPageVisible(acceptRequestPage, true);
        else if (category == "Skill") SetPageVisible(skillPage, true);
        else if (category == "MemberSkill") SetPageVisible(skillPage2, true);
        else if (category == "Event") SetPageVisible(eventPage, true);
        else if (category == "Manager") SetPageVisible(managePage, true);
        else if (category == "House") SetPageVisible(housePage, true);
        else if (category == "Storage") SetPageVisible(storagePage, true);
        else if (category == "Raid") SetPageVisible(raidPage, true);
        else if (category == "ChampionShip") SetPageVisible(championshipPage, true);
        else if (category == "ChampionShipHistroy") SetPageVisible(pvpHistoryPage, true);
    }

    /// <summary>HideAllPages (@0x1419698f0).</summary>
    void HideAllPages()
    {
        SetPageVisible(joinPage, false);
        SetPageVisible(createPage, false);
        SetPageVisible(infoPage, false);
        SetPageVisible(memberPage, false);
        SetPageVisible(acceptRequestPage, false);
        SetPageVisible(skillPage, false);
        SetPageVisible(skillPage2, false);
        SetPageVisible(eventPage, false);
        SetPageVisible(managePage, false);
        SetPageVisible(housePage, false);
        SetPageVisible(storagePage, false);
        SetPageVisible(raidPage, false);
        SetPageVisible(championshipPage, false);
        SetPageVisible(pvpHistoryPage, false);
    }

    /// <summary>CreateInfoPage (@0x141969f60): made now, UpdateGuildInfo, SetMasterInfo, UpdateGuildExp, the fund and the
    /// player's rights. STILL-OPEN: SetGuildMark of the guild's own mark.</summary>
    void CreateInfoPage()
    {
        if (!EnsurePageCreated("guild_info_page", ref infoPage)) return;
        UpdateGuildInfo();
        SetMasterInfo();
        UpdateGuildExp();
        SetGuildFund();
        SetPlayer();
    }

    /// <summary>CreateMemberPage (@0x14196a090): made now, the members, then the sort dropdown's index.</summary>
    void CreateMemberPage()
    {
        if (!EnsurePageCreated("guild_member_page", ref memberPage)) return;
        BuildMembers();
        if (Host.Child("m_sortDropdown", memberPage) is { } sort) Host.Set(sort, "selectedIndex", memberSort);
    }

    /// <summary>CreateManagePage (@0x14196a1b0, uiguilddialog.cpp:1591): made now, SetGradeInfo, then the member rows (FUN_141975620).
    /// STILL-OPEN: in the NA region the edit grade popup's grade name takes 15 characters (SetMaxChars 0xf); the port has no region flag.</summary>
    void CreateManagePage()
    {
        if (!EnsurePageCreated("guild_manage_page", ref managePage)) return;
        SetGradeInfo();
        BuildMembers();
    }

    /// <summary>The House feature (IsFeatureEnabled 0x5b): both parts of the House page do nothing without it.</summary>
    public const int HouseFeature = 0x5b;

    /// <summary>CreateHousePage (@0x14196b0e0, uiguilddialog.cpp:1687): made now, then the house part (FUN_141971490) and the npc part (FUN_141971ac0).</summary>
    void CreateHousePage()
    {
        if (!EnsurePageCreated("guild_house_page", ref housePage)) return;
        SetHouseInfo();
        SetNpcInfo();
    }

    /// <summary>CreateStoragePage (@0x14196b160, uiguilddialog.cpp:1698): made now, then UpdateStoragePage.</summary>
    void CreateStoragePage()
    {
        if (EnsurePageCreated("guild_storage_page", ref storagePage)) UpdateStoragePage();
    }

    /// <summary>The Storage page (m_storagePage) once made.</summary>
    public object StoragePage => storagePage;

    /// <summary>UpdateStoragePage (@0x141972520, uiguilddialog.cpp:2640): nothing without the page.
    /// STILL-OPEN: the item slot bag. The client lists the storage's items (CItem of the id and rank, the count), destroys and creates the
    /// ItemSlotBag of m_strStorageName (icon type 0xc15, flags 0x800, the slot callback UpdateStorageSlotCount) and calls the page's
    /// initItemData with bagId; the port has no ItemSlotBag for a dialog, so the item grid is not filled.</summary>
    void UpdateStoragePage()
    {
        if (storagePage == null) return;
    }

    /// <summary>The last history text handed to the page's text area, raised just before it is set.</summary>
    public event Action<string> StorageHistoryTextBuilt;

    /// <summary>The grade's name colour (CItemDataTable::GetTextColorString: "#" and the colour in hex).</summary>
    public Func<int, uint> ItemTextColor = _ => 0xffffff;

    /// <summary>UpdateStorageHistory (@0x1419729d0, uiguilddialog.cpp:2692): the history newest first, the lines of the last 7 days only
    /// (older ones skipped), each s_guild_storage_history_gift of the giver, the item (its name in its grade's colour), the count, the
    /// receiver and the date (s_common_date_month_min of the local year, month, day, hour, minute), joined by newlines, into
    /// m_history.m_textArea as HTML. Nothing without the page.</summary>
    void UpdateStorageHistory()
    {
        if (storagePage == null) return;
        long since = ServerTime() - 7L * 86400;
        var sb = new System.Text.StringBuilder();
        var history = Book.StorageHistory;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            var h = history[i];
            if (h.Time < since) continue;
            var t = Ms2.Net.MsTime.LocalTm(h.Time);
            string date = ClientFormat.Format(CommonByKey("s_common_date_month_min"), t.Year, t.Month, t.Day, t.Hour, t.Minute);
            string item = "<FONT color='#" + ItemTextColor(h.Item.Rank).ToString("x", System.Globalization.CultureInfo.InvariantCulture) + "'>" + StringTableText(1, h.Item.ItemId) + "</FONT>";
            string line = ClientFormat.Format(CommonByKey("s_guild_storage_history_gift"), h.From.Name, item, (uint)h.Item.Count, h.To.Name, date);
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);
        }
        StorageHistoryTextBuilt?.Invoke(sb.ToString());
        if (Host.Child("m_history", storagePage) is not { } clip) return;
        if (Host.Child("m_textArea", clip) is not { } area) return;
        Host.Set(area, "htmlText", sb.ToString());
    }

    /// <summary>CreateRaidPage (@0x14196b1d0, uiguilddialog.cpp:1706): made now, then UpdateRaidScore and the raid history (FUN_141973220).</summary>
    void CreateRaidPage()
    {
        if (!EnsurePageCreated("guild_raid_page", ref raidPage)) return;
        UpdateRaid();
    }

    /// <summary>The Raid page (m_raidPage) once made.</summary>
    public object RaidPage => raidPage;

    /// <summary>UpdateRaidScore then FUN_141973220 (the order of the page's creation and of the raid reset packet).</summary>
    void UpdateRaid()
    {
        UpdateRaidScore();
        UpdateRaidHistory();
    }

    /// <summary>UpdateRaidScore (@0x141972fc0, uiguilddialog.cpp:2718): the local member's total raid count and the guild's total
    /// (s_guild_raid_info_score) into the m_scoreText of the page's m_info clip. Nothing without the page, the member or the clip.</summary>
    void UpdateRaidScore()
    {
        if (raidPage == null) return;
        if (Book.Me is not { } me) return;
        if (Host.Child("m_info", raidPage) is not { } info) return;
        SetChildText(info, "m_scoreText", ClientFormat.Format(CommonByKey("s_guild_raid_info_score"), me.RaidTotal, Book.TotalRaidCount));
    }

    /// <summary>The rows handed to setHistory, raised just before the call.</summary>
    public event Action<IReadOnlyList<object>> RaidHistoryRowsBuilt;

    /// <summary>FUN_141973220 (@0x141973220, uiguilddialog.cpp:2100): a row per raid of the table (ids ascending) to setHistory: whether the local
    /// member cleared it (a count above 0) and s_guild_raid_history_desc of the raid's name (string table 0x14), the member's name and the count.
    /// Nothing without the page or the member.</summary>
    void UpdateRaidHistory()
    {
        if (raidPage == null) return;
        if (Book.Me is not { } me) return;
        var rows = new List<object>();
        foreach (uint id in Book.Raids?.Ids() ?? Enumerable.Empty<uint>())
        {
            if (Book.Raids.Get(id) is not { } raid) continue;
            int count = me.Raid.TryGetValue((int)raid.DungeonRoomId, out var c) ? c : 0;
            string text = ClientFormat.Format(CommonByKey("s_guild_raid_history_desc"), StringTableText(0x14, raid.DungeonRoomId), Book.PlayerName(), count);
            rows.Add(Host.Obj(("isComplete", count > 0), ("text", text)));
        }
        RaidHistoryRowsBuilt?.Invoke(rows);
        Host.Invoke(raidPage, "setHistory", rows);
    }

    /// <summary>CGameRule::IsGuildChampionshipOpen (@0x14047e880), the flag GameEventGuildChampionship::OnStart (@0x1403c6a10, gameevent.cpp:3109) sets
    /// when feature 0x110 is on and vfunc_6 (@0x1403c6a50) clears. STILL-OPEN: the game event that raises it is not ported, so the host leaves it false.</summary>
    public Func<bool> GuildChampionshipOpen = () => false;
    /// <summary>The game option's etc flag m_n00 (SetInformArena). STILL-OPEN: the option is not in the host.</summary>
    public Func<bool> InformArena = () => false;
    /// <summary>CGuildManager::GetTargetGuildName (the guild the championship is set against, UpdateBelongGuildText). STILL-OPEN: the target
    /// guild (SetTargetGuildAndNotify, @0x1413bc250) is not ported, so the host leaves it empty.</summary>
    public Func<string> TargetGuildName = () => "";
    /// <summary>FUN_14083bdc0(CMsTime, true, "/", ":"): the date and time text of an instant.</summary>
    public Func<long, string> DateText = unix => MailTexts.DateString(unix, 0);

    /// <summary>The ChampionShip page (m_championshipPage) and the battle history page (m_pvpHistoryPage) once made.</summary>
    public object ChampionshipPage => championshipPage;
    public object PvpHistoryPage => pvpHistoryPage;

    /// <summary>The rating handed to setRating (score, fill rate, grade, text), raised just before the call.</summary>
    public event Action<int, float, int, string> ChampionshipRatingBuilt;
    /// <summary>The rows handed to buildHistory and buildPrevHistory, raised just before the call.</summary>
    public event Action<IReadOnlyList<object>> PvpHistoryRowsBuilt;
    public event Action<IReadOnlyList<object>> PvpSeasonRowsBuilt;

    /// <summary>CreateChampionshipPage (@0x14196a320, uiguilddialog.cpp:1611): only while the championship is open; made now, then
    /// UpdateChampionshipInfo and UpdateChampionshipRating.</summary>
    void CreateChampionshipPage()
    {
        if (!GuildChampionshipOpen()) return;
        if (!EnsurePageCreated("guild_champion_ship_page", ref championshipPage)) return;
        UpdateChampionshipInfo();
        UpdateChampionshipRating();
    }

    /// <summary>CreatePvpHistoryPage (@0x14196a3b0, :1620): only while the championship is open; made now, then RequestPvpHistoryAndSeason.</summary>
    void CreatePvpHistoryPage()
    {
        if (!GuildChampionshipOpen()) return;
        if (!EnsurePageCreated("guild_pvp_history_page", ref pvpHistoryPage)) return;
        Book.RequestPvpHistoryAndSeason();
    }

    /// <summary>UpdateChampionshipInfo (@0x141973610, :2130): with feature 0x110, SetInformArena, the season's name and its period
    /// ("{begin} ~ {end}", both empty without a season), UpdateBelongGuildText, then the season rows deferred by m_bLoading.</summary>
    void UpdateChampionshipInfo()
    {
        if (!FeatureEnabled(ChampionshipFeature) || championshipPage == null) return;
        Host.Invoke(championshipPage, "SetInformArena", InformArena());
        string title = "", period = "";
        var season = Book.GetSeason();
        if (season.Id != 0)
        {
            title = season.Name;
            period = ClientFormat.Format("{0} ~ {1}", DateText(season.Begin), DateText(season.End));
        }
        SetChildText(championshipPage, "m_titleText", title);
        SetChildText(championshipPage, "m_periodText", period);
        UpdateBelongGuildText();
        if (loadingSeasons)
        {
            UpdatePvpSeasonHistory();
            loadingSeasons = false;
        }
    }

    /// <summary>UpdateBelongGuildText (@0x14196cf80, :1904): string 0x3fb of the target guild's name, empty without one.</summary>
    void UpdateBelongGuildText()
    {
        if (championshipPage == null) return;
        string guild = TargetGuildName() ?? "";
        SetChildText(championshipPage, "m_belongGuildText", guild.Length < 1 ? "" : ClientFormat.Format(Common(0x3fb), guild));
    }

    /// <summary>UpdateChampionshipRating (@0x14196c1d0, :1815): the PvP state, the grade of the score (string table 0x2d) and how far the
    /// score is to the next grade's points (1 at the top) to setRating, the season's result (0x3ee: total, wins, losses), the member's
    /// participation count (0x3f5) and the result on the battle page. Nothing without the page.</summary>
    void UpdateChampionshipRating()
    {
        if (championshipPage == null) return;
        int score = Book.Score;
        Host.Invoke(championshipPage, "SetPvPState", Book.PvpState);
        var season = Book.GetSeason();
        int grade = season.GetRankForScore(score);
        string strGrade = StringTableText(0x2d, grade);
        float rate = 1.0f;
        int min = season.GetRankPoints(grade), next;
        if (grade < 7 && min != (next = season.GetRankPoints(grade + 1)))
            rate = (float)(score - min) / (float)(next - min);
        if (GuildChampionshipOpen())
        {
            grade = season.GetRankForScore(score);
            strGrade = StringTableText(0x2d, grade);
            int min2 = season.GetRankPoints(grade), next2;
            if (grade < 7 && min2 != (next2 = season.GetRankPoints(grade + 1)))
                rate = (float)(score - min2) / (float)(next2 - min2);
        }
        string strScore = ClientFormat.Format(Common(0x3ed), strGrade, score);
        ChampionshipRatingBuilt?.Invoke(score, rate, grade, strScore);
        Host.Invoke(championshipPage, "setRating", score, (double)rate, grade, strScore);
        int win = Book.WinCount, lose = Book.LoseCount;
        string result = ClientFormat.Format(Common(0x3ee), win + lose, win, lose);
        SetChildText(championshipPage, "m_seasonResult", result);
        if (Book.Me is { } me) SetChildText(championshipPage, "m_participate", ClientFormat.Format(Common(0x3f5), me.Fe0));
        if (pvpHistoryPage != null) Host.Invoke(pvpHistoryPage, "setSeasonResult", result);
    }

    /// <summary>FUN_141974240 (@0x141974240, :2806): the battle rows, newest first, to buildHistory. Nothing without the battle page.
    /// STILL-OPEN: the guild marks of the rows' guilds are fetched from the web (RequestHistoryGuildMarkToWeb) and handed to the page afterwards.</summary>
    void UpdatePvpHistory()
    {
        if (pvpHistoryPage == null) return;
        var rows = new List<object>();
        foreach (var h in Book.PvpHistoryNewestFirst) rows.Add(PvpHistoryRow(h));
        PvpHistoryRowsBuilt?.Invoke(rows);
        Host.Invoke(pvpHistoryPage, "buildHistory", rows);
    }

    /// <summary>FUN_141974640 (@0x141974640, :2847): the finished seasons, newest first, to buildPrevHistory and their summed result to
    /// setPrevSeasonResult. Without the dialog's movie it only sets m_bLoading, and the first UpdateChampionshipInfo runs it.</summary>
    void UpdatePvpSeasonHistory()
    {
        if (NoMovie) { loadingSeasons = true; return; }
        if (pvpHistoryPage == null) return;
        var rows = new List<object>();
        int win = 0, lose = 0;
        foreach (var h in Book.PvpSeasonHistoryNewestFirst)
        {
            rows.Add(Host.Obj(("seasonName", h.SeasonName), ("combatHistory", ClientFormat.Format(Common(0x3ee), h.Wins + h.Losses, h.Wins, h.Losses)),
                ("rank", ClientFormat.Format(Common(0x3ef), h.Rank)), ("grade", h.Grade)));
            win += h.Wins; lose += h.Losses;
        }
        PvpSeasonRowsBuilt?.Invoke(rows);
        Host.Invoke(pvpHistoryPage, "buildPrevHistory", rows);
        Host.Invoke(pvpHistoryPage, "setPrevSeasonResult", ClientFormat.Format(Common(0x3ee), win + lose, win, lose));
    }

    /// <summary>FUN_141977050 (@0x141977050): "id,mark:id,mark" into the marks, an entry not of two parts left out, joined with ",".</summary>
    public static string MemberMarks(string text)
    {
        var marks = new List<string>();
        foreach (var entry in (text ?? "").Split(':'))
        {
            if (entry.Length < 1) continue;
            var parts = entry.Split(',');
            if (parts.Length != 2) continue;
            marks.Add(parts[1]);
        }
        return string.Join(",", marks);
    }

    /// <summary>FillPvpHistoryRow (@0x141977b70, :3301): the battle seen from the local guild on the left (a battle of another guild mirrored:
    /// the second guild left, the rounds' wins the 'L's). The time is the days, hours or minutes since (0x3d2-0x3d4, else 0x3d5, empty when not past).</summary>
    object PvpHistoryRow(GuildPvpHistory h)
    {
        string time = "";
        long elapsed = ServerTime() - h.Time;
        if (elapsed > 0)
        {
            var span = TimeSpan.FromSeconds(elapsed);
            if (span.Days > 0) time = ClientFormat.Format(Common(0x3d2), (long)span.Days);
            else if (span.Hours > 0) time = ClientFormat.Format(Common(0x3d3), span.Hours);
            else if (span.Minutes > 0) time = ClientFormat.Format(Common(0x3d4), span.Minutes);
            else time = Common(0x3d5);
        }
        int win = 0, lose = 0;
        for (int i = 0; i < 5; i++)
        {
            if (h.RoundResults[i] == 'W') win++;
            else if (h.RoundResults[i] == 'L') lose++;
        }
        bool own = h.FirstGuildId == Book.GuildId;
        var fields = new List<(string, object)>();
        bool noFight = false, isWin = true;
        string score;
        if (own)
        {
            score = ClientFormat.Format(Common(0x3e9), win, lose);
            if (win == 0 && lose == 0)
            {
                noFight = true;
                score = Common(0x3ea);
                if (h.Result == 1) { score = Common(0x3eb); isWin = false; }
            }
        }
        else
        {
            isWin = false;
            score = ClientFormat.Format(Common(0x3e9), lose, win);
            if (win == 0 && lose == 0) { noFight = true; score = Common(0x3eb); }
        }
        fields.Add(("leftSn", (own ? h.FirstGuildId : h.SecondGuildId).ToString()));
        fields.Add(("rightSn", (own ? h.SecondGuildId : h.FirstGuildId).ToString()));
        fields.Add(("isWin", isWin));
        fields.Add(("leftGuildName", own ? h.FirstGuildName : h.SecondGuildName));
        fields.Add(("rightGuildName", own ? h.SecondGuildName : h.FirstGuildName));
        fields.Add(("score", score));
        fields.Add(("isNofight", noFight));
        for (int i = 0; i < 5; i++)
        {
            fields.Add(("left" + (i + 1), MemberMarks(own ? h.FirstMembers[i] : h.SecondMembers[i])));
            fields.Add(("right" + (i + 1), MemberMarks(own ? h.SecondMembers[i] : h.FirstMembers[i])));
            fields.Add(("result" + (i + 1), h.RoundResults[i] == (own ? 'W' : 'L')));
        }
        fields.Add(("time", time));
        return Host.Obj(fields.ToArray());
    }

    /// <summary>The rows handed to setHouseFacility, raised just before the call.</summary>
    public event Action<IReadOnlyList<object>> HouseFacilityRowsBuilt;

    /// <summary>The rows handed to setNpc, raised just before the call.</summary>
    public event Action<IReadOnlyList<object>> NpcRowsBuilt;

    /// <summary>The House page (m_housePage) once made.</summary>
    public object HousePage => housePage;

    /// <summary>FUN_141971490 (@0x141971490, uiguilddialog.cpp:2452-2495): the house of the guild's level and theme. Its theme title (string table
    /// 0x36), its area (s_home_info_area) and its facility names (string table 0x37, two to a row) go to the page's "m_house" clip, the facility rows
    /// to setHouseFacility, and its image path to setPreviewImage. Nothing without the feature, the page or the house row.</summary>
    void SetHouseInfo()
    {
        if (!FeatureEnabled(HouseFeature) || housePage == null) return;
        if (Book.Houses?.Get(Book.HouseLevel, Book.HouseTheme) is not { } house) return;
        if (Host.Child("m_house", housePage) is not { } houseClip) return;
        SetChildText(houseClip, "m_themeTitle", StringTableText(0x36, house.StringKey));
        if (Host.Child("m_houseInfo", houseClip) is { } info)
        {
            SetChildText(info, "m_areaText", ClientFormat.Format(CommonByKey("s_home_info_area"), house.AreaSize));
            var rows = new List<object>();
            var ids = house.Facilities;
            for (int i = 0; i < ids.Count; i += 2)
            {
                var row = new List<(string, object)> { ("facility1", StringTableText(0x37, ids[i])) };
                if (i + 1 < ids.Count) row.Add(("facility2", StringTableText(0x37, ids[i + 1])));
                rows.Add(Host.Obj(row.ToArray()));
            }
            HouseFacilityRowsBuilt?.Invoke(rows);
            Host.Invoke(housePage, "setHouseFacility", rows);
        }
        Host.Invoke(housePage, "setPreviewImage", house.ImagePath);
    }

    /// <summary>FUN_141971ac0 (@0x141971ac0, uiguilddialog.cpp:2499-2553): a row per npc kind of the table to setNpc: its name (s_guild_ui_house_npc_name
    /// with the level, or _nolevel at level 0), the next level's conditions (the guild and house levels) and cost (a number, or _nocost), and
    /// whether the master can upgrade it (the guild and house levels reach the next row's). At the top level the row's texts are "MAX" and it
    /// cannot be upgraded. Nothing without the feature or the page.</summary>
    void SetNpcInfo()
    {
        if (!FeatureEnabled(HouseFeature) || housePage == null) return;
        bool master = Book.PlayerName() is { } me && Book.IsMaster(me);
        var rows = new List<object>();
        foreach (int kind in Book.Npcs?.Kinds() ?? Enumerable.Empty<int>())
        {
            string condition = "", cost = "", name = "";
            bool canUpgrade = true;
            int level = Book.NpcLevel(kind);
            if (Book.Npcs.Get(kind, level + 1) is not { } next)
            {
                if (Book.Npcs.Get(kind, level) is { } current)
                {
                    condition = "MAX";
                    cost = "MAX";
                    name = ClientFormat.Format(CommonByKey("s_guild_ui_house_npc_name"), StringTableText(0x3f, current.StringKey), level);
                }
                canUpgrade = false;
            }
            else
            {
                condition = ClientFormat.Format(CommonByKey("s_guild_ui_house_npc_condition_guildlevel"), next.RequireGuildLevel)
                    + "\n" + ClientFormat.Format(CommonByKey("s_guild_ui_house_npc_condition_guildhouselevel"), next.RequireHouseLevel);
                cost = next.UpgradeCost == 0 ? CommonByKey("s_guild_ui_house_npc_upgrade_nocost")
                    : ClientFormat.Format(CommonByKey("s_guild_ui_house_npc_upgrade_cost"), GroupText(next.UpgradeCost));
                name = level == 0
                    ? ClientFormat.Format(CommonByKey("s_guild_ui_house_npc_name_nolevel"), StringTableText(0x3f, next.StringKey))
                    : ClientFormat.Format(CommonByKey("s_guild_ui_house_npc_name"), StringTableText(0x3f, next.StringKey), level);
                if (Book.GuildLevel < next.RequireGuildLevel) canUpgrade = false;
                if (Book.HouseLevel < next.RequireHouseLevel) canUpgrade = false;
                if (!master) canUpgrade = false;
            }
            rows.Add(Host.Obj(("type", kind), ("shopName", name), ("condition", condition), ("canUpgrade", canUpgrade), ("upgradeCost", cost)));
        }
        NpcRowsBuilt?.Invoke(rows);
        Host.Invoke(housePage, "setNpc", rows);
    }

    /// <summary>The Manager page (m_managePage) once made.</summary>
    public object ManagePage => managePage;

    /// <summary>CreateAcceptRequestPage (@0x14196a440): made now, the guild info, the requests and the member count
    /// text.</summary>
    void CreateAcceptRequestPage()
    {
        if (!EnsurePageCreated("guild_accept_request_page", ref acceptRequestPage)) return;
        UpdateGuildInfo();
        OnRequestsForGuild();
        SetChildText(acceptRequestPage, "m_memberCountText", ClientFormat.Format(Common(1000), Book.MemberCount, Book.Capacity));
    }

    /// <summary>CreateSkillPage (@0x14196a590): made now, setType 0, the skill list, the fund texts.</summary>
    void CreateSkillPage()
    {
        if (!EnsurePageCreated("guild_skill_page", ref skillPage)) return;
        Host.Invoke(skillPage, "setType", 0);
        BuildGuildSkills();
        UpdateSkillFund();
    }

    /// <summary>CreateMemberSkillPage (@0x14196a640): made now, setType 1, the personal skill list.</summary>
    void CreateMemberSkillPage()
    {
        if (!EnsurePageCreated("guild_skill_page", ref skillPage2)) return;
        Host.Invoke(skillPage2, "setType", 1);
        BuildPersonalSkills();
    }

    /// <summary>The Event page (m_eventPage) once made.</summary>
    public object EventPage => eventPage;
    /// <summary>The buttons of the vs game part (m_button368 register, m_button378 unregister).</summary>
    public (object Register, object Unregister) EventButtons => (eventRegisterButton, eventUnregisterButton);
    /// <summary>How many open events wait to expire (m_requests).</summary>
    public int EventRequestCount => requests.Count;

    /// <summary>CreateEventPage (@0x14196a6f0, uiguilddialog.cpp:1668): the page made now, its vs game part and buttons found (a missing one is a
    /// GfxExceptionSwfLoad: the page stays as made and nothing more runs), the loader added to the image area, the click handlers, then the event rows
    /// (FUN_141970900) and the vs game part (FUN_141970f00).</summary>
    void CreateEventPage()
    {
        if (!EnsurePageCreated("UIGuildDialog_EventPage", ref eventPage)) return;
        eventVsGame = Host.Child("m_guildEventVsGame", eventPage);
        if (eventVsGame == null) return;
        eventRegisterButton = Host.Child("m_guildEventVsGameRegisterButton", eventVsGame);
        if (eventRegisterButton == null) return;
        eventUnregisterButton = Host.Child("m_guildEventVsGameUnregisterButton", eventVsGame);
        if (eventUnregisterButton == null) return;
        eventImageArea = Host.Child("m_guildEventImageArea", eventVsGame);
        if (eventImageArea == null) return;
        var loaderClass = Host.RT.GetClass("flash.display.Loader");
        if (loaderClass != null) Host.RT.Guard("create the event loader", () => eventLoader = Host.RT.Construct(loaderClass, Array.Empty<object>()));
        if (eventLoader != null) Host.Invoke(eventImageArea, "addChild", eventLoader);
        AddEventClick(eventRegisterButton, OnEventRegisterClick);
        AddEventClick(eventUnregisterButton, OnEventUnregisterClick);
        BuildEvents();
        SetVsGame();
    }

    void AddEventClick(object button, Action handler)
    {
        Func<object[], object> f = _ => { handler(); return null; };
        Host.Invoke(button, "addEventListener", "buttonClick", f, false, 0, false);
    }

    /// <summary>FUN_141970900 (@0x141970900, uiguilddialog.cpp:2383): m_requests cleared, then a row per guild event to buildGuildEvent. A row whose
    /// custom event (FUN_1404d27b0) or field is missing ends the build without the invoke. An open event (IsActive) is state 1 and its row expires
    /// 30 seconds after its time.</summary>
    void BuildEvents()
    {
        if (eventPage == null) return;
        requests.Clear();
        var rows = new List<object>();
        foreach (int id in Events?.Ids() ?? Enumerable.Empty<int>())
        {
            if (Events.Get(id) is not { } ev) continue;
            if (Book.CustomEvent(ev.Id) is not { } custom) return;
            if (FieldCapacity(ev.MapCode) is not { } maxUser) return;
            int state = 0;
            bool open = false;
            if (Book.IsEventActive(custom))
            {
                state = 1;
                open = true;
                long now = Book.ServerTime();
                uint tick = TickCount();
                uint expire = (uint)(int)(tick + (30 - (now - (uint)custom.Time)) * 1000);
                int at = requests.FindLastIndex(r => r.Expire <= expire) + 1;     // multimap::insert: after the equal keys
                requests.Insert(at, (expire, ev.Id));
            }
            rows.Add(Host.Obj(("id", ev.Id), ("eventName", StringTableText(3, ev.MapCode)),
                ("maxUserText", ClientFormat.Format(CommonByKey("s_guild_ui_event_max_user_count"), maxUser)),
                ("eventDesc", StringTableText(0x26, ev.Id)), ("imgpath", ev.ImagePath), ("state", state), ("isOpenTag", open)));
        }
        EventRowsBuilt?.Invoke(rows);
        Host.Invoke(eventPage, "buildGuildEvent", rows);
    }

    /// <summary>The rows handed to the page's buildGuildEvent (the value objects of the call), raised just before it.</summary>
    public event Action<IReadOnlyList<object>> EventRowsBuilt;

    /// <summary>FUN_141970f00 (@0x141970f00, uiguilddialog.cpp:2429): the vs game part's title, user count, description (feature 0x78 on, else the
    /// closed text) and its image; m_eventInfo goes right of the title. STILL-OPEN: GetIconUrl's body (FUN_140d011c0 @0x140d011c0 loads the url it
    /// returns; "img://icon:" and the path is assumed).</summary>
    void SetVsGame()
    {
        if (eventVsGame == null) return;
        SetChildText(eventVsGame, "m_eventName", CommonByKey("s_guild_ui_vsGame_title"));
        SetChildText(eventVsGame, "m_maxUserCount", CommonByKey("s_guild_ui_vsGame_userCount"));
        if (Host.Child("m_eventName", eventVsGame) is { } name && Host.Child("m_eventInfo", eventVsGame) is { } info)
        {
            double x = Convert.ToDouble(Host.Get(name, "x") ?? 0.0), w = Convert.ToDouble(Host.Get(name, "textWidth") ?? 0.0);
            Host.Set(info, "x", (double)(uint)(x + w + 10.0));
        }
        SetChildText(eventVsGame, "m_eventDesc", CommonByKey(FeatureEnabled(VsGameFeature) ? "s_guild_ui_vsGame_description" : "s_guild_ui_vsGame_description_close"));
        if (eventLoader != null)
        {
            // ASSUMED (FUN_140d011c0 body not read): unloadAndStop, then a load of "img://icon:" + path
            Host.Invoke(eventLoader, "unloadAndStop", true);
            var req = Host.RT.GetClass("flash.net.URLRequest");
            if (req != null)
            {
                object r = null;
                Host.RT.Guard("create the event url", () => r = Host.RT.Construct(req, new object[] { "img://icon:" + VsGameImage }));
                if (r != null) Host.Invoke(eventLoader, "load", r);
            }
        }
        UpdateEventButtons();
    }

    /// <summary>The vs game feature (IsFeatureEnabled 0x78).</summary>
    public const int VsGameFeature = 0x78;
    /// <summary>The vs game's image path (FUN_141970f00).</summary>
    public const string VsGameImage = "./Data/Resource/Image/Map/GuildEvent/00000001.png";

    /// <summary>PurgeExpiredRequests (@0x1419760c0, uiguilddialog.cpp:3097): the requests whose tick passed are dropped from the front and the
    /// rows rebuilt.</summary>
    void PurgeExpiredRequests()
    {
        if (eventPage == null || requests.Count == 0) return;
        uint now = TickCount();
        int n = 0;
        while (n < requests.Count && requests[n].Expire < now) n++;
        if (n == 0) return;
        requests.RemoveRange(0, n);
        BuildEvents();
    }

    /// <summary>UpdateEventButtons (@0x1419761f0, uiguilddialog.cpp:3121): both hidden without feature 0x78, else the unregister button when
    /// the guild is registered (FUN_1404d2c60 on CGuildManager::GetInstance(), uiguilddialog.cpp:3130; ASSUMED: it reads f354, its body is not in the tree) and the register button when not. The client calls it too for the modes 0x76/0x77
    /// (FUN_141968140, :1247), here <see cref="GuildBook.VsGameRefresh"/>.</summary>
    public void UpdateEventButtons()
    {
        if (eventRegisterButton == null || eventUnregisterButton == null) return;
        bool on = FeatureEnabled(VsGameFeature), registered = on && Book.VsGameRegistered;
        Host.SetVisible(eventUnregisterButton, registered);
        Host.SetVisible(eventRegisterButton, on && !registered);
    }

    /// <summary>OnEventRegisterClick (@0x141976380): SendGuildMode115 when the player exists.</summary>
    void OnEventRegisterClick()
    {
        if (PlayerName() == null) return;
        Book.SendGuildMode115();
    }

    /// <summary>OnEventUnregisterClick (@0x1419763d0): SendGuildMode116 when the player exists.</summary>
    void OnEventUnregisterClick()
    {
        if (PlayerName() == null) return;
        Book.SendGuildMode116();
    }

    /// <summary>FUN_14196e2c0 (@0x14196e2c0): the fund and the event fund to the info page and the skill page.</summary>
    void UpdateSkillFund()
    {
        if (NoMovie) return;
        int fund = Book.Info?.Fund ?? 0, eventFund = Book.Info?.EventFund ?? 0;
        if (infoPage != null) Host.Invoke(infoPage, "setGuildFund", GroupText(fund));
        if (skillPage == null) return;
        SetChildText(skillPage, "m_guildFund", GroupText(fund));
        SetChildText(skillPage, "m_guildEventFund", GroupText(eventFund));
        Host.Invoke(skillPage, "onChangedFund", fund);
    }

    /// <summary>The guild page (FUN_14196e590, :2213) writes the duration with FormatTimeSpan; the member page (FUN_14196f830, :2324)
    /// with the short time text.</summary>
    public string SkillInfoText(GuildSkillTable.Skill s, bool personal = false)
    {
        string info = "";
        if (s.Duration > 0)
            info += ClientFormat.Format(CommonByKey("s_guild_ui_skill_duration"), personal ? ShortTimeText((uint)s.Duration) : TimeSpanText(s.Duration));
        if (s.Cooltime > 0)
        {
            if (info.Length > 0) info += ", ";
            info += ClientFormat.Format(CommonByKey("s_guild_ui_skill_cooldown"), ShortTimeText((uint)s.Cooltime));
        }
        return info;
    }

    /// <summary>FUN_141b708a0 (@0x141b708a0, util.cpp:999) without the seconds: s_common ids 0x97b-0x97f.</summary>
    string ShortTimeText(uint seconds)
    {
        if (seconds < 60) return ClientFormat.Format(Common(0x97b), seconds);
        if (seconds < 3600)
        {
            uint m = seconds / 60, sec = seconds % 60;
            return sec == 0 ? ClientFormat.Format(Common(0x97c), m) : ClientFormat.Format(Common(0x97e), m, sec);
        }
        uint h = seconds / 3600, mm = seconds / 60 % 60;
        return mm == 0 ? ClientFormat.Format(Common(0x97d), h) : ClientFormat.Format(Common(0x97f), h, mm);
    }

    /// <summary>One skill's state, its text and the rate of the duration spent (FUN_14196e590, FUN_14196f830).</summary>
    (int State, string Text) SkillState(GuildSkillTable.Skill s, int remainDuration, int remainCool)
    {
        int state = 0;
        string text = "";
        if (remainDuration == 0)
        {
            if (remainCool != 0) { state = 2; text = CommonByKey("s_guild_ui_skill_waiting"); }
        }
        else { state = 3; text = CommonByKey("s_guild_ui_skill_using"); }
        if (Book.GuildLevel < s.RequireLevel)
        {
            state = 1;
            text = ClientFormat.Format(CommonByKey("s_guild_ui_skill_require_level"), s.RequireLevel);
        }
        return (state, text);
    }

    static float SkillRate(int duration, int remainDuration) => duration != 0 ? (float)(duration - remainDuration) / duration : 1f;

    /// <summary>The attendance gate of buildGuildSkill: the member's fc8 plus a day has passed.</summary>
    bool CanUseSkills() => Book.Me is { } me && Book.ServerTime() >= me.Fc8 + 86400;

    /// <summary>FUN_14196e590 (@0x14196e590): the guild skills of the skill page: the fund texts, one row per skill with a record, its
    /// row and its effect, then buildGuildSkill(bCanUse, rows), setSimpleActiveIcon and UpdateSkillInfo. STILL-OPEN: the game
    /// event's sale rate is 0 (GuildBook.GuildSkillSaleRate).</summary>
    void BuildGuildSkills()
    {
        if (NoMovie || skillPage == null) return;
        activeSkills.Clear();
        lastSkillInfoSecond = long.MinValue;
        int fund = Book.Info?.Fund ?? 0, eventFund = Book.Info?.EventFund ?? 0;
        SetChildText(skillPage, "m_guildFund", GroupText(fund));
        SetChildText(skillPage, "m_guildEventFund", GroupText(eventFund));
        bool master = Book.PlayerName() is { } me && Book.IsMaster(me);
        var rows = new List<object>();
        var active = new List<object>();
        foreach (int id in Book.SkillTable?.GuildSkillIds() ?? Enumerable.Empty<int>())
        {
            if (Book.Info?.Skills.FirstOrDefault(r => r.Id == id) is not { } rec) continue;
            if (Book.SkillTable.Get(rec.Id, rec.Level) is not { } s) continue;
            if (EffectIcon((uint)s.AdditionalEffectId, (ushort)s.AdditionalEffectLevel) is not { } icon) continue;
            int require = Book.SkillCostWithEventDiscount(s.Id, s.Level);
            bool bEvent = s.IsEvent;
            int remainDuration = Book.RemainingDuration(rec), remainCool = Book.RemainingCooldown(rec);
            var (state, stateText) = SkillState(s, remainDuration, remainCool);
            if (state == 3) active.Add(Host.Obj(("imgpath", icon)));
            rows.Add(Host.Obj(
                ("id", s.Id), ("type", bEvent ? 2 : 0),
                ("skillName", EffectText((uint)s.AdditionalEffectId, (ushort)s.AdditionalEffectLevel, "name")),
                ("requireFund", GroupText(require)),
                ("isEnough", (bEvent ? eventFund : fund) >= require),
                ("isMaster", master),
                ("saleRate", bEvent ? 0 : Book.GuildSkillSaleRate() / 100),
                ("skillInfo", SkillInfoText(s)),
                ("skillDesc", EffectText((uint)s.AdditionalEffectId, (ushort)s.AdditionalEffectLevel, "tooltipDescription")),
                ("state", state), ("stateString", stateText), ("imgpath", icon),
                ("remainCoolSec", remainCool), ("remainDurationSec", remainDuration),
                ("rate", SkillRate(s.Duration, remainDuration))));
            if (remainCool > 0 || remainDuration > 0) activeSkills.Add(rec.Id);
        }
        if (Book.Me == null) return;
        Host.Invoke(skillPage, "buildGuildSkill", CanUseSkills(), rows);
        Host.Invoke(skillPage, "setSimpleActiveIcon", active);
        UpdateSkillInfo();
    }

    /// <summary>UpdateSkillInfo (@0x1419758a0): the remaining times of the timed skills to updateSkillInfo; one that ran out rebuilds the page.
    /// The client calls it from vfunc_72 each frame. The host calls <see cref="Tick"/>.</summary>
    public void UpdateSkillInfo()
    {
        if (NoMovie || activeSkills.Count == 0 || skillPage == null) return;
        // every input is a whole second (server time, duration, time), so the rows change once a second
        long now = Book.ServerTime();
        if (now == lastSkillInfoSecond) return;
        lastSkillInfoSecond = now;
        var rows = new List<object>();
        foreach (int id in activeSkills)
        {
            if (Book.SkillTable?.Get(id, Book.SkillLevel(id)) is not { } s) continue;
            if (Book.Info?.Skills.FirstOrDefault(r => r.Id == id) is not { } rec) continue;
            int remainDuration = Book.RemainingDuration(rec), remainCool = Book.RemainingCooldown(rec);
            rows.Add(Host.Obj(("id", id), ("remainCoolSec", remainCool), ("remainDurationSec", remainDuration),
                ("rate", SkillRate(s.Duration, remainDuration))));
            if (remainCool == 0 && remainDuration == 0) { BuildGuildSkills(); return; }
        }
        Host.Invoke(skillPage, "updateSkillInfo", rows);
    }

    /// <summary>The per-frame part of vfunc_72 (@0x1419614e0) that is ported: UpdateSkillInfo.</summary>
    public void Tick()
    {
        UpdateSkillInfo();
        PurgeExpiredRequests();
    }

    /// <summary>FUN_14196f830 (@0x14196f830): the personal skills of the second skill page, over the player's mesos.
    /// STILL-OPEN: the remaining time of the player's own buff (AdditionalCli::GetRemainTicks) is not ported, so a personal skill is
    /// never shown as in use; the game event's sale rate is 0; the ids are GetPersonalSkillIds (@0x1404d7730).</summary>
    void BuildPersonalSkills()
    {
        if (NoMovie || skillPage2 == null) return;
        long meso = Book.Mesos();
        SetChildText(skillPage2, "m_guildFund", GroupText(meso));
        if (Book.Level() == null) return;           // GetLocalPlayer() == 0
        bool master = Book.PlayerName() is { } me && Book.IsMaster(me);
        var rows = new List<object>();
        foreach (int id in Book.SkillTable?.PersonalSkillIds() ?? Enumerable.Empty<int>())
        {
            int level = Book.SkillLevel(id);
            if (Book.SkillTable.Get(id, level) is not { IsPersonal: true } s) continue;
            if (EffectIcon((uint)s.AdditionalEffectId, (ushort)s.AdditionalEffectLevel) is not { } icon) continue;
            long require = Book.PersonalSkillDiscountedCost(id, level);
            var (state, stateText) = SkillState(s, 0, 0);
            rows.Add(Host.Obj(
                ("id", id), ("type", 1),
                ("skillName", EffectText((uint)s.AdditionalEffectId, (ushort)s.AdditionalEffectLevel, "name")),
                ("requireFund", GroupText(require)),
                ("isEnough", meso >= require),
                ("isMaster", master),
                ("saleRate", Book.GuildPersonalSkillSaleRate() / 100),
                ("skillInfo", SkillInfoText(s, true)),
                ("skillDesc", EffectText((uint)s.AdditionalEffectId, (ushort)s.AdditionalEffectLevel, "tooltipDescription")),
                ("state", state), ("stateString", stateText), ("imgpath", icon),
                ("remainCoolSec", 0), ("remainDurationSec", 0),
                ("rate", SkillRate(s.Duration, 0))));
        }
        if (Book.Me == null) return;
        Host.Invoke(skillPage2, "buildGuildSkill", CanUseSkills(), rows);
        Host.Invoke(skillPage2, "setSimpleActiveIcon", new List<object>());
    }

    /// <summary>FUN_14196da70 (@0x14196da70): the requests' count to the root, then (with the accept page up) its rows,
    /// a page of 7.</summary>
    void OnRequestsForGuild()
    {
        if (NoMovie) return;
        var all = Book.RequestsForGuild;
        Host.Invoke(Host.Entity, "setAcceptMemberCount", all.Count);
        if (acceptRequestPage == null) return;
        int start = (acceptPage - 1) * 7;
        if (all.Count <= start)
        {
            start = 0;
            acceptPage = 1;
        }
        int end = Math.Min(start + 7, all.Count);
        int pages = all.Count / 7 + (all.Count % 7 != 0 ? 1 : 0);
        var rows = new List<object>();
        for (int i = start; i < end; i++)
        {
            var r = all[i];
            if (r == null) continue;
            rows.Add(Host.Obj(
                ("charSn", MailTexts.IdText(r.CharId)),
                ("charname", r.Name),
                ("memberInfo", ClientFormat.Format(Common(0x402), r.Level, JobName(r.Job))),
                ("trophy", r.TrophyTotal),
                ("requestSn", MailTexts.IdText(r.Id)),
                ("imgpath", ImagePath(r.CharId, r.Picture))));
        }
        // the auto-accept check box is the guild's "listed in the search" flag (FUN_1404d1860): STILL-OPEN, so false
        Host.Invoke(acceptRequestPage, "buildRequestMemberPage", acceptPage, pages, false, rows);
    }

    void OnGuildInfoRefresh()
    {
        if (IsClosed || NoMovie || !Book.InGuild) return;
        UpdateGuildInfo();
    }

    /// <summary>FUN_141973b20 (@0x141973b20): one member's row to the root and the member page, then the master's info and
    /// the player's rights when the row's member is the master or the player. FUN_14196e590 (the skill page's fund) is
    /// STILL-OPEN with the skill page.</summary>
    void OnMemberRefresh(string name)
    {
        if (IsClosed || NoMovie || Book.FindMemberByName(name) is not { } m) return;
        var row = MemberRow(m);
        Host.Invoke(Host.Entity, "addOrUpdateMember", row);
        if (memberPage != null) Host.Invoke(memberPage, "addOrUpdateMember", row);
        if (managePage != null) Host.Invoke(managePage, "addOrUpdateMember", row);
        if (Book.IsMaster(name)) SetMasterInfo();
        if (PlayerName() == name) SetPlayer();
    }

    /// <summary>OnPacketGuildExp (guildmanager.cpp:2723) with the dialog open: UpdateGuildExp, and when the guild level changed
    /// FUN_14196e590 (the skill page) and FUN_141971ac0 (the npc info).</summary>
    void OnExpRefresh(bool levelChanged)
    {
        if (IsClosed || NoMovie) return;
        UpdateGuildExp();
        if (!levelChanged) return;
        BuildGuildSkills();
        SetNpcInfo();
    }

    /// <summary>FUN_1413c4b60 / FUN_1413c4bd0 (:2739, :2748) with the dialog open: FUN_14196e2c0.</summary>
    void OnFundRefresh()
    {
        if (IsClosed || NoMovie) return;
        UpdateSkillFund();
    }

    /// <summary>RemoveMember (@0x141973df0).</summary>
    void OnMemberRemoved(string name)
    {
        if (IsClosed || NoMovie) return;
        Host.Invoke(Host.Entity, "removeMember", name);
        if (memberPage != null) Host.Invoke(memberPage, "removeMember", name);
        if (managePage != null) Host.Invoke(managePage, "removeMember", name);
    }

    void SetChildText(object page, string name, string text)
    {
        if (Host.Child(name, page) is { } t) Host.Set(t, "text", text);
    }

    /// <summary>UpdateGuildInfo (@0x14196b620).</summary>
    void UpdateGuildInfo()
    {
        string count = ClientFormat.Format(Common(1000), Book.MemberCount, Book.Capacity);
        if (infoPage == null) return;
        SetChildText(infoPage, "m_guildNameText", Book.GuildName);
        SetChildText(infoPage, "m_createdDayText", AchieveTexts.FormatDate(Book.Info?.CreateTime ?? 0));
        SetChildText(infoPage, "m_guildMemberCountText", count);
        SetChildText(infoPage, "m_guildNotifyText", Book.Info?.Notice ?? "");
        void Total(string clip, string icon, long value)
        {
            if (Host.Child(clip, infoPage) is not { } c) return;
            if (Host.Child("m_totalIcon", c) is { } i) Host.Invoke(i, "gotoAndStop", icon);   // vfunc_15
            SetChildText(c, "m_totalText", ClientFormat.Format("{0}", GroupText(value)));
        }
        Total("m_totalTrophy", "trophy", Book.TotalTrophy);
        Total("m_totalItemLevel", "itemLevel", Book.TotalItemLevel);
        Total("m_totalCharLevel", "charLevel", Book.TotalCharLevel);
        // SetGuildExpEvent(IsGuildExpEventRunning()): STILL-OPEN, never running
        Host.Invoke(infoPage, "setGuildExpEvent", false);
    }

    /// <summary>UpdateGuildExp (@0x14196e0a0, uiguilddialog.cpp:2046): setGuildExp(level, max, rate) on the Info page.</summary>
    void UpdateGuildExp()
    {
        if (infoPage == null) return;
        int exp = Book.Info?.Exp ?? 0;
        int level = LevelByExp(exp);
        if (level <= 0) return;
        int prev = AccumExp(level - 1), next = AccumExp(level);
        float rate = (float)(exp - prev) / (float)(next - prev);
        if (rate < 0f) rate = 0f;
        if (rate > 1f) rate = 1f;
        bool max = false;
        if (MaxLevel() == level) { max = true; rate = 1f; }
        Host.Invoke(infoPage, "setGuildExp", level, max, rate);
    }

    /// <summary>FUN_14196e2c0: the fund text on the Info page.</summary>
    void SetGuildFund()
    {
        if (NoMovie) return;
        if (infoPage != null) Host.Invoke(infoPage, "setGuildFund", GroupText(Book.Info?.Fund ?? 0));
    }

    /// <summary>FUN_14196b3a0: the player's name on the root and the player's rights on the Info page.</summary>
    void SetPlayer()
    {
        if (NoMovie || PlayerName() is not { } name) return;
        Host.Invoke(Host.Entity, "setPlayer", name);
        if (infoPage != null)
            Host.Invoke(infoPage, "setPlayer", Book.IsMaster(name), Book.HasMemberPermission(name, 8), Book.HasMemberPermission(name, 0x40));
    }

    /// <summary>SetMasterInfo (@0x14196c860): the master's name on the root, and on the Info page the name, the rank text and the mark.</summary>
    void SetMasterInfo()
    {
        if (NoMovie || Book.MasterMember is not { } master) return;
        string grade = "";
        if (Book.GetGrade(master.Grade) is { } g)
            grade = ClientFormat.Format(Common(0x3d9), (ushort)master.Level, JobName(master.JobCode), g.Name);
        string mark = ImagePath(master.CharId, master.Code);
        Host.Invoke(Host.Entity, "setMasterInfo", master.Name);
        if (infoPage != null) Host.Invoke(infoPage, "setMasterInfo", master.Name, grade, mark);
    }

    /// <summary>SetGradeInfo (@0x14196cd30): the grades 0..5 to the root and the Manager page.</summary>
    void SetGradeInfo()
    {
        if (NoMovie) return;
        var rows = new List<object>();
        for (int i = 0; i < 6; i++)
        {
            if (Book.GetGrade(i) is not { } g) continue;
            rows.Add(Host.Obj(("gradeRank", i), ("gradeName", g.Name), ("gradeAuth", g.Permissions)));
        }
        Host.Invoke(Host.Entity, "setGradeInfo", rows);
        if (managePage != null) Host.Invoke(managePage, "setGradeInfo", rows);
    }

    /// <summary>FUN_141975620: the member rows to the root and the Member page.</summary>
    void BuildMembers()
    {
        if (NoMovie) return;
        var rows = new List<object>();
        foreach (var m in Book.Members) rows.Add(MemberRow(m));
        Host.Invoke(Host.Entity, "buildGuildMembers", rows);
        if (memberPage != null) Host.Invoke(memberPage, "buildGuildMembers", rows);
        if (managePage != null) Host.Invoke(managePage, "buildGuildMembers", rows);
    }

    /// <summary>FUN_1419771b0 (:3462): one member's row.</summary>
    object MemberRow(GuildMember m)
    {
        string location;
        if (m.Offline != 0)
        {
            string time;
            if (m.LastSeen == 0) time = Common(0x3d6);
            else
            {
                var span = TimeSpan.FromSeconds(ServerTime() - m.LastSeen);
                if (span.Days > 0) time = ClientFormat.Format(Common(0x3d2), (long)span.Days);
                else if (span.Hours > 0) time = ClientFormat.Format(Common(0x3d3), span.Hours);
                else if (span.Minutes > 0) time = ClientFormat.Format(Common(0x3d4), span.Minutes);
                else time = Common(0x3d5);
            }
            location = ClientFormat.Format(Common(0x3d7), time);
        }
        else if (m.Channel == 0) location = MapName(m.MapId);
        else location = ClientFormat.Format(Common(0x3d8), m.Channel, MapName(m.MapId));
        string grade = Book.GetGrade(m.Grade)?.Name ?? "";
        string info = ClientFormat.Format(Common(0x3d9), (ushort)m.Level, JobName(m.JobCode), grade);
        bool master = Book.IsMaster(m.Name);
        bool me = PlayerCharId() is { } id && id == m.CharId;
        return Host.Obj(
            ("charname", m.Name), ("location", location), ("memberInfo", info), ("trophy", m.TrophyTotal), ("level", (int)(ushort)m.Level),
            ("jobCode", m.JobCode), ("contribution", m.Contribution), ("totalContribution", (uint)m.TotalContribution),
            ("itemLevel", (uint)m.ItemLevel), ("memo", m.Memo), ("imgpath", ImagePath(m.CharId, m.Code)),
            ("master", master), ("isMe", me), ("grade", m.Grade), ("raid", m.RaidTotal), ("offline", m.Offline));
    }

    /// <summary>SetPageVisible (@0x141969870): vfunc_4.</summary>
    void SetPageVisible(object page, bool visible)
    {
        if (page == null) return;
        Host.SetVisible(page, visible);
    }

    /// <summary>EnsurePageCreated (@0x141969d10): false when made already; else ASUIComponent(linkage, 2) added to the movie
    /// root (m_90) at (0xa8, 0x50).</summary>
    bool EnsurePageCreated(string linkage, ref object page)
    {
        if (page != null) return false;
        var cls = Host.RT.GetClass(linkage);
        if (cls == null) return false;
        object v = null;
        Host.RT.Guard("create " + linkage, () => v = Host.RT.Construct(cls, Array.Empty<object>()));
        if (v == null) return false;
        page = v;
        Host.Invoke(Host.Entity, "addChild", v);
        Host.Set(v, "x", 0xa8);                             // SetPositionInt(0xa8, 0x50)
        Host.Set(v, "y", 0x50);
        return true;
    }

    /// <summary>CreateJoinPage (@0x141969ee0): made now, SendGuildMode86 and FUN_14196d660.</summary>
    void CreateJoinPage()
    {
        if (EnsurePageCreated("guild_join_page", ref joinPage))
        {
            Book.SendMyRequests();
            BuildSearchMy();
        }
    }

    void OnSearchRefresh(int page)
    {
        if (!IsClosed) BuildSearchGuild(page);
    }

    /// <summary>FUN_14196d0c0 (:1926): the search results' page (9 rows) on the Join page; a page past the end is page 1.
    /// <paramref name="page"/> 0 keeps the page.</summary>
    public void BuildSearchGuild(int page)
    {
        if (joinPage == null || NoMovie) return;
        if (page != 0) searchPage = page;
        var results = Book.SearchResults;
        int start = (searchPage - 1) * SearchPageSize;
        if ((uint)results.Count <= (uint)start)            // size_t compare
        {
            start = 0;
            searchPage = 1;
        }
        int end = Math.Min(start + SearchPageSize, results.Count);
        int pages = results.Count / SearchPageSize + (results.Count % SearchPageSize != 0 ? 1 : 0);
        var rows = new List<object>();
        for (int i = start; i < end; i++)
        {
            var g = results[i];
            if (g == null) continue;
            var row = new List<(string, object)>
            {
                ("guildName", g.Name),
                ("guildSn", MailTexts.IdText(g.GuildId)),
                ("guildLevel", LevelByExp(g.Exp)),
                ("masterName", g.MasterName),
                ("trophy", g.TrophyTotal),
                ("capacityText", ClientFormat.Format("{0} / {1}", g.MemberCount, g.Capacity)),
            };
            if (Book.FindRequestByGuild(g.GuildId) is { } request)  // FUN_1413c8000
            {
                row.Add(("requestSn", MailTexts.IdText(request.Id)));
                row.Add(("state", 1));
            }
            else row.Add(("state", 0));
            rows.Add(Host.Obj(row.ToArray()));
        }
        Host.Invoke(joinPage, "buildSearchGuild", searchPage, pages, rows);   // FUN_141979e60
    }

    /// <summary>FUN_14196d660 (:1976): this character's join requests on the Join page.</summary>
    public void BuildSearchMy()
    {
        if (joinPage == null || NoMovie || IsClosed) return;
        var rows = new List<object>();
        foreach (var r in Book.MyRequests)
        {
            if (r == null) continue;
            rows.Add(Host.Obj(
                ("guildName", r.GuildName),
                ("guildSn", MailTexts.IdText(r.GuildId)),
                ("guildLevel", LevelByExp(r.Exp)),
                ("requestSn", MailTexts.IdText(r.Id)),
                ("masterName", r.MasterName),
                ("state", 1),
                ("trophy", r.TrophyTotal),
                ("capacityText", ClientFormat.Format("{0} / {1}", r.MemberCount, r.Capacity))));
        }
        Host.Invoke(joinPage, "buildSearchMy", rows);                        // InvokeWithArray
    }

    /// <summary>The master's name check of onChangeMaster, onChangeMemberGrade and onExpelMember: the master's own name
    /// (CStringW ==, case-sensitive) is the notice BlockFormat(0x3e1).</summary>
    bool IsMasterName(string name)
    {
        if (Book.MasterMember is not { } master || name != master.Name) return false;
        Book.ShowNotice(Common(0x3e1));
        return true;
    }

    /// <summary>vfunc_28 (@0x1419634e0, :652) and FUN_1419681b0 (:1261): the movie's calls.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (base.OnCallback(name, args)) return true;
        string Str(int i) => i < args.Length ? Rt.ToString(args[i]) ?? "" : "";
        int Int(int i) => i < args.Length ? (int)Rt.ToNumber(args[i]) : 0;
        float Float(int i) => i < args.Length ? (float)Rt.ToNumber(args[i]) : 0;
        switch (name.ToLowerInvariant())
        {
            case "oncategorychanged":
                if (args.Length > 0) OpenCategoryPage(Str(0));
                return true;
            case "onclickcreateguild":
            {
                // the block check (FindBlockedAction(7) -> ShowBlockNotice, return 0) is STILL-OPEN
                var box = MessageBoxQueue.Create(5, ClientFormat.Format(Common(0x3db), CreatePrice, NameLengthMin, NameLengthMax),
                    this, "createGuild");
                box.SetInput(0, "", NameLengthMax, 0, 0);
                ShowBox(box);
                return true;
            }
            case "onsearchguild":
                if (!NoMovie) Host.Invoke(Host.Entity, "showPropensity", Book.SearchPropensity, 1, true);
                return true;
            case "onsearchguildbyname":
            {
                var box = MessageBoxQueue.Create(5, CommonByKey("s_guild_ui_search_search_name"), this, "serachGuildName");
                box.SetButtonPreset(2);
                ShowBox(box);
                return false;                   // the client falls through to its return 0
            }
            case "onsearchprepage":
                searchPage--;
                if (searchPage < 1) searchPage = 1;
                BuildSearchGuild(0);
                return true;
            case "onsearchnextpage":
                searchPage++;
                BuildSearchGuild(0);
                return true;
            case "onrequestjoin":
                if (args.Length > 0) Book.Join(unchecked((long)ReservedWords.ParseUInt64(Str(0))));          // FUN_140718350
                return true;
            case "onrequestcanceljoin":
                if (args.Length > 0) Book.CancelJoin(unchecked((long)ReservedWords.ParseUInt64(Str(0))));
                return true;
            case "onacceptmemeber":                   // the movie's spelling
                if (args.Length > 0) Book.Respond(unchecked((long)ReservedWords.ParseUInt64(Str(0))), true);
                return true;
            case "onrejectmember":
                if (args.Length > 0) Book.Respond(unchecked((long)ReservedWords.ParseUInt64(Str(0))), false);
                return true;
            case "onacceptmemberprevpage":
                acceptPage--;
                if (acceptPage < 1) acceptPage = 1;
                OnRequestsForGuild();
                return true;
            case "onacceptmembernextpage":
                acceptPage++;
                OnRequestsForGuild();
                return true;
            case "onguildskilluse":
                if (args.Length > 0) GuildSkillUseBox(Int(0));
                return true;
            case "onguildmemberskilluse":
                if (args.Length > 0) PersonalSkillUseBox(Int(0));
                return true;
            case "onguildskillupgrade":
            case "onguildmemberskillupgrade":
                if (args.Length > 0) SkillUpgradeBox(Int(0));
                return false;                   // the client's chain ends without a return 1
            case "onguildmemberpopupmenu":
                // :811: x, y, the name; FindMemberByName, then PopUpMenuFromGuildMember on the 0x4e39 menu
                if (args.Length > 2 && Book.FindMemberByName(Str(2)) is { } clicked) MemberPopupRequested?.Invoke(Float(0), Float(1), clicked);
                return true;
            case "onsearchguildmasterpopupmenu":
                if (args.Length > 0) PopupRequested?.Invoke(Float(0), Float(1), Str(2));
                return true;
            case "onopendungeondialog":
                OpenDungeonRoomDialog?.Invoke(DungeonRoomGroupType);
                return true;
            case "onbreakguild":
                ShowBox(MessageBoxQueue.Create(1, Common(0x3df), this, "breakGuild"));   // ShowMessageBox(BlockFormat(0x3df), 1, ...)
                return true;
            case "onleaveguild":
                ShowBox(MessageBoxQueue.Create(1, Common(0x3e0), this, "leaveGuild"));  // ShowMessageBox(BlockFormat(0x3e0), 1, ...)
                return true;
            case "onsetpropensity":
                if (args.Length > 0 && Int(0) == 1)
                {
                    Book.SearchPropensity = Int(1);
                    Book.Search(false);             // SendGuildMode87(false)
                }
                else if (args.Length > 0 && Int(0) == 2)
                    Book.SetPropensity(Book.PropensityFocus, Int(1));       // SendGuildMode68(FUN_1404d1860(), value)
                return true;
            case "onchageregistsearch":                 // the movie's spelling (:1423)
                if (args.Length > 0) Book.SetPropensity(Rt.ToBoolean(args[0]) ? 1 : 0, Book.Propensity);
                return true;
            case "onclickdonate":
            {
                // :680: the movie root (m_30) gets showDonatePopup(count, max, s_guild_ui_donate_popup of the meso text,
                // count and max); at the property's donationMax the box s_guild_err_donateMax instead. No member or no
                // property is the client's return 0.
                if (NoMovie) return true;
                if (PlayerName() is not { } me || Book.FindMemberByName(me) is not { } member) return false;
                if (Book.Property(Book.GuildLevel) is not { } property) return false;
                int count = Book.DonationToday(member), max = property.DonationMax;
                if (count >= max) { ShowBox(MessageBoxQueue.Create(0, CommonByKey("s_guild_err_donateMax"), null, "")); return true; }
                string text = ReplaceValueWords(ClientFormat.Format(CommonByKey("s_guild_ui_donate_popup"), GroupText(DonateMeso), count, max));
                Host.Invoke(Host.Entity, "showDonatePopup", count, max, text);
                return true;
            }
            case "ondonate":
                if (args.Length > 0) Book.Donate(Int(0));      // :706: FUN_1413b8540(PopInt)
                return true;
            case "onclickattend":
                Book.Attend();                                  // :714: SendGuildMode15
                return true;
            case "onclickinvitemember":
            {
                // :666: CreateMessageBox(5, BlockFormat(0x3da), "addMember"), SetInput(2, "", 0, 0, 0)
                var box = MessageBoxQueue.Create(5, Common(0x3da), this, "addMember");
                box.SetInput(2, "", 0, 0, 0);
                ShowBox(box);
                return true;
            }
            case "ongiftguilditem":
            {
                // :1210: the receiver, the item id, the rank and the count; the gift sent (all checks passed) and the page made: onGiftPopDown
                if (args.Length == 0) return true;
                var item = new GuildStorageItem(Int(1), (ushort)Int(2), Int(3), false, false, false, false);
                if (Book.SendStorageGift(item, Str(0)) && storagePage != null && !NoMovie) Host.Invoke(storagePage, "onGiftPopDown");
                return true;
            }
            case "requeststoragehistory":
                Book.SendGuildMode112();                // :1223
                return true;
            case "onchangenotify":
            {
                // :766: CreateMessageBox(0xb, BlockFormat(0x3dc), "changeNotify"), SetInput(0, GetNotice(), 300, 1, 0x14)
                var box = MessageBoxQueue.Create(0xb, Common(0x3dc), this, "changeNotify");
                box.SetInput(0, Book.GuildNotice, 300, 1, 0x14);
                ShowBox(box);
                return true;
            }
            case "onchangepropensity":
            {
                // :776: showPropensity(GetPropensity(), 2, IsMaster(the local player)); nothing without a local player
                if (PlayerName() is not { } me || NoMovie) return true;
                Host.Invoke(Host.Entity, "showPropensity", Book.Propensity, 2, Book.IsMaster(me));
                return true;
            }
            case "onclickmemobutton":
            {
                // :829: only the local player's own row (CStringW !=, case-sensitive) opens the memo box; the client then
                // falls through to its return 0
                if (args.Length == 0) return false;
                if (PlayerName() is not { } me || me != Str(0)) return true;
                var box = MessageBoxQueue.Create(5, CommonByKey("s_guild_ui_memo"), this, "changeMemo");
                box.SetInput(0, "", 0x14, 0, 0);
                ShowBox(box);
                return false;
            }
            case "onchangemaster":
            {
                // :848: not the master; a member of the name; m_strName, then the box 0x14 FormatStringId(0x3e2, name)
                if (args.Length == 0) return true;
                string target = Str(0);
                if (IsMasterName(target) || Book.FindMemberByName(target) == null) return true;
                masterTarget = target;
                ShowBox(MessageBoxQueue.Create(0x14, ClientFormat.Format(Common(0x3e2), masterTarget), this, "changeMaster"));
                return true;
            }
            case "onchangemembergrade":
                // :874: not the master, then SendGuildMode10(name, grade)
                if (args.Length > 0 && !IsMasterName(Str(0))) Book.SetMemberGrade(Str(0), Int(1));
                return true;
            case "onexpelmember":
            {
                // :890: as onChangeMaster with m_strDesc, 0x3e3 and the tag expelMember
                if (args.Length == 0) return true;
                string target = Str(0);
                if (IsMasterName(target) || Book.FindMemberByName(target) == null) return true;
                expelTarget = target;
                ShowBox(MessageBoxQueue.Create(0x14, ClientFormat.Format(Common(0x3e3), expelTarget), this, "expelMember"));
                return true;
            }
            case "onclickopenmayview": return OpenRanks(1, RankUrl);
            case "onopenguildrank": return OpenRanks(1, RealtimeRankUrl);
            case "onviewpvprank": return OpenRanks(5, PvpRankUrl);
            case "onchangegrade":                       // (:931) the client falls through to return 0
            {
                if (args.Length == 0 || args[0] is not AsArray grades || NoMovie) return false;
                var errors = new System.Text.StringBuilder();
                for (int i = 0; i < grades.Items.Count; i++)
                {
                    if (grades.Items[i] is not AsObject row) continue;               // FUN_1409e7dc0: an object or null
                    string gradeName = Rt.ToString(Host.Get(row, "gradeName")) ?? "";
                    int auth = (int)Rt.ToNumber(Host.Get(row, "gradeAuth"));          // Value::GetInt
                    if (Book.GetGrade(i) is not { } old) continue;
                    if (old.Name == gradeName && old.Permissions == auth) continue;
                    if (Book.CheckGradeName?.Invoke(gradeName) is { } ban)
                    {
                        if (ban.Length != 0)
                        {
                            if (errors.Length != 0) errors.Append('\n');
                            errors.Append(ClientFormat.Format(Common(0x423), old.Name, ban));
                        }
                        continue;
                    }
                    Book.SetGrade(i, new GuildGrade(old.Grade, gradeName, auth));
                }
                if (errors.Length != 0) ShowBox(MessageBoxQueue.Create(0, ClientFormat.Format(Common(0x422), errors.ToString()), null, ""));
                return false;
            }
            case "onchagegradeerror":                   // the movie's spelling (:916); the client falls through to return 0
            {
                if (args.Length == 0) return false;
                int error = Int(0);
                string text = error == 1 ? Common(999) : error == 2 ? Common(0x3e1) : "";
                if (text.Length > 0) Book.ShowNotice(text);
                return false;
            }
        }
        return false;
    }

    string SkillName(Ms2.Formats.World.GuildSkillTable.Skill s) => EffectText((uint)s.AdditionalEffectId, (ushort)s.AdditionalEffectLevel, "name");

    /// <summary>OnGuildSkillUse (uiguilddialog.cpp:1084): a type 0x1f box of s_guild_ui_skill_use_skill (the _event text for an event
    /// skill) with the cost after the sale and the skill's name, the yes / no preset, the terms check box and the skill id as
    /// its argument; yes (FUN_141974ba0 @0x141974ba0) is <see cref="GuildBook.UseSkill"/>. FUN_14151f200
    /// (<see cref="NextBox"/>) closes the open box first.</summary>
    void GuildSkillUseBox(int id)
    {
        int level = Book.SkillLevel(id);
        if (Book.SkillTable?.Get(id, level) is not { } s) return;
        string cost = GroupText(Book.SkillCostWithEventDiscount(s.Id, s.Level));
        string msg = ClientFormat.Format(CommonByKey(s.IsEvent ? "s_guild_ui_skill_use_skill_event" : "s_guild_ui_skill_use_skill"), cost, SkillName(s));
        var box = new MessageBoxData(0x1f) { Msg = ReplaceValueWords(msg) };
        box.SetButtonPreset(2);
        box.Append(id);
        box.SetCheckbox0(CommonByKey("s_guild_ui_skill_use_skill_term_agree"), false);
        box.SetAgreement(false);
        box.Callback = (n, b) => { if (n == 0) Book.UseSkill(b.ArgInt(0, 0)); return true; };
        NextBox();
        ShowBox(box);
    }

    /// <summary>OnGuildMemberSkillUse (:1115): a yes / no box (type 1) of s_guild_ui_skill_use_personal_skill with the discounted
    /// cost and the name, for a personal skill only; yes (FUN_141974bf0) is <see cref="GuildBook.UsePersonalSkill"/>.</summary>
    void PersonalSkillUseBox(int id)
    {
        int level = Book.SkillLevel(id);
        if (Book.SkillTable?.Get(id, level) is not { IsPersonal: true } s) return;
        string msg = ClientFormat.Format(CommonByKey("s_guild_ui_skill_use_personal_skill"),
            GroupText(Book.PersonalSkillDiscountedCost(id, level)), SkillName(s));
        var box = MessageBoxQueue.Create(1, ReplaceValueWords(msg), null, "");
        box.Append(id);
        box.Callback = (n, b) => { if (n == 0) Book.UsePersonalSkill(b.ArgInt(0, 0)); return true; };
        ShowBox(box);
    }

    /// <summary>OnGuildSkillUpgrade / OnGuildMemberSkillUpgrade (:1141): a yes / no box (type 1) of
    /// s_guild_ui_event_skill_upgrade_popup (s_guild_ui_skill_upgrade_popup) with the next level's upgrade cost, the name and the
    /// next level; both rows must exist. Yes (OnUpgradeSkillConfirm @0x141974c40) is <see cref="GuildBook.UpgradeSkill"/>.</summary>
    void SkillUpgradeBox(int id)
    {
        int level = Book.SkillLevel(id);
        if (Book.SkillTable?.Get(id, level) is not { } cur || Book.SkillTable.Get(id, level + 1) is not { } next) return;
        string msg = ClientFormat.Format(CommonByKey(next.IsEvent ? "s_guild_ui_event_skill_upgrade_popup" : "s_guild_ui_skill_upgrade_popup"),
            GroupText(next.UpgradeCost), SkillName(cur), level + 1);
        var box = MessageBoxQueue.Create(1, msg, null, "");
        box.Append(id);
        box.Callback = (n, b) => { if (n == 0) Book.UpgradeSkill(b.ArgInt(0, 0)); return true; };
        ShowBox(box);
    }

    /// <summary>OnMessage (:560): a box's answer. The client closes the box (FUN_14151f200) unless an answer of 0
    /// with a one-part tag (which closes it first) or a two-part tag whose handler says to keep it; here the box closes
    /// through MessageBoxQueue.Next and the listener says it used the answer.</summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        if (IsClosed) return false;
        bool close = true;
        if (button == 0)
        {
            var parts = (tag ?? "").Split(',', 2);   // FUN_14028cd00(tag, parts, L",", 2)
            if (parts.Length == 1)
            {
                MessageBoxQueue.Next();
                close = false;                  // the client closes the box first and no handler closes it again
                if (tag.StartsWith("breakGuild", StringComparison.Ordinal)) Book.Break();             // OnBreakGuild
                else if (tag.StartsWith("leaveGuild", StringComparison.Ordinal)) Book.Leave();       // SendGuildMode7
                else if (tag.StartsWith("expelMember", StringComparison.Ordinal)) { Book.Expel(expelTarget); expelTarget = ""; }      // SendGuildMode8(m_strDesc)
                else if (tag.StartsWith("changeMaster", StringComparison.Ordinal)) { Book.ChangeMaster(masterTarget); masterTarget = ""; }   // SendGuildMode63(m_strName)
                // sendMail (OnSendMail), unregistPvP (Pvp mode 2): STILL-OPEN
            }
            else
            {
                string arg = parts[1];
                if (tag.StartsWith("createGuild", StringComparison.Ordinal)) close = Book.Create(arg);           // OnCreateGuild
                else if (tag.StartsWith("serachGuildName", StringComparison.Ordinal)) close = Book.SearchByName(arg);   // SendGuildMode88
                else if (tag.StartsWith("addMember", StringComparison.Ordinal)) close = arg.Length > 0 && Book.Invite(arg);           // OnAddMember @0x1419755a0
                else if (tag.StartsWith("changeNotify", StringComparison.Ordinal)) close = arg.Length > 0 && Book.SendText(1, arg);   // OnChangeNotify @0x141974df0
                else if (tag.StartsWith("changeMemo", StringComparison.Ordinal)) close = Book.SetMemo(arg);                           // OnChangeMemo @0x141974e70
                // checkChampionGuild (SendPVPMode4): STILL-OPEN
            }
        }
        if (close) MessageBoxQueue.Next();
        return true;
    }
}
