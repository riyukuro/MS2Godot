using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIAchieveDialog (client/ms2/uiachievedialog.cpp), game menu 0x4e73: the trophy dialog. The category tree (setCategory),
/// the summary page (the last clears and the score) and the trophy list (m_heroTrophy's m_list, with the filter dropdown,
/// the search box and the reward-kind filter) come from the player's <see cref="AchieveBook"/>; the reward, bookmark and
/// GM progress buttons send SendOp::Achieve.
/// <para>
/// STILL-OPEN (each answered by a delegate that defaults to "none"): the game events of CheckRequire and the category
/// filters (GameEventManagerCli); the collection marks of reqCondition (DAT_143ab30e0 items, CFishingManager, CPetManager,
/// the dungeon record); IsDebugMode (CInputKeyHandler); OnUseLink (FUN_140d2cca0, raised as <see cref="UseLink"/>); vfunc_80, the platform's late search answer
/// (KR's vfunc_37 answers at once, so it is not reached).
/// </para>
/// </summary>
public sealed class AchieveController : UiController
{
    public override string Movie => "uiachievedialog";
    protected override string EntityMethod => "setCategory";
    /// <summary>GetTypeName (:206).</summary>
    public override string TypeName => "AchieveDialog";

    /// <summary>GetAchieveManager(): the player's trophies.</summary>
    public AchieveBook Book;
    public AchieveTexts Texts;
    /// <summary>s_aCategory: the category rows.</summary>
    public AchieveCategoryTable Categories;
    /// <summary>DAT_143ab2914: the GM level (0 for a player); 50 and up shows the debug buttons.</summary>
    public Func<int> GmLevel = () => 0;
    /// <summary>CInputKeyHandler::IsDebugMode (FUN_14025e7c0). STILL-OPEN: no debug mode; false.</summary>
    public Func<bool> DebugMode = () => false;
    /// <summary>CItemDataTable::GetPaletteColorBySN(palette, colorSN): null without the colour.</summary>
    public Func<uint, uint, Items.ColorPaletteTable.Color> Colors = (_, _) => null;
    /// <summary>WorldService::GetLocalPlayer() != null.</summary>
    public Func<bool> HasPlayer = () => true;
    /// <summary>PCBase::IsDead of the local player.</summary>
    public Func<bool> LocalPlayerDead = () => false;
    /// <summary>GetQuestName (condition types 0x3b / 0x31).</summary>
    public Func<long, string> QuestName = _ => "";
    /// <summary>CQuestManagerCommon::IsQuestRewarded.</summary>
    public Func<long, bool> QuestRewarded = _ => false;
    /// <summary>DungeonRoomTable::GetRoom (condition type 0xc5).</summary>
    public Func<long, DungeonRoom> Room = _ => null;
    /// <summary>The game event tests of CAchieve::CheckRequire / CanRewardRequire. STILL-OPEN: no game events.</summary>
    public Func<string, bool> EventEnabled = _ => false;
    public Func<long, bool> EventFound = _ => false;
    /// <summary>DAT_143ab30e0.Lookup(id) != 0 (types 8 / 10, the item collection). STILL-OPEN: false.</summary>
    public Func<long, bool> ItemCollected = _ => false;
    /// <summary>CFishingManager FUN_140cdc380 (type 0xd1). STILL-OPEN: false.</summary>
    public Func<long, bool> FishCollected = _ => false;
    /// <summary>CPetManager::GetCountByKey(id) != 0 (type 0x10a). STILL-OPEN: false.</summary>
    public Func<long, bool> PetCollected = _ => false;
    /// <summary>GetMyCharacter()->GetRecord()->GetRoomValue5c(id) != 0 (type 0xc5). STILL-OPEN: false.</summary>
    public Func<long, bool> DungeonCleared = _ => false;

    /// <summary>FUN_140710590: send a packet.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>FUN_141275260(0x80d, 1, 1): the dead notice when a reward is taken.</summary>
    public event Action<int> SystemNotice;
    /// <summary>PlaySystemSound.</summary>
    public event Action<string> PlaySound;
    /// <summary>FUN_141924680: <see cref="ShowTrophy"/> on a closed dialog opens it.</summary>
    public event Action Open;
    /// <summary>OnUseLink: FUN_140d2cca0(id, rank). STILL-OPEN.</summary>
    public event Action<uint, uint> UseLink;

    /// <summary>m_178: the selected tree row (an index of GetRows); ctor 0.</summary>
    public int TabIndex;
    /// <summary>m_17c: the selected category id; ctor FindIdByTab(1).</summary>
    public int CategoryId;
    /// <summary>m_180: the filter dropdown index set at load; ctor 0.</summary>
    public int FilterIndexAtLoad;
    /// <summary>m_188: the search text set at load.</summary>
    public string SearchAtLoad = "";
    /// <summary>m_190: every reward kind shown (ctor true).</summary>
    public bool AllKinds = true;
    /// <summary>m_198: reward kind -> shown (ctor 0..13 = 1).</summary>
    readonly SortedDictionary<int, int> kinds = new();
    /// <summary>m_1e0: trophy id -> the row is expanded.</summary>
    readonly Dictionary<int, int> expanded = new();

