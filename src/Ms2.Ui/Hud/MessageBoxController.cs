namespace Ms2.Ui.Hud;

/// <summary>
/// The message box queue (client/ms2/messageboxqueue.cpp): boxes waiting for the message box layer (UI id 0x4e48). A box
/// that a new one replaces goes to the queue's tail, and closing a box shows the newest queued box again.
/// </summary>
public static class MessageBoxQueue
{
    static readonly object Lock = new();
    static readonly LinkedList<MessageBoxData> Queue = new();

    /// <summary>GetMessageBoxLayer @0x14151f890: FindMessageBox(0x4e48), the loaded layer (null without one).</summary>
    public static MessageBoxController Layer { get; set; }

    /// <summary>FUN_14151f150: show <paramref name="data"/> now; the box it replaces goes back to the queue.</summary>
    public static void Show(MessageBoxData data)
    {
        lock (Lock)
        {
            var cur = Current();
            if (ShowNow(data) != 0 && cur != null) Queue.AddLast(cur);
        }
    }

    /// <summary>FUN_14151f200: show the newest queued box that is still live; close the layer when none is left.</summary>
    public static void Next()
    {
        lock (Lock)
        {
            if (Queue.Count == 0)
            {
                Layer?.CloseBox();
                return;
            }
            var data = RemoveTail();
            while (data != null)
            {
                if (data.IsLive)
                {
                    if (ShowNow(data) != 0) return;
                }
                else if (data.HasTimer)
                {
                    data.InvokeCallback(1, data.MovieBox);     // FUN_141a04a30
                }
                data = Queue.Count == 0 ? null : RemoveTail();
            }
            Layer?.CloseBox();
        }
    }

    static MessageBoxData RemoveTail()
    {
        var d = Queue.Last.Value;
        Queue.RemoveLast();
        return d;
    }

    /// <summary>FUN_14151f460: drop the queue and close every box with <paramref name="result"/>.</summary>
    public static void Clear(int result)
    {
        lock (Lock)
        {
            Queue.Clear();
            if (Layer is { } l && !l.IsIdle) l.CloseAll(result);
        }
    }

    /// <summary>FUN_14151f6e0: no box is shown (true without the layer).</summary>
    public static bool IsIdle => Layer?.IsIdle ?? true;

    /// <summary>RemoveFromQueue @0x14151f720.</summary>
    public static void Remove(MessageBoxData data)
    {
        lock (Lock) Queue.Remove(data);
    }

    /// <summary>FUN_14151f820: the box shown now.</summary>
    public static MessageBoxData Current() => Layer?.CurrentBox;

    /// <summary>FUN_14151f9c0: every queued box is modaless.</summary>
    public static bool AllModaless()
    {
        lock (Lock) return Queue.All(d => d == null || d.Modaless);
    }

    /// <summary>FUN_14151fad0: ShowMessage on the layer (0 without it).</summary>
    static int ShowNow(MessageBoxData data) => Layer?.ShowMessage(data) ?? 0;

    public static int Count
    {
        get { lock (Lock) return Queue.Count; }
    }

    // ---- the factory (unit_140d73aa0_messageboxfactory.cpp) ----------------------------------------------------------

    /// <summary>CreateMessageBox: a box of <paramref name="type"/> with the text, listener and tag, not shown yet.</summary>
    public static MessageBoxData Create(int type, string text, IMessageBoxListener listener, string tag)
    {
        var d = new MessageBoxData(type) { Listener = listener, Extra = tag ?? "", Msg = text ?? "" };
        return d;
    }

    /// <summary>ShowMessageBoxWithCallback @0x140d74390: a box answered by <paramref name="callback"/>, shown now.</summary>
    public static MessageBoxData ShowWithCallback(string text, int type, Func<int, MessageBoxData, bool> callback)
    {
        var d = CreateWithCallback(text, type, callback);
        Show(d);
        return d;
    }

    /// <summary>CreateMessageBoxWithCallback @0x140d74500: the same, not shown yet.</summary>
    public static MessageBoxData CreateWithCallback(string text, int type, Func<int, MessageBoxData, bool> callback) =>
        new MessageBoxData(type) { Msg = text ?? "", Callback = callback };

    /// <summary>ShowMessageBox(text, type, listener, tag): CreateMessageBox, then FUN_14151f150.</summary>
    public static MessageBoxData ShowMessageBox(string text, int type, IMessageBoxListener listener, string tag)
    {
        var d = Create(type, text, listener, tag);
        Show(d);
        return d;
    }

    /// <summary>FUN_140d73bc0: <see cref="ShowMessageBox"/> of common string <paramref name="id"/> (GetStringCommon).</summary>
    public static MessageBoxData ShowCommon(int id, int type, IMessageBoxListener listener, string tag) =>
        ShowMessageBox(MessageBoxData.Common(id), type, listener, tag);

    // ---- the terms boxes (unit_140d74b20_termsagreementmessagebox.cpp) -----------------------------------------------

    /// <summary>g_143ac2fb8 (Unk_BitSet11): the terms the player agreed to, by bit, for this run (memory only).</summary>
    static readonly HashSet<int> agreedTerms = new();

    /// <summary>g_1439f5a0c: the bit of the terms box shown last.</summary>
    static int termsBit;

