using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2.Ui;

/// <summary>
/// A product as the manager keeps it: the read <see cref="BlackMarketProduct"/>, its item (CBlackMarketProduct::m_item, the
/// slot's CItem; null when the item data does not know the id) and the client's own fields m_nStateA (set by the bought
/// handler) and m_n88 (the row index the pages give it when they build their lists).
/// </summary>
public sealed class BlackMarketEntry
{
    public readonly BlackMarketProduct Product;
    public readonly Item Item;
    /// <summary>m_nStateA: sold out, or an own product bought (FUN_140a21930).</summary>
    public bool StateA;
    /// <summary>m_n88: the row index (FUN_140f15350, FUN_140f15570).</summary>
    public int Row;

    public BlackMarketEntry(BlackMarketProduct product, Item item) { Product = product; Item = item; }

    public long Id => Product.Id;
    /// <summary>GetRemain (@0x1401be3b0).</summary>
    public int Remain => Product.Remain;

    /// <summary>GetState (@0x1401be330, blackmarketitemoption.cpp:603): with <paramref name="like"/>, 5 while m_nStateB is
    /// set; without, 4 while m_nStateA is set; then 3 when the end of the sale is not after the server time, else 2.</summary>
    public int GetState(bool like, long serverTime)
    {
        if (like) { if (Product.StateB != 0) return 5; }
        else if (StateA) return 4;
        return Product.ExpireTime <= serverTime ? 3 : 2;
    }
}

/// <summary>The black market dialog (UIBlackMarketDialog, id 0x4e97) as the manager calls it (uiblackmarketdialog.cpp:372-474).
/// Each call's own checks (vfunc_55, the page being there or current) are the dialog's.</summary>
public interface IBlackMarketDialog
{
    /// <summary>BuildOwnerItemList @0x1418d0de0: the register page's own list from the own products.</summary>
    void BuildOwnerItemList(IReadOnlyList<BlackMarketEntry> own);
    /// <summary>FUN_1418d0e40: the register page's FUN_140f12d40, an own product added.</summary>
    void OwnAdded(BlackMarketEntry entry);
    /// <summary>FUN_1418d0f00: the register page's FUN_140f13030, an own product removed with the packet's reason.</summary>
    void OwnRemoved(BlackMarketEntry entry, int reason);
    /// <summary>BuildItemList @0x1418d1330: the search page's BuildItemList(first, end, pos). The client passes the search
    /// list's tail position from before the read; here it is that tail's index, or -1 for none (the walk then starts at the
    /// head, FUN_140f15350).</summary>
    void BuildItemList(int first, int end, int tail);
    /// <summary>FUN_1418d1080: the bought notice (a new item of the product's id and rarity with the count bought, string
    /// 0x945) and the search page's UpdateItem(entry, GetState(false)).</summary>
    void Bought(BlackMarketEntry entry, int count);
    /// <summary>FUN_1418d13a0: the register page's FUN_140f137a0(item id, rarity, price) while it is the current page.</summary>
    void ReferencePrice(int itemId, int rarity, long price);
    /// <summary>OnItemSearchNotice @0x1418d0b60: the search page's FUN_140f0de60(type, result, value).</summary>
    void SearchNotice(int type, int result, long value);
}

/// <summary>
/// The black market manager, CBlackMarketMan (client/ms2/blackmarketman.cpp): the own products and the search results by
/// listing id (and the search results in their order), the sends (SendOp::BlackMarket 0x5F) and what OnBlackMarket
/// (@0x140a20650) does on each BlackMarket packet (0x98, <see cref="BlackMarketMessage"/>). No send changes anything here:
/// the lists change only on the server's answers. The UI calls come out as hooks and events, as in <see cref="TradeBook"/>.
/// </summary>
public sealed class BlackMarketBook
{
    /// <summary>The black market dialog's UI id.</summary>
    public const int DialogId = 0x4e97;

