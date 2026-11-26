using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Shop;

namespace Ms2Godot.Ui;

/// <summary>
/// The npc shop: UIShopDialog (0x4e3b, movie uishopdialog) with its C++ side as <see cref="ShopController"/>.
/// Live: <see cref="WatchShop"/> subscribes to <see cref="Ms2Godot.Net.LiveSession.ShopReceived"/> (Inbound 0x51) and
/// <see cref="Ms2Godot.Net.LiveSession.TalkUiOpen"/> (CNpc::OpenTalkUi kind 1); command 0 opens the movie with the
/// header already read so initShop picks the right shop variant, and Outbound 0x28 goes out through the session.
/// Offline: <see cref="OpenShop"/> opens the fixture gear shop (<see cref="UiFixture.GearShop"/>) for display / --shop shots.
/// </summary>
public partial class HudScene
{
    MovieDialog shopMovie;
    ShopController shop;
    ShopController liveShop;
    string shopShot;                            // --shop all|weapons|armor|buyback: the fixture gear shop on that filter or tab
    bool shopWatched;
    Action unwatchShop;

    bool ShopOpen => shop != null && !shop.IsClosed;

    ShopController CreateShopController(Ms2Godot.Net.LiveSession live)
    {
        long Now() => live?.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var c = new ShopController
        {
            Categories = new ShopCategoryTable(assets.Packs),
            Npcs = World.FieldContents.Npcs(assets.Packs),
            ServerTime = Now,
            SaleEvents = live?.SaleEvents ?? new Ms2.Ui.Shop.ShopSaleEvents(),
            ShopSeedResetTime = (int)Ms2.Ui.FeatureElements.Constants(assets.Packs).Float("ShopSeedResetTime", 0),
            ItemUseError = itemUse.Error,
            EmptySlotCount = tab => live?.Inventory?.Cells(tab).Count(cell => cell.Empty) ?? 0,
            ItemTab = id => live?.Inventory?.TabOfItem(id) ?? -1,
            InventoryTabName = tab => inventory?.Tabs.FirstOrDefault(t => t.Tab == tab).Name is { } n ? hudModel.Tables.Common("s_inventory_tab_" + n) ?? "" : "",
            FindInventorySlotItem = key => inventory?.FindSlotItem(key),
            SellInventoryItem = item => inventory?.TrySellItem(item),
            Wearer = () => live != null ? live.Me.ToPcInfo(Now()) : OfflineMe().ToPcInfo(Now()),
            ShiftHeld = () => Input.IsKeyPressed(Key.Shift),
            LinkShopItem = (itemId, rarity, _, _) =>
            {
                if (Ms2.Ui.Items.Item.Create(hudModel.Tables.Items, itemId, rarity) is { } it) ChatLog.LinkItem(it);
            },
            LinkPlainItem = (itemId, rarity) =>
            {
                if (Ms2.Ui.Items.Item.Create(hudModel.Tables.Items, itemId, rarity) is { } it) ChatLog.LinkItem(it);
            },
            SystemNotice = SystemNoticeText,
            PreviewItem = item => OpenItemPreview(item, 1),    // ItemPreviewMgr::OpenPreview(item, 1) (uishopdialog.cpp:2603)
            NoticeWithType = (text, _) => SystemNoticeText(text),
            Sound = key => FieldAudio.Instance?.PlayUi(key),
        };
        if (live != null)
        {
            c.BindLiveState(
                () => live.Guilds, () => live.Achieves, () => achieveTexts, () => live.Me,
                () => live.Items.All, () => live.PlayingScoreUid, hudModel.Tables.Items, itemUse.Error);
            // PlotIndoorSize stays null: the plot's indoor size (FUN_1408a0d30) is not ported
            c.UgcHome = () => new ShopController.UgcHomeState(live.Home.HasInfo, live.Home.PlotId, null, live.Home.HasNoHome);
            c.BindLiveNetwork(live.SendShop, () => live.SendTalk(new NpcTalkEndRequest()));
            c.PlayerTalking = () => live.IsTalking();     // MyPC::FUN_141527e60 (UIShopDialog::vfunc_72 closes the shop once it is false)
            BindMeratQueue(live, c);
        }
        else
        {
            c.Request += (name, _) => GD.Print($"shop: {name} is not sent (display only)");
        }
        c.OpenDialog += id =>
        {
            if (id == TokenPocketController.UiId) { if (!TokenPocketOpen) OpenTokenPocket(); }
            else GD.Print($"shop: dialog 0x{id:x} is not in this HUD");
        };
        c.InventoryWindowPos += _ =>
        {
            if (inventory == null || inventory.IsClosed) OpenInventory();
            if (inventory != null && shop != null) shop.PlaceBesideInventory(inventory, !inventory.IsClosed);
        };
        return c;
    }