    /// <summary>
    /// FUN_140d74640 -> ShowTermsMessageBox @0x140d746e0: a terms box (type 0x1b, "MessageBoxTerms") with the text of
    /// common string <paramref name="textId"/> and the tag "terms": yes / no, the title of common string
    /// <paramref name="titleId"/>, the "I agree" check box (0x9d0, checked), and the bit's "already agreed"; shown now.
    /// </summary>
    public static MessageBoxData ShowTerms(IMessageBoxListener listener, int bit, int titleId, int textId)
    {
        var box = Create(0x1b, MessageBoxData.Common(textId), listener, "terms");
        box.SetButtonPreset(1);
        box.Title = MessageBoxData.Common(titleId);
        box.SetCheckbox0(MessageBoxData.Common(0x9d0), true);
        termsBit = bit;
        box.SetAgreement(agreedTerms.Contains(bit));
        Show(box);
        return box;
    }

    /// <summary>
    /// ShowTermsMessageBoxWithCallback @0x140d748b0: the same terms box with the given title and text, answered by
    /// <paramref name="callback"/> instead of a listener; shown now (FUN_140d73c60).
    /// </summary>
    public static MessageBoxData ShowTermsWithCallback(int bit, string title, string text,
        Func<int, MessageBoxData, bool> callback)
    {
        termsBit = bit;
        var box = new MessageBoxData(0x1b) { Msg = text ?? "", Extra = "terms" };
        box.SetButtonPreset(1);
        box.Title = title ?? "";
        box.SetCheckbox0(MessageBoxData.Common(0x9d0), true);
        box.SetAgreement(agreedTerms.Contains(bit));
        box.Callback = callback;
        Show(box);
        return box;
    }

    /// <summary>FUN_140d74e50: mark terms bit <paramref name="bit"/> agreed.</summary>
    public static void Agree(int bit) => agreedTerms.Add(bit);

    /// <summary>Whether terms bit <paramref name="bit"/> is agreed (the bit set's FUN_140d750f0).</summary>
    public static bool IsAgreed(int bit) => agreedTerms.Contains(bit);

    /// <summary>
    /// FUN_140d74e80: the answer of a terms box from its click tag (MessageBoxTerms.getExtraData: the tag, then
    /// ",checkbox0:" and the check box's state). A tag that starts with "terms" and has a second part marks the last
    /// box's bit agreed (FUN_140d74e50; not bit 8) whatever was checked; the answer is whether that part says "true".
    /// </summary>
    public static bool TermsAgreed(string tag)
    {
        if (tag == null || !tag.StartsWith("terms", StringComparison.Ordinal)) return false;
        var parts = tag.Split(',');
        if (parts.Length < 2) return false;
        if (termsBit != 8) agreedTerms.Add(termsBit);
        return parts[1].Contains("true", StringComparison.Ordinal);
    }
}

/// <summary>
/// The C++ side of the message box layer, UIMessageBox (client/ms2/uimessagebox.cpp; UI id 0x4e48), for its movie
/// uimessagebox. The boxes' layout, buttons and keys are the movie's (MessageBoxFactory and the msgbox_* classes); this
/// opens and closes them with the box record's members and answers their OnClick. Of the tag handlers "gameexit",
/// "useBindFuncItem", "useBindEquipItem", "useBindGemEquipItem" and "learn_emotion" are ported; the others are logged.
/// </summary>
public sealed class MessageBoxController : UiController
{
    public override string Movie => "uimessagebox";
    protected override string EntityMethod => "closeCurrent";
    protected override bool IsDialog => false;
    public override string TypeName => "MessageBox";

    internal MessageBoxData data;    // m_data
    int count;              // m_count
    bool closing;           // m_closing

    /// <summary>The box shown now (GetCurrentBox @0x1419f8470).</summary>
    public MessageBoxData CurrentBox => data;

    /// <summary>FUN_1419f8270: no box is shown.</summary>
    public bool IsIdle => count == 0;

    /// <summary>PlaySystemSound (the box's sound on open, dialog_close on close); the host has no UI audio.</summary>
    public Action<string> PlaySound = _ => { };

    /// <summary>Feature 0x61 (FillObj's rbmerat member).</summary>
    public bool RbMerat;

    /// <summary>HandleGameexitButton @0x1419fb480, button 0: SendRequestQuitAndDisconnect, then PostQuitMessage(0).</summary>
    public event Action GameExit;

    /// <summary>HandleUseBindFuncItemButton's yes (uimessagebox.cpp:1868): the box's item uid used again with bConfirmed
    /// (UseItem(item, 1)); false when the inventory no longer has the item. Null: the item is not found.</summary>
    public Func<long, bool> UseBindFuncItem;
    /// <summary>HandleLearnEmotionButton's OK: the box's item uid (the local player test, FindItemByUid and
    /// Unk_Inventory::SendEmotion, <see cref="InventoryController.ConfirmLearnEmotion"/>).</summary>
    public Action<long> LearnEmotion;
    /// <summary>use_function_item's yes: FindItemByUid of the box's item uid and FUN_140a63880(item, "")
    /// (<see cref="InventoryController.ConfirmUseFunctionItem"/>).</summary>
    public Action<long> UseFunctionItem;
    /// <summary>The guild name box's use (HandleChangeGuildNameButton): the box's item uid and the name; false keeps the box.</summary>
    public Func<long, string, bool> UseGuildNameItem;
    /// <summary>CBanTextChecker::CheckGuildName (CommonString GetChecker): null when the name passes, else its message.</summary>
    public Func<string, string> CheckGuildName;
    /// <summary>ShowSystemNotice(text, 1, 1).</summary>
    public Action<string> SystemNotice;

