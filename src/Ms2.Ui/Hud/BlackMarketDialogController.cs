using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The C++ side of the black market, UIBlackMarketDialog (client/ms2/uiblackmarketdialog.cpp; UI id 0x4e97) and its item
/// pages (uiblackmarketpage.cpp), for the movie uiblackmarketdialog. Pages in the ctor's order: 0 the item search
/// (BlackMarketPageItemSearch), 1 the item register (BlackMarketPageItemRegister), 2..4 the meso pages.
/// Every send goes through <see cref="BlackMarketBook"/>; nothing local changes before the server answers.
/// STILL-OPEN: the meso pages (2..4: SendMesoMarket, the meso detail button of feature 0xf4) and mode 6 are not ported, so
/// those tabs show no page; the tooltips of vfunc_45 (getRegisterItemTooltip, getEquipmentItemTooltip, OnSlotShiftClick)
/// and the name candidates of vfunc_74; the trade-limited buy text (s_msg_blackmarket_ask_buy_limit_tradecount) has no
/// trade-count source (<see cref="TradeCount"/>); the register page's total price text (FUN_141275860, feature 0xc5) and
/// its changeTotalPrice listener; the fee event dates of box 0x949 and the meso limits of the terms text 0x9d9.
/// </summary>
public sealed class BlackMarketDialogController : UiController, IBlackMarketDialog
{
    public override string Movie => "uiblackmarketdialog";
    protected override string EntityMethod => "onProcessKey";
    /// <summary>GetTypeName.</summary>
    public override string TypeName => "BlackMarketDialog";

    public BlackMarketBook Book { get; }
    /// <summary>The black market tab and option tables (vfunc_65's table Load).</summary>
    public BlackMarketTable Table = BlackMarketTable.Empty;

    // ---- hooks ------------------------------------------------------------------------------------------------------

    /// <summary>The local player's account id (the seller test of OnClickBuy and the rows' myItem).</summary>
    public Func<long> OwnAccountId = () => 0;
    /// <summary>GetServerTime, unix seconds.</summary>
    public Func<long> ServerTime = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    /// <summary>timeGetTime for the search page's tick (m_tick).</summary>
    public Func<long> Milliseconds = () => Environment.TickCount64;
    /// <summary>CFeatureManager::GetLevelCap (the search page's initData maxLevel).</summary>
    public Func<int> LevelCap = () => 99;
    /// <summary>CFeatureManager::IsEnabled(feature id).</summary>
    public Func<int, bool> FeatureEnabled = _ => false;
    /// <summary>The preview HUD test of OnClickBuy and OnRightClick (FUN_14199a750).</summary>
    public Func<bool> PreviewHudOn = () => false;
    /// <summary>Content shutdown of kind 7 (vfunc_73).</summary>
    public Func<bool> ContentShutdown = () => false;
    /// <summary>The local player is dead (vfunc_72 / vfunc_73).</summary>
    public Func<bool> IsDead = () => false;
    /// <summary>FUN_141275260(text, 1, 1): a system notice (also to the chat log).</summary>
    public Action<string> Notice = _ => { };
    /// <summary>CBlockManager's notice of a blocked action (the block notice of the action id).</summary>
    public Action<int> BlockNotice = _ => { };
    /// <summary>FUN_14151f150: show a message box.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>ShowMessageBox(text, 0, 0, ""): a plain box.</summary>
    public Action<string> ShowText = t => MessageBoxQueue.ShowMessageBox(t, 0, null, "");
    /// <summary>ShowTermsMessageBoxWithCallback(bit, title, text, callback).</summary>
    public Action<int, string, string, Func<int, MessageBoxData, bool>> ShowTerms =
        (bit, title, text, cb) => MessageBoxQueue.ShowTermsWithCallback(bit, title, text, cb);
    /// <summary>FUN_140d74e50(bit): the terms bit agreed.</summary>
    public Action<int> AgreeTerms = MessageBoxQueue.Agree;
    /// <summary>The item message (CItemMessage: an item with a text, as the item get notice).</summary>
    public Action<Item, string> ItemMessage = (_, _) => { };
    /// <summary>PlaySound2D of a UI sound key.</summary>
    public Action<string> Sound = _ => { };
    /// <summary>SendNpcTalkMode0 (vfunc_67).</summary>
    public Action SendNpcTalkEnd = () => { };
    /// <summary>FindBinding(slot)->GetInvenSlot()->GetItemUid(): the item uid of a bag slot binding, 0 for none.</summary>
    public Func<string, long> SlotItemUid = _ => 0;
    /// <summary>InvenSlot::CanRegisterToBlackMarket. STILL-OPEN: not ported; false keeps every slot out.</summary>
    public Func<Item, bool> CanRegister = _ => false;
    /// <summary>JobOffered (FUN_140222be0's filter) of a job code.</summary>
    public Func<int, bool> JobOffered = code => code switch { 1 or 90 or 100 or 110 => false, _ => true };
    /// <summary>String table 0x4d: a job's name.</summary>
    public Func<int, string> JobName = _ => "";
    /// <summary>CStringTable::GetRankName.</summary>
    public Func<int, string> RankName = _ => "";
    /// <summary>Client::Constants BlackMarketSellEndDay.</summary>
    public Func<int> SellEndDay = () => 7;
    /// <summary>The trade count of a trade-limited item, or null when the item is not trade limited. STILL-OPEN: the client's
    /// test and count are not ported; null always takes the plain buy text 0x947.</summary>
    public Func<Item, int?> TradeCount = _ => null;
    /// <summary>A unix time as local time (the period text of the own rows).</summary>
    public Func<long, DateTime> LocalTime = t => DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime;

    // ---- the dialog -------------------------------------------------------------------------------------------------

    readonly SearchPage search;
    readonly RegisterPage register;
    /// <summary>m_curPage: the page index, -1 for none.</summary>
    int curPage = -1;

    public BlackMarketDialogController(BlackMarketBook book)
    {
        Book = book;
        search = new SearchPage(this);
        register = new RegisterPage(this);
    }