    /// <summary>FUN_140710590: the send.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>GetStringCommon: the common string of an id.</summary>
    public Func<int, string> Common = _ => "";
    /// <summary>GetStringCommonByKey: the common string of a key.</summary>
    public Func<string, string> CommonKey = _ => "";
    /// <summary>CStringTable::FUN_141811980(1, id): an item's name.</summary>
    public Func<int, string> ItemName = _ => "";
    /// <summary>The item data, for the products' items (FUN_140523c40's template lookup).</summary>
    public ItemDataTable ItemData = ItemDataTable.Empty;
    /// <summary>FindBlackMarketDialog(0x4e97) (@0x140a22f10): the dialog, or null.</summary>
    public Func<IBlackMarketDialog> Dialog = () => null;
    /// <summary>CUIService::IsDialogOpen(0x4e97).</summary>
    public Func<bool> IsDialogOpen;
    /// <summary>Client::Constants PropertyProtectionTime (+0x6aa8; the decomp calls the field m_nRegisterFee), read for
    /// result 37.</summary>
    public Func<int> PropertyProtectionTime = () => 0;

    /// <summary>FUN_141275260(id, 1, 1), Unk_BmMessage (FUN_141275460(id, 1, 1, result)) and ShowSystemNotice(text, 1, 1): a
    /// notice of a text, its type and whether it also goes to the chat log.</summary>
    public event Action<string, int, bool> Notice;
    /// <summary>FUN_140d73bc0(id, 0, 0, "") and ShowMessageBox(text, 0, 0, ""): an OK box.</summary>
    public event Action<string> Box;
    /// <summary>FUN_1412751b0(text, kind): a chat log line.</summary>
    public event Action<string, int> ChatLine;
    /// <summary>PlaySystemSound(key).</summary>
    public event Action<string> Sound;
    /// <summary>CUIService::OpenDialog(id).</summary>
    public event Action<int> OpenDialog;

    readonly Dictionary<long, BlackMarketEntry> own = new();
    readonly List<long> ownOrder = new();               // the walk order (see OwnProducts)
    readonly Dictionary<long, BlackMarketEntry> searchById = new();
    readonly List<BlackMarketEntry> search = new();

    /// <summary>m_mapOwn's values (GetOwnProducts @0x140a210e0), in the order they came in. STILL-OPEN: the client walks
    /// its CAtlMap (17 bins at first) in bin order, which this does not copy.</summary>
    public IReadOnlyList<BlackMarketEntry> OwnProducts => ownOrder.Select(id => own[id]).ToList();
    /// <summary>m_listSearch.</summary>
    public IReadOnlyList<BlackMarketEntry> SearchProducts => search;
    /// <summary>m_nReload: the next search result replaces the list.</summary>
    public bool Reload { get; private set; }
    /// <summary>Mode 6's m_nF8, m_tmA, m_tmB. STILL-OPEN: nothing in the client reads them back.</summary>
    public byte StatusFlag { get; private set; }
    public long StatusTimeA { get; private set; }
    public long StatusTimeB { get; private set; }

    public BlackMarketBook() { IsDialogOpen = () => Dialog() != null; }

    // ---- register checks and fees -----------------------------------------------------------------------------------

    /// <summary>The client's register page limit on own listings (OnClickRegister: GetOwnProductCount() &lt; 0x1c).</summary>
    public const int OwnProductLimit = 0x1c;
    /// <summary>Client::Constants BlackMarketSellMinPrice / BlackMarketSellMaxPrice (<see cref="GameTables"/>), set by the
    /// HUD.</summary>
    public long SellMinPrice = 10, SellMaxPrice = 9900000000;
    /// <summary>g_143ab2a90.FindItemByUid: the inventory item of a uid, or null.</summary>
    public Func<long, Item> FindItem = _ => null;
    /// <summary>DAT_143ab2898: the mesos held.</summary>
    public Func<long> Mesos = () => 0;
    /// <summary>CBlockManager::FindBlockedAction(id) found a block (ShowBlockNotice shows it). STILL-OPEN: the port has no
    /// block manager feed yet, so nothing is blocked.</summary>
    public Func<int, bool> ActionBlocked = _ => false;
    /// <summary>CGameRule::GetBlackMarketFeeDiscountRate (@0x140479560, m_690): -1 (the ctor and reset) or the rate in
    /// 0..10000 a GameEventBlackMarketFeeDiscount sets while it runs (gameevent.cpp:1314). STILL-OPEN: the port has no game
    /// event feed, so it stays -1.</summary>
    public Func<int> FeeDiscountRate = () => -1;