    /// <summary>HandleUseBindEquipItemButton's yes (uimessagebox.cpp:1885): the box's item uid, arg 0 (the slot) and arg 1 (the
    /// layer) put on again with the binding skip (RequestEquipItem(item, a0, a1, 1)): its result, or null when the inventory
    /// no longer has the item. Null hook: the item is not found.</summary>
    public Func<long, uint, uint, int?> UseBindEquipItem;
    /// <summary>HandleUseBindEquipItemButton's end (uimessagebox.cpp:1899): the box's item uid and nResult (0 for the no, else
    /// the yes's RequestEquipItem), for the auto-equip's pending item (<see cref="AutoEquip.OnBindEquipAnswered"/>).</summary>
    public Action<long, int> BindEquipAnswered;

    /// <summary>HandleUseBindGemEquipItemButton's yes (uimessagebox.cpp:1907): the box's item uid and arg 0 (the gem slot) put
    /// on with the binding skip (EquipGem(item, a0, 1)); false when the inventory no longer has the item.</summary>
    public Func<long, int, bool> UseBindGemEquipItem;

    /// <summary>CPartyManager::SendPartyMode2(answer): HandlePartyinviteButton's answer to the last invite.</summary>
    public Action<int> AnswerPartyInvite;

    /// <summary>CGuildManager::SendGuildMode5(accept): HandleGuildinviteButton's answer to the invite received.</summary>
    public Action<bool> AnswerGuildInvite;

    /// <summary>CNetworkService::SendRecallUserReply(accept) (networkservice.cpp:522): HandleRecallconfirmButton's answer, button 0 accepts.</summary>
    public Action<bool> AnswerRecallUser;
    /// <summary>FUN_140710590 of HandleRecallscrollconfirmButton's RecallScroll (<see cref="RecallScrollConfirm.Accept"/>).</summary>
    public Action<Ms2.Net.Packets.RecallScrollAccept> SendRecallScroll;

    /// <summary>HandleRequestTradeExButton @0x1419fbc40 (uimessagebox.cpp:1503): the trade request box's answer, its argument 0
    /// (the requester's id, FUN_141a02f10(0, 0)) and yes (button 0) to FUN_14185b8e0.</summary>
    public Action<long, bool> RequestTradeAnswer;

    /// <summary>HandleTradeInButton @0x1419fe940 (uimessagebox.cpp:2042), the trade count box's yes: the box's item uid and
    /// the count typed (_wtoi of the tag's second token). The handler does the count check (FUN_141a002b0) and the add.</summary>
    public Action<long, int> TradeIn;

    /// <summary>FUN_141a002b0 (uimessagebox.cpp:2390): validates the item count of the sell, sell confirm and drop boxes; when unset, true.</summary>
    public Func<long, int, bool> ItemCountValid;

    /// <summary>HandleDropButton @0x1419fe790 (uimessagebox.cpp:2008), the drop box's yes: the item uid and the count typed (the
    /// plain "drop" tag gives 1). The handler does the count check and the request.</summary>
    public Action<long, int> DropItem;

    /// <summary>HandleDestroyItemButton @0x1419fba50 (uimessagebox.cpp:1352), the destroy box's yes: the box's item uid.</summary>
    public Action<long> DestroyItem;

    /// <summary>HandleDropButton @0x1419fe790 (uimessagebox.cpp:2008): no record, 1; the yes passes the box's item uid and the
    /// count on (the handler checks the count, FUN_141a002b0, and sends SendRequestItemInventoryMode4); 1.</summary>
    bool HandleDropButton(int button, int count)
    {
        if (data == null) return true;
        if (button == 0) DropItem?.Invoke(data.ItemUid, count);
        return true;
    }

    /// <summary>HandleDestroyItemButton @0x1419fba50 (uimessagebox.cpp:1352): no record, 1; the yes passes the box's item uid on
    /// (the handler finds the item and sends SendRequestItemInventoryMode5); 1. The local player check (none: 1) is the host's.</summary>
    bool HandleDestroyItemButton(int button)
    {
        if (data == null) return true;
        if (button == 0) DestroyItem?.Invoke(data.ItemUid);
        return true;
    }

    /// <summary>HandleSellButton @0x1419fe830 (uimessagebox.cpp:2024), the sell quantity box's yes (after closing the box):
    /// the item uid and count to pass to SellItem (:670).</summary>
    public Action<long, int> SellItem;

    /// <summary>HandleSellConfirmButton @0x1419fea00 (uimessagebox.cpp:2070), the sell confirmation box's yes: the item uid
    /// and count to pass to RequestSellItem.</summary>
    public Action<long, int> SellConfirm;

    /// <summary>A tag this port has no handler for (HandlePlainTag / FUN_1419fa910's other tags).</summary>
    public event Action<int, string> UnportedTag;

    bool HasRoot => Host?.Entity != null;     // m_30

    /// <summary>ShowMessage @0x1419f7ff0.</summary>
    public int ShowMessage(MessageBoxData box)
    {
        if (!HasRoot) return 0;
        if (IsSameBox(box)) return 0;
        closing = false;
        if (!IsIdle) CloseCurrent(0);
        Open(box);
        return 1;
    }

    /// <summary>FUN_1419f8120: the current box closes (with 1 while CloseAll runs).</summary>
    public void CloseBox()
    {
        CloseCurrent(closing ? 1 : 0);
    }

    /// <summary>CloseAll @0x1419f8170.</summary>
    public void CloseAll(int result)
    {
        if (HasRoot)
        {
            closing = true;
            Host.Invoke(Host.Entity, "closeAll", result);
        }
    }

