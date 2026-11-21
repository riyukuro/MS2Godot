using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui.Hud;

/// <summary>
/// The mail send window, UIMailSendDialog (client/ms2/uimaillistdialog.cpp:1353-1679, UI id 0x4e4a, type name "MailSendDialog",
/// movie uimailsenddialog): the receiver (an auto-complete input over the buddies and the guild members), the title, the
/// content, and the send. OnSend refuses an empty title (notice 0x247), an empty content (0x248), a title over 100 or a content
/// over 2000 characters (0x244) and a title or content the ban checker refuses (its message), else it sends
/// <see cref="MailSendRequest"/>. The reply data (SetReplyMail, SetReplyData) fills the three fields.
/// <para>STILL-OPEN: the send draft of CUserMail (the attachments, the meso and the fee: ResetSendDraft, UpdateSendFee) and the
/// block check (CBlockManager 0xb) before the dialog opens are not ported; CBanTextChecker::FilterReservedWords (the URL
/// ban words) is a delegate with no default; FUN_1419ed010 (the m_buddys menu) has no child in this movie.</para>
/// </summary>
public sealed class MailSendDialogController : UiController
{
    public override string Movie => "uimailsenddialog";
    protected override string EntityMethod => "selectRecvName";
    public override string TypeName => "MailSendDialog";

    public const int UiId = 0x4e4a;
    /// <summary>The notices: an empty title, an empty content, the limits (s_mail_error_sendmail), the content limit input.</summary>
    public const int EmptyTitleNotice = 0x247, EmptyContentNotice = 0x248, LimitNotice = 0x244, LimitInputNotice = 0x256;
    /// <summary>OnSend's limits.</summary>
    public const int TitleMax = MailSendRequest.TitleMax, ContentMax = MailSendRequest.ContentMax;
    /// <summary>The key codes vfunc_13 forwards: Enter 0x1c, Tab 0xf.</summary>
    public const int EnterKey = 0x1c, TabKey = 0xf;

    /// <summary>FUN_140710590: the send.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>ShowSystemNotice / FUN_141275260: the notice text.</summary>
    public event Action<string> NoticeText;
    /// <summary>CheckMailTitleText: the refusal message of a title, null when it passes.</summary>
    public Func<string, string> CheckTitle = _ => null;
    /// <summary>CheckContentText: the refusal message of a content, null when it passes.</summary>
    public Func<string, string> CheckContent = _ => null;
    /// <summary>CBanTextChecker::FilterReservedWords (STILL-OPEN: not ported): the text as the send reads it.</summary>
    public Func<string, string> FilterText = s => s;
    /// <summary>FUN_1419ed7f0: the names the receiver completes (the buddies, then the guild members but the player).</summary>
    public Func<IEnumerable<string>> Names = () => Array.Empty<string>();
    /// <summary>The dialog closed.</summary>
    public event Action Closed;

    MailInfo reply;
    /// <summary>A reply mail is held (kept until the dialog closes).</summary>
    public bool HasReply => reply != null;
    bool closing;


    /// <summary>The mail being answered (SetReplyMail): null is ignored, the dialog keeps the one it has.</summary>
    public void SetReplyMail(MailInfo mail)
    {
        if (mail == null) return;
        reply = mail;
    }

    /// <summary>The reply's date text (GetDateString).</summary>
    public Func<long, string> DateText = t => t.ToString();
    /// <summary>FormatStringId: StringFormat(GetStringCommon(id), args) for the reply ids 0x805 and 0x806.</summary>
    public Func<int, object[], string> FormatId = (_, _) => "";