    // The client's precompiled Lua (Data/lua, the chunk's Blackmarket_RegisterDeposit_Max = 100000 and
    // Blackmarket_RegisterDeposit_Percent = 1, Blackmarket_RegisterDeposit_Rate = Percent / 100), reached through the CLua
    // wrappers of commonfiles/luautil.cpp:600-685. Ported by hand, as the quest rewards' Lua is.
    const long DepositMax = 100000;
    const int DepositPercent = 1;
    const double DepositRate = DepositPercent / 100.0;

    /// <summary>FUN_1401bcdb0 (@0x1401bcdb0) = CLua::CalcBlackMarketRegisterDeposit: the Lua
    /// calc_blackMarketRegisterDeposit(n) = math.min(math.ceil(n * Rate), Max) on (double)n, read back as an __int64.</summary>
    public static long RegisterDeposit(long total) => (long)Math.Min(Math.Ceiling(total * DepositRate), DepositMax);
    /// <summary>GetBlackMarketRegisterDepositMax (@0x1401bcde0): the Lua calc_blackMarketRegisterDepositMax() = Max.</summary>
    public static long RegisterDepositMax => DepositMax;
    /// <summary>GetBlackMarketRegisterDepositPercent (@0x1401bce00): the Lua calc_blackMarketRegisterDepositPercent() =
    /// Percent.</summary>
    public static int RegisterDepositPercent => DepositPercent;

    /// <summary>GetBlackMarketCostRate (@0x1401bcd60): a discount rate (&gt;= 0) over 10000f; else CLua::
    /// CalcBlackMarketCostRate(1), the Lua calc_blackMarketCostRate(x) = 0.05 (x unused) read back as a float.</summary>
    public float CostRate()
    {
        int rate = FeeDiscountRate();
        return rate >= 0 ? rate / 10000.0f : (float)0.05;
    }
    /// <summary>FUN_1401bcd30 (@0x1401bcd30): the cost rate in ten-thousandths, (int)(rate * 10000f), for FormatPercent.</summary>
    public int CostRatePermyriad() => (int)(CostRate() * 10000.0f);

    /// <summary>FUN_1401bcbf0 (@0x1401bcbf0, blackmarketitemoption.cpp): the buy fee of a price. 0 without the item. With a
    /// discount rate, (__int64)((float)price * CostRate()). Else 0 without the item data, then CLua::
    /// CalcBlackMarketBuyFeeCost(price, rarity, the data's m_nGroup, the enchant level): the Lua
    /// calc_blackMarketBuyFeeCost = math.floor(calc_blackMarketCostRate(rarity) * price) on (double)price, which reads
    /// neither the group nor the enchant level, and the cost rate ignores its argument.</summary>
    public long BuyFee(Item item, long price)
    {
        if (item == null) return 0;
        if (FeeDiscountRate() >= 0) return (long)((float)price * CostRate());
        if (item.Data == null) return 0;
        return (long)Math.Floor(0.05 * (double)price);
    }

    /// <summary>What <see cref="CheckRegister"/> found, in OnClickRegister's order.</summary>
    public enum RegisterCheck
    {
        /// <summary>uid 0: nothing.</summary>
        None,
        /// <summary>No item of the uid: box 0x935.</summary>
        NoItem,
        /// <summary>The item holds fewer than the count: box 0x931.</summary>
        LackCount,
        /// <summary>Action 0x25 blocked: ShowBlockNotice(block, 0x25, 0).</summary>
        Blocked,
        /// <summary>price * count outside the sell limits: box FormatStringId(0x930, min, max).</summary>
        PriceRange,
        /// <summary>Fewer mesos than the deposit: box s_blackmarket_error_register_lack_deposit.</summary>
        LackDeposit,
        /// <summary>The own listings at the limit: box FormatStringId(0x933, 0x1c).</summary>
        Full,
        /// <summary>The terms box (ShowTermsMessageBoxWithCallback(6, ...), title 0x9d8 of the deposit, text 0x9d9); its
        /// yes goes on to <see cref="ConfirmRegister"/>.</summary>
        Terms,
    }

