using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The player's mails, CUserMail (g_143ab2ee0, commonfiles/usermail.cpp), with what CSceneStateGame::OnMail (@0x141721cd0,
/// scenestategame.cpp:11496) does on each Mail packet (0x54). The mail list dialog (UIMailListDialog) reads the book and
/// three flags the packets set while it is open (DAT_143af37b0, DAT_143af37b8, DAT_143af37b4); its frame clears them. The
/// client's UI calls come out as events. STILL-OPEN: the mail send dialog (0x4e4a) and the gift notify dialog (0x4e5e) are
/// not ported, so Sent and GiftReceived have no dialog to reach.
/// </summary>
public sealed class MailBook
{
    readonly List<MailInfo> mails = new();                      // m_mails, in add order
    readonly Dictionary<long, MailInfo> map = new();            // m_map

    public MailBook(MailTexts texts) => Texts = texts;

    /// <summary>The texts CMailInfo::Read resolves with; the HUD sets the loaded set (with its tables) once it is up.</summary>
    public MailTexts Texts { get; set; }

    /// <summary>m_00, GetUnreadCount: the game menu's mail count (FUN_141938ae0 0x4e49).</summary>
    public int Unread { get; private set; }
    /// <summary>m_04, GetUnreadAlarm.</summary>
    public int UnreadAlarm { get; private set; }
    /// <summary>m_d0: the character the list was read for (FUN_1408e4890 sets DAT_143ab2828).</summary>
    public long OwnerId { get; private set; }

    bool receiveSound;      // m_d8
    bool deleteSound;       // m_dc

    /// <summary>DAT_143af37b0: the list is to be drawn again (and the open mail shown again).</summary>
    public bool ListRefresh;
    /// <summary>DAT_143af37b8: the mail whose view is to be drawn again (0: none).</summary>
    public long ListInfoId;
    /// <summary>DAT_143af37b4: a mail was deleted.</summary>
    public bool ListRemoved;

    /// <summary>WorldService::HasTutorial. STILL-OPEN: no tutorial state in this HUD, so false.</summary>
    public Func<bool> HasTutorial = () => false;
    /// <summary>DAT_143ab2828: the local character's id.</summary>
    public Func<long> CharacterId = () => 0;

    /// <summary>FUN_141275260(id, type, bLog) and ShowSystemNotice: a system notice of a text, its type, and whether it also
    /// goes to the chat log (FUN_1412751b0 kind 0x14).</summary>
    public event Action<string, int, bool> Notice;
    /// <summary>FUN_1412751b0(text, 0x14): a chat log line alone.</summary>
    public event Action<string> ChatLine;
    /// <summary>FUN_140d73bc0(id, 0, 0, ""): a message box of a text.</summary>
    public event Action<string> Box;
    /// <summary>PlaySystemSound(key).</summary>
    public event Action<string> Sound;
    /// <summary>UIMailListDialog::OnReadMail on the open list.</summary>
    public event Action<long> ReadMail;
    /// <summary>UIMailListDialog::RequestRefresh on the open list.</summary>
    public event Action RefreshRequested;
    /// <summary>Mode 1 (the mail was sent): the draft reset and the send dialog's close (STILL-OPEN, no send dialog).</summary>
    public event Action Sent;
    /// <summary>Mode 0x16 with no tutorial, after the notice: the gift dialog (0x4e5e, STILL-OPEN).</summary>
    public event Action<MailMessage.Gift> GiftReceived;
    /// <summary>The game menu's FUN_141937e40(mode) at the end of OnMail: FUN_141938ae0(0x4e49, 0).</summary>
    public event Action MenuCount;

    /// <summary>GetMails: the array in add order.</summary>
    public IReadOnlyList<MailInfo> Mails => mails;

    /// <summary>FindMailById.</summary>
    public MailInfo Find(long id) => map.TryGetValue(id, out var m) ? m : null;

    /// <summary>GetMailCountByKind.</summary>
    public int CountByKind(int kind) => mails.Count(m => m.Kind == kind);

    /// <summary>CountUnreadMailsByKind.</summary>
    public int CountUnreadByKind(int kind) => mails.Count(m => m.Kind == kind && !m.IsRead);

    /// <summary>FUN_1408e3030 / ClearMails.</summary>
    public void Clear() { map.Clear(); mails.Clear(); }

    /// <summary>AddMailIfNew (@0x1408e3a60): a known id is left as it is.</summary>
    public void AddIfNew(MailInfo m)
    {
        if (m == null || map.ContainsKey(m.Id)) return;
        map[m.Id] = m;
        mails.Add(m);
    }

    /// <summary>
    /// RemoveMail (@0x1408e3b70): not found, or not forced while the mail holds something, leaves it (0); otherwise it goes,
    /// and with no tutorial the delete sound is due.
    /// </summary>
    public bool Remove(long id, bool force)
    {
        if (Find(id) is not { } m) return false;
        if (!force && m.HasAttachment()) return false;
        map.Remove(id);
        mails.Remove(m);
        if (!HasTutorial()) deleteSound = true;
        return true;
    }

    /// <summary>SetUnreadCount (@0x1408e3490): an unsigned count, so any count other than 0 with no tutorial makes the receive
    /// sound due; GetUnreadCount reads it back as int.</summary>
    public void SetUnreadCount(uint v)
    {
        Unread = (int)v;
        if (v > 0 && !HasTutorial()) receiveSound = true;
    }

