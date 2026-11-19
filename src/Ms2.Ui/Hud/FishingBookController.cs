using Ms2.Avm2;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIFishingBookDialog (UI id 0x4ee6, ms2/client/ms2/uifishingbookdialog.cpp, movie uifishingbookdialog): the fishing book,
/// opened by the upper HUD's fishing book button. It lists the fish of the tab's habitats (the book's fish only) twenty to a
/// page, sorted by fishing mastery, rank and name, with the player's catch records (<see cref="FishingBookData"/>, filled by
/// the Fishing packet), and shows one fish's details and the fisher's grade and title.
/// </summary>
/// <remarks>
/// The book sends nothing: it only reads the client's fishing state. STILL-OPEN: IsFishKnown is true in the client's debug
/// mode (CInputKeyHandler::IsDebugMode), which is <see cref="DebugMode"/> here, off.
/// STILL-OPEN: the descriptions keep their "$map:{id}$" words as the client's GetDescription leaves them; the movie's text
/// handling is not ported past ReplaceBoldWords.
/// STILL-OPEN: OnFishingResult (fishingmanager.cpp:1382), which mode 8 also runs: the result timer, CloseDialog 0x4ee7, the
/// guide trigger, the reward notice and the success sound; mode 7 also sets m_bFishing = 0. Only the records are kept here.
/// The base callbacks are covered: UiHost runs the UIDialog callback (DialogCallback) before OnCallback, and UIObject's
/// vfunc_49 (GetTooltip, @0x14131edf0) is empty in the client and in <see cref="UiController.GetTooltip"/>.
/// </remarks>
public sealed class FishingBookController : UiController
{
    public override string TypeName => "FishingBookDialog";          // GetTypeName (@0x140f412e0)
    public override string Movie => "uifishingbookdialog";
    protected override string EntityMethod => "setFisher";

    /// <summary>Slots per page (0x14, IsValidPage @0x140f42cd0).</summary>
    public const int PageSize = 20;
    /// <summary>The fisher titles, best first (GetBestFisherTitleId @0x140f42ed0): the title tag and the trophy that earns it.</summary>
    public static readonly (string Tag, int AchieveId)[] FisherTitles =
    {
        ("fisherChampion", 0x1607bb8), ("fisherPro", 0x1607bb7), ("fisherMaster", 0x1607aea),
        ("fisherExpert", 0x1607ae9), ("fisherIntermediate", 0x1607ae8), ("fisherBeginner", 0x1607ae7),
    };
    public const string OpenSound = "System_Fishing_BookOpen";       // OnLoadComplete @0x140f41330

    public FishingBookData Data = new();
    /// <summary>HasTitle(tag): the player owns the title with that tag's id.</summary>
    public Func<uint, bool> HasTitleId = _ => false;
    /// <summary>CAchieveManager::IsCompleted(id).</summary>
    public Func<int, bool> AchieveCompleted = _ => false;
    /// <summary>CInputKeyHandler::IsDebugMode.</summary>
    public bool DebugMode;
    /// <summary>CReservedWordTable::ReplaceBoldWords.</summary>
    public Func<string, string> ReplaceBoldWords = s => s;
    /// <summary>CFieldData::FindField(id) != null, for the habitat maps of a description.</summary>
    public Func<uint, bool> MapExists = _ => true;
    /// <summary>PlaySystemSound(name, "").</summary>
    public event Action<string> PlaySound;

    int page, tab;                   // m_nPage (+0x178), m_nTab (+0x17c)

    FishTable Table => Model.Tables.Fish;
    FishingSpots Mastery => Model.Tables.Fishing;
    string Common(string key) => Model.Tables.Common(key);
    bool Loaded => Host?.Entity != null;       // the movie is there (m_30)

    public int Page => page;
    public int Tab => tab;