    /// <summary>The blocked action OnClickRegister checks.</summary>
    public const int RegisterBlockAction = 0x25;

    /// <summary>BlackMarketPageItemRegister::OnClickRegister (@0x140f13e70, uiblackmarketpage.cpp) up to its boxes: the
    /// checks in order and the deposit, FUN_1401bcdb0(price * count). Nothing is sent and nothing changes here; on
    /// <see cref="RegisterCheck.Terms"/> the page keeps "{uid},{price},{count}" as its request keyword
    /// (m_request.SetKeyword(2, ...)) for <see cref="ConfirmRegister"/>.</summary>
    public RegisterCheck CheckRegister(long itemUid, long price, int count, out long deposit)
    {
        deposit = 0;
        if (itemUid == 0) return RegisterCheck.None;
        var item = FindItem(itemUid);
        if (item == null) return RegisterCheck.NoItem;
        if (item.Count < (uint)count) return RegisterCheck.LackCount;
        if (ActionBlocked(RegisterBlockAction)) return RegisterCheck.Blocked;
        long total = price * count;
        if (total < SellMinPrice || SellMaxPrice < total) return RegisterCheck.PriceRange;
        deposit = RegisterDeposit(total);
        if (Mesos() < deposit) return RegisterCheck.LackDeposit;
        return own.Count < OwnProductLimit ? RegisterCheck.Terms : RegisterCheck.Full;
    }

    /// <summary>FUN_140f13a10 (@0x140f13a10), the terms box's answer: nothing unless it is yes (0). With a discount rate
    /// (&gt;= 0) the page first shows box 0x949 (the event's begin and end, FormatPercent(rate, true)), whose close
    /// (OnFeeNoticeClosed) sends; returns false then and the page calls <see cref="SendRegister"/> on that close. Else
    /// SendRegisterRequest (@0x140f13d80) at once. FUN_140d74e50(6), called first on yes, is a message box call (kind 6) and
    /// belongs to the page.</summary>
    public bool ConfirmRegister(int result, long itemUid, long price, int count)
    {
        if (result != 0) return false;
        if (FeeDiscountRate() >= 0) return false;
        SendRegister(itemUid, price, count);
        return true;
    }

    // ---- sends ------------------------------------------------------------------------------------------------------

    /// <summary>SendBlackMarketMode1 @0x140a20940: ask for the own listings.</summary>
    public void SendOwnList() => Send(new BlackMarketOwnListRequest());
    /// <summary>SendBlackMarketMode2 @0x140a20a00: register (item uid, the price, count).</summary>
    public void SendRegister(long itemUid, long price, int count) => Send(new BlackMarketRegisterRequest(itemUid, price, count));
    /// <summary>SendBlackMarketMode3 @0x140a20aa0: cancel an own listing.</summary>
    public void SendCancel(long id) => Send(new BlackMarketCancelRequest(id));
    /// <summary>SendBlackMarketMode5 @0x140a20bf0: buy a count of a listing.</summary>
    public void SendBuy(long id, int count) => Send(new BlackMarketBuyRequest(id, count));
    /// <summary>SendBlackMarketMode8 @0x140a20c70: ask an item's reference price.</summary>
    public void SendReferencePrice(int itemId, int rarity) => Send(new BlackMarketReferencePriceRequest(itemId, rarity));

    /// <summary>SendBlackMarketMode4 @0x140a20b10: nothing without a condition or when it does not compact; page 1 sets
    /// m_nReload (the answer replaces the list); then the send.</summary>
    public void SendSearch(BlackMarketSearch condition)
    {
        if (condition == null || !condition.Compact()) return;
        if (condition.Page == 1) Reload = true;
        Send(new BlackMarketSearchRequest(condition));
    }

    // ---- lists ------------------------------------------------------------------------------------------------------

    /// <summary>FindOwnProduct @0x140a20e70.</summary>
    public BlackMarketEntry FindOwnProduct(long id) => own.TryGetValue(id, out var e) ? e : null;
    /// <summary>FindSearchProduct @0x140a20ff0.</summary>
    public BlackMarketEntry FindSearchProduct(long id) => searchById.TryGetValue(id, out var e) ? e : null;

