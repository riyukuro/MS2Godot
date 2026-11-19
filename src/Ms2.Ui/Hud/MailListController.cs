using Ms2.Avm2;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The C++ side of the mail list, UIMailListDialog (client/ms2/uimaillistdialog.cpp; UI id 0x4e49), for its movie
/// uimaillistdialog. The list is the player's mails (<see cref="MailBook"/>, CUserMail): tab 1 the player mails (kinds 1..3),
/// any other tab the system mails (kinds 100..0x70) and the ad mails (200), 14 rows a page. The mail sends (read, take,
/// delete; SendOp::Mail 0x2D) go through <see cref="Send"/>.
/// STILL-OPEN: the send dialog (0x4e4a, onSendMail and onReply, with CBlockManager's block check 0xb), the sender popup
/// (0x4e39, onRClickMailSender), the ad bill (0x4f17, onBuyAd and its tooltip FUN_1419e8b70), the wedding ticket of kind
/// 0x70 (WeddingManager::ReadTicketMail) and the token price text (FUN_1419e9400 = FUN_1418fec00(GetPayType(type))) are
/// not ported; they come out as events or are left out as noted below.
/// </summary>
public sealed class MailListController : UiController
{
    public override string Movie => "uimaillistdialog";
    protected override string EntityMethod => "setMailInfo";
    /// <summary>GetTypeName (@0x1419e3630).</summary>
    public override string TypeName => "MailListDialog";

    /// <summary>The rows of one page (RefreshMailList, IsPageValid, OnMailRemoved).</summary>
    public const int PageSize = 14;

    public MailBook Book { get; }
    MailTexts Texts => Book.Texts;

    int page;                                           // m_nPage
    readonly Dictionary<long, bool> checkedMails = new(); // m_mapChecked
    bool refresh;                                       // m_bRefresh
    long timerStart;                                    // m_timer

    /// <summary>The server time (GetServerTime), unix seconds.</summary>
    public Func<long> ServerTime = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    /// <summary>A millisecond clock for m_timer (Init / Check(1000)).</summary>
    public Func<long> Milliseconds = () => Environment.TickCount64;
    /// <summary>The client language GetDateString reads (MailTexts.DateString).</summary>
    public Func<int> Language = () => 0;
    /// <summary>FUN_1422dc430, the test SendMailMode0 makes before it sends. STILL-OPEN: what it tests; true here.</summary>
    public Func<bool> CanRequest = () => true;
    /// <summary>FUN_1419e9400(type, amount): a benefit's price text. STILL-OPEN (FUN_1418fec00 and GetPayType are not
    /// ported): with null the "token{n}" members are left out.</summary>
    public Func<int, long, string> PriceText;
    /// <summary>FUN_140710590: the send.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>FUN_14151f150 (FUN_140d73c60 and ShowMessageBox): show a message box.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>FUN_141275260(id, 1, 1): a system notice of a text (also to the chat log).</summary>
    public event Action<string> SystemNotice;
    /// <summary>onSendMail: OpenDialog(0x4e4a) after the block check (STILL-OPEN).</summary>
    public event Action SendRequested;
    /// <summary>ReplyToMail: SetReplyMail on the send dialog, then ToggleDialog(0x4e4a) (STILL-OPEN).</summary>
    public event Action<MailInfo> ReplyRequested;
    /// <summary>onRClickMailSender on a kind-1 mail: the popup 0x4e39 at (x, y) with the sender (STILL-OPEN).</summary>
    public event Action<string, float, float> SenderMenuRequested;
    /// <summary>onBuyAd: OnBuyAd (@0x1419e8080), the market bill 0x4f17 (STILL-OPEN).</summary>
    public event Action<MailInfo> BuyAdRequested;

    public MailListController(MailBook book) => Book = book;

    /// <summary>The page shown (m_nPage).</summary>
    public int Page => page;

