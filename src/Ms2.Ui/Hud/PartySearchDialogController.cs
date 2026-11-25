using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The C++ side of the party search window, UIPartySearchDialog (client/ms2/uipartysearchdialog.cpp; UI id 0x4eda, the game
/// menu's "partysearch"), for its movie uipartysearchdialog (PartySearchDialog). It keeps a search condition (m_180, a
/// CMatchPartyRecruitSchCond), queues a search (m_178) that the tick sends no sooner than 500 ms after the last one (m_17c),
/// lists the received listings 12 a page and handles the rows' join, whisper and invite calls and the register buttons
/// (the register dialog, <see cref="PartySearchRegisterDialogController"/>, is UI id 0x4edb).
/// A Party packet (opcode 0x53) whose mode byte is 0 (the party result message) refreshes the list: UIPartySearchDialog::vfunc_79
/// (@0x140f8c6b0, uipartysearchdialog.cpp:343) is the dialog's packet handler (the event is the CInPacket, and
/// Unk_PsdKeyEvent::vfunc_1 reads its next byte, the mode) and with the movie loaded runs ForceClickRefresh, see <see cref="OnPartyApplied"/>.
/// The chief menu's field-player variant (FUN_141a7eb60) is <see cref="PopupMenuController.OpenForPartySearch"/>'s.
/// </summary>
public sealed class PartySearchDialogController : UiController, IMessageBoxListener
{
    public const int UiId = 0x4eda;
    /// <summary>FUN_140f8ed40's page size.</summary>
    public const int PageSize = 0xc;
    /// <summary>IsSearchDelayElapsed's delay, in ms.</summary>
    public const uint SearchDelayMs = 500;
    const string AskJoinTag = "askPartyJoinByAlreadyMemberSearch";

    public override string Movie => "uipartysearchdialog";
    protected override string EntityMethod => "buildItemListTo";
    /// <summary>GetTypeName (@0x140f8c580).</summary>
    public override string TypeName => "PartySearchDialog";

    readonly PartyBook book;

    public PartySearchDialogController(PartyBook book)
    {
        this.book = book;
        book.ResultsReceived += OnResults;
        book.RecruitShown += OnRecruitShown;
        book.SearchTextCleared += ClearSearchText;
        book.Applied += OnPartyApplied;
    }

    /// <summary>Drops the book subscriptions (the HUD's teardown).</summary>
    public void Detach()
    {
        book.ResultsReceived -= OnResults;
        book.RecruitShown -= OnRecruitShown;
        book.SearchTextCleared -= ClearSearchText;
        book.Applied -= OnPartyApplied;
    }

    /// <summary>vfunc_79 (@0x140f8c6b0, uipartysearchdialog.cpp:343): with the movie loaded, a Party packet of mode 0 (the party
    /// result message) runs ForceClickRefresh.</summary>
    void OnPartyApplied(PartyMessage message)
    {
        if (message is PartyResult && Host?.Entity != null && !IsClosed) Host.Invoke("forceClickRefresh");
    }

    /// <summary>m_180: the search condition, made by the first InitSearchCondition.</summary>
    public PartySearchCondition Condition { get; private set; }
    /// <summary>m_178: a search is queued for the tick.</summary>
    public bool SearchQueued { get; private set; }
    /// <summary>m_17c: GetTickCountMs2 of the last search sent.</summary>
    uint lastSearchMs;

    public Func<uint> TickMs = () => (uint)Environment.TickCount;
    /// <summary>GetStringCommon.</summary>
    public Func<int, string> Common = id => id.ToString("x");
    /// <summary>The local player's character id (PCBase::FUN_141656a80); 0 with no player.</summary>
    public Func<long> PlayerCharId = () => 0;
    /// <summary>The local player's name (PCBase vfunc_77); null with no player.</summary>
    public Func<string> PlayerName = () => null;
    /// <summary>CBanTextChecker::CheckPartySearchText: null when the text passes, else the message.</summary>
    public Func<string, string> CheckSearchText = _ => null;
    /// <summary>FUN_140f8ff40 / FUN_141275460(id, 1, 1): the system notice of a common string.</summary>
    public Action<int> Notice = _ => { };
    /// <summary>The chat window's whisper to a name (GetState()->m_48 FUN_141a252a0).</summary>
    public Action<string> Whisper = _ => { };
    /// <summary>CUIService::OpenDialog(0x4edb).</summary>
    public Action OpenRegister = () => { };
    /// <summary>CUIService::CloseDialog(0x4edb).</summary>
    public Action CloseRegister = () => { };
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;

    /// <summary>
    /// OnLoadComplete (@0x140f8c740): UIDialog's, BindCloseButton("m_closeButton"), InitSearchCondition, initData, then
    /// initDataTo with alreadyRegisterMemberSearch (SetAlreadyRegisterMemberSearch: the own listing is set), the sort rows
    /// (FUN_140f8ef30) and the row of the condition's kind; QueueSearch(1) comes before the initDataTo call.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        BindCloseButton("m_closeButton");
        InitSearchCondition();
        Host.Invoke("initData");
        int index = 0;
        var sorts = SortRows(Condition.Kind, ref index);
        QueueSearch(1);
        Host.Invoke("initDataTo", Host.Obj(("alreadyRegisterMemberSearch", !book.Recruitment.IsEmpty), ("arrSortMemberSearch", sorts),
            ("selectedSortIndex", index)));
    }

    /// <summary>vfunc_26 (@0x140f8c970): UIDialog's, m_17c = 0, the queued search dropped (FUN_140f8ded0) and the register
    /// dialog closed.</summary>
    public override void Close()
    {
        base.Close();
        lastSearchMs = 0;
        SearchQueued = false;
        CloseRegister();
    }

    /// <summary>vfunc_15 (@0x140f8c9c0): a queued search goes out once the delay has passed
    /// (CPartyManager::SendMatchPartyMode2), stamping m_17c.</summary>
    public void Tick()
    {
        if (!SearchQueued || !IsSearchDelayElapsed() || Condition == null) return;
        book.Search(Condition);
        lastSearchMs = TickMs();
        SearchQueued = false;
    }

    /// <summary>vfunc_13 (@0x140f8ca80): Enter goes to the movie's onProcessKey("onEnterKey"); true when the movie took it.</summary>
    public bool EnterKey() => Host?.Entity != null && Rt.ToBoolean(Host.Invoke("onProcessKey", (object)"onEnterKey"));

    /// <summary>The sort rows (FUN_140f8ef30): {label = common string, sortValue}; <paramref name="index"/> gets the row of
    /// <paramref name="kind"/>.</summary>
    List<object> SortRows(int kind, ref int index)
    {
        (int Label, int Sort)[] sorts = { (0xb29, 0xc), (0xb2a, 0xb), (0xb2b, 0x16), (0xb2c, 0x15) };
        var rows = new List<object>(sorts.Length);
        for (int i = 0; i < sorts.Length; i++)
        {
            rows.Add(Host.Obj(("label", Common(sorts[i].Label) ?? ""), ("sortValue", sorts[i].Sort)));
            if (kind == sorts[i].Sort) index = i;
        }
        return rows;
    }

    /// <summary>
    /// FUN_140f8f410: the rows [start, start + count) of the listings as {title, memberCount ("{count}/{max}"), chiefName,
    /// allowAutoJoin, mine (the leader is the local character), sn (the listing id)}; returns the listing count.
    /// </summary>
    int Rows(int start, int count, List<object> rows, IReadOnlyList<PartyRecruit> list)
    {
        int total = list.Count;
        if (count == 0) return total;
        if (start < 0 || (total != 0 && start >= total)) return total;
        int end = Math.Min(start + count, total);
        long me = PlayerCharId();
        for (int i = start; i < end; i++)
        {
            var r = list[i];
            if (r == null) continue;
            rows.Add(Host.Obj(("title", r.Title ?? ""), ("memberCount", $"{r.MemberCount}/{r.MaxMembers}"), ("chiefName", r.LeaderName ?? ""),
                ("allowAutoJoin", r.AutoJoin ? 1 : 0), ("mine", me == r.LeaderCharacterId), ("sn", r.Id.ToString())));
        }
        return total;
    }

    /// <summary>FUN_140f8ed40 (@0x140f8ed40), the manager's results notify: the queued search dropped, then FUN_140f8def0 with
    /// a page of 12: buildItemListTo(rows, total, start == 0) and, with a search text, updateNoticeText (common 0xb32 of the
    /// text for no listing, else "").</summary>
    void OnResults(IReadOnlyList<PartyRecruit> list, int start)
    {
        SearchQueued = false;
        if (Host?.Entity == null || IsClosed || Condition == null) return;
        var rows = new List<object>();
        int total = Rows(start, PageSize, rows, list);
        Host.Invoke("buildItemListTo", rows, total, start == 0);
        if (!string.IsNullOrEmpty(Condition.Text))
            Host.Invoke("updateNoticeText", (object)(total == 0 ? ClientFormat.Format(Common(0xb32) ?? "", Condition.Text) : ""));
    }

    /// <summary>FUN_140f8ee50 through the manager: updateDataTo({alreadyRegisterMemberSearch}); after the own listing changed
    /// (<paramref name="refresh"/>), forceClickRefresh too (ForceClickRefresh @0x140f8e610).</summary>
    void OnRecruitShown(PartyRecruit own, bool refresh)
    {
        if (Host?.Entity == null || IsClosed) return;
        Host.Invoke("updateDataTo", Host.Obj(("alreadyRegisterMemberSearch", !own.IsEmpty)));
        if (refresh) Host.Invoke("forceClickRefresh");
    }

    /// <summary>ClearSearchText (@0x140f8dde0): m_inputSearch's text and the condition's text emptied.</summary>
    public void ClearSearchText()
    {
        if (Host?.Entity != null && !IsClosed && Host.Child("m_inputSearch") is { } input) Host.Set(input, "text", "");
        if (Condition != null) Condition.Text = "";
    }

    /// <summary>IsSearchDelayElapsed (@0x140f8e3a0): now is at least m_17c + 500.</summary>
    bool IsSearchDelayElapsed() => TickMs() >= lastSearchMs + SearchDelayMs;

    /// <summary>QueueSearch (@0x140f8e3f0): page set, page size 0, m_178 = 1.</summary>
    void QueueSearch(int page)
    {
        Condition.Page = page;
        Condition.PageSize = 0;
        SearchQueued = true;
    }

    /// <summary>ValidateSearchText (@0x140f8e480): a passing text becomes the condition's; a refused one shows common 0xb33 of
    /// the check's text in updateNoticeText.</summary>
    bool ValidateSearchText(string text)
    {
        var bad = CheckSearchText(text ?? "");
        if (bad == null) { Condition.Text = text ?? ""; return true; }
        Host.Invoke("updateNoticeText", (object)ClientFormat.Format(Common(0xb33) ?? "", bad));
        return false;
    }

    /// <summary>InitSearchCondition (@0x140f8e670): the first open makes the condition (page 1, kind 0xc); later opens set
    /// page 1 and empty the text.</summary>
    void InitSearchCondition()
    {
        if (Condition == null) Condition = new PartySearchCondition { Page = 1, Kind = 0xc };
        else { Condition.Page = 1; Condition.Text = ""; }
    }

    /// <summary>vfunc_28 (@0x140f8cdf0): the movie's calls, compared without case (_stricmp).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        object Arg(int i) => args != null && i < args.Length ? args[i] : null;
        if (Condition == null) InitSearchCondition();
        if (Is(name, "onChangeIndexDungeonList"))
        {
            Condition.FieldId = 0; Condition.Category = 0; Condition.Page = 1; Condition.Text = "";
            QueueSearch(1);
            return true;
        }
        if (Is(name, "onChangeSortPartySearch")) return true;
        if (Is(name, "onChangeSortMemeberSearch"))
        {
            Condition.Kind = Rt.ToInt32(Arg(0));
            if (ValidateSearchText(Rt.ToString(Arg(1)))) QueueSearch(1);
            return true;
        }
        if (Is(name, "onChangePage"))
        {
            // FUN_140f8e340: buildPageTo(rows [start, start + count), total) unless start < 0 or count 0
            bool ok = IsSearchDelayElapsed();
            if (ok)
            {
                int start = Rt.ToInt32(Arg(0)), count = Rt.ToInt32(Arg(1));
                if (start >= 0 && count != 0)
                {
                    var rows = new List<object>();
                    int total = Rows(start, count, rows, book.Results);
                    Host.Invoke("buildPageTo", rows, total);
                }
            }
            SetResult(Arg(2), ok);
            return true;
        }
        if (Is(name, "OnChangeList"))
        {
            bool ok = IsSearchDelayElapsed();
            if (ok) QueueSearch(Rt.ToInt32(Arg(0)));
            SetResult(Arg(1), ok);
            return true;
        }
        if (Is(name, "onClickRegisterMemeberSearch")) { OnClickRegister(); return true; }
        if (Is(name, "onClickRegisterMemeberSearchModify")) { OnClickRegisterModify(); return true; }
        if (Is(name, "onClickRefresh") || Is(name, "onClickSearch"))
        {
            if (ValidateSearchText(Rt.ToString(Arg(0)))) QueueSearch(1);
            return true;
        }
        if (Is(name, "onClickPartyInvite")) { OnInvite(Rt.ToString(Arg(0))); return true; }
        if (Is(name, "onClickPartyJoin")) { OnJoin(ParseId(Arg(0))); return true; }
        if (Is(name, "onClickWhisper"))
        {
            var who = Rt.ToString(Arg(0));
            if (string.IsNullOrEmpty(who) || PlayerName() is not { } me) return true;
            if (me == who) Notice(0xb34);
            else Whisper(who);
            return true;
        }
        if (Is(name, "onRightClickChiefName")) { OnRightClickChiefName(Arg(0), Arg(1), Arg(2)); return true; }
        return false;
    }

    void SetResult(object ret, bool ok)
    {
        if (ret is AsObject o) Host.Set(o, "result", ok);
    }

    /// <summary>
    /// onRightClickChiefName (uipartysearchdialog.cpp:641): the click's x and y, the row's sn as a listing id; an id of 0 or an
    /// unknown listing does nothing; a listing whose character id (the second id, "created") is above 0 goes to FUN_140f8e790
    /// (@0x140f8e790) with its owner account, that id and the chief's name.
    /// </summary>
    void OnRightClickChiefName(object x, object y, object sn)
    {
        long id = ParseId(sn);
        if (id == 0 || book.FindRecruit(id) is not { } recruit) return;
        if (recruit.LeaderCharacterId > 0)
            ChiefMenu(Rt.ToInt32(x), Rt.ToInt32(y), recruit.LeaderAccountId, recruit.LeaderCharacterId, recruit.LeaderName);
    }

    /// <summary>The shot fixture's right click on a listing's chief name (the row's sn as the movie sends it).</summary>
    public void RightClickChiefShot(long id) => OnRightClickChiefName(640, 260, id.ToString());

    /// <summary>FUN_140f8e790: the chief-name popup menu (see PopupMenuController.OpenForPartySearch).</summary>
    public Action<float, float, long, long, string> ChiefMenu = (_, _, _, _, _) => { };

    /// <summary>FUN_140718350: the row's sn text as a listing id (0 when it does not parse).</summary>
    static long ParseId(object v) => long.TryParse(Rt.ToString(v), out var id) ? id : 0;

    /// <summary>onClickPartyInvite: the own name notice 0xb38; in a party and not its leader, 0xb39; else
    /// CPartyManager::SendPartyMode1(name), the party invite.</summary>
    void OnInvite(string who)
    {
        if (string.IsNullOrEmpty(who) || PlayerName() is not { } me) return;
        if (who == me) { Notice(0xb38); return; }
        if (book.IsInParty && !book.IsLeader(PlayerCharId())) { Notice(0xb39); return; }
        book.Send(new PartyInviteRequest(who));
    }

    /// <summary>
    /// onClickPartyJoin: a found listing whose leader is the local character gives notice 0xb35; in a party, box 0xb36; with an
    /// own listing, the yes / no box 0xb31 tagged "askPartyJoinByAlreadyMemberSearch,{id}" (<see cref="OnMessageBox"/>);
    /// else CPartyManager::SendPartyInviteFromRecruit.
    /// </summary>
    void OnJoin(long id)
    {
        if (id == 0 || book.FindRecruit(id) is not { } recruit) return;
        if (PlayerName() is { } me)
        {
            if (recruit.LeaderCharacterId == PlayerCharId()) { Notice(0xb35); return; }
            if (book.IsInParty) { Box(0xb36, 0, null, ""); return; }
            if (!book.Recruitment.IsEmpty) { Box(0xb31, 1, this, AskJoinTag + "," + id); return; }
        }
        book.JoinRecruit(recruit, PlayerName() ?? "");
    }

    /// <summary>OnClickRegisterMemeberSearch (@0x140f8eab0): in a party and not its leader, box 0xb3a; a register pending, box
    /// 0xb3d; 10 members or more, the box of common 0xb3b of 10; else the register dialog opens.</summary>
    void OnClickRegister()
    {
        if (PlayerName() == null) return;
        if (book.IsInParty && !book.IsLeader(PlayerCharId())) { Box(0xb3a, 0, null, ""); return; }
        if (book.RecruitPending) Box(0xb3d, 0, null, "");
        else if (book.Members.Count >= PartyBook.MaxMembers)
            ShowBox(MessageBoxQueue.Create(0, ClientFormat.Format(Common(0xb3b) ?? "", PartyBook.MaxMembers), null, ""));
        else OpenRegister();
    }

    /// <summary>OnClickRegisterMemeberSearchModify (@0x140f8ec40): in a party and not its leader, box 0xb3e; no own listing,
    /// box 0xb3f; else the register dialog opens.</summary>
    void OnClickRegisterModify()
    {
        if (PlayerName() == null) return;
        if (book.IsInParty && !book.IsLeader(PlayerCharId())) { Box(0xb3e, 0, null, ""); return; }
        if (book.Recruitment.IsEmpty) Box(0xb3f, 0, null, "");
        else OpenRegister();
    }

    /// <summary>FUN_140d73bc0(id, type, listener, tag): a box of the common string.</summary>
    void Box(int id, int type, IMessageBoxListener listener, string tag) => ShowBox(MessageBoxQueue.Create(type, Common(id) ?? "", listener, tag));

    /// <summary>OnMessage (@0x140f8cc10): yes (0) on the "askPartyJoinByAlreadyMemberSearch,{id}" box joins the listing when it
    /// is still found and set.</summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        if (button != 0 || tag == null || !tag.StartsWith(AskJoinTag, StringComparison.Ordinal)) return true;
        var parts = tag.Split(',', 2);
        if (parts.Length < 2 || !ulong.TryParse(parts[1], out var id)) return true;
        if (book.FindRecruit((long)id) is { IsEmpty: false } recruit) book.JoinRecruit(recruit, PlayerName() ?? "");
        return true;
    }

    /// <summary>BindCloseButton (uidialog.cpp:977): the child's click runs the close.</summary>
    void BindCloseButton(string name)
    {
        if (Host.Child(name) is not { } b) { Host.RT.NoteMissing("party search dialog: " + name); return; }
        Func<object[], object> f = _ => { Close(); return null; };
        Host.Invoke(b, "addEventListener", "buttonClick", f, false, 0, false);
    }
}
