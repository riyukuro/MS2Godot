using Godot;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The mail list: UIMailListDialog (0x4e49, movie uimaillistdialog) over CUserMail's mails. Live, the book is the session's
/// (the Mail packets, 0x54) and its sends go out (Mail 0x2D: the list, read, take and delete requests). Offline, an empty book,
/// and the sends are logged. The C++ side is kept between opens, as the client keeps the dialog (its page and check marks are
/// reset at each load). The send dialog (0x4e4a), the sender popup (0x4e39) and the ad bill (0x4f17) are not in this HUD:
/// their requests are logged (STILL-OPEN).
/// </summary>
public partial class HudScene
{
    MailListController mail;            // its C++ side, kept between opens
    MovieDialog mailMovie;              // the movie, from open until OnEndClose
    static MailBook offlineMails;
    static MailTexts mailTexts;         // CSystemMailContent and the text helpers, read once
    bool mailsWatched;

    bool MailOpen => mail != null && !mail.IsClosed;

    /// <summary>CUIService::ToggleDialog(0x4e49): open when closed, else vfunc_67.</summary>
    void ToggleMail()
    {
        if (!MailOpen) OpenMail();
        else mail.Close();
    }

    /// <summary>DAT_143ab0250's index of the client language (GetDateString's switch): KR 0, JP 1, EN 2, CN 3, FR 4, DE 5,
    /// PT 6, ES 7.</summary>
    int ClientLanguageIndex() => Array.IndexOf(new[] { "KR", "JP", "EN", "CN", "FR", "DE", "PT", "ES" },
                                               (assets.Language ?? "").ToUpperInvariant()) is var i and >= 0 ? i : 0;

    /// <summary>
    /// The book's texts (LoadSystemMailContent, with ReplaceObjectWords of the quest log's word tables) and the helpers
    /// CMailInfo::Read and the dialog resolve with: GetStringCommonByKey, the string tables, GetLocalizedText and the
    /// reserved words (the value words with the local player's name).
    /// </summary>
    MailTexts MailTexts()
    {
        if (mailTexts == null)
        {
            var langs = new GameStrings(assets.Packs, assets.Language).Languages;
            var words = (questTable ??= QuestTable.Load(assets.Packs, langs, data.Locale, f => !data.FeaturesOff.Contains(f))).Words;
            mailTexts = Ms2.Ui.MailTexts.Load(assets.Packs, langs, data.Locale, words.ReplaceObjectWords);
            string lang = (assets.Language ?? "").ToUpperInvariant();
            int region = lang switch { "KR" => 0, "JP" => 1, "CN" => 3, _ => 2 };   // as NewNotices: assumed from the language
            var reserved = hudModel.Tables.ReservedWords;
            string Player() => hudModel.HasLocalPlayer ? hudModel.Player.Name : null;
            mailTexts.CommonByKey = hudModel.Tables.Common;
            mailTexts.TableString = hudModel.Tables.StringTableText;
            mailTexts.LocalizedText = t => ServerStringGroup.LocalizedText(t, lang, region);
            mailTexts.ReplaceValueWords = t => reserved.ReplaceValueWords(t, Player());
            mailTexts.ReplaceObjectAndValueWords = t => reserved.ReplaceValueWords(words.ReplaceObjectWords(t), Player());
        }
        return mailTexts;
    }