    /// <summary>
    /// OnLoadComplete (@0x140f41330): UIDialog's; page 0, tab 0; the tab filled; SetFisher; the bag's first tile selected;
    /// the opening sound.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        page = 0;
        tab = 0;
        Fill(tab);
        SetFisher();
        if (Host.Child("m_bag") is { } bag) Host.Set(bag, "selectedIndex", 0);     // ASTileList::SetSelectedIndex(0)
        PlaySound?.Invoke(OpenSound);
    }

    /// <summary>
    /// vfunc_28 (@0x140f41440), after UIDialog's: onChangeFish (SetFish(PopUInt)), onTabChange (the tab, page 0, filled),
    /// onPrevPage and onNextPage (the page moved when the tab has it, filled). Not handled (0) either way.
    /// </summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (name.Equals("onChangeFish", StringComparison.OrdinalIgnoreCase))
        {
            if (args is { Length: > 0 }) SetFish(Rt.ToUint32(args[0]));
        }
        else if (name.Equals("onTabChange", StringComparison.OrdinalIgnoreCase))
        {
            if (args is { Length: > 0 })
            {
                tab = Rt.ToInt32(args[0]);
                page = 0;
                Fill(tab);
            }
        }
        else if (name.Equals("onPrevPage", StringComparison.OrdinalIgnoreCase))
        {
            if (IsValidPage(page - 1)) { page--; Fill(tab); }
        }
        else if (name.Equals("onNextPage", StringComparison.OrdinalIgnoreCase))
        {
            if (IsValidPage(page + 1)) { page++; Fill(tab); }
        }
        return false;
    }

    /// <summary>
    /// vfunc_49 (@0x140f416b0), after UIObject's, with a local player: the fishing mastery's progress in the grade
    /// (s_fishing_mastery_tooltip_level_max at the last grade, else common string 0xbeb with the mastery gained in the grade and
    /// the grade's span) as a text tooltip, onDelay 300, offDelay 60000, align 1.
    /// </summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        if (!Model.HasLocalPlayer) return null;
        uint exp = Pc.FishingMastery;
        int grade = Mastery.Grade(exp);
        string text;
        if (grade == Mastery.MaxGrade) text = Common("s_fishing_mastery_tooltip_level_max");
        else
        {
            uint baseValue = Mastery.Value(grade);
            uint span = Mastery.Value(grade + 1) - baseValue;        // unsigned int arithmetic as the client's
            text = ClientFormat.Format(Model.Tables.Common(0xbeb), exp - baseValue, span);     // BlockFormat(0xbeb, nCur, nMax)
        }
        var tip = new AsTooltip();
        tip["tooltip"] = "export_common_tooltip_tooltipText";
        tip["onDelay"] = 300;
        tip["offDelay"] = 60000;
        tip.SetTooltipAlign(1);
        tip["text"] = text;
        return tip;
    }

    /// <summary>vfunc_67 (@0x140f41940): UIDialog's close, then every fish record is no longer new.</summary>
    public override void Close()
    {
        base.Close();
        Data.ClearNew();
    }

    /// <summary>RefreshCurrentTab (@0x140f419e0): the current tab filled again (a catch result while the book is open).</summary>
    public void RefreshCurrentTab()
    {
        if (Loaded) Fill(tab);
    }

    /// <summary>
    /// SetFisher (@0x140f41a10), with a local player and the movie: setFisher({fisherTitleID, fisherTitle, fisherGradeName,
    /// curMastery, maxMastery}), the mastery in its grade and the grade's span (the title name, or s_fishing_none_title without
    /// one; the grade name, with s_fishing_mastery_gauge_level_max at the last grade).
    /// </summary>
    public void SetFisher()
    {
        if (!Model.HasLocalPlayer || !Loaded) return;
        Host.Invoke("setFisher", FisherObject());
    }

    /// <summary>The object SetFisher sends.</summary>
    public AsObject FisherObject()
    {
        uint exp = Pc.FishingMastery;
        int grade = Mastery.Grade(exp);
        uint baseValue = Mastery.Value(grade);
        uint nextBase = Mastery.Value(grade + 1);
        uint title = BestFisherTitleId();
        string strTitle = title == 0 ? Common("s_fishing_none_title") : Model.Tables.TitleName((int)title);
        string strGrade = GradeName(grade);
        if (grade == Mastery.MaxGrade) strGrade += " " + Common("s_fishing_mastery_gauge_level_max");
        return Host.Obj(("fisherTitleID", title), ("fisherTitle", strTitle), ("fisherGradeName", strGrade),
            ("curMastery", exp - baseValue), ("maxMastery", nextBase - baseValue));
    }

    /// <summary>
    /// SetTabNew (@0x140f41e90), with the movie: for each tab 0 to 3 setTabNew(tab, any new fish of its habitats): habitat 0 is
    /// tab 0, 1 is tab 1, 7 is tab 3, any other tab 2. Only the fish with data count.
    /// </summary>
    public void SetTabNew()
    {
        if (!Loaded) return;
        var isNew = new bool[4];
        foreach (var f in Data.All)
        {
            if (!f.IsNew || Table.Get(f.Id) is not { } data) continue;
            isNew[data.Habitat switch { 0 => 0, 1 => 1, 7 => 3, _ => 2 }] = true;
        }
        for (int j = 0; j < 4; j++) Host.Invoke("setTabNew", j, isNew[j]);
    }

    /// <summary>
    /// SetFish (@0x140f42100), with the movie: setFish(details) of the fish, an empty object for an unknown id. The fish's
    /// icon, name, fishing area (the mastery grade's name, or s_fishing_all_spot when it ignores the spot's), rank, the rank's
    /// name in common string 0xbda, the description, whether it is known and its medal; known fish add the total and big
    /// catch counts (0xbdb) and the best record (0xbdc).
    /// </summary>
    public void SetFish(uint fishId)
    {
        if (!Loaded) return;
        Host.Invoke("setFish", FishObject(fishId));
    }

    /// <summary>The object SetFish sends: the fish's data, or for an id without data the else branch of SetFish
    /// (uifishingbookdialog.cpp), {acquire: 0, medal: 0}.</summary>
    public AsObject FishObject(uint fishId)
    {
        if (Table.Get(fishId) is { } data)
        {
            int area = Mastery.Grade(data.Mastery);
            bool known = IsFishKnown(fishId);
            var members = new List<(string, object)>
            {
                ("iconPath", data.IconPath),
                ("fishName", Model.Tables.StringTableText(0x1d, fishId)),
                ("fishingExp", data.IgnoreSpotMastery ? Common("s_fishing_all_spot") : GradeName(area)),
                ("fishRank", data.Rank),
                ("fishGradeName", ClientFormat.Format(Model.Tables.Common(0xbda), RankName(data.Rank))),
                ("desc", Description(data, known)),
                ("acquire", known ? 1 : 0),
                ("medal", known ? FishMedal(fishId) : 0),
            };
            if (known)
            {
                members.Add(("totalCatchCount", ClientFormat.Format(Model.Tables.Common(0xbdb), TotalCatchCount(fishId))));
                members.Add(("bigSizeCatchCount", ClientFormat.Format(Model.Tables.Common(0xbdb), BigSizeCatchCount(fishId))));
                members.Add(("bestRecord", ClientFormat.Format(Model.Tables.Common(0xbdc), BestRecord(fishId))));
            }
            return Host.Obj(members.ToArray());
        }
        return Host.Obj(("acquire", 0), ("medal", 0));
    }

    /// <summary>
    /// FUN_140f42790 (@0x140f42790): the tab's fish sorted (<see cref="SortedIds"/>), the bag emptied and given the page's
    /// twenty tiles (a fish's id, icon, acquire, grade (rank), medal and isNew; an empty object past the last fish), then
    /// invalidated; the page number in "m_page" and SetTabNew.
    /// </summary>
    void Fill(int tabIndex)
    {
        if (!Loaded) return;
        var ids = SortedIds(tabIndex);
        if (Host.Child("m_bag") is { } bag)
        {
            var dp = Host.Get(bag, "dataProvider");
            if (!Rt.IsNullish(dp)) Host.Invoke(dp, "cleanUp");
            for (int i = 0; i < PageSize; i++)
            {
                int index = page * PageSize + i;
                AsObject tile = null;
                if (index >= 0 && index < ids.Count && Table.Get(ids[index]) is { } data)
                    tile = Host.Obj(("id", data.Id), ("iconPath", data.IconPath), ("acquire", IsFishKnown(data.Id) ? 1 : 0),
                        ("grade", data.Rank), ("medal", FishMedal(data.Id)), ("isNew", IsFishNew(data.Id) ? 1 : 0));
                if (!Rt.IsNullish(dp)) Host.Invoke(dp, "push", tile ?? Host.Obj());
            }
            Host.Invoke(bag, "invalidate");        // vfunc_24(true)
            Host.Invoke(bag, "validateNow");
        }
        if (Host.Child("m_page") is { } text) Host.Set(text, "text", ClientFormat.Format("{0}", page + 1));
        SetTabNew();
    }

    /// <summary>GetCurrentTabFishIds (@0x140f42d90), then qsort with CompareFishIds: tab 0 habitat 0, tab 1 habitat 1, tab 2
    /// habitats 2 to 6, another tab habitat 7; the book's fish only.</summary>
    public List<uint> SortedIds(int tabIndex)
    {
        var ids = TabIds(tabIndex);
        CrtQsort.Sort(ids, CompareFishIds);
        return ids;
    }

    List<uint> TabIds(int tabIndex)
    {
        var ids = new List<uint>();
        switch (tabIndex)
        {
            case 0: ids.AddRange(Table.IdsByHabitat(0, true)); break;
            case 1: ids.AddRange(Table.IdsByHabitat(1, true)); break;
            case 2:
                for (int h = 2; h <= 6; h++) ids.AddRange(Table.IdsByHabitat(h, true));
                break;
            default: ids.AddRange(Table.IdsByHabitat(7, true)); break;
        }
        return ids;
    }

    /// <summary>IsValidPage (@0x140f42cd0): the page is within ceil(fish / 20) pages of the current tab.</summary>
    public bool IsValidPage(int p)
    {
        int count = TabIds(tab).Count;
        int pages = count / PageSize + (count % PageSize != 0 ? 1 : 0);
        return p >= 0 && p < pages;
    }

    /// <summary>CompareFishIds (@0x140f438b0): 1 when either fish has no data; else by fishing mastery, then rank (ascending),
    /// then the names by wcscmp (UTF-16 code units).</summary>
    int CompareFishIds(uint a, uint b)
    {
        if (Table.Get(a) is not { } fa || Table.Get(b) is not { } fb) return 1;
        if (fa.Mastery != fb.Mastery) return fa.Mastery > fb.Mastery ? 1 : -1;
        if (fa.Rank != fb.Rank) return fa.Rank > fb.Rank ? 1 : -1;
        return string.CompareOrdinal(Model.Tables.StringTableText(0x1d, a), Model.Tables.StringTableText(0x1d, b));
    }

    /// <summary>GetBestFisherTitleId (@0x140f42ed0): 0 without a local player; else the first of <see cref="FisherTitles"/> whose
    /// title the player has or whose trophy is completed, as the title table's id of its tag.</summary>
    public uint BestFisherTitleId()
    {
        if (!Model.HasLocalPlayer) return 0;
        foreach (var (tag, achieveId) in FisherTitles)
        {
            uint titleId = Model.Tables.TitleTags.FindTitleId(tag);
            if ((titleId != 0 && HasTitleId(titleId)) || AchieveCompleted(achieveId)) return titleId;
        }
        return 0;
    }

    /// <summary>GetGradeName (@0x140f432e0) = FUN_1403b2240: common string 0xbba + n, s_fishing_grade_Lv{n} for n &gt; 0.</summary>
    public string GradeName(int n) => Common(FishingSpots.LevelKey(n));

    /// <summary>CStringTable::GetRankName (@0x141813150): common string 0x4f9 for 0, else s_word_rank_{n:00}.</summary>
    string RankName(int rank) => rank == 0 ? Model.Tables.Common(0x4f9) : Common("s_word_rank_" + rank.ToString("00"));

    /// <summary>IsFishKnown (@0x140f43330): true in the debug mode, else a record of the fish or its companion.</summary>
    public bool IsFishKnown(uint id) => DebugMode || Data.Has(Table, id);

    /// <summary>IsFishNew (@0x140f43380): the fish's record or its companion's is new.</summary>
    public bool IsFishNew(uint id)
        => (Data.Find(id) is { IsNew: true }) || (Data.Find(CompanionId(id)) is { IsNew: true });

    /// <summary>GetCompanionFishId (@0x140f434b0): 0 without fish data.</summary>
    public uint CompanionId(uint id) => Table.Get(id)?.Companion ?? 0;

    /// <summary>GetFishMedal (@0x140f43540): 0 without fish data, else the better size grade of the fish and its companion.</summary>
    public int FishMedal(uint id)
        => Table.Get(id) is { } data ? Math.Max(Data.SizeGrade(Table, id), Data.SizeGrade(Table, data.Companion)) : 0;

    /// <summary>GetTotalCatchCount (@0x140f43610): the fish's count and its companion's.</summary>
    public int TotalCatchCount(uint id) => (Data.Find(id)?.TotalCount ?? 0) + (Data.Find(CompanionId(id))?.TotalCount ?? 0);

    /// <summary>GetBigSizeCatchCount (@0x140f436f0).</summary>
    public int BigSizeCatchCount(uint id) => (Data.Find(id)?.BigCount ?? 0) + (Data.Find(CompanionId(id))?.BigCount ?? 0);

    /// <summary>GetBestRecord (@0x140f437d0): the larger best size of the fish and its companion.</summary>
    public int BestRecord(uint id)
        => (int)Math.Max(Data.Find(id)?.Size ?? 0, Data.Find(CompanionId(id))?.Size ?? 0);

    /// <summary>CFishData::GetDescription (@0x1403b16b0): not for a habitat of 7 the habitat line (s_fishing_fish_habitat, the
    /// first three maps and s_fishing_fish_habitat_more, or s_fishing_fish_habitat_unknown), then the detail text (string table
    /// 0x1f) of a known fish or the short one (0x1e), ReplaceBoldWords over all.</summary>
    string Description(FishData data, bool detail)
    {
        string s = "";
        if (data.Habitat != 7)
        {
            s = Common("s_fishing_fish_habitat");
            string maps = Table.MapList(data.Id, MapExists);
            s += maps.Length != 0 ? maps + Common("s_fishing_fish_habitat_more") : Common("s_fishing_fish_habitat_unknown");
        }
        s += Model.Tables.StringTableText(detail ? 0x1f : 0x1e, data.Id);
        return ReplaceBoldWords(s);
    }
}