    /// <summary>
    /// FUN_1408e48f0: the receive sound ("mail_recieve") waits for the field (WorldService::FUN_141bcfbd0: state 4 and
    /// m_2b4) and is not played on sub scenes 0x6200041 / 0x620007f (GFxSceneService::FUN_1422b9ea0); the delete sound
    /// ("mail_delete") plays at once. STILL-OPEN: which HUD states those two client tests are.
    /// </summary>
    public void Tick(bool fieldReady, bool subScene)
    {
        if (receiveSound)
        {
            if (!fieldReady) return;
            receiveSound = false;
            if (!subScene) Sound?.Invoke("mail_recieve");
        }
        if (deleteSound)
        {
            deleteSound = false;
            Sound?.Invoke("mail_delete");
        }
    }

    /// <summary>
    /// CSceneStateGame::OnMail: one Mail packet. listOpen is IsDialogClosed(0x4e49) == 0 (for mode 0xe, the list's
    /// FUN_141924630). A mode that finds no mail returns before the game menu's count (as the client does).
    /// </summary>
    public void Apply(MailMessage msg, bool listOpen, long now)
    {
        switch (msg)
        {
            case MailMessage.Clear:
                Clear();
                break;
            case MailMessage.Listed l:
                foreach (var m in l.Mails)
                {
                    Texts.Resolve(m);           // CMailInfo::Read's text part
                    AddIfNew(m);
                }
                SetUnreadCount(0);
                UnreadAlarm = 0;
                break;
            case MailMessage.EndList:
                if (listOpen) ListRefresh = true;
                OwnerId = CharacterId();
                break;
            case MailMessage.Sent:
                Box?.Invoke(Texts.Common(0x228));
                Sent?.Invoke();
                break;
            case MailMessage.MarkedRead r:
                if (Find(r.Id) is { } read) read.ReadTime = r.ReadTime;       // FUN_1408e3540 -> SetReadTime
                if (listOpen) ReadMail?.Invoke(r.Id);
                break;
            case MailMessage.Returned r:
                SystemNotice(0x229);
                Remove(r.Id, true);
                break;
            case MailMessage.Collected c:
            {
                if (Find(c.Id) is not { } m) return;
                if (c.Attachment) m.SetAttachmentReceiveTime(c.Slot, c.Time);
                else m.ClaimMeso(c.Time);
                if (listOpen) ListInfoId = c.Id;
                break;
            }
            case MailMessage.CollectedAll c:
            {
                if (Find(c.Id) is not { } m) return;
                m.ClaimMeso(c.Time);
                m.ClaimMeret(c.Time);
                m.ClaimRedMeret(c.Time);
                m.MarkBenefitsUsed(now);
                for (int i = 0; i < 5; i++) m.SetAttachmentReceiveTime(i, c.Time);
                if (listOpen) { ListInfoId = c.Id; ListRefresh = true; }
                break;
            }
            case MailMessage.AdBought b:
            {
                if (Find(b.Id) is not { } m) return;
                m.SetAdSold(b.Time);
                if (listOpen) { ListInfoId = b.Id; ListRefresh = true; }
                break;
            }
            case MailMessage.Deleted d:
                Remove(d.Id, true);
                if (listOpen) ListRemoved = true;
                break;
            case MailMessage.New n:
                SetUnreadCount((uint)n.Unread);
                UnreadAlarm = n.UnreadAlarm;
                if (n.Unread != 0 && !HasTutorial() && n.Alarm) Notice?.Invoke(Texts.Common(0x22b), 8, true);
                if (listOpen) RefreshRequested?.Invoke();
                break;
            case MailMessage.Full:
                if (!HasTutorial()) Notice?.Invoke(Texts.Common(0x257), 1, false);
                ChatLine?.Invoke(Texts.Common(0x258));
                break;
            case MailMessage.Gift g:
                if (!HasTutorial())
                {
                    Notice?.Invoke(Texts.CommonKeyF("s_mail_notify_recieve_gift", g.Sender), 8, true);
                    GiftReceived?.Invoke(g);
                }
                break;
            case MailMessage.Error e:
                OnError(e.Type, e.Code);
                break;
        }
        MenuCount?.Invoke();
    }

    /// <summary>FUN_141275260(id, 1, 1).</summary>
    void SystemNotice(int id) => Notice?.Invoke(Texts.Common(id), 1, true);

    /// <summary>Mode 0x14 (u8 type, u8 error): the error's notice; an unknown one gives Unk_SSGFormatMsg(0x23a, type, error).</summary>
    void OnError(int type, int code)
    {
        int id = code switch
        {
            1 => 0x23e, 0x1d => 0x24f, 0x1e => 0x250, 2 or 5 => 0x240, 3 or 4 => 0x23f, 0xc => 0x244, 0x10 => 0x249,
            0x11 => 0x241, 0x18 => 0x243, 0x19 => 0x242, 0x14 or 0x16 => 0x24a, 0x15 => 0x24b, 0x17 => 0x24c, 0x1a => 0x34,
            0x1b => 0x24d, 0x1c => 0x24e, 0x1f => 0x251, 0x20 => 0x252, 0x21 => 0xc07,
            _ => 0,
        };
        if (id == 0)
        {
            Notice?.Invoke(Texts.CommonF(0x23a, type, code), 1, true);
            return;
        }
        SystemNotice(id);
        if (id == 0x24a) Sound?.Invoke("system_notice_error_warningBag");     // DAT_143ae7978
    }
}