    /// <summary>The PlaySystemSound of onReward (kTrophyRewardGet).</summary>
    public const string RewardSound = "system_Trophy_Reward_Get";
    /// <summary>The common string of the dead notice.</summary>
    public const int DeadNotice = 0x80d;

    public AchieveController(AchieveBook book, AchieveTexts texts, AchieveCategoryTable categories)
    {
        Book = book;
        Texts = texts;
        Categories = categories;
        CategoryId = categories.FindIdByTab(1);
        for (int i = 0; i < 14; i++) kinds[i] = 1;
    }

    /// <summary>OnLoadComplete (:212): UIDialog's first, then the expand marks and the pending ids cleared, the search text,
    /// RequestUserEnv u8 3, the filter index, and the category tree (FUN_1418a9360).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        expanded.Clear();
        Book.Pending.Clear();
        SetSearchText(SearchAtLoad);
        Send(new UserEnvRequest());
        SetFilterIndex(FilterIndexAtLoad);
        BuildTree();
    }

    /// <summary>vfunc_67 (:404): UIDialog's close, then the tab, the search text and the category reset.</summary>
    public override void Close()
    {
        base.Close();
        TabIndex = 0;
        SearchAtLoad = "";
        CategoryId = Categories.FindIdByTab(1);
    }

    /// <summary>vfunc_15 (:413), each frame: while open, the trophies a packet named (DAT_143af0f30) are redrawn, then the
    /// set is cleared.</summary>
    public void Process()
    {
        if (Book.Pending.Count == 0 || IsClosed) return;
        foreach (int id in Book.Pending.ToList()) UpdateOne(id);
        Book.Pending.Clear();
    }

    /// <summary>
    /// vfunc_13 (:425), a key while the dialog is shown: Escape runs the movie's closePopup and closes the dialog when that
    /// is false (vfunc_69); Enter (DIK 0x1c) runs the search. True = the key is taken (Enter never is).
    /// </summary>
    public bool OnKey(bool escape, bool enter)
    {
        if (Host?.Entity == null) return false;
        if (escape)
        {
            if (Rt.ToBoolean(Host.Invoke("closePopup"))) return true;
            Close();
            return true;
        }
        if (enter) RequestSearch();
        return false;
    }

    /// <summary>vfunc_53 (:469): "all", then each kind's name = its mark. A deviation: kind 0's name is "",
    /// which the client writes as an attribute with no name; an XML attribute needs a name, so it is not written (the load
    /// reads it back as 1 either way, XmlGetAttr finding nothing).</summary>
    protected override void SaveSettings(DialogSettings s)
    {
        s.SetAttr("all", AllKinds);
        foreach (var (k, v) in kinds)
        {
            string name = AchieveData.RewardTypeName(k);
            if (name.Length != 0) s.SetAttr(name, v);
        }
    }

    /// <summary>vfunc_54 (:483): "all", then kinds 0..13 each 1 unless the file has its name.</summary>
    protected override void LoadSettings(DialogSettings s)
    {
        bool all = AllKinds;
        s.GetAttr("all", ref all);
        AllKinds = all;
        for (int i = 0; i < 14; i++)
        {
            int v = 1;
            string name = AchieveData.RewardTypeName(i);
            if (name.Length != 0) s.GetAttr(name, ref v);
            kinds[i] = v;
        }
    }

    /// <summary>vfunc_49 (@0x1418a8690): the item id's tooltip at its default rank, ASTooltip(player, 0x20, 2).FUN_1409e1770
    /// (id, GetItemRank(id)); none when the item has no data.</summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        int id = args is { Length: > 0 } ? (int)Rt.ToUint32(args[0]) : 0;
        return Items.ItemTooltip.ById(Model, 0x20, id, Items.ItemTooltip.ItemRank(Model.Tables.Items, id));
    }

    /// <summary>vfunc_28 (:226).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        uint UInt(int i) => Rt.ToUint32(args[i]);
        int Int(int i) => Rt.ToInt32(args[i]);
        bool Bool(int i) => Rt.ToBoolean(args[i]);
        bool any = args is { Length: > 0 };   // ASData::SetArgsIfAny
        switch (name.ToLowerInvariant())
        {
            case "ontabchange":
                if (!any) return false;
                TabIndex = Int(0);
                CategoryId = args.Length > 1 ? Int(1) : 0;
                Fill();
                expanded.Clear();
                return true;
            case "onupdaterenderer":
                UpdateRewardCounts();
                return true;
            case "onreward":
            {
                if (!any) return false;
                if (!HasPlayer()) return false;
                if (LocalPlayerDead()) { SystemNotice?.Invoke(DeadNotice); return false; }
                Send(new AchieveRewardRequest((int)UInt(0)));    // u8 3, i32 the trophy id (:255-259)
                PlaySound?.Invoke(RewardSound);
                return true;
            }
            case "onuselink":
                if (any) UseLink?.Invoke(UInt(0), args.Length > 1 ? UInt(1) : 0);
                return true;
            case "ondbgup": return Debug(args, 0, true);
            case "ondbgdown": return Debug(args, 1, false);
            case "ondbgup30": return Debug(args, 2, true);
            case "ondbgup99": return Debug(args, 3, true);
            case "onclickachievedetail":
                if (any) ShowTrophy(Int(0));
                return false;
            case "onsearch":
                if (any) RequestSearch();
                return false;
            case "onbookmark":
                if (any)
                {
                    uint id = UInt(0);
                    bool mark = args.Length > 1 && Bool(1);
                    if (Book.Find((int)id) is { } a)
                    {
                        a.Bookmark = mark;
                        Send(new AchieveBookmarkRequest(a.Id, mark));   // u8 4, i32 GetId, u8 the mark (:356-360)
                    }
                }
                return false;
            case "onfilterchange":
                Fill();
                return true;
            case "onclicksummary":
                if (any) ShowTrophy((int)UInt(0));
                return true;
            case "onexpand":
                if (any) expanded[(int)UInt(0)] = args.Length > 1 && Bool(1) ? 1 : 0;
                return false;
            case "onfilter":
                if (any)
                {
                    AllKinds = Bool(0);
                    string list = args.Length > 1 ? Rt.ToString(args[1]) : "";
                    foreach (int k in kinds.Keys.ToList()) kinds[k] = 0;
                    foreach (string tok in list.Split(',', StringSplitOptions.RemoveEmptyEntries))   // FUN_14025a680
                        kinds[AchieveData.ParseRewardType(tok)] = 1;
                    Fill();
                }
                return false;
        }
        return false;
    }

    /// <summary>onDbgUp / onDbgDown / onDbgUp30 / onDbgUp99 (:279-325): GM 50 and up only; u8 5, i32 the trophy id, u8 the
    /// mode; the up modes only while the progress is short of the start grade's goal (FUN_1418acd10). The call is never
    /// handled (returns 0).</summary>
    bool Debug(object[] args, byte mode, bool needsRoom)
    {
        if (GmLevel() < 50) return false;
        if (args is not { Length: > 0 }) return false;
        uint id = Rt.ToUint32(args[0]);
        if (!needsRoom || CanDebugUp((int)id)) Send(new AchieveDebugRequest((int)id, mode));
        return false;
    }

    /// <summary>FUN_1418acd10: the player has the trophy and its progress is below the start grade's goal.</summary>
    bool CanDebugUp(int id) => Book.Find(id) is { } a && a.Count < a.GradeGoal(a.StartGrade);

    /// <summary>vfunc_45 (:498): getFilter(kind) = the kind's mark (1 when unknown); getTooltipColor (:509) and
    /// getTooltipAchieve (:536), compared without case.</summary>
    public override object GetUserValue(string name, object[] args)
    {
        if (name == "getFilter")
        {
            int k = AchieveData.ParseRewardType(args is { Length: > 0 } ? Rt.ToString(args[0]) : "");
            return !kinds.TryGetValue(k, out int v) || v != 0;
        }
        if (string.Equals(name, "getTooltipColor", StringComparison.OrdinalIgnoreCase)) return TooltipColor(args);
        if (string.Equals(name, "getTooltipAchieve", StringComparison.OrdinalIgnoreCase)) return TooltipAchieve(args);
        return null;
    }

    /// <summary>getTooltipAchieve (:536), a trophy slot's tooltip: two uints, the trophy and its grade; the achieve tooltip
    /// (<see cref="AchieveTexts.Tooltip"/>, ASTooltip of the local player with flags 2) with the comment
    /// s_achieve_tooltip_comment (0x781); no value without the trophy's data.</summary>
    AsTooltip TooltipAchieve(object[] args)
    {
        uint id = args is { Length: > 0 } ? Rt.ToUint32(args[0]) : 0;
        uint grade = args is { Length: > 1 } ? Rt.ToUint32(args[1]) : 0;
        if (Texts.Tooltip((int)id, (int)grade) is not { } t) return null;
        t["comment"] = Texts.Common(0x781);
        return t;
    }

    /// <summary>
    /// getTooltipColor (:509): four uints, then the palette object of a reward row (its paletteID and colorSN). Without the
    /// colour (GetPaletteColorBySN) or its name key there is no value; else the text tooltip (ASTooltip(1, 2)) of
    /// s_beauty_tooltip_color_desc of the colour's name (string table 0x48) and description (0x49) and a newline, aligned 1,
    /// none when the name is empty.
    /// </summary>
    AsTooltip TooltipColor(object[] args)
    {
        if (args is not { Length: > 4 }) return null;
        uint palette = Rt.ToUint32(Host.Get(args[4], "paletteID"));
        uint sn = Rt.ToUint32(Host.Get(args[4], "colorSN"));
        if (Colors(palette, sn) is not { } c || c.StringKey == 0) return null;     // (unsigned)m_n2c > 0: any key but 0
        string desc = "";
        string colorName = Texts.TableString(0x48, c.StringKey);
        if (colorName.Length != 0)
            desc = Texts.CommonKeyF("s_beauty_tooltip_color_desc", colorName, Texts.TableString(0x49, c.StringKey)) + "\n";
        var tip = new AsTooltip();
        if (!tip.ApplyTextTooltip(desc)) return null;
        tip.SetTooltipAlign(1);
        return tip;
    }

    // ---- refresh -------------------------------------------------------------------------------------------------

    /// <summary>FUN_1418a8f80 (:548), after a trophy packet (scenestategame.cpp:4445): the tab index, the page, the reward
    /// counts, the expand marks cleared.</summary>
    public void Refresh()
    {
        if (Host?.Entity == null) return;
        ApplyTabIndex();
        Fill();
        UpdateRewardCounts();
        expanded.Clear();
    }

    /// <summary>FUN_1418a8ff0 (:559): the page of the selected category: the summary for tab 1, else the trophy list
    /// (the emblem tab's survival trophies, or the trophy tab's of the category's tags).</summary>
    public void Fill()
    {
        if (Host?.Entity == null) return;
        if (Categories.FindById(CategoryId) is not { } cat) return;
        if (Host.Child("m_heroTrophy") is { } hero)
        {
            if (cat.Tab == 2) Host.Invoke(hero, "gotoAndPlay", "search");          // ASGameEntity::vfunc_13
            else if (cat.Tab == 0) Host.Invoke(hero, "gotoAndPlay", "search_filter");
        }
        switch (cat.Tab)
        {
            case 1:
                Host.Invoke(Host.Entity, "gotoAndPlay", "summary");
                Summary();
                return;
            case 2:
                Host.Invoke(Host.Entity, "gotoAndPlay", "hero");
                HeroList(new FilterOptions { Survival = true, Filter = GetFilterIndex() });
                break;
            case 0:
                Host.Invoke(Host.Entity, "gotoAndPlay", "hero");
                HeroList(new FilterOptions { CategoryTag = cat.CategoryTag, ContentsTag = cat.ContentsTag, Filter = GetFilterIndex() });
                break;
        }
    }

    /// <summary>FUN_1418a9360 (:602): setCategory {type 1, nodes}: each open tab a node, its children the low rows.</summary>
    void BuildTree()
    {
        if (Host?.Entity == null) return;
        var nodes = new List<object>();
        foreach (var t in Categories.GetTabs())
        {
            var node = new Dictionary<string, object>
            {
                ["id"] = t.Id,
                ["label"] = Texts.TableString(0x4a, t.Id),
                ["rendererClassName"] = "export_renderer_category",
                ["tabType"] = t.Tab,
                ["alwaysOpen"] = t.Tree == 2,
                ["type"] = t.Tree == 1 ? 2 : 1,
            };
            var children = new List<object>();
            foreach (var c in t.Children)
                children.Add(new Dictionary<string, object>
                {
                    ["id"] = c.Id,
                    ["label"] = Texts.TableString(0x4a, c.Id),
                    ["rendererClassName"] = "export_renderer_category_low",
                    ["tabType"] = c.Tab,
                });
            if (children.Count != 0) node["nodes"] = children;
            nodes.Add(node);
        }
        Host.Invoke("setCategory", new Dictionary<string, object> { ["type"] = 1, ["nodes"] = nodes });
    }

    /// <summary>UpdateRewardCounts (:639): each row's title with the count of rewards waiting in it (common 0x784), type 8
    /// trophies left out.</summary>
    void UpdateRewardCounts()
    {
        if (Host?.Entity == null) return;
        var rows = Categories.GetRows();
        var counts = new int[rows.Count];
        foreach (var a in Book.GetList(0, 0, false, false))
        {
            if (a.Data.Type == 8) continue;
            int n = a.RewardCount;
            if (n == 0) continue;
            for (int j = 0; j < rows.Count; j++)
                if (rows[j].Matches(a.Data)) counts[j] += n;
        }
        for (int i = 0; i < rows.Count; i++)
        {
            string title = Texts.TableString(0x4a, rows[i].Id);
            if (counts[i] > 0) title += Texts.CommonF(0x784, counts[i]);    // BlockFormat(0x784, n)
            Host.Invoke("setRewardCount", rows[i].Id, title);
        }
    }

    /// <summary>FUN_1418a9c20 (:674): m_summaryTrophy's last clears (up to 13, the newest first) and the score.</summary>
    void Summary()
    {
        if (Host.Child("m_summaryTrophy") is not { } s) return;
        Host.Invoke(s, "removeAll");
        byte index = 0;
        foreach (var (_, id, grade) in ClearRecords(Book))
        {
            var a = Book.Find(id);
            if (a?.Data == null) continue;
            string desc = Formats.World.ReservedWords.StripTags(DescOf(a, grade));    // FUN_141842290
            var row = new Dictionary<string, object>
            {
                ["id"] = (uint)a.Id,
                ["icon"] = a.Data.IconPath,                               // GetName(1)
                ["grade"] = grade,
                ["date"] = AchieveTexts.RecordedDate(a, grade),
                ["title"] = Texts.Name(a.Id),
                ["desc"] = desc,
            };
            Host.Invoke(s, "setTrophy", index, row);                       // InvokeWithIndexAndRow
            if (index++ >= 12) break;
        }
        var tally = Book.Tally;
        Host.Invoke(s, "setScore", tally[0], tally[1], tally[2]);
    }

    /// <summary>AcCollectClears (@0x1418adae0): every cleared grade (GetValue != 0, the clear time) of GetFirstList's
    /// trophies, sorted by AcClearOrder: the time descending, then the grade descending.</summary>
    public static List<(long Time, int Id, int Grade)> ClearRecords(AchieveBook book)
    {
        var list = new List<(long Time, int Id, int Grade)>();
        foreach (var a in book.GetFirstList())
            for (int g = a.Data.FirstGrade; g <= a.CurGrade; g++)
            {
                long t = a.GetValue(g);
                if (t != 0) list.Add((t, a.Id, g));
            }
        return list.OrderByDescending(r => r.Time).ThenByDescending(r => r.Grade).ToList();
    }

    /// <summary>The FilterOptions of FUN_1418aa190.</summary>
    public sealed class FilterOptions
    {
        public bool Survival;
        public string CategoryTag = "", ContentsTag = "";
        /// <summary>The filter dropdown: 1 in progress, 2 done, 3 bookmarked, else all.</summary>
        public int Filter;
    }

    /// <summary>FUN_1418aa190 (:710): m_list refilled with the trophies that pass, in load order grouped by
    /// <see cref="SortState"/>; an "empty" row when none.</summary>
    void HeroList(FilterOptions o)
    {
        if (Host.Child("m_heroTrophy") is not { } hero) return;
        if (Host.Child("m_list", hero) is not { } list) return;
        var dp = Host.Get(list, "dataProvider");
        Host.Invoke(dp, "cleanUp");                                         // FUN_1422dd1d0
        foreach (var row in HeroRows(GetSearchText(), o)) Host.Invoke(dp, "push", row);   // AddItem
        Host.Set(list, "selectedIndex", 0);
        Host.Invoke(list, "invalidate");                                    // vfunc_24(true)
        Host.Invoke(list, "validateNow");
        Host.Set(list, "scrollPosition", 0);
    }

    /// <summary>The rows of <see cref="HeroList"/>.</summary>
    public List<Dictionary<string, object>> HeroRows(string search, FilterOptions o)
    {
        var rows = new List<Dictionary<string, object>>();
        var all = Book.GetListFromTable(0, 0, false, false);
        if (all.Count == 0)
        {
            rows.Add(new Dictionary<string, object> { ["state"] = "empty" });
            return rows;
        }
        var sorted = new SortedDictionary<ulong, Achieve>();
        for (int i = 0; i < all.Count; i++)
        {
            if (!RowFilter(all[i], search, o)) continue;
            sorted[((ulong)(uint)SortState(all[i]) << 32) | (uint)i] = all[i];
        }
        foreach (var a in sorted.Values) rows.Add(Row(a));
        if (rows.Count == 0) rows.Add(new Dictionary<string, object> { ["state"] = "empty", ["search"] = 1 });
        return rows;
    }

    /// <summary>FUN_1418aa660 (:759): a trophy a packet changed: the reward counts, then the summary or its list row
    /// (updateAchieve) when the selected category holds it.</summary>
    void UpdateOne(int id)
    {
        if (Host?.Entity == null) return;
        if (Book.Find(id) is not { } a) return;
        UpdateRewardCounts();
        if (Categories.FindById(CategoryId) is not { } cat) return;
        switch (cat.Tab)
        {
            case 1:
                Summary();
                break;
            case 0:
            case 2:
                if (cat.Matches(Book.DataOf(id)) && Host.Child("m_heroTrophy") is { } hero)
                    Host.Invoke(hero, "updateAchieve", Row(a));
                break;
        }
    }

    /// <summary>RequestSearch (:788): with a search text, the platform's check (KR's vfunc_37 answers 1 at once), then the
    /// page.</summary>
    void RequestSearch()
    {
        if (GetSearchText().Length == 0) return;
        Fill();
    }

    /// <summary>
    /// FUN_1418aa9b0 (:800): show a trophy: its name as the search text and its tree row selected; a closed dialog opens
    /// with the filter reset (FUN_141924680, <see cref="Open"/>), an open one resets the filter and redraws. Called by
    /// onClickAchieveDetail / onClickSummary and the dungeon room dialog's requirement link (uidungeonroomdialog.cpp:1454).
    /// </summary>
    public void ShowTrophy(int id)
    {
        SearchAtLoad = Texts.Name(id);
        TabIndex = TabOf(id);
        if (Categories.GetRow(TabIndex) is { } row) CategoryId = row.Id;
        if (Host?.Entity == null || IsClosed)
        {
            FilterIndexAtLoad = 0;
            Open?.Invoke();
        }
        else
        {
            SetFilterIndex(0);
            SetSearchText(SearchAtLoad);
            ApplyTabIndex();
            Fill();
        }
    }

    // ---- rows ----------------------------------------------------------------------------------------------------

    /// <summary>FUN_1418ab7e0 (:944): a list row; no members without a local player.</summary>
    public Dictionary<string, object> Row(Achieve a)
    {
        var row = new Dictionary<string, object>();
        if (!HasPlayer()) return row;
        uint item = a.Reward is { Type: 1 } r ? r.Code : 0;
        TitleRow(row, a);
        RewardLine(row, a);
        row["date"] = AchieveTexts.RecordedDate(a, a.CurGrade);
        row["state"] = Secret(a) ? "complete" : "normal";                 // GetAchieveStateName
        row["count"] = a.RewardCount;
        row["icon"] = a.Data.IconPath;                                       // GetName(0)
        row["bookmark"] = a.Bookmark ? 1 : 0;
        row["tooltipItem"] = item;
        row["expand"] = expanded.TryGetValue(a.Id, out int e) ? e : 0;      // FUN_1418acca0
        row["expandAble"] = 1;
        row["disabled"] = 0;                                                 // FUN_1418acce0: 0 either way
        CondList(row, a);
        ReqAchieve(row, a);
        Clears(row, a);
        return row;
    }

    /// <summary>FUN_1418ab520 (:911): id, desc, name, and the progress of the grade shown (or grade 11 for a secret one).</summary>
    void TitleRow(Dictionary<string, object> row, Achieve a)
    {
        if (a.Data == null) return;
        int g = a.State == 3 ? a.CurGrade : Hidden(a) ? a.CurGrade - 1 : a.StartGrade;
        row["id"] = (uint)a.Id;
        row["desc"] = DescOf(a, g);
        row["name"] = Texts.Name(a.Id);
        if (!Secret(a))
        {
            long max = a.GradeGoal(g);
            row["cur"] = Math.Min(a.Count, max);
            row["max"] = max;
            row["grade"] = g;
            row["progress"] = Texts.ProgressText(a, g);
            if (GmLevel() >= 50) row["debug"] = DebugMode();
        }
        else row["grade"] = 11;
    }

    /// <summary>FUN_1418ab000 (:872): "reward" = s_achieve_reward of the start grade's reward text, or
    /// s_achieve_reward_item_coloring for an item-coloring reward with feature 0xbb on, which also gives palette0,
    /// palette1, ... for the reward palette's colours of the trophy and its start grade (:881-903): paletteID, realColor
    /// (ch0), visibleColor (the palette colour), ch2Color (ch2) and colorSN, with show2color also realColor2 and
    /// visibleColor2 (both ch2). The colours are uints (FUN_1409c4990), colorSN an int and paletteID a uint.</summary>
    void RewardLine(Dictionary<string, object> row, Achieve a)
    {
        string r = Texts.StartRewardText(a.Data.Id);
        if (r.Length == 0) return;
        string text = Texts.CommonKeyF("s_achieve_reward", r);
        if (Texts.FeatureOn(0xbb) && a.Reward is { Type: 8 } reward)
        {
            text = Texts.CommonKeyF("s_achieve_reward_item_coloring", r);
            int index = 0;
            foreach (var c in Texts.RewardColors(a.Data.Id, a.StartGrade, reward.Code))
            {
                var palette = new Dictionary<string, object>
                {
                    ["paletteID"] = reward.Code, ["realColor"] = c.Ch0, ["visibleColor"] = c.Visible, ["ch2Color"] = c.Ch2,
                    ["colorSN"] = c.ColorSN,
                };
                if (c.Show2Color) { palette["realColor2"] = c.Ch2; palette["visibleColor2"] = c.Ch2; }
                row["palette" + index++.ToString(System.Globalization.CultureInfo.InvariantCulture)] = palette;
            }
        }
        row["reward"] = text;
    }

    /// <summary>FUN_1418ad560: the condition types whose ids are listed.</summary>
    static bool ListsConditions(int type) => type is 0x3b or 8 or 10 or 0x31 or 0xc5 or 0xd1 or 0x10a;

    /// <summary>AcCollectedIds (@0x1418adcd0): the code ids of the current grade's condition.</summary>
    static List<long> CollectedIds(Achieve a) => a.Data?.Grade(a.CurGrade)?.Condition?.Code.Ids;

    /// <summary>FUN_1418abaa0 (:973): reqCondition, each listed id's text and mark.</summary>
    void CondList(Dictionary<string, object> row, Achieve a)
    {
        int type = a.Condition.Type;
        if (!ListsConditions(type)) return;
        if (CollectedIds(a) is not { Count: > 0 } ids) return;
        var list = new List<object>();
        foreach (long id in ids)
            list.Add(new Dictionary<string, object> { ["condition"] = CondText(type, id), ["complete"] = CondDone(type, id) ? 1 : 0 });
        row["reqCondition"] = list;
    }

    /// <summary>FUN_1418ad5d0 (:1314).</summary>
    string CondText(int type, long id)
    {
        switch (type)
        {
            case 8: case 10: return Texts.TableString(1, id);
            case 0x3b: case 0x31: return QuestName(id);
            case 0xd1: return Texts.CommonKeyF("s_achieve_collectiong_fish_collect", Texts.TableString(0x1d, id));
            case 0x10a: return Texts.CommonKeyF("s_achieve_collectiong_pet_collect", Texts.TableString(0x3b, id));
            case 0xc5:
                if (Room(id) is { } room)
                    return Texts.CommonKeyF("s_achieve_collectiong_dungeon_clear", DungeonGroupName(room.GroupType), Texts.TableString(0x14, id));
                break;
        }
        return "";
    }

    /// <summary>FUN_1418ad960 (:1339).</summary>
    bool CondDone(int type, long id) => type switch
    {
        8 or 10 => ItemCollected(id),
        0x3b or 0x31 => QuestRewarded(id),
        0xd1 => FishCollected(id),
        0x10a => PetCollected(id),
        0xc5 => DungeonCleared(id),
        _ => false,
    };

    /// <summary>translateenum.cpp:50 TranslateEnumDungeonGroup's keys (the same list WorldMapController reads).</summary>
    internal static readonly string[] DungeonGroupKeys =
    {
        null, "normal", "raid", "chaosRaid", "reverseRaid", "lapenta", "guildRaid", "darkStream", "worldBoss", "item", "vip",
        "event", "fameChallenge", "colosseum", "turka",
    };

    /// <summary>TranslateEnumDungeonGroup: s_enum_dungeon_group_&lt;key&gt;, "" outside the list.</summary>
    string DungeonGroupName(int g) => g > 0 && g < DungeonGroupKeys.Length ? Texts.CommonByKey("s_enum_dungeon_group_" + DungeonGroupKeys[g]) : "";

    /// <summary>FUN_1418abc60 (:994): reqAchieve for the trophy-of-trophies condition types: each listed trophy, the grade it
    /// needs (the target's first value, or its max grade) and whether that grade is cleared.</summary>
    void ReqAchieve(Dictionary<string, object> row, Achieve a)
    {
        int type = a.Condition.Type;
        if (type is not (0x5e or 0x61 or 0x62 or 0x60)) return;
        if (a.Data?.Grade(a.CurGrade) is not { } grade) return;
        int want = (int)grade.Condition.Target.FirstValue;
        if (CollectedIds(a) is not { } ids) return;
        var list = new List<object>();
        foreach (long raw in ids)
        {
            int id = (int)raw;
            if (Book.DataOf(id) is not { } other) continue;
            int req = want == 0 ? other.MaxGrade : want;
            list.Add(new Dictionary<string, object>
            {
                ["id"] = (uint)id,
                ["trophy"] = Texts.Name(id),
                ["complete"] = Book.IsGradeCleared(id, req) ? 1 : 0,
                ["grade"] = req,
            });
        }
        row["reqAchieve"] = list;
    }

    /// <summary>FUN_1418abfa0 (:1028): clearAchieve, a mark per grade shown (a hidden trophy's up to the one before its
    /// current), with the reward kind's filter mark when not every kind is shown.</summary>
    void Clears(Dictionary<string, object> row, Achieve a)
    {
        if (!HasPlayer() || a.Data == null) return;
        int first = a.Data.FirstGrade, last = a.Data.MaxGrade;
        if (Hidden(a) && a.State != 3) last = a.CurGrade - 1;
        var list = new List<object>();
        for (int g = first; g <= last; g++)
        {
            var item = new Dictionary<string, object>
            {
                ["id"] = (uint)a.Id,
                ["grade"] = g,
                ["icon"] = a.Data.IconPath,
                ["complete"] = a.GetValue(g) != 0 ? 1 : 0,
            };
            if (a.Data.Grade(g) is { } gd && gd.Reward is { Type: not 0 } rw && !AllKinds && kinds.TryGetValue(rw.Type, out int c))
                item["checked"] = c;
            list.Add(item);
        }
        row["clearAchieve"] = list;
    }

    // ---- tests of a trophy ----------------------------------------------------------------------------------------

    /// <summary>FUN_1418ac320 (:1060): the index of the first tree row holding the trophy, -1 when none.</summary>
    int TabOf(int id)
    {
        var d = Book.DataOf(id);
        if (d == null) return -1;
        var rows = Categories.GetRows();
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Matches(d)) return i;
        return -1;
    }

    /// <summary>IsAchieveLocked (:1106).</summary>
    bool Locked(Achieve a)
    {
        if (DebugMode()) return false;
        if (a?.Data == null) return false;
        if (a.Data.Locking != 0) return true;
        return a.CheckRequire(EventEnabled, EventFound);
    }

    /// <summary>FUN_1418ac870 (:1123): a locked trophy that is done or has a reward waiting.</summary>
    bool Hidden(Achieve a) => Locked(a) && (a.State == 3 || a.CanRewardLocking || a.CanRewardRequire(EventEnabled, EventFound));

    /// <summary>FUN_1418ac8f0 (:1137): the list order group: a reward waiting 0, open 1, done 2.</summary>
    int SortState(Achieve a)
    {
        if (a.Data == null) return 0;
        if (a.RewardCount != 0) return 0;
        if (a.State == 3 || Hidden(a)) return 2;
        return 1;
    }

    /// <summary>FUN_1418aca90 (:1160): not reached, and hidden or with m_80 set.</summary>
    bool Secret(Achieve a) => !a.IsGradeReached && (Hidden(a) || a.Value80 >= 1);

    /// <summary>FUN_1418aac30 (:844): a secret trophy's last grade shows its current text, else the grade's requirement.</summary>
    string DescOf(Achieve a, int g) =>
        Secret(a) && g == a.Data.MaxGrade ? Texts.CurrentText(a) : Texts.RequirementText(a.Id, g, true);

    /// <summary>FUN_1418ace00 (:1216): whether a trophy goes in the list.</summary>
    public bool RowFilter(Achieve a, string search, FilterOptions o)
    {
        if (a?.Data == null) return false;
        var d = a.Data;
        int state = Hidden(a) ? 3 : a.State;
        if (d.Type == 8 && !DebugMode()) return false;
        if (!AllKinds)
        {
            bool shown = false;
            for (int g = d.FirstGrade; g <= d.MaxGrade && !shown; g++)
                if (d.Grade(g) is { } gd && kinds.TryGetValue(gd.Reward?.Type ?? 0, out int v) && v != 0) shown = true;
            if (!shown) return false;
        }
        if (o.Survival)
        {
            if (d.Type != 9) return false;
            if (!a.IsGradeReached && state != 3 && !DebugMode()) return false;
        }
        else if (d.Type == 9) return false;
        if (o.CategoryTag.Length != 0 && !d.CategoryTag.Contains(o.CategoryTag, StringComparison.Ordinal)) return false;
        if (o.ContentsTag.Length != 0 && !d.ContentsTag.Contains(o.ContentsTag, StringComparison.Ordinal)) return false;
        switch (o.Filter)
        {
            case 1: if (state != 2 || !a.IsStarted) return false; break;
            case 2: if (state != 3) return false; break;
            case 3: if (!a.Bookmark) return false; break;
        }
        if (search.Length != 0)
        {
            string title = Formats.World.ReservedWords.StripTags(Texts.Name(a.Id));
            string desc = Formats.World.ReservedWords.StripTags(DescOf(a, a.CurGrade));          // FUN_1418aabd0
            string extra = Formats.World.ReservedWords.StripTags(Texts.StartRewardText(a.Id));
            if (Find(title, search) == -1 && Find(desc, search) == -1 && Find(extra, search) == -1) return false;
        }
        if (Locked(a) && !Hidden(a)) return false;
        return true;
    }

    /// <summary>FUN_14083fec0: both lower-cased, then the first index.</summary>
    static int Find(string s, string sub) => s.ToLowerInvariant().IndexOf(sub.ToLowerInvariant(), StringComparison.Ordinal);

    // ---- movie members -------------------------------------------------------------------------------------------

    object Hero() => Host?.Child("m_heroTrophy");

    /// <summary>GetSearchText (:853): m_heroTrophy's m_searchInput text.</summary>
    string GetSearchText() => Hero() is { } h && Host.Child("m_searchInput", h) is { } i ? Rt.ToString(Host.Get(i, "text")) : "";

    /// <summary>SetSearchText (:863).</summary>
    void SetSearchText(string text)
    {
        if (Hero() is { } h && Host.Child("m_searchInput", h) is { } i) Host.Set(i, "text", text);
    }

    /// <summary>GetFilterIndex (:1078): the m_filter dropdown's index while shown, else 4 (no filter).</summary>
    int GetFilterIndex()
    {
        if (Hero() is { } h && Host.Child("m_filter", h) is { } f)
            return Rt.ToBoolean(Host.Get(f, "visible")) ? Rt.ToInt32(Host.Get(f, "selectedIndex")) : 4;
        return 4;
    }

    /// <summary>SetFilterIndex (:1089).</summary>
    void SetFilterIndex(int index)
    {
        if (Hero() is { } h && Host.Child("m_filter", h) is { } f) Host.Set(f, "selectedIndex", index);
    }

    /// <summary>ApplyTabIndex (:1098): setTabIndex(m_178).</summary>
    void ApplyTabIndex()
    {
        if (Host?.Entity == null) return;
        Host.Invoke("setTabIndex", TabIndex);
    }
}