    string Common(int id) => Book.Common(id);
    string CommonKey(string k) => Book.CommonKey(k);
    static string Grp(long n) => Thousands(n);
    AsObject Root => Host?.Entity;

    /// <summary>The page shown: 0 the search, 1 the register, -1 none or a meso page.</summary>
    public int CurrentPage => curPage;

    /// <summary>vfunc_73 (@0x1418cf770), can open: content shutdown 7 → notice 0x2f7; dead → the dead notice; else true.</summary>
    public bool CanOpen()
    {
        if (ContentShutdown()) { Notice(Common(0x2f7)); return false; }
        if (IsDead()) { Notice(CommonKey("s_blackmarket_error_not_useable_by_dead")); return false; }
        return true;
    }

    /// <summary>vfunc_65 (@0x1418cf620) after the base open succeeds: SendBlackMarketMode1. STILL-OPEN: SendMesoMarket.</summary>
    public void Opened() => Book.SendOwnList();

    /// <summary>vfunc_72: dead while open → the dead notice and close.</summary>
    public void CheckDead()
    {
        if (!IsDead() || IsClosed) return;
        Notice(CommonKey("s_blackmarket_error_not_useable_by_dead"));
        Close();
    }

    /// <summary>OnLoadComplete (uiblackmarketdialog.cpp:222): the base, the meso tab hidden without feature 0x15, each
    /// page's SetRoot + OnInit, then the root's initData.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (!FeatureEnabled(0x15)) Host.SetChildVisible("m_radioMeso", false);
        search.OnInit();
        register.OnInit();
        Host.Invoke("initData");
    }

    /// <summary>vfunc_67 (@0x1418cf680): the base close, no page, each page's vfunc_2, SendNpcTalkMode0.</summary>
    public override void Close()
    {
        base.Close();
        curPage = -1;
        search.Shutdown();
        register.Shutdown();
        SendNpcTalkEnd();
    }

    /// <summary>vfunc_28: "onChangeTab" → FUN_1418d0c20(index); every other call goes to the current page's vfunc_4.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (name.Equals("onChangeTab", StringComparison.OrdinalIgnoreCase))
        {
            ChangeTab(Rt.ToInt32(Arg(args, 0)));
            return true;
        }
        // The client's FUN_1412761e0(root, 0) takes the mouse from the whole dialog until the answer, so no click reaches
        // these callbacks while locked. Our runtime's hit test still lets clicks reach the children of a root with
        // mouseChildren and mouseEnabled both off (logged in the AS3 runtime gaps), so the locked clicks are dropped here.
        if (Locked && IsLockedClick(name)) return true;
        return curPage switch
        {
            0 => search.OnCallback(name, args),
            1 => register.OnCallback(name, args),
            _ => false,
        };
    }

    static bool IsLockedClick(string name) =>
        name.Equals("OnClickBuy", StringComparison.OrdinalIgnoreCase)
        || name.Equals("OnClickCancelSell", StringComparison.OrdinalIgnoreCase)
        || name.Equals("OnClickRegister", StringComparison.OrdinalIgnoreCase);

    /// <summary>FUN_1418d0c20: 0 &lt;= index &lt; 5 → that page current and its vfunc_3. STILL-OPEN: pages 2..4 (meso).</summary>
    void ChangeTab(int index)
    {
        if (index < 0 || index >= 5) return;
        curPage = index;
        if (index == 0) search.Show();
        else if (index == 1) register.Show();
    }

    /// <summary>vfunc_15: the current page's vfunc_5 each frame.</summary>
    public void Process()
    {
        if (Root == null || IsClosed) return;
        if (curPage == 0) search.Tick();
        else if (curPage == 1) register.Tick();
    }

    /// <summary>vfunc_13: Enter → onProcessKey("onEnterKey"), Tab → onProcessKey("onTabKey").</summary>
    public void Key(bool enter) => Host.Invoke("onProcessKey", (object)(enter ? "onEnterKey" : "onTabKey"));

    /// <summary>OnRightClick with the preview HUD off: clearRegisterPageSlot.</summary>
    public void RightClick() { if (!PreviewHudOn()) Host.Invoke("clearRegisterPageSlot"); }

    // ---- IBlackMarketDialog (the manager's calls) ------------------------------------------------------------------

    bool Down => Host == null || IsClosed;   // vfunc_55

    public void BuildOwnerItemList(IReadOnlyList<BlackMarketEntry> own) { if (!Down) register.BuildOwnerItemList(own); }
    public void OwnAdded(BlackMarketEntry entry) { if (!Down) register.OwnAdded(entry); }
    public void OwnRemoved(BlackMarketEntry entry, int reason) { if (!Down) register.OwnRemoved(entry, reason); }
    public void BuildItemList(int first, int end, int tail) { if (!Down) search.BuildItemList(first, end, tail); }

    /// <summary>FUN_1418d1080: a new item of the product's id and rarity with the count bought, ItemMessage 0x945, and the
    /// search page's UpdateItem(entry, GetState(false)).</summary>
    public void Bought(BlackMarketEntry entry, int count)
    {
        if (Down || entry?.Item == null) return;
        var item = Item.Create(Book.ItemData, entry.Item.ItemId, entry.Item.Rarity);
        if (item != null) { item.Count = (uint)count; ItemMessage(item, Common(0x945)); }
        search.UpdateItem(entry, entry.GetState(false, ServerTime()));
    }

    public void ReferencePrice(int itemId, int rarity, long price) { if (!Down && curPage == 1) register.ReferencePrice(itemId, rarity, price); }
    public void SearchNotice(int type, int result, long value) { if (!Down) search.SearchNotice(type, result, value); }

    /// <summary>vfunc_79: the register page's OnReplyReceived, then the search page's.</summary>
    public void ReplyReceived(int action)
    {
        if (Down) return;
        register.OnReplyReceived(action);
        search.OnReplyReceived(action);
    }

    // ---- shared helpers ---------------------------------------------------------------------------------------------

    static object Arg(object[] a, int i) => a != null && i < a.Length ? a[i] : null;
    static string Str(object[] a, int i) => Arg(a, i) is { } v && !Rt.IsNullish(v) ? Rt.ToString(v) : "";

    /// <summary>FUN_1412761e0(root, b): the root's mouseEnabled and mouseChildren.</summary>
    void Lock(bool enable)
    {
        if (Root == null) return;
        Host.Set(Root, "mouseChildren", enable);
        Host.Set(Root, "mouseEnabled", enable);
    }

    /// <summary>Whether the root is locked (FUN_1412761e0(root, 0)).</summary>
    public bool Locked => Root != null && !Rt.ToBoolean(Host.Get(Root, "mouseChildren"));

    /// <summary>The item's name in its rarity colour (SetName with colour).</summary>
    string ColoredName(Item item) =>
        $"<FONT color='#{Model.Tables.Items.TextColor(item.Rarity):x}'>{Book.ItemName(item.ItemId)}</FONT>";

    /// <summary>_wcstoui64 / _wcstoi64 of a movie string: blanks, a sign, the digits; 0 without digits.</summary>
    public static long ParseInt64(string s)
    {
        s ??= "";
        int i = 0; bool neg = false; long v = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++)
            v = v > (long.MaxValue - 9) / 10 ? long.MaxValue : v * 10 + (s[i] - '0');
        return neg ? -v : v;
    }

    /// <summary>StateName: 1 wait, 2 sale, 3 expired, 4 soldout, 5 hide.</summary>
    public static string StateName(int st) => st switch
    {
        1 => "wait", 2 => "sale", 3 => "expired", 4 => "soldout", 5 => "hide", _ => "",
    };

    /// <summary>FUN_140f156d0: "level" up 0xb / down 0xc, "price" up 0x15 / down 0x16, else 0.</summary>
    public static byte SortCode(string sort, bool up) => sort switch
    {
        "level" => (byte)(up ? 0xb : 0xc),
        "price" => (byte)(up ? 0x15 : 0x16),
        _ => 0,
    };

    /// <summary>FUN_1402227e0: the job group mask of a job code (200 or <paramref name="all"/>: every job).</summary>
    public static int JobMask(int job, bool all)
    {
        if (all || job == 200) return 0xfff;
        return job switch
        {
            1 => 1, 10 => 2, 20 => 4, 30 => 8, 40 => 0x10, 50 => 0x20, 60 => 0x40, 70 => 0x80,
            80 => 0x100, 90 => 0x200, 100 => 0x400, 110 => 0x800, _ => 0,
        };
    }

    /// <summary>The minor category of a category (FUN_1401bd010): 30000 → 99999, a major (n00) → n99, else itself.</summary>
    public static int MinorCategory(int cat) => cat == 30000 ? 99999 : cat % 100 == 0 ? cat + 99 : cat;

    /// <summary>The stat key (%01d%01d%03d of group, percent, index) as the condition's stat min.</summary>
    public static int OptionKey(int group, bool percent, int idx) =>
        int.Parse(group.ToString("D1", CultureInfo.InvariantCulture) + (percent ? "1" : "0")
            + idx.ToString("D3", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>A percent stat's search value: (int)(100 × value + 1e-6) rounded to tens (a rest of 5 up), then 5 below it
    /// (or 5 above when that rounding went up past the value); not below 0.</summary>
    public static int OptionPercentValue(double value)
    {
        int s = (int)(100 * (double)(float)value + 1e-06);
        int rem = s % 10, round = s - rem, step;
        if (s != round)
        {
            round = rem >= 5 ? round + 10 : round;
            step = s < round ? -5 : 5;
        }
        else step = -5;
        return Math.Max(0, round + step);
    }

    /// <summary>The job codes of FUN_140222be0.</summary>
    static readonly int[] JobCodes = { 1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110 };

    /// <summary>
    /// FUN_140f14ce0: a product row. No entry or item → false; the slot data, the listing id, the coloured name, the level
    /// limit, the price of what remains, the unit price when the item stacks (0x91b), the state; own rows (<paramref
    /// name="manage"/>) also the listing time and, on sale, the end of the period (0x91d); others myItem when the seller
    /// is the player.
    /// </summary>
    bool Row(Dictionary<string, object> obj, BlackMarketEntry e, bool manage)
    {
        if (e?.Item == null) return false;
        if (!Model.HasLocalPlayer) return true;
        var item = e.Item;
        var slot = new List<(string, object)>();
        ItemSlot.Build(slot, item, true, 0x4415, Model, Model.ItemSlotSources);
        obj["slotData"] = slot.ToDictionary(p => p.Item1, p => p.Item2);
        obj["marketSN"] = ((ulong)e.Id).ToString(CultureInfo.InvariantCulture);
        obj["name"] = ColoredName(item);
        obj["level"] = item.Data.LevelLimit;
        obj["price"] = Grp(e.Product.PriceFor(e.Remain));
        if (item.Data.SlotMax > 1) obj["price_1"] = ClientFormat.Format(Common(0x91b), Grp(e.Product.UnitPrice));
        int st = e.GetState(manage, ServerTime());
        obj["saleState"] = StateName(st);
        if (manage)
        {
            obj["manage"] = true;
            obj["createtime"] = e.Product.ListedTime.ToString(CultureInfo.InvariantCulture);
            if (st == 2)
            {
                var t = LocalTime(e.Product.ExpireTime);
                obj["period"] = ClientFormat.Format(Common(0x91d), t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second);
            }
        }
        else if (e.Product.SellerAccountId == OwnAccountId()) obj["myItem"] = true;
        return true;
    }

    // ---- BlackMarketRequest (uiblackmarketpage.cpp:225) -------------------------------------------------------------

    sealed class Request
    {
        public int Action, Count;
        public long Key;
        public string Keyword = "";
        public void SetProduct(int action, long key, int count) { Action = action; Key = key; Count = count; }
        public void SetKeyword(int action, string keyword) { Action = action; Keyword = keyword; }
        public void Reset() { Action = 0; Key = 0; Count = 0; Keyword = ""; }
        /// <summary>Done(action): the pending action answered → reset, true.</summary>
        public bool Done(int action)
        {
            if (Action != action) return false;
            Reset();
            return true;
        }
    }

    // ---- the search page (BlackMarketPageItemSearch) ----------------------------------------------------------------

    sealed class SearchPage
    {
        readonly BlackMarketDialogController d;
        readonly Request request = new();
        readonly List<BlackMarketTable.Tab> tabs = new();
        object page, itemList;
        BlackMarketSearch cond;
        int pending, optionId = -1;
        bool sendPending, optionsBuilt;
        long tick = long.MinValue / 2;

        public SearchPage(BlackMarketDialogController d) => this.d = d;
        UiHost Host => d.Host;

        /// <summary>OnInit: m_searchPage and its m_itemList; the page's initData (maxLevel, enableSocketSystem 0x2e).</summary>
        public void OnInit()
        {
            page = Host.Child("m_searchPage");
            itemList = page != null ? Host.Child("m_itemList", page) : null;
            Host.Invoke(page, "initData", Host.Obj(("maxLevel", d.LevelCap()), ("enableSocketSystem", d.FeatureEnabled(0x2e))));
        }

        /// <summary>vfunc_2: everything reset, option id -1, the tabs cleared.</summary>
        public void Shutdown()
        {
            page = itemList = null;
            request.Reset();
            cond = null; pending = 0; sendPending = false; optionId = -1; optionsBuilt = false;
            tabs.Clear();
        }

        /// <summary>vfunc_3: the option list once.</summary>
        public void Show()
        {
            if (optionsBuilt) return;
            optionsBuilt = true;
            BuildSearchOptionList();
        }

        bool CanSearchNow => (uint)d.Milliseconds() >= tick + 1000;

        /// <summary>vfunc_5: a pending search goes out once the 1 s delay is over.</summary>
        public void Tick()
        {
            if (!sendPending || pending <= 0 || cond == null) return;
            if (!CanSearchNow) return;
            Host.Invoke("updateNoticeSymbolFrom", (object)"loading", page);
            d.Book.SendSearch(cond);
            tick = (uint)d.Milliseconds();
            sendPending = false;
        }

        public bool OnCallback(string name, object[] a)
        {
            switch (name.ToLowerInvariant())
            {
                case "onchangecategory":
                    FillSubTabArray(Arg(a, 1), Rt.ToInt32(Arg(a, 0)));
                    return true;
                case "onchangelowcategory":
                    BuildOptionLists(Arg(a, 1), Rt.ToInt32(Arg(a, 0)));
                    return true;
                case "onchangepage":
                {
                    bool r = false;
                    if (pending == 0 && CanSearchNow)
                    {
                        int first = Rt.ToInt32(Arg(a, 0)), count = Rt.ToInt32(Arg(a, 1));
                        int pos = first < d.Book.SearchProducts.Count ? first : -1;
                        Host.Invoke("buildPage", Rows(first, first + count, pos), count);
                        r = true;
                    }
                    Host.Set(Arg(a, 2), "result", r);
                    return true;
                }
                case "onchangelist":
                {
                    bool r = false;
                    if (pending == 0 && CanSearchNow && cond != null)
                    {
                        cond.Page = Rt.ToInt32(Arg(a, 0));
                        pending = cond.MajorCategory;
                        sendPending = true;
                        r = true;
                    }
                    Host.Set(Arg(a, 1), "result", r);
                    return true;
                }
                case "onclicksearch":
                    Host.Set(Arg(a, 1), "result", ReadSearchCondition(Arg(a, 0)));
                    return true;
                case "onclicksortbutton":
                {
                    bool r = false;
                    if (pending == 0 && CanSearchNow && cond != null)
                    {
                        cond.Page = 1;
                        cond.Sort = SortCode(Str(a, 0), Rt.ToBoolean(Arg(a, 1)));
                        pending = cond.MajorCategory;
                        sendPending = true;
                        r = true;
                    }
                    Host.Set(Arg(a, 2), "result", r);
                    return true;
                }
                case "onclickbuy":
                    if (d.PreviewHudOn()) return true;
                    if (d.Book.ActionBlocked(0x26)) { d.BlockNotice(0x26); return true; }
                    OnClickBuy(ParseInt64(Str(a, 0)), Str(a, 1));
                    return true;
            }
            return false;
        }

        /// <summary>AppendTabs: each tab as {label, id, priority} into the list and m_tabs, with its children when it shows them.</summary>
        void AppendTabs(List<object> list, IEnumerable<BlackMarketTable.Tab> from)
        {
            foreach (var t in from)
            {
                list.Add(new Dictionary<string, object> { ["label"] = t.Name, ["id"] = t.Id, ["priority"] = t.Priority });
                tabs.Add(t);
                if (t.ShowIncludeLowTab) AppendTabs(list, t.Children);
            }
        }

        /// <summary>BuildSearchOptionList: the categories, the jobs (all + the offered ones), level 99 and the ranks (all + 2..6).</summary>
        void BuildSearchOptionList()
        {
            var cats = new List<object>();
            AppendTabs(cats, d.Table.Tabs);
            var jobs = new List<object> { new Dictionary<string, object> { ["label"] = "$s_blackmarket_dropdown_joball", ["code"] = 200 } };
            foreach (int code in JobCodes)
                if (d.JobOffered(code)) jobs.Add(new Dictionary<string, object> { ["label"] = d.JobName(code), ["code"] = code });
            var ranks = new List<object> { new Dictionary<string, object> { ["label"] = "$s_blackmarket_dropdown_rankall", ["rank"] = 0 } };
            for (int r = 2; r <= 6; r++)
                ranks.Add(new Dictionary<string, object> { ["label"] = ClientFormat.Format(d.Common(0x91c), d.RankName(r)), ["rank"] = r });
            Host.Invoke("buildSearchOptionList", new Dictionary<string, object>
            {
                ["categoryArray"] = cats, ["jobArray"] = jobs, ["maxLevel"] = 99, ["itemRankArray"] = ranks,
            });
        }

        BlackMarketTable.Tab FindTab(int id) => tabs.FirstOrDefault(t => t.Id == id);

        /// <summary>FillSubTabArray: ret.array = the tab's children when the tab is found and does not show them itself.</summary>
        void FillSubTabArray(object ret, int id)
        {
            if (FindTab(id) is not { } t || t.ShowIncludeLowTab) return;
            var list = new List<object>();
            foreach (var c in t.Children)
                list.Add(new Dictionary<string, object> { ["label"] = c.Name, ["id"] = c.Id, ["priority"] = c.Priority });
            Host.Set(ret, "array", list);
        }

        /// <summary>BuildOptionLists: the tab's option stat lists when its option is new, then autoCompleteName, optionId, result.</summary>
        void BuildOptionLists(object ret, int id)
        {
            var t = d.Table.GetTab(id) ?? FindTab(id);
            if (t == null) return;
            int opt = t.OptionId;
            if (opt >= 0 && optionId != opt && d.Table.OptionStats((uint)opt) is { Length: > 0 } stats)
            {
                for (int i = 0; i < stats.Length; i++)
                {
                    if (d.Table.StatTable(stats[i]) is not { } st) continue;
                    var list = new List<object> { new Dictionary<string, object> { ["label"] = "$s_blackmarket_dropdown_notselect", ["idx"] = -1 } };
                    foreach (var s in st)
                        list.Add(new Dictionary<string, object>
                        {
                            ["label"] = s.Percent ? s.Name + " (%)" : s.Name, ["idx"] = s.Id, ["group"] = s.Kind,
                            ["percent"] = s.Percent ? 1 : 0, ["defaultValue"] = s.Default,
                        });
                    optionId = opt;
                    Host.Set(ret, "option" + i, list);
                    Host.Set(ret, "option" + i + "_id", stats[i]);
                }
            }
            Host.Set(ret, "autoCompleteName", t.AutoCompleteName);
            Host.Set(ret, "optionId", opt);
            Host.Set(ret, "result", true);
        }

        int GetInt(object o, string k) => Rt.ToInt32(Host.Get(o, k));
        int AToI(object o, string k)
        {
            var v = Host.Get(o, k);
            return Rt.IsNullish(v) ? 0 : (int)Math.Clamp(ParseInt64(Rt.ToString(v)), int.MinValue, int.MaxValue);
        }

        /// <summary>ReadSearchCondition (uiblackmarketpage.cpp:875-939): the movie's search data as the condition.</summary>
        bool ReadSearchCondition(object data)
        {
            if (pending > 0) return false;
            cond ??= new BlackMarketSearch();
            int cat = GetInt(data, "categoryId");
            if (FindTab(cat) == null) return false;
            var nameV = Host.Get(data, "itemName");
            string name = Rt.IsNullish(nameV) ? "" : Rt.ToString(nameV);
            int min = AToI(data, "minLevel"), max = AToI(data, "maxLevel");
            if (max == 0) max = 99;
            (min, max) = (Math.Min(min, max), Math.Max(min, max));
            int job = GetInt(data, "jobCode"), rank = GetInt(data, "rank"), pageNo = GetInt(data, "pageNumber");
            var sortV = Host.Get(data, "sortDataName");
            string sort = Rt.IsNullish(sortV) ? "" : Rt.ToString(sortV);
            bool up = Rt.ToBoolean(Host.Get(data, "isSortUp"));
            bool optionSearch = Rt.ToBoolean(Host.Get(data, "isOptionSearch"));
            int enMin = GetInt(data, "enchantMinLevel"), enMax = GetInt(data, "enchantMaxLevel");
            if (enMax == -1) enMax = 15;
            int soMin = GetInt(data, "socketMinCount"), soMax = GetInt(data, "socketMaxCount");
            if (soMax == -1) soMax = 3;

            var c = cond;
            c.MajorCategory = cat;
            c.MinorCategory = MinorCategory(cat);
            c.MinLevel = min; c.MaxLevel = max;
            c.JobGroup = JobMask(job, job == 200);
            c.Rank = rank;
            c.Name = name;
            c.Page = pageNo;
            c.Sort = SortCode(sort, up);
            c.OptionSearch = (byte)(optionSearch ? 1 : 0);
            c.Limit = 1;
            c.EnchantMin = enMin; c.EnchantMax = enMax;
            c.SocketMin = (byte)soMin; c.SocketMax = (byte)soMax;
            if (optionSearch)
                for (int i = 0; i < 3; i++)
                {
                    var o = Host.Get(data, "option" + i);
                    if (o == null || Rt.IsNullish(o)) { c.StatMin[i] = c.StatMax[i] = 0; continue; }
                    int idx = GetInt(o, "idx"), group = GetInt(o, "group");
                    bool percent = GetInt(o, "percent") == 1;
                    c.StatMin[i] = OptionKey(group, percent, idx);
                    double mv = Rt.ToNumber(Host.Get(o, "minValue"));
                    c.StatMax[i] = percent ? OptionPercentValue(mv) : (int)mv;
                }
            if (c.OptionSearch == 1)
            {
                c.Name = c.Name.Trim();
                if (c.Name.Length == 0 && c.MinorCategory == 99999)
                {
                    cond = null;
                    d.ShowText(d.CommonKey("s_blackmarket_error_cannot_searchex_without_condition"));
                    return false;
                }
            }
            pending = cat;
            sendPending = true;
            return true;
        }

        /// <summary>FUN_140f15350: up to 7 rows from <paramref name="first"/> (end clamped to first + 6), walking from
        /// <paramref name="pos"/> (or the head) with i counting from first.</summary>
        List<object> Rows(int first, int end, int pos)
        {
            var list = new List<object>();
            var all = d.Book.SearchProducts;
            if (first < 0 || first > end || all.Count == 0) return list;
            int i = first;
            end = Math.Min(first + 6, end);
            for (int cur = pos >= 0 ? pos : 0; cur < all.Count; cur++, i++)
            {
                if (i > end) break;
                var obj = new Dictionary<string, object>();
                if (d.Row(obj, all[cur], false)) { all[cur].Row = i; list.Add(obj); }
            }
            return list;
        }

        /// <summary>BuildItemList(a, b, p): the answer of the pending search → buildItemList(rows, count, a == 0).</summary>
        public void BuildItemList(int first, int end, int tail)
        {
            if (Host?.Entity == null || cond == null || cond.MajorCategory != pending) return;
            pending = 0;
            // The client walks from the old tail position; the next one is the first new row.
            var rows = Rows(first, end, tail + 1 < d.Book.SearchProducts.Count ? tail + 1 : -1);
            Host.Invoke("buildItemList", rows, rows.Count, first == 0);
        }

        /// <summary>UpdateItem(entry, state): the row's state, price and count.</summary>
        public void UpdateItem(BlackMarketEntry e, int st)
        {
            if (Host?.Entity == null || e?.Item == null) return;
            Host.Invoke("updateItem", new Dictionary<string, object>
            {
                ["marketSN"] = ((ulong)e.Id).ToString(CultureInfo.InvariantCulture), ["saleState"] = StateName(st),
                ["price"] = Grp(e.Product.PriceFor(e.Remain)),
                ["slotData"] = new Dictionary<string, object> { ["count"] = e.Item.Count },
            }, e.Row);
        }

        /// <summary>FUN_140f0de60: result 0x24 clears the name input; type 5 marks the row sold out (0x17) or expired
        /// (0x1b); type 4 drops the pending search (errorSearchFromDelay).</summary>
        public void SearchNotice(int type, int result, long value)
        {
            if (result == 0x24 && Host.Child("m_inputName", page) is { } input) Host.Set(input, "text", "");
            if (type == 5)
            {
                if (d.Book.FindSearchProduct(value) is not { } e) return;
                if (result == 0x17) { e.StateA = true; UpdateItem(e, 4); }
                else if (result == 0x1b) UpdateItem(e, 3);
            }
            else if (type == 4)
            {
                pending = 0;
                sendPending = false;
                Host.Invoke("errorSearchFromDelay");
            }
        }

        public void OnReplyReceived(int action) { if (request.Done(action)) d.Lock(true); }

        /// <summary>OnClickBuy: the state re-read (a stale row updated), sold out 0x87, expired, an own product, the mesos of
        /// one unit (0x34); else the count box.</summary>
        void OnClickBuy(long id, string state)
        {
            if (d.Book.FindSearchProduct(id) is not { Item: not null } e) return;
            int st = e.GetState(false, d.ServerTime());
            if (!string.Equals(state, StateName(st), StringComparison.OrdinalIgnoreCase)) UpdateItem(e, st);
            if (st == 4) { d.Notice(d.Common(0x87)); return; }
            if (st == 3) { d.Notice(d.CommonKey("s_blackmarket_error_buy_expired")); return; }
            if (e.Product.SellerAccountId == d.OwnAccountId())
            {
                d.ShowText(d.CommonKey("s_blackmarket_error_cannot_buy_ownProduct"));
                return;
            }
            if (d.Book.Mesos() < e.Product.PriceFor(1)) { d.Notice(d.Common(0x34)); return; }
            AskBuyCount(e);
        }

        /// <summary>AskBuyCount: a count box (0x12) when more than one is listed, else the confirm of one.</summary>
        void AskBuyCount(BlackMarketEntry e)
        {
            if (e.Product.Count <= 1) { Confirm(e, 1); return; }
            long unit = e.Product.UnitPrice;
            uint roll = unit > 0 ? (uint)Math.Min(d.Book.Mesos() / unit, e.Remain) : 1;
            var box = new MessageBoxData(0x12) { Msg = d.Common(0x295), Title = d.Common(0x45f), EnterAllowed = false };
            box.SetButtonLabelIds(0x47d, 0x476, 0);
            box.SetItemEntry(e.Item, d.ColoredName(e.Item), 0, 1, (int)roll, (int)e.Product.PriceFor(1), 1, d.Model.Tables.Items.TextColor);
            box.Callback = CountAnswer;
            box.Append(e.Id);
            d.ShowBox(box);
        }

        /// <summary>FUN_140f113e0: yes → the count from the click tag ("id,count") → the confirm.</summary>
        bool CountAnswer(int result, MessageBoxData box)
        {
            if (Host?.Entity == null) return true;
            if (result != 0) return true;
            var parts = (box.ClickedTag ?? "").Split(',', 2);
            if (parts.Length < 2) return true;
            long id = box.ArgLong(0, 0);
            int n = MessageBoxData.WToI(parts[1]);
            if (d.Book.FindSearchProduct(id) is { Item: not null } e) Confirm(e, n);
            return true;
        }

        /// <summary>The buy confirm (0x11): the item (a copy of the count bought when it differs), the price text.</summary>
        void Confirm(BlackMarketEntry e, int n)
        {
            var show = e.Item;
            if (n != show.Count)
            {
                show = Item.Create(d.Book.ItemData, e.Item.ItemId, e.Item.Rarity) ?? e.Item;
                if (show != e.Item) show.Count = (uint)n;
            }
            long price = e.Product.PriceFor(n);
            string def = d.CommonKey("s_msg_blackmarket_ask_buy_text_default");
            string msg = d.TradeCount(e.Item) is int tc
                ? ClientFormat.Format(d.CommonKey("s_msg_blackmarket_ask_buy_limit_tradecount"), Grp(price), tc, tc - 1, def)
                : ClientFormat.Format(d.Common(0x947), Grp(price), def);
            var box = new MessageBoxData(0x11) { Msg = msg };
            box.SetItemEntry(show, d.ColoredName(show), 1, 0, -1, -1, 1, d.Model.Tables.Items.TextColor);
            box.SetButtonLabelIds(0x477, 0x478, 0);
            box.Callback = BuyAnswer;
            box.Append(e.Id);
            box.Append(n);
            d.ShowBox(box);
        }

        /// <summary>OnBuyCountMsgBox (@0x140f115b0): yes → BuyProduct.</summary>
        bool BuyAnswer(int result, MessageBoxData box)
        {
            if (result == 0) BuyProduct(box.ArgLong(0, 0), box.ArgInt(1, 0));
            return true;
        }

        /// <summary>BuyProduct (@0x140f107a0): the product and its item, the mesos of the count (0x34); then the root
        /// locked (FUN_1412761e0(root, 0)), the request kept as action 5 and SendBlackMarketMode5.</summary>
        void BuyProduct(long id, int n)
        {
            if (d.Book.FindSearchProduct(id) is not { Item: not null } e) return;
            if (d.Book.Mesos() < e.Product.PriceFor(n)) { d.Notice(d.Common(0x34)); return; }
            d.Lock(false);
            request.SetProduct(5, 0, 0);
            d.Book.SendBuy(id, n);
        }
    }

    // ---- the register page (BlackMarketPageItemRegister) ------------------------------------------------------------

    sealed class RegisterPage
    {
        readonly BlackMarketDialogController d;
        readonly Request request = new();
        object page, counter;
        bool filled, shown, built;
        // RegisterItem
        int regId, regRank, regDefault;
        long regPrice;

        public RegisterPage(BlackMarketDialogController d) { this.d = d; ResetItem(); }
        UiHost Host => d.Host;

        void ResetItem() { regId = 0; regRank = 0; regPrice = 0; regDefault = d.Book.CostRatePermyriad(); }
        bool IsSame(int id, int rank) => regId == id && regRank == rank;
        bool SetItem(Item item)
        {
            if (item == null) { ResetItem(); return false; }
            if (IsSame(item.ItemId, item.Rarity)) return false;
            regId = item.ItemId; regRank = item.Rarity; regPrice = 0; regDefault = d.Book.CostRatePermyriad();
            return true;
        }

        /// <summary>FUN_140f14900: the fee text 0x92a (sale days, the cost rate, the deposit percent, the deposit max).</summary>
        string FeeText() => ClientFormat.Format(d.Common(0x92a), d.SellEndDay(),
            (d.Book.CostRatePermyriad() / 100.0).ToString("0.##", CultureInfo.InvariantCulture),
            BlackMarketBook.RegisterDepositPercent, Grp(BlackMarketBook.RegisterDepositMax));

        /// <summary>OnInit: m_registerPage, initData (the price limits, their tooltip 0x94a, the fee), m_counter; without
        /// feature 0xc5 the total price is hidden (its y taken by the max price).</summary>
        public void OnInit()
        {
            page = Host.Child("m_registerPage");
            Host.Invoke(page, "initData", Host.Obj(
                ("minPrice", d.Book.SellMinPrice), ("maxPrice", d.Book.SellMaxPrice),
                ("tooltipSellPrice", ClientFormat.Format(d.Common(0x94a), Grp(d.Book.SellMinPrice), Grp(d.Book.SellMaxPrice))),
                ("noticeFee", FeeText())));
            counter = page != null ? Host.Child("m_counter", page) : null;
            var ctl = page != null ? Host.Child("m_registerControl", page) : null;
            if (ctl != null && !d.FeatureEnabled(0xc5) && Host.Child("m_totalprice", ctl) is { } total)
            {
                Host.SetVisible(total, false);
                var y = Host.Get(total, "y");
                if (Host.Child("m_maxPrice", ctl) is { } mp) Host.Set(mp, "y", y);
                if (Host.Child("m_textMaxPrice", ctl) is { } tm) Host.Set(tm, "y", y);
            }
        }

        public void Shutdown()
        {
            page = counter = null;
            filled = shown = built = false;
            ResetItem();
            request.Reset();
        }

        /// <summary>vfunc_3: the own list once.</summary>
        public void Show()
        {
            if (built) return;
            built = true;
            BuildOwnerItemList(d.Book.OwnProducts);
            filled = true;
            shown = true;
        }

        /// <summary>vfunc_5: the counter text (s_blackmarket_register_counter of the own count and the limit).</summary>
        public void Tick()
        {
            if (!filled || counter == null) return;
            Host.Set(counter, "htmlText", ClientFormat.Format(d.CommonKey("s_blackmarket_register_counter"),
                d.Book.OwnProducts.Count, BlackMarketBook.OwnProductLimit));
            filled = false;
        }

        public bool OnCallback(string name, object[] a)
        {
            switch (name.ToLowerInvariant())
            {
                case "onenableregister":
                {
                    long uid = d.SlotItemUid(Str(a, 0));
                    var ret = Arg(a, 1);
                    if (uid == 0 || d.Book.FindItem(uid) is not { } item || !d.CanRegister(item)) return true;
                    if (RegisterObj(ret, item)) { Host.Set(ret, "success", true); SelectItem(item); }
                    return true;
                }
                case "onclickregister":
                    OnClickRegister(ParseInt64(Str(a, 0)), ParseInt64(Str(a, 1)), Rt.ToInt32(Arg(a, 2)));
                    return true;
                case "onclickcancelsell":
                    OnClickCancelSell(ParseInt64(Str(a, 0)));
                    return true;
                case "onclearslot":
                    ResetItem();
                    return true;
            }
            return false;
        }

        List<object> OwnRows(IReadOnlyList<BlackMarketEntry> own)
        {
            var list = new List<object>();
            int index = 0;
            foreach (var e in own)
            {
                index++;
                var obj = new Dictionary<string, object>();
                if (d.Row(obj, e, true)) { list.Add(obj); e.Row = index; }
            }
            return list;
        }

        public void BuildOwnerItemList(IReadOnlyList<BlackMarketEntry> own)
        {
            if (Host?.Entity == null) return;
            Host.Invoke("buildOwnerItemList", OwnRows(own));
        }

        /// <summary>FUN_140f12d40: registItemComplete of the row, the item message 0x943, the ready sound.</summary>
        public void OwnAdded(BlackMarketEntry e)
        {
            if (Host?.Entity == null || e == null || !shown) return;
            var obj = new Dictionary<string, object>();
            if (!d.Row(obj, e, true)) return;
            e.Row = Rt.ToInt32(Host.Invoke("registItemComplete", obj));
            if (e.Item != null) d.ItemMessage(e.Item, ClientFormat.Format(d.Common(0x943), d.SellEndDay()));
            d.Sound("BlackMarket_Ready");
            filled = true;
        }

        /// <summary>FUN_140f13030: removeItemComplete of the id, the item message (0x944, or "already" for another reason).</summary>
        public void OwnRemoved(BlackMarketEntry e, int reason)
        {
            if (Host?.Entity == null || e == null || !shown) return;
            Host.Invoke("removeItemComplete", (object)((ulong)e.Id).ToString(CultureInfo.InvariantCulture));
            filled = true;
            string msg = reason == 0 ? d.Common(0x944) : d.CommonKey("s_msg_blackmarket_complete_remove_already");
            if (e.Item != null) d.ItemMessage(e.Item, msg);
        }

        /// <summary>FUN_140f137a0: the reference price of the selected item (setRefPrice; 0 → unknown).</summary>
        public void ReferencePrice(int itemId, int rarity, long price)
        {
            if (Host?.Entity == null || !IsSame(itemId, rarity)) return;
            regPrice = price;
            Host.Invoke("setRefPrice", (object)(price == 0 ? "$s_blackmarket_price_in_shop_unknown" : Grp(price)));
        }

        public void OnReplyReceived(int action) { if (request.Done(action)) d.Lock(true); }

        /// <summary>SelectItem (@0x140f13ce0): a newly selected item → SendBlackMarketMode8 (its reference price).</summary>
        void SelectItem(Item item)
        {
            if (item != null && SetItem(item)) d.Book.SendReferencePrice(item.ItemId, item.Rarity);
        }

        /// <summary>FUN_140f14650: the register slot's data (refPrice, maxPrice, defaultPrice, itemGUID, itemName, the fee).</summary>
        bool RegisterObj(object ret, Item item)
        {
            if (IsSame(item.ItemId, item.Rarity) && regPrice != 0) Host.Set(ret, "refPrice", Grp(regPrice));
            Host.Set(ret, "maxPrice", Grp(d.Book.SellMaxPrice));
            Host.Set(ret, "defaultPrice", Grp(0));
            Host.Set(ret, "itemGUID", ((ulong)item.Uid).ToString(CultureInfo.InvariantCulture));
            Host.Set(ret, "itemName", d.ColoredName(item));
            if (item.Rarity != regRank && regDefault != d.Book.CostRatePermyriad()) Host.Set(ret, "noticeFee", FeeText());
            return true;
        }

        /// <summary>OnClickCancelSell: the stop-sale confirm (0x11) of the own product; the request kept as action 3.</summary>
        void OnClickCancelSell(long id)
        {
            if (d.Book.FindOwnProduct(id) is not { Item: not null } e) return;
            request.SetProduct(3, id, e.Remain);
            long price = e.Product.PriceFor(e.Remain);
            string msg = e.GetState(true, d.ServerTime()) switch
            {
                5 => d.CommonKey("s_msg_blackmarket_ask_stopsale_hide"),
                3 => d.CommonKey("s_msg_blackmarket_ask_stopsale_expired"),
                _ => ClientFormat.Format(d.CommonKey("s_msg_blackmarket_ask_stopsale"), Grp(price)),
            };
            var box = new MessageBoxData(0x11) { Msg = msg };
            box.SetItemEntry(e.Item, d.ColoredName(e.Item), 1, 0, -1, -1, 1, d.Model.Tables.Items.TextColor);
            box.SetButtonLabelIds(0x477, 0x478, 0);
            box.Callback = CancelAnswer;
            d.ShowBox(box);
        }

        /// <summary>FUN_140f13950: yes → the root locked and SendBlackMarketMode3 of the kept id; no → the request reset.</summary>
        bool CancelAnswer(int result, MessageBoxData box)
        {
            if (result == 0) { d.Lock(false); d.Book.SendCancel(request.Key); }
            else request.Reset();
            return true;
        }

        /// <summary>OnClickRegister (@0x140f13e70): the book's checks in order with their boxes; the terms box last.</summary>
        void OnClickRegister(long uid, long price, int count)
        {
            switch (d.Book.CheckRegister(uid, price, count, out long deposit))
            {
                case BlackMarketBook.RegisterCheck.None: return;
                case BlackMarketBook.RegisterCheck.NoItem: d.ShowText(d.Common(0x935)); return;
                case BlackMarketBook.RegisterCheck.LackCount: d.ShowText(d.Common(0x931)); return;
                case BlackMarketBook.RegisterCheck.Blocked: d.BlockNotice(BlackMarketBook.RegisterBlockAction); return;
                case BlackMarketBook.RegisterCheck.PriceRange:
                    d.ShowText(ClientFormat.Format(d.Common(0x930), Grp(d.Book.SellMinPrice), Grp(d.Book.SellMaxPrice)));
                    return;
                case BlackMarketBook.RegisterCheck.LackDeposit:
                    d.ShowText(d.CommonKey("s_blackmarket_error_register_lack_deposit"));
                    return;
                case BlackMarketBook.RegisterCheck.Full:
                    d.ShowText(ClientFormat.Format(d.Common(0x933), BlackMarketBook.OwnProductLimit));
                    return;
                case BlackMarketBook.RegisterCheck.Terms:
                    request.SetKeyword(2, $"{uid},{price},{count}");
                    d.ShowTerms(6, ClientFormat.Format(d.Common(0x9d8), Grp(deposit)), d.Common(0x9d9), TermsAnswer);
                    return;
            }
        }

        /// <summary>FUN_140f13a10: yes → terms bit 6 agreed; with a fee discount the notice 0x949 first, whose close sends;
        /// else SendRegisterRequest at once.</summary>
        bool TermsAnswer(int result, MessageBoxData box)
        {
            if (result != 0) return true;
            d.AgreeTerms(6);
            if (d.Book.FeeDiscountRate() >= 0)
            {
                var n = new MessageBoxData(0)
                {
                    Msg = ClientFormat.Format(d.Common(0x949), "", "", d.Book.FeeDiscountRate()),
                    Callback = (_, _) => { SendRegisterRequest(); return true; },
                };
                d.ShowBox(n);
                return true;
            }
            SendRegisterRequest();
            return true;
        }

        /// <summary>SendRegisterRequest (@0x140f13d80): the kept "uid,price,count" → SendBlackMarketMode2 (no lock here).</summary>
        void SendRegisterRequest()
        {
            var parts = request.Keyword.Split(',');
            if (parts.Length < 3) return;
            d.Book.SendRegister(ParseInt64(parts[0]), ParseInt64(parts[1]), MessageBoxData.WToI(parts[2]));
        }
    }
}