    /// <summary>OnLoadComplete: UIDialog's, then SetReplyData. (ResetSendDraft and UpdateSendFee: STILL-OPEN.)</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        closing = false;
        SetReplyData();
    }

    /// <summary>SetReplyData (@0x1419ecbf0): setData with receiver, title and content of the reply mail; an empty object without one.</summary>
    public void SetReplyData()
    {
        if (Host?.Entity == null) return;
        if (reply is not { } m) { Host.Invoke("setData", Host.Obj()); return; }
        Host.Invoke("setData", Host.Obj(
            ("receiver", m.Sender),
            ("title", FormatId(0x805, new object[] { m.Title })),
            ("content", FormatId(0x806, new object[] { m.Sender, DateText(m.SentTime), m.Title, m.Content }))));
    }

    /// <summary>vfunc_65 on a dialog that is already up: the movie's setTopMostLayer.</summary>
    public void BringToFront() => Host.Invoke("setTopMostLayer");

    /// <summary>vfunc_67: the dialog closes and the reply mail is dropped.</summary>
    public override void Close()
    {
        if (closing) return;
        closing = true;
        reply = null;
        base.Close();
        Closed?.Invoke();
    }

    /// <summary>vfunc_13: Enter forwards onEnterKey and Tab onTabKey; true when handled.</summary>
    public bool OnKeyCode(int code)
    {
        if (Host?.Entity == null) return false;
        switch (code)
        {
            case EnterKey: Host.Invoke("onEnterKey"); return true;
            case TabKey: Host.Invoke("onTabKey"); return true;
        }
        return false;
    }

    /// <summary>vfunc_79: Mail (0x54) mode 0x14 with the error code 1 (no such receiver) or 0x19 (to oneself): the receiver is selected.</summary>
    public void OnMailError(int code)
    {
        if (code is 1 or 0x19 && Host?.Entity != null) Host.Invoke("selectRecvName");
    }

    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onsend": OnSend(); return true;
            case "onlimitinput": Notice(LimitInputNotice); return true;
        }
        return false;
    }

    void Notice(int id)
    {
        string t = Model?.Tables.Common(id);
        if (!string.IsNullOrEmpty(t)) NoticeText?.Invoke(t);
    }

    string ReceiverText() => Host.Child("m_receiver") is { } r ? Convert.ToString(Host.Get(r, "text")) ?? "" : "";

    string TitleText()
    {
        string s = "";
        if (Host.Child("m_title") is { } t && Host.Get(t, "textField") is { } f) s = Convert.ToString(Host.Get(f, "text")) ?? "";
        return FilterText(s);
    }

    string ContentText() => FilterText(Convert.ToString(Host.Invoke("getContent")) ?? "");

    /// <summary>OnSend (@0x1419ed1c0, uimaillistdialog.cpp:1583): the checks in the client's order, then the send. Returns the packet sent (null when refused).</summary>
    public MailSendRequest OnSend()
    {
        if (Host?.Entity == null) return null;
        string receiver = ReceiverText(), title = TitleText(), content = ContentText();
        if (title.Length == 0) { Notice(EmptyTitleNotice); return null; }
        if (content.Length == 0) { Notice(EmptyContentNotice); return null; }
        if (title.Length > TitleMax || content.Length > ContentMax) { Notice(LimitNotice); return null; }
        string refused = CheckTitle(title) ?? CheckContent(content);
        if (refused != null) { NoticeText?.Invoke(refused); return null; }
        var p = new MailSendRequest(receiver, title, content);
        Send(p);
        return p;
    }

    bool ReceiverFocused(object input) => Rt.ToNumber(Host.Get(input, "focused")) > 0;

    /// <summary>vfunc_74 (a character typed, uimaillistdialog.cpp:1480): with the receiver focused, the names that contain its lower-cased text (trailing blanks trimmed) are the
    /// candidates ({label: name} in an ASDataProvider); an empty text gives an empty list.</summary>
    public void UpdateCandidates()
    {
        if (Host?.Entity == null || Host.Child("m_receiver") is not { } input) return;
        if (!ReceiverFocused(input)) return;
        string typed = ReceiverText().TrimEnd().ToLowerInvariant();
        var dp = Host.RT.Construct(Host.RT.GetClass("scaleform.clik.data.DataProvider"), Array.Empty<object>());
        if (typed.Length > 0)
            foreach (var n in Names())
                if (n.ToLowerInvariant().Contains(typed, StringComparison.Ordinal)) Host.Invoke(dp, "push", Host.Obj(("label", n)));
        Host.Invoke(input, "setCandidates", dp);
    }
}
