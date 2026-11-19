using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Shop;

/// <summary>
/// UIShopDialog (client/ms2/uishopdialog.cpp; UI id 0x4e3b), the C++ side of the npc shop movie uishopdialog. Its root
/// (ShopDialogProxy) makes the shop's own dialog by the shop type (initShop), which the movie's ShopDialog code then runs:
/// the tab bar, the category list, the product list and the restock bar. This feeds it the shop header and the products of
/// Inbound 0x51 (commands 0 and 1), the tabs and category tree (CShopCategoryTable), and the product rows.
/// <para>Not ported (the shop's other commands, raised or left out): buying, selling and buying back, the instant restock,
/// the new-items group, the star / gacha / merat items, the item preview, the search's ban-word check and the give-point
/// event. A buy, sell or restock request is raised as <see cref="Request"/> and sends nothing.</para>
/// </summary>
public sealed class ShopController : UiController
{
    public override string Movie => "uishopdialog";
    protected override string EntityMethod => "initShop";
    public override string TypeName => "ShopDialog";                       // GetTypeName (@0x141af2be0)

    /// <summary>vfunc_52 (@0x141af7dc0, uishopdialog.cpp:1379) returns 1: the drag bar is in the shop clip under the proxy
    /// (m_dialog), not a child of the proxy itself, so the saved position (ShopDialog.xml) is read and kept.</summary>
    protected override bool HasDragBar => true;

    /// <summary>The ui id (vfunc_48).</summary>
    public const int UiId = 0x4e3b;

    /// <summary>CShopCategoryTable.</summary>
    public ShopCategoryTable Categories = ShopCategoryTable.Empty;
    /// <summary>NpcDataTableCli: the npc's shop icon, icon, gender and name.</summary>
    public Ms2.Formats.World.NpcTable Npcs;
    /// <summary>GetServerTime (seconds); 0 when the time is not synced.</summary>
    public Func<long> ServerTime = () => 0;
    /// <summary>Client::Constants ShopSeedResetTime: the reset interval of a shop that sends none.</summary>
    public int ShopSeedResetTime;
    /// <summary>
    /// Unk_Inventory::GetItemUseError (inventory.cpp:1578) for the local player: 0 usable, 1 too low a level, other codes
    /// for the other limits. It reads the player's gender, job, level, PC-bang and VIP state, the wedding and the learned
    /// emotions; null leaves every product in the list when "usable items only" is ticked.
    /// </summary>
    public Func<Item, int> ItemUseError;

    /// <summary>A request whose client path sends a packet or opens a part not here: "buy" (serial, quantity), "sell"
    /// (slot key), "reset", "repurchase".</summary>
    public event Action<string, object[]> Request;
    /// <summary>CUIService::OpenDialog of another window: the token pocket 0x4ece when the header asks for it.</summary>
    public event Action<int> OpenDialog;
    /// <summary>vfunc_66 with the inventory closed: the "openWindowPos" message to the inventory (0x4e2d) with this id.</summary>
    public event Action<int> InventoryWindowPos;

    /// <summary>A product as the dialog keeps it (CNpcShopProductCli): the packet's record and the item made from it.</summary>
    public sealed class Product
    {
        public ShopProduct Record;
        public Item Item;
        /// <summary>m_20: the category tag split at ','.</summary>
        public string[] Categories = Array.Empty<string>();
    }

    /// <summary>A tab row of a group (NpcShopTabInfo): the table's tab, its depth and the products it lists.</summary>
    public sealed class TabRow
    {
        public ShopTab Tab;
        public int Depth;
        public readonly List<Product> Products = new();
    }

    ShopInfo info = new();
    readonly SortedDictionary<int, Product>[] products = { new(), new(), new() };   // m_products, by serial (std::map)
    readonly List<TabRow>[] tabs = { new(), new(), new() };                         // m_tabs
    int group;              // m_nGroup
    int category;           // m_nCategory
    bool checkEnableItems = true;   // m_bCheckEnableItems (ctor 1)
    bool headerRead;        // m_188
    bool resetPending;      // m_18b
    bool ready;             // m_bReady
    long resetCheckTime;    // m_1a0
    int resetPrice;         // m_nResetPrice
    string search = "";     // m_strSearch
    object shop;            // m_spShop

    public ShopInfo Info => info;
    /// <summary>The shop's own dialog initShop made (m_spShop).</summary>
    public object ShopView => shop;
    public int Group => group;
    public int Category => category;
    public bool CheckEnableItems => checkEnableItems;
    public IReadOnlyList<TabRow> Tabs(int g) => tabs[g];
    public IReadOnlyDictionary<int, Product> Products(int g) => products[g];

