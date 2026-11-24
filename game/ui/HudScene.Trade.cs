using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The trade: CTradeManagerEx (the session's <see cref="TradeBook"/>, the Trade packets 0x82 and sends 0x49) and its dialog
/// UITradeDialogEx (0x4e83, movie uitradedialogex) with its C++ side as <see cref="TradeDialogController"/>. Live only: with
/// no session there is no partner, so nothing opens. The item material sounds (TradeSlot::PlayItemSound, PlayItemSoundByUid)
/// play. STILL-OPEN: the item event dispatcher's listeners (UIItemEventDispatcher vfunc_17/18) are logged; the dialogs
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
        c.ItemSound += item => PlayItemSound(item?.Data);
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
        tradeMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"trade: load={tradeMovie.Loaded} in {t.ElapsedMilliseconds} ms with {live.Trades.PartnerName}");
        uiRoot.AddChild(tradeMovie);
        c.PlaceBesideInventory();
    }

    // ---- scripted live runs (MS2_LIVE_TRADE) ---------------------------------------------------------------------------

    /// <summary>Scripted runs: the trade dialog is open.</summary>
    public bool TradeDialogShown => TradeOpen;

    /// <summary>Scripted runs: both sides latched in the open dialog.</summary>
    public bool TradeBothLatched => TradeOpen && trade.MineLatched && trade.PartnerLatched;

    /// <summary>Scripted runs: my side latched in the open dialog.</summary>
    public bool TradeMineLatched => TradeOpen && trade.MineLatched;

    /// <summary>Scripted runs: the latch sounds heard since the dialog opened (mine and the partner's lock).</summary>
    public int TradeLocksHeard { get; private set; }

    /// <summary>Scripted runs: the popup menu's Trade on a field user (SendTradeRequest).</summary>
    public void TradeAsk(long characterId) => Trades?.RequestTrade(characterId);

    /// <summary>
    /// Scripted runs: the inventory opened on the tab of the <paramref name="n"/>th held item (counting the tabs in order),
    /// and that item right-clicked (InvenSlot's use: with the trade dialog at the head, TryAddToTrade); a count box that
    /// follows is answered by the caller. 0 when there is no such item, else its uid.
    /// </summary>
    public long TradeRightClickItem(int n)
    {
        var view = Ms2Godot.Net.LiveSession.Instance?.Inventory;
        if (view == null) return 0;
        if (inventory == null || inventory.IsClosed) OpenInventory();
        if (inventory?.Host == null) { GD.Print("live: trade: no inventory movie"); return 0; }
        for (int t = 0; t < inventory.Tabs.Count; t++)
        {
            var cells = view.Cells(inventory.Tabs[t].Tab);
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].Empty || n-- > 0) continue;
                inventory.Host.Set(inventory.Host.Child("m_tab"), "selectedIndex", t);
                GD.Print($"live: trade: right-click item {cells[i].ItemId} x{cells[i].Amount} (tab {inventory.Tabs[t].Tab} slot {i})");
                inventory.OnCallback("notifySlotUse", new object[] { Ms2.Ui.Hud.InventoryController.SlotName(i), 1 });
                return cells[i].Uid;
            }
        }
        GD.Print($"live: trade: no held item {n} in tabs {string.Join(",", inventory.Tabs.Select(x => $"{x.Tab}:{view.Used(x.Tab)}"))}");
        return 0;
    }

    /// <summary>Scripted runs: my side's trade slot <paramref name="slot"/> right-clicked (TradeSlot's use: SendTradeMode9).</summary>
    public void TradeRemoveMine(int slot) => TradeCallback("notifySlotUse", TradeDialogController.SlotName(true, slot), 1);

    /// <summary>Scripted runs: the item ids and counts on both sides of the trade table.</summary>
    public string TradeTableText()
    {
        var items = Trades?.Items.AllItems;
        return items == null ? "" : string.Join(", ", items.Select(i => $"{i.ItemId} x{i.Count}"));
    }

    /// <summary>Scripted runs: every held item as "id xcount", over the tabs in order, and the mesos the HUD shows.</summary>
    public string BagText()
    {
        var view = Ms2Godot.Net.LiveSession.Instance?.Inventory;
        if (view == null) return "";
        var held = new List<string>();
        for (int t = 0; t < Ms2.Net.Packets.ItemBook.Tabs; t++)
            foreach (var c in view.Cells(t))
                if (!c.Empty) held.Add($"{c.ItemId} x{c.Amount} (tab {t})");
        return $"mesos {(hudModel.HasLocalPlayer ? hudModel.Player.Mesos : -1)}; items {string.Join(", ", held)}";
    }

    /// <summary>Scripted runs: a callback of the trade movie (OnLock with the mesos text, OnConfirm, OnClose).</summary>
    public void TradeCallback(string name, params object[] args)
    {
        if (TradeOpen) trade.OnCallback(name, args);
    }
}