    /// <summary>AddOwnProduct @0x140a20cf0: true when the id was not in; the map takes the entry either way.</summary>
    bool AddOwnProduct(BlackMarketEntry e)
    {
        bool isNew = !own.ContainsKey(e.Id);
        if (isNew) ownOrder.Add(e.Id);
        own[e.Id] = e;
        return isNew;
    }

    /// <summary>RemoveOwnProduct @0x140a20e00.</summary>
    void RemoveOwnProduct(long id)
    {
        if (own.Remove(id)) ownOrder.Remove(id);
    }

    /// <summary>AddSearchProduct @0x140a20f30: into the map by id and onto the list's tail.</summary>
    void AddSearchProduct(BlackMarketEntry e)
    {
        searchById[e.Id] = e;
        search.Add(e);
    }

    BlackMarketEntry Entry(BlackMarketProduct p) =>
        new(p, p.Item != null ? InventoryController.ToItem(p.Item, ItemData) : null);

    // ---- OnBlackMarket ----------------------------------------------------------------------------------------------

    /// <summary>OnBlackMarket @0x140a20650.</summary>
    public void OnBlackMarket(BlackMarketMessage m)
    {
        switch (m)
        {
            case BlackMarketMessage.Status s:
                StatusFlag = s.Flag;
                StatusTimeA = s.TimeA;
                StatusTimeB = s.TimeB;
                break;
            case BlackMarketMessage.OwnList l: OnOwnProductList(l.Products); break;
            case BlackMarketMessage.OwnAdded a: OnOwnProductAdded(a.Product); break;
            case BlackMarketMessage.OwnRemoved r: OnOwnProductRemoved(r.Id, r.Reason); break;
            case BlackMarketMessage.SearchResult r: OnSearchResult(r.Products); break;
            case BlackMarketMessage.Bought b: OnBought(b.Id, b.Count); break;
            case BlackMarketMessage.Sold:
                Sound?.Invoke("BlackMarket_Sell");
                ChatLine?.Invoke(Common(0x925), 0x12);
                SendOwnList();
                break;
            case BlackMarketMessage.Open:
                if (!IsDialogOpen()) OpenDialog?.Invoke(DialogId);
                break;
            case BlackMarketMessage.ReferencePrice p:
                Dialog()?.ReferencePrice(p.ItemId, p.Rarity, p.Price);
                break;
            case BlackMarketMessage.Error e: OnError(e); break;
        }
    }

    /// <summary>OnOwnProductList @0x140a211a0: the own map emptied, each product's item count clamped to what remains
    /// (an item of unknown data has no clamp here) and added; then the register page's own list.</summary>
    void OnOwnProductList(IReadOnlyList<BlackMarketProduct> products)
    {
        own.Clear();
        ownOrder.Clear();
        foreach (var p in products)
        {
            var e = Entry(p);
            e.Item?.SetCountClamped(e.Remain);
            AddOwnProduct(e);
        }
        Dialog()?.BuildOwnerItemList(OwnProducts);
    }

    /// <summary>OnOwnProductAdded @0x140a21340: no count clamp; the dialog hears of it only when the id is new.</summary>
    void OnOwnProductAdded(BlackMarketProduct p)
    {
        var e = Entry(p);
        if (AddOwnProduct(e)) Dialog()?.OwnAdded(e);
    }

    /// <summary>OnOwnProductRemoved @0x140a21460: an id in the own map goes to the dialog with the reason, then out of
    /// the map; another id does nothing.</summary>
    void OnOwnProductRemoved(long id, byte reason)
    {
        if (FindOwnProduct(id) is not { } e) return;
        Dialog()?.OwnRemoved(e, reason);
        RemoveOwnProduct(id);
    }