    /// <summary>ShowIfIdle @0x1419f81f0: replaces the shown box (the name is the decomp's; it returns when idle).</summary>
    public void ShowIfIdle(MessageBoxData box)
    {
        if (IsIdle) return;
        CloseCurrent(0);
        Open(box);
    }

    /// <summary>UpdateErrorMessage @0x1419f82b0.</summary>
    public void UpdateErrorMessage(string text)
    {
        if (HasRoot) Host.Invoke(Host.Entity, "onProcessFunction", "updateErrorMsg", text);
    }

    /// <summary>UpdateMessage @0x1419f8370.</summary>
    public void UpdateMessage(string text)
    {
        if (!HasRoot) return;
        if (data != null) data.Msg = text;
        Host.Invoke(Host.Entity, "onProcessFunction", "updateMsg", text);
    }

    /// <summary>IsSameBox @0x1419f84c0: the same non-empty tag, or the same callback.</summary>
    bool IsSameBox(MessageBoxData box)
    {
        if (box == null || data == null) return false;
        if (box.Extra.Length > 0 && data.Extra.Length > 0 && data.Extra == box.Extra) return true;
        return box.Callback != null && data.Callback != null && box.Callback == data.Callback;
    }

    /// <summary>FUN_1419f8ec0: the layer comes to the front, the box's sound plays, FillObj goes to the movie's open, and the
    /// layer shows.</summary>
    void Open(MessageBoxData box)
    {
        count = 1;
        data = box;
        if (box.Sound.Length > 0) PlaySound(box.Sound);
        if (data == null) return;
        Host.Invoke(Host.Entity, "open", data.FillObj(MessageBoxQueue.AllModaless(), RbMerat, Host.Model));
        SetShown(true);
    }

    /// <summary>vfunc_8: the root's visibility (vfunc_47's input block is the movie's modal background here).</summary>
    void SetShown(bool visible) => Host.SetVisible(Host.Entity, visible);

    /// <summary>ResetAfterClose @0x1419f9110.</summary>
    void ResetAfterClose()
    {
        if (!IsIdle)
        {
            SetShown(false);
            PlaySound("dialog_close");
        }
        data?.ReleaseItems(Host?.Model);     // the record's last reference goes with m_data (~CMsgBoxDataItem's DestroyItemInfoSlot)
        data = null;
        count = 0;
        closing = false;
    }

    /// <summary>FUN_1419f9250: the movie closes the current box (closeCurrent(id, result)), then the layer resets. The
    /// modaless position save is not ported.</summary>
    void CloseCurrent(int result)
    {
        if (HasRoot && data != null)
        {
            data.ReArm();                                  // FUN_141a03470: the countdown's seconds left keep running
            Host.Invoke(Host.Entity, "closeCurrent", (long)data.Id, result);
        }
        ResetAfterClose();
    }

