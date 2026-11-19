using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The trade: CTradeManagerEx (the session's <see cref="TradeBook"/>, the Trade packets 0x82 and sends 0x49) and its dialog
/// UITradeDialogEx (0x4e83, movie uitradedialogex) with its C++ side as <see cref="TradeDialogController"/>. Live only: with
/// no session there is no partner, so nothing opens. STILL-OPEN: the item material sounds (TradeSlot::PlayItemSound,
/// PlayItemSoundByUid) and the item event dispatcher's listeners (UIItemEventDispatcher vfunc_17/18) are logged; the dialogs
/// the start closes (0x4e3b, 0x4ec1, 0x4e47) are not in this HUD.
/// </summary>
public partial class HudScene
{
    TradeDialogController trade;        // from open until the close
    MovieDialog tradeMovie;             // the movie, from open until OnEndClose
    bool tradesWatched;
    Action unwatchTrades;

    bool TradeOpen => trade != null && !trade.IsClosed;

    /// <summary>
    /// The book's UI calls (CTradeManagerEx's handlers): ShowSystemNotice (bLog as a chat line of kind 0x14), the chat lines
    /// (kind 0x12), the sounds, the dialog open and close. Subscribed once per scene.
    /// </summary>
    void WatchTrades()
    {
        if (tradesWatched || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        tradesWatched = true;
        var book = live.Trades;
        book.ItemData = hudModel.Tables.Items;
        book.Common = id => hudModel.Tables.Common(id) ?? "";
        book.CommonKey = key => hudModel.Tables.Common(key) ?? "";
        book.ItemName = id => hudModel.Tables.ItemName(id) ?? "";
        book.RequestDuration = () => hudModel.Tables.TradeRequestDuration;
        // CGameOption's community block +8 (Unk_CommunityStateB m_bDisableTradeRequest, trademanagerex_1.cpp:388)
        book.RefuseTrade = () => GameOptions.AccountCommunity.DisableTradeRequest;
        book.Dialog = () => TradeOpen ? trade : null;
        void Notice(string text, int type, bool log)
        {
            if (string.IsNullOrEmpty(text) || SystemText == null) return;
            SystemText.ShowMessage(text, type, 0);
            if (log) ChatLog.AddSystemLine(text, TalkKind.SystemNotice);
        }
        void Chat(string text, int kind) { if (!string.IsNullOrEmpty(text)) ChatLog.AddSystemLine(text, kind); }
        void Sound(string key) => FieldAudio.Instance?.PlayUi(key);
        void Open(int id) { if (id == TradeBook.DialogId) OpenTrade(); }
        void Close(int id)
        {
            if (id == TradeBook.DialogId) { if (TradeOpen) trade.Close(); }
            else GD.Print($"trade: dialog 0x{id:x} is not in this HUD");
        }
        void Item(Ms2.Ui.Items.Item item, bool added) =>
            GD.Print($"trade: item event {(added ? "added" : "removed")} {item?.Uid} (the dispatcher's listeners are not ported)");
        book.Notice += Notice;
        book.ChatLine += Chat;
        book.Sound += Sound;
        book.OpenDialog += Open;
        book.CloseDialog += Close;
        book.ItemEvent += Item;
        unwatchTrades = () =>
        {
            book.Notice -= Notice; book.ChatLine -= Chat; book.Sound -= Sound; book.OpenDialog -= Open;
            book.CloseDialog -= Close; book.ItemEvent -= Item;
            book.Dialog = () => null;
            tradesWatched = false;
        };
    }

    /// <summary>The book the inventory and the popup menu use: the session's, null offline.</summary>
    static TradeBook Trades => Ms2Godot.Net.LiveSession.Instance?.Trades;

    /// <summary>UITradeDialogEx's key handling, as UIDialog's: Escape closes it (vfunc_67, SendTradeMode7).</summary>
    bool TradeKeys(InputEventKey k)
    {
        if (!TradeOpen || k.Keycode != Key.Escape) return false;
        trade.Close();
        return true;
    }

    /// <summary>
    /// OpenDialog(0x4e83) from OnTradeStarted: the movie runs its own ActionScript with UITradeDialogEx's C++ side. vfunc_66
    /// places it beside the open inventory, or opens the inventory beside it ("openWindowPos").
    /// </summary>
    void OpenTrade()
    {
        if (TradeOpen || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        if (tradeMovie != null) { tradeMovie.Host.Unload(); tradeMovie.QueueFree(); tradeMovie = null; }
        var me = live.Me;
        var c = new TradeDialogController
        {
            Book = live.Trades,
            MyName = () => hudModel.HasLocalPlayer ? hudModel.Player.Name : "",
            MyProfile = () => me?.User is { } u ? Ms2Godot.Net.ProfileImageService.Images.Path(u.CharacterId, u.Picture, true) : "",
            // FUN_141028be0(id, picture, 1): the default image for a picture not downloaded yet
            ProfilePath = (id, picture) => Ms2Godot.Net.ProfileImageService.Images.Path(id, picture, true),
            ActionKeyDown = ActionKeyDown,
            ShiftHeld = () => Input.IsKeyPressed(Key.Shift),
            LinkItem = ChatLog.LinkItem,
            OpenInventory = () => inventory != null && !inventory.IsClosed ? inventory : null,
        };
        TradeLocksHeard = 0;
        c.SystemSound += key =>
        {
            if (key == "trade_lock") TradeLocksHeard++;
            FieldAudio.Instance?.PlayUi(key);
        };
        c.ItemSound += item => GD.Print($"trade: item sound {item?.ItemId} (the material sounds are not ported)");
        c.InventoryWindowPos += id =>
        {
            // the inventory's "openWindowPos": it opens beside the asking dialog
            if (inventory == null || inventory.IsClosed) OpenInventory();
            trade?.PlaceBesideInventory();
        };
        c.InventoryTradeClosed += () => GD.Print("trade: the inventory's trade mode end (FUN_141999e00) is not ported");
        c.Button += name => GD.Print($"trade: {name} is not ported");
        c.Unloaded += () =>
        {
            tradeMovie?.Host.Unload(); tradeMovie?.QueueFree(); tradeMovie = null;
        };
        trade = c;
        var t = System.Diagnostics.Stopwatch.StartNew();
        tradeMovie = new MovieDialog(assets, c, hudModel, GetWindow().Size);
        GD.Print($"trade: load={tradeMovie.Loaded} in {t.ElapsedMilliseconds} ms with {live.Trades.PartnerName}");
        uiRoot.AddChild(tradeMovie);
        c.PlaceBesideInventory();
    }

    // ---- scripted live runs (MS2_LIVE_TRADE) ---------------------------------------------------------------------------

    /// <summary>Scripted runs: the trade dialog is open.</summary>
    public bool TradeDialogShown => TradeOpen;

    /// <summary>Scripted runs: the latch sounds heard since the dialog opened (mine and the partner's lock).</summary>
    public int TradeLocksHeard { get; private set; }

    /// <summary>Scripted runs: the popup menu's Trade on a field user (SendTradeRequest).</summary>
    public void TradeAsk(long characterId) => Trades?.RequestTrade(characterId);

    /// <summary>
    /// Scripted runs: the inventory opened on its first tab holding an item, and that item right-clicked (InvenSlot's use: with
    /// the trade dialog at the head, TryAddToTrade); a count box that follows is answered with its OK. False when the bag is empty.
    /// </summary>
    public bool TradeRightClickFirstItem()
    {
        var view = Ms2Godot.Net.LiveSession.Instance?.Inventory;
        if (view == null) return false;
        if (inventory == null || inventory.IsClosed) OpenInventory();
        if (inventory?.Host == null) return false;
        for (int t = 0; t < inventory.Tabs.Count; t++)
        {
            if (view.Used(inventory.Tabs[t].Tab) == 0) continue;
            inventory.Host.Set(inventory.Host.Child("m_tab"), "selectedIndex", t);
            var cells = view.Cells(view.Tab);
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].Empty) continue;
                GD.Print($"live: trade: right-click item {cells[i].Uid} (tab {inventory.Tabs[t].Tab} slot {i})");
                inventory.OnCallback("notifySlotUse", new object[] { Ms2.Ui.Hud.InventoryController.SlotName(i), 1 });
                return true;
            }
        }
        return false;
    }

    /// <summary>Scripted runs: a callback of the trade movie (OnLock with the mesos text, OnConfirm, OnClose).</summary>
    public void TradeCallback(string name, params object[] args)
    {
        if (TradeOpen) trade.OnCallback(name, args);
    }
}

