using Ms2.Avm2;
using Ms2.Formats.Text;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using ReservedWords = Ms2.Formats.World.ReservedWords;

namespace Ms2.Ui.Hud;

/// <summary>
/// The C++ side of the friend query, UICommunityQueryDialog (client/ms2/uibuddyeventlistener.cpp:1596; UI id 0x4e4e), for its
/// movie uicommunityquerydialog: the name and memo input of the friend dialog's requests. Modes (m_180): 1 ask a friend,
/// 2 block, 3 home block, 4 edit a block memo, 5 edit a home block memo; the popup's 6 remove a friend, 7 unblock, 8 remove a
/// home block ask first in a message box. The sends (SendOp::Buddy 0x30) go through <see cref="Send"/>.
/// STILL-OPEN: the home blocks (HomeBlockManager FUN_140a46f00, FUN_140a47c80, RequestRemove) come out as events; the
/// HomeBlock packet (0x138) closing the dialog on its results 2, 3 and 5 is <see cref="OnHomeBlockResult"/>; vfunc_8(1) at the
/// end of InitData is left out.
/// </summary>
public sealed class CommunityQueryController : ModalDialogController, IMessageBoxListener
{
    public const int UiId = 0x4e4e;
    public override int TypeId => UiId;

    public override string Movie => "uicommunityquerydialog";
    protected override string EntityMethod => "initData";
    /// <summary>GetTypeName (@0x1418da870).</summary>
    public override string TypeName => "CommunityQueryDialog";

    public BuddyBook Book { get; }

    int mode;                // m_180
    string name = "";        // m_188
    long id;                 // m_190: the entry id
    long homeId;             // m_198
    string memo = "";        // m_1a0

    /// <summary>GetStringCommon.</summary>
    public Func<int, string> Common = _ => "";
    /// <summary>CommonString GetChecker; null skips the checks.</summary>
    public BanTextChecker Checker;
    /// <summary>The local player's name (CheckName's self test, CompareNoCase); null when there is none.</summary>
    public Func<string> SelfName = () => null;
    /// <summary>FUN_140710590: the send.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>ShowMessageBox.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>FUN_14151f200: the next message box.</summary>
    public Action NextBox = MessageBoxQueue.Next;

    /// <summary>FUN_141275260(id, 1, 1) and Unk_AkoMessage(id, 1, 1, name): a system notice (also to the chat log).</summary>
    public event Action<string> SystemNotice;
    /// <summary>PlaySystemSound(key, "").</summary>
    public event Action<string> Sound;
    /// <summary>CUIService::OpenDialog(0x4e4e): the dialog opens for a query (OnQuery while closed).</summary>
    public event Action OpenRequested;
    /// <summary>HomeBlockManager FUN_140a46f00(home id, memo): a home block (the homeBlock box). STILL-OPEN.</summary>
    public event Action<long, string> HomeBlockRequested;
    /// <summary>HomeBlockManager FUN_140a47c80(id, memo): a home block's memo (mode 5). STILL-OPEN.</summary>
    public event Action<long, string> HomeBlockMemoEdited;
    /// <summary>HomeBlockManager RequestRemove(id) (the delHomeBlock box). STILL-OPEN.</summary>
    public event Action<long> HomeBlockRemoveRequested;

    public CommunityQueryController(BuddyBook book) => Book = book;

    public int Mode => mode;

    bool NoMovie => Host?.Entity == null;