    /// <summary>vfunc_28 @0x1419f7980: OnClick(button, sID, tag, box); OnTopMostLayer is the layer order (one layer here).
    /// OnChangeTime and OnPopupTimerComplete are the timed boxes' countdown (CMsgBoxData); onHyperLinkClick belongs to box
    /// types not ported. OnPopupTimerComplete's UIMsg 2002 (the extra text) goes to a listener that implements
    /// IMessageBoxTimerListener; the other listeners here take no UIMsgs (STILL-OPEN).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (string.Equals(name, "OnClick", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length >= 3)
            {
                int button = (int)Avm2.Rt.ToNumber(args[0]);
                uint id = (uint)Avm2.Rt.ToNumber(args[1]);
                string tag = Avm2.Rt.ToString(args[2]) ?? "";
                object extra = args.Length > 3 ? args[3] : null;
                if (data != null) data.ClickedTag = tag;
                OnClick(button, id, tag, extra);
            }
            return true;
        }
        if (string.Equals(name, "OnChangeTime", StringComparison.OrdinalIgnoreCase))
        {
            if (data != null && args.Length >= 1)
                data.OnChangeTime(HasRoot, (int)Avm2.Rt.ToNumber(args[0]), text => Host.Invoke(Host.Entity, "onProcessFunction", "updateMsg", text));
            return true;
        }
        if (string.Equals(name, "OnTopMostLayer", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(name, "OnPopupTimerComplete", StringComparison.OrdinalIgnoreCase))
        {
            if (data == null) return false;
            data.OnPopupTimerComplete(HasRoot);
            if (data.Listener is IMessageBoxTimerListener tl) tl.OnTimerComplete(this, data.Extra);
            return false;
        }
        return false;
    }

    /// <summary>FUN_1419f9410: a callback box calls its delegate; any other goes to its listener and the tag handlers, and
    /// closes when a handler used it or the listener did not.</summary>
    public void OnClick(int button, uint id, string tag, object extra)
    {
        if (data == null || data.Id != id)
        {
            MessageBoxQueue.Next();
            return;
        }
        bool close = closing;
        var box = data;
        if (box.HasCallback)
        {
            if (box.InvokeCallback(button, extra))
            {
                if (box == data) MessageBoxQueue.Next();
                else MessageBoxQueue.Remove(box);
            }
        }
        else
        {
            bool handled = true;
            if (data.Listener is { } l) handled = l.OnMessageBox(this, button, tag);
            bool consumed = HandleTag(button, tag);
            if (tag == "rankArenaJoin" && !handled && !closing) return;
            if (consumed || !handled) close = true;
        }
        if (close && !MessageBoxQueue.IsIdle)
        {
            var next = MessageBoxQueue.Current();
            if (next != null)
            {
                if (next.Id == id) MessageBoxQueue.Next();
                else MessageBoxQueue.Remove(box);
            }
        }
    }

    /// <summary>FUN_1419f97a0: the tag handlers; true when the box is used up (a type 0 box always is). The group tags'
    /// UIMsg 0x7d1 is not ported (no group boxes).</summary>
    bool HandleTag(int button, string tag)
    {
        if (data == null) return true;
        if (tag.Length == 0) return !IsIdle;
        var box = data;
        if (HandlePlainTag(button, tag)) return true;
        if (HandleArgTag(button, tag)) return true;
        return box.Type == 0;
    }

    /// <summary>HandlePlainTag @0x1419f9ba0 (uimessagebox.cpp:899), its "gameexit", "useBindFuncItem", "useBindEquipItem",
    /// "useBindGemEquipItem" (:1052-1064), "learn_emotion" (:983-987) and "use_function_item" (:973-977) branches; FUN_1419fa910 ("tag,arg") and the other plain tags are reported as
    /// unported, except on a box with a listener: its tags (the slot menu's "expelMember:name", "changeChief:name") are the
    /// listener's, which OnClick has already called, and no plain tag handler takes them.</summary>
    internal bool HandlePlainTag(int button, string tag)
    {
        if (tag == "gameexit")
            return HandleGameexitButton(button);
        if (tag == "useBindFuncItem")
            return HandleUseBindFuncItemButton(button);
        if (tag == "useBindEquipItem")
            return HandleUseBindEquipItemButton(button);
        if (tag == CharNameChange.ConfirmTag)          // s_143af3ca8 (:988)
            return HandleConfirmChangeCharNameButton(button);
        if (tag == "useBindGemEquipItem")
            return HandleUseBindGemEquipItemButton(button);
        if (tag == InventoryController.LearnEmotionTag)                    // s_143af3ca0 (uimessagebox.cpp:983-987)
            return HandleLearnEmotionButton(button);
        if (tag == InventoryController.UseFunctionItemTag)                 // s_143af3c90 (uimessagebox.cpp:973-977)
            return HandleUseFunctionItemButton(button);
        if (tag.StartsWith(PartyInviteBox.Tag, StringComparison.Ordinal))   // strTag.Find("partyinvite") == 0
            return HandlePartyinviteButton(button);
        if (tag == RecallUserBox.Tag)
            return HandleRecallconfirmButton(button);
        if (tag == GuildInviteBox.Tag)
            return HandleGuildinviteButton(button);
        if (tag == "requestTradeEx")
            return HandleRequestTradeExButton(button);
        if (tag == RecallScrollNotifyController.ConfirmTag)
            return HandleRecallscrollconfirmButton(button);
        if (tag == "sell")
            return HandleSellButton(button, 1);
        if (tag == "drop")
            return HandleDropButton(button, 1);
        if (tag == "destroy_item")
            return HandleDestroyItemButton(button);
        if (tag == LoginRefusalBox.MaintenanceExitTag)
            return HandleMaintenanceExitButton(button);
        if (tag == LoginRefusalBox.AlreadyReconnectTag)
            return HandleAlreadyReconnectButton(button);
        if (tag == LoginRefusalBox.ExternalBlockTag || tag == LoginRefusalBox.AutoExternalBlockTag)
            return HandleExternalBlockButton(button);
        if (tag.IndexOf(',') < 0 && data?.Listener == null) UnportedTag?.Invoke(button, tag);
        return false;
    }

    /// <summary>FUN_1419fa910 (uimessagebox.cpp:1092): a tag of exactly two tokens split by "," (FUN_14028cb70(tag, tokens,
    /// L",", 2)); 0 otherwise. Of its tags "tradein", "drop", "sell", "sellconfirm" and "changeGuildName" are ported; the others are reported as unported.</summary>
    internal bool HandleArgTag(int button, string tag)
    {
        var tokens = tag.Split(',');
        if (tokens.Length != 2) return false;
        if (tokens[0] == "tradein") return HandleTradeInButton(button, MessageBoxData.WToI(tokens[1]));
        if (tokens[0] == "usestorein") return HandleUseStoreButton(false, button, MessageBoxData.WToI(tokens[1]));
        if (tokens[0] == "usestoreout") return HandleUseStoreButton(true, button, MessageBoxData.WToI(tokens[1]));
        if (tokens[0] == "swapstorein") return HandleSwapStoreInButton(button, MessageBoxData.WToI(tokens[1]));
        if (tokens[0] == "drop") return HandleDropButton(button, MessageBoxData.WToI(tokens[1]));
        if (tokens[0] == "sell") return HandleSellButton(button, MessageBoxData.WToI(tokens[1]));
        if (tokens[0] == "sellconfirm") return HandleSellConfirmButton(button, MessageBoxData.WToI(tokens[1]));
        if (tokens[0] == InvalidNameRename.Tag) return HandleChangeInvalidCharNameButton(button, tokens[1]);
        if (tokens[0] == InventoryController.ChangeGuildNameTag) return HandleChangeGuildNameButton(button, tokens[1]);
        if (tokens[0] == CharNameChange.Tag) return HandleChangeCharNameButton(button, tokens[1]);
        if (data?.Listener == null) UnportedTag?.Invoke(button, tag);
        return false;
    }

    /// <summary>HandlePartyinviteButton @0x1419fb930 (uimessagebox.cpp:1326): button 0 accepts (1); any other declines (9),
    /// 12 when the box's countdown pressed the button (GetTimer().m_nButton), 21 while CloseAll closes the boxes.</summary>
    bool HandlePartyinviteButton(int button)
    {
        int result = button == 0 ? Ms2.Net.Packets.PartyInviteAnswer.Accept : Ms2.Net.Packets.PartyInviteAnswer.Decline;
        if (result == Ms2.Net.Packets.PartyInviteAnswer.Decline)
        {
            if (closing) result = Ms2.Net.Packets.PartyInviteAnswer.Busy;
            else if (data != null && data.Timer.Button != 0) result = Ms2.Net.Packets.PartyInviteAnswer.TimedOut;
        }
        AnswerPartyInvite?.Invoke(result);
        return true;
    }

    /// <summary>HandleGuildinviteButton @0x1419fba00 (uimessagebox.cpp:1342): button 0 accepts, any other (the countdown's
    /// too) declines; 1.</summary>
    /// <summary>UIMessageBox::HandleRecallconfirmButton @0x1419fc640 (uimessagebox.cpp:1470): SendRecallUserReply(button == 0).</summary>
    bool HandleRecallconfirmButton(int button)
    {
        AnswerRecallUser?.Invoke(button == 0);
        return true;
    }

    bool HandleGuildinviteButton(int button)
    {
        AnswerGuildInvite?.Invoke(button == 0);
        return true;
    }

    /// <summary>HandleRecallscrollconfirmButton @0x1419fcf60 (uimessagebox.cpp:1641): no box, 1; button 0 sends RecallScroll with
    /// the box's slot, name and field; 1.</summary>
    bool HandleRecallscrollconfirmButton(int button)
    {
        if (data == null) return true;
        if (button == 0) SendRecallScroll?.Invoke(RecallScrollConfirm.Accept(data));
        return true;
    }

    /// <summary>HandleRequestTradeExButton @0x1419fbc40: the answer (yes on button 0) with argument 0; 1.</summary>
    bool HandleRequestTradeExButton(int button)
    {
        long id = data?.ArgLong(0, 0) ?? 0;
        RequestTradeAnswer?.Invoke(id, button == 0);
        return true;
    }

    /// <summary>HandleUseStoreInButton @0x1419fe240 / HandleUseStoreOutButton @0x1419fe310 (uimessagebox.cpp:1928, :1944): no
    /// record, 1; the yes passes the item uid and the typed count on; 1.</summary>
    bool HandleUseStoreButton(bool take, int button, int count)
    {
        if (data == null) return true;
        if (button == 0) (take ? UseStoreOut : UseStoreIn)?.Invoke(data.ItemUid, count);
        return true;
    }

    /// <summary>HandleSwapStoreInButton @0x1419fe590 (uimessagebox.cpp:1974): no record, 1; the yes passes the box's item uid,
    /// its slot (FUN_141a02c60) and the typed count on; 1. STILL-OPEN: swapstoreout- (@0x1419fe690) has no opener here.</summary>
    bool HandleSwapStoreInButton(int button, int count)
    {
        if (data == null) return true;
        if (button == 0) SwapStoreIn?.Invoke(data.ItemUid, data.InventorySlot, count);
        return true;
    }

    /// <summary>The swapstorein count box's yes: the item uid, the store slot and the count typed.</summary>
    public Action<long, int, int> SwapStoreIn;

    /// <summary>The store count boxes' yes (usestorein / usestoreout): the box's item uid and the count typed.</summary>
    public Action<long, int> UseStoreIn, UseStoreOut;

    /// <summary>HandleTradeInButton @0x1419fe940: no record, 1; the yes passes the item uid and count on; 1.</summary>
    bool HandleTradeInButton(int button, int count)
    {
        if (data == null) return true;
        if (button == 0) TradeIn?.Invoke(data.ItemUid, count);
        return true;
    }

    /// <summary>HandleSellButton @0x1419fe830 (uimessagebox.cpp:2024): no record, 1; on button 0, validates count
    /// (FUN_141a002b0), closes this box (FUN_14151f200) so a follow-up sellconfirm box replaces cleanly, then invokes
    /// <see cref="SellItem"/>; 1.</summary>
    bool HandleSellButton(int button, int count)
    {
        if (data == null) return true;
        if (button == 0)
        {
            long uid = data.ItemUid;
            if (ItemCountValid != null && !ItemCountValid(uid, count)) return true;
            MessageBoxQueue.Next();
            SellItem?.Invoke(uid, count);
        }
        return true;
    }

    /// <summary>HandleSellConfirmButton @0x1419fea00 (uimessagebox.cpp:2070): no record, 1; on button 0, validates count
    /// (FUN_141a002b0), then invokes <see cref="SellConfirm"/> (RequestSellItem); 1.</summary>
    bool HandleSellConfirmButton(int button, int count)
    {
        if (data == null) return true;
        if (button == 0)
        {
            long uid = data.ItemUid;
            if (ItemCountValid != null && !ItemCountValid(uid, count)) return true;
            SellConfirm?.Invoke(uid, count);
        }
        return true;
    }

    /// <summary>HandleGameexitButton @0x1419fb480.</summary>
    /// <summary>HandleMaintenanceExitButton @0x1419fc7d0 (uimessagebox.cpp:1513-1522): button 0 sends RequestQuit and closes the
    /// socket (SendRequestQuitAndDisconnect), restores the display mode (FUN_1416a0e70) and ends the process (PostQuitMessage(0)); 1.</summary>
    bool HandleMaintenanceExitButton(int button)
    {
        if (button == 0) MaintenanceExit?.Invoke();
        return true;
    }
    /// <summary>The maintenanceExit tag's OK (<see cref="HandleMaintenanceExitButton"/>).</summary>
    public event Action MaintenanceExit;

    /// <summary>HandleAlreadyReconnectButton @0x1419fcc70 (uimessagebox.cpp:1572-1577): button 0 connects to the configured server
    /// again (CNetworkService::ConnectToConfiguredServer, networkservice.cpp:458-463), whose RequestLogin sends the login again; 1.</summary>
    bool HandleAlreadyReconnectButton(int button)
    {
        if (button == 0) AlreadyReconnect?.Invoke();
        return true;
    }
    /// <summary>The alreadyReconnect tag's OK: the login again.</summary>
    public event Action AlreadyReconnect;

    /// <summary>HandleChangeInvalidCharNameButton @0x1419fecc0 (uimessagebox.cpp:2119-2156), the rename box's buttons
    /// (<see cref="InvalidNameRename.Answer"/>): no record, 1; a name to fix keeps the box with the error under its input
    /// (0); the request (<see cref="RenameInvalidName"/>) or the cancel (<see cref="RenameInvalidNameCancelled"/>), 1.</summary>
    bool HandleChangeInvalidCharNameButton(int button, string name)
    {
        if (data == null) return true;
        var (todo, error) = InvalidNameRename.Answer(button, name, CharacterNameError, MessageBoxData.Common);
        switch (todo)
        {
            case InvalidNameRename.Action.ShowError: UpdateErrorMessage(error); return false;
            case InvalidNameRename.Action.Send: RenameInvalidName?.Invoke(data.ArgLong(0, 0), name); return true;
            default: RenameInvalidNameCancelled?.Invoke(); return true;
        }
    }
    /// <summary>FUN_141276a30, the character name check: its message, null when the name passes (no checker: passes).</summary>
    public Func<string, string> CharacterNameError;
    /// <summary>The rename box's request: the character (argument 0) and the name (SendCharacterManagementMode5).</summary>
    public event Action<long, string> RenameInvalidName;
    /// <summary>The rename box's cancel: FUN_1418f20d0, the character list's input on.</summary>
    public event Action RenameInvalidNameCancelled;

    /// <summary>HandleExternalBlockButton @0x1419fccb0 (uimessagebox.cpp:1581-1611): button 0 reads argument 0 (FUN_141a00450) and
    /// opens its page: 0 the certify page, then the quit as <see cref="HandleMaintenanceExitButton"/>; 1 the identification page; 1.</summary>
    bool HandleExternalBlockButton(int button)
    {
        if (button != 0) return true;
        int type = data?.ArgInt(0, -1) ?? -1;
        if (type == 0) ExternalBlock?.Invoke("http://maplestory2.nexon.com/Auth/Certify", true);
        else if (type == 1) ExternalBlock?.Invoke("http://maplestory2.nexon.com/auth/identification", false);
        return true;
    }
    /// <summary>The externalBlock tags' OK: the page to open, and whether the client quits after it.</summary>
    public event Action<string, bool> ExternalBlock;

    bool HandleGameexitButton(int button)
    {
        if (button == 0)
        {
            GameExit?.Invoke();
            return false;
        }
        return true;
    }

    /// <summary>UIMessageBox::FUN_1419fc810 @0x1419fc810 (uimessagebox.cpp:1527-1567), "use_function_item": no record, 1; the
    /// yes (button 0) without the ask argument (arg 0) uses the box's item (<see cref="UseFunctionItem"/>); 1.
    /// STILL-OPEN: the ask argument's count box (type 0x1a, string 0x2a7, tag s_143af3da0): no ported box sets it, so such a
    /// box does nothing here.</summary>
    bool HandleUseFunctionItemButton(int button)
    {
        if (data == null) return true;
        if (button == 0 && data.ArgInt(0, 0) == 0) UseFunctionItem?.Invoke(data.ItemUid);
        return true;
    }

    /// <summary>UIMessageBox::HandleLearnEmotionButton @0x1419fc550 (uimessagebox.cpp:1451-1466): the OK (button 0) closes the
    /// box (FUN_14151f200) and sends the emotion item's learn (<see cref="LearnEmotion"/>); 1 either way.</summary>
    bool HandleLearnEmotionButton(int button)
    {
        if (data == null) return true;
        if (button == 0) LearnEmotion?.Invoke(data.ItemUid);
        return true;
    }

    /// <summary>HandleChangeGuildNameButton @0x1419feeb0 (uimessagebox.cpp:2159): no record, 1. The yes: an empty name
    /// shows common string 0xd as the box's error (UpdateErrorMessage) and keeps it; a name CheckGuildName refuses goes to
    /// the system notice and keeps it; else the item of the box's uid is used with the name (0 without the item).</summary>
    internal bool HandleChangeGuildNameButton(int button, string name)
    {
        if (data == null) return true;
        if (button != 0) return true;
        if (string.IsNullOrEmpty(name)) { UpdateErrorMessage(MessageBoxData.Common(0xd) ?? ""); return false; }
        if (CheckGuildName?.Invoke(name) is { } message) { SystemNotice?.Invoke(message); return false; }
        return UseGuildNameItem?.Invoke(data.ItemUid, name) ?? false;
    }

    /// <summary>HandleChangeCharNameButton @0x1419feae0 (uimessagebox.cpp:2086): no record, 1; see <see cref="CharNameChange.Answer"/>.
    /// The check goes as SendOp CheckCharName with the name and the box's item uid.</summary>
    internal bool HandleChangeCharNameButton(int button, string name)
    {
        if (data == null) return true;
        var (act, error) = CharNameChange.Answer(button, name, CharacterNameError, MessageBoxData.Common);
        if (act == CharNameChange.Action.ShowError) { UpdateErrorMessage(error); return false; }
        if (act == CharNameChange.Action.Send) CheckCharName?.Invoke(name, data.ItemUid);
        return true;
    }
    /// <summary>The name change box's check request (SendOp CheckCharName): the name and the item uid.</summary>
    public Action<string, long> CheckCharName;

    /// <summary>HandleConfirmChangeCharNameButton @0x1419fce20 (uimessagebox.cpp:1624): no record, 1; the yes uses the item of
    /// argument 0 (when the inventory has it) with the name of argument 1 (FUN_140a63880); 1.</summary>
    bool HandleConfirmChangeCharNameButton(int button)
    {
        if (data == null) return true;
        if (button == 0) UseCharNameItem?.Invoke(data.ArgLong(0, 0), data.ArgString(1, ""));
        return true;
    }
    /// <summary>The confirm's use: the item uid and the name. Null: nothing is used.</summary>
    public Action<long, string> UseCharNameItem;

    /// <summary>HandleUseBindFuncItemButton @0x1419fde80: no record, 1; the yes finds the item (0 without it) and uses it
    /// confirmed; 1.</summary>
    bool HandleUseBindFuncItemButton(int button)
    {
        if (data == null) return true;
        if (button == 0 && !(UseBindFuncItem?.Invoke(data.ItemUid) ?? false)) return false;
        return true;
    }

    /// <summary>HandleUseBindEquipItemButton @0x1419fdf80 (uimessagebox.cpp:1885): no record, 1; the yes finds the item (0
    /// without it) and puts it on with FUN_141a02e90(0, 0) and FUN_141a02e90(1, 0), nResult its RequestEquipItem; then, for
    /// the no too, the feature 0xe8 pending item test with nResult (<see cref="BindEquipAnswered"/>); 1.</summary>
    bool HandleUseBindEquipItemButton(int button)
    {
        if (data == null) return true;
        long uid = data.ItemUid;
        int result = 0;
        if (button == 0)
        {
            if (UseBindEquipItem?.Invoke(uid, data.ArgUInt(0, 0), data.ArgUInt(1, 0)) is not { } r) return false;
            result = r;
        }
        BindEquipAnswered?.Invoke(uid, result);
        return true;
    }

    /// <summary>HandleUseBindGemEquipItemButton @0x1419fe110 (uimessagebox.cpp:1907): no record, 1; the yes finds the item (0
    /// without it) and puts the gem on with FUN_141a02e50(0, 0), the slot, and the binding skip (EquipGem(item, slot, 1)); 1.</summary>
    bool HandleUseBindGemEquipItemButton(int button)
    {
        if (data == null) return true;
        if (button == 0 && !(UseBindGemEquipItem?.Invoke(data.ItemUid, data.ArgInt(0, 0)) ?? false)) return false;
        return true;
    }

    /// <summary>MessageBoxFactory.getCurMsgBox: the movie's last box (m_msgBoxArray's tail), null when none.</summary>
    public object MovieBox()
    {
        if (!HasRoot) return null;
        var boxes = Host.Get(Host.Entity, "m_msgBoxArray");
        int n = Avm2.Rt.ToInt32(Host.Get(boxes, "length"));
        return n == 0 ? null : Host.Get(boxes, (n - 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>GetInputText @0x141a04da0: m_textInput's text in the box the callback got (m_movie); "" without it.</summary>
    public string InputText(MessageBoxData box)
    {
        if (box?.MovieBox == null || Avm2.Rt.IsNullish(box.MovieBox)) return "";
        var input = Host.Get(box.MovieBox, "m_textInput");
        return Avm2.Rt.IsNullish(input) ? "" : Avm2.Rt.ToString(Host.Get(input, "text")) ?? "";
    }

    /// <summary>IsCheckbox0Checked @0x141a04aa0 / IsCheckbox1Checked @0x141a04c20: m_checkbox0/1's selected.</summary>
    public bool CheckboxChecked(MessageBoxData box, int n)
    {
        if (box?.MovieBox == null || Avm2.Rt.IsNullish(box.MovieBox)) return false;
        var check = Host.Get(box.MovieBox, n == 0 ? "m_checkbox0" : "m_checkbox1");
        return !Avm2.Rt.IsNullish(check) && Avm2.Rt.ToBoolean(Host.Get(check, "selected"));
    }

    /// <summary>vfunc_13 @0x1419f7560: a shown box's keys go to the movie's onProcessKey. <paramref name="key"/> is
    /// "enter", "escape", "up", "left", "down", "right" or "tab"; true when the key is used.</summary>
    public bool ProcessKey(string key)
    {
        if (IsIdle) return false;
        if (data == null || !HasRoot)
        {
            CloseAll(0);
            if (data == null || !HasRoot) return false;
        }
        string method = key switch
        {
            "enter" => data.EnterAllowed ? "onEnterKey" : null,
            "escape" => data.NoEscape ? null : "onEscapeKey",
            "up" => "onUpKey",
            "left" => "onLeftKey",
            "down" => "onDownKey",
            "right" => "onRightKey",
            "tab" => "onTabKey",
            _ => "",
        };
        if (method == "") return false;
        if (method == null) return true;
        Host.Invoke(Host.Entity, "onProcessKey", method);
        return true;
    }
}
