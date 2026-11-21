using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The mail send window: UIMailSendDialog (0x4e4a, movie uimailsenddialog) with its C++ side as
/// <see cref="MailSendDialogController"/>. The mail list's send and reply buttons open it; its send goes out as Mail 0x2D mode 1
/// through the live session; the book's "sent" (mode 1) closes it. STILL-OPEN: the send draft (attachments, meso, fee) and the
/// block check (CBlockManager 0xb) before it opens (see the controller).
/// </summary>
public partial class HudScene
{
    MailSendDialogController mailSend;      // from open until the close
    MovieDialog mailSendMovie;              // the movie, from open until the unload
    bool mailSendTyped;                     // a key reached the dialog: its receiver's candidates are rebuilt next frame

    bool MailSendOpen => mailSend != null && !mailSend.IsClosed;

    /// <summary>CUIService::ToggleDialog(0x4e4a): open when closed, else vfunc_67.</summary>
    void ToggleMailSend(MailInfo reply = null)
    {
        if (MailSendOpen) { if (reply != null) mailSend.SetReplyMail(reply); mailSend.Close(); return; }
        OpenMailSend(reply);
    }

    /// <summary>OpenDialog(0x4e4a): a window that is already up comes to the front (vfunc_65, uimaillistdialog.cpp:841).</summary>
    void OpenMailSend(MailInfo reply)
    {
        if (MailSendOpen) { mailSend.SetReplyMail(reply); mailSend.BringToFront(); return; }
        if (GuideRefusesOpen("MailSendDialog")) return;
        if (mailSendMovie != null) { mailSendMovie.Host.Unload(); mailSendMovie.QueueFree(); mailSendMovie = null; }
        var live = Ms2Godot.Net.LiveSession.Instance;
        var texts = MailTexts();
        var checker = CommunityChecker();
        var c = new MailSendDialogController
        {
            Send = p =>
            {
                if (live != null) live.SendMail(p);
                else GD.Print($"mail: offline, not sent: {p}");
            },
            CheckTitle = t => checker.CheckMailTitleText(t, out var msg) != 0 ? msg : null,
            CheckContent = t => checker.CheckContentText(t, out var msg) != 0 ? msg : null,
            DateText = t => Ms2.Ui.MailTexts.DateString(t, ClientLanguageIndex()),
            FormatId = (id, args) => texts.CommonF(id, args),   // ids 0x805/0x806 are in MailTexts.CommonKeys
            Names = () => MailReceiverNames(live),
        };
        c.NoticeText += text => SystemNoticeText(text);
        c.Unloaded += () => { mailSendMovie?.Host.Unload(); mailSendMovie?.QueueFree(); mailSendMovie = null; };
        c.SetReplyMail(reply);
        mailSend = c;
        mailSendMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"mail send: load={mailSendMovie.Loaded}");
        uiRoot.AddChild(mailSendMovie);
    }

    /// <summary>FUN_1419ed7f0: the buddies' names, then the guild members' but the player's own (keyed by id, a later entry wins).</summary>
    static IEnumerable<string> MailReceiverNames(Ms2Godot.Net.LiveSession live)
    {
        var names = new Dictionary<long, string>();
        if (live == null) return names.Values;
        foreach (var b in live.Buddies.Buddies) names[b.CharacterId] = b.Name;
        foreach (var m in live.Guilds.Members) if (m.CharId != live.CharacterId) names[m.CharId] = m.Name;
        return names.Values;
    }

    /// <summary>UIMailSendDialog::vfunc_13: Escape closes (UIDialog), Enter and Tab go to the movie; a typed character marks the
    /// receiver's candidates to be rebuilt (vfunc_74).</summary>
    bool MailSendKeys(InputEventKey k)
    {
        if (!MailSendOpen || !k.Pressed) return false;
        switch (k.Keycode)
        {
            case Key.Escape: mailSend.Close(); return true;
            case Key.Enter or Key.KpEnter: return mailSend.OnKeyCode(MailSendDialogController.EnterKey);
            case Key.Tab: return mailSend.OnKeyCode(MailSendDialogController.TabKey);
        }
        if (k.Unicode >= 0x20) mailSendTyped = true;   // a typed character (vfunc_74)
        return false;
    }

    /// <summary>The frame after a key: the candidates (only while the dialog is up and a key reached it).</summary>
    void MailSendTick()
    {
        if (!mailSendTyped) return;
        mailSendTyped = false;
        if (MailSendOpen) mailSend.UpdateCandidates();
    }

    /// <summary>The <c>--uigen-open mailsend</c> shot: the dialog answering a fixture mail.</summary>
    void OpenMailSendShot()
    {
        var m = new MailInfo { Kind = 1, Id = 1, Sender = "Mapler", Title = "Hello", Content = "See you in Tria.", SentTime = 1_700_000_000 };
        OpenMailSend(m);
    }
}