    /// <summary>OnLoadComplete (@0x1419e36f0): UIDialog's, the check marks cleared, page 0, the page text, SendMailMode0.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        checkedMails.Clear();
        page = 0;
        UpdatePageText();
        SendListRequest();
    }

    /// <summary>vfunc_67 (@0x1419e36b0): the attachment slots dropped, UIDialog's close, then no refresh due.</summary>
    public override void Close()
    {
        ClearAttachmentTooltips();
        base.Close();
        refresh = false;
    }

    /// <summary>
    /// vfunc_15 (@0x1419e4de0), each frame while the movie is there: the book's three flags (DAT_143af37b0 the list again
    /// and the open mail; DAT_143af37b8 one mail's view; DAT_143af37b4 a mail deleted, with notice 0x22a), then the list
    /// request one second after RequestRefresh.
    /// </summary>
    public void Process()
    {
        if (Host?.Entity == null) return;
        if (Book.ListRefresh)
        {
            Book.ListRefresh = false;
            RefreshMailList();
            long open = OpenMailId();
            if (open != 0) SetMailInfo(open);
        }
        if (Book.ListInfoId != 0)
        {
            SetMailInfo(Book.ListInfoId);
            Book.ListInfoId = 0;
        }
        if (Book.ListRemoved)
        {
            OnMailRemoved();
            Book.ListRemoved = false;
            SystemNotice?.Invoke(Texts.Common(0x22a));
        }
        // m_timer.Check(1000). STILL-OPEN: whether Check restarts the timer; it is only read once per RequestRefresh, as
        // SendMailMode0 clears m_bRefresh.
        if (refresh && Milliseconds() - timerStart >= 1000) SendListRequest();
    }

    /// <summary>OnMailRemoved (@0x1419e4f10): the page pulled back to the last one, the list, and the view emptied.</summary>
    void OnMailRemoved()
    {
        int last = Math.Max(1, (int)MathF.Ceiling(GetTabMails().Count / 14.0f));
        if (page >= last)
        {
            page = last - 1;
            UpdatePageText();
        }
        RefreshMailList();
        SetMailInfo(0);
    }

    /// <summary>OnReadMail (@0x1419e5000): the movie's readMail with the id, and the counts.</summary>
    public void OnReadMail(long id)
    {
        if (Host?.Entity == null) return;
        Host.Invoke(Host.Entity, "readMail", MailTexts.IdText(id));
        UpdateMailCount();
    }

    /// <summary>OnReceiveMail (@0x1419e50b0): the movie's receiveMail with the id. The client has no caller of it in
    /// OnMail.</summary>
    public void OnReceiveMail(long id)
    {
        if (Host?.Entity == null) return;
        Host.Invoke(Host.Entity, "receiveMail", MailTexts.IdText(id));
    }

    /// <summary>RequestRefresh (@0x1419e5150): the list request is due in one second.</summary>
    public void RequestRefresh()
    {
        refresh = true;
        timerStart = Milliseconds();
    }

    /// <summary>UpdateMailCount (@0x1419e5900): onMailCount(the unread player mails, the unread system mails).</summary>
    void UpdateMailCount()
    {
        if (Host?.Entity == null) return;
        int user = 0, system = 0;
        for (int k = 1; k < 4; k++) user += Book.CountUnreadByKind(k);
        for (int k = 100; k < 0x71; k++) system += Book.CountUnreadByKind(k);
        Host.Invoke("onMailCount", user, system);
    }

    static bool IsSystemKind(int kind) => kind >= 100 && kind < 0x71;
    static bool IsAdMail(MailInfo m) => m.Kind == 200;              // IsAdMail @0x1419ead90
    static bool IsAdSoldOut(MailInfo m) => m.AdSold != 0;           // IsAdSoldOut @0x1419eadd0 (m_n130)

    /// <summary>
    /// SetMailInfo (@0x1419e5a10): the movie's setMailInfo with the mail's view (an empty object when not found). The
    /// system kinds' sender, title and content go through ReplaceObjectAndValueWords. Not ported: kind 0x70's "wedding"
    /// member (ReadTicketMail, STILL-OPEN) and the "token{n}" members without <see cref="PriceText"/>.
    /// </summary>
    public void SetMailInfo(long id)
    {
        ClearAttachmentTooltips();
        if (Host?.Entity == null) return;
        var info = new List<(string, object)>();
        if (Book.Find(id) is { } m)
        {
            string sender = m.Sender, title = m.Title, content = m.Content;
            if (IsSystemKind(m.Kind))
            {
                sender = Texts.ReplaceObjectAndValueWords(sender);
                title = Texts.ReplaceObjectAndValueWords(title);
                content = Texts.ReplaceObjectAndValueWords(content);
            }
            info.Add(("sender", sender));
            info.Add(("title", title));
            info.Add(("content", content));
            info.Add(("attachment", m.HasAttachment() ? 1 : 0));
            info.Add(("state", StateName(m)));
            info.Add(("binding", BindAttachmentTooltips(m)));
            info.Add(("date", MailTexts.DateString(m.SentTime, Language())));
            long meso = MesoAmount(m), merat = MeratAmount(m), red = MeratRedAmount(m);
            if (meso > 0) info.Add(("meso", Thousands(meso)));          // FUN_1409404f0
            if (merat > 0) info.Add(("merat", Thousands(merat)));
            if (red > 0) info.Add(("meratRed", Thousands(red)));
            AddTokens(m, info);
            switch (m.Kind)
            {
                case 3:
                    info.Add(("disableReply", true));
                    break;
                case 200:
                    info.Add(("productName", m.AdName));
                    info.Add(("price", m.AdPrice.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    info.Add(("salePrice", m.AdSalePrice.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    info.Add(("expired", m.AdExpired(ServerTime()) ? 1 : 0));
                    break;
                default:
                    if (m.Kind >= 1 && m.Kind < 4) info.Add(("visibleUserMailIcon", true));
                    break;
            }
        }
        Host.Invoke("setMailInfo", Host.Obj(info.ToArray()));
    }

    /// <summary>GetMesoAmount (@0x1419e9160): the unclaimed meso range (a begin other than 0, no claim: not IsUnclaimed's
    /// begin above 0) plus the meso benefit (type 0, sub 0).</summary>
    public static long MesoAmount(MailInfo m) => Unclaimed(m.Meso) + Benefit(m, 0, 0);

    /// <summary>GetMeratAmount (@0x1419e9200): the unclaimed merat range plus the merat benefits (type 2, subs 0..3).</summary>
    public static long MeratAmount(MailInfo m) =>
        Unclaimed(m.Meret) + Benefit(m, 2, 0) + Benefit(m, 2, 1) + Benefit(m, 2, 2) + Benefit(m, 2, 3);

    /// <summary>GetMeratRedAmount (@0x1419e9360): the unclaimed red merat range plus the benefit type 8, sub 0.</summary>
    public static long MeratRedAmount(MailInfo m) => Unclaimed(m.RedMeret) + Benefit(m, 8, 0);

    static long Unclaimed(MailRange r) => r.Begin != 0 && r.Claim == 0 ? r.Begin : 0;

    /// <summary>FindBenefit(type, sub) then GetAvailableAmount (0 when used or not found; a negative amount counts).</summary>
    static long Benefit(MailInfo m, int type, int sub) =>
        m.Benefits.FirstOrDefault(b => b.Type == type && b.Sub == sub)?.AvailableAmount ?? 0;

    /// <summary>FUN_1419e9460: the other benefits (not types 2 or 8, sub 0) with an available amount other than 0 (a
    /// negative one shows) as "token1".."tokenN".</summary>
    void AddTokens(MailInfo m, List<(string, object)> info)
    {
        if (PriceText == null) return;
        int n = 1;
        foreach (var b in m.Benefits)
        {
            if (b.Type == 2 || b.Type == 8 || b.Sub != 0 || b.AvailableAmount == 0) continue;
            info.Add(($"token{n++}", PriceText(b.Type, b.AvailableAmount)));
        }
    }

    /// <summary>GetMailStateName (@0x1419e7260).</summary>
    static string StateName(MailInfo m)
    {
        if (IsAdMail(m)) return IsAdSoldOut(m) ? "ad" : "ad_attached";
        if (m.Kind == 0x70) return "wedding_invitation";
        return m.HasAttachment() ? "attached" : "non_attached";
    }

    /// <summary>
    /// GetDaysLeftString (@0x1419e95d0): past the expiry 0x80b; under a day 0x809, or for the tooltip 0x80a(hours,
    /// minutes); else 0x807 / 0x808 (tooltip) with the days.
    /// </summary>
    public string DaysLeft(MailInfo m, bool tooltip)
    {
        long now = ServerTime(), expire = m.ExpireTime;
        if (now > expire) return Texts.Common(0x80b);
        var span = TimeSpan.FromSeconds(expire - now);
        if (span.Days < 1)
        {
            if (!tooltip) return Texts.Common(0x809);
            long secs = (long)span.TotalSeconds;
            return Texts.CommonF(0x80a, secs / 3600, secs / 60 % 60);
        }
        return Texts.CommonF(tooltip ? 0x808 : 0x807, span.Days);
    }

    /// <summary>"MailListDialog.{n}": the slot names ClearAttachmentTooltips and BindAttachmentTooltips use ("%s.%d").</summary>
    string SlotName(int n) => $"{TypeName}.{n}";

    /// <summary>
    /// BindAttachmentTooltips (@0x1419e67a0): an ad mail's ads (an item of the ad's id and rarity, its count clamped) or the
    /// attachments not yet received, each as "MailListDialog.{n}" in order (CreateMailItemSlot, an ASSlot of icon type 0x15
    /// in mailitemslot.cpp); the count bound. STILL-OPEN: the slot's tooltip value (m_nValue: the ad's m_n24, the
    /// attachment's m_n20) and the slot flags, kept at ItemInfoSlot's defaults.
    /// </summary>
    int BindAttachmentTooltips(MailInfo m)
    {
        int n = 0;
        var table = Model.Tables.Items;
        for (int i = 0; i < 5; i++)
        {
            Item item;
            if (IsAdMail(m))
            {
                if (m.Ads[i] is not { } ad) continue;
                item = Item.Create(table, ad.ItemId, ad.Rarity);          // FUN_14052b130(m_n18, m_n20, 0)
                if (item == null) continue;
                item.SetCountClamped(ad.Value1c);
            }
            else
            {
                if (m.Attachments[i] is not { ReceiveTime: 0 } a) continue;
                item = a.Item != null ? InventoryController.ToItem(a.Item, table) : null;
            }
            string key = SlotName(n++);
            Model.ItemSlots[key] = new ItemInfoSlot(key, item, i, 0x15, 0);
        }
        return n;
    }

    /// <summary>ClearAttachmentTooltips (@0x1419e6b70): RemoveMailItemSlot of the five slot names.</summary>
    void ClearAttachmentTooltips()
    {
        if (Host?.Model is not { } model) return;
        for (int i = 0; i < 5; i++) model.ItemSlots.Remove(SlotName(i));
    }

    /// <summary>FUN_1419e6c30: the movie's getMailID, atoi'd (0 when empty).</summary>
    long OpenMailId()
    {
        if (Host?.Entity == null) return 0;
        string s = Rt.ToString(Host.Invoke("getMailID")) ?? "";
        return s.Length != 0 ? ReservedWords.Atoi(s) : 0;
    }

    /// <summary>GetSelectedTab (@0x1419e7fd0): m_tab's selectedIndex (-1 without it).</summary>
    int SelectedTab() => Child("m_tab") is { } bar ? Rt.ToInt32(Host.Get(bar, "selectedIndex")) : -1;

    object Child(string name) => Host?.Entity == null ? null : Host.Child(name);

    /// <summary>IsMailInCurrentTab (@0x1419e6ec0).</summary>
    bool InCurrentTab(MailInfo m, int tab) =>
        tab == 1 ? m.Kind >= 1 && m.Kind < 4 : IsSystemKind(m.Kind) || IsAdMail(m);

    /// <summary>
    /// GetTabMails (@0x1419e6d20): the tab's ad mails in book order, then the others by FUN_1406911c0 (the sent time) from
    /// the newest. STILL-OPEN: CRBMultiMap's order of equal times; here the later-added one of a tie comes first, as the
    /// map's tail-to-head walk of an insert-after-equal tree gives.
    /// </summary>
    public List<MailInfo> GetTabMails()
    {
        int tab = SelectedTab();
        var ads = new List<MailInfo>();
        var rest = new List<(long Time, int Order, MailInfo Mail)>();
        int order = 0;
        foreach (var m in Book.Mails)
        {
            if (!InCurrentTab(m, tab)) continue;
            if (IsAdMail(m)) ads.Add(m);
            else rest.Add((m.SentTime, order++, m));
        }
        ads.AddRange(rest.OrderByDescending(r => r.Time).ThenByDescending(r => r.Order).Select(r => r.Mail));
        return ads;
    }

    /// <summary>IsPageValid (@0x1419e6fe0): 0 &lt;= page &lt; ceil(the tab's mail count / 14), counted by kind (the ad
    /// mails not counted).</summary>
    bool IsPageValid(int p)
    {
        int count = 0;
        if (SelectedTab() == 1) for (int k = 1; k < 4; k++) count += Book.CountByKind(k);
        else for (int k = 100; k < 0x71; k++) count += Book.CountByKind(k);
        int pages = (int)MathF.Ceiling(count / 14.0f);
        return p >= 0 && p < pages;
    }

    /// <summary>UpdatePageText (@0x1419e73f0): m_page shows the page + 1.</summary>
    void UpdatePageText()
    {
        if (Child("m_page") is { } t) Host.Set(t, "text", (page + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// RefreshMailList (@0x1419e5190): m_list cleaned up, then the page's rows (an expired mail skipped, and its place on
    /// the page not given to another), the open mail's row selected (-1 when not on the page), the counts, the page buttons
    /// and m_checkAll cleared.
    /// </summary>
    public void RefreshMailList()
    {
        long open = OpenMailId();
        int select = -1;
        if (Child("m_list") is { } list)
        {
            var dp = Host.Get(list, "dataProvider");
            Host.Invoke(dp, "cleanUp");                                 // FUN_1422dd1d0
            long now = ServerTime();
            int index = -1, row = 0;
            foreach (var m in GetTabMails())
            {
                index++;
                if (index < page * PageSize) continue;
                if (index >= page * PageSize + PageSize) break;
                if (m.IsExpired(now)) continue;
                checkedMails.TryGetValue(m.Id, out bool sel);
                string sender = m.Sender, title = m.Title;
                if (IsSystemKind(m.Kind))
                {
                    sender = Texts.ReplaceObjectAndValueWords(sender);
                    title = Texts.ReplaceObjectAndValueWords(title);
                }
                Host.Invoke(dp, "push", Host.Obj(
                    ("mailSN", MailTexts.IdText(m.Id)),
                    ("sender", sender),
                    ("content", title),
                    ("date", DaysLeft(m, false)),
                    ("dateTooltip", DaysLeft(m, true)),
                    ("read", m.IsRead ? 1 : 0),
                    ("attachment", m.HasAttachment() ? 1 : 0),
                    ("select", sel ? 1 : 0),
                    ("adMail", IsAdMail(m) ? 1 : 0),
                    ("adSoldout", IsAdSoldOut(m) ? 1 : 0),
                    ("adExpired", m.AdExpired(now) ? 1 : 0)));
                if (m.Id == open) select = row;
                row++;
            }
            Host.Invoke(list, "invalidate");
            Host.Invoke(list, "validateNow");                           // vfunc_24(true)
            Host.Set(list, "selectedIndex", select);
        }
        UpdateMailCount();
        if (Child("m_prev") is { } prev) Host.Set(prev, "enabled", IsPageValid(page - 1));
        if (Child("m_next") is { } next) Host.Set(next, "enabled", IsPageValid(page + 1));
        if (Child("m_checkAll") is { } all) Host.Set(all, "selected", false);
    }

    /// <summary>SendMailMode0 (@0x1419e90d0): the list request (u8 0) while the movie is there, and no refresh due.</summary>
    void SendListRequest()
    {
        if (Host?.Entity == null || !CanRequest()) return;
        Send(new MailListRequest());
        refresh = false;
    }

    /// <summary>SendMailMode2 (@0x1419e61c0): the read mark of a mail not read yet.</summary>
    void SendRead(long id)
    {
        if (Book.Find(id) is not { } m || m.ReadTime != 0) return;
        Send(new MailReadRequest(m.Id));                                // u8 2, i64 GetId
    }

    /// <summary>SendMailMode11 (@0x1419e6290): take what the mail holds, when it holds something (FUN_140690b80).</summary>
    void SendCollect(long id)
    {
        if (Book.Find(id) is not { } m || !m.HasClaimable(ServerTime())) return;
        Send(new MailCollectRequest(m.Id));                             // u8 0xb, i64 GetId
    }

    /// <summary>
    /// FUN_1419e6410 (onRemove): no id gives box 0x23d; an expired ad mail goes at once (u8 0xd, count 1, the id); else
    /// the confirm box (0x235; an ad not sold out 0x236; a mail holding something 0x237) answered by SendMailMode13.
    /// </summary>
    void RemoveOne(long id)
    {
        if (id == 0)
        {
            ShowBox(MessageBoxQueue.Create(0, Texts.Common(0x23d), null, ""));     // FUN_140d73bc0(0x23d, 0, 0, "")
            return;
        }
        if (Book.Find(id) is not { } m) return;
        int str = 0x235;
        if (IsAdMail(m))
        {
            if (m.AdExpired(ServerTime()))
            {
                Send(new MailDeleteRequest(new[] { m.Id }));
                return;
            }
            if (!IsAdSoldOut(m)) str = 0x236;
        }
        else if (m.HasAttachment()) str = 0x237;
        var box = MessageBoxQueue.Create(1, Texts.Common(str), null, "");
        box.Args.Add(MailTexts.IdText(id));
        box.Callback = OnRemoveAnswer;
        ShowBox(box);
    }

    /// <summary>HasLockedAttachment (@0x1419e74e0): an attachment not received whose item is locked
    /// (CItem::FUN_140528e20: lock state 1, or state 0 before the unlock time).</summary>
    bool HasLockedAttachment(long id)
    {
        if (Book.Find(id) is not { } m) return false;
        long now = ServerTime();
        foreach (var a in m.Attachments)
        {
            if (a == null || a.ReceiveTime > 0) continue;
            if (a.Item is { } e && (e.LockState == 1 || (e.LockState == 0 && now < e.UnlockTime))) return true;
        }
        return false;
    }

    /// <summary>ParseMailIds (@0x1419e7ea0): the ","-split ids the book has; with skipAttached a mail holding something is
    /// left out.</summary>
    List<long> ParseMailIds(string ids, bool skipAttached)
    {
        var res = new List<long>();
        foreach (var s in ids.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            long id = unchecked((long)ReservedWords.ParseUInt64(s));
            if (Book.Find(id) is not { } m) continue;
            if (m.HasAttachment() && skipAttached) continue;
            res.Add(id);
        }
        return res;
    }

    /// <summary>FUN_1419e70c0: one of the ","-split ids is a mail holding something.</summary>
    bool AnyAttached(string ids)
    {
        var parts = ids.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        return parts.Any(s => Book.Find(unchecked((long)ReservedWords.ParseUInt64(s))) is { } m && m.HasAttachment());
    }

    static string Arg0(MessageBoxData d) => d.Args.Count > 0 ? d.Args[0] : "";     // FUN_141a02fd0(0, "")

    /// <summary>SendMailMode13 (@0x1419e7c80): OK deletes the one mail (u8 0xd, count 1, GetId) unless an attachment is
    /// locked (box s_itemlock_mail_remove).</summary>
    bool OnRemoveAnswer(int button, MessageBoxData d)
    {
        if (button != 0) return true;
        long id = unchecked((long)ReservedWords.ParseUInt64(Arg0(d)));
        if (HasLockedAttachment(id))
        {
            ShowBox(MessageBoxQueue.Create(0, Texts.CommonByKey("s_itemlock_mail_remove"), null, ""));
            return true;
        }
        if (Book.Find(id) is { } m) Send(new MailDeleteRequest(new[] { m.Id }));
        return true;
    }

    /// <summary>SendMailMode18 (@0x1419e7640): OK marks the listed mails read (u8 0x12).</summary>
    bool OnReadListAnswer(int button, MessageBoxData d)
    {
        if (button != 0) return true;
        Send(new MailReadListRequest(ParseMailIds(Arg0(d), false)));
        return true;
    }

    /// <summary>SendMailMode19 (@0x1419e77c0): OK takes what the listed mails hold (u8 0x13).</summary>
    bool OnReceiveListAnswer(int button, MessageBoxData d)
    {
        if (button != 0) return true;
        Send(new MailCollectListRequest(ParseMailIds(Arg0(d), false)));
        return true;
    }

    /// <summary>
    /// FUN_1419e7940: OK deletes the listed mails (u8 0xd); the mails holding something stay out unless the box's check
    /// (FUN_141a02d50, the clicked tag) holds "true"; a locked attachment among them gives box s_itemlock_mail_remove.
    /// </summary>
    bool OnDeleteListAnswer(int button, MessageBoxData d)
    {
        if (button != 0) return true;
        bool found = (d.ClickedTag ?? "").Contains("true", StringComparison.Ordinal);
        var ids = ParseMailIds(Arg0(d), !found);
        if (ids.Any(HasLockedAttachment))
        {
            ShowBox(MessageBoxQueue.Create(0, Texts.CommonByKey("s_itemlock_mail_remove"), null, ""));
            return true;
        }
        Send(new MailDeleteRequest(ids));
        return true;
    }

    void ClearChecks()
    {
        checkedMails.Clear();
        if (Child("m_checkAll") is { } all) Host.Set(all, "selected", false);
    }

    /// <summary>vfunc_28 (@0x1419e37d0). Only onSelectMail and onSendMail answer 1; the others run and answer 0.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        bool any = args is { Length: > 0 };   // ASData::SetArgsIfAny
        string Str(int i) => args.Length > i ? Rt.ToString(args[i]) ?? "" : "";
        long AtoiArg() => ReservedWords.Atoi(Str(0));                  // atoi(FUN_1422b8d30())
        long U64Arg(int i) => unchecked((long)ReservedWords.ParseUInt64(Str(i)));
        switch (name.ToLowerInvariant())
        {
            case "onselectmail":
                if (any)
                {
                    long id = AtoiArg();
                    SetMailInfo(id);
                    SendRead(id);
                }
                return true;
            case "onsendmail":
                SendRequested?.Invoke();
                return true;
            case "onselecttab":
                page = 0;
                RefreshMailList();
                SetMailInfo(0);
                UpdatePageText();
                checkedMails.Clear();
                return false;
            case "onrecieveall":
                if (any) SendCollect(AtoiArg());
                return false;
            case "onreply":
                if (any && Book.Find(AtoiArg()) is { } reply) ReplyRequested?.Invoke(reply);
                return false;
            case "onremove":
                if (any) RemoveOne(AtoiArg());
                return false;
            case "onprevpage":
            case "onnextpage":
            {
                int to = name.Equals("onPrevPage", StringComparison.OrdinalIgnoreCase) ? page - 1 : page + 1;
                if (IsPageValid(to))
                {
                    page = to;
                    RefreshMailList();
                    UpdatePageText();
                    ClearChecks();
                }
                return false;
            }
            case "onreadlist":
                if (any) ShowIdsBox(Texts.Common(0x22d), Str(0), OnReadListAnswer);
                return false;
            case "ondeletelist":
                if (any)
                {
                    string ids = Str(0);
                    if (AnyAttached(ids))
                    {
                        var box = MessageBoxQueue.Create(0x19, Texts.Common(0x231), null, "");     // FUN_140d73e40
                        box.SetButtonPreset(1);
                        box.SetCheckbox0(Texts.Common(0x232), false);
                        box.Args.Add(ids);
                        box.Callback = OnDeleteListAnswer;
                        ShowBox(box);
                    }
                    else ShowIdsBox(Texts.Common(0x22e), ids, OnDeleteListAnswer);
                }
                return false;
            case "onreceivelist":
                if (any) ShowIdsBox(Texts.Common(0x22f), Str(0), OnReceiveListAnswer);
                return false;
            case "onclickmailcheckbox":
                if (any) checkedMails[U64Arg(0)] = args.Length > 1 && Rt.ToBoolean(args[1]);
                return false;
            case "onrclickmailsender":
                if (any && Book.Find(U64Arg(0)) is { Kind: 1 })
                {
                    float x = args.Length > 2 ? (float)Rt.ToNumber(args[2]) : 0;
                    float y = args.Length > 3 ? (float)Rt.ToNumber(args[3]) : 0;
                    SenderMenuRequested?.Invoke(Str(1), x, y);
                }
                return false;
            case "onbuyad":
                if (any && Book.Find(U64Arg(0)) is { } ad) BuyAdRequested?.Invoke(ad);
                return false;
        }
        return false;
    }

    /// <summary>CreateMessageBox(1, text, 0, "") with the ids as its string arg and the answer, then FUN_140d73c60.</summary>
    void ShowIdsBox(string text, string ids, Func<int, MessageBoxData, bool> answer)
    {
        var box = MessageBoxQueue.Create(1, text, null, "");
        box.Args.Add(ids);
        box.Callback = answer;
        ShowBox(box);
    }
}