    /// <summary>
    /// The book's UI calls (CSceneStateGame::OnMail): the notices (ShowSystemNotice with bLog as a chat line of kind 0x14),
    /// the message boxes, the sounds, the open list's OnReadMail and RequestRefresh, and the game menu's mail count
    /// (FUN_141938ae0(0x4e49, 0)). Subscribed once per scene.
    /// </summary>
    void WatchMails()
    {
        if (mailsWatched || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        mailsWatched = true;
        var book = live.Mails;
        book.Texts = MailTexts();
        live.MailListOpen = () => MailOpen;
        void Notice(string text, int type, bool log)
        {
            if (string.IsNullOrEmpty(text) || SystemText == null) return;
            SystemText.ShowMessage(text, type, 0);
            if (log) ChatLog.AddSystemLine(text, TalkKind.SystemNotice);
        }
        void Chat(string text) => ChatLog.AddSystemLine(text, TalkKind.SystemNotice);
        void Box(string text) => MessageBoxQueue.ShowMessageBox(text, 0, null, "");
        void Sound(string key) => FieldAudio.Instance?.PlayUi(key);
        void Read(long id) { if (MailOpen) mail.OnReadMail(id); }
        void Refresh() { if (MailOpen) mail.RequestRefresh(); }
        void Count() => gameMenu?.UpdateNotifyCount(0x4e49);
        void Sent() => GD.Print("mail: sent (the send dialog 0x4e4a is not in this HUD)");
        void Gift(Ms2.Net.Packets.MailMessage.Gift g) => GD.Print($"mail: gift from {g.Sender} (the gift dialog 0x4e5e is not in this HUD)");
        book.Notice += Notice;
        book.ChatLine += Chat;
        book.Box += Box;
        book.Sound += Sound;
        book.ReadMail += Read;
        book.RefreshRequested += Refresh;
        book.MenuCount += Count;
        book.Sent += Sent;
        book.GiftReceived += Gift;
        unwatchMails = () =>
        {
            book.Notice -= Notice; book.ChatLine -= Chat; book.Box -= Box; book.Sound -= Sound; book.ReadMail -= Read;
            book.RefreshRequested -= Refresh; book.MenuCount -= Count; book.Sent -= Sent; book.GiftReceived -= Gift;
            live.MailListOpen = () => false;
            mailsWatched = false;
        };
    }

    Action unwatchMails;

    /// <summary>
    /// FUN_1408e48f0 each frame: the book's due sounds. STILL-OPEN: the field test (WorldService::FUN_141bcfbd0) is taken as
    /// this HUD being up and the sub scene test (GFxSceneService::FUN_1422b9ea0) as false.
    /// </summary>
    void MailTick()
    {
        Ms2Godot.Net.LiveSession.Instance?.Mails.Tick(true, false);
        if (MailOpen) mail.Process();   // UIMailListDialog::vfunc_15: the flags the packets set
    }

    /// <summary>UIMailListDialog's key handling, as UIDialog's: Escape closes it.</summary>
    bool MailKeys(InputEventKey k)
    {
        if (!MailOpen || k.Keycode != Key.Escape) return false;
        mail.Close();
        return true;
    }

    /// <summary>
    /// Opens the mail list (FUN_141924680 -> vfunc_65): the movie runs its own ActionScript with UIMailListDialog's C++ side
    /// (MailListController). A dialog that is open is left; the movie is dropped when its close tween ends.
    /// </summary>
    void OpenMail()
    {
        if (GuideRefusesOpen("MailListDialog")) return;
        if (MailOpen) return;
        if (mailMovie != null) { mailMovie.Host.Unload(); mailMovie.QueueFree(); mailMovie = null; }
        var live = Ms2Godot.Net.LiveSession.Instance;
        var book = live != null ? live.Mails : (offlineMails ??= new MailBook(MailTexts()));
        book.Texts = MailTexts();
        if (mail == null || mail.Book != book)
        {
            var c = new MailListController(book)
            {
                ServerTime = () => live?.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Language = ClientLanguageIndex,
                Send = packet =>
                {
                    if (Ms2Godot.Net.LiveSession.Instance is { } s) s.SendMail(packet);
                    else GD.Print($"mail: offline, not sent: {packet}");
                },
            };
            c.SystemNotice += text => SystemNoticeText(text);
            c.SendRequested += () => GD.Print("mail: the send dialog (0x4e4a) is not in this HUD");
            c.ReplyRequested += m => GD.Print($"mail: reply to {m.Sender} (the send dialog 0x4e4a is not in this HUD)");
            c.SenderMenuRequested += (name, x, y) => GD.Print($"mail: sender menu for {name} (0x4e39 is not in this HUD)");
            c.BuyAdRequested += m => GD.Print($"mail: buy ad {m.Id} (the bill 0x4f17 is not in this HUD)");
            c.Unloaded += () => { mailMovie?.Host.Unload(); mailMovie?.QueueFree(); mailMovie = null; };
            mail = c;
        }
        var t = System.Diagnostics.Stopwatch.StartNew();
        mailMovie = new MovieDialog(assets, mail, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"mail: load={mailMovie.Loaded} in {t.ElapsedMilliseconds} ms ({book.Mails.Count} mails)");
        uiRoot.AddChild(mailMovie);
    }

    /// <summary>The scripted live run's mail list: opened as the O key does (CUIService::ToggleDialog(0x4e49)); false when
    /// it was already up.</summary>
    public bool OpenMailScripted()
    {
        if (MailOpen) return false;
        OpenMail();
        return true;
    }

    /// <summary>The scripted live run's mail list, while it is up.</summary>
    public MailListController MailList => MailOpen ? mail : null;

    /// <summary>
    /// A scripted click in the mail list on the member reached by <paramref name="path"/>, from the dialog: a name is a
    /// member, "#n" the display list's child n (getChildAt), "tab:n" the tab bar's button n (getButtonAt) and "row:n" the
    /// list's row n (getRendererAt). The click lands at the middle of the member's bounds; false while the dialog or a step
    /// is not there. The mail page (MailPage and its kinds) is m_contents' child 0, with its buttons "ok" (take or reply) and
    /// "remove".
    /// </summary>
    public bool ClickMail(params string[] path)
    {
        if (!MailOpen || mailMovie is not { } m) return false;
        object at = m.Host.Entity;
        foreach (var step in path)
        {
            if (step.StartsWith('#') && int.TryParse(step.AsSpan(1), out int ci)) at = m.Host.Invoke(at, "getChildAt", ci);
            else if (step.StartsWith("tab:") && int.TryParse(step.AsSpan(4), out int ti)) at = m.Host.Invoke(at, "getButtonAt", ti);
            else if (step.StartsWith("row:") && int.TryParse(step.AsSpan(4), out int ri)) at = m.Host.Invoke(at, "getRendererAt", ri);
            else at = m.Host.Child(step, at);
            if (at == null) return false;
        }
        if (at is not Ms2.Avm2.Flash.AsDisplayObject o) return false;
        var b = o.GetBounds(null);
        m.Player.Click(b.X + b.W * 0.5, b.Y + b.H * 0.5);
        return true;
    }

    /// <summary>FUN_141275260's tail for a resolved text: ShowSystemNotice(text, 1, true).</summary>
    void SystemNoticeText(string text)
    {
        if (string.IsNullOrEmpty(text) || SystemText == null) return;
        SystemText.ShowMessage(text, SystemTextController.Warning, 0);
        ChatLog.AddSystemLine(text, TalkKind.SystemNotice);
    }
}
