using Godot;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The black market: UIBlackMarketDialog (0x4e97, movie uiblackmarketdialog) over CBlackMarketMan's lists. Live only: the
/// book is the session's (the BlackMarket packets, 0x98) and the server opens the dialog (mode 0x16, the NPC's talk). Its
/// sends go out through the session (BlackMarket 0x5F). The C++ side is kept between opens, as the client keeps the dialog.
/// </summary>
public partial class HudScene
{
    BlackMarketDialogController blackMarket;
    MovieDialog blackMarketMovie;
    bool blackMarketWatched;

    bool BlackMarketOpen => blackMarket != null && !blackMarket.IsClosed;

    /// <summary>
    /// The book's UI calls (CBlackMarketMan's notices, boxes, chat lines and sounds) and its dialog (the open dialog, or
    /// none) and lookups (the bag's FindItemByUid, the mesos, the constants). Subscribed once per scene.
    /// </summary>
    void WatchBlackMarket()
    {
        if (blackMarketWatched || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        blackMarketWatched = true;
        var book = live.BlackMarket;
        var t = hudModel.Tables;
        book.Common = t.Common;
        book.CommonKey = t.Common;
        book.ItemName = t.ItemName;
        book.ItemData = t.Items;
        book.SellMinPrice = t.BlackMarketSellMinPrice;
        book.SellMaxPrice = t.BlackMarketSellMaxPrice;
        book.FindItem = uid => live.Inventory?.FindByUid(uid) is { } e ? InventoryController.ToItem(e, t.Items) : null;
        book.Mesos = () => hudModel.HasLocalPlayer ? hudModel.Player.Mesos : 0;
        book.Dialog = () => BlackMarketOpen ? blackMarket : null;
        void Notice(string text, int type, bool log)
        {
            if (string.IsNullOrEmpty(text) || SystemText == null) return;
            SystemText.ShowMessage(text, type, 0);
            if (log) ChatLog.AddSystemLine(text, TalkKind.SystemNotice);
        }
        void Chat(string text, int kind) => ChatLog.AddSystemLine(text, kind);
        void Box(string text) => MessageBoxQueue.ShowMessageBox(text, 0, null, "");
        void Sound(string key) => FieldAudio.Instance?.PlayUi(key);
        void Open(int id) { if (id == 0x4e97) OpenBlackMarket(); }
        book.Notice += Notice;
        book.ChatLine += Chat;
        book.Box += Box;
        book.Sound += Sound;
        book.OpenDialog += Open;
        unwatchBlackMarket = () =>
        {
            book.Notice -= Notice; book.ChatLine -= Chat; book.Box -= Box; book.Sound -= Sound; book.OpenDialog -= Open;
            book.Dialog = () => null;
            blackMarketWatched = false;
        };
    }

    Action unwatchBlackMarket;

    /// <summary>UIBlackMarketDialog::vfunc_15 each frame (the pages' ticks).</summary>
    void BlackMarketTick() { if (BlackMarketOpen) blackMarket.Process(); }

    /// <summary>UIBlackMarketDialog's keys: Escape closes it (UIDialog's), Enter and Tab go to the movie (vfunc_13).</summary>
    bool BlackMarketKeys(InputEventKey k)
    {
        if (!BlackMarketOpen) return false;
        if (k.Keycode == Key.Escape) { blackMarket.Close(); return true; }
        if (k.Keycode is Key.Enter or Key.KpEnter) { blackMarket.Key(true); return true; }
        if (k.Keycode == Key.Tab) { blackMarket.Key(false); return true; }
        return false;
    }

    /// <summary>
    /// Opens the black market (CUIService::ShowDialog(0x4e97) -> vfunc_73, vfunc_65): the can-open test, the movie with the
    /// dialog's C++ side, then SendBlackMarketMode1. The movie is dropped when its close tween ends.
    /// </summary>
    void OpenBlackMarket()
    {
        if (BlackMarketOpen || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        if (blackMarketMovie != null) { blackMarketMovie.Host.Unload(); blackMarketMovie.QueueFree(); blackMarketMovie = null; }
        var t = hudModel.Tables;
        if (blackMarket == null)
        {
            var c = new BlackMarketDialogController(live.BlackMarket)
            {
                Table = t.BlackMarket,
                ServerTime = () => live.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                OwnAccountId = () => Ms2Godot.Flow.Session.Chosen?.User?.AccountId ?? 0,
                FeatureEnabled = hudModel.IsFeatureEnabled,
                Notice = SystemNoticeText,
                JobName = code => t.JobName(code),
                RankName = r => t.Common($"s_word_rank_{r:00}"),
                SellEndDay = () => t.BlackMarketSellEndDay,
                Sound = key => FieldAudio.Instance?.PlayUi(key),
                ItemMessage = (item, text) => SystemNoticeText(text),
            };
            c.Unloaded += () => { blackMarketMovie?.Host.Unload(); blackMarketMovie?.QueueFree(); blackMarketMovie = null; };
            blackMarket = c;
        }
        if (!blackMarket.CanOpen()) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        blackMarketMovie = new MovieDialog(assets, blackMarket, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"black market: load={blackMarketMovie.Loaded} in {sw.ElapsedMilliseconds} ms");
        uiRoot.AddChild(blackMarketMovie);
        if (blackMarketMovie.Loaded) blackMarket.Opened();
    }

    /// <summary>Scripted runs: the dialog opened as the server's open answer (mode 0x16) does.</summary>
    public bool OpenBlackMarketScripted() { OpenBlackMarket(); return BlackMarketOpen; }

    /// <summary>
    /// Scripted runs: a click at the centre of the movie object at <paramref name="path"/> (member names; "tab:N" a button
    /// bar's button; "find:NAME" the first visible descendant of that name, depth first).
    /// </summary>
    public bool ClickBlackMarket(params string[] path)
    {
        if (!BlackMarketOpen || blackMarketMovie is not { } m) return false;
        object at = m.Host.Entity;
        foreach (var step in path)
        {
            if (step.StartsWith("tab:") && int.TryParse(step.AsSpan(4), out int ti)) at = m.Host.Invoke(at, "getButtonAt", ti);
            else if (step.StartsWith("find:")) at = FindVisible(at as Ms2.Avm2.Flash.AsDisplayObject, step[5..]);
            else at = m.Host.Child(step, at);
            if (at == null) return false;
        }
        if (at is not Ms2.Avm2.Flash.AsDisplayObject o) return false;
        var b = o.GetBounds(null);
        m.Player.Click(b.X + b.W * 0.5, b.Y + b.H * 0.5);
        return true;
    }

    static Ms2.Avm2.Flash.AsDisplayObject FindVisible(Ms2.Avm2.Flash.AsDisplayObject o, string name)
    {
        if (o == null || !o.visible) return null;
        if (o.InstanceName == name) return o;
        if (o is Ms2.Avm2.Flash.AsContainer c)
            for (int i = 0; i < c.Children.Count; i++) if (FindVisible(c.Children[i], name) is { } hit) return hit;
        return null;
    }

    /// <summary>The scripted live run's black market, while it is up.</summary>
    public BlackMarketDialogController BlackMarketDialog => BlackMarketOpen ? blackMarket : null;
}
