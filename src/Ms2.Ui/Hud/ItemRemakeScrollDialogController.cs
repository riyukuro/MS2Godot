using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The Change Attributes scroll window, UIItemRemakeScrollDialog (ms2/uiitemremakescrolldialog.cpp, UI id 0x4f09, type name
/// "ItemRemakeScrollDialog", movie uiitemremakescrolldialog). The server's ChangeAttributesScroll type 0 names the scroll and
/// opens it; an item dropped on the slot that the scroll's row (itemRemakeScroll.xml) accepts is shown with its options; the
/// confirm sends kind 1 (with the rows to lock), type 2 shows the old and the new roll, a pick of the new one sends kind 3 and
/// type 3 shows the applied item; a pick of the old one ends at once. Type 4 is the error notice.
/// <para>STILL-OPEN: the client dereferences a missing scroll row in OnLoadComplete and a missing target in SendRemakeRequest
/// and the type 2 reader (crashes there; nothing happens here); the high-roll box's callback reads its argument 2, which is
/// never set (so it always sends 0); a lock row past the option rows (a crash in GetLockedOptionText; skipped here); the pet
/// group 2 option names (no rows in the data); a drag source that is not an inventory slot (the client casts any MS2DragSlot
/// to InvenSlot; only inventory slots count here).</para>
/// </summary>
public sealed class ItemRemakeScrollDialogController : UiController, IDragSlotOwner
{
    public override string Movie => "uiitemremakescrolldialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "ItemRemakeScrollDialog";

    /// <summary>vfunc_48 (@0x140f607f0).</summary>
    public const int UiId = 0x4f09;
    /// <summary>m_strSlot ("{0}.slot" of the type name) and m_strIngredientSlot (OnLoadComplete :254).</summary>
    public const string Slot = "ItemRemakeScrollDialog.slot", IngredientSlot = "targetIngredient.slot";
    /// <summary>ItemScrollSlot's CreateItemInfoSlot icon type (1) and tooltip flags (0x20).</summary>
    public const int SlotFlags = 1, TipFlags = 0x20;
    /// <summary>s_strBindPetTag: the bind box's tag, answered by UIMessageBox::HandleRemakeOptionScrollBindPetButton.</summary>
    public const string BindPetTag = "remakeOptionScrollBindPet";
    /// <summary>The pet lock ingredient's function name.</summary>
    public const string LockPetFunction = "LockItemOptionPet";

    /// <summary>m_nState: 0 shut, 1 no item, 2 an item is on the slot, 3 a new roll is shown, 4 done.</summary>
    public int State { get; private set; }
    /// <summary>m_nPet: the row's onlyPet.</summary>
    public int Pet { get; private set; }
    /// <summary>m_bFinal: the scroll is still held after the end (another use is possible).</summary>
    public bool Final { get; private set; }
    /// <summary>m_bLockOption: the row's representOpValue.</summary>
    public bool LockOption { get; private set; }
    /// <summary>m_nOptionCount: the rows of the target's options.</summary>
    public int OptionCount { get; private set; }
    /// <summary>m_nScrollUid: set by the type 0 answer before the open.</summary>
    public long ScrollUid { get; set; }
    /// <summary>m_nIngredientUid.</summary>
    public long IngredientUid { get; private set; }
    /// <summary>m_spTarget, m_spOption (the new roll), m_spIngredient.</summary>
    public Item Target { get; private set; }
    public Item Option { get; private set; }
    public Item Ingredient { get; private set; }
    /// <summary>The items the two ItemScrollSlots show.</summary>
    public Item TargetSlotItem { get; private set; }
    public Item IngredientSlotItem { get; private set; }
    /// <summary>FUN_1412761e0(m_30, enabled): false while a request is out.</summary>
    public bool InputEnabled { get; private set; } = true;

    public Action<IOutPacket> Send = _ => { };
    /// <summary>g_143ab2a90.FindItemByUid: the inventory's item of the uid.</summary>
    public Func<long, Item> FindItem = _ => null;
    /// <summary>Unk_Inventory::FUN_140a5f7b0(tag, rarity): the items of a tag (and rarity, 0 any).</summary>
    public Func<int, int, int> CountByTag = (_, _) => 0;
    /// <summary>FUN_140f5d020: ReplaceItem, the item goes back into the inventory.</summary>
    public Action<Item> Store = _ => { };
    public Action<MessageBoxData> ShowData = d => MessageBoxQueue.Show(d);
    public Func<bool> ShiftHeld = () => false;
    /// <summary>CReservedWordTable::ReplaceObjectAndValueWords.</summary>
    public Func<string, string> ReplaceWords = s => s;
    /// <summary>ShowSystemNotice(text, 1, 1) and FUN_1412752d0(key, 1).</summary>
    public event Action<string> NoticeText;
    public event Action Closed;

    bool slotsRegistered, closing;

    string T(string key) => Model?.Tables.Common(key) ?? "";
    string Fmt(string key, params object[] a) => ClientFormat.Format(T(key), a);
    ItemTooltip Tip => new(Model, 0);
    void Notice(string key) => NoticeText?.Invoke(T(key));
    /// <summary>NOTICE (:194): ShowMessageBoxWithCallback(text, 0, FUN_140b240b0()).</summary>
    void NoticeBox(string text) => ShowData(MessageBoxQueue.CreateWithCallback(text, 0, (_, _) => true));
    /// <summary>RM_NOT_FOUND (:185) and ShowMessageBox(text, 0, 0, ""): a plain box.</summary>
    void PlainBox(string text) => ShowData(MessageBoxQueue.Create(0, text, null, ""));

    // ---- load / close -----------------------------------------------------------------------------------------------

    /// <summary>
    /// OnLoadComplete (@0x140f58430, :250): state 1, the two slots registered on the scroll and ingredient uids, then with the
    /// scroll held and its data the row's limit notice and description, the pet kind and the lock flag; initData with the slot
    /// names, the texts, onlyPet and feature 0x86 (enableOptionLock).
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Host?.Entity == null) return;
        State = 1;
        slotsRegistered = true;                                                   // FUN_140d42540 twice
        string notice = "", desc = "";
        Pet = 0;
        var scroll = FindItem(ScrollUid);
        if (scroll?.Data != null && Row(scroll) is { } row)
        {
            notice = ScrollLimitNotice(row);
            desc = ScrollDescription(scroll, row);
            Pet = row.PetKind;
            LockOption = row.LockOption != 0;
        }
        Host.Invoke("initData", Host.Obj(("bindSlotId", Slot), ("bindIngredientSlotId", IngredientSlot), ("notice", notice),
            ("desc", desc), ("onlyPet", Pet), ("enableOptionLock", Model.IsFeatureEnabled(0x86))));
    }

    /// <summary>vfunc_67 (@0x140f58f40, :322): the uids, the state, the slots and every field reset.</summary>
    public override void Close()
    {
        if (closing) return;
        closing = true;
        ScrollUid = IngredientUid = 0;
        State = 0;
        slotsRegistered = false;                                                  // FUN_140d42630 twice
        TargetSlotItem = IngredientSlotItem = null;
        Target = Option = Ingredient = null;
        Final = LockOption = false;
        OptionCount = 0;
        Pet = 0;
        base.Close();
        Closed?.Invoke();
    }

    /// <summary>The close button and Escape (UIDialog vfunc_69 / vfunc_70: vfunc_71 first, vfunc_67 when it returns 0).</summary>
    public void RequestClose()
    {
        if (!CloseGuard()) Close();
    }

    /// <summary>The key walk's Escape (UIDialog::vfunc_13) closes through vfunc_70 as the close button does: state 3 asks first.</summary>
    public override void EscapeClose() => RequestClose();

    public override bool OnCallbackBeforeDialog(string name, object[] args)
    {
        if (!string.Equals(name, "onCloseDialog", StringComparison.OrdinalIgnoreCase)) return false;
        RequestClose();
        return true;
    }

    /// <summary>vfunc_71 (@0x140f59070, :343): with a new roll shown (state 3) the close is asked (s_itemremake_scroll_ask_close,
    /// _pet for a pet row); a yes closes (OnAskCloseMsgBox @0x140f5f250).</summary>
    bool CloseGuard()
    {
        if (State != 3) return false;
        string ask = T(Pet != 0 ? "s_itemremake_scroll_ask_close_pet" : "s_itemremake_scroll_ask_close");
        ShowData(MessageBoxQueue.CreateWithCallback(ask, 1, (b, _) => { if (b == 0) Close(); return true; }));
        return true;
    }

    void SetInputEnabled(bool enable)
    {
        InputEnabled = enable;
        var root = Host?.Entity;
        if (root == null) return;
        Host.Set(root, "mouseChildren", enable);
        Host.Set(root, "mouseEnabled", enable);
    }

    // ---- the movie's commands -------------------------------------------------------------------------------------

    /// <summary>vfunc_28 (@0x140f58dd0, :294): onClickConfirm and GetEnableLockOption (each only with arguments), and the slots'
    /// notifyslotuse / notifyslotshiftclick (ItemScrollSlot, itemscrollslot.cpp).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onclickconfirm":
                if (args.Length > 0) OnClickConfirm(args);
                return true;
            case "getenablelockoption":
                if (args.Length > 0) GetEnableLockOption(args);
                return true;
            case "notifyslotuse": return SlotUse(args);
            case "notifyslotshiftclick":
                if (args.Length < 1 || !IsDragSlot(args[0] as string)) return false;
                Host.Push((string)args[0]);                                          // DragSlot::vfunc_15
                return true;
        }
        return false;
    }

    /// <summary>NotifyDragSlot::SlotUse: button 0 without Shift pushes the slot again; button 1 takes the item off
    /// (ItemScrollSlot vfunc_14 -> vfunc_17: SetItem(null), the remove fn, vfunc_10).</summary>
    bool SlotUse(object[] args)
    {
        if (args.Length < 1 || args[0] is not string slot || !IsDragSlot(slot)) return false;
        int button = args.Length > 1 ? InventoryController.ArgInt(args[1]) : 0;
        if (button == 0) { if (!ShiftHeld()) Host.Push(slot); }
        else if (button == 1) RemoveFromSlot(slot);
        return true;
    }

    public void RemoveFromSlot(string slot)
    {
        if (slot == Slot) { TargetSlotItem = null; OnTargetRemoved(); }
        else if (slot == IngredientSlot) { IngredientSlotItem = null; OnIngredientRemoved(); }
        else return;
        Host?.Push(slot);
    }

    /// <summary>OnClickConfirm (@0x140f5acf0, :582): by state, put an item on (1), remake (2), apply the pick (3), and at the end
    /// again with the scroll still held (Restart) or close (4).</summary>
    void OnClickConfirm(object[] args)
    {
        if (Host?.Entity == null) return;
        switch (State)
        {
            case 1:                                                                  // FUN_140f5adb0 (:607)
                if (Target == null) Notice("s_itemremake_scroll_notice_puton_item");
                else SetStateAndShow(2);
                break;
            case 2: AskRemake(); break;
            case 3: ApplySelectedOption(args); break;
            case 4:
                if (Final) Restart();
                else Close();                                                        // CloseDialog (@0x140f5bb20)
                break;
        }
    }

    /// <summary>
    /// FUN_140f5ae60 (@0x140f5ae60, :621): the scroll must be held. With no row to lock the remake starts (FUN_140f59420); else the
    /// lock is asked: a pet row needs the ingredient, a lock kind and the options, and asks s_pet_remake_ask_lock_item_option
    /// (the count, the ingredient's pet lock id, the locked rows, its name); else a lock kind (none: impossible) and
    /// s_itemremake_ask_lock_item_option with the lock item. A yes starts SendRemakeRequest (OnLockOptionMsgBox @0x140f5e4c0).
    /// </summary>
    void AskRemake()
    {
        if (Target == null || State != 2) return;
        if (FindItem(ScrollUid) == null) { PlainBox(T("s_itemremake_scroll_error_have_not_scroll")); return; }
        var locked = LockedOptionIndexes();
        long uid = Target.Uid;
        if (locked.Count == 0) { StartRemake(uid, 0); return; }
        string ask;
        if (Pet != 0)
        {
            if (Ingredient == null) return;
            if (LockId(Target) == 0) return;
            if (Target.Record?.Options == null) return;
            string rows = LockedOptionText(Target, locked);
            int petId = Ingredient.Data?.FunctionArgInt(0, 0) ?? 0;
            ask = Fmt("s_pet_remake_ask_lock_item_option", (ulong)locked.Count, (uint)petId, rows, ColoredName(Ingredient), (uint)petId);
        }
        else
        {
            int lockId = LockId(Target);
            if (lockId == 0) { Notice("s_itemremake_error_server_impossible"); return; }
            ask = Fmt("s_itemremake_ask_lock_item_option", LockItemText(lockId, LockLevel(Target)));
        }
        var box = MessageBoxQueue.CreateWithCallback(ask, 1, (b, d) =>                // ShowYesNoMessageBox
        {
            if (b == 0) SendRemakeRequest(d.ArgLong(0, 0), 0);
            return true;
        });
        box.Append(uid);
        ShowData(box);
    }

    /// <summary>
    /// FUN_140f59420 (@0x140f59420, :386): the scroll and the item must be held, the player and the row there; an item of rarity 5
    /// or more with a high roll (FUN_1409e4950) asks first in box 0x11 (its high-roll rows and
    /// s_itemremake_scroll_ask_high_sa_item_option, the item, buttons 0x475 / 0x476); a yes sends (FUN_140f5f2a0 @0x140f5f2a0,
    /// which reads the box's argument 2, never set: 0). Else it sends at once.
    /// </summary>
    public void StartRemake(long uid, int unk)
    {
        if (FindItem(ScrollUid) is not { } scroll) { PlainBox(T("s_itemremake_scroll_error_have_not_scroll")); return; }
        if (FindItem(uid) is not { } item) { Notice("s_itemremake_scroll_error_invalid_target"); return; }
        if (Model == null || !Model.HasLocalPlayer) return;
        if (Row(scroll) == null) return;
        string highSa = Tip.RemakeHighRollText(item, out bool best);
        if (item.Rarity >= 5 && best)
        {
            var box = MessageBoxQueue.Create(0x11, highSa + "\n" + T("s_itemremake_scroll_ask_high_sa_item_option"), null, "");
            box.Callback = (b, d) =>
            {
                if (b == 0) SendRemakeRequest(d.ItemUid, d.ArgInt(2, 0));
                return true;
            };
            box.ItemUid = item.Uid;
            box.SetItemEntry(item, Tip.Name(item), 1, 0, -1, -1, 1, Model.Tables.Items.TextColor);
            box.SetButtonLabelIds(0x475, 0x476, 0);
            box.Append(uid);
            box.Append(unk);
            ShowData(box);
            return;
        }
        SendRemakeRequest(uid, unk);
    }

    /// <summary>UIMessageBox::HandleRemakeOptionScrollBindPetButton (@0x1419fda70, uimessagebox.cpp:1809) past the box's data:
    /// no local player, 0; the yes (button 0) needs the box's item (FindItemByUid, else 0) and, with the window made
    /// (Unk_MessageBoxDialog 0x4f09), the window not closed (vfunc_55, else 0), then SendRemakeRequest(arg 0, 1). 1 otherwise.</summary>
    public static bool AnswerBindPet(ItemRemakeScrollDialogController dialog, bool hasLocalPlayer, Func<long, Item> findItem,
        int button, long itemUid, long arg)
    {
        if (!hasLocalPlayer) return false;
        if (button != 0) return true;
        if (findItem?.Invoke(itemUid) == null) return false;
        if (dialog == null) return true;
        if (dialog.IsClosed) return false;
        dialog.SendRemakeRequest(arg, 1);
        return true;
    }

    /// <summary>
    /// SendRemakeRequest (@0x140f59ad0, :433): the checks of FUN_140f59420 again; a pet row (not yet confirmed, unk 0) on an item
    /// that binds on use and is not bound asks first in box 0x11 (s_msg_transfer_bind_pet_remakeoption with the player's name,
    /// the item, buttons 0x477 / 0x478, tag <see cref="BindPetTag"/>), whose yes comes back with unk 1. Then kind 1 with the
    /// scroll, the target and the ingredient: with rows to lock there must be more than one row, a lock kind and one lock item
    /// held (FUN_140a5f7b0); each row is locked (Unk_RemakeStat::FUN_140619290), a negative index sets the locked flag. Input off.
    /// m_bHasLocked never holds here (FUN_140f5e240 gives ""), so the indexes are not shifted.
    /// </summary>
    public void SendRemakeRequest(long uid, int unk)
    {
        if (FindItem(ScrollUid) is not { } scroll) { PlainBox(T("s_itemremake_scroll_error_have_not_scroll")); return; }
        if (FindItem(uid) is not { } item) { Notice("s_itemremake_scroll_error_invalid_target"); return; }
        if (Model == null || !Model.HasLocalPlayer) return;
        if (Row(scroll) is not { } row) return;
        if (row.PetKind != 0 && unk == 0 && InventoryController.NeedsBindConfirm(item))
        {
            var bind = InventoryEquip.BindBox(Model, item, "s_msg_transfer_bind_pet_remakeoption", BindPetTag);
            bind.Append(uid);
            ShowData(bind);
            return;
        }
        if (Target == null) return;                                                   // STILL-OPEN: the client dereferences it
        var locked = LockedOptionIndexes();
        bool locks = false;
        var rows = new List<RemakeLock>();
        if (locked.Count > 0)
        {
            if (OptionCount <= 1) { NoticeBox(T("s_itemremake_notice_disable_lock_item_option_error_count")); return; }
            int lockId = LockId(item);
            if (lockId == 0) { Notice("s_itemremake_error_server_impossible"); return; }
            int level = LockLevel(item);
            if (CountByTag(lockId, level) <= 0)
            {
                NoticeBox(Fmt("s_itemremake_notice_disable_lock_item_option_when_click_remake", LockItemText(lockId, level)));
                return;
            }
            foreach (int index in locked)
            {
                if (index >= 0)
                {
                    if (Tip.RemakeOptionLock(item, 2, index, out int kind, out int value)) rows.Add(new RemakeLock((byte)kind, (short)value));
                }
                else locks = true;
            }
        }
        Send(ChangeAttributesScrollRequest.Remake(ScrollUid, Target.Uid, IngredientUid, locks, rows.ToArray()));
        SetInputEnabled(false);
    }

    /// <summary>
    /// ApplySelectedOption (@0x140f5b670, :675): with a new roll shown, the target held and a pick (-1: the notice), input off and
    /// the picked item stored (FUN_140f5d020, before the answer, as the client does); the new roll sends kind 3, the old one
    /// ends at once (state 4, ShowComplete).
    /// </summary>
    void ApplySelectedOption(object[] args)
    {
        if (State != 3 || Target == null || Option == null) return;
        if (FindItem(Target.Uid) == null) { PlainBox(T("s_itemremake_scroll_error_have_not_item")); return; }
        int selected = args[0] is AsObject o ? Rt.ToInt32(Host.Get(o, "selectedOptionIndex")) : 0;
        if (selected == -1)
        {
            Notice(Pet != 0 ? "s_itemremake_scroll_error_invalid_selected_option_pet" : "s_itemremake_scroll_error_invalid_selected_option");
            return;
        }
        SetInputEnabled(false);
        var chosen = selected == 0 ? Target : Option;
        Store(chosen);
        if (chosen == Option) Send(ChangeAttributesScrollRequest.Apply(chosen.Uid));
        else
        {
            State = 4;
            ShowComplete(chosen);
        }
    }

    /// <summary>Restart (@0x140f5bb60, :721).</summary>
    void Restart() => SetStateAndShow(Target != null ? 2 : 1);

    /// <summary>
    /// GetEnableLockOption (@0x140f5bbb0, :730): the movie asks why a row can not be locked (the rows it has, the most it may
    /// lock). One row or none: the count notice; a pet row: no lock at all (s_pet_remake_notice_disable_lock_item_option), or at
    /// the limit the ingredient's limit (s_pet_remake_notice_lock_item_option_limit) or the max count; else no lock item held.
    /// </summary>
    void GetEnableLockOption(object[] args)
    {
        if (Host?.Entity == null || Target == null) return;
        uint have = (uint)Rt.ToInt32(args[0]);
        uint need = args.Length > 1 ? (uint)Rt.ToInt32(args[1]) : 0;
        if (OptionCount <= 1) NoticeBox(T("s_itemremake_notice_disable_lock_item_option_error_count"));
        else if (Pet != 0)
        {
            if (need == 0) PlainBox(ReplaceWords(T("s_pet_remake_notice_disable_lock_item_option")));
            else if (need <= have)
            {
                if (FindItem(IngredientUid) is not { } ingredient) return;
                var d = ingredient.Data;
                if (d == null || d.FunctionName != LockPetFunction) return;
                uint petId = (uint)d.FunctionArgInt(0, 0);
                if (petId == need) PlainBox(Fmt("s_pet_remake_notice_lock_item_option_limit", ColoredName(ingredient), petId));
                else PlainBox(T("s_pet_remake_notice_lock_item_option_max_count"));
            }
        }
        else
        {
            int lockId = LockId(Target);
            int level = LockLevel(Target);
            if (lockId != 0 && CountByTag(lockId, level) == 0)
                NoticeBox(Fmt("s_itemremake_notice_disable_lock_item_option", LockItemText(lockId, level)));
        }
    }

    /// <summary>GetLockedOptionIndexes (@0x140f5f310, :1251): the movie's getLockOptionIndex array.</summary>
    List<int> LockedOptionIndexes()
    {
        var list = new List<int>();
        if (Host?.Invoke("getLockOptionIndex") is AsArray a)
            foreach (var v in a.Items) list.Add(Rt.ToInt32(v));
        return list;
    }

    /// <summary>GetLockedOptionText: the target's rows (GetOptionText of Unk_RemakeStat(2, item, false)) split at the line
    /// breaks (empty pieces dropped), each locked row and a line break. STILL-OPEN: a row past the end (the client reads past
    /// the array) is skipped.</summary>
    string LockedOptionText(Item item, List<int> locked)
    {
        var rows = Tip.RemakeOptionText(item, 2, false, out _).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var sb = new System.Text.StringBuilder();
        foreach (int i in locked)
            if (i >= 0 && i < rows.Length) sb.Append(rows[i]).Append('\n');
        return sb.ToString();
    }

    // ---- the answers ------------------------------------------------------------------------------------------------

    /// <summary>
    /// FUN_140f59230 (@0x140f59230, :357), the open window's part: input on, then type 2 (FUN_140f5a7e0) and 3 (FUN_140f5a9c0)
    /// while open, and type 4 (OnRemakeError). Type 0 is the open (<see cref="ScrollUid"/>, then the open, by the caller).
    /// <paramref name="readBody"/> reads the item body over a copy of the record.
    /// </summary>
    public void OnPacket(ChangeAttributesScrollPacket p, Func<EquipItem, EquipItem> readBody)
    {
        SetInputEnabled(true);
        switch (p.Type)
        {
            case 2: if (!IsClosed) OnNewRoll(p, readBody); break;
            case 3: if (!IsClosed) OnApplied(p, readBody); break;
            case 4: OnRemakeError(p); break;
        }
    }

    /// <summary>FUN_140f5a7e0 (:524): the new roll is read over a copy of the target; the item gone: the box (the roll stays);
    /// else state 3 and both rolls shown. STILL-OPEN: without a target the client crashes; nothing here.</summary>
    void OnNewRoll(ChangeAttributesScrollPacket p, Func<EquipItem, EquipItem> readBody)
    {
        if (Target == null) return;
        Option = Remade(Target, readBody);
        if (FindItem(p.Uid) == null) { PlainBox(T("s_itemremake_scroll_error_have_not_item")); return; }
        State = 3;
        ShowSelectOption();
    }

    /// <summary>FUN_140f5a9c0 (:542): the applied item read over the new roll; another uid: nothing; else stored, state 4 and
    /// the end shown.</summary>
    void OnApplied(ChangeAttributesScrollPacket p, Func<EquipItem, EquipItem> readBody)
    {
        if (Option == null) return;
        Option = Remade(Option, readBody);
        if (p.Uid != Option.Uid) return;
        Store(Option);
        State = 4;
        ShowComplete(Option);
    }

    void OnRemakeError(ChangeAttributesScrollPacket p)
    {
        if (Model != null && ErrorText(Model.Tables, Pet, p.Flag, p.Code) is { } text) NoticeText?.Invoke(text);
    }

    /// <summary>
    /// OnRemakeError (@0x140f5ab10, :557) with FUN_140f5e2a0 (@0x140f5e2a0, :1065): code 0 nothing; a code of the check's table its
    /// key (with "_pet" for a pet row); 0xc / 0xd the remake failed, 0xe no roll to apply, 0xf the scroll not used; else
    /// s_itemremake_scroll_error_server_default with the flag and the code. Also for a type 4 while the window is shut
    /// (<paramref name="pet"/> 0 then).
    /// </summary>
    public static string ErrorText(GameTables t, int pet, int flag, int code)
    {
        if (code == 0) return null;
        if (CheckErrorKey(code) is { } key) return t.Common(pet != 0 ? key + "_pet" : key) ?? "";
        string Common(string k) => t.Common(k) ?? "";
        return code switch
        {
            0xc or 0xd => Common("s_itemremake_scroll_error_server_fail_remake"),
            0xe => Common("s_itemremake_scroll_error_server_fail_apply_before_option"),
            0xf => Common("s_itemremake_scroll_error_server_fail_consume_scroll"),
            _ => ClientFormat.Format(Common("s_itemremake_scroll_error_server_default"), flag, code),
        };
    }

    /// <summary>FUN_140f5e2a0's table.</summary>
    static string CheckErrorKey(int code) => code switch
    {
        1 => "s_itemremake_scroll_error_invalid_target",
        10 => "s_itemremake_scroll_error_invalid_scroll",
        2 => "s_itemremake_scroll_error_invalid_target_data",
        3 => "s_itemremake_scroll_error_invalid_target_stat",
        4 => "s_itemremake_scroll_error_impossible_slot",
        8 => "s_itemremake_scroll_error_impossible_item",
        5 => "s_itemremake_scroll_error_impossible_rank",
        6 => "s_itemremake_scroll_error_impossible_level",
        7 => "s_itemremake_scroll_error_impossible_property",
        0xb => "s_itemremake_scroll_error_invalid_scroll_data",
        0x10 => "s_itemremake_error_server_fail_lack_lock_consume_item",
        0x11 => "s_content_shutdown_notice",
        _ => null,
    };

    /// <summary>FUN_140f5e2a0 in the window: the key's notice; false for a code it has no key for.</summary>
    bool ErrorNotice(int code)
    {
        if (code == 0 || CheckErrorKey(code) == null || Model == null) return false;
        NoticeText?.Invoke(ErrorText(Model.Tables, Pet, 0, code));
        return true;
    }

    static Item Remade(Item item, Func<EquipItem, EquipItem> readBody)
    {
        var n = item.Clone();
        if (readBody != null && item.Record != null) n.Record = readBody(item.Record);
        return n;
    }

    // ---- the steps shown --------------------------------------------------------------------------------------------

    /// <summary>SetStateAndShow (@0x140f5e1f0, :1049).</summary>
    void SetStateAndShow(int state)
    {
        State = state;
        if (state == 2) ShowOption();
        else if (state == 1) ShowEmpty();
    }

    /// <summary>FUN_140f5c2e0 (:782): restart and ClearOptions (@0x140f5e510).</summary>
    void ShowEmpty()
    {
        if (State != 1) return;
        Host?.Invoke("restart");
        Target = Option = null;
        OptionCount = 0;
    }

    /// <summary>FUN_140f5c340 (:791): the target's rows (Unk_RemakeStat(2, target, true)) in s_itemremake_scroll_cur_option
    /// (s_itemremake_scroll_pet_cur_option for a pet row), their count and whether its kind can lock (CanLockOptions).</summary>
    void ShowOption()
    {
        if (Target == null || State != 2) return;
        OptionCount = 0;
        string rows = Tip.RemakeOptionText(Target, 2, true, out int count);
        OptionCount = count;
        string text = Fmt(Pet != 0 ? "s_itemremake_scroll_pet_cur_option" : "s_itemremake_scroll_cur_option", rows);
        bool canLock = ItemRemakeOptionDialogController.CanLockOptions(Target, Model.Tables.ItemModels, Model.IsFeatureEnabled);
        Host?.Invoke("showOption", (object)text, (uint)count, canLock);
    }

    /// <summary>ShowSelectOption (@0x140f5c700, :819): input on; the old and the new rows.</summary>
    void ShowSelectOption()
    {
        SetInputEnabled(true);
        if (Target == null || State != 3) return;
        string cur = Pet != 0 ? "s_itemremake_scroll_pet_cur_option" : "s_itemremake_scroll_cur_option";
        string @new = Pet != 0 ? "s_itemremake_scroll_pet_new_option" : "s_itemremake_scroll_new_option";
        Host?.Invoke("selectOption", (object)Fmt(cur, Tip.RemakeOptionText(Target, 2, true, out _)),
            Fmt(@new, Tip.RemakeOptionText(Option, 2, true, out _)), 0);
    }

    /// <summary>ShowComplete (@0x140f5cba0, :846): input on; at state 4 Final is whether the scroll is still held, the item's
    /// rows in s_itemremake_scroll_final_option (_pet_), the item on the slot, the ingredient slot emptied when it is used up;
    /// the target stays only for another use.</summary>
    void ShowComplete(Item item)
    {
        SetInputEnabled(true);
        if (State != 4) return;
        Final = FindItem(ScrollUid) != null;
        string text = Fmt(Pet != 0 ? "s_itemremake_scroll_pet_final_option" : "s_itemremake_scroll_final_option",
            Tip.RemakeOptionText(item, 2, true, out _));
        Host?.Invoke("complete", (object)text, Final);
        SetTargetSlot(item);                                                          // FUN_140f5d140
        if (FindItem(IngredientUid) == null) SetIngredientSlot(null);
        Target = Option = null;
        if (Final) Target = item;
    }

    /// <summary>FUN_140f5d140 (:901): the slot's item set and pushed, while it is registered.</summary>
    void SetTargetSlot(Item item)
    {
        if (!slotsRegistered) return;
        TargetSlotItem = item;
        Host?.Push(Slot);
    }

    /// <summary>SetIngredientItem (@0x140f5d220, :914): the slot's item set and pushed; with no slot an empty item clears the
    /// ingredient.</summary>
    void SetIngredientSlot(Item item)
    {
        if (slotsRegistered)
        {
            IngredientSlotItem = item;
            Host?.Push(IngredientSlot);
            return;
        }
        if (item == null)
        {
            Ingredient = null;
            IngredientUid = 0;
            Host?.Invoke("selectItemIngredient");
        }
    }

    // ---- the row's texts --------------------------------------------------------------------------------------------

    /// <summary>GetRemakeScrollData (@0x140f5d380, :933): the row of the scroll's function argument 0.</summary>
    ItemRemakeScrollData Row(Item scroll) => scroll?.Data == null ? null : Model?.Tables.RemakeScroll(scroll.Data.FunctionArgInt(0, 0));

    /// <summary>
    /// GetScrollLimitNotice (@0x140f5d460, :943): s_itemremake_scroll_limit with the level (one level, from a level, a range),
    /// the ranks (each its coloured rank name, the later ones through s_itemremake_scroll_limit_add) and the condition (an item
    /// list: item_condition(_pet); a pet row: notice_pet; else the slot names (FUN_141811980(8, slot), empty ones skipped)
    /// in slot_condition).
    /// </summary>
    public string ScrollLimitNotice(ItemRemakeScrollData row)
    {
        if (row == null) return "";
        string level = row.LevelMin == row.LevelMax ? Fmt("s_common_scroll_limit_level", (ushort)row.LevelMin)
            : row.LevelMax == 0 ? Fmt("s_common_scroll_limit_level_upper", (ushort)row.LevelMin)
            : Fmt("s_common_scroll_limit_level_range", (ushort)row.LevelMin, (ushort)row.LevelMax);
        string sep = T("s_itemremake_scroll_limit_add");
        var limit = new System.Text.StringBuilder();
        for (int i = 0; i < row.Ranks.Length; i++)
        {
            string rank = ColoredRank(row.Ranks[i]);
            limit.Append(i != 0 ? ClientFormat.Format(sep, rank) : rank);
        }
        string condition = "";
        if (row.Items.Length != 0)
            condition = T(row.PetKind != 0 ? "s_itemremake_scroll_notice_item_condition_pet" : "s_itemremake_scroll_notice_item_condition");
        else if (row.PetKind != 0) condition = T("s_itemremake_scroll_notice_pet");
        else if (row.Slots.Length != 0)
        {
            var slots = new System.Text.StringBuilder();
            foreach (uint s in row.Slots)
            {
                string name = Model.Tables.SlotName((int)s);
                if (name.Length == 0) continue;
                slots.Append(slots.Length != 0 ? ClientFormat.Format(sep, name) : name);
            }
            condition = Fmt("s_itemremake_scroll_notice_slot_condition", slots.ToString());
        }
        return Fmt("s_itemremake_scroll_limit", level, limit.ToString(), condition);
    }

    /// <summary>
    /// GetScrollDescription (@0x140f5dd50, :1006): with an option value the kind and the value lines
    /// (s_itemremake_scroll_desc_add_option_(pet_) + kind_x / kind_o and value_x / value_o / value_high / value_max), with the
    /// lock flag and every slot a weapon or armour one (FUN_140222c60) the represent line; s_itemremake_scroll_desc with the text and
    /// the scroll's coloured name, the reserved words replaced.
    /// </summary>
    public string ScrollDescription(Item scroll, ItemRemakeScrollData row)
    {
        if (scroll == null || row == null) return "";
        string text = "";
        if (row.OptionValue != 0)
        {
            string prefix = row.PetKind != 0 ? "s_itemremake_scroll_desc_add_option_pet_" : "s_itemremake_scroll_desc_add_option_";
            string kind = row.OptionKind switch { 0 => "kind_x", 1 => "kind_o", _ => "" };       // GetOptionKindKeySuffix
            string value = row.OptionValue switch { 0 => "value_x", 1 => "value_o", 2 => "value_high", 3 => "value_max", _ => "" };
            text = T(prefix + kind) + "\n" + T(prefix + value);
        }
        if (row.LockOption != 0 && row.Slots.All(s => LockSlotKind(s)))
            text += "\n" + T("s_itemremake_scroll_desc_represent_wap_value_o");
        return ReplaceWords(Fmt("s_itemremake_scroll_desc", text, ColoredName(scroll)));
    }

    /// <summary>FUN_140222c60: the weapon (0x1e-0x22, 0x28, 0x29, 0x32-0x38) slots.</summary>
    public static bool LockSlotKind(uint slot) => slot is (>= 0x1e and <= 0x22) or 0x28 or 0x29 or (>= 0x32 and <= 0x38);

    string ColoredRank(uint rank)
    {
        string name = rank == 0 ? Model.Tables.Common(0x4f9) ?? "" : T("s_word_rank_" + rank.ToString("00", CultureInfo.InvariantCulture));
        return $"<FONT color='#{Model.Tables.Items.TextColor((int)rank):x}'>{name}</FONT>";
    }

    string Colored(string name, int rank) => $"<FONT color='#{Model.Tables.Items.TextColor(rank):x}'>{name}</FONT>";
    /// <summary>CItem::FUN_140528160: the item's name in its rarity's colour.</summary>
    string ColoredName(Item item) => Colored(Tip.Name(item), item.Rarity);
    int LockId(Item item) => ItemRemakeOptionDialogController.LockItemId(item, Model.Tables.ItemModels);
    int LockLevel(Item item) => ItemRemakeOptionDialogController.LockItemLevel(item, Model.IsFeatureEnabled);

    /// <summary>FUN_140619760: the lock item's name in the level's colour (or its own rank for 0).</summary>
    public string LockItemText(int lockId, int level)
    {
        if (lockId == 0) return "";
        int id = Model.Tables.Items.TagItemId(lockId);
        int rank = level != 0 ? level : ItemTooltip.ItemRank(Model.Tables.Items, id);
        return Colored(Model.Tables.ItemName(id), rank);
    }

    // ---- the slots (ItemScrollSlot) ---------------------------------------------------------------------------------

    /// <summary>FUN_140f5e570 (:1116): the target's remove fn: selectItem with no argument and state 1.</summary>
    void OnTargetRemoved()
    {
        if (Host?.Entity == null) return;
        Host.Invoke("selectItem");
        SetStateAndShow(1);
    }

    /// <summary>OnIngredientRemoved (@0x140f5ed50, :1181).</summary>
    void OnIngredientRemoved()
    {
        if (Host?.Entity == null) return;
        Ingredient = null;
        IngredientUid = 0;
        Host.Invoke("selectItemIngredient");
    }

    /// <summary>
    /// OnTargetSlotDrop (@0x140f5e5f0, :1125): no scroll uid, the item gone or the scroll gone: their boxes; the row's check
    /// (FUN_140616170) refusing: its notice; else the item on the slot, the target, selectItem with its coloured name and
    /// whether a row can be locked (a lock item held; a pet row: the ingredient's count) and state 2.
    /// </summary>
    public void DropTarget(long uid)
    {
        if (Host?.Entity == null) return;
        if (ScrollUid == 0) { PlainBox(T("s_itemremake_scroll_error_have_not_scroll")); return; }
        if (FindItem(uid) is not { } item) { PlainBox(T("s_itemremake_scroll_error_have_not_item")); return; }
        var scroll = FindItem(ScrollUid);
        if (scroll?.Data == null) { PlainBox(T("s_itemremake_scroll_error_have_not_scroll")); return; }
        int err = ItemRemakeScroll.Check(item, Row(scroll), Model.Tables.ItemOptions);
        if (err != ItemRemakeScroll.Ok) { ErrorNotice(err); return; }
        TargetSlotItem = item;
        Host.Push(Slot);
        Target = item;
        int lockCount = 0;
        if (Pet == 0)
        {
            int lockId = LockId(Target);
            if (lockId != 0) lockCount = CountByTag(lockId, LockLevel(Target)) != 0 ? 1 : 0;
        }
        else if (Ingredient != null) lockCount = Ingredient.Data?.FunctionArgInt(0, 0) ?? 0;
        Host.Invoke("selectItem", Host.Obj(("itemName", ColoredName(item)), ("maxOptionLockCount", lockCount != 0 ? 1 : 0)));
        SetStateAndShow(2);
    }

    /// <summary>
    /// OnIngredientSlotDrop (@0x140f5ede0, :1191): no scroll uid or the item gone: their boxes; not a pet lock item
    /// (LockItemOptionPet): the error 1 notice; else on the slot, the most rows to lock (its argument, at most one less than the
    /// rows), the ingredient, selectItemIngredient and state 2.
    /// </summary>
    public void DropIngredient(long uid)
    {
        if (Host?.Entity == null) return;
        if (ScrollUid == 0) { PlainBox(T("s_itemremake_scroll_error_have_not_scroll")); return; }
        if (FindItem(uid) is not { } item) { PlainBox(T("s_itemremake_scroll_error_have_not_item")); return; }
        var d = item.Data;
        if (d == null) return;
        if (d.FunctionName != LockPetFunction) { ErrorNotice(1); return; }
        IngredientSlotItem = item;
        Host.Push(IngredientSlot);
        int limit = Math.Min(OptionCount - 1, d.FunctionArgInt(0, 0));
        Ingredient = item;
        IngredientUid = item.Uid;
        Host.Invoke("selectItemIngredient", Host.Obj(("maxOptionLockCount", limit)));
        SetStateAndShow(2);
    }

    Item SlotItem(string name) => !slotsRegistered ? null : name == Slot ? TargetSlotItem : name == IngredientSlot ? IngredientSlotItem : null;

    public override IEnumerable<string> GetBagSlotBindings(string bag) => Array.Empty<string>();

    public override object GetBinding(string name)
    {
        if (name != Slot && name != IngredientSlot) return base.GetBinding(name);
        var item = SlotItem(name);
        if (item == null || Model == null || !Model.HasLocalPlayer) return null;
        var slot = new List<(string, object)>();
        return ItemSlot.Build(slot, item, Model.HasLocalPlayer, SlotFlags, Model, Model.ItemSlotSources) ? Host.Obj(slot.ToArray()) : null;
    }

    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (name != Slot && name != IngredientSlot) return base.GetSlotTooltip(name);
        var item = SlotItem(name);
        if (Model == null || !Model.HasLocalPlayer || item == null) return new AsTooltip();
        var tip = new ItemTooltip(Model, TipFlags) { Localize = k => Host.Player.Translate?.Invoke(k) ?? k };
        return tip.Fill(item) ? tip.Data : new AsTooltip();
    }

    public bool IsDragSlot(string name) => slotsRegistered && (name == Slot || name == IngredientSlot);
    public void RefreshSlot(string name) { if (IsDragSlot(name)) Host?.Push(name); }
    public void SlotDragStarted(string name) { }
    public void SlotDropUnresolved(string name) => RefreshSlot(name);

    /// <summary>ItemScrollSlot::vfunc_18: the drop fn with the dragged slot, then the source pushed (DragSlot::vfunc_10).</summary>
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner)
    {
        if (IsDragSlot(name) && sourceOwner is IInvenSlotOwner inv && inv.InvenSlotUid(source) is long uid)
        {
            if (name == Slot) DropTarget(uid);
            else DropIngredient(uid);
        }
        sourceOwner?.RefreshSlot(source);
    }
}