    string Common(string key, params object[] args) => Ms2.Quest.ClientFormat.Format(Model.Tables.Common(key) ?? "", args);
    static string Number(long n) => n.ToString("N0", CultureInfo.InvariantCulture);    // FUN_1409404f0

    // ---- packets ---------------------------------------------------------------------------------------------------

    /// <summary>OnShopPacket (@0x141af8710): commands 0 and 1; the others are not read yet.</summary>
    public void OnShopPacket(ShopPacket p)
    {
        if (p.Command == 0) ReadShopInfo(p.Info);
        else if (p.Command == 1) ReadProducts(p.Products);
    }

    /// <summary>
    /// ReadShopInfo (@0x141af88d0): the group 0 products cleared, the header kept; the reset price starts at its original;
    /// the first header since the close sets "usable items only" from the shop; the token pocket opens when asked for (once,
    /// and while it is closed); the reset type 1, 2, 3 sets the interval to a day, a week, the month's days.
    /// </summary>
    public void ReadShopInfo(ShopInfo s)
    {
        ResetProducts(0);
        info = s;
        if (s.ResetEnable) resetPrice = s.ResetPrice;
        if (!headerRead) checkEnableItems = s.CheckEnableItems;
        resetPending = s.ResetEnable;
        if (s.OpenInventory && !headerRead) OpenDialog?.Invoke(0x4ece);
        headerRead = true;
        resetCheckTime = ServerTime();
        if (s.ResetType == 1) s.ResetInterval = 86400;
        else if (s.ResetType == 2) s.ResetInterval = 7 * 86400;
        else if (s.ResetType == 3)
        {
            var now = DateTimeOffset.FromUnixTimeSeconds(ServerTime()).LocalDateTime;
            s.ResetInterval = DateTime.DaysInMonth(now.Year, now.Month) * 86400;
        }
    }