    /// <summary>OnSearchResult @0x140a216b0: with m_nReload the list and map are emptied first, and m_nReload is cleared.
    /// Each product's count is clamped to what remains; only products whose item has data are kept. Then the search page
    /// builds from the old count to the last index (at least 0), walking from the old tail.</summary>
    void OnSearchResult(IReadOnlyList<BlackMarketProduct> products)
    {
        if (Reload)
        {
            searchById.Clear();
            search.Clear();
        }
        Reload = false;
        int old = search.Count;
        int tail = old - 1;
        foreach (var p in products)
        {
            var e = Entry(p);
            if (e.Item == null) continue;               // GetData() == 0
            e.Item.SetCountClamped(e.Remain);
            AddSearchProduct(e);
        }
        Dialog()?.BuildItemList(old, Math.Max(search.Count - 1, 0), tail);
    }

    /// <summary>
    /// FUN_140a21930 (mode 5): an id not in the search map does nothing. Else the count bought is added, the item clamped
    /// to what remains, m_nStateA set when none remains or the product is an own one; with the dialog, the search page
    /// builds from the list's count (so no row, as there) and the dialog shows the bought notice and the row's update.
    /// </summary>
    void OnBought(long id, int count)
    {
        if (FindSearchProduct(id) is not { } e) return;
        e.Product.Bought += count;
        e.Item?.SetCountClamped(e.Remain);
        if (e.Remain == 0) e.StateA = true;
        if (FindOwnProduct(id) != null) e.StateA = true;
        if (Dialog() is not { } dlg) return;
        dlg.BuildItemList(search.Count, Math.Max(search.Count - 1, 0), search.Count - 1);
        dlg.Bought(e, count);
    }

    /// <summary>FUN_140a21ca0 (mode 0): the notice or box of the result, then the search page's notice of type, result
    /// and value.</summary>
    void OnError(BlackMarketMessage.Error e)
    {
        void N(int id) => Notice?.Invoke(Common(id), 1, true);                       // FUN_141275260(id, 1, 1)
        void B(int id) => Box?.Invoke(Common(id));                                    // FUN_140d73bc0(id, 0, 0, "")
        void Result() => Notice?.Invoke(ClientFormat.Format(Common(0x92b), e.Result), 1, true);   // Unk_BmMessage(0x92b, 1, 1, result)
        void Sys(string key) => Notice?.Invoke(CommonKey(key), 1, true);             // ShowSystemNotice(key text, 1, 1)
        switch (e.Result)
        {
            case 22: N(0x34); break;
            case 11:
            case 12: if (e.Type == 2) B(0x935); else Result(); break;
            case 13: N(0x88); break;
            case 14: B(0x935); break;
            case 30: B(0x936); break;
            case 25: B(0x932); break;
            case 26: B(0x931); break;
            case 31: if (e.Type == 2) B(0x92f); else Result(); break;
            case 29: N(0x92d); break;
            case 23: if (e.Type == 3) Sys("s_err_reserved_other"); else N(0x87); break;
            case 21: Box?.Invoke(ClientFormat.Format(Common(0x933), 0x1c)); break;   // BlockFormat<int>(0x933, 0x1c)
            case 5: B(0x934); break;
            case 24:
            case 34:
            case 35:
            case 40: break;
            case 32: B(0x92c); break;
            case 36: B(0x8b7); break;
            case 37:                                                                  // FUN_14028dee0(0x688, time, BlockFormat(0x5a5))
                Box?.Invoke(ClientFormat.Format(Common(0x688), PropertyProtectionTime(), ClientFormat.Format(Common(0x5a5))));
                break;
            case 38: B(0xc07); break;
            case 39: Sys("s_blackmarket_error_not_useable"); break;
            case 27: Sys("s_blackmarket_error_buy_expired"); break;
            case 42: Box?.Invoke(CommonKey("s_blackmarket_error_cannot_buy_ownProduct")); break;
            case 41: Box?.Invoke(CommonKey("s_blackmarket_error_purchase_count")); break;
            case 43: Box?.Invoke(ClientFormat.Format(Common(0x93d), ItemName(e.TableId), e.Number)); break;
            case 44: Box?.Invoke(ClientFormat.Format(Common(0x93e), ItemName(e.TableId), e.Number)); break;
            default: Result(); break;
        }
        Dialog()?.SearchNotice(e.Type, e.Result, e.Value);
    }
}
