using Ms2.Quest;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui.Hud;

/// <summary>
/// The UI half of the party votes (PartyVoteCli, client/ms2/partymanager.cpp): what a vote's start (vfunc_2) and end
/// (vfunc_3) do outside the party book. The kick vote (PartyVoteKickUserCli) posts a chat line and redraws the window's vote
/// page; the ready check (PartyVoteReadyCli) clears the window's deadline, posts a chat line, plays its sound and, when the
/// own character has not answered, opens the timed answer box; its end shows a notice and keeps the ready marks for
/// <see cref="GameTables.PartyVoteReadyTagExpireSeconds"/>.
/// </summary>
public sealed class PartyVoteUi
{
    public GameTables Tables;
    /// <summary>DAT_143ab2828: the own character id.</summary>
    public long SelfCharacterId;
    /// <summary>The party window while loaded (FindDialog(0x4e45)); null closed.</summary>
    public Func<PartyController> Dialog = () => null;
    /// <summary>FUN_1412751b0(text, kind): a chat line.</summary>
    public Action<string, int> ChatLine = (_, _) => { };
    /// <summary>ShowSystemNotice(text, 1, 1).</summary>
    public Action<string> Notice = _ => { };
    /// <summary>PlaySystemSound(key).</summary>
    public Action<string> PlaySound = _ => { };
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>FUN_140710590: the party send.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>GetTickCountMs2.</summary>
    public Func<long> NowMs = () => Environment.TickCount64;

    string Key(string key) => Tables?.Common(key) ?? "";

    /// <summary>The book's VoteStarted: vfunc_2 of the vote's kind (the book has already marked the ready states).</summary>
    public void OnStarted(PartyBook book, PartyVote vote)
    {
        var dialog = Dialog();
        if (vote.Kind == PartyVote.KickUser)
        {
            // PartyVoteKickUserCli::vfunc_2: s_party_vote_kick_user_notify of the vote owner's name (FindMember; "" if gone).
            string owner = book.Find(vote.Owner)?.User.Name ?? "";
            ChatLine(ClientFormat.Format(Key("s_party_vote_kick_user_notify"), owner), 2);
            if (dialog != null) { dialog.RefreshVote(); dialog.ArrangeBottomButton(); }
        }
        else if (vote.Kind == PartyVote.Ready)
        {
            // PartyVoteReadyCli::vfunc_2.
            dialog?.SetDeadline(0);
            string text = Key("s_party_vote_ready_message");
            ChatLine(text, 2);
            PlaySound("System_PartyReady_Popup_01");
            if (vote.HasVoted(SelfCharacterId)) return;
            var box = MessageBoxQueue.CreateWithCallback(text, 1, (n, b) => OnMessageBoxResult(vote, n, b));
            box.SetTimer(0x14, 1, MessageBoxData.Common(0x2b8));
            box.NoInputBlock = true;
            box.Modaless = true;
            box.EnterAllowed = false;
            ShowBox(box);
        }
    }

    /// <summary>The book's VoteEnded: vfunc_3 (the book has already marked the unanswered members, FUN_14162c3a0).
    /// Only the ready check does anything here: the kick vote's end is the window's 0x31 handler.</summary>
    public void OnEnded(PartyBook book, PartyVote vote)
    {
        if (vote.Kind != PartyVote.Ready) return;
        Notice(Key("s_party_vote_ready_message_end"));
        Dialog()?.SetDeadline(NowMs() + (Tables?.PartyVoteReadyTagExpireSeconds ?? 10) * 1000L);
    }

    /// <summary>PartyVoteReadyCli::OnMessageBoxResult @0x140a8a0b0: the first button answers yes; another button answers
    /// no, unless the box's timer pressed it (OnPopupTimerComplete sets Timer.Button): then nothing is sent.</summary>
    bool OnMessageBoxResult(PartyVote vote, int n, MessageBoxData box)
    {
        if (n == 0) Send(new PartyVoteAnswer(vote.Id, true));
        else if (box.Timer.Button == 0) Send(new PartyVoteAnswer(vote.Id, false));
        return true;
    }
}