    /// <summary>
    /// ReadProducts (@0x141af90f0): nothing without a shop; each product kept by serial (its item's slot the index in the
    /// batch; an existing serial is kept, std::map::insert). Once the count the header announced is there, the shop is
    /// enabled again and, when loaded, the list rebuilt with the countdown and the reset price.
    /// </summary>
    public void ReadProducts(IReadOnlyList<ShopProduct> list)
    {
        if (info.ShopId == 0) return;
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            var item = Item.Create(Model.Tables.Items, r.ItemId, r.Rarity);
            if (item != null)
            {
                item.Count = (uint)Math.Max(r.Bundle, 0);         // SetCount(m_b8)
                item.Slot = i;                                    // FUN_140db6db0
                item.Record = r.Item;
                if (r.Item != null) { item.EnchantLevel = r.Item.Enchant.Level; item.UnlimitedEnchantLevel = r.Item.LimitBreak.Level; }
            }
            var p = new Product { Record = r, Item = item, Categories = Split(r.Categories) };
            products[0].TryAdd(r.Serial, p);
        }
        if (info.ProductCount == products[0].Count)
        {
            EnableShop(true);
            if (ready)
            {
                UpdateProductList(0);
                UpdateSerial(info.ResetTime);
                RefreshResetPrice();
            }
        }
    }

    /// <summary>Unk_SplitA(m_b0, ","): the non-empty parts.</summary>
    static string[] Split(string tag) => (tag ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);

    // ---- the dialog ------------------------------------------------------------------------------------------------

    /// <summary>GetShopMovieName (@0x141b05450): the shop's own dialog class by shop type.</summary>
    public static string ShopMovieName(int type) => type switch
    {
        1 => "UIShopDialogWander",
        2 => "UIShopDialogStar",
        3 => "UIShopDialogPSSB",
        4 => "UIShopDialogGacha",
        _ => "UIShopDialog",
    };

    /// <summary>OnLoadComplete (@0x141af3820) up to UIDialog's: initShop by the shop type, before the dialog is aligned
    /// (ShopDialogProxy.alignment centres by the shop's panel size, 1 by 1 with no shop).</summary>
    public override void OnLoadCompleteBeforeBase()
    {
        if (Host.Entity != null) shop = Host.Invoke(Host.Entity, "initShop", ShopMovieName(info.ShopType));
    }

    /// <summary>
    /// OnLoadComplete (@0x141af3820) after initShop (<see cref="OnLoadCompleteBeforeBase"/>; none closes the dialog):
    /// UIDialog's, then initData
    /// (iconName: the header's icon, else the npc's shop icon, else its icon; strGender; checkEnableItems; the title:
    /// the shop's title row, else for an npc s_title_shop with its name; dpTab; notice), m_money, the reset data, the
    /// countdown, the group's tabs and the ready flag. The npc talk (type 1), the web guide (type 3), the star, gacha and
    /// preview parts are not ported.
    /// STILL-OPEN: the last step, CGuideManager::AddTrigger(ShopOpen = 0x20, shop id) for a shop id other than 0
    /// (uishopdialog.cpp:943; the trigger names, guideentity_1.cpp:42), waits for a guide system. The only guide that tests
    /// it is the FameContents shop guide (table/guide_famecontents.xml event 80082: shops 206-211). The npc overhead icon
    /// seen through the translucent title bar is not the dialog's: it is the npc's UIEmoticon movie (npc iconName, e.g.
    /// emoticon_movie_npc_equip, uiemoticon.cpp:101) at its Eff_UI point.
    /// </summary>
    public override void OnLoadComplete()
    {
        if (Host.Entity == null) return;
        if (shop == null || shop is Undefined) { Close(); return; }
        base.OnLoadComplete();
        if (!Model.HasLocalPlayer) return;
        string icon = info.Icon ?? "", gender = "";
        if (info.NpcId > 0 && Npcs?.Find(info.NpcId) is { } npc)
        {
            if (icon.Length == 0) { icon = npc.ShopIcon ?? ""; if (icon.Length == 0) icon = npc.Icon ?? ""; }
            gender = npc.Gender == 1 ? "girl" : "boy";           // NpcDataCli::IsFemale (npcdatacli.cpp:231)
        }
        var data = new List<(string, object)> { ("iconName", icon) };
        if (gender.Length > 0) data.Add(("strGender", gender));
        data.Add(("checkEnableItems", checkEnableItems));
        string title = Model.Tables.ShopTitle(info.ShopId);
        if (title.Length == 0)
        {
            if (info.ShopType == 2) title = "$s_shop_title_star";
            else if (info.ShopType == 4) throw new NotImplementedException("gacha shop title (ItemGachaBoxTable coin)");
            // FormatStringId(0x45e) = s_title_shop (commonstringtable.cpp:2732); FUN_1401aa6a0: the npc's name
            else if (info.NpcId > 0) title = Common("s_title_shop", Npcs?.Find(info.NpcId)?.Name ?? "");
        }
        data.Add(("title", title));
        data.Add(("dpTab", DataProvider(FillTabs())));
        data.Add(("notice", info.ShopType == 3 ? Common("s_shop_text_notice_pssb") : ""));
        if (info.ShopType == 4) throw new NotImplementedException("gacha shop coins");
        Host.Invoke(shop, "initData", Host.Obj(data.ToArray()));
        UpdateMoney();
        var reset = new List<(string, object)> { ("resetEnable", info.ResetEnable && info.ResetType != 99) };
        reset.Add(("resetNotice", info.ResetByAccount ? Common("s_shop_notice_reset_by_account") : ""));
        bool visible = !info.HideReset;
        reset.Add(("resetButtonVisible", visible));
        if (visible)
        {
            reset.Add(("resetPriceText", PriceText(info.ResetCurrency, info.ResetCurrencyItem, resetPrice, "")));
            if (resetPrice != info.ResetPrice) reset.Add(("resetPriceOrgText", Number(info.ResetPrice)));
        }
        Host.Invoke(shop, "setResetItemData", Host.Obj(reset.ToArray()));
        UpdateSerial(info.ResetTime);
        SetTab(group, true);
        ready = true;
    }

    /// <summary>
    /// vfunc_67 (@0x141af2e90): UIDialog's, then the shop forgotten: "usable items only" back on, the groups and the
    /// header cleared, the ready flag off.
    /// </summary>
    public override void Close()
    {
        base.Close();
        headerRead = false;
        checkEnableItems = true;
        resetPending = false;
        ready = false;
        category = 0;
        group = 0;
        for (int i = 0; i < 3; i++) ResetProducts(i);
        search = "";
        info = new ShopInfo { ResetEnable = false, HideReset = true };
        resetPrice = 0;
        shop = null;
    }

    /// <summary>
    /// vfunc_66 (@0x141af2cb0, uishopdialog.cpp:626) after UIDialog's (the shop's position as it opens): with the inventory
    /// (0x4e2d) open, the two placed side by side, the shop on the left (FUN_141275c30; a gacha shop's inventory 180 lower);
    /// with it closed, the "openWindowPos" message with this id, which opens the inventory beside the shop
    /// (UIInventoryDialog's OnMessage). The gacha coin check before the message (ItemGachaBoxTable::GetCoinItemIdByShopId)
    /// is not ported.
    /// </summary>
    public void PlaceBesideInventory(UiController inventory, bool inventoryOpen)
    {
        if (Host?.Entity == null) return;
        var place = OpenPosition();
        if (!inventoryOpen) { InventoryWindowPos?.Invoke(UiId); return; }
        if (info.ShopType == 4) PlaceSideBySide(this, place, inventory, (0f, 0f), (0f, 180f));
        else PlaceSideBySide(this, place, inventory);
    }

    /// <summary>
    /// vfunc_72 (@0x141af3460), per frame: once the reset time has passed, the movie's endTime and the group 0 products
    /// cleared (the client then asks the server for the list, Shop 10, not sent here).
    /// </summary>
    public void Process()
    {
        if (shop == null || !Model.HasLocalPlayer) return;
        if (!resetPending) return;
        long now = ServerTime();
        if (now == 0 || info.ResetType == 99) return;
        if (info.ResetTime < now)
        {
            Host.Invoke(shop, "endTime");
            ResetProducts(0);
            Request?.Invoke("list", Array.Empty<object>());
            resetPending = false;
        }
    }

    /// <summary>ResetProducts (@0x141afef10).</summary>
    void ResetProducts(int g)
    {
        if (g < 0 || g >= 3) return;
        products[g].Clear();
        tabs[g].Clear();
    }

    /// <summary>FUN_1412761e0 (@0x1412761e0): the shop's mouseChildren and mouseEnabled.</summary>
    void EnableShop(bool on)
    {
        if (shop == null) return;
        Host.Set(shop, "mouseChildren", on);
        Host.Set(shop, "mouseEnabled", on);
    }

    /// <summary>ASDataProvider (asdataprovider.cpp:18): a scaleform.clik.data.DataProvider with the values pushed.</summary>
    object DataProvider(IEnumerable<object> values)
    {
        var dp = Host.RT.Construct(Host.RT.GetClass("scaleform.clik.data.DataProvider"), Array.Empty<object>());
        foreach (var v in values) Host.Invoke(dp, "push", v);
        return dp;
    }

    // ---- tabs and categories ---------------------------------------------------------------------------------------

    /// <summary>FillTabs (@0x141afbe30): purchase; buy back unless the shop only sells; new items with the new tab.</summary>
    List<object> FillTabs()
    {
        var list = new List<object> { FillTab(0, "$s_shop_purchase", "$s_tooltip_shop_tab_purchase") };
        if (!info.SellOnly) list.Add(FillTab(1, "$s_shop_repurchase", "$s_tooltip_shop_tab_repurchase"));
        if (info.HasNewTab) list.Add(FillTab(2, "$s_shop_new_items", "$s_tooltip_shop_tab_new_items"));
        return list;
    }

    /// <summary>FillTab (@0x141afd270): FUN_1418fffd0's tab record (tabIndex, label, tooltipData), isNewTab, renderReverse.</summary>
    AsObject FillTab(int g, string label, string tooltip) => Host.Obj(
        ("tabIndex", g), ("label", label),
        ("tooltipData", Host.Obj(("className", "export_common_tooltip_tooltipText"), ("id", tooltip), ("align", "topLeft"))),
        ("isNewTab", g == 2), ("renderReverse", g == 1));

    /// <summary>
    /// SetTab (@0x141afd380): the tabs of the shown group rebuilt for <paramref name="g"/>'s products (the header's tab shop
    /// for group 0; group 2's is not read yet) and, when shown, sent as the category list. (The client builds into the shown
    /// group's tab list, m_tabs[m_nGroup], with the given group's products.)
    /// </summary>
    void SetTab(int g, bool show)
    {
        if (g < 0 || g >= 3) return;
        var list = tabs[group];
        int tabShop = g == 0 ? info.TabShopId : 0;
        BuildTabsForProducts(tabShop, list, products[g]);
        if (show) BuildCategoryList(list);
    }

    /// <summary>
    /// BuildShopTabsForProducts (unit_141347b10.cpp:264): the list cleared; the category's tabs, for each product (by serial)
    /// or once with none.
    /// </summary>
    void BuildTabsForProducts(int tabShop, List<TabRow> list, SortedDictionary<int, Product> byserial)
    {
        list.Clear();
        var tree = Categories.GetTabs(tabShop);
        if (tree == null || tree.Count == 0) return;
        if (byserial.Count > 0) foreach (var p in byserial.Values) AddTabs(list, tree, p, 0);
        else AddTabs(list, tree, null, 0);
    }

    /// <summary>
    /// FUN_1413487e0 (unit_141347b10.cpp:286): each tab that lists the product (a tab with no category of its own and sub
    /// tabs takes any product; else one of the product's categories in the tab's list) or is static, added once
    /// (FindTabIndex), its depth set, the product added when it matched, then its sub tabs one level deeper.
    /// </summary>
    static void AddTabs(List<TabRow> list, IReadOnlyList<ShopTab> tree, Product p, int depth)
    {
        foreach (var tab in tree)
        {
            if (tab == null) continue;
            bool match = p != null && tab.Category.Length == 0 && tab.SubTabs.Count > 0;
            if (p != null && !match)
                foreach (var c in p.Categories)
                    if (IsCategoryInList(c, tab.Category)) { match = true; break; }
            if (!match && tab.Static == 0) continue;
            var row = list.Find(r => r.Tab == tab);
            if (row == null) list.Add(row = new TabRow { Tab = tab });
            row.Depth = depth;
            if (match) row.Products.Add(p);
            if (tab.SubTabs.Count > 0) AddTabs(list, tab.SubTabs, p, depth + 1);
        }
    }

    /// <summary>IsCategoryInList (unit_141347b10.cpp:228): the category is one of the list's comma separated entries
    /// (case-insensitive).</summary>
    public static bool IsCategoryInList(string category, string list)
    {
        foreach (var token in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (string.Equals(category, token, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// BuildCategoryList (unit_141347b10.cpp:335) and FUN_141348dd0: per tab row TabInfo::Write (id when set, symbol,
    /// priority, name, bestPage, alwaysOpen), depth, tabindex, space; then buildCategoryList(rows, deepest depth). No sale
    /// event (nSale 0).
    /// </summary>
    void BuildCategoryList(List<TabRow> list)
    {
        var rows = new List<object>();
        int maxDepth = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i].Tab;
            var o = new List<(string, object)>();
            if (t.Id != -1) o.Add(("id", t.Id));
            o.Add(("symbol", t.Symbol));
            o.Add(("priority", t.Priority));
            o.Add(("name", Model.Tables.CategoryName(t.NameId)));
            // IsShopTab (meratmarketbasketinfo.cpp:2336): ids 10 to 12
            if (t.Static == 1 && t.Id >= 10 && t.Id <= 12) o.Add(("bestPage", true));
            o.Add(("alwaysOpen", t.AlwaysOpen));
            o.Add(("depth", list[i].Depth));
            maxDepth = Math.Max(maxDepth, list[i].Depth);
            o.Add(("tabindex", (uint)i));
            o.Add(("space", 1));
            rows.Add(Host.Obj(o.ToArray()));
        }
        if (shop != null) Host.Invoke(shop, "buildCategoryList", Host.ToAs(rows), maxDepth);
    }

    /// <summary>UpdateProductList (@0x141b03700): the shown group's tabs rebuilt and shown, and with no tab the list built
    /// at once; another group's tabs only rebuilt.</summary>
    void UpdateProductList(int g)
    {
        if (group == g)
        {
            SetTab(g, true);
            if (g < 0 || g >= 3 || tabs[g].Count == 0) RefreshItems("");
        }
        else SetTab(g, false);
    }

    // ---- the product list ------------------------------------------------------------------------------------------

    /// <summary>
    /// RefreshItems (@0x141afbfb0): group 0 lists the shown category's products (shuffled for a random-order shop) that
    /// contain the search text, group 1 every bought-back product; then buildShopList(rows, shuffled). The star items, the
    /// new-items group and the give-point event are not ported.
    /// </summary>
    public void RefreshItems(string text)
    {
        var rows = new List<object>();
        var list = tabs[group];
        if (group == 0 && category >= 0)
        {
            bool searching = text.Length > 0;
            if (category < list.Count)
            {
                var items = list[category].Products;
                if (info.Shuffle) Shuffle(items);
                foreach (var p in items)
                {
                    if (p == null) continue;
                    if (searching && (p.Item == null || ItemName(p.Item).IndexOf(text, StringComparison.Ordinal) < 0)) continue;
                    if (PopulateProduct(p) is { } row) rows.Add(row);
                }
            }
        }
        else if (group == 1)
            foreach (var p in products[1].Values)
                if (PopulateProduct(p) is { } row) rows.Add(row);
        if (shop != null) Host.Invoke(shop, "buildShopList", DataProvider(rows), info.Shuffle);
        search = text;
    }

    static readonly Random shuffleRandom = new();

    /// <summary>ShuffleProducts (0x141b06ec0): from the end, each swapped with one at or before it (rand() % (i + 1)).</summary>
    static void Shuffle(List<Product> list)
    {
        for (int i = list.Count - 1; i >= 1; i--)
        {
            int j = shuffleRandom.Next(i + 1);
            if (i != j) (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>CItem::FUN_140527b10: the item's display name (ItemTooltip.Name; its branches not ported give "").</summary>
    string ItemName(Item item) => new ItemTooltip(Model, 0).Name(item) ?? "";

    /// <summary>The flags PopulateProduct builds the slot with: item, its lock state, count and no drag.</summary>
    public const int SlotFlags = 0x415;

    /// <summary>
    /// PopulateProduct (@0x141b00240): null without the item or its data, and (outside buy back, with "usable items only"
    /// ticked, unless paid in guild points) for an item the player cannot use or whose job is not recommended. Else the
    /// slot (FUN_1409c3860, 0x415), itemCount, remain, the grade-coloured label, level, the prices (and the discount),
    /// soldOut, displayOrder, meratItem, premiumSlotBG, saleTag, bgFrame, the requirements the player does not meet, serialID,
    /// itemID, productType, showRecipe and showPreview. The guild / achievement / championship / fame checks are not ported
    /// (their products show no requirement line).
    /// </summary>
    AsObject PopulateProduct(Product p)
    {
        if (p?.Item?.Data == null) return null;
        var item = p.Item;
        var data = item.Data;
        var r = p.Record;
        if (group != 1 && checkEnableItems && r.Currency != 7)
        {
            int use = ItemUseError?.Invoke(item) ?? 0;
            if (use != 0 && use != 1) return null;
            if (Model.HasLocalPlayer && !data.IsJobRecommended(Pc.JobGroup)) return null;
        }
        var slot = new List<(string, object)>();
        if (!ItemSlot.Build(slot, item, Model.HasLocalPlayer, SlotFlags, Model, Model.ItemSlotSources)) return Host.Obj(slot.ToArray());
        // the slot's itemCount set again (SetMember on an existing member keeps its place): GetStackCount as text (FUN_140719630)
        var count = ("itemCount", (object)StackCount(p).ToString(CultureInfo.InvariantCulture));
        int at = slot.FindIndex(m => m.Item1 == "itemCount");
        if (at >= 0) slot[at] = count; else slot.Add(count);
        string remain = r.Limit > 0 && r.Remaining > 0 ? Common("s_shop_remain_count", r.Remaining) : "";
        slot.Add(("remain", remain));
        uint color = Model.Tables.Items.TextColor(item.Rarity);
        slot.Add(("label", $"<FONT color='#{color:x}'>{ItemName(item)}</FONT>"));      // FUN_14183e930
        long sale = r.Price, price = r.OriginalPrice;
        slot.Add(("level", (uint)data.LevelLimit));                                      // CItem::GetRequiredLevel
        slot.Add(("salePrice", sale));
        slot.Add(("salePriceText", PriceText(r.Currency, r.CurrencyItemId, sale, r.IconTag)));
        if (sale != price && price > 0)
        {
            slot.Add(("price", price));
            slot.Add(("priceText", Number(price)));
            int percent = (int)(100 - sale * 100 / price);
            slot.Add(("bgSaleText", Common("s_common_percent", (uint)percent)));
        }
        slot.Add(("soldOut", r.SoldOut ? 1 : 0));
        slot.Add(("displayOrder", 9999));
        slot.Add(("meratItem", false));
        slot.Add(("premiumSlotBG", r.PremiumSlot));
        slot.Add(("saleTag", r.SaleTag));
        slot.Add(("bgFrame", BackgroundFrame(r.Label)));
        slot.Add(("serialID", r.Serial));
        slot.Add(("itemID", (uint)item.ItemId));
        slot.Add(("productType", 0));                                                     // m_1c: 0 for CNpcShopProductCli
        slot.Add(("showRecipe", data.MaidId > 0));
        slot.Add(("showPreview", data.FunctionName == "UGCMapPackage"));
        return Host.Obj(slot.ToArray());
    }

    /// <summary>GetStackCount (@0x141b04700): a stackable item's count (the bundle), else 0.</summary>
    static int StackCount(Product p) => p.Item?.Data is { IsStackable: true } ? (int)p.Item.Count : 0;

    /// <summary>SetProductBackground (@0x141b02450): the row frame by the product's label (2 premium, 4 hot, 5-7 the black
    /// friday ones), else normal; the CN spring event's is not ported.</summary>
    static string BackgroundFrame(int label) => label switch
    {
        2 => "premium",
        4 => "hot",
        5 => "bf_1",
        6 => "bf_2",
        7 => "bf_3",
        _ => "normal",
    };

    /// <summary>
    /// FUN_141349f30 (unit_141347b10.cpp:562): the price text of a currency kind: with an icon tag FUN_1418fee40, else
    /// FUN_1418fec90 (uicommon.cpp:912): an item price s_common_payitem (amount, item), else s_common_payment (amount, the
    /// currency's icon and its vertical offset). FUN_1403baee0 maps the kind to the currency.
    /// </summary>
    public string PriceText(int kind, int itemId, long amount, string iconTag)
    {
        int currency = CurrencyOf(kind);
        if (!string.IsNullOrEmpty(iconTag))
        {
            // FUN_1418fee40: an item price with the tag; else FUN_1418fec00 (the red merat swap is not ported)
            if (currency == 2) return Common("s_common_payitem", Number(amount), iconTag);
            if (currency == 3) throw new NotImplementedException("merat price text (FUN_1418fec00)");
        }
        if (currency == 2) return Common("s_common_payitem", Number(amount), (uint)itemId);
        var (icon, vspace) = CurrencyIcon(currency, itemId);
        return icon.Length > 0 ? Common("s_common_payment", Number(amount), icon, vspace) : "";
    }

    /// <summary>FUN_1403baee0 (gameentitycommon.cpp:268): a shop's currency kind as the ui currency.</summary>
    public static int CurrencyOf(int kind) => kind switch
    {
        0 => 1, 1 => 2, 2 => 4, 3 => 5, 4 => 3, 5 => 6, 6 => 7, 7 => 11, 8 => 12, 9 => 10, 10 => 9, 11 => 13, 12 => 14,
        13 => 15, 14 => 16, _ => 17,
    };

    /// <summary>FUN_1418fe870 (uicommon.cpp:816): a currency's icon and its vertical offset. The red / rainbow merat icons
    /// (features) are not ported: 9 and 10 throw.</summary>
    public static (string Icon, int VSpace) CurrencyIcon(int currency, int itemId) => currency switch
    {
        0 => ("export_common_icon_cash_r", -4),
        1 => ("export_common_icon_meso_h21", -6),
        2 => ("export_common_icon_item_" + ((uint)itemId).ToString(CultureInfo.InvariantCulture), -6),
        3 => ("export_common_icon_merat", -4),
        4 => ("export_common_icon_honor", -6),
        5 => ("export_common_icon_treva", -6),
        6 => ("export_common_icon_lu", -6),
        7 => ("export_common_icon_habi", -6),
        9 or 10 => throw new NotImplementedException("merat icon by feature"),
        0xb => ("export_icon_guild_fund", -4),
        0xc => ("export_common_icon_reverse_coin", -7),
        0xd => ("export_common_icon_mentor_token", -4),
        0xe => ("export_common_icon_mentee_token", -4),
        0xf => ("export_common_icon_star_point", -6),
        0x10 => ("export_common_icon_meso_market_token", -4),
        _ => ("", -4),
    };

    // ---- the reset bar ---------------------------------------------------------------------------------------------

    /// <summary>UpdateSerial (@0x141afd480): with the reset on, updateTimer(seconds left, capped at the interval; the
    /// interval, or ShopSeedResetTime without one).</summary>
    void UpdateSerial(long resetTime)
    {
        if (shop == null || info.ResetType == 99 || !info.ResetEnable) return;
        uint interval = (uint)info.ResetInterval;
        if (interval == 0) interval = (uint)ShopSeedResetTime;
        long now = ServerTime();
        ulong left = 0;
        if (now > 0 && resetTime > now) left = (ulong)(resetTime - now);
        if ((long)left > interval) left = interval;
        Host.Invoke(shop, "updateTimer", Host.ToAs((uint)left), Host.ToAs(interval));
    }

    /// <summary>RefreshResetPrice (@0x141afd5d0): updateResetData(shown, price, original price when discounted).</summary>
    void RefreshResetPrice()
    {
        if (shop == null || !info.ResetEnable || info.ResetType == 99) return;
        UpdateMoney();
        bool show = !info.HideReset;
        string price = "", original = "";
        if (show)
        {
            if (resetPrice != info.ResetPrice) original = Number(info.ResetPrice);
            price = PriceText(info.ResetCurrency, info.ResetCurrencyItem, resetPrice, "");
        }
        Host.Invoke(shop, "updateResetData", show, price, original);
    }

    /// <summary>UpdateMoney (@0x141b03800): the reset price; the reset-price sale event is not ported, so its original.</summary>
    void UpdateMoney()
    {
        if (!info.ResetEnable) return;
        resetPrice = info.ResetPrice;
    }

    // ---- the movie's calls -----------------------------------------------------------------------------------------

    /// <summary>
    /// vfunc_28 (@0x141af5730) after UIDialog's: OnChangeTab, OnChangeCategory, OnCheckEnableItems and OnSearch; OnBuyItem,
    /// OnSell and OnResetList are raised as <see cref="Request"/>; the preview, link, recipe and equip calls are not ported.
    /// </summary>
    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onchangetab":
                if (shop == null) return true;
                SetNew(group, false);                                    // ChangeTab (@0x141afd840): SelectTab(old, 0)
                group = ToInt(Arg(args, 0));
                if (group >= 0 && group < 3) UpdateProductList(group);
                return true;
            case "onchangecategory":
                category = ToInt(Arg(args, 0));
                RefreshItems("");
                return true;
            case "oncheckenableitems":
                checkEnableItems = Rt.ToBoolean(Arg(args, 0));
                if (group == 0) RefreshItems("");
                return true;
            case "onsearch":
                Search(Arg(args, 0) as string ?? Convert.ToString(Arg(args, 0), CultureInfo.InvariantCulture) ?? "");
                return true;
            case "onbuyitem":
                Request?.Invoke("buy", new[] { Host.Get(Arg(args, 0), "serialID"), Arg(args, 1) });
                return true;
            case "onsell":
                Request?.Invoke("sell", new[] { Arg(args, 0) });
                return true;
            case "onresetlist":
                Request?.Invoke("reset", new object[] { resetPrice });
                return true;
        }
        return false;
    }

    static object Arg(object[] a, int i) => a != null && i < a.Length ? a[i] : null;
    static int ToInt(object v) => v switch { int i => i, uint u => (int)u, double d => (int)d, _ => Convert.ToInt32(v, CultureInfo.InvariantCulture) };

    /// <summary>Search (@0x141afb590): not in buy back; nothing for an empty text on an empty search; a text is listed once
    /// the ban-word check passes (CClientPlatform vfunc_37, not ported: taken as passed).</summary>
    void Search(string text)
    {
        if (group == 1) return;
        if (search.Length == 0 && text.Length == 0) return;
        RefreshItems(text);
    }

    /// <summary>SelectTab (@0x141afef80): setNew(group, new).</summary>
    void SetNew(int g, bool on)
    {
        if (shop != null) Host.Invoke(shop, "setNew", g, on);
    }

    // ---- tooltips --------------------------------------------------------------------------------------------------

    /// <summary>
    /// vfunc_49 (@0x141af6b10): m_resetAni the time to the reset (days / hours / minutes / seconds strings 0x304-0x307), plus
    /// the by-account note; m_resetListButton the restock text; a product row its item's tooltip (FUN_141349210 with
    /// ASTooltip flags 4; gd's item tooltip body, <see cref="ItemTooltip.Fill"/>). The merat and gacha rows are not ported.
    /// </summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        string name = Arg(args, 0) as string ?? "";
        if (name == "m_resetAni")
        {
            long now = ServerTime();
            if (now == 0 || info.ResetTime < now) return null;
            long left = info.ResetTime - now;
            if (left <= 0) return null;
            var span = TimeSpan.FromSeconds(left);
            // SplitSeconds (@0x141b05100); GetStringCommon 0x304-0x307 = s_shop_reset_remain_* (commonstringtable.cpp)
            string text = span.Days > 0 ? Common(CommonKey(0x304), span.Days, span.Hours, span.Minutes, span.Seconds)
                : span.Hours > 0 ? Common(CommonKey(0x305), span.Hours, span.Minutes, span.Seconds)
                : span.Minutes > 0 ? Common(CommonKey(0x306), span.Minutes, span.Seconds)
                : Common(CommonKey(0x307), span.Seconds);
            if (info.ResetByAccount) text += Common("s_shop_tooltip_notice_reset_by_account");
            var tip = AsTooltip.Text(text);
            tip?.SetTooltipAlign(5);
            return tip;
        }
        if (name == "m_resetListButton")
        {
            string text;
            if (info.ResetLimited)
            {
                var (icon, vspace) = CurrencyIcon(CurrencyOf(info.ResetTooltipCurrency), info.ResetCurrencyItem);
                text = Common("s_shop_tooltip_lua_reset", (uint)info.ResetCount, $"<img src='{icon}' vspace='{vspace}'/>");
            }
            else text = Common("s_shop_tooltip_reset");
            var tip = AsTooltip.Text(text);
            tip?.SetTooltipAlign(4);
            return tip;
        }
        if (name == "m_coinInfo") return null;
        var row = Arg(args, 1);
        if (row == null || row is Undefined) return null;
        int serial = ToInt(Host.Get(row, "serialID"));
        int type = ToInt(Host.Get(row, "productType"));
        if (type != 0 && type != 2) return null;
        if (group < 0 || group >= 3 || !products[group].TryGetValue(serial, out var p) || p.Item == null) return null;
        var t = new ItemTooltip(Model, 4);
        return t.Fill(p.Item) ? t.Data : null;
    }

    /// <summary>The common string keys of the reset countdown ids.</summary>
    static string CommonKey(int id) => id switch
    {
        0x304 => "s_shop_reset_remain_day",
        0x305 => "s_shop_reset_remain_hour",
        0x306 => "s_shop_reset_remain_min",
        _ => "s_shop_reset_remain_sec",
    };
}