    /// <summary>OnLoadComplete (@0x1418da6b0): UIModalDialog's, then InitData.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        InitData();
    }

    /// <summary>vfunc_67 (@0x1418da680): UIModalDialog's close, then ResetQuery.</summary>
    public override void Close()
    {
        base.Close();
        ResetQuery();
    }

    /// <summary>ResetQuery (@0x1418db6d0).</summary>
    void ResetQuery()
    {
        name = "";
        memo = "";
        id = 0;
        homeId = 0;
        mode = 0;
    }

    /// <summary>InitData (@0x1418db750): initData(mode, name, the text: 0x60a for a request, the block memo of the entry
    /// for mode 4; mode 5's home block memo is STILL-OPEN, "").</summary>
    void InitData()
    {
        if (NoMovie) return;
        string text = "";
        if (mode == 1) text = Common(0x60a);
        else if (mode == 4) { if (id > 0 && Book.FindById(id) is { } b) text = b.Memo; }
        Host.Invoke(Host.Entity, "initData", mode, name, text);
    }

    /// <summary>
    /// OnMessage (@0x1418daa70) from the popup, the community dialog (FUN_1418d6ed0) or the mentoring invite: the result, the
    /// mode, the name, the entry id and the home id. Result 0 with modes 1..8 opens the dialog when it is closed; result 1
    /// with modes 6, 7, 8 asks first in a box (0x5f8 delBuddy, 0x5f9 delBanBuddy, 0x5fa delHomeBlock).
    /// </summary>
    public bool OnQuery(int result, int queryMode, string queryName = "", string entryId = "", string queryHomeId = "")
    {
        mode = queryMode;
        name = queryName ?? "";
        id = unchecked((long)ReservedWords.ParseUInt64(entryId ?? ""));
        homeId = unchecked((long)ReservedWords.ParseUInt64(queryHomeId ?? ""));
        if (result == 0)
        {
            if (mode == 0 || mode >= 9) return true;
            if (IsClosed)
            {
                OpenRequested?.Invoke();
                return true;
            }
        }
        else if (result == 1)
        {
            if (mode == 6) Box(0x5f8, 1, "delBuddy");
            else if (mode == 7) Box(0x5f9, 1, "delBanBuddy");
            else if (mode == 8) Box(0x5fa, 1, "delHomeBlock");
        }
        return false;
    }

    void Box(int textId, int type, string tag) =>
        ShowBox(MessageBoxQueue.Create(type, ClientFormat.Format(Common(textId), name), this, tag));

    void ShowText(string text) => ShowBox(MessageBoxQueue.Create(0, text, null, ""));

    /// <summary>OnMessage from the message box: yes (0) runs the tag's request, any other button the movie's errorName;
    /// then FUN_14151f200. Not handled: it returns 0 (uibuddyeventlistener.cpp:1796).</summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        if (button == 0)
        {
            switch (tag ?? "")
            {
                case "delBuddy": Send(BuddyRequest.Remove(id)); break;
                case "delBanBuddy": Send(BuddyRequest.Unblock(id)); break;
                case "banBuddy":
                    if (Checker == null) break;                 // the client checks and sends only inside if (pChecker)
                    if (MemoCheck(out string banMessage))
                    {
                        memo = memo.Trim();                     // CStringW::Trim trims m_1a0 itself
                        Send(BuddyRequest.Block(id, name, memo));
                    }
                    else ShowText(banMessage);
                    break;
                case "homeBlock":
                    if (Checker == null) break;
                    if (MemoCheck(out string homeMessage))
                    {
                        memo = memo.Trim();
                        HomeBlockRequested?.Invoke(homeId, memo);
                    }
                    else ShowText(homeMessage);
                    break;
                case "delHomeBlock": HomeBlockRemoveRequested?.Invoke(id); break;
            }
        }
        else if (!NoMovie) Host.Invoke(Host.Entity, "errorName");
        NextBox();
        return false;
    }

    /// <summary>vfunc_28 (@0x1418da8c0): OnOk(mode, name, memo) runs the mode (1 handled); OnCancel closes (answers 0).</summary>
    public override bool OnCallback(string callback, object[] args)
    {
        if (string.Equals(callback, "OnOk", StringComparison.OrdinalIgnoreCase))
        {
            if (args is not { Length: > 0 }) return false;
            int m = Rt.ToInt32(args[0]);
            name = args.Length > 1 ? Rt.ToString(args[1]) ?? "" : "";
            memo = args.Length > 2 ? Rt.ToString(args[2]) ?? "" : "";
            RunMode(m);
            return true;
        }
        if (string.Equals(callback, "OnCancel", StringComparison.OrdinalIgnoreCase)) Close();
        return false;
    }

    /// <summary>vfunc_13 (@0x1418db5b0): Enter (DIK 0x1c) runs onEnterKey, Tab (0xf) onTabKey. True = the key is taken.</summary>
    public bool OnKey(bool enter, bool tabKey)
    {
        if (IsClosed || NoMovie) return false;
        if (enter) { Host.Invoke(Host.Entity, "onEnterKey"); return true; }
        if (tabKey) { Host.Invoke(Host.Entity, "onTabKey"); return true; }
        return false;
    }

    /// <summary>vfunc_79 (@0x1418da6e0): a Buddy packet of protocol 2, 5, 10 or 12 is the answer to the query.</summary>
    public void OnPacket(BuddyMessage m)
    {
        if (m.Protocol is 2 or 5 or 10 or 12) OnBuddyResponse(m);
    }

    /// <summary>vfunc_79 on a HomeBlock packet (0x138): its first int 2, 3 or 5 closes the dialog.</summary>
    public void OnHomeBlockResult(int result)
    {
        if (result is 2 or 3 or 5) Close();
    }

    /// <summary>OnBuddyResponse (@0x1418dba00): 0 and 10 close; any other result shows its notice (9 closes first) and runs
    /// errorName. Then ResetQuery.</summary>
    void OnBuddyResponse(BuddyMessage m)
    {
        if (m.Result is 0 or 10) Close();
        else
        {
            if (m.Result == 9) Close();
            switch (m.Result)
            {
                case 1: Notice(0x614); break;
                case 2: Notice(0x622, m.Name); break;
                case 3: Notice(0x621, m.Name); break;
                case 4: Notice(0x617, m.Name); break;
                case 5: Notice(0x61c, m.Name); break;
                case 7: Notice(0x61d, m.Name); break;
                case 8: Notice(0x61e, m.Name); break;
                case 6: Notice(0x61b, m.Name); break;
                case 9: case 0xb: Notice(0x602, m.Name); break;
                default: Notice(0x613); break;
            }
            if (!NoMovie) Host.Invoke(Host.Entity, "errorName");
        }
        ResetQuery();
    }

    void Notice(int textId) => SystemNotice?.Invoke(Common(textId));
    void Notice(int textId, string who) => SystemNotice?.Invoke(ClientFormat.Format(Common(textId), who ?? ""));

    /// <summary>FUN_1418dbcb0: OnOk's mode. The memo is trimmed (m_1a0.Trim(), in place) before each check.</summary>
    void RunMode(int m)
    {
        switch (m)
        {
            case 1:
            {
                if (RejectRequestName(name)) return;
                memo = memo.Trim();
                // uibuddyeventlistener.cpp:1925: the check and the send are inside if (pChecker), so no checker sends nothing
                if (Checker == null) break;
                if (Checker.CheckInviteMessage(memo, out string message) == 0)
                {
                    Send(BuddyRequest.Invite(name, memo));
                    Sound?.Invoke("PC_InviteSuccess");         // s_strPcInviteSuccess
                }
                else ShowText(message);
                break;
            }
            case 2:
                if (RejectBlockName(name)) return;
                memo = memo.Trim();
                if (MemoPasses(memo)) Box(0x5fb, 1, "banBuddy");
                break;
            case 3:
                if (RejectName(name)) return;
                memo = memo.Trim();
                if (MemoPasses(memo)) Box(0x5fc, 1, "homeBlock");
                break;
            case 4:
                if (RejectName(name)) return;
                memo = memo.Trim();
                if (MemoPasses(memo)) Send(BuddyRequest.EditMemo(id, name, memo));
                break;
            case 5:
                if (RejectName(name)) return;
                memo = memo.Trim();
                if (MemoPasses(memo)) HomeBlockMemoEdited?.Invoke(id, memo);
                break;
            default:
                Close();
                break;
        }
    }

    /// <summary>CheckBuddyMemo of m_1a0 as it is (the box tags check before the trim); the callers test the checker first.</summary>
    bool MemoCheck(out string message) => Checker.CheckBuddyMemo(memo, out message) == 0;

    /// <summary>FUN_1418d3050: the block memo's check; a failed one shows its box (tag communityBanMemoFail, no listener).</summary>
    bool MemoPasses(string text)
    {
        if (Checker != null && Checker.CheckBuddyMemo(text, out string message) != 0)
        {
            ShowBox(MessageBoxQueue.Create(0, message, null, CommunityController.BanMemoFailTag));
            return false;
        }
        return true;
    }

    /// <summary>FUN_1418dc410: an empty name (0x615) or CheckName(name, true) failing (10 the own name: 0x616, else 0x614)
    /// is rejected with its notice and errorName. Note the empty test reads m_188, not the argument (the same string here).</summary>
    bool RejectName(string text)
    {
        bool rejected = false;
        if (name.Length == 0)
        {
            Notice(0x615);
            rejected = true;
        }
        if (!rejected)
        {
            int r = CheckName(text);
            if (r != 0)
            {
                Notice(r == 10 ? 0x616 : 0x614);
                rejected = true;
            }
        }
        if (rejected && !NoMovie) Host.Invoke(Host.Entity, "errorName");
        return rejected;
    }

    /// <summary>CheckName (@0x141276c80) with the self test: 10 for the own name (CompareNoCase), else the character name check
    /// FUN_140225210(name, out, 0), the long form.</summary>
    int CheckName(string text)
    {
        if (SelfName() is { } self && string.Compare(text, self, StringComparison.OrdinalIgnoreCase) == 0) return 10;
        return Checker?.CheckCharacterName(text, out _, false) ?? 0;
    }

    /// <summary>FUN_1418dc540: the name check, then a known entry of the name is rejected by its state (0x619 blocked,
    /// 0x622 our request waits, 0x61a their request, 0x621 a friend).</summary>
    bool RejectRequestName(string text)
    {
        if (RejectName(text)) return true;
        bool rejected = false;
        if (Book.FindByName(text) is { } b)
        {
            int textId = b.Blocked ? 0x619 : b.Waiting ? 0x622 : b.Received ? 0x61a : 0x621;
            Notice(textId, b.Name);
            rejected = true;
        }
        if (rejected && !NoMovie) Host.Invoke(Host.Entity, "errorName");
        return rejected;
    }

    /// <summary>FUN_1418dc7f0: the name check, then an entry of the name already blocked is rejected (0x620).</summary>
    bool RejectBlockName(string text)
    {
        if (RejectName(text)) return true;
        bool rejected = false;
        if (Book.FindByName(text) is { Blocked: true } b)
        {
            Notice(0x620, b.Name);
            rejected = true;
        }
        if (rejected && !NoMovie) Host.Invoke(Host.Entity, "errorName");
        return rejected;
    }
}
