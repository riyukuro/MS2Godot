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
}

/// <summary>
/// The C++ side of the message box layer, UIMessageBox (client/ms2/uimessagebox.cpp; UI id 0x4e48), for its movie
/// uimessagebox. The boxes' layout, buttons and keys are the movie's (MessageBoxFactory and the msgbox_* classes); this
/// opens and closes them with the box record's members and answers their OnClick. Of the tag handlers "gameexit",
/// "useBindFuncItem" and "useBindEquipItem" are ported; the others are logged.
/// </summary>
public sealed class MessageBoxController : UiController
{
    public override string Movie => "uimessagebox";
    protected override string EntityMethod => "closeCurrent";
    protected override bool IsDialog => false;
    public override string TypeName => "MessageBox";

    MessageBoxData data;    // m_data
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

    /// <summary>HandleUseBindEquipItemButton's yes (uimessagebox.cpp:1885): the box's item uid, arg 0 (the slot) and arg 1 (the
    /// layer) put on again with the binding skip (RequestEquipItem(item, a0, a1, 1)); false when the inventory no longer has
    /// the item. Null: the item is not found.</summary>
    public Func<long, uint, uint, bool> UseBindEquipItem;

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
        if (HasRoot && data != null) Host.Invoke(Host.Entity, "closeCurrent", (long)data.Id, result);
        ResetAfterClose();
    }

    /// <summary>vfunc_28 @0x1419f7980: OnClick(button, sID, tag, box); OnTopMostLayer is the layer order (one layer here).
    /// OnChangeTime, OnPopupTimerComplete and onHyperLinkClick belong to box types not ported.</summary>
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
        if (string.Equals(name, "OnTopMostLayer", StringComparison.OrdinalIgnoreCase)) return true;
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
        return box.Type == 0;
    }

    /// <summary>HandlePlainTag @0x1419f9ba0 (uimessagebox.cpp:899), its "gameexit", "useBindFuncItem" and "useBindEquipItem"
    /// branches (:1052-1061); FUN_1419fa910 ("tag,arg") and the other plain tags are reported as unported, except on a box
    /// with a listener: its tags (the slot menu's "expelMember:name", "changeChief:name") are the listener's, which OnClick
    /// has already called, and no plain tag handler takes them.</summary>
    bool HandlePlainTag(int button, string tag)
    {
        if (tag == "gameexit")
            return HandleGameexitButton(button);
        if (tag == "useBindFuncItem")
            return HandleUseBindFuncItemButton(button);
        if (tag == "useBindEquipItem")
            return HandleUseBindEquipItemButton(button);
        if (data?.Listener == null) UnportedTag?.Invoke(button, tag);
        return false;
    }

    /// <summary>HandleGameexitButton @0x1419fb480.</summary>
    bool HandleGameexitButton(int button)
    {
        if (button == 0)
        {
            GameExit?.Invoke();
            return false;
        }
        return true;
    }

    /// <summary>HandleUseBindFuncItemButton @0x1419fde80: no record, 1; the yes finds the item (0 without it) and uses it
    /// confirmed; 1.</summary>
    bool HandleUseBindFuncItemButton(int button)
    {
        if (data == null) return true;
        if (button == 0 && !(UseBindFuncItem?.Invoke(data.ItemUid) ?? false)) return false;
        return true;
    }

    /// <summary>HandleUseBindEquipItemButton @0x1419fdf80: no record, 1; the yes finds the item (0 without it) and puts it on
    /// with FUN_141a02e90(0, 0) and FUN_141a02e90(1, 0); 1. The feature 0xe8 pending item reset is not ported (no pending
    /// item).</summary>
    bool HandleUseBindEquipItemButton(int button)
    {
        if (data == null) return true;
        long uid = data.ItemUid;
        if (button == 0 && !(UseBindEquipItem?.Invoke(uid, data.ArgUInt(0, 0), data.ArgUInt(1, 0)) ?? false)) return false;
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