    void WatchShop()
    {
        if (shopWatched || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        shopWatched = true;
        liveShop ??= CreateShopController(live);
        void OnPacket(ShopPacket p)
        {
            liveShop.OnShopPacket(p);
            if (p.Command == 0 && !ShopOpen) OpenLiveShop();
        }
        void OnOpen(int id)
        {
            if (id == ShopController.UiId && !ShopOpen && liveShop.Info.ShopId != 0) OpenLiveShop();
        }
        void OnClose(int id)
        {
            if (id == ShopController.UiId && ShopOpen) shop.Close();
        }
        void OnSystemShop(SystemShopPacket p) => SystemShopFlow.OnSystemShop(p, liveShop, key => hudModel.Tables.Common(key),
            () => { if (!ShopOpen) OpenLiveShop(); }, MessageBoxQueue.Show);
        live.ShopReceived += OnPacket;
        live.MeretMarketReceived += liveShop.OnMeretMarket;
        live.SystemShopReceived += OnSystemShop;
        live.TalkUiOpen += OnOpen;
        live.TalkUiClose += OnClose;
        unwatchShop = () =>
        {
            live.ShopReceived -= OnPacket;
            live.MeretMarketReceived -= liveShop.OnMeretMarket;
            live.SystemShopReceived -= OnSystemShop;
            live.TalkUiOpen -= OnOpen;
            live.TalkUiClose -= OnClose;
            shopWatched = false;
        };
    }

    void OpenLiveShop()
    {
        if (GuideRefusesOpen("ShopDialog") || liveShop == null) return;
        if (shopMovie != null) { shopMovie.Host.Unload(); shopMovie.QueueFree(); shopMovie = null; }
        BindBoxAnswers();
        var m = new MovieDialog(assets, liveShop, hudModel, ScreenMode.PixelSize(GetWindow()));
        liveShop.Unloaded += OnLiveShopUnloaded;
        void OnLiveShopUnloaded()
        {
            liveShop.Unloaded -= OnLiveShopUnloaded;
            m.Host.Unload();
            m.QueueFree();
            if (shopMovie == m) shopMovie = null;
        }
        shop = liveShop; shopMovie = m;
        uiRoot.AddChild(m);
        liveShop.PlaceBesideInventory(inventory, inventory != null && !inventory.IsClosed);
    }

    /// <summary>UIShopDialog::vfunc_72 each frame (timer rollover, zero allocations).</summary>
    void ShopTick() { if (ShopOpen) shop.Process(); }

    /// <summary>UIShopDialog's keys: Escape closes (CloseShop), Enter searches when m_inputSearch is focused (vfunc_13).</summary>
    bool ShopKeys(InputEventKey k)
    {
        if (!ShopOpen) return false;
        if (k.Keycode == Key.Escape) { shop.CloseShop(); return true; }
        if (k.Keycode == Key.Enter) return shop.OnEnterKey();
        return false;
    }

    /// <summary>
    /// Opens the npc shop over the fixture gear shop (<see cref="UiFixture.GearShop"/>, in the shop packet's shape): the
    /// header before the movie loads, the products after, as the server's commands 0 and 1 arrive. Buy, sell and restock
    /// requests are only logged.
    /// </summary>
    void OpenShop()
    {
        if (GuideRefusesOpen("ShopDialog")) return;
        if (shopMovie != null) { shopMovie.Host.Unload(); shopMovie.QueueFree(); shopMovie = null; }
        long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (info, products) = UiFixture.GearShop(Now());
        var c = CreateShopController(null);
        c.ReadShopInfo(info);
        var m = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.ReadProducts(products);
        // the movie goes when the close tween ends (OnEndClose -> Unload), as the other dialogs' movies do
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (shopMovie == m) shopMovie = null;
        };
        shop = c; shopMovie = m;
        uiRoot.AddChild(m);
    }

    /// <summary>The shot's filter or tab, picked through the movie's own list and tab bar (their change events call back).</summary>
    void PickShopView(string view)
    {
        if (shopMovie == null || shop?.ShopView == null) return;
        var h = shopMovie.Host;
        if (view == "buyback") { h.Set(h.Get(shop.ShopView, "m_tab"), "selectedIndex", 1); return; }
        // shopcategory.xml category 5: the Weapons and Armor tabs' name ids
        int nameId = view switch { "weapons" => 2017, "armor" => 2018, _ => 0 };
        if (nameId == 0) return;
        int row = shop.Tabs(0).ToList().FindIndex(r => r.Tab.NameId == nameId);
        if (row >= 0) h.Set(h.Get(shop.ShopView, "m_categoryList"), "selectedIndex", row);
    }
}
