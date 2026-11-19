using Ms2.Avm2;
using Ms2.Quest;
using Ms2.Net.Packets;
using ReservedWords = Ms2.Formats.World.ReservedWords;

namespace Ms2.Ui.Hud;

/// <summary>
/// The C++ side of the guild dialog, UIGuildDialog (client/ms2/uiguilddialog.cpp; UI id 0x4e4f), for its movie uiguilddialog.
/// The state is <see cref="GuildBook"/> (CGuildManager). Only the side outside a guild is here: the Join page (the search by
/// propensity or by name, the join requests and their cancel, this character's requests) and the Create page (the create
/// box). The pages are made on first use (EnsurePageCreated) and laid on the movie root.
/// STILL-OPEN: the in-guild pages (Info, Member, Skill, MemberSkill, Event, AcceptMember, Manager, House, Storage, Raid,
/// ChampionShip, ChampionShipHistroy) and their callbacks, the invite (onClickInviteMember), the master popup
/// (OnSearchGuildMasterPopupMenu, 0x4e39) as an event, the block check of OnClickCreateGuild (CBlockManager
/// FindBlockedAction(7)), the web guide button (UIDialog::BindWebGuideButton, uidialog.cpp:997), the championship icon
/// (CGameRule::IsGuildChampionshipOpen), the event buttons of the 0x8a handler FUN_141968140 (modes 0x76/0x77), the
/// master and grade info, the member list, the requests for the guild (FUN_14196da70 passes their count, 0 here) and the
/// tab settings (vfunc_54 / vfunc_53, "Histroy" lastTabIndex).
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
    object createPage;          // m_createPage
    object joinPage;            // m_joinPage
    object infoPage;            // m_infoPage
    object memberPage;          // m_memberPage
    int memberSort;             // m_nMemberSort (0 in the ctor)

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
    /// <summary>FUN_140d73c60: show a message box.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;

    /// <summary>OnSearchGuildMasterPopupMenu: the popup 0x4e39 at (x, y) for the master's name (Unk_PopupMenuInfo(x, y, 3,
    /// 0x3f3fe), SetGuildInfo from FindSearchResultByName). STILL-OPEN.</summary>
    public event Action<float, float, string> PopupRequested;

    public GuildDialogController(GuildBook book) => Book = book;

    /// <summary>m_nTab.</summary>
    public int Tab => tab;
    /// <summary>m_nSearchPage.</summary>
    public int SearchPage => searchPage;
    /// <summary>The Join page (m_joinPage) once made.</summary>
    public object JoinPage => joinPage;
    /// <summary>The Create page (m_createPage) once made.</summary>
    public object CreatePage => createPage;

    bool NoMovie => Host?.Entity == null;

    /// <summary>OnLoadComplete (@0x141961ab0, :371).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        Book.SearchRefresh -= OnSearchRefresh;
        Book.SearchRefresh += OnSearchRefresh;
        Book.MyRequestsRefresh -= BuildSearchMy;
        Book.MyRequestsRefresh += BuildSearchMy;
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
        // FUN_14196da70: setAcceptMemberCount(the requests for the guild); m_joinRequestsForGuild is not ported, so 0
        if (!NoMovie) Host.Invoke(Host.Entity, "setAcceptMemberCount", 0);
    }

    /// <summary>vfunc_67 (:297): ClosePopup on the root, UIDialog's close, ClearSearchResults. The pages stay
    /// until the movie is dropped (vfunc_26 clears them).</summary>
    public override void Close()
    {
        if (!NoMovie) Host.Invoke(Host.Entity, "ClosePopup");
        base.Close();
        Book.SearchRefresh -= OnSearchRefresh;
        Book.MyRequestsRefresh -= BuildSearchMy;
        Book.ClearSearchResults();
        createPage = null;
        joinPage = null;
        infoPage = null;
        memberPage = null;
    }

    /// <summary>InitializeGuildDialog (:2945).</summary>
    void InitializeGuildDialog()
    {
        // FUN_1419260d0("m_webGuideButton", 0) = UIDialog::BindWebGuideButton: STILL-OPEN (WebGuideTable)
        Host.Invoke(Host.Entity, "setupCategory", false);                  // SetupCategory: IsGuildChampionshipOpen && IsInGuild
        Host.Invoke(Host.Entity, "showGuildDialog", Book.InGuild ? 1 : 0); // ShowGuildDialog
        SetMasterInfo();
        SetGradeInfo();
        BuildMembers();                                                     // FUN_141975620
        SetPlayer();                                                        // FUN_14196b3a0
        if (Host.Child("m_attendBG") is { } attend) Host.SetVisible(attend, false);  // UpdateAttendBackground: CanAttend
    }

    /// <summary>OpenCategoryPage (@0x141969040): HideAllPages, the page made, then ShowCategoryPage.</summary>
    public void OpenCategoryPage(string category)
    {
        if (NoMovie) return;
        SetPageVisible(joinPage, false);                    // HideAllPages
        SetPageVisible(createPage, false);
        SetPageVisible(infoPage, false);
        SetPageVisible(memberPage, false);
        if (category == "Join") CreateJoinPage();
        else if (category == "Create") EnsurePageCreated("guild_create_page", ref createPage);   // CreateCreatePage
        else if (category == "Info") CreateInfoPage();
        else if (category == "Member") CreateMemberPage();
        // the other in-guild pages: STILL-OPEN
        if (category == "Join") SetPageVisible(joinPage, true);              // ShowCategoryPage (@0x141969290)
        else if (category == "Create") SetPageVisible(createPage, true);
        else if (category == "Info") SetPageVisible(infoPage, true);
        else if (category == "Member") SetPageVisible(memberPage, true);
    }

    /// <summary>CreateInfoPage (@0x14196a000): made now, UpdateGuildInfo, SetMasterInfo, UpdateGuildExp, the fund and the
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

    /// <summary>UpdateGuildExp (@0x14196dc00).</summary>
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
    }

    /// <summary>FUN_141975620: the member rows to the root and the Member page.</summary>
    void BuildMembers()
    {
        if (NoMovie) return;
        var rows = new List<object>();
        foreach (var m in Book.Members) rows.Add(MemberRow(m));
        Host.Invoke(Host.Entity, "buildGuildMembers", rows);
        if (memberPage != null) Host.Invoke(memberPage, "buildGuildMembers", rows);
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

    /// <summary>vfunc_28 (@0x1419634e0, :652) and FUN_1419681b0 (:1261): the movie's calls outside a guild.</summary>
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
            case "onsearchguildmasterpopupmenu":
                if (args.Length > 0) PopupRequested?.Invoke(Float(0), Float(1), Str(2));
                return true;
            case "onsetpropensity":
                if (args.Length > 0 && Int(0) == 1)
                {
                    Book.SearchPropensity = Int(1);
                    Book.Search(false);             // SendGuildMode87(false)
                }
                // type 2 is SendGuildMode68 (in a guild): STILL-OPEN
                return true;
        }
        return false;
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
                close = false;
                // breakGuild, leaveGuild, expelMember, changeMaster, sendMail, unregistPvP: in a guild, STILL-OPEN
            }
            else
            {
                string arg = parts[1];
                if (tag.StartsWith("createGuild", StringComparison.Ordinal)) close = Book.Create(arg);           // OnCreateGuild
                else if (tag.StartsWith("serachGuildName", StringComparison.Ordinal)) close = Book.SearchByName(arg);   // SendGuildMode88
                // addMember, changeNotify, changeMemo, checkChampionGuild: in a guild, STILL-OPEN
            }
        }
        if (close) MessageBoxQueue.Next();
        return true;
    }
}
