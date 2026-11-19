using Ms2.Avm2;
using Ms2.Quest;
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
    }

    /// <summary>InitializeGuildDialog (:2945).</summary>
    void InitializeGuildDialog()
    {
        // FUN_1419260d0("m_webGuideButton", 0) = UIDialog::BindWebGuideButton: STILL-OPEN (WebGuideTable)
        Host.Invoke(Host.Entity, "setupCategory", false);                  // SetupCategory: IsGuildChampionshipOpen && IsInGuild
        Host.Invoke(Host.Entity, "showGuildDialog", Book.InGuild ? 1 : 0); // ShowGuildDialog
        // SetMasterInfo: only with a master member (STILL-OPEN, the members are not read)
        Host.Invoke(Host.Entity, "setGradeInfo", new List<object>());       // SetGradeInfo: the grades 0..5 (none read)
        Host.Invoke(Host.Entity, "buildGuildMembers", new List<object>());  // FUN_141975620: the members (none read)
        if (PlayerName() is { } name) Host.Invoke(Host.Entity, "setPlayer", name);   // FUN_14196b3a0
        if (Host.Child("m_attendBG") is { } attend) Host.SetVisible(attend, false);  // UpdateAttendBackground: CanAttend
    }

    /// <summary>OpenCategoryPage (@0x141969040): HideAllPages, the page made, then ShowCategoryPage.</summary>
    public void OpenCategoryPage(string category)
    {
        if (NoMovie) return;
        SetPageVisible(joinPage, false);                    // HideAllPages
        SetPageVisible(createPage, false);
        if (category == "Join") CreateJoinPage();
        else if (category == "Create") EnsurePageCreated("guild_create_page", ref createPage);   // CreateCreatePage
        // the in-guild pages: STILL-OPEN
        if (category == "Join") SetPageVisible(joinPage, true);              // ShowCategoryPage (@0x141969290)
        else if (category == "Create") SetPageVisible(createPage, true);
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
