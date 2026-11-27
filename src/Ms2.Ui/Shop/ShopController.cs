using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Shop;

/// <summary>
/// UIShopDialog (client/ms2/uishopdialog.cpp; UI id 0x4e3b), the C++ side of the npc shop movie uishopdialog. Its root
/// (ShopDialogProxy) makes the shop's own dialog by the shop type (initShop), which the movie's ShopDialog code then runs:
/// the tab bar, the category list, the product list and the restock bar. This feeds it Inbound 0x51 (commands 0, 1, 2, 4,
/// 6, 7, 8, 9, 11, 12, 13, 14, 15), the tabs and category tree (CShopCategoryTable), the product rows, tooltips (vfunc_49),
/// vfunc_28 callbacks, and the buy, buyback, sell, restock and new-items flows over Outbound 0x28 (<see cref="Send"/>).
/// <para>STILL-OPEN: the merat bill dialog's unported parts (see MarketBillDialogController), the avatar's unported previews (see ShopAvatarPreview),
/// search ban-word check (vfunc_80), give-point event, and live state sources for fame grade, guild championship
/// season rank table, and guild NPC levels (their buy gates refuse as the client does when state is absent).</para>
/// </summary>
public sealed partial class ShopController : UiController, Hud.IMessageBoxListener
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
    /// (slot key), "reset", "list", "repurchase".</summary>
    public event Action<string, object[]> Request;
    /// <summary>CUIService::OpenDialog of another window: the token pocket 0x4ece when the header asks for it.</summary>
    public event Action<int> OpenDialog;
    /// <summary>vfunc_66 with the inventory closed: the "openWindowPos" message to the inventory (0x4e2d) with this id.</summary>
    public event Action<int> InventoryWindowPos;

    /// <summary>FUN_140710590: sends Outbound 0x28 (<see cref="Ms2.Net.Outbound.Shop"/>) packets.</summary>
    public Action<Ms2.Net.Wire.IOutPacket> Send;
    /// <summary>UITalkManager::SendNpcTalkMode0 (vfunc_67, uishopdialog.cpp:711).</summary>
    public Action SendNpcTalkEnd;
    /// <summary>CNetworkService::SendSystemShop(sortKind, 0) (vfunc_67, uishopdialog.cpp:712-713).</summary>
    public Action<int, int> SendSystemShop;
    /// <summary>Unk_Inventory::FUN_140a5f180(itemId, rank): owned count of an item currency.</summary>
    public Func<int, int, long> ItemCount = (_, _) => 0;
    /// <summary>Unk_Inventory::FUN_140a5f7b0(tag, 0): owned count of items matching a currency tag.</summary>
    public Func<int, long> ItemTagCount = _ => 0;
    /// <summary>Unk_Inventory::GetEmptySlotCount(tab): empty inventory slots for cube-mesh max buy count.</summary>
    public Func<int, int> EmptySlotCount = _ => 1;

    /// <summary>The inventory tab of an item id (FUN_140ec2ac0 over the inventory) and the tab's name (GetInventoryTabName); -1 without an inventory.</summary>
    public Func<int, int> ItemTab = _ => -1;
    public Func<int, string> InventoryTabName = _ => "";
    /// <summary>CGuildManager::m_info.GetFund() (uicommon.cpp:999, :1050).</summary>
    public Func<long> GuildFund = () => 0;
    /// <summary>Resolves a slot binding name ("InventoryDialog.&lt;i&gt;") for OnSell (uishopdialog.cpp:2533).</summary>
    public Func<string, Item> FindInventorySlotItem;
    /// <summary>InvenSlot::FUN_14140be60(pItem): sells an inventory slot item through the inventory's sell dialog flow.</summary>
    public Action<Item> SellInventoryItem;
    /// <summary>PCBase::FUN_141676b00(uid) (itemugcmesh.cpp:1091): true if the pet item is currently summoned.</summary>
    public Func<long, bool> IsSummonedPet = _ => false;
    /// <summary>FUN_140c8dab0(GetInstrumentManager()) (uishopdialog.cpp:2100): uid of the score currently playing.</summary>
    public Func<long> PlayingScoreUid = () => 0;
    /// <summary>CContentShutdown::Get(10) (uishopdialog.cpp:2080): shop content shutdown flag.</summary>
    public bool ShopShutdown;
    /// <summary>CGameEventValueSaleItemsRestricted (uishopdialog.cpp:2109): item restricted from selling.</summary>
    public Func<int, bool> IsSaleRestricted = _ => false;
    /// <summary>ShowSystemNotice(text, 1, 1) (uishopdialog.cpp:1767, :1864, :2085).</summary>
    public Action<string> SystemNotice;
    /// <summary>FUN_1412751b0(text, type) (uishopdialog.cpp:1850): guild storage purchase notice (type 0x14).</summary>
    public Action<string, int> NoticeWithType;
    /// <summary>FUN_140d73c60 / ShowMessageBox: shows a message box (defaults to <see cref="Hud.MessageBoxQueue.Show"/>).</summary>
    public Action<Hud.MessageBoxData> ShowBox = Hud.MessageBoxQueue.Show;
    /// <summary>FUN_14151f200: closes the current modal message box (defaults to <see cref="Hud.MessageBoxQueue.Next"/>).</summary>
    public Action CloseBox = Hud.MessageBoxQueue.Next;
    /// <summary>FUN_14151f820: the current modal message box (defaults to <see cref="Hud.MessageBoxQueue.Current"/>).</summary>
    public Func<Hud.MessageBoxData> CurrentBox = Hud.MessageBoxQueue.Current;
    /// <summary>PlaySystemSound (shop_itembuy, shop_reset, system_notice_error_warningBag).</summary>
    public Action<string> Sound;
    /// <summary>The local player's equipment record (MyPC) for compare tooltips (vfunc_49, uishopdialog.cpp:1303).</summary>
    public Func<PcInfo> Wearer;
    /// <summary>Shift key held (compare tooltip secondary slot).</summary>
    public Func<bool> ShiftHeld = () => false;
    /// <summary>ItemPreviewMgr::IsHudOn (uishopdialog.cpp:1042, :1056, :2599).</summary>
    public Func<bool> IsPreviewHudOn = () => false;
    /// <summary>FUN_140d2d230 (uishopdialog.cpp:2569): link a normal shop product (itemId, rarity, shopId, serial) into chat.</summary>
    public Action<int, int, int, int> LinkShopItem;
    /// <summary>FUN_140d2d150 (uishopdialog.cpp:2571): link a new-tab item (itemId, rarity) into chat.</summary>
    public Action<int, int> LinkPlainItem;
    /// <summary>ItemPreviewMgr::OpenPreview(item, 1) (uishopdialog.cpp:2603).</summary>
    public Action<Item> PreviewItem;
    /// <summary>Gacha shop preview (uishopdialog.cpp:3448): try an item on the preview avatar.</summary>
    public Action<Item> PreviewGachaItemRequested;
    /// <summary>Gacha shop main items for the active category (RefreshCategories, uishopdialog.cpp:2698-2731: m_spPreview->InitWithItems).
    /// The shop's own ItemPreview (new ItemPreview(pMyPC, this, 1), uishopdialog.cpp:888) is <see cref="Preview"/>; the HUD's avatar view
    /// follows it (see ShopController.Avatar.cs), so this event is for a listener that wants the list itself.</summary>
    public Action<IReadOnlyList<Item>> PreviewMainItems;
    /// <summary>onClickUnequipAll (uishopdialog.cpp:3489).</summary>
    public Action PreviewUnequipAll;
    /// <summary>onClickShowMyEquip (uishopdialog.cpp:3479).</summary>
    public Action PreviewShowMyEquip;
    /// <summary>OnUnEquip (uishopdialog.cpp:3507): equip slot index and item uid.</summary>
    public Action<int, ulong> PreviewUnequipSlot;
    /// <summary>onClickRecipe (uishopdialog.cpp:1135): maid id, x, y for UIMaidRecipeListDialog (0x4ecb).</summary>
    public Action<int, float, float> OpenMaidRecipe;
    /// <summary>onClickPreview (uishopdialog.cpp:1165): item id, x, y for UIInteriorPreviewDialog (0x4ee3).</summary>
    public Action<int, float, float> OpenInteriorPreview;
    /// <summary>FUN_1418ffba0 (uishopdialog.cpp:974): true when buying a UGCMapPackage requires the 0x1f confirm box.</summary>
    public Func<ItemData, bool> CanBuyUgcPackage;

    /// <summary>The local player's UGC map info as FUN_1418ffba0 reads it (uicommon.cpp:1183-1209): the plot id (m_nField1c),
    /// the indoor size type of that plot (FUN_1408a0d30, null when the plot has none) and MyPC::HasNoHome. Null result =
    /// the client's GetUGCMapInfo is null. STILL-OPEN: the live session does not carry the home state, so the live wiring
    /// leaves this unset and <see cref="NeedsUgcAgreement"/> asks for the agreement box (the client would buy at once only
    /// when the package fits the player's home).</summary>
    public Func<UgcHomeState> UgcHome;
    /// <summary>The indoor size type of a UGCMapPackage item (GetUGCMapPackageData(function arg 0)->m_strIndoorSizeType);
    /// STILL-OPEN: the package table is not ported, null = unknown.</summary>
    public Func<ItemData, string> PackageIndoorSize;

    public readonly record struct UgcHomeState(bool Known, int PlotId, string PlotIndoorSize, bool HasNoHome);

    /// <summary>FUN_1418ffba0 (uicommon.cpp:1183-1209): the agreement box is needed when the package's indoor size differs
    /// from the player's plot, or the player has a home but no plot. State not wired = ask (never buy silently).</summary>
    public bool NeedsUgcAgreement(ItemData data)
    {
        if (CanBuyUgcPackage != null) return CanBuyUgcPackage(data);
        if (data == null) return false;
        if (UgcHome == null) return true;
        var home = UgcHome();
        if (!home.Known) return false;                    // GetUGCMapInfo() == 0 -> return 0
        if (home.PlotIndoorSize != null)
        {
            string package = PackageIndoorSize?.Invoke(data);
            if (package != null && package != home.PlotIndoorSize) return true;
        }
        return home.PlotId == 0 && !home.HasNoHome;
    }
    /// <summary>CAchieveManager::IsBeforeStartGrade (uishopdialog.cpp:1295, :3599): true when player meets achieve
    /// (defaults false so absent state refuses as the client does).</summary>
    public Func<int, int, bool> HasAchieve = (_, _) => false;
    /// <summary>FUN_14093b9c0 (uishopdialog.cpp:3603): the achievement grade title shown in the gate notice.</summary>
    public Func<int, int, string> RequireAchieveText = (_, _) => "";
    /// <summary>GetRequireAchieveText (achievemanager.cpp:942, uishopdialog.cpp:1298): the formatted requirement line for the tooltip.</summary>
    public Func<int, int, string> RequireAchieveTooltipText = (_, _) => "";
    /// <summary>FUN_14093bad0 (achievemanager.cpp:918): the trophy's title in the given format, "" without its data.</summary>
    public Func<int, int, string, string> TaggedAchieveTitle = (_, _, _) => "";
    /// <summary>CGuildManager::IsInGuild (uishopdialog.cpp:3568, :3644, :3683, :3703).</summary>
    public Func<bool> IsInGuild = () => false;
    /// <summary>CGuildManager::IsMaster(playerName) (uishopdialog.cpp:3703).</summary>
    public Func<bool> IsGuildMaster = () => false;
    /// <summary>CGuildManager::GetTotalTrophy (uishopdialog.cpp:3576).</summary>
    public Func<int> GuildTotalTrophy = () => 0;
    /// <summary>CGuildMember::fc8 join timestamp in seconds, or null when local member is not found (uishopdialog.cpp:3573-3578).</summary>
    public Func<long?> GuildMemberJoinTime = () => null;
    /// <summary>CGuildSeason::GetRankForScore(CGuildManager::GetScore) (uishopdialog.cpp:3646-3647; STILL-OPEN: defaults 0).</summary>
    public Func<int> GuildChampionshipRank = () => 0;
    /// <summary>CStringTable(0x2d, rank) (uishopdialog.cpp:3648; STILL-OPEN).</summary>
    public Func<int, string> GuildChampionshipRankName = _ => "";
    /// <summary>CGuildMember::fe0 championship score, or null when local member is not found (uishopdialog.cpp:3660-3663).</summary>
    public Func<int?> GuildMemberChampionshipScore = () => null;
    /// <summary>CGuildManager::GetNpcLevel(npcId) (uishopdialog.cpp:3685; STILL-OPEN: defaults 0).</summary>
    public Func<int, int> GuildNpcLevel = _ => 0;
    /// <summary>Fame alliance grade (alliance != 0) or general fame grade (alliance == 0) (uishopdialog.cpp:3619-3622; STILL-OPEN: the session keeps no fame points, so the default 0 stands and every fame-gated product is refused).</summary>
    public Func<int, int> FameGradeOf = _ => 0;

    /// <summary>Unk_Inventory::FUN_140a5f7b0 (inventory.cpp:835): sums item counts across <paramref name="items"/>
    /// matching string tag <paramref name="tag"/> that the player can use; a nonzero <paramref name="rarity"/> counts only the items
    /// of that rarity (:846).</summary>
    public static long CountItemsByTag(IEnumerable<EquipItem> items, ItemDataTable table, int tag, Func<Item, int> itemUseError = null, int rarity = 0)
    {
        if (tag == 0 || items == null || table == null) return 0;
        long total = 0;
        foreach (var e in items)
        {
            if (e == null || e.Amount <= 0) continue;
            var data = table.Get(e.Id);
            if (data == null || ItemTags.ByName(data.StringTag) != tag) continue;
            if (itemUseError != null)
            {
                var it = Item.Create(table, e.Id, e.Rarity);
                if (it != null) { it.Record = e; it.Count = (uint)e.Amount; if (itemUseError(it) != 0) continue; }
            }
            if (rarity != 0 && e.Rarity != rarity) continue;
            total += e.Amount;
        }
        return total;
    }

    /// <summary>Wires the live session's guild, inventory, pet, playing-score, and achievement state onto this controller
    /// (uishopdialog.cpp:2099, :2429-2454, :3563-3709; itemugcmesh.cpp:1091; inventory.cpp:835; achievemanager.cpp:447).</summary>
    public void BindLiveState(
        Func<GuildBook> guilds,
        Func<AchieveBook> achieves,
        Func<AchieveTexts> achieveTexts,
        Func<MyCharacter> me,
        Func<IEnumerable<EquipItem>> items,
        Func<long> playingScoreUid,
        ItemDataTable itemTable,
        Func<Item, int> itemUseError = null)
    {
        ItemCount = (id, _) => items?.Invoke()?.Where(e => e.Id == id).Sum(e => (long)e.Amount) ?? 0;
        ItemTagCount = tag => CountItemsByTag(items?.Invoke(), itemTable, tag, itemUseError);
        GuildFund = () => guilds?.Invoke()?.Info?.Fund ?? 0;
        IsInGuild = () => guilds?.Invoke()?.InGuild ?? false;
        IsGuildMaster = () => me?.Invoke()?.User?.Name is { } name && (guilds?.Invoke()?.IsMaster(name) ?? false);
        GuildTotalTrophy = () => guilds?.Invoke()?.TotalTrophy ?? 0;
        GuildMemberJoinTime = () => me?.Invoke()?.User?.Name is { } name && guilds?.Invoke()?.FindMemberByName(name) is { } m ? m.Fc8 : null;
        GuildMemberChampionshipScore = () => me?.Invoke()?.User?.Name is { } name && guilds?.Invoke()?.FindMemberByName(name) is { } m ? m.Fe0 : null;
        IsSummonedPet = uid => uid != 0 && me?.Invoke()?.Pet is { } pet && pet.Uid == uid;
        PlayingScoreUid = playingScoreUid ?? (() => 0);
        HasAchieve = (id, grade) => achieves?.Invoke()?.Find(id) is { } a && a.StartGrade > grade;
        RequireAchieveText = (id, grade) => achieveTexts?.Invoke()?.GradeTitle(id, grade) ?? "";
        RequireAchieveTooltipText = (id, grade) => achieveTexts?.Invoke()?.RequireText(id, grade) ?? "";
        TaggedAchieveTitle = (id, grade, format) => achieveTexts?.Invoke()?.TaggedTitle(id, grade, format) ?? "";
    }

    /// <summary>Wires the live session's Shop (0x28), SystemShop (0x65), and NpcTalk end sends
    /// (uishopdialog.cpp:711-714, networkservice.cpp:530).</summary>
    public void BindLiveNetwork(Action<Ms2.Net.Wire.IOutPacket> sendShop, Action sendNpcTalkEnd)
    {
        Send = sendShop;
        SendNpcTalkEnd = sendNpcTalkEnd;
        SendSystemShop = (type, state) => sendShop?.Invoke(new SystemShopRequest((byte)type, (byte)state));
    }

    /// <summary>UIShopDialog::Selection (uishopdialog.cpp:551): tracks the pending request's serial, list index and command.</summary>
    struct PendingSelection
    {
        public int Kind, Index, Secondary;
        public void Reset() { Kind = 0; Index = -1; Secondary = -1; }
        public void ResetIf(int cmd) { if (Secondary == cmd) Reset(); }
        public void Set(int kind, int index, int secondary) { Kind = kind; Index = index; Secondary = secondary; }
    }

    /// <summary>A product as the dialog keeps it (CNpcShopProductCli / CNpcShopProductNewCli): the packet's record,
    /// its item and its kind (m_1c: 0 normal/repurchase, 2 new-tab).</summary>
    public sealed class Product
    {
        public ShopProduct Record;
        public Item Item;
        /// <summary>m_1c: 0 for CNpcShopProductCli, 2 for CNpcShopProductNewCli.</summary>
        public int ProductKind;
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
    int sortKind;           // m_nSortKind
    bool checkEnableItems = true;   // m_bCheckEnableItems (ctor 1)
    bool headerRead;        // m_188
    bool resetPending;      // m_18b
    bool ready;             // m_bReady
    long resetCheckTime;    // m_1a0
    int resetPrice;         // m_nResetPrice
    int repurchaseCount;    // m_2c8
    bool repurchaseEmpty;   // m_bRepurchaseEmpty
    int newTabShopId;       // m_288
    int newTotal;           // m_nNewTotal
    bool newComplete;       // m_bNewComplete
    string search = "";     // m_strSearch
    object shop;            // m_spShop
    internal object Dialog => shop;
    PendingSelection selection = new() { Index = -1, Secondary = -1 };
    bool shopEnabled = true;

    /// <summary>The model the shop's data reads before the dialog's movie has a host: the header and the items arrive ahead of the
    /// talk window's answer that opens the dialog (CNpc::OpenTalkUi, uitalkcinematic.cpp:1474), and the client's UIShopDialog object
    /// exists, with its tables, before its movie loads.</summary>
    public UiModel BoundModel;
    new UiModel Model => Host?.Model ?? BoundModel;
    new PlayerState Pc => Model.Player;

    public ShopInfo Info => info;
    /// <summary>The shop's own dialog initShop made (m_spShop).</summary>
    public object ShopView => shop;
    public int Group => group;
    public int Category => category;
    public int SortKind { get => sortKind; set => sortKind = value; }
    public bool CheckEnableItems => checkEnableItems;
    public bool ShopEnabled => shopEnabled;
    public IReadOnlyList<TabRow> Tabs(int g) => tabs[g];
    public IReadOnlyDictionary<int, Product> Products(int g) => products[g];

    string Common(string key, params object[] args) => Ms2.Quest.ClientFormat.Format(Model.Tables.Common(key) ?? "", args);
    string Common(int id, params object[] args) => Ms2.Quest.ClientFormat.Format(Model.Tables.Common(id) ?? "", args);
    static string Number(long n) => n.ToString("N0", CultureInfo.InvariantCulture);    // FUN_1409404f0

    // ---- packets ---------------------------------------------------------------------------------------------------

    /// <summary>OnShopPacket (@0x141af8710, uishopdialog.cpp:1524): dispatches Inbound 0x51 sub-commands and clears
    /// matching pending selection state.</summary>
    public void OnShopPacket(ShopPacket p)
    {
        if (p == null) return;
        switch (p.Command)
        {
            case 0: ReadShopInfo(p.Info); break;
            case 1: ReadProducts(p.Products); break;
            case 2: ReadProductUpdate(p.ProductUpdate); break;
            case 4: ReadPurchaseResult(p.PurchaseResult); break;
            case 6: ReadRepurchaseStart(p.RepurchaseCount); break;
            case 7: ReadRepurchaseAdd(p.Products); break;
            case 8: ReadRepurchaseRemoval(p.RemoveSerial); break;
            case 9: ReadResetResult(p.ResetResult); break;
            case 11: ReadMeratStart(p.MeratCount); break;
            case 12: ReadMeratItems(p.MeratItems); break;
            case 13: ReadNewStart(p.NewStart); break;
            case 14: ReadNewProducts(p.NewProducts); break;
            case 15: ReadShopError(p.Error); break;
        }
        UpdateGachaCoin();
        selection.ResetIf(p.Command);
    }

    /// <summary>CNpcShopMeratItemCli (npcshopmeratitem): a star-shop product, its display order and the preview item it
    /// builds once (FUN_141b05180).</summary>
    public sealed class MeratItem
    {
        public PremiumProduct Product;
        public int DisplayOrder;
        public Item Preview;
    }

    readonly List<MeratItem> meratItems = new();   // m_meratItems

    /// <summary>ReadMeratStart (@0x141af9700, uishopdialog.cpp:1710): with a shop, clears the star items and reserves the count.</summary>
    public void ReadMeratStart(int count)
    {
        if (info.ShopId == 0) return;
        meratItems.Clear();
        if (count > 0) meratItems.Capacity = Math.Max(meratItems.Capacity, count);
    }

    /// <summary>ReadMeratItems (@0x141af9790, uishopdialog.cpp:1722): with a shop, appends each star item.</summary>
    public void ReadMeratItems(IReadOnlyList<ShopMeratItem> list)
    {
        if (info.ShopId == 0 || list == null) return;
        for (int i = 0; i < list.Count; i++)
            if (list[i]?.Product != null)
                meratItems.Add(new MeratItem { Product = list[i].Product, DisplayOrder = list[i].DisplayOrder });
    }

    /// <summary>FindMeratItem (@0x141affd40, uishopdialog.cpp:2797): the star item whose product id is the serial.</summary>
    public MeratItem FindMeratItem(int serial)
    {
        for (int i = 0; i < meratItems.Count; i++)
            if (meratItems[i].Product.Id == serial) return meratItems[i];
        return null;
    }

    /// <summary>RefreshPremium (@0x141b037a0, uishopdialog.cpp:3297): with star items, group 0 lists again.</summary>
    public void RefreshPremium()
    {
        if (meratItems.Count == 0) return;
        if (group == 0) RefreshItems(search);
    }

    /// <summary>ReadRepurchaseStart (@0x141af9ce0, uishopdialog.cpp:1778): clears group 1 and records expected count.</summary>
    public void ReadRepurchaseStart(int count)
    {
        ResetProducts(1);
        repurchaseCount = count;
        repurchaseEmpty = count == 0;
    }

    /// <summary>ReadRepurchaseAdd (@0x141af9d70, uishopdialog.cpp:1788): adds sold items into group 1, invokes addItem and
    /// setNew(1, true) for live additions, and refreshes when the announced count arrives.</summary>
    public void ReadRepurchaseAdd(IReadOnlyList<ShopProduct> list)
    {
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            var item = Item.Create(Model.Tables.Items, r.ItemId, r.Rarity);
            if (item != null)
            {
                item.Count = (uint)Math.Max(r.Count, 0);
                item.Slot = products[1].Count;
                item.Record = r.Item;
                if (r.Item != null) { item.EnchantLevel = r.Item.Enchant.Level; item.UnlimitedEnchantLevel = r.Item.LimitBreak.Level; }
            }
            var p = new Product { Record = r, Item = item, Categories = Array.Empty<string>() };
            products[1].TryAdd(r.Serial, p);
            if (repurchaseEmpty) RepurchaseAdded(p);
        }
        if (repurchaseEmpty) SetNew(1, true);
        if (repurchaseCount == products[1].Count)
        {
            EnableShop(true);
            if (group == 1 && ready) RefreshItems("");
            repurchaseEmpty = true;
        }
    }

    /// <summary>RepurchaseAdded (@0x141afb320, uishopdialog.cpp:2004): appends a newly sold item to the buyback view.</summary>
    void RepurchaseAdded(Product p)
    {
        if (group == 1 && shop != null && PopulateProduct(p) is { } row)
            Host.Invoke(shop, "addItem", row);
    }

    /// <summary>ReadRepurchaseRemoval (@0x141afa130, uishopdialog.cpp:1819): re-enables shop, removes product from group 1,
    /// and invokes removeItem(serial, index).</summary>
    public void ReadRepurchaseRemoval(int serial)
    {
        EnableShop(true);
        if (!products[1].Remove(serial, out _)) return;
        if (group == 1 && shop != null)
        {
            int idx = selection.Secondary == 8 ? selection.Index : -1;
            Host.Invoke(shop, "removeItem", serial, idx);
        }
    }

    /// <summary>ReadNewStart (@0x141af9390, uishopdialog.cpp:1664): starts group 2 when npc and shop ids match.</summary>
    public void ReadNewStart(ShopNewStart s)
    {
        if (info.ShopId == 0 || s == null || s.NpcId != info.NpcId || s.ShopId != info.ShopId) return;
        ResetProducts(2);
        newComplete = false;
        newTabShopId = s.TabShopId;
        newTotal = s.TotalCount;
    }

    /// <summary>ReadNewProducts (@0x141af94e0, uishopdialog.cpp:1683): adds CNpcShopProductNewCli entries to group 2 and
    /// rebuilds tabs once all announced items arrive.</summary>
    public void ReadNewProducts(IReadOnlyList<ShopNewProduct> list)
    {
        if (info.ShopId == 0 || list == null) return;
        int first = products[2].Count;
        for (int i = 0; i < list.Count; i++)
        {
            var np = list[i];
            int serial = first + i;
            var item = Item.Create(Model.Tables.Items, np.ItemId, np.Rarity);
            if (item != null) item.Count = 1;
            var rec = new ShopProduct
            {
                Serial = serial, ItemId = np.ItemId, Rarity = np.Rarity, Categories = np.Categories,
                Label = np.ProductType, SaleTag = np.ProductKind, Period = np.Period,
            };
            products[2].TryAdd(serial, new Product { Record = rec, Item = item, ProductKind = 2, Categories = Split(np.Categories) });
        }
        if (products[2].Count >= newTotal)
        {
            newComplete = true;                     // ReadNewProducts (uishopdialog.cpp:1683-1704) does not re-enable the shop
            UpdateProductList(2);
        }
    }

    /// <summary>ReadProductUpdate (@0x141af98d0, uishopdialog.cpp:1740): re-enables shop, updates product buy count and
    /// redraws the selected row via updateItem.</summary>
    public void ReadProductUpdate(ShopProductUpdate u)
    {
        if (u == null) return;
        EnableShop(true);
        if (products[0].TryGetValue(u.Serial, out var p))
        {
            p.Record.Bought = u.BuyCount;
            UpdateProduct(p);
        }
    }

    /// <summary>UpdateProduct (@0x141afaeb0, uishopdialog.cpp:1956): rewrites the selected row of group 0.</summary>
    void UpdateProduct(Product p)
    {
        if (shop == null || p == null || group != 0) return;
        if (PopulateProduct(p) is { } row)
            Host.Invoke(shop, "updateItem", selection.Index, row);
    }

    /// <summary>ReadPurchaseResult (@0x141afa2f0, uishopdialog.cpp:1834): re-enables shop; posts guild-storage notice when
    /// <see cref="ShopPurchaseResult.GuildStorage"/> is set.</summary>
    public void ReadPurchaseResult(ShopPurchaseResult r)
    {
        if (r == null) return;
        EnableShop(true);
        if (r.GuildStorage && Model != null)
        {
            uint color = Model.Tables.Items.TextColor(r.Rank);
            string name = $"<FONT color='#{color:x}'>{Model.Tables.ItemName(r.ItemId) ?? ""}</FONT>";
            string text = r.Count > 1
                ? Common("s_msg_take_item_count_to_guild_storage", name, r.Count)
                : Common("s_msg_take_item_to_guild_storage", name);
            NoticeWithType?.Invoke(text, 0x14);
        }
    }

    /// <summary>ReadResetResult (@0x141af9ad0, uishopdialog.cpp:1756): refund notice if an item was refunded, reset sound,
    /// clears group 0 products and invokes requestItemList.</summary>
    public void ReadResetResult(ShopResetResult r)
    {
        if (r == null) return;
        if (r.Success && r.RefundItemId > 0 && r.RefundCount > 0 && Model != null)
        {
            string text = Common("s_shop_reset_refund_item", Model.Tables.ItemName(r.RefundItemId) ?? "", (uint)r.RefundCount);
            if (text.Length > 0) SystemNotice?.Invoke(text);
        }
        Sound?.Invoke("shop_reset");
        ResetProducts(0);
        if (shop != null) Host.Invoke(shop, "requestItemList");
    }

    /// <summary>ReadShopError (@0x141afa640, uishopdialog.cpp:1858): re-enables shop and shows the client's error notice or
    /// message box for the server refusal code.</summary>
    public void ReadShopError(ShopError e)
    {
        if (e == null) return;
        EnableShop(true);
        switch (e.Error)
        {
            case 8: case 9: case 29: break;
            case 2: case 13: SystemNotice?.Invoke(Common(0x49)); break;
            case 11: ShowLackCurrency(e.Value, 0, 0); break;
            case 12: SystemNotice?.Invoke(Common(0x35)); break;
            case 16: SystemNotice?.Invoke(Common(0x5b)); break;
            case 20: SystemNotice?.Invoke(Common("s_err_lack_guild_require_date")); break;
            case 4: case 5: case 6: SystemNotice?.Invoke(Common(0x4b)); break;
            case 7: SystemNotice?.Invoke(Common(0x4a)); break;
            case 15:
                SystemNotice?.Invoke(Common(0x43));
                Sound?.Invoke("system_notice_error_warningBag");
                break;
            case 10: SystemNotice?.Invoke(Common(0x2d2)); break;
            case 18: SystemNotice?.Invoke(Common(0x38, (uint)e.Value)); break;
            case 23: SystemNotice?.Invoke(Common(0xc07)); break;
            case 24: SystemNotice?.Invoke(Common("s_msg_cant_sell_to_only_sell_shop")); break;
            case 25: SystemNotice?.Invoke(Common(0x688, Model?.Tables.PropertyProtectionTime ?? 0, Common(0x5a3))); break;
            case 26: SystemNotice?.Invoke(Common("s_guild_err_buy_no_master")); break;
            case 27: ShowLackCurrency(7, 0, 0); break;
            case 28: SystemNotice?.Invoke(Common("s_err_invalid_item_cannot_buy_by_period")); break;
            case 30: ShowSimpleBox(Common("s_shop_no_star_point_event")); break;
            case 31: SystemNotice?.Invoke(Common("s_shop_err_change_reset_price")); break;
            case 32: SystemNotice?.Invoke(Common("s_meratmarket_error_country_limit")); break;
            case 33: SystemNotice?.Invoke(Common("s_err_cannot_sell_petitem_summon")); break;
            default: SystemNotice?.Invoke(Common(0xcd, e.Error)); break;
        }
        selection.ResetIf(e.Command);
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
    /// preview parts are not ported. The last step is CGuideManager::AddTrigger(ShopOpen = 0x20, shop id) for a shop id other
    /// than 0 (uishopdialog.cpp:943); the only guide that tests it is the FameContents shop guide
    /// (table/guide_famecontents.xml event 80082: shops 206-211). The npc overhead icon
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
        string title = ShopTitleText();
        data.Add(("title", title));
        data.Add(("dpTab", DataProvider(FillTabs())));
        data.Add(("notice", info.ShopType == 3 ? Common("s_shop_text_notice_pssb") : ""));
        if (info.ShopType == 4)
        {
            // gachaUIType and the coin count with its payment icon tag (uishopdialog.cpp:865-869)
            data.Add(("gachaUIType", (uint)Model.Tables.GachaBoxes.UiByShopId(info.ShopId)));
            data.Add(("coinAmount", GachaCoinValue()));
        }
        Host.Invoke(shop, "initData", Host.Obj(data.ToArray()));
        OpenAvatar();
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
        if (info.ShopId != 0) Guide.GuideManager.Current?.AddTrigger(Guide.GuideTrigger.ShopOpen, info.ShopId);
    }

    /// <summary>
    /// vfunc_67 (@0x141af2e90, uishopdialog.cpp:661): UIDialog's, then the shop forgotten: "usable items only" back on,
    /// the groups and the header cleared, the ready flag off, SendNpcTalkMode0, and SendSystemShop(m_nSortKind, 0) when
    /// m_nSortKind &gt; 0.
    /// </summary>
    public override void Close()
    {
        if (shop != null && Preview != null) Host.Invoke(shop, "unloadRenderTarget");
        CloseAvatar();
        base.Close();
        Purchases.Cancel();                                                              // OnDialogClosed: Finish(1)
        headerRead = false;
        checkEnableItems = true;
        resetPending = false;
        ready = false;
        category = 0;
        group = 0;
        repurchaseCount = 0;
        repurchaseEmpty = false;
        newTabShopId = 0;
        newTotal = 0;
        newComplete = false;
        selection.Reset();
        meratItems.Clear();
        for (int i = 0; i < 3; i++) ResetProducts(i);
        search = "";
        info = new ShopInfo { ResetEnable = false, HideReset = true };
        resetPrice = 0;
        shop = null;
        shopEnabled = true;
        // SendNpcTalkMode0 (@0x141b1feb0, uitalkcinematic.cpp:1441) returns at once when no talk is open: a close after the talk
        // ended (walking away) sends nothing
        if (PlayerTalking == null || PlayerTalking()) SendNpcTalkEnd?.Invoke();
        if (sortKind > 0) SendSystemShop?.Invoke(sortKind, 0);
        sortKind = 0;
    }

    /// <summary>
    /// CUIService::RequestCloseDialog(0x4e3b) (@0x141adc690, uidialog.cpp:620 vfunc_68): runs vfunc_67 only (<see cref="Close"/>),
    /// so a purchase box or a market bill that is open stays. <see cref="CloseShop"/> (the Escape key's close) is the one that closes those.
    /// </summary>
    public void RequestClose() { if (!IsClosed) Close(); }

    /// <summary>UIShopDialog::vfunc_13 runs UIDialog's first (uishopdialog.cpp:1336): Escape closes through vfunc_71, CloseShop.</summary>
    public override void EscapeClose() => CloseShop();

    /// <summary>UIShopDialog::CloseShop (@0x141aff000, uishopdialog.cpp:2641): closes any active shop trade / buyItem
    /// message box, then closes the shop dialog.</summary>
    public void CloseShop()
    {
        if (CurrentBox?.Invoke() is { } box && (box.Type == 0x12 || box.Extra.StartsWith("buyItem,", StringComparison.Ordinal)))
            CloseBox?.Invoke();
        Close();
    }

    /// <summary>UIShopDialog::vfunc_13 (@0x141af7b00, uishopdialog.cpp:1336): Enter while m_inputSearch has focus starts a search.</summary>
    public bool OnEnterKey()
    {
        if (IsClosed || shop == null) return false;
        var input = Host.Child("m_inputSearch", shop);
        if (input == null) return false;
        if (!Rt.ToBoolean(Host.Get(input, "focused"))) return false;
        string text = Rt.ToString(Host.Get(input, "text")) ?? "";
        Search(text);
        return true;
    }

    /// <summary>
    /// vfunc_66 (@0x141af2cb0, uishopdialog.cpp:626) after UIDialog's (the shop's position as it opens): with the inventory
    /// (0x4e2d) open, the two placed side by side, the shop on the left (FUN_141275c30; a gacha shop's inventory 180 lower);
    /// with it closed, the "openWindowPos" message with this id, which opens the inventory beside the shop
    /// (UIInventoryDialog's OnMessage), except for a shop with a gacha coin (ItemGachaBoxTable::GetCoinItemIdByShopId other than 0,
    /// :635-637), which leaves the inventory closed.
    /// </summary>
    public void PlaceBesideInventory(UiController inventory, bool inventoryOpen)
    {
        if (Host?.Entity == null) return;
        var place = OpenPosition();
        if (!inventoryOpen)
        {
            if (Model.Tables.GachaBoxes.CoinItemIdByShopId(info.ShopId) != 0) return;
            InventoryWindowPos?.Invoke(UiId);
            return;
        }
        if (info.ShopType == 4) PlaceSideBySide(this, place, inventory, (0f, 0f), (0f, 180f));
        else PlaceSideBySide(this, place, inventory);
    }

    /// <summary>MyPC::FUN_141527e60 (@0x141527e60, mypc.cpp:2086): the player is talking to an npc (UITalkManager::GetTalking), or the
    /// cube-home mark FUN_141527f60 is set (housing only, not ported). Null for a shop that is not tied to a live talk (the offline
    /// fixtures), which then never closes by walking away.</summary>
    public Func<bool> PlayerTalking;

    /// <summary>
    /// vfunc_72 (@0x141af3460, uishopdialog.cpp:726), per frame: a shop that is not a system shop (m_nSortKind 0) closes (CloseShop)
    /// once the player is no longer talking (the npc walked out of range, was removed, or the talk ended); otherwise, once the
    /// reset time has passed, invokes endTime, clears group 0 products and sends Shop mode 10; on a limited-reset shop when the
    /// local day rolls over (IsEarlierDay), clears group 0 and sends Shop mode 10. Zero heap allocations per frame.
    /// </summary>
    public void Process()
    {
        if (shop == null || !Model.HasLocalPlayer) return;
        if (sortKind == 0 && PlayerTalking != null && !PlayerTalking()) { CloseShop(); return; }
        if (!resetPending) return;
        long now = ServerTime();
        if (now == 0 || info.ResetType == 99) return;
        if (info.ResetTime < now)
        {
            Host.Invoke(shop, "endTime");
            ResetProducts(0);
            Send?.Invoke(new ShopRequestListRequest());
            Request?.Invoke("list", Array.Empty<object>());
            resetPending = false;
        }
        else if (info.ResetEnable && info.ResetLimited && IsEarlierDay(resetCheckTime, now))
        {
            resetCheckTime = now;
            ResetProducts(0);
            Send?.Invoke(new ShopRequestListRequest());
            Request?.Invoke("list", Array.Empty<object>());
        }
    }

    static bool IsEarlierDay(long earlierUnix, long laterUnix) =>
        DateTimeOffset.FromUnixTimeSeconds(earlierUnix).LocalDateTime.Date
        < DateTimeOffset.FromUnixTimeSeconds(laterUnix).LocalDateTime.Date;

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
        shopEnabled = on;
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
    /// for group 0, the new-items packet's tab shop for group 2) and, when shown, sent as the category list. (The client builds into the shown
    /// group's tab list, m_tabs[m_nGroup], with the given group's products.)
    /// </summary>
    void SetTab(int g, bool show)
    {
        if (g < 0 || g >= 3) return;
        var list = tabs[group];
        int tabShop = g == 0 ? info.TabShopId : g == 2 ? newTabShopId : 0;
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
            foreach (var m in meratItems)       // the star items come before the shop's own
            {
                if (searching && MeratName(m.Product).IndexOf(text, StringComparison.Ordinal) < 0) continue;
                if (PopulateMerat(m) is { } row) rows.Add(row);
            }
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
        {
            foreach (var p in products[1].Values)
                if (PopulateProduct(p) is { } row) rows.Add(row);
        }
        else if (info.HasNewTab && group == 2)
        {
            FillNewProducts(rows, text);
        }
        if (shop != null) Host.Invoke(shop, "buildShopList", DataProvider(rows), info.Shuffle);
        search = text;
    }

    /// <summary>FillNewProducts (@0x141afc9d0, uishopdialog.cpp:2264): requests group 2 via Outbound 0x28 mode 13 until
    /// complete, then populates new-tab rows (PopulateNewProduct :3039).</summary>
    void FillNewProducts(List<object> rows, string text)
    {
        if (!info.HasNewTab) return;
        if (!newComplete)
        {
            EnableShop(false);
            Send?.Invoke(new ShopRequestNewItemsRequest());
            return;
        }
        bool searching = text.Length > 0;
        var list = tabs[2];
        IEnumerable<Product> source = category >= 0 && category < list.Count ? list[category].Products
            : newTabShopId == 0 ? products[2].Values : Array.Empty<Product>();
        foreach (var p in source)
        {
            if (p == null) continue;
            if (searching && (p.Item == null || ItemName(p.Item).IndexOf(text, StringComparison.Ordinal) < 0)) continue;
            if (PopulateNewProduct(p) is { } row) rows.Add(row);
        }
    }

    /// <summary>PopulateNewProduct (@0x141b01e90, uishopdialog.cpp:3039): row for CNpcShopProductNewCli (productType 2).</summary>
    AsObject PopulateNewProduct(Product p)
    {
        if (p?.Item?.Data == null) return null;
        var item = p.Item;
        var data = item.Data;
        var r = p.Record;
        var slot = new List<(string, object)>();
        if (!ItemSlot.Build(slot, item, Model.HasLocalPlayer, SlotFlags, Model, Model.ItemSlotSources)) return Host.Obj(slot.ToArray());
        slot.Add(("serialID", r.Serial));
        int at = slot.FindIndex(m => m.Item1 == "itemCount");
        if (at >= 0) slot[at] = ("itemCount", ""); else slot.Add(("itemCount", ""));
        uint color = Model.Tables.Items.TextColor(item.Rarity);
        slot.Add(("label", $"<FONT color='#{color:x}'>{Model.Tables.ItemName(item.ItemId) ?? ""}</FONT>"));
        slot.Add(("productType", 2));
        slot.Add(("saleTag", r.SaleTag));
        slot.Add(("showRecipe", data.MaidId > 0));
        slot.Add(("showPreview", data.FunctionName == "UGCMapPackage"));
        slot.Add(("bgFrame", BackgroundFrame(r.Label)));
        return Host.Obj(slot.ToArray());
    }

    /// <summary>RefreshCategories (@0x141aff1c0, uishopdialog.cpp:2700): for gacha shops, collects MainItem entries in the
    /// active category and invokes visibleMainItem(hasAny).</summary>
    void RefreshCategories()
    {
        if (info.ShopType != 4) return;
        var main = new List<Item>();
        if (group == 0 && category >= 0 && category < tabs[0].Count)
        {
            var items = tabs[0][category].Products;
            if (info.Shuffle) Shuffle(items);
            foreach (var p in items)
                if (p?.Item != null && p.Record.MainItem) main.Add(p.Item);
        }
        PreviewMainItemsNow(main);
        PreviewMainItems?.Invoke(main);
        if (shop != null) Host.Invoke(shop, "visibleMainItem", main.Count > 0);
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

    /// <summary>CItem::FUN_140527b10: the item's display name (ItemTooltip.Name).</summary>
    string ItemName(Item item) => new ItemTooltip(Model, 0).Name(item);

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

    /// <summary>CDBPremiumProduct::GetName (meratmarketbasketinfo.cpp:841): a limited product's own name, else the item's.</summary>
    string MeratName(PremiumProduct p) => p.Type == 3 ? p.Name : Model.Tables.ItemName(p.ItemId) ?? "";

    /// <summary>CMeratMarketPremiumProduct::HasLimit (meratmarketbasketinfo.cpp:966).</summary>
    static bool MeratHasLimit(PremiumProduct p) => p.Type switch
    {
        1 or 7 or 8 => p.BuyLimit != 0,
        3 or 6 => p.LimitFlag != 0 || p.BuyLimit != 0,
        _ => false,
    };

    /// <summary>CMeratMarketPremiumProduct::IsBuyLimitReached (:983).</summary>
    static bool MeratBuyLimitReached(PremiumProduct p) => MeratHasLimit(p) && p.BuyLimit != 0 && p.BuyCount >= p.BuyLimitMax;

    /// <summary>The running premium market sale events (CShopEventManager), shared with the live session.</summary>
    public ShopSaleEvents SaleEvents { get; set; } = new();

    /// <summary>CDBPremiumProduct::GetSalePrice (:685) with the running sale events (<see cref="ShopSaleEvents.SalePrice"/>).</summary>
    long MeratSalePrice(PremiumProduct p) => SaleEvents.SalePrice(p);

    /// <summary>CDBPremiumProduct::FUN_1406a0230 (:861): the product's currency as the ui currency (0 meso, 1 and 3 merat, 2
    /// red merat), FUN_14069c3e0 (:217).</summary>
    static int MeratUiCurrency(PremiumProduct p) => (p.Currency == 3 ? 1 : p.Currency) switch { 1 => 3, 2 => 9, _ => 1 };

    /// <summary>FUN_1418fec90 (uicommon.cpp:912) for a ui currency: an item price s_common_payitem, else s_common_payment with the
    /// currency's icon (9 is the red merat's only with feature 0x43, else the merat's).</summary>
    public string CurrencyPriceText(int currency, long amount, int itemId = 0)
    {
        if (currency == 2) return Common("s_common_payitem", Number(amount), (uint)itemId);
        var (icon, vspace) = CurrencyIcon(currency, itemId, Model.IsFeatureEnabled(Feature.RedMerat));
        return icon.Length > 0 ? Common("s_common_payment", Number(amount), icon, vspace) : "";
    }

    /// <summary>The bill dialog of a star item is to be opened (OpenDialog 0x4f17 after SetParam).</summary>
    public event Action<MarketBillParam> OpenMarketBill;
    /// <summary>The merat market's queue of pending purchases (CMeratMarketMan).</summary>
    public readonly MeratPurchaseQueue Purchases = new();

    /// <summary>
    /// BuyMeratItem (@0x141b05cd0, uishopdialog.cpp:2473): needs the star point event, a star item of group 0; a product at its
    /// buy limit only notice 0xa17 (StartMeratItem for one not shown sold out is STILL-OPEN); not enough currency a notice; else one bill row (name, sale price, pay type, star point text)
    /// with source kind 4 goes to the bill dialog; its buy callback queues the packet (CMeratMarketBuy::FUN_140d604c0: key,
    /// option, quantity 1, price, star point, pay type; type 6 home fields and type 7/8 random rate not read here: the products'
    /// source of them is STILL-OPEN) and starts the queue.
    /// </summary>
    public bool BuyMeratItem(int serial)
    {
        if (!HasStarEvent()) { ShowSimpleBox(Common("s_shop_no_star_point_event")); return false; }
        var m = FindMeratItem(serial);
        var prem = m?.Product;
        if (prem == null || group != 0) return false;
        long price = MeratSalePrice(prem);
        if (MeratBuyLimitReached(prem)) { SystemNotice?.Invoke(Common(0xa17)); return false; }
        int currency = MeratUiCurrency(prem);
        if (!HasEnoughCurrency(currency, price, 0))
        {
            SystemNotice?.Invoke(Common(currency == 9 ? "s_err_lack_redmerat" : "s_err_lack_merat"));
            return false;
        }
        var preview = m.Preview ??= Item.Create(Model.Tables.Items, prem.ItemId, prem.Grade);
        var row = new MarketBillRow
        {
            ItemId = prem.ItemId, Name = MeratName(prem), Price = price, PrevPrice = price, PayType = currency,
            StarPointText = StarPointText(prem), SerialId = prem.Id, Premium = prem,
        };
        var icon = new List<(string, object)>();
        if (preview != null) ItemSlot.Build(icon, preview, Model.HasLocalPlayer, SlotFlags, Model, Model.ItemSlotSources);
        row.Icon = icon;
        var param = new MarketBillParam { Type = 3, DialogId = 0x4e3b, Currency = currency, ShopId = info.ShopId, SourceKind = 4 };
        param.Rows.Add(row);
        param.Buy = (pa, _) => QueueMeratBuys(pa, false, "", "");
        param.GiftBuy = (pa, _, name, msg) => QueueMeratBuys(pa, true, name, msg);
        OpenMarketBill?.Invoke(param);
        return true;
    }

    /// <summary>OnBuySucceeded / OnBuyFailed (0x6C): the queue takes the answer; a bought product's buy count follows the answer.</summary>
    public void OnMeretMarket(Ms2.Net.Packets.MeretMarketPacket p)
    {
        if (p?.Buy == null) return;
        if (p.Succeeded && FindMeratItem(p.Buy.PremiumId)?.Product is { } prem) prem.BuyCount = p.BuyCount;   // OnBuySucceeded (meratmarketman.cpp:2647); the sold count is STILL-OPEN
        Purchases.OnAnswer(p);
    }

    /// <summary>
    /// The confirm callback of the bill (FUN_140ebded0 / FUN_140ebef60, uimeratmarketcommon.cpp:543 / :685): the payment map must be
    /// payable (FUN_140ebc590: a lack notice per currency, nothing sent), a buy (not a gift) needs the bag room per tab (FUN_140ebcc20,
    /// message 0xa01) and each product CanBuyPremiumProduct (meratmarketman.cpp:1745: level, achievement, sold out; a product with a
    /// job limit or a PC bang level is refused as STILL-OPEN data, the premium product's selling and event period likewise when set).
    /// Nothing is queued unless every check passes.
    /// </summary>
    void QueueMeratBuys(MarketBillParam pa, bool gift, string name, string message)
    {
        if (info.ShopId == 0 || pa == null || pa.Rows.Count == 0) return;
        var rows = new List<MarketBillRow>();
        foreach (var r in pa.Rows)
        {
            if (gift && !r.Checked) continue;
            if (r.Premium != null) rows.Add(r);
        }
        if (rows.Count == 0) return;
        var pay = new Dictionary<int, long>();
        foreach (var r in rows) pay[r.PayType] = pay.GetValueOrDefault(r.PayType) + r.Price;
        foreach (var kv in pay)
        {
            if (kv.Value == 0 || HasEnoughCurrency(kv.Key, kv.Value, 0)) continue;
            SystemNotice?.Invoke(Common(kv.Key == 9 ? "s_err_lack_redmerat" : "s_err_lack_merat"));
            return;
        }
        if (!gift && !HasBagRoom(rows)) return;
        foreach (var r in rows) if (!CanBuyMerat(r.Premium)) return;
        foreach (var r in rows)
        {
            var prem = r.Premium;
            var buy = new Ms2.Net.Packets.MeretMarketBuy
            {
                Kind = pa.SourceKind, PremiumId = prem.Id, Quantity = 1, Option = r.Option, ShopId = pa.ShopId,
                Price = r.Price, StarPoint = r.StarPointText.Length > 0, PayType = r.PayType,
                Gift = gift ? 1 : 0, Receiver = gift ? name : "", Message = gift ? message : "",
            };
            Purchases.Queue(new MeratPurchaseQueue.Pending { Buy = buy, Name = r.Name, PayType = r.PayType }, pa.SourceKind);
        }
        Purchases.Start(pa.SourceKind);
    }

    /// <summary>FUN_140ebcc20: the items of the rows counted per inventory tab against the empty slots; the first tab short shows 0xa01.</summary>
    bool HasBagRoom(List<MarketBillRow> rows)
    {
        var need = new Dictionary<int, int>();
        foreach (var r in rows)
        {
            if (r.ItemId == 0) continue;
            int tab = ItemTab(r.ItemId);
            if (tab >= 0 && tab < 15) need[tab] = need.GetValueOrDefault(tab) + 1;
        }
        for (int tab = 0; tab < 15; tab++)
        {
            if (!need.TryGetValue(tab, out int n) || n <= 0 || EmptySlotCount(tab) >= n) continue;
            ShowSimpleBox(Common(0xa01, InventoryTabName(tab), n));
            return false;
        }
        return true;
    }

    /// <summary>CMeratMarketMan::CanBuyPremiumProduct (:1745) with its notices; false for what the client data here cannot decide.</summary>
    bool CanBuyMerat(PremiumProduct p)
    {
        if (p.JobMask != 0 || p.PcBangLevel != 0) return false;                         // IsJobAllowed / PC bang level: STILL-OPEN
        if (p.StartDate != 0 || p.EndDate != 0 || p.EventStart != 0 || p.EventEnd != 0) return false;   // IsSelling / IsEventPeriod: STILL-OPEN
        int level = Pc.Level;
        if (level != 0 && ((ushort)p.MinLevel != 0 || (ushort)p.MaxLevel != 0))             // IsLevelAllowed (:830)
        {
            int min = (ushort)p.MinLevel == 0 ? 1 : (ushort)p.MinLevel, max = (ushort)p.MaxLevel;
            if (level < min || (max != 0 && max < level))
            {
                string key = "s_meratmarket_error_buy_limit_";
                SystemNotice?.Invoke((ushort)p.MinLevel > 0 && max > 0
                    ? (min == max ? Common(key + "level_only", min) : Common(key + "level_range", min, max))
                    : (ushort)p.MinLevel > 0 ? Common(key + "level_min", min) : Common(key + "level_max", max));
                return false;
            }
        }
        if (p.AchieveId != 0 && !HasAchieve(p.AchieveId, p.AchieveGrade))                // FUN_140ebfb90
        {
            SystemNotice?.Invoke(Common(0x5c, TaggedAchieveTitle(p.AchieveId, p.AchieveGrade, Common(0x308))));
            return false;
        }
        if (MeratBuyLimitReached(p) || (p.Type == 3 && p.LimitCount != 0 && p.SoldCount >= p.LimitCount)
            || (p.Type == 6 && p.LimitCount != 0 && p.HomeSoldCount >= p.LimitCount))
        {
            SystemNotice?.Invoke(Common(0xa17));
            return false;
        }
        return true;
    }

    /// <summary>GetStarPointText (uimeratmarketcommon.cpp:1047): with the star point event and a point price, the product's
    /// s_meratmarket_text_add_star_point.</summary>
    string StarPointText(PremiumProduct p)
    {
        if (!Pc.StarPointEvent || !p.PointPrice || p.Point <= 0) return "";
        return Ms2.Quest.ClientFormat.Format(Model.Tables.Common("s_meratmarket_text_add_star_point") ?? "", (long)p.Point);
    }

    /// <summary>
    /// PopulateMerat (@0x141b01480, uishopdialog.cpp:2971): the row of a star item; null without the item data and (outside
    /// buy back, with "usable items only" ticked) for an item the player cannot use or whose job is not recommended. The slot is
    /// built from the preview item with the grade, then itemCount, the grade-coloured product name, the sale price and its text,
    /// the price (and discount) when they differ, saleTag, soldOut, displayOrder, meratItem, serialID, itemID, productType,
    /// showRecipe, showPreview, level, the star point text and the requirement of an achievement not yet reached.
    /// </summary>
    AsObject PopulateMerat(MeratItem m)
    {
        var prem = m?.Product;
        if (prem == null) return null;
        var preview = m.Preview ??= Item.Create(Model.Tables.Items, prem.ItemId, prem.Grade);   // FUN_141b05180
        var data = preview?.Data;
        if (data == null) return null;
        if (group != 1 && checkEnableItems)
        {
            int use = ItemUseError?.Invoke(preview) ?? 0;
            if (use != 0 && use != 1) return null;
            if (Model.HasLocalPlayer && !data.IsJobRecommended(Pc.JobGroup)) return null;
        }
        var slot = new List<(string, object)>();
        if (!ItemSlot.Build(slot, preview, Model.HasLocalPlayer, SlotFlags, Model, Model.ItemSlotSources)) return Host.Obj(slot.ToArray());
        int count = data.IsStackable ? prem.ItemCount : 0;                                  // GetB0(0)
        int at = slot.FindIndex(x => x.Item1 == "itemCount");
        if (at >= 0) slot[at] = ("itemCount", count); else slot.Add(("itemCount", count));
        uint color = Model.Tables.Items.TextColor(prem.Grade);
        slot.Add(("label", $"<FONT color='#{color:x}'>{MeratName(prem)}</FONT>"));
        long sale = MeratSalePrice(prem), price = prem.Price;
        slot.Add(("salePrice", sale));
        slot.Add(("salePriceText", CurrencyPriceText(MeratUiCurrency(prem), sale)));
        if (sale != price && price > 0)
        {
            slot.Add(("price", price));
            slot.Add(("priceText", Number(price)));
            slot.Add(("bgSaleText", Common("s_common_percent", (uint)(int)(100 - sale * 100 / price))));
        }
        slot.Add(("saleTag", prem.SaleTag));
        slot.Add(("soldOut", MeratBuyLimitReached(prem)));
        slot.Add(("displayOrder", m.DisplayOrder));
        slot.Add(("meratItem", true));
        slot.Add(("serialID", prem.Id));
        slot.Add(("itemID", (uint)prem.ItemId));
        slot.Add(("productType", 1));                                                     // GetProductType: m_40, 1 from the constructor
        slot.Add(("showRecipe", data.MaidId > 0));
        slot.Add(("showPreview", data.FunctionName == "UGCMapPackage"));
        slot.Add(("level", (uint)data.LevelLimit));
        string star = StarPointText(prem);
        if (star.Length > 0) slot.Add(("starPoint", star));
        if (prem.AchieveId != 0 && !HasAchieve(prem.AchieveId, prem.AchieveGrade))
            slot.Add(("requireAchieve", TaggedAchieveTitle(prem.AchieveId, prem.AchieveGrade, Common(0x308))));
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
        // FUN_1418fee40 (bAlt false): an item price with the tag; any other kind is FUN_1418fec00 = FUN_1418fec90 below.
        if (!string.IsNullOrEmpty(iconTag) && currency == 2) return Common("s_common_payitem", Number(amount), iconTag);
        return CurrencyPriceText(currency, amount, itemId);
    }

    /// <summary>FUN_1403baee0 (gameentitycommon.cpp:268): a shop's currency kind as the ui currency.</summary>
    public static int CurrencyOf(int kind) => kind switch
    {
        0 => 1, 1 => 2, 2 => 4, 3 => 5, 4 => 3, 5 => 6, 6 => 7, 7 => 11, 8 => 12, 9 => 10, 10 => 9, 11 => 13, 12 => 14,
        13 => 15, 14 => 16, _ => 17,
    };

    /// <summary>FUN_1418fe870 (uicommon.cpp:816): a currency's icon and its vertical offset. The red / rainbow merat icons
    /// are the client's with the red merat feature (0x43, FUN_14036d650 / FUN_14036d6d0), else the merat's.</summary>
    public static (string Icon, int VSpace) CurrencyIcon(int currency, int itemId, bool redMerat = false) => currency switch
    {
        0 => ("export_common_icon_cash_r", -4),
        1 => ("export_common_icon_meso_h21", -6),
        2 => ("export_common_icon_item_" + ((uint)itemId).ToString(CultureInfo.InvariantCulture), -6),
        3 => ("export_common_icon_merat", -4),
        4 => ("export_common_icon_honor", -6),
        5 => ("export_common_icon_treva", -6),
        6 => ("export_common_icon_lu", -6),
        7 => ("export_common_icon_habi", -6),
        9 => (redMerat ? "export_common_icon_redmerat" : "export_common_icon_merat", -4),
        10 => (redMerat ? "export_common_icon_rbmerat" : "export_common_icon_merat", -4),
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

    /// <summary>The dialog's title (uishopdialog.cpp:836-850): the shop's title row, else by type the star shop's, the gacha shop's
    /// s_title_gacha_shop with the coin's name (:843-846), or for an npc s_title_shop with its name (FormatStringId(0x45e),
    /// commonstringtable.cpp:2732; FUN_1401aa6a0).</summary>
    internal string ShopTitleText()
    {
        string title = Model.Tables.ShopTitle(info.ShopId);
        if (title.Length == 0)
        {
            if (info.ShopType == 2) title = "$s_shop_title_star";
            else if (info.ShopType == 4) title = Common("s_title_gacha_shop", Model.Tables.ItemName(Model.Tables.GachaBoxes.CoinItemIdByShopId(info.ShopId)) ?? "");
            else if (info.NpcId > 0) title = Common("s_title_shop", Npcs?.Find(info.NpcId)?.Name ?? "");
        }
        return title;
    }

    /// <summary>FUN_1418fec90(2, owned, paymentIconTag) (uishopdialog.cpp:866-869, :2126-2128): the coin item's count in the bag as an
    /// item price whose icon is the shop's paymentIconTag.</summary>
    internal string GachaCoinAmount()
    {
        int coin = Model.Tables.GachaBoxes.CoinItemIdByShopId(info.ShopId);
        long owned = (uint)ItemCount(coin, 0);
        return CurrencyPriceText(2, owned, Model.Tables.GachaBoxes.PaymentIconTagByShopId(info.ShopId));
    }

    /// <summary>
    /// The coin count in the form the loaded movie takes. The KMS2_DEV movie's gachaCoinAmount(String) and initData's coinAmount put the
    /// text in m_coinInfo.textField.htmlText (the payitem markup <see cref="GachaCoinAmount"/>); the GMS2 movie's gachaCoinAmount(int) and
    /// initData set m_coinInfo.textField.text from the count (initData also plays m_coinInfo.m_icon "_"+gachaUIType, which is why the
    /// icon tag is no part of its text). The movie's own declaration decides; the GMS2 exe is not decompiled, so the plain count (not a
    /// grouped one) is the reading taken for it.
    /// </summary>
    internal object GachaCoinValue()
    {
        switch (shop == null ? null : Host.ParamType(shop, "gachaCoinAmount", 0))
        {
            case "int": return ItemCountOfCoin();
            case "uint": return (uint)ItemCountOfCoin();
            case "Number": return (double)(uint)ItemCountOfCoin();
            default: return GachaCoinAmount();
        }
    }

    int ItemCountOfCoin() => (int)(uint)ItemCount(Model.Tables.GachaBoxes.CoinItemIdByShopId(info.ShopId), 0);

    /// <summary>UpdateGachaCoin (@0x141afbd10, uishopdialog.cpp:2120): a gacha shop's coin count, sent to the movie's gachaCoinAmount;
    /// run at the end of every shop packet (OnShopPacket :1569) and after an item break result (<see cref="FollowItemBreak"/>).</summary>
    public void UpdateGachaCoin()
    {
        if (shop == null || info.ShopType != 4) return;
        Host.Invoke(shop, "gachaCoinAmount", GachaCoinValue());
    }

    /// <summary>
    /// CItemBreakManager::ForwardPacketToDialog's second call (@0x140532160, itembreakmanager.cpp:360): after the dismantle dialog (type 1)
    /// has taken a result packet, the open shop dialog (0x4e3b) runs UpdateGachaCoin, whatever dialog it is (the call itself returns
    /// for a shop that is not a gacha shop). The host calls this when the shop opens and runs the returned action when it unloads, as
    /// the client's lookup finds the shop only while it exists. The refresh follows the manager's mode 3 / type 1 packet in the
    /// client's order: the break dialog first, the coin count after it.
    /// </summary>
    public Action FollowItemBreak(ItemBreakManager manager)
    {
        void OnResult(int _) => UpdateGachaCoin();
        manager.ShopCoinRefresh += OnResult;
        return () => manager.ShopCoinRefresh -= OnResult;
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
        if (AvatarCallback(name, args)) return true;
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
                RefreshCategories();
                return true;
            case "oncheckenableitems":
                checkEnableItems = Rt.ToBoolean(Arg(args, 0));
                if (group == 0) RefreshItems("");
                return true;
            case "onsearch":
                Search(Arg(args, 0) as string ?? Convert.ToString(Arg(args, 0), CultureInfo.InvariantCulture) ?? "");
                return true;
            case "onbuyitem":
            {
                var row = Arg(args, 0);
                int serial = ToInt(Host.Get(row, "serialID"));
                int productType = ToInt(Host.Get(row, "productType"));
                int index = ToInt(Arg(args, 1));
                if (info.ShopType != 4 && IsPreviewHudOn()) PreviewProduct(serial, productType, false);
                else if (productType != 1) BuyProduct(serial, index);
                else BuyMeratItem(serial);
                return true;
            }
            case "onrightclick":
            {
                if (IsPreviewHudOn() && Arg(args, 0) is { } row && row is not Undefined)
                    PreviewProduct(ToInt(Host.Get(row, "serialID")), ToInt(Host.Get(row, "productType")), false);
                return true;
            }
            case "onsell":
                SellItem(Arg(args, 0) as string ?? "");
                return true;
            case "onresetlist":
                ResetList();
                return true;
            case "onuselink":
            {
                if (Arg(args, 0) is { } row && row is not Undefined)
                {
                    int serial = ToInt(Host.Get(row, "serialID"));
                    int productType = ToInt(Host.Get(row, "productType"));
                    if (productType == 1) Request?.Invoke("linkMerat", new object[] { serial });
                    else LinkProduct(serial);
                }
                return true;
            }
            case "onusepreview":
            {
                if (Arg(args, 0) is { } row && row is not Undefined)
                {
                    int serial = ToInt(Host.Get(row, "serialID"));
                    int productType = ToInt(Host.Get(row, "productType"));
                    if (info.ShopType == 4) PreviewGachaItem(serial);
                    else PreviewProduct(serial, productType, true);
                }
                return true;
            }
            case "onclickrecipe":
            {
                if (Arg(args, 0) is { } row && row is not Undefined)
                {
                    int itemId = (int)(uint)ToInt(Host.Get(row, "itemID"));
                    float x = (float)Rt.ToNumber(Arg(args, 1)), y = (float)Rt.ToNumber(Arg(args, 2));
                    if (Model?.Tables.Items.Get(itemId) is { MaidId: > 0 } data)
                    {
                        // STILL-OPEN: the maid recipe dialog (0x4ecb) is not in this HUD; OpenDialog only logs it.
                        OpenMaidRecipe?.Invoke(data.MaidId, x, y);
                        OpenDialog?.Invoke(0x4ecb);
                    }
                }
                return true;
            }
            case "onclickpreview":
            {
                if (Arg(args, 0) is { } row && row is not Undefined)
                {
                    int itemId = (int)(uint)ToInt(Host.Get(row, "itemID"));
                    if (Model?.Tables.Items.Get(itemId) is { FunctionName: "UGCMapPackage" })
                    {
                        float x = 0f, y = 0f;
                        if (shop != null)
                        {
                            x = (float)Rt.ToNumber(Host.Get(shop, "x")) + (float)Math.Ceiling(Rt.ToNumber(Host.Get(shop, "width")));
                            y = (float)Rt.ToNumber(Host.Get(shop, "y"));
                        }
                        // STILL-OPEN: UIInteriorPreviewDialog (0x4ee3) is not in this HUD; OpenDialog only logs it.
                        OpenInteriorPreview?.Invoke(itemId, x, y);
                        OpenDialog?.Invoke(0x4ee3);
                    }
                }
                return true;
            }
            case "onclickunequipall":
                UnequipAllNow();
                PreviewUnequipAll?.Invoke();
                return true;
            case "onclickshowmyequip":
                ShowMyEquipNow();
                PreviewShowMyEquip?.Invoke();
                return true;
            case "onclickshowmainitems":
                RefreshCategories();
                return true;
            case "onunequip":
            {
                int slot = Hud.MessageBoxData.WToI(Rt.ToString(Arg(args, 0)) ?? "");
                ulong uid = 0;
                if (Arg(args, 1) is { } row && row is not Undefined)
                    ulong.TryParse(Rt.ToString(Host.Get(row, "itemGUID")) ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out uid);
                UnequipNow(slot, uid);
                PreviewUnequipSlot?.Invoke(slot, uid);
                return true;
            }
        }
        return false;
    }

    /// <summary>LinkProduct (@0x141afe960, uishopdialog.cpp:2560): kind 0 links shop product (shopId, serial), kind 2 links plain item.</summary>
    void LinkProduct(int serial)
    {
        var p = FindCurrentProduct(serial);
        if (p?.Item == null) return;
        if (p.ProductKind == 0) LinkShopItem?.Invoke(p.Item.ItemId, p.Item.Rarity, info.ShopId, serial);
        else if (p.ProductKind == 2) LinkPlainItem?.Invoke(p.Item.ItemId, p.Item.Rarity);
    }

    /// <summary>PreviewProduct (@0x141afee80, uishopdialog.cpp:2630) / PreviewNormalItem (:2592).</summary>
    void PreviewProduct(int serial, int productType, bool force)
    {
        if (productType == 1) { Request?.Invoke("previewMerat", new object[] { serial }); return; }
        var p = FindCurrentProduct(serial);
        if (p?.Item == null) return;
        // PreviewItem is set by the HUD (HudScene.Shop.cs: ItemPreviewMgr::OpenPreview); IsPreviewHudOn stays unset there, so
        // only force opens the window and the HUD-preview redirects never fire.
        // STILL-OPEN: PreviewNormalItem (uishopdialog.cpp:2592) first calls InitColorsFromPalette (CItemExtraData) on the item's
        // proxy before OpenPreview; the item's palette colours are not initialised here.
        if (IsPreviewHudOn() || force) PreviewItem?.Invoke(p.Item);
    }

    /// <summary>CanPreviewItem (@0x141b03f70, uishopdialog.cpp:3434): a local player, the item's gender allowed, then
    /// FUN_141b48950 (uiugcitemeditdialog.cpp:2337): not expired (CItem::IsExpired) and doNotPreview == 0 (IsPreviewable,
    /// :2325). The client does not test the item type.</summary>
    public bool CanPreviewItem(Item item) =>
        Model != null && Model.HasLocalPlayer && item?.Data != null
        && item.Data.IsGenderAllowed(Pc.Gender)
        && !(item.ExpiryTime > 0 && item.ExpiryTime <= ServerTime())
        && item.Data.DoNotPreview == 0;

    /// <summary>PreviewGachaItem (@0x141b04050, uishopdialog.cpp:3448): validates previewability (notice 0xf9 on fail).</summary>
    void PreviewGachaItem(int serial)
    {
        var p = FindCurrentProduct(serial);
        if (p?.Item == null) return;
        if (!CanPreviewItem(p.Item)) { SystemNotice?.Invoke(Common(0xf9)); return; }
        PreviewGachaItemNow(p.Item);
        PreviewGachaItemRequested?.Invoke(p.Item);
    }

    // ---- currency, buy, sell and reset flows -----------------------------------------------------------------------

    void ShowSimpleBox(string text) => ShowBox?.Invoke(Hud.MessageBoxQueue.Create(0, text ?? "", null, ""));

    /// <summary>UIShopDialog::HasStarEvent (@0x141aff180, uishopdialog.cpp:2690): star shop requires active event.</summary>
    public bool HasStarEvent() => !(info.ShopType == 2 && !Pc.StarPointEvent);

    /// <summary>HasEnoughCurrency (@0x1418ff1f0, uicommon.cpp:971) over <see cref="UiModel.Player"/> and inventory/guild hooks.</summary>
    public bool HasEnoughCurrency(int currency, long amount, int itemId)
    {
        if (Model == null) return false;
        switch (currency)
        {
            case 1: return Pc.Mesos >= amount;
            case 2: return ItemCount(itemId, 0) >= amount;
            case 3: return MeratTotal() >= amount;
            case 4: case 5: case 6: case 7:
            case 0xc: case 0xd: case 0xe: case 0xf: case 0x10:
                return Pc.Tokens.TryGetValue(currency, out long tok) && tok >= amount;
            case 9: return Pc.RedMerat >= amount;
            case 10: return Pc.RedMerat >= amount || MeratTotal() >= amount;
            case 0xb: return Math.Max(0, GuildFund()) >= amount;
            default: return false;
        }
    }

    /// <summary>CanPayFee (@0x14134a1d0, unit_141347b10.cpp:597-614): CanPayCurrency over the shop's currency kind
    /// (<see cref="CurrencyOf"/>, FUN_1403baee0), then <see cref="HasEnoughCurrency"/> with the price as an unsigned amount.</summary>
    public bool CanPayFee(int kind, int itemId, long amount) => HasEnoughCurrency(CurrencyOf(kind), (uint)amount, itemId);

    /// <summary>GetCurrencyAmount (@0x1418ff5a0, uicommon.cpp:1020).</summary>
    public long GetCurrencyAmount(int currency, long amount, int itemId) =>
        Model == null ? 0 : CurrencyWallet.Amount(Pc, currency, amount, () => ItemCount(itemId, 0), GuildFund);

    long MeratTotal() => CurrencyWallet.Merat(Pc);

    /// <summary>ShowLackCurrencyMessage (@0x1418ff710, uicommon.cpp:1068) via FUN_14134a120 (unit_141347b10.cpp:594).</summary>
    public void ShowLackCurrency(int shopKind, int itemId, long amount)
    {
        int currency = CurrencyOf(shopKind);
        switch (currency)
        {
            case 1: SystemNotice?.Invoke(Common(0x34)); break;
            case 2: SystemNotice?.Invoke(Common(0x38, (uint)itemId)); break;
            case 4: SystemNotice?.Invoke(Common(0x39)); break;
            case 5: SystemNotice?.Invoke(Common(0x3a)); break;
            case 6: SystemNotice?.Invoke(Common(0x3b)); break;
            case 7: SystemNotice?.Invoke(Common(0x3c)); break;
            case 9: SystemNotice?.Invoke(Common("s_err_lack_redmerat")); break;
            case 0xb: SystemNotice?.Invoke(Common("s_guild_err_not_enough_guild_fund")); break;
            case 0xc: SystemNotice?.Invoke(Common("s_err_lack_reverse_coin")); break;
            case 0xd: SystemNotice?.Invoke(Common("s_err_lack_mentor_token")); break;
            case 0xe: SystemNotice?.Invoke(Common("s_err_lack_mentee_token")); break;
            case 0xf: SystemNotice?.Invoke(Common("s_err_lack_star_point")); break;
            case 0x10: SystemNotice?.Invoke(Common(0x41)); break;
            case 3: case 10: ShowLackMeratAsk?.Invoke(); break;   // nOpt 1 (FUN_14134a120): CUIService::ShowLackMeratAsk (uicommon.cpp:1086)
        }
    }

    /// <summary>CUIService::ShowLackMeratAsk (@0x141add210, uiservice.cpp:1108): the box asking about the lack of merat that a
    /// merat or blue merat shortfall shows (ShowLackCurrencyMessage, currency 3 and 10, uicommon.cpp:1084).</summary>
    public Action ShowLackMeratAsk;

    /// <summary>
    /// CNpcShopProductCli::FUN_140db8580 (npcshopproductbase.cpp:492-500): computes price * count and truncates to 32 bits
    /// before CanPayCurrency / HasEnoughCurrency (the client's 32-bit truncation, taken unsigned by CanPayCurrency's unsigned parameter); on failure posts
    /// FUN_14134a120 notice and returns false.
    /// </summary>
    public bool CanPayProduct(Product p, int count)
    {
        if (p == null) return false;
        // The int total goes to CanPayCurrency(unsigned nAmount) (unit_141347b10.cpp:574), zero-extended for HasEnoughCurrency
        // (uicommon.cpp:971): a price of 3,000,000,000 stays 3,000,000,000, and 2^32 wraps to 0.
        long total = unchecked((uint)(p.Record.Price * count));
        if (!HasEnoughCurrency(CurrencyOf(p.Record.Currency), total, p.Record.CurrencyItemId))
        {
            ShowLackCurrency(p.Record.Currency, p.Record.CurrencyItemId, total);
            return false;
        }
        return true;
    }

    /// <summary>CNpcShopProductCli::CheckStarPointEvent (npcshopproductbase.cpp:504): currency 0xd requires star event.</summary>
    public bool CheckStarPointEvent(Product p)
    {
        if (p != null && p.Record.Currency == 0xd && !Pc.StarPointEvent)
        {
            ShowSimpleBox(Common("s_shop_no_star_point_event"));
            return false;
        }
        return true;
    }

    /// <summary>CNpcShopProductCli::CheckBuyPeriod (npcshopproductbase.cpp:295-415): checks period window and life expiry.</summary>
    public bool CheckBuyPeriod(Product p, out string error)
    {
        error = "";
        if (p == null) return false;
        long now = ServerTime();
        if (p.Record.Period is { } per && now > 0)
        {
            if (per.HasTime && ((per.Start > 0 && now < per.Start) || (per.End > 0 && now > per.End)))
            {
                error = Common("s_err_invalid_item_cannot_buy_by_period");
                return false;
            }
            if (per.HasPart)
            {
                var local = DateTimeOffset.FromUnixTimeSeconds(now).LocalDateTime;
                if (per.Days.Count > 0 && !per.Days.Contains((int)local.DayOfWeek))
                {
                    error = Common("s_err_invalid_item_cannot_buy_by_period");
                    return false;
                }
                if (per.PartTimes.Count > 0)
                {
                    int sec = local.Hour * 3600 + local.Minute * 60 + local.Second;
                    bool inPart = false;
                    foreach (var (s, e) in per.PartTimes)
                        if (sec >= s && sec <= e) { inPart = true; break; }
                    if (!inPart)
                    {
                        error = Common("s_err_invalid_item_cannot_buy_by_period");
                        return false;
                    }
                }
            }
        }
        if (p.Record.Life is { Type: 2, Time: > 0 } life && now > 0 && now >= life.Time)
        {
            error = Common("s_blackmarket_error_buy_expired");
            return false;
        }
        return true;
    }

    /// <summary>CNpcShopProductCli::FUN_140db8210 (npcshopproductbase.cpp:438): max purchases allowed in one bill.</summary>
    public int MaxBuyCount(Product p)
    {
        if (p?.Item?.Data is not { } d) return 0;
        if (d.HasCubeTextureMesh && d.SlotMax == 1) return Math.Max(0, EmptySlotCount(0));
        if (d.IsStackable)
        {
            int slotMax = (int)Math.Max(1u, d.SlotMax);
            int buy = p.Record.Limit > 0 ? Math.Min(p.Record.RemainingItemCount, slotMax) : slotMax;
            if (p.Record.Bundle > 1) buy /= p.Record.Bundle;
            return buy;
        }
        return 1;
    }

    /// <summary>CNpcShopProductCli::GetMaxAffordableCount (npcshopproductbase.cpp:465): capped by owned currency and MaxBuyCount.</summary>
    public int MaxAffordableCount(Product p)
    {
        if (p == null) return 0;
        long price = p.Record.Price;
        if (price <= 0) return 1;
        int maxBuy = MaxBuyCount(p);
        long have = p.Record.Currency == 1
            ? (p.Record.CurrencyItemTag != 0 ? ItemTagCount(p.Record.CurrencyItemTag) : ItemCount(p.Record.CurrencyItemId, p.Item?.Data?.Rank ?? 0))
            : GetCurrencyAmount(CurrencyOf(p.Record.Currency), price, p.Record.CurrencyItemId);
        long afford = have / price;
        return (int)(afford < maxBuy ? afford : maxBuy);
    }

    Product FindCurrentProduct(int serial) =>
        group >= 0 && group < 3 && products[group].TryGetValue(serial, out var p) ? p : null;

    /// <summary>Guild trophy gate (FUN_141b047f0, uishopdialog.cpp:3563): requires guild membership, 7 days (604800 s) in
    /// the guild, and guild total trophies &gt;= product requirement.</summary>
    public bool CheckGuildTrophy(Product p, bool notify)
    {
        if (p == null) return false;
        int trophy = p.Record.GuildTrophy;
        if (trophy < 1) return true;
        if (!IsInGuild() || Model == null || !Model.HasLocalPlayer) return false;
        if (GuildMemberJoinTime() is not { } joinTime) return false;
        if (joinTime + 604800 > ServerTime())
        {
            if (notify) SystemNotice?.Invoke(Common("s_err_lack_guild_require_date"));
            return false;
        }
        if (trophy <= GuildTotalTrophy()) return true;
        if (notify) SystemNotice?.Invoke(Common(0x5b));
        return false;
    }

    /// <summary>Guild master gate (FUN_141b04fd0, uishopdialog.cpp:3697): currency 7 (guild fund) requires guild master.</summary>
    public bool CheckGuildMaster(Product p, bool notify)
    {
        if (p == null) return false;
        if (p.Record.Currency == 7)
        {
            if (Model == null || !Model.HasLocalPlayer) return false;
            if (!IsInGuild() || !IsGuildMaster())
            {
                if (notify) SystemNotice?.Invoke(Common("s_guild_err_buy_no_master"));
                return false;
            }
        }
        return true;
    }

    /// <summary>Achievement gate (FUN_141b04a10, uishopdialog.cpp:3593): checks IsBeforeStartGrade(achieve, grade).</summary>
    public bool CheckAchieve(Product p, bool notify)
    {
        if (p == null) return false;
        int id = p.Record.Achieve;
        if (id == 0) return true;
        int grade = p.Record.AchieveGrade;
        if (HasAchieve(id, grade)) return true;
        if (notify) SystemNotice?.Invoke(Common(0x5c, RequireAchieveText(id, grade) ?? ""));
        return false;
    }

    /// <summary>Fame (alliance) grade gate (FUN_141b04af0, uishopdialog.cpp:3612): requires fame grade &gt;= requirement.</summary>
    public bool CheckFameGrade(Product p, bool notify)
    {
        if (p == null) return false;
        int alliance = p.Record.Alliance;
        int grade = p.Record.FameGrade;
        if (grade == 0) return true;
        if (FameGradeOf(alliance) >= grade) return true;
        if (notify)
        {
            // GetAllianceName (questmasktable.cpp:556-558) and FUN_140762f50 (:571, GetStringByKey s_fame_grade_<n>)
            string allianceText = Common($"s_quest_alliance_{FameTable.AllianceName(alliance)}");
            string gradeText = StripTags(Common($"s_fame_grade_{grade}"));
            SystemNotice?.Invoke(Common("s_err_lack_fame_alliance", allianceText, gradeText));
        }
        return false;
    }

    /// <summary>Guild championship gate (FUN_141b04c30, uishopdialog.cpp:3638): requires guild rank and member score.</summary>
    public bool CheckChampionship(Product p, bool notify)
    {
        if (p == null) return false;
        int rank = p.Record.ChampionshipRank;
        int score = p.Record.ChampionshipScore;
        if (rank == 0 && score == 0) return true;
        if (!IsInGuild()) return false;
        if (GuildChampionshipRank() < rank)
        {
            if (notify) SystemNotice?.Invoke(Common(0x5d, GuildChampionshipRankName(rank) ?? ""));
            return false;
        }
        if (Model == null || !Model.HasLocalPlayer) return false;
        if (GuildMemberChampionshipScore() is not { } have) return false;
        if (have < score)
        {
            if (notify) SystemNotice?.Invoke(Common(0x5e, score));
            return false;
        }
        return true;
    }

    /// <summary>Guild NPC level gate (FUN_141b04ed0, uishopdialog.cpp:3677): requires guild NPC level &gt;= requirement.</summary>
    public bool CheckGuildNpc(Product p, bool notify)
    {
        if (p == null) return false;
        int npc = p.Record.GuildNpc;
        int level = p.Record.GuildNpcLevel;
        if (npc == 0) return true;
        if (!IsInGuild()) return false;
        if (level > GuildNpcLevel(npc))
        {
            if (notify) SystemNotice?.Invoke(Common("s_err_lack_guild_npc_level"));
            return false;
        }
        return true;
    }

    static string StripTags(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) return s ?? "";
        var sb = new System.Text.StringBuilder(s.Length);
        bool inTag = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// UIShopDialog::BuyProduct (@0x141afd8e0, uishopdialog.cpp:2423): checks star event, guild trophy, buy period,
    /// star point currency, affordability for 1 unit, guild master (currency 7), achievement, fame grade, guild trophy,
    /// championship, guild NPC level and sold-out status before opening the purchase bill (group 0) or requesting
    /// repurchase (group 1).
    /// </summary>
    public void BuyProduct(int serial, int rowIndex)
    {
        if (!HasStarEvent()) { ShowSimpleBox(Common("s_shop_no_star_point_event")); return; }
        var p = FindCurrentProduct(serial);
        if (p == null) return;
        if (!CheckGuildTrophy(p, true)) return;
        if (group != 1)
        {
            if (!CheckBuyPeriod(p, out string periodError)) { ShowSimpleBox(periodError); return; }
            if (!CheckStarPointEvent(p)) return;
            if (!CanPayProduct(p, 1)) return;
            if (!CheckGuildMaster(p, true)) return;
            if (!CheckAchieve(p, true)) return;
            if (!CheckFameGrade(p, true)) return;
            if (!CheckGuildTrophy(p, true)) return;
            if (!CheckChampionship(p, true)) return;
            if (!CheckGuildNpc(p, true)) return;
            if (p.Record.SoldOut) { ShowSimpleBox(Common(0x49)); return; }
            ShowPurchaseBill(p, rowIndex);
        }
        else
        {
            if (RequestRepurchase(serial))
            {
                EnableShop(false);
                selection.Set(serial, rowIndex, 3);
            }
        }
    }

    /// <summary>
    /// UIShopDialog::ShowPurchaseBill (@0x141b02770, uishopdialog.cpp:3136): stackable (MaxBuyCount &gt; 1) opens box 0x12
    /// ("shopTrade") with OnPriceChanged callback and tag "buyItem,{serial}"; single item confirms via box 0x11
    /// ("itemNotify") with tag "buyItem,{serial},1" when currency != 0 or price &gt;= ShopBuyConfirmPrice, else sends
    /// immediately and disables the shop until the server answers.
    /// </summary>
    void ShowPurchaseBill(Product p, int rowIndex)
    {
        if (p?.Item == null || Model == null) return;
        int serial = p.Record.Serial;
        int max = MaxBuyCount(p);
        if (max > 1)
        {
            var box = Hud.MessageBoxQueue.Create(0x12, Common(0x295), this, "");
            selection.Set(serial, rowIndex, 4);
            box.Title = Common(0x45f);
            box.SetButtonLabelIds(0x47d, 0x476, 0);
            box.SetItemEntry(p.Item, ItemName(p.Item), 1, 1, MaxAffordableCount(p), (int)p.Record.Price,
                Math.Max(1, p.Record.Bundle), Model.Tables.Items.TextColor);
            Func<object[], object> onPrice = OnPriceChanged;
            box.Values["shopTradeChangePriceFuntion"] = onPrice;
            box.Values["shopSerialID"] = serial;
            box.Extra = $"buyItem,{serial}";
            box.EnterAllowed = false;
            ShowBox?.Invoke(box);
        }
        else
        {
            bool confirm = p.Record.Currency != 0 || p.Record.Price >= Model.Tables.ShopBuyConfirmPrice;
            if (confirm)
            {
                selection.Set(serial, rowIndex, 4);
                var box = Hud.MessageBoxQueue.Create(0x11, Common(0x28d), this, $"buyItem,{serial},1");
                box.SetItemEntry(p.Item, ItemName(p.Item), 1, 0, -1, -1, 1, Model.Tables.Items.TextColor);
                box.SetButtonPreset(1);
                box.EnterAllowed = false;
                ShowBox?.Invoke(box);
            }
            else if (SendPurchase(serial, 1))
            {
                EnableShop(false);
                selection.Set(serial, rowIndex, 4);
            }
        }
    }

    /// <summary>UIShopDialog::OnPriceChanged (@0x141b03240, uishopdialog.cpp:3217): updates the shopTrade box's price HTML text.</summary>
    object OnPriceChanged(object[] args)
    {
        if (shop == null || args == null || args.Length < 3) return null;
        int serial = ToInt(args[0]);
        var field = args[1];
        long amount = (uint)ToInt(args[2]);
        var p = FindCurrentProduct(serial);
        if (p == null || field == null) return null;
        Host?.Set(field, "htmlText", PriceText(p.Record.Currency, p.Record.CurrencyItemId, amount, p.Record.IconTag));
        return null;
    }

    /// <summary>UIShopDialog::OnMessage (@0x141af5020, uishopdialog.cpp:949): answers the "buyItem,{serial},{count}" box.</summary>
    public bool OnMessageBox(Hud.MessageBoxController sender, int button, string tag)
    {
        if (button != 0) { CloseBox?.Invoke(); return false; }
        if (tag != null && tag.Contains("buyItem", StringComparison.Ordinal))
        {
            var parts = tag.Split(',');
            if (parts.Length != 3) return false;
            if (parts[1].Length > 0 && parts[2].Length > 0)
            {
                int serial = Hud.MessageBoxData.WToI(parts[1]);
                int count = Hud.MessageBoxData.WToI(parts[2]);
                var p = FindCurrentProduct(serial);
                if (p?.Item == null) return false;
                if (p.Item.Data.FunctionName == "UGCMapPackage" && NeedsUgcAgreement(p.Item.Data))
                {
                    CloseBox?.Invoke();
                    var box = Hud.MessageBoxQueue.CreateWithCallback(Common(0x6ec), 0x1f, ConfirmUgcPurchase);
                    box.Append(serial);
                    box.Append(count);
                    box.SetButtonPreset(2);
                    box.SetCheckbox0(Common(0x9d1), false);
                    box.SetAgreement(false);
                    box.SetSize(0x1c0, 400);
                    ShowBox?.Invoke(box);
                }
                else if (SendPurchase(serial, count))
                {
                    EnableShop(false);
                    CloseBox?.Invoke();
                }
            }
            return true;
        }
        return false;
    }

    /// <summary>ConfirmUgcPurchase (@0x141af8660, uishopdialog.cpp:1506): sends purchase of a UGCMapPackage after the 0x1f
    /// terms confirmation box.</summary>
    bool ConfirmUgcPurchase(int button, Hud.MessageBoxData box)
    {
        if (button == 0)
        {
            int serial = box.ArgInt(0, 0);
            int quantity = (short)box.ArgInt(1, 0);
            if (SendPurchase(serial, quantity))
            {
                EnableShop(false);
                CloseBox?.Invoke();
            }
        }
        return true;
    }

    /// <summary>UIShopDialog::SendPurchase (@0x141b00060, uishopdialog.cpp:2837): checks quantity &gt; 0, product existence
    /// and currency, then sends Outbound 0x28 mode 4 (serial, quantity, shopId) and plays shop_itembuy.</summary>
    public bool SendPurchase(int serial, int quantity)
    {
        if (quantity <= 0) return false;
        var p = FindCurrentProduct(serial);
        if (p == null) return false;
        if (!CanPayProduct(p, quantity)) return false;
        Send?.Invoke(new ShopBuyRequest(serial, quantity, info.ShopId));
        Sound?.Invoke("shop_itembuy");
        Request?.Invoke("buy", new object[] { serial, quantity });
        return true;
    }

    /// <summary>UIShopDialog::RequestRepurchase (@0x141affeb0, uishopdialog.cpp:2816): checks player mesos &gt;= price
    /// (notice 0x34 on fail), then sends Outbound 0x28 mode 3 (serial) and plays shop_itembuy.</summary>
    public bool RequestRepurchase(int serial)
    {
        var p = FindCurrentProduct(serial);
        if (p == null) return false;
        if (Pc.Mesos < p.Record.Price)
        {
            SystemNotice?.Invoke(Common(0x34));
            return false;
        }
        Send?.Invoke(new ShopRepurchaseRequest(serial));
        Sound?.Invoke("shop_itembuy");
        Request?.Invoke("repurchase", new object[] { serial });
        return true;
    }

    /// <summary>
    /// UIShopDialog::CanSellItem (@0x141afb780, uishopdialog.cpp:2076): checks open shop, shop shutdown (0x2f7), sell-only
    /// flag, star event, item lock (box 0x22), socketed gems, playing score, sellable flag (0xec) and restricted list (0x30b).
    /// </summary>
    public bool CanSellItem(Item item)
    {
        if (shop == null || item?.Data == null || Model == null) return false;
        if (ShopShutdown) { SystemNotice?.Invoke(Common(0x2f7)); return false; }
        if (info.SellOnly) { SystemNotice?.Invoke(Common("s_msg_cant_sell_to_only_sell_shop")); return false; }
        if (!HasStarEvent()) { ShowSimpleBox(Common("s_shop_no_star_point_event")); return false; }
        long now = ServerTime();
        if (!ItemTradeRules.NotLocked(item, now, out int lockReason))
        {
            var box = Hud.MessageBoxQueue.Create(0x22, "", null, "");
            if (lockReason == 2) box.Values["confirmMsg"] = "$s_itemlock_msgbox_lock_gemstone_notice";
            ShowBox?.Invoke(box);
            return false;
        }
        if (!ItemTradeRules.GemsShopSellable(item.Record?.Socket, Model.Tables.Items.Get, now))
        {
            ShowSimpleBox(Common("s_itembreak_usedgemstone_item"));
            return false;
        }
        if (item.Data.IsScoreItem && PlayingScoreUid() == item.Uid)
        {
            ShowSimpleBox(Common("s_play_instrument_cant_destory_score"));
            return false;
        }
        if (!ItemTradeRules.CanSellToShop(item, Model.Tables.Items.Get, now))
        {
            SystemNotice?.Invoke(Common(0xec));
            return false;
        }
        if (IsSaleRestricted(item.ItemId))
        {
            SystemNotice?.Invoke(Common(0x30b));
            return false;
        }
        return true;
    }

    /// <summary>UIShopDialog::SellItem (@0x141afe7f0, uishopdialog.cpp:2533): resolves the dragged inventory slot key and
    /// routes to <see cref="SellInventoryItem"/> (InvenSlot::FUN_14140be60).</summary>
    public void SellItem(string slotKey)
    {
        if (shop == null) return;
        Request?.Invoke("sell", new object[] { slotKey });
        var item = FindInventorySlotItem?.Invoke(slotKey);
        if (item == null) return;
        if (SellInventoryItem != null) SellInventoryItem(item);
        else if (CanSellItem(item)) RequestSellItem(item, (int)item.Count);
    }

    /// <summary>RequestSellItem (@0x141425fc0, itemugcmesh.cpp:1084): checks count &gt; 0 and summoned pet (notice
    /// s_err_cannot_sell_petitem_summon), then sends Outbound 0x28 mode 5 (itemUid, count).</summary>
    public bool RequestSellItem(Item item, int count)
    {
        if (item == null || count <= 0) return false;
        if (IsSummonedPet(item.Uid))
        {
            SystemNotice?.Invoke(Common("s_err_cannot_sell_petitem_summon"));
            return false;
        }
        Send?.Invoke(new ShopSellRequest(item.Uid, count));
        return true;
    }

    /// <summary>UIShopDialog::ResetList (@0x141afe8b0, uishopdialog.cpp:2548): sends Outbound 0x28 mode 9 (resetPrice) and
    /// disables the shop until the server responds.</summary>
    public void ResetList()
    {
        Send?.Invoke(new ShopResetListRequest(resetPrice));
        Request?.Invoke("reset", new object[] { resetPrice });
        EnableShop(false);
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
                var (icon, vspace) = CurrencyIcon(CurrencyOf(info.ResetTooltipCurrency), info.ResetCurrencyItem, Model.IsFeatureEnabled(Feature.RedMerat));
                text = Common("s_shop_tooltip_lua_reset", (uint)info.ResetCount, $"<img src='{icon}' vspace='{vspace}'/>");
            }
            else text = Common("s_shop_tooltip_reset");
            var tip = AsTooltip.Text(text);
            tip?.SetTooltipAlign(4);
            return tip;
        }
        if (name == "m_coinInfo")
        {
            int coinId = Model.Tables.GachaBoxes.CoinItemIdByShopId(info.ShopId);
            int boxId = coinId != 0 ? Model.Tables.GachaBoxes.RandomBoxIdByCoinItem(coinId) : 0;
            if (boxId == 0 || !Model.Tables.GachaBoxes.HasBox(boxId)) return null;
            var tip = AsTooltip.Text(Model.Tables.ItemName(coinId) ?? "");
            tip?.SetTooltipAlign(4);
            return tip;
        }
        var row = Arg(args, 1);
        if (row == null || row is Undefined) return null;
        int serial = ToInt(Host.Get(row, "serialID"));
        int type = ToInt(Host.Get(row, "productType"));
        if (type != 0 && type != 2) return null;
        if (group < 0 || group >= 3 || !products[group].TryGetValue(serial, out var p) || p.Item == null) return null;
        // vfunc_49 (uishopdialog.cpp:1288-1311): flags 4, plus 0x4800 when equipment & (ComparesItems || type == 2)
        int rhsFlags = 4;
        if (p.Item.Data.Type == 1 && (info.ComparesItems || type == 2)) rhsFlags |= 0x4800;
        var record = Wearer?.Invoke();
        var equipment = WornEquipment.Of(record, Model.Tables.Items);
        var worn = (rhsFlags & 0x4800) != 0 ? equipment?.ForCompare(p.Item, Model.Tables.ItemModels, ShiftHeld()) : null;
        var t = new ItemTooltip(Model, rhsFlags)
        {
            Owner = record != null ? (record.CharacterId, record.Name) : null,
            OwnerWearsUid = uid => equipment?.Wears(uid) == true,
            OwnerEquipment = equipment,
            ShiftHeld = ShiftHeld,
        };
        if (type == 0 && p.Record.Achieve != 0 && !HasAchieve(p.Record.Achieve, p.Record.AchieveGrade))
        {
            string req = RequireAchieveTooltipText(p.Record.Achieve, p.Record.AchieveGrade);
            if (!string.IsNullOrEmpty(req))
                t.ShopGuideText = Common("s_shop_tooltip_require_achieve", req);   // FUN_1409dbc30 (astooltip.cpp:506)
        }
        if (!t.FillCompare(worn, p.Item)) return null;
        t.SetEnablePreviewBoth(null, p.Item);
        return t.Data;
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
