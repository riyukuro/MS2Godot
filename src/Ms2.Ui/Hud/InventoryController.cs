using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>The owner of the inventory's bag slots (InvenSlot: AsDragSlot's TypeCast 0x6200023 casts a drag slot to it),
/// with the inventory's (g_143ab2a90, Unk_Inventory) lookup and equip request.</summary>
public interface IInvenSlotOwner : IDragSlotOwner
{
    /// <summary>InvenSlot::GetItemUid: the slot's item uid (m_nItemUid, 0 for an empty cell); null for a name that is not one
    /// of the bag's slots.</summary>
    long? InvenSlotUid(string name);
    /// <summary>Unk_Inventory::FindItemByUid: null when the inventory has no item of the uid.</summary>
    Item FindItem(long uid);
    /// <summary>Unk_Inventory::CanEquipItem(item, slot, layer) (inventory.cpp:1418), its boxes shown.</summary>
    bool CanEquipItem(Item item, int slot, int layer);
    /// <summary>Unk_Inventory::RequestEquipItem(item, slot, layer, 0) (inventory.cpp:1236) with its checks.</summary>
    void RequestEquipItem(Item item, int slot, int layer);
}

/// <summary>
/// UIInventoryDialog (client/ms2/uiinventorydialog.cpp; UI id 0x4e2d), the C++ side of the inventory movie uiinventorydialog,
/// over an <see cref="InventoryView"/> (the client's g_143ab2a90 as the dialog reads it). The movie draws the window, the tab
/// bar, the slot list (m_itemBagEx, a TileList of InventorySlot renderers bound by name) and the buttons; this sends it the
/// tabs, the slot names of the shown tab, the used/total label, the tabs' new marks, the button state and the mesos.
/// <para>A slot's contents are InvenSlot::vfunc_8 (invenslot.cpp:1592), ASSlot::FUN_1409c3860 over the item and its item
/// data; that path is not here yet, so every slot is pushed as an empty one (BindData::PushBinding's null).</para>
/// <para>Nothing here sends a packet: the sort, the slot expansion, the bank call and item use are raised as
/// <see cref="Button"/> / <see cref="ToggleDialog"/> for the host to leave unsent.</para>
/// </summary>
public sealed partial class InventoryController : UiController, IInventoryBinding, IInvenSlotOwner
{
    public override string Movie => "uiinventorydialog";
    protected override string EntityMethod => "changeMoney";
    public override string TypeName => "InventoryDialog";      // GetTypeName @0x141997190

    /// <summary>m_188: the slot bag's name (GetTypeName); its slots are "InventoryDialog.&lt;i&gt;" (FUN_1422cc860 "%s.%d").</summary>
    public const string BagName = "InventoryDialog";

    /// <summary>The inventory the dialog shows.</summary>
    public InventoryView View;
    /// <summary>FUN_140a66510 / FUN_140a66750: a tab's base slot count and its expand maximum (<see cref="BagSlots"/>).</summary>
    public Func<int, int> BaseSlots = _ => BagSlots.Default, MaxSlots = _ => BagSlots.Default;
    /// <summary>The local player's record (MyPC): its equipment answers the hover's compare and "equipped" line; null: no
    /// equipment is known (nothing is compared or marked equipped).</summary>
    public Func<PcInfo> Wearer;
    /// <summary>PCBase::IsDead of the local player (the live MyCharacter.Dead); unset: false.</summary>
    public Func<bool> LocalPlayerDead = () => false;
    /// <summary>CUIService m_n128 (FUN_141adcb40): a Shift key is held (DIK 0x2a / 0x36, uiscenegame.cpp:1128).</summary>
    public Func<bool> ShiftHeld = () => false;
    /// <summary>The local player state the tooltip's owned counts read (g_143ab2a90 and PC::CanUseItem); null counts none.</summary>
    public Func<Skills.SlotWorld> SlotWorld;
    /// <summary>CInputKeyHandler::IsActionKeyDown(action, 0, 0) (FUN_14025e7c0): the keys of the action are held. Actions read
    /// here: 0x49406 (MS2DragSlot::vfunc_19, the right-click preview) and 0x4940d (FUN_1414056b0, the skin tab put-on).</summary>
    public Func<int, bool> ActionKeyDown = _ => false;
    /// <summary>DAT_143af62d0, initData's second argument (the dev mode: the auto pick-up option button enabled).</summary>
    public bool DevMode;

    /// <summary>CUIService::ToggleDialog(id) of a button: item break 0x4e8d, item lock 0x4f08, socket and gem 0x4f0a, enchant 0x4e91.</summary>
    public event Action<int> ToggleDialog;
    /// <summary>A button whose client path sends a request or opens a part not here (by its callback name): OnClickSort,
    /// onClickSlotExpand, onClickOpenBank, onClickItemOption, onClickItemMerge, onClickItemEnchantTransform,
    /// onClickUnlimitedEnchant, onClickItemRemakeOption, OnItemSearch, notifySlotUse (the double click, the preview and the
    /// right-click modes other than put-on), notifySlotDragEnd (a drop off the bag, a drop from a kind of slot not ported).</summary>
    public event Action<string> Button;

    /// <summary>m_1f0 (BuildTabList @0x14199d1c0): the tab buttons in order, by name and tab.</summary>
    readonly List<(string Name, int Tab)> tabs = new();
    int slotCount;          // the slots of the bag built last (GetSlotCount of m_194 then)

    bool Feature(int id) => Model.IsFeatureEnabled(id);
    string Localize(string key) => Host.Player.Translate?.Invoke(key) ?? key;

    /// <summary>
    /// OnLoadComplete @0x141997dd0: UIDialog's, m_itemBagEx (a load error without it), UpdateButtonState, the search input's change
    /// handler, the tab bar (FUN_14199ca90), initData(GetTabButtonIndex(m_194), DAT_143af62d0), the new marks and the
    /// expand label pending, the mesos (vfunc_oa0_11 with DAT_143ab2898). Then UISwfEntityManager::vfunc_9 calls vfunc_46
    /// (uiswfentitymanager.cpp:554-561).
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Host.Child("m_itemBagEx") == null) { Host.RT.NoteMissing("inventory: m_itemBagEx"); return; }
        UpdateButtonState();
        // the search input's change handler (OnSearchTextChanged @0x14199a620) lists the tab's item names: needs the item names
        BuildTabs();
        if (View != null)
        {
            View.TabButtons = TabButtons();
            View.Bind(this, View.Tab, DevMode);
        }
        SetMesos(Pc.Mesos);
        RefreshFeatureButtons();
    }

    /// <summary>vfunc_46 @0x1419994e0: the item break button's tooltip, the buttons hidden by their features (socket and gem
    /// 0x2e, merge 0x6f, open bank 0x75, enchant transform 0xa6, unlimited enchant 0x10d), then updateButtonPos.</summary>
    void RefreshFeatureButtons()
    {
        if (Host.Child("m_itemBreak") is { } brk) SetTooltipText(brk, GetItemBreakTooltip());
        foreach (var (feature, name) in new[] { (0x2e, "m_itemSocketAndGem"), (0x6f, "m_itemMerge"), (0x75, "m_openBank"), (0xa6, "m_itemEnchantTransform"), (0x10d, "m_unlimitedEnchant") })
            if (!Feature(feature) && Host.Child(name) is { } b) Host.SetVisible(b, false);
        Host.Invoke("updateButtonPos");
    }

    /// <summary>vfunc_67 @0x141997330: SetNew(0) on every item of all 15 tabs (FUN_14199be00), the bag dropped, UIDialog's
    /// close, m_19c = 0.</summary>
    public override void Close()
    {
        if (View != null)
            for (int t = 0; t < ItemBook.Tabs; t++) View.ClearNewInTab(t);
        slotCount = 0;
        base.Close();
        View?.Unbind();
    }

    /// <summary>vfunc_15 @0x141997580: the pending new marks, expand label and tab rebuild (the view's flags).</summary>
    public void Process() => View?.Update();

    public override void OnStatus(StatusEvent e)
    {
        if (e == StatusEvent.Meso) SetMesos(Pc.Mesos);
    }

    /// <summary>vfunc_oa0_11 @0x1419976e0: m_meso's text, the amount with separators (FUN_1409404f0).</summary>
    public void SetMesos(long mesos)
    {
        if (Host.Child("m_meso") is { } t) Host.Set(t, "text", NumberText.Group(mesos.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>BuildTabList @0x14199d1c0: the tabs in button order; piece with feature 0xed, gem 0x2e, pet 0x8c, lapenshard 0xd3.</summary>
    void BuildTabList()
    {
        tabs.Clear();
        tabs.Add(("equip", 0)); tabs.Add(("skin", 1)); tabs.Add(("summon", 2)); tabs.Add(("material", 3));
        if (Feature(0xed)) tabs.Add(("piece", 14));
        tabs.Add(("mastery", 8)); tabs.Add(("life", 4)); tabs.Add(("quest", 5));
        if (Feature(0x2e)) tabs.Add(("gem", 6));
        if (Feature(0x8c)) tabs.Add(("pet", 9));
        if (Feature(0xd3)) tabs.Add(("lapenshard", 13));
        tabs.Add(("activeskill", 10)); tabs.Add(("coin", 11)); tabs.Add(("badge", 12)); tabs.Add(("misc", 7));
    }

    /// <summary>m_1a8: the button index of each of the 15 tabs, -1 for a tab without a button.</summary>
    int[] TabButtons()
    {
        var a = Enumerable.Repeat(-1, ItemBook.Tabs).ToArray();
        for (int i = 0; i < tabs.Count; i++) a[tabs[i].Tab] = i;
        return a;
    }

    public IReadOnlyList<(string Name, int Tab)> Tabs => tabs;

    /// <summary>FUN_14199ca90: m_tab's dataProvider, per tab {label "$s_inventory_tab_&lt;name&gt;", tooltipData {className
    /// export_common_tooltip_tooltipText, id "$s_tooltip_inven_&lt;name&gt;", align topLeft}, tabIndex}, and its
    /// clikIndexChange handler (ASButtonBar::AddIndexChangeHandler) OnTabChanged.</summary>
    void BuildTabs()
    {
        var bar = Host.Child("m_tab");
        if (bar == null) return;
        BuildTabList();
        var items = new List<object>();
        foreach (var (name, tab) in tabs)
            items.Add(Host.Obj(("label", "$s_inventory_tab_" + name),
                ("tooltipData", Host.Obj(("className", "export_common_tooltip_tooltipText"), ("id", "$s_tooltip_inven_" + name), ("align", "topLeft"))),
                ("tabIndex", tab)));
        Host.Set(bar, "dataProvider", DataProvider(items));
        Func<object[], object> onChange = a => { RT(() => OnTabChanged(a.Length > 0 ? a[0] : null)); return null; };
        Host.Invoke(bar, "addEventListener", "clikIndexChange", onChange, false, 0, false);
    }

    void RT(Action a) => Host.RT.Guard("inventory tab", a);

    /// <summary>OnTabChanged @0x14199a4b0: a valid index with data sets m_194 to its tabIndex, m_19c = 7, UpdateButtonState.</summary>
    void OnTabChanged(object ev)
    {
        if (ev == null || Rt.ToNumber(Host.Get(ev, "index")) < 0) return;
        var data = Host.Get(ev, "data");
        if (Rt.IsNullish(data)) return;
        View?.SelectTab((int)Rt.ToNumber(Host.Get(data, "tabIndex")));
        UpdateButtonState();
    }

    /// <summary>The item lock dialog (0x4f08) / the item break dialog (0x4e8d) / the donation dialog (0x4e8e) is open
    /// (UIDialog::vfunc_55 == 0 of each in UpdateButtonState). Set by the host; closed when unset.</summary>
    public Func<bool> LockDialogOpen = () => false, BreakDialogOpen = () => false, DonationDialogOpen = () => false;
    /// <summary>A lock (0x4f08) or break (0x4e8d) dialog exists (UpdateButtonState's lookups, @0x141999f60 uiinventorydialog.cpp:1150-1154:
    /// all of lock, break and the two extraction dialogs null = return before the push). The host sets it to "its controller was
    /// created"; unset means none exists. STILL-OPEN: the extraction scroll dialogs (0x4f49, 0x4f4a) are not in this HUD, so they
    /// are always null here.</summary>
    public Func<bool> LockOrBreakDialogExists = () => false;
    /// <summary>m_1a4 (UpdateButtonState): 0 none, 1 break, 2 donation, 3 lock open (the last one found open wins, in that order).
    /// STILL-OPEN: 4 and 5, the item and pet extraction scroll dialogs (0x4f49, 0x4f4a), are not in this HUD.</summary>
    public int ButtonMode { get; private set; }

    /// <summary>
    /// UpdateButtonState @0x141999f60. bAutoPickup: level &gt;= 1. bOpenBank: PC bang or VIP (FUN_141658f30 || IsVip); neither
    /// state is here, so 0. An open lock dialog turns bItemBreak off (m_1a4 3); an open break dialog (1) or donation dialog (2)
    /// turns bItemLock off. None of lock, break and the extraction scrolls (0x4f49 / 0x4f4a, not in this HUD) existing returns
    /// before the push (uiinventorydialog.cpp:1152, <see cref="LockOrBreakDialogExists"/>). Public: the break and lock dialogs call it after
    /// their load and close (uiinventorydialog.cpp:2347, 2359).
    /// </summary>
    public void UpdateButtonState()
    {
        bool bank = false;
        int itemBreak = 1, itemLock = 1;
        ButtonMode = 0;
        if (!LockOrBreakDialogExists()) return;
        if (LockDialogOpen()) { ButtonMode = 3; itemBreak = 0; }
        if (BreakDialogOpen()) { ButtonMode = 1; itemLock = 0; }
        if (DonationDialogOpen()) { ButtonMode = 2; itemLock = 0; }
        Host.Invoke("updateButtonState", Host.Obj(
            ("bAutoPickup", Pc.Level >= 1 ? 1 : 0),
            ("bItemEchant", true),
            ("bItemBreak", itemBreak),
            ("itemBreakToolTip", GetItemBreakTooltip()),
            ("bItemLock", itemLock),
            ("bRemakeOption", true),
            ("bUnlimitedEnchant", true),
            ("bGem", true),
            ("slotExpandToolTip", GetExpandTooltip()),
            ("bOpenBank", bank ? 1 : 0),
            ("openBankTooltip", GetOpenBankTooltip(bank))));
    }

    /// <summary>UpdateButtonState's lookups (uiinventorydialog.cpp:1152): the lock (0x4f08) or break (0x4e8d) dialog was created.
    /// The donation dialog (0x4e8e) is not in the test, so a break controller that is the donation one does not count.</summary>
    public static bool LockOrBreakExists(ItemLockDialogController itemLock, ItemBreakDialogController itemBreak)
        => itemLock != null || itemBreak?.Id == ItemBreakDialogController.UiId;

    /// <summary>What the item lock and break dialogs do to the inventory after their load and unload (uiinventorydialog.cpp:2340-2362,
    /// uiitemlockdialog.cpp:160-185): nothing while it is closed, else UpdateButtonState, then FUN_141999e00.</summary>
    public void ItemDialogChanged()
    {
        if (IsClosed) return;
        UpdateButtonState();
        AddFlags();
    }

    /// <summary>FUN_141999e00 (@0x141999e00): m_19c = 7, the new marks, the expand label and the tab rebuild pending again.</summary>
    public void AddFlags() => View?.MarkAllPending();

    /// <summary>RefreshItemSlot @0x14199a3f0: not while closed; the shown tab's slot holding the item is pushed again
    /// (InventorySlotBag::RefreshSlotForItem).</summary>
    public void RefreshItemSlot(long uid)
    {
        if (IsClosed || View == null || View.FindByUid(uid) == null) return;
        var cells = View.Cells(View.Tab);
        for (int i = 0; i < cells.Count; i++)
            if (!cells[i].Empty && cells[i].Uid == uid) { Host.Push(SlotName(i)); return; }
    }

    /// <summary>GetItemBreakTooltip @0x14199d9c0: with m_itemBreak enabled or level &gt;= 20 the break tooltip (+ the skin line
    /// with feature 0x3a, the socket line with 0x2e), else the disabled one.</summary>
    string GetItemBreakTooltip()
    {
        if (Host.Child("m_itemBreak") is not { } b) return "";
        if (Rt.ToBoolean(Host.Get(b, "enabled")) || (ushort)Pc.Level >= 0x14)
        {
            string s = Localize("$s_inventory_item_break_tooltip");
            if (Feature(0x3a)) s += "\n" + Localize("$s_itembreak_tooltip_skin");
            if (Feature(0x2e)) s += "\n" + Localize("$s_itembreak_tooltip_socket");
            return s;
        }
        return Localize("$s_inventory_item_break_disable_tooltip");
    }

    /// <summary>GetExpandTooltip @0x14199ddd0: "$s_inventory_slotexpand_tooltip" with 6 and FUN_140a66510 + FUN_140a66750 of m_194.</summary>
    string GetExpandTooltip()
    {
        int tab = View?.Tab ?? 0;
        return Format(Localize("$s_inventory_slotexpand_tooltip"), 6, BaseSlots(tab) + MaxSlots(tab));
    }

    /// <summary>GetOpenBankTooltip @0x14199deb0: the open text, or the disabled one (+ the PC bang line with feature 0x3c, the
    /// VIP line with 0x4a).</summary>
    string GetOpenBankTooltip(bool bank)
    {
        if (Host.Child("m_openBank") == null) return "";
        if (bank) return Localize("$s_inventory_open_bank_tooltip");
        string s = Localize("$s_inventory_open_bank_disable_tooltip");
        if (Feature(0x3c)) s += "\n" + Localize("$s_inventory_open_bank_tooltip_pcbang");
        if (Feature(0x4a)) s += "\n" + Localize("$s_inventory_open_bank_tooltip_vip");
        return s;
    }

    /// <summary>ASDataProvider (asdataprovider.cpp:18): a scaleform.clik.data.DataProvider with the values pushed.</summary>
    object DataProvider(IEnumerable<object> values)
    {
        var dp = Host.RT.Construct(Host.RT.GetClass("scaleform.clik.data.DataProvider"), Array.Empty<object>());
        foreach (var v in values) Host.Invoke(dp, "push", v);
        return dp;
    }

    /// <summary>ASUIComponent::SetTooltipText @0x1422c7b40: the text into an existing _tooltipData, else tooltipData = {id: text}.
    /// (Its RegisterTooltip, the tooltip manager's side, is the tooltip hosting's.)</summary>
    void SetTooltipText(object c, string text)
    {
        var data = Host.Get(c, "_tooltipData");
        if (!Rt.IsNullish(data)) Host.Set(data, "text", text);
        else Host.Set(c, "tooltipData", Host.Obj(("id", text)));
    }

    /// <summary>StringFormat: "{n}" replaced by the n-th argument.</summary>
    static string Format(string f, params object[] args)
    {
        for (int i = 0; i < args.Length; i++) f = f.Replace("{" + i + "}", Convert.ToString(args[i], System.Globalization.CultureInfo.InvariantCulture));
        return f;
    }

    /// <summary>CContentShutdown::Get(0x39) == 1 (the item merge is shut down). Default: not shut down.</summary>
    public Func<bool> MergeShutdown = () => false;
    /// <summary>MyPC::vfunc_65 == 1 (the local player is in the state that refuses a forced talk).</summary>
    public Func<bool> TalkRefused = () => false;

    /// <summary>onClickItemMerge (uiinventorydialog.cpp:920-943): the shutdown notice 0x2f7, the battle-state notice, then the
    /// merge smith's talk and the option dialog 0x4e68. STILL-OPEN: the talk script (MergeSmithScriptID, script 0xa7e618,
    /// BeginTalkWithoutNpc) has no counterpart here; the dialog opens directly.</summary>
    void OnClickItemMerge()
    {
        if (MergeShutdown()) { NoticeText?.Invoke(Model.Tables.Common(0x2f7) ?? ""); return; }
        if (TalkRefused()) { NoticeText?.Invoke(Model.Tables.Common("s_error_talk_battle_state_force_talk") ?? ""); return; }
        ToggleDialog?.Invoke(0x4e68);
    }

    /// <summary>
    /// vfunc_28 @0x141998680 after UIDialog's. The sort is the client's; the buttons that open a dialog toggle it; the rest
    /// send a request (the slot expansion's box and request, the bank call) or open a part not here, and are only raised.
    /// </summary>
    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onclickitembreak": ToggleDialog?.Invoke(0x4e8d); return true;
            case "onclickitemlock": ToggleDialog?.Invoke(0x4f08); return true;
            case "onclickitemsocketandgem": ToggleDialog?.Invoke(0x4f0a); return true;
            case "onclickitemenchant": ToggleDialog?.Invoke(0x4e91); return true;           // OnClickItemEnchant @0x14199aaa0
            case "onclicksort": OnClickSort(); return true;
            case "notifyslotuse": return SlotUse(args);
            case "notifyslotshiftclick": return SlotShiftClick(args);
            case "onclickitemmerge": OnClickItemMerge(); return true;
            case "onclickitemoption": case "onclickitemenchanttransform": case "onclickunlimitedenchant":
            case "onitemsearch": case "onclickslotexpand":
                Button?.Invoke(name); return true;
            // these two fall through to "return 0" in the client (:945-951, :979-981)
            case "onclickitemremakeoption": case "onclickopenbank":
                Button?.Invoke(name); return false;
            // the movie raises these too; UIInventoryDialog::vfunc_28 (@0x141998680, :832-981) has no branch for them and UIDialog's
            // does not either, so the client answers 0 and nothing happens: onClickMesoDrop (the meso drop button; the drop is the
            // slot drag's, notifySlotDragEnd) and OnSelected (a pick in the search input's candidate list; the search itself runs
            // from OnItemSearch, :952, and the candidates from OnSearchTextChanged, :1232).
            case "onclickmesodrop": case "onselected":
                return false;
        }
        return false;
    }

    /// <summary>
    /// A callback's int argument (NotifyDragSlot::SlotUse's button, notifydragslot.cpp:42). The client's Value::GetInt
    /// (gfx_value.h:237) returns the union's IValue without a type check, so only an int Value (VT_Int) has a defined
    /// result; another type reads other bits of the union (a double's low word, a string's pointer). Here a number is
    /// truncated to an int, and anything else (NaN or infinite) is 0. That last case is the port's own choice; there is
    /// no client value to match.
    /// </summary>
    internal static int ArgInt(object v)
    {
        double d = Rt.ToNumber(v);
        return double.IsNaN(d) || double.IsInfinity(d) ? 0 : (int)d;
    }

    /// <summary>The bag slot index of a slot name of the shown bag, -1 for another name.</summary>
    int SlotIndexOf(object name) =>
        name is string n && n.StartsWith(BagName + ".", StringComparison.Ordinal) && int.TryParse(n.AsSpan(BagName.Length + 1), out int i)
        && i >= 0 && i < slotCount ? i : -1;

    // ---- the bag's drag slots (InvenSlot), through NotifyDragSlot ----------------------------------------------------

    /// <summary>The slots of the shown bag are its drag slots.</summary>
    public bool IsDragSlot(string name) => SlotIndexOf(name) >= 0;

    /// <summary>DragSlot::vfunc_10: the slot pushed again.</summary>
    public void RefreshSlot(string name) { if (SlotIndexOf(name) is int i and >= 0) Host.Push(SlotName(i)); }

    /// <summary>
    /// InvenSlot::vfunc_20 (invenslot.cpp:1299) for a slot with an item: the item's new flag cleared (SetNew(0)). Its
    /// material sound and UIDialogEventDispatcher's vfunc_3 are not here.
    /// </summary>
    public void SlotDragStarted(string name)
    {
        if (SlotIndexOf(name) is int i and >= 0 && View?.ItemAt(View.Tab, i) is { } e) View.ClearNew(e.Uid);
    }

    /// <summary>
    /// InvenSlot::vfunc_18 (invenslot.cpp:1141), something dropped on a slot of this bag. A slot of this bag: the move
    /// request (mode 3: the source slot's item uid, 0 for an empty cell as m_nItemUid, and this slot's index), then the
    /// source's vfunc_10 (pushed again: nothing moves until the server's item packets do). The character window's equip
    /// slot (EquipSlot, FUN_14120d560): <see cref="WornDroppedOnBag"/>. Any other source (the store slot branch and the
    /// rest) is not ported: raised. The item sound (PlayItemSoundByUid) is not here.
    /// </summary>
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner)
    {
        int to = SlotIndexOf(name);
        if (to < 0) return;
        if (sourceOwner == this && SlotIndexOf(source) is int from and >= 0)
        {
            long uid = View?.ItemAt(View.Tab, from)?.Uid ?? 0;
            View?.RequestMove?.Invoke(uid, (short)to);
            RefreshSlot(source);
        }
        else if (sourceOwner is IEquipSlotOwner equips && equips.IsEquipSlot(source))
        {
            if (equips.WornSlot(source) is { } worn) WornDroppedOnBag(worn.Item, worn.Slot, worn.Kind, to, equips);
        }
        else if (sourceOwner is IItemLockSlotOwner lk && lk.LockSlotUid(source) is long old and not 0 && LockSlotDropped != null)
        {
            Item mine = View?.ItemAt(View.Tab, to) is { } e && Model != null ? ToItem(e, Model.Tables.Items) : null;
            LockSlotDropped(old, mine);
            sourceOwner.RefreshSlot(source);
        }
        else if (sourceOwner is IItemBreakSlotOwner bk && bk.BreakSlotUid(source) is long oldB and not 0 && BreakSlotDropped != null)
        {
            Item mine = View?.ItemAt(View.Tab, to) is { } e && Model != null ? ToItem(e, Model.Tables.Items) : null;
            BreakSlotDropped(oldB, mine);
            sourceOwner.RefreshSlot(source);
        }
        else Button?.Invoke("notifySlotDragEnd");
    }

    /// <summary>
    /// The branch NotifyDragSlot::SlotDragEnd picks by FUN_1422aeed0(2), vfunc_12 or vfunc_17 (InvenSlot::vfunc_17,
    /// invenslot.cpp:1109, the drop-the-item path), is open: the slot is pushed again and the callback raised.
    /// </summary>
    public void SlotDropUnresolved(string name)
    {
        RefreshSlot(name);
        Button?.Invoke("notifySlotDragEnd");
    }


    /// <summary>
    /// InvenSlot::vfunc_18's EquipSlot branch (invenslot.cpp:1153): the target cell's item, when its model has the worn item's
    /// group type (CItem::FUN_140527a20: m_68 for equip subtype 0x1f / 0x22, else the model's first group), is put on at that
    /// slot with the equip slot's kind: RequestEquipItem(mine, slot, kind, 0) (<see cref="RequestEquip"/>); else, when the
    /// shown tab (FUN_140a65b70) is the worn item's tab, the worn item is taken off (RequestUnequipItem, with the character window's checks: <see cref="IEquipSlotOwner.RequestUnequip"/>). Nothing moves here.
    /// The source's vfunc_21 (its sound) is not here.
    /// </summary>
    void WornDroppedOnBag(Item worn, int wornSlot, int kind, int target, IEquipSlotOwner equips)
    {
        if (View == null || Model == null) return;
        var models = Model.Tables.ItemModels;
        int group = wornSlot != 0 && worn.Data.EquipSubtype is 0x1f or 0x22 ? wornSlot : models.Get(worn.Data.ItemPreset)?.FirstSlotType ?? 0;
        var mine = View.ItemAt(View.Tab, target) is { } e ? ToItem(e, Model.Tables.Items) : null;
        if (mine != null && models.Get(mine.Data.ItemPreset)?.HasSlotType(group) == true) RequestEquip(mine, group, kind);
        else if (View.Tab == View.TabOfItem(worn.Data.Id)) equips.RequestUnequip(worn);
    }

    /// <summary>FUN_140d2cfc0 (chatlinkhelpers.cpp:578): the item linked into the chat input (the chat log's
    /// <see cref="MessengerBrowser.LinkItem(Item)"/>); null leaves it out.</summary>
    public Action<Item> LinkItem;

    /// <summary>
    /// NotifyDragSlot::SlotShiftClick (notifydragslot.cpp:63) for a slot of this bag: InvenSlot::vfunc_15 (invenslot.cpp:1099)
    /// links the slot's item (FindItemByUid of m_nItemUid, then FUN_140d2cfc0: <see cref="LinkItem"/>), then
    /// DragSlot::vfunc_15 (dragslot.cpp:80) is vfunc_10, the slot pushed again. No name does nothing; another name is
    /// raised (notifySlotShiftClick).
    /// </summary>
    bool SlotShiftClick(object[] args)
    {
        if (args.Length < 1 || args[0] == null || args[0] is Ms2.Avm2.Undefined) return true;
        int i = SlotIndexOf(args[0]);
        if (i < 0) { Button?.Invoke("notifySlotShiftClick"); return true; }
        if (View?.ItemAt(View.Tab, i) is { } e && e.Uid != 0 && FindItem(e.Uid) is { } item) LinkItem?.Invoke(item);
        RefreshSlot(SlotName(i));
        return true;
    }

    /// <summary>
    /// NotifyDragSlot::SlotUse (notifydragslot.cpp:33) for a slot of this bag: the button is the second argument (0 without
    /// one). Button 0, the double click: nothing while Shift is held (GetKeyState(VK_SHIFT)), else <see cref="DoubleClick"/>.
    /// Button 1, the right click: MS2DragSlot::vfunc_19 (ms2dragslot.cpp:60; the slot is enabled) with action 0x49406 held is
    /// the preview (vfunc_16, not ported: raised), else <see cref="RightClick"/>. Other names are raised (notifySlotUse). The
    /// client guards neither against a repeat: each call goes through.
    /// </summary>
    bool SlotUse(object[] args)
    {
        int i = args.Length > 0 ? SlotIndexOf(args[0]) : -1;
        int button = args.Length > 1 ? ArgInt(args[1]) : 0;
        if (i >= 0 && button == 0)
        {
            if (!ShiftHeld()) DoubleClick(i);
            return true;
        }
        if (i < 0 || button != 1 || ActionKeyDown(0x49406)) { Button?.Invoke("notifySlotUse"); return true; }
        RightClick(i);
        return true;
    }

    /// <summary>
    /// InvenSlot::vfunc_14 (invenslot.cpp:904): none for an empty cell; the item's new flag cleared (SetNew(0), FUN_14199bc00);
    /// then by FUN_14140d3e0 (<see cref="UseMode"/>): mode 1 is <see cref="TryAddToTrade"/>, mode 6 puts it on, FUN_140a621c0(uid, 0, FUN_1414056b0) =
    /// RequestEquipItem(item, 0, layer, 0) (<see cref="RequestEquip"/>). FUN_1414056b0 (:601): 0 for a non-skin item; a skin
    /// item 1, or with feature 0x8e 2 unless action 0x4940d is held. The other modes (7 the quick slot, 12 the gem, 15 the
    /// UGC rename) are not ported and are raised.
    /// </summary>
    void RightClick(int i)
    {
        if (View?.ItemAt(View.Tab, i) is not { } e || Model == null) return;
        var item = ToItem(e, Model.Tables.Items);
        if (item == null) return;
        View.ClearNew(e.Uid);
        int mode = UseMode(item, false);
        if (mode == 1) { TryAddToTrade(item); return; }
        if (mode == 2) { TrySellItem(item); return; }
        if (mode == 3 && BankDeposit != null) { BankDeposit(item); return; }
        if (mode is 8 or 9 && BreakAdd != null) { BreakAdd(item); return; }              // cases 8, 9: the open break dialog lists it
        if (mode == 10 && LockSelect != null) { LockSelect(item); return; }       // case 10: the open lock dialog selects it
        if (mode == 17 && RevertSelect != null) { RevertSelect(item); return; }    // case 17: the open merge revert dialog checks and shows it
        if (mode != 6) { Button?.Invoke("notifySlotUse"); return; }
        int layer = !item.Data.HasSkinType ? 0 : !Feature(0x8e) ? 1 : ActionKeyDown(0x4940d) ? 1 : 2;
        RequestEquip(item, 0, layer);
    }

    /// <summary>
    /// The functions with a handler of their own (RegisterItemUseFuncs, unit_140d1ae30_itemfunctiondispatch.cpp:373): UseItem
    /// runs it in place of the plain use request. None is ported: such an item's use is raised.
    /// </summary>
    public static readonly IReadOnlySet<string> OwnUseFunctions = new HashSet<string>(StringComparer.Ordinal)
    {
        "OpenItemBox", "OpenItemBoxWithKey", "OpenDungeonItemBox", "CallAirTaxi", "CallMedic", "CallBlackMarket", "OpenBank",
        "LevelPotion", "OpenInstrument", "ExpandInven", "ChangeCharName", "ChangeGuildName", "ChangeGender", "ResetAbilityCoolTime",
        "RecallGuild", "RecallWedding", "RecallParty", "EnchantScroll", "FishingRod", "UGCMapPackage", "SelectItemBox",
        "OpenItemBoxLullu", "OpenItemBoxLulluSimple", "OpenPetBox", "ItemRemakeScroll", "ItemRePackingScroll", "PetTraining",
        "OpenWebPage", "ItemSocketScroll", "ItemExchangeScroll", "ItemExtraction", "PetExtraction", "QuestScroll", "ItemMergeRevert",
        "OpenMassive", "ItemActiveCodiTab", "ItemChangeBeauty", "InstallBillBoard", "OpenCoupleEffectBox", "SuperWorldChat",
        "WeddingChat", "TreasureMap", "CreateReactor", "FestivalEvent", "BlueprintImport", "ChatEmoticonAdd",
        "OpenItemBoxLimitedQuantity", "OpenGachaBox", "QuestStart",
    };

    /// <summary>The common strings the use checks show (commonstringtable.cpp; the level and job ones by key).</summary>
    public const string UseLevelKey = "s_err_not_enough_level";

    /// <summary>
    /// InvenSlot::vfunc_13 (invenslot.cpp:765): no local player or an empty cell, nothing. A cube texture item (UGC edit) and a
    /// music score item (the score dialogs) come first and are not ported: raised. An item with a function: FUN_140a661e0
    /// (always 1), FUN_14140dea0 (:2082) on the record: CItem::FUN_140528980 false (item.cpp:1717: expired, not a pet, no maid
    /// id): box 225 (0xe1) when expired; the level: ShowMessageBox(s_err_not_enough_level) (:2106, a box here: CanUseItem's
    /// notice comes after it); the job: s_item_err_disable_job; the gender: box 45. Then TryUseItem (:532) and
    /// <see cref="UseItem"/>. An item without a function: a shop id, a pet, an emotion and an equipment item (type 1) are
    /// not ported (raised); type 0x13 does nothing; others pass FUN_14140dea0 and go to <see cref="UseItemSkill"/>.
    /// STILL-OPEN: the cube craft placement (MyPC FUN_141659690), the PC bang (0xe2), VIP, wedding and owner-bind (0xfe)
    /// checks, TryUseItem's use cooldown (FUN_141409060, box 0x55). Without the wearer's record the requirement checks are
    /// skipped.
    /// </summary>
    void DoubleClick(int i)
    {
        if (Model == null || !Model.HasLocalPlayer || View?.ItemAt(View.Tab, i) is not { } e) return;
        var item = ToItem(e, Model.Tables.Items);
        if (item == null) return;
        var d = item.Data;
        if (d.HasCubeTextureMesh || d.IsScoreItem) { Button?.Invoke("notifySlotUse"); return; }
        if (!d.HasFunction)
        {
            // :853-893: a shop id, a pet, an emotion (type 0x18) and a type 1 item have their own handlers (raised); type 0x13 none
            if (d.ShopId > 0 || d.IsPetItem || d.Type is 0x18 or 1 or 0x13 || UseItemSkill == null) { Button?.Invoke("notifySlotUse"); return; }
            if (UseChecks(item)) UseItemSkill(item);    // :894-897, FUN_14053c8b0
            return;
        }
        if (UseChecks(item)) UseItem(item, false);
    }

    /// <summary>FUN_14140dea0 (invenslot.cpp:2082) on the record: box 225 for an expired item that is not a pet and has no maid
    /// id; the level, job and gender boxes. Without the wearer's record only the expiry is checked.</summary>
    bool UseChecks(Item item)
    {
        var d = item.Data;
        var record = Wearer?.Invoke();
        bool expired = item.ExpiryTime > 0 && item.ExpiryTime <= View.ServerTime();
        if (expired && !d.IsPetItem && !d.HasMaidId) { ShowCommonBox(ExpiredKey); return false; }
        if (record != null && !d.IsLevelAllowed(record.Level)) { ShowCommonBox(UseLevelKey); return false; }
        if (record != null && !d.IsJobAllowed(record.JobGroup)) { ShowCommonBox(DisabledJobKey); return false; }
        if (record != null && !d.IsGenderAllowed(record.Gender)) { ShowCommonBox(GenderKey); return false; }
        return true;
    }

    /// <summary>Unk_Inventory::FUN_140a635f0 (inventory.cpp:1379), the double click of an item without a function that is
    /// not type 0x13: a type 2, skill (5) or ride (9) item casts its item skill (FUN_141769320) at level 1 with the item uid on
    /// the record; other types do nothing there. Unset: raised (notifySlotUse).</summary>
    public Action<Item> UseItemSkill;

    /// <summary>The common string GetStringCommon(0xea) (commonstringtable.cpp:1851): CanUseItem's box for an item
    /// FUN_140528980 refuses that has not expired.</summary>
    public const string InvalidUseKey = "s_item_err_puton_invalid";

    /// <summary>CItem::FUN_140528980 (item.cpp:1717): an expired item that is not a pet and has no maid id is not usable.
    /// STILL-OPEN: the music score part (a score item: FUN_140525730 / FUN_140527340 and its CItemCustomMusicScore's m_n38),
    /// the one way to the 0xea box: the score block is read past, not kept.</summary>
    static bool Usable(Item item, bool expired) => !(expired && !item.Data.IsPetItem && !item.Data.HasMaidId);

    /// <summary>
    /// CanUseItem (unit_140d1ae30_itemfunctiondispatch.cpp:430): no local player, false; CItem::FUN_140528980 false: box 225
    /// (0xe1) when expired, else box 0xea; the level: ShowSystemNotice(s_err_not_enough_level, 1, 1) (:461), a notice, not a
    /// box; the job: ShowMessageBox(s_item_err_disable_job); the gender: box 45 (0x2d).
    /// STILL-OPEN: the skill cooldown (the skill system's FUN_141765fe0 of the item's skill, box 0x55), the PC bang (0xe2), VIP,
    /// wedding and owner-bind (0xfe) checks: their player states are not in the port. Without the wearer's record the
    /// requirement checks are skipped.
    /// </summary>
    bool CanUseItem(Item item)
    {
        if (Model == null || !Model.HasLocalPlayer) return false;
        var d = item.Data;
        bool expired = item.ExpiryTime > 0 && item.ExpiryTime <= (View?.ServerTime() ?? 0);
        if (!Usable(item, expired)) { ShowCommonBox(expired ? ExpiredKey : InvalidUseKey); return false; }
        var record = Wearer?.Invoke();
        if (record != null && !d.IsLevelAllowed(record.Level)) { SystemNotice?.Invoke(UseLevelKey); return false; }
        if (record != null && !d.IsJobAllowed(record.JobGroup)) { ShowCommonBox(DisabledJobKey); return false; }
        if (record != null && !d.IsGenderAllowed(record.Gender)) { ShowCommonBox(GenderKey); return false; }
        return true;
    }

    /// <summary>
    /// UseItem(item, bConfirmed) (unit_140d1ae30_itemfunctiondispatch.cpp:491): <see cref="CanUseItem"/>; FUN_140a661e0 (always
    /// 1); unless confirmed, an item that binds on use and is not bound (<see cref="NeedsBindConfirm"/>) asks first: box 0x11
    /// with s_msg_transfer_bind_when_use formatted with the player's name (vfunc_77, FUN_14028dfa0), tag
    /// <see cref="BindOnUseBox"/>, the item uid and the item entry (its name coloured, count 0), the yes / no labels
    /// (0x477 / 0x478), shown; its yes comes back as <see cref="ConfirmBindUse"/>. Then a function of
    /// <see cref="OwnUseFunctions"/> has its own handler (raised); else FUN_140a63880 = <see cref="RequestUseItem"/>(uid, "").
    /// Nothing is used up here: the server's item packets do.
    /// STILL-OPEN: the function shutdown list (CGameEventValueManager::ShutdownFunctionItem()->IsShutdown of the item id,
    /// notice 0x2f7) and the item sound.
    /// </summary>
    internal bool UseItem(Item item, bool confirmed)
    {
        if (!CanUseItem(item)) return false;
        var d = item.Data;
        if (!confirmed && NeedsBindConfirm(item))
        {
            ShowBox(BindBox(item, BindOnUseKey, BindOnUseBox));
            return true;
        }
        if (item.Data.FunctionName == "ItemMergeRevert" && RevertOpen != null) { RevertOpen(item); return true; }   // ItemMergeRevert @0x140d21480
        if (item.Data.FunctionName == "OpenItemBox" && OpenMultiBox != null) return OpenItemBox(item);                // @0x140d259d0
        if (item.Data.FunctionName == "SelectItemBox" && OpenSelectBox != null) return SelectItemBox(item);          // @0x140d22430
        if (OwnUseFunctions.Contains(item.Data.FunctionName)) { Button?.Invoke("notifySlotUse"); return true; }
        RequestUseItem(item, "");
        return true;
    }

    /// <summary>
    /// Unk_Inventory::RequestUseItem (inventory.cpp:1201): a dead local player gets box 66
    /// (<see cref="MyInfoController.DeadKey"/>) unless the function is one of <see cref="NoStateCheckFunctions"/>;
    /// FUN_140a65e30's GetItemUseError (expired without a maid id: box 225); then the use request with the argument.
    /// Returns whether it was sent. The exemption is for the item box handlers (<see cref="OwnUseFunctions"/> holds them),
    /// which reach this later with their own argument; plain uses never have those functions.
    /// </summary>
    public bool RequestUseItem(Item item, string argument)
    {
        if (!NoStateCheckFunctions.Contains(item.Data.FunctionName) && LocalPlayerDead()) { ShowCommonBox(MyInfoController.DeadKey); return false; }   // :1205-1211
        bool expired = item.ExpiryTime > 0 && item.ExpiryTime <= View.ServerTime();
        if (expired && !item.Data.HasMaidId) { ShowCommonBox(ExpiredKey); return false; }       // GetItemUseError 5 (an expired pet)
        View.RequestUse?.Invoke(item.Uid, argument);
        return true;
    }

    /// <summary>RequestUseItem's functions that skip the player state check (checkState 0, inventory.cpp:1205-1207).</summary>
    public static readonly HashSet<string> NoStateCheckFunctions = new(StringComparer.Ordinal) { "OpenItemBox", "OpenItemBoxLullu", "OpenItemBoxLulluSimple" };

    /// <summary>The bind boxes' common strings, by key (FUN_14028dfa0 formats them with the player's name).</summary>
    public const string BindOnUseKey = "s_msg_transfer_bind_when_use", BindOnEquipKey = "s_msg_transfer_bind_when_equip";

    /// <summary>
    /// The bind box of RequestEquipItem (inventory.cpp:1245-1256) and UseItem (unit_140d1ae30_itemfunctiondispatch.cpp:509-518):
    /// CreateMessageBox(0x11 / 17, the key's text with the player's name, no listener, the tag), SetItemUid, SetItemEntry(item,
    /// FUN_140527b10 (the item's name), 1, 0, -1, -1, 1), SetButtonLabelIds(0x477, 0x478, 0). The equip box appends its slot
    /// and mode first.
    /// </summary>
    MessageBoxData BindBox(Item item, string key, string tag)
    {
        string text = Ms2.Quest.ClientFormat.Format(Model.Tables.Common(key) ?? "", Pc.Name);
        var box = MessageBoxQueue.Create(0x11, text, null, tag);
        box.ItemUid = item.Uid;
        box.SetItemEntry(item, new ItemTooltip(Model, 0).Name(item), 1, 0, -1, -1, 1, Model.Tables.Items.TextColor);
        box.SetButtonLabelIds(0x477, 0x478, 0);
        return box;
    }

    /// <summary>HandleUseBindFuncItemButton @0x1419fde80 (uimessagebox.cpp:1868), the yes: FindItemByUid of the box's item uid,
    /// then UseItem(item, 1). False when the inventory no longer has the item (the handler's 0).</summary>
    public bool ConfirmBindUse(long uid)
    {
        if (View?.FindByUid(uid) is not { } e || Model == null) return false;
        var item = ToItem(e, Model.Tables.Items);
        if (item == null) return false;
        UseItem(item, true);
        return true;
    }

    /// <summary>HandleUseBindEquipItemButton @0x1419fdf80 (uimessagebox.cpp:1885), the yes: FindItemByUid of the box's item
    /// uid, then RequestEquipItem(item, arg 0, arg 1, 1), the slot and mode the box kept. False when the inventory no longer
    /// has the item (the handler's 0).
    /// STILL-OPEN: with feature 0xe8, a refused equip of the pending item clears it (FUN_140a66f20 / FUN_140a66f40,
    /// inventory.cpp:1769, the auto-equip pending item): the port has no pending item.</summary>
    public bool ConfirmBindEquip(long uid, uint slot, uint mode)
    {
        if (View?.FindByUid(uid) is not { } e || Model == null) return false;
        var item = ToItem(e, Model.Tables.Items);
        if (item == null) return false;
        RequestEquip(item, (int)slot, (int)mode, true);
        return true;
    }

    /// <summary>FUN_141275260(id, 1, 1) (unit_141274d10_uihelpers.cpp:267) and ShowSystemNotice of a common string: the
    /// notice, by its key.</summary>
    public event Action<string> SystemNotice;

    // ---- trade (InvenSlot::TryAddToTrade, invenslot.cpp:1678) -------------------------------------------------------------

    /// <summary>The trade dialog (0x4e83, Unk_InvGetDialogById&lt;UITradeDialogEx&gt;): its book while it is open, else null.</summary>
    public Func<TradeBook> Trade = () => null;
    /// <summary>The npc shop dialog (0x4e3b, Unk_BreakDialog&lt;UIShopDialog&gt;): its controller while it is open, else null.</summary>
    public Func<Shop.ShopController> Shop = () => null;
    /// <summary>The id of the dialog at the head of DAT_143af2258 (the open dialogs that take a right-clicked item), 0
    /// with none. The host gives <see cref="TradeBook.DialogId"/> while the trade dialog is open, or
    /// <see cref="Shop.ShopController.UiId"/> while the shop is open.</summary>
    public Func<int> DialogHead = () => 0;
    /// <summary>The bank at the head of the dialogs (0x4e47): its CStore::Allowed (FUN_14140d3e0's mode 3 test).</summary>
    public Func<Item, bool> BankAllowed = _ => true;
    /// <summary>InvenSlot::vfunc_14 case 3: the right-clicked item goes to the bank. Null: raised, not sent.</summary>
    public Action<Item> BankDeposit;
    /// <summary>InvenSlot::vfunc_14 case 10 (the item lock dialog at the head): the item goes to the lock bag when it is open. Null: raised, not sent.</summary>
    public Action<Item> LockSelect;
    /// <summary>Mode 17: the open merge revert dialog's CheckItem, its pick, and the scroll's own handler (ItemMergeRevert).</summary>
    public Func<Item, int> RevertCheck = _ => 0;
    public Action<Item> RevertSelect, RevertOpen;
    /// <summary>InvenSlot::vfunc_18 with an ItemLockSlot as the source (invenslot.cpp:1245): the slot's item uid and this slot's item (null for an empty cell).</summary>
    public Action<long, Item> LockSlotDropped;
    /// <summary>CItemBreakManager::CanBreakItem (the use mode of the break dialogs, invenslot.cpp:1986).</summary>
    public Func<Item, bool> BreakCanBreak = _ => false;
    /// <summary>InvenSlot::vfunc_14 cases 8 and 9: TryAddItem(item, -1) while the break dialog is open and idle. Null: raised, not sent.</summary>
    public Action<Item> BreakAdd;
    /// <summary>InvenSlot::vfunc_18 with an ItemBreakSlot as the source (invenslot.cpp:1238): the slot's item uid and this slot's item (null for an empty cell).</summary>
    public Action<long, Item> BreakSlotDropped;
    /// <summary>FUN_140c8dab0(GetInstrumentManager()): the uid of the score being played, 0 with none.</summary>
    public Func<long> PlayingScoreUid = () => 0;
    /// <summary>PlayItemSoundByUid: the item's sound.</summary>
    public event Action<Item> ItemSound;
    /// <summary>ShowSystemNotice(text, 1, 1) of a resolved text (FUN_141275460 with its arguments).</summary>
    public event Action<string> NoticeText;

    long Now => View?.ServerTime() ?? 0;
    ItemData GemData(int id) => Model.Tables.Items.Get(id);

    /// <summary>FUN_140633e40 (CanTradeItem) over this inventory's server time and item table.</summary>
    bool CanTradeItem(Item item, out int reason) => ItemTradeRules.CanTrade(item, GemData, Now, out reason);

    /// <summary>The trade box's tag (ShowTradeCountInput @0x14140d200), answered by UIMessageBox::HandleArgTag.</summary>
    public const string TradeInBox = "tradein";

    /// <summary>
    /// InvenSlot::TryAddToTrade (@0x14140b6d0), in the client's order: no item or no transfer, 0. CItemLockManager::CanUseItem
    /// (unit_1406125d0_itemlockmanager.cpp:230): a locked item or gem shows the box 0x22 (for a gem with the member
    /// confirmMsg = "$s_itemlock_msgbox_lock_gemstone_notice"), 0. CanTradeItem fails: the notice
    /// s_trade_tradelimit_by_gemstone (reason 2) or GetStringCommon(0x83f), 0. A score being played: the notice
    /// s_trade_tradelimit_playing_score, 0. With the trade dialog: no free slot shows a box of GetStringCommon(0x82f), 0;
    /// else with Shift held (DIK 0x2a / 0x36) or an item that does not ask a count (CItem::FUN_140526850) the whole stack
    /// is added (SendTradeMode8(uid, count, slot)) with the item's sound, else the count box. Then 1.
    /// </summary>
    public int TryAddToTrade(Item item)
    {
        if (item?.Record?.Transfer == null || Model == null) return 0;
        long now = Now;
        if (!ItemTradeRules.NotLocked(item, now, out int lockReason))
        {
            var box = MessageBoxQueue.Create(0x22, "", null, "");
            if (lockReason == 2) box.Values["confirmMsg"] = "$s_itemlock_msgbox_lock_gemstone_notice";
            ShowBox(box);
            return 0;
        }
        if (!CanTradeItem(item, out int reason))
        {
            NoticeText?.Invoke(reason == 2 ? Model.Tables.Common("s_trade_tradelimit_by_gemstone") ?? "" : Model.Tables.Common(0x83f));
            return 0;
        }
        if (item.Data.IsScoreItem && PlayingScoreUid() == item.Uid)
        {
            NoticeText?.Invoke(Model.Tables.Common("s_trade_tradelimit_playing_score") ?? "");
            return 0;
        }
        if (Trade() is { } book)
        {
            int slot = book.Items.FindFreeSlot();
            if (slot < 0)
            {
                ShowBox(MessageBoxQueue.Create(0, Model.Tables.Common(0x82f), null, ""));
                return 0;
            }
            if (!ItemTradeRules.AsksCount(item, now) || ShiftHeld())
            {
                book.SendAddItem(item.Uid, (int)item.Count, slot);
                ItemSound?.Invoke(item);
            }
            else ShowTradeCountInput(item.Uid, (int)item.Count, (int)item.Data.SlotMax);
        }
        return 1;
    }

    /// <summary>ShowTradeCountInput (@0x14140d200): FUN_140d73e40(5, 0x842, 0, "tradein"), a count box of
    /// GetStringCommon(0x842) with the item uid; the input (mode 1) starts at the count and takes as many digits as the
    /// slot max has (FUN_1412773a0, unit_141274d10_uihelpers.cpp:967).</summary>
    void ShowTradeCountInput(long uid, int count, int max)
    {
        var box = MessageBoxQueue.Create(5, Model.Tables.Common(0x842), null, TradeInBox);
        box.ItemUid = uid;
        box.SetInput(1, count.ToString(System.Globalization.CultureInfo.InvariantCulture), DigitCount(max), 0, 0);
        ShowBox(box);
    }

    /// <summary>FUN_1412773a0: the length of _itoa(value, 10) (a minus sign counts).</summary>
    public static int DigitCount(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;

    /// <summary>
    /// UIMessageBox::HandleTradeInButton (uimessagebox.cpp:2042)'s yes: FUN_141a002b0 (uid 0 passes; IsItemCountValid
    /// :2390, a count of 0 or less or above the stack shows Unk_BreakMessage(0xee, 1, 1, 1, have)); then, with the trade
    /// dialog open and a free slot (FUN_141b283f0), SendTradeMode8(uid, count, slot) and the item's sound.
    /// </summary>
    public void TradeIn(long uid, int count)
    {
        if (Model == null) return;
        if (uid != 0 && FindItem(uid) is { } item && (count <= 0 || count > (int)item.Count))
        {
            NoticeText?.Invoke(Ms2.Quest.ClientFormat.Format(Model.Tables.Common(0xee), 1, item.Count));
            return;
        }
        if (Trade() is not { } book) return;
        int slot = book.Items.FindFreeSlot();
        if (slot < 0) return;
        book.SendAddItem(uid, count, slot);
        if (FindItem(uid) is { } sounded) ItemSound?.Invoke(sounded);
    }

    // ---- npc shop sell (InvenSlot::FUN_14140be60, invenslot.cpp:1756, :1882; UIMessageBox SellItem, uimessagebox.cpp:670) ----

    /// <summary>The sell boxes' tags (invenslot.cpp:348, uimessagebox.cpp:84-85), answered by UIMessageBox::HandlePlainTag / HandleArgTag.</summary>
    public const string SellBox = "sell", SellConfirmBox = "sellconfirm";

    /// <summary>CItem::GetSellPrice (item.cpp:1235 -> ItemProperty::GetSellPrice * equipmentSellPrice factor when equip).</summary>
    public long SellPriceOf(Item item)
    {
        if (item?.Data == null || Model == null) return 0;
        long price = item.Data.PropertySellPrice(item.Rarity);
        if (item.Data.Type == 1) price = (long)(price * Model.Tables.EquipmentSellRate(item.Rarity));
        return price;
    }

    /// <summary>Resolves an inventory slot binding name ("InventoryDialog.&lt;i&gt;") to its item, or null.</summary>
    public Item FindSlotItem(string slotKey)
    {
        if (SlotIndexOf(slotKey) is int i and >= 0 && View?.ItemAt(View.Tab, i) is { } e && Model != null)
            return ToItem(e, Model.Tables.Items);
        return null;
    }

    /// <summary>
    /// InvenSlot::FUN_14140be60 (@0x14140be60, invenslot.cpp:1756): checks the open shop's CanSellItem (FUN_14140e3f0 :2132);
    /// with Shift held or an expiry item (CItem::FUN_140525c60) calls RequestSellItem immediately for the whole stack;
    /// else opens the sell quantity / confirmation flow (<see cref="ShowSellDialog"/>).
    /// </summary>
    public int TrySellItem(Item item)
    {
        if (item == null || Model == null) return 0;
        var shop = Shop();
        if (shop == null || !shop.CanSellItem(item)) return 0;
        if (ShiftHeld() || ItemTradeRules.HasPeriod(item))
        {
            shop.RequestSellItem(item, (int)item.Count);
            return 1;
        }
        ShowSellDialog(item);
        return 1;
    }

    /// <summary>
    /// FUN_14140cb90 (@0x14140cb90, invenslot.cpp:1882): for a stack of more than 1, opens box 0x12 ("shopTrade") with
    /// tag "sell", title 0x460, buttons 0x47e/0x476 and the item entry; for a single item, proceeds to
    /// <see cref="SellItemConfirmedStep"/> (uimessagebox.cpp:670).
    /// STILL-OPEN: CQuestManager::IsItemUsedByQuest (quest item sell warning s_msg_quest_item_sell / s_msg_quest_item_sell_stack).
    /// </summary>
    void ShowSellDialog(Item item)
    {
        int count = (int)item.Count;
        if (count > 1)
        {
            var box = MessageBoxQueue.Create(0x12, Model.Tables.Common("s_msg_item_sell") ?? "", null, SellBox);
            box.ItemUid = item.Uid;
            box.Title = Model.Tables.Common(0x460);
            box.SetButtonLabelIds(0x47e, 0x476, 0);
            box.SetItemEntry(item, new ItemTooltip(Model, 0).Name(item), 1, count, count, (int)SellPriceOf(item), 1, Model.Tables.Items.TextColor);
            ShowBox(box);
        }
        else
        {
            SellItemConfirmedStep(item, count);
        }
    }

    /// <summary>
    /// SellItem (@0x1419f8920, uimessagebox.cpp:670): asks once more (box 0x11, tag "sellconfirm,&lt;count&gt;", message 0x28c,
    /// buttons 0x47e/0x476) when rarity &gt; 3, skin item, or sellPrice * count &gt;= ShopSellConfirmPrice; otherwise calls
    /// RequestSellItem directly.
    /// </summary>
    void SellItemConfirmedStep(Item item, int count)
    {
        if (item == null || Model == null) return;
        long total = SellPriceOf(item) * count;
        bool ask = item.Rarity > 3 || item.Data.HasSkinType || total >= Model.Tables.ShopSellConfirmPrice;
        if (ask)
        {
            var box = MessageBoxQueue.Create(0x11, Model.Tables.Common(0x28c), null, $"{SellConfirmBox},{count}");
            box.ItemUid = item.Uid;
            var copy = Item.Create(Model.Tables.Items, item.ItemId, item.Rarity);
            if (copy != null)
            {
                copy.Rarity = item.Rarity;
                copy.Count = (uint)Math.Max(count, 0);
                copy.Uid = item.Uid;
                copy.Record = item.Record;
                box.SetItemEntry(copy, new ItemTooltip(Model, 0).Name(copy), 1, 0, -1, -1, 1, Model.Tables.Items.TextColor);
            }
            box.SetButtonLabelIds(0x47e, 0x476, 0);
            ShowBox(box);
        }
        else
        {
            Shop()?.RequestSellItem(item, count);
        }
    }

    /// <summary>FUN_141a002b0 (uimessagebox.cpp:2031, :2077, :2390): uid 0 passes; otherwise 1 &lt;= count &lt;= item.Count,
    /// else notice 0xee formatted with (1, have) and returns false.</summary>
    public bool IsSellCountValid(long uid, int count)
    {
        if (Model == null) return false;
        if (uid != 0 && FindItem(uid) is { } item && (count <= 0 || count > (int)item.Count))
        {
            NoticeText?.Invoke(Ms2.Quest.ClientFormat.Format(Model.Tables.Common(0xee), 1, item.Count));
            return false;
        }
        return true;
    }

    /// <summary>UIMessageBox::HandleSellButton (@0x1419fe830, uimessagebox.cpp:2024): after validation, calls SellItem.</summary>
    public void SellCountStep(long uid, int count)
    {
        if (FindItem(uid) is { } item) SellItemConfirmedStep(item, count);
    }

    /// <summary>UIMessageBox::HandleSellConfirmButton (@0x1419fea00, uimessagebox.cpp:2070): after validation, sends sell.</summary>
    public void SellConfirmStep(long uid, int count)
    {
        if (FindItem(uid) is { } item) Shop()?.RequestSellItem(item, count);
    }

    /// <summary>The common string ids the equip checks show (commonstringtable.cpp:1841).</summary>
    public const string InvalidCodiSlotKey = "s_item_err_Invalid_slot_codi", InvalidSlotKey = "s_item_err_Invalid_slot";
    public const string LowLevelKey = "s_item_err_puton_low_level", ExpiredKey = "s_item_err_puton_expired", GenderKey = "s_err_gender",
        EquipJobKey = "s_item_err_puton_job", DisabledJobKey = "s_item_err_disable_job";

    /// <summary>
    /// Unk_Inventory::RequestEquipItem(item, slot, layer, skipBinding) (inventory.cpp:1236): while the guide restricts putting
    /// items on (CGuideManager::IsRestricted(4), :1237): notice 2185 (<see cref="Guide.GuideManager.ItemPutOnOffNotice"/>);
    /// no local player, nothing.
    /// FUN_140a65e30 (:1630) on GetItemUseError (:1572): an expired item (CItem::IsExpired, server time) without a maid id:
    /// box 225 (FUN_140d73bc0: ShowMessageBox(GetStringCommon(id), 0)); GetItemRequirementError (:1586) on the wearer's record:
    /// gender box 45, job box 232 for an equipment item else 233, level box 224. A slot of 0 is
    /// <see cref="WornEquipment.EquipSlotFor"/> (FUN_141420a70; worn items usable as PC::CanUseItem's expiry, CItem::FUN_140528980
    /// (item.cpp:1717: expired, not a pet and no maid id), and codi-layer parts). CanEquipItem (:1418): not an equipment item with model groups none of type 0 (FUN_14053c710):
    /// nothing; layer 2 without feature 0x8e or a skin type: s_item_err_Invalid_slot_codi; layer 1 without a skin type: box
    /// 0xd3 (211). No record: no worn item. Unless <paramref name="skipBinding"/>, an item that binds on equip and is not
    /// bound asks first (:1245-1256): box 17 with s_msg_transfer_bind_when_equip and the player's name, tag
    /// <see cref="BindOnEquipBox"/>, the slot asked for and the layer appended, the item uid and entry, yes / no; its yes comes
    /// back as <see cref="ConfirmBindEquip"/>, which asks again with the skip. The send maps the layer as FUN_1401fb870: 2
    /// only with feature 142. Nothing is put on here: the server's equip packets do.
    /// A dead local player (IsDead, :1241) gets box 66 (<see cref="MyInfoController.DeadKey"/>) first.
    /// STILL-OPEN: the PC bang, VIP
    /// and wedding requirements (their player states are not in the port), the music score part of FUN_140528980, the rest of
    /// CanEquipItem (PC::vfunc_132 / CItem::FUN_140525e50 slot acceptance, the worn two-slot pair boxes 0xd2 / 0xd4 / 0xd6 /
    /// 0x43, s_item_err_codi_slot, the cube texture block), the warning sounds (s_warningLevel,
    /// s_warningGender, s_warningClass) and the item sound. Without the wearer's record the requirement checks are skipped
    /// (the client always has the player).
    /// </summary>
    void RequestEquip(Item item, int slot, int layer, bool skipBinding = false)
    {
        if (Guide.GuideManager.Current?.IsRestricted(Guide.GuideLimit.ItemPutOn) == true)
        { SystemNotice?.Invoke(Guide.GuideManager.ItemPutOnOffNotice); return; }
        if (!Model.HasLocalPlayer) return;
        if (LocalPlayerDead()) { ShowCommonBox(MyInfoController.DeadKey); return; }      // inventory.cpp:1241
        var d = item.Data;
        long now = View.ServerTime();
        bool expired = item.ExpiryTime > 0 && item.ExpiryTime <= now;
        if (expired && !d.HasMaidId) { ShowCommonBox(ExpiredKey); return; }
        var record = Wearer?.Invoke();
        if (record != null && !d.IsGenderAllowed(record.Gender)) { ShowCommonBox(GenderKey); return; }
        if (record != null && !d.IsJobAllowed(record.JobGroup)) { ShowCommonBox(d.Type == 1 ? EquipJobKey : DisabledJobKey); return; }
        if (record != null && !d.IsLevelAllowed(record.Level)) { ShowCommonBox(LowLevelKey); return; }
        var models = Model.Tables.ItemModels;
        var worn = WornEquipment.Of(record, Model.Tables.Items) ?? WornEquipment.Of(new PcInfo { Equips = new List<EquipItem>() }, Model.Tables.Items);
        bool CanUse(Item w) => ItemLevels.Usable(w, now) && (record?.CodiActive == true || !IsLayer2(w));
        bool IsLayer2(Item w) => record?.Outfit2?.Any(o => o.Uid == w.Uid) == true;
        int selected = slot == 0 ? worn.EquipSlotFor(item, models, Feature, CanUse, w => ItemLevels.Of(w)?.Total) : slot;
        if (!CanEquipItem(item, selected, layer)) return;      // inventory.cpp:1244: the slot picked, not the one asked for
        if (!skipBinding && NeedsBindConfirm(item))
        {
            // inventory.cpp:1245-1256: the box keeps the slot asked for (0: chosen again on the yes) and the layer
            var box = BindBox(item, BindOnEquipKey, BindOnEquipBox);
            box.Append((uint)slot);
            box.Append((uint)layer);
            ShowBox(box);
            return;
        }
        byte mode = layer switch { 1 => 1, 2 => Feature(142) ? (byte)2 : (byte)1, _ => 0 };
        View.RequestEquip?.Invoke(item.Uid, (byte)selected, mode);
    }

    /// <summary>The part of CanEquipItem (inventory.cpp:1418) the port has (see <see cref="RequestEquip"/>): not an equipment
    /// item with model groups none of type 0 (FUN_14053c710): false; layer 2 without feature 0x8e or a skin type: box
    /// s_item_err_Invalid_slot_codi; layer 1 without a skin type: box 0xd3 (211). The slot is not read (its acceptance is
    /// STILL-OPEN, see <see cref="RequestEquip"/>).</summary>
    public bool CanEquipItem(Item item, int slot, int layer)
    {
        var d = item.Data;
        if (d.Type != 1 || Model.Tables.ItemModels.Get(d.ItemPreset) is not { SlotTypes.Count: > 0 } model || model.SlotTypes.Contains(0)) return false;
        if (layer == 2 && (!Feature(0x8e) || !d.HasSkinType)) { ShowCommonBox(InvalidCodiSlotKey); return false; }
        if (layer == 1 && !d.HasSkinType) { ShowCommonBox(InvalidSlotKey); return false; }
        return true;
    }

    /// <summary>RequestEquipItem(item, slot, layer, 0): <see cref="RequestEquip"/>.</summary>
    public void RequestEquipItem(Item item, int slot, int layer) { if (Model != null && View != null) RequestEquip(item, slot, layer); }

    /// <summary>InvenSlot::GetItemUid of a slot of the shown tab.</summary>
    public long? InvenSlotUid(string name) => SlotIndexOf(name) is int i and >= 0 ? View?.ItemAt(View.Tab, i)?.Uid ?? 0 : null;

    /// <summary>FindItemByUid over the view (<see cref="InventoryView.FindByUid"/>).</summary>
    public Item FindItem(long uid) => View?.FindByUid(uid) is { } e && Model != null ? ToItem(e, Model.Tables.Items) : null;

    /// <summary>CItemTransfer::HasBindingRule (itemsocketsystemutil.cpp:2788, rule &amp; 8) and no binding (HasBinding, :2795): the
    /// client asks first, box 17 / 0x11, and sends only on its confirm.</summary>
    internal static bool NeedsBindConfirm(Item item) => item.Record?.Transfer is { } t && (t.Rule & 8) != 0 && !t.HasBinding;

    /// <summary>The bind boxes' tags (s_useBindEquipItem inventory.cpp:196, s_strUseBindFuncItem
    /// unit_140d1ae30_itemfunctiondispatch.cpp:304), which UIMessageBox::HandlePlainTag answers (uimessagebox.cpp:1052-1061).</summary>
    public const string BindOnEquipBox = "useBindEquipItem", BindOnUseBox = "useBindFuncItem";

    void ShowCommonBox(string key) => ShowBox(MessageBoxQueue.Create(0, Model.Tables.Common(key) ?? "", null, ""));

    /// <summary>The message box text GetStringCommon(0x2a1) (commonstringtable.cpp:2289).</summary>
    public const string ExpiredItemsKey = "s_msg_item_remove_expired_item_in_inventory";

    /// <summary>"OnClickSort" (:836-857): the view's checks; with expired items, FUN_140d73e40(1, 0x2a1): a type-1 box with
    /// OnSortConfirm as callback, SetButtonPreset(1), FUN_141a024a0 (Enter does not answer it), modaless, shown (FUN_140d73c60).</summary>
    void OnClickSort()
    {
        if (View == null || View.ClickSort() != InventoryView.SortClick.Confirm) return;
        var box = MessageBoxQueue.Create(1, MessageBoxData.CommonByKey(ExpiredItemsKey) ?? "", null, "");
        box.Callback = (result, _) => { View?.OnSortConfirm(result); return true; };
        box.SetButtonPreset(1);
        box.EnterAllowed = false;
        box.Modaless = true;
        ShowBox(box);
    }

    /// <summary>FUN_140d73c60: the box goes to the message box queue (FUN_14151f150).</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;

    // ---- IInventoryBinding: the view's calls into the movie -------------------------------------------------------------

    void IInventoryBinding.initData(int tabButtonIndex, bool devMode) => Host.Invoke("initData", tabButtonIndex, devMode);

    void IInventoryBinding.updateButtonState(InventoryButtonState state) => UpdateButtonState();

    /// <summary>FUN_14199bc00: setNew(button, bNew).</summary>
    void IInventoryBinding.setNew(int buttonIndex, bool bNew) => Host.Invoke("setNew", buttonIndex, bNew);

    /// <summary>UpdateExpandLabel @0x14199c0d0: m_expand's text (ASLabel FUN_1422e4a50), then vfunc_24(true): invalidate, validateNow.</summary>
    void IInventoryBinding.m_expand(string text)
    {
        if (Host.Child("m_expand") is not { } l) return;
        Host.Set(l, "text", text);
        Host.Invoke(l, "invalidate");
        Host.Invoke(l, "validateNow");
    }

    /// <summary>FUN_14199ba10: the bag made again with GetSlotCount(m_194) slots (CreateSlotBag), m_itemBagEx's dataProvider
    /// its slot names (GetSlotNames, ASCoreList::vfunc_25), then invalidate + validateNow.</summary>
    void IInventoryBinding.bindSlotBag(int tab, IReadOnlyList<BagCell> cells)
    {
        if (Host.Child("m_itemBagEx") is not { } list) return;
        slotCount = cells.Count;
        Host.Set(list, "dataProvider", DataProvider(Enumerable.Range(0, slotCount).Select(i => (object)SlotName(i))));
        Host.Invoke(list, "invalidate");
        Host.Invoke(list, "validateNow");
    }

    /// <summary>InventorySlotBag::RefreshSlotForItem: the slot's binding pushed again (DragSlot::vfunc_10, PushBinding).</summary>
    void IInventoryBinding.refreshSlot(int tab, int slot, BagCell cell) => Host.Push(SlotName(slot));

    public static string SlotName(int i) => BagName + "." + i;

    /// <summary>
    /// The slot "InventoryDialog.&lt;i&gt;" of the shown tab: InvenSlot::vfunc_8 (invenslot.cpp:1592): null for an empty
    /// cell, an unknown item or no local player; else an ASSlot(2) object built by ASSlot::FUN_1409c3860 with flags 0x1c01f
    /// and updateIconSearchOn (FUN_14140c180: the item's name equal to the search text, false while it is empty; the search
    /// is not ported, so it is empty). Other names go to the base (the item slot table).
    /// </summary>
    public override object GetBinding(string name)
    {
        if (!name.StartsWith(BagName + ".", StringComparison.Ordinal) || !int.TryParse(name.AsSpan(BagName.Length + 1), out int i))
            return base.GetBinding(name);
        if (View?.ItemAt(View.Tab, i) is not { } e || Model == null || !Model.HasLocalPlayer) return null;
        var item = ToItem(e, Model.Tables.Items);
        if (item == null) return null;
        var slot = new List<(string, object)>();
        if (!ItemSlot.Build(slot, item, true, SlotFlags, Model, Model.ItemSlotSources)) return null;
        slot.Add(("updateIconSearchOn", false));
        return Host.Obj(slot.ToArray());
    }

    /// <summary>
    /// The slot "InventoryDialog.&lt;i&gt;"'s tooltip through NotifyDragSlot::GetSlotTooltip: null for another name or a slot
    /// past the bag; an empty object when the slot gives none (GetSlotTooltip makes the object before asking the slot).
    /// </summary>
    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (!name.StartsWith(BagName + ".", StringComparison.Ordinal) || !int.TryParse(name.AsSpan(BagName.Length + 1), out int i))
            return base.GetSlotTooltip(name);
        if (i < 0 || i >= slotCount) return null;
        return SlotTooltip(i) ?? new AsTooltip();
    }

    /// <summary>
    /// InvenSlot::vfunc_0 (ITooltip, invenslot.cpp:1507): none for an empty cell, an unknown item or no local player. An
    /// ItemExchangeScroll item shows its exchange result (CItemExchangeScrollTable) instead: not ported, none here. Else the
    /// worn item it is compared with (FUN_14140c030: MyPC::FUN_14152e2f0); none when that is the item itself. ASTooltip(the
    /// player, 0x80, 2).FUN_1409e13a0(worn, item), then SetEnablePutonBoth("", vfunc_22) and, the slot being enabled (the
    /// ctor's SetEnabled(true), invenslot.cpp:630), SetEnablePreviewBoth(none, item). The use mode 2 parts (the mouse
    /// cursor, customizeColor cleared) and the dagger swap sound need an open dialog or audio and are not here.
    /// </summary>
    AsTooltip SlotTooltip(int i)
    {
        if (View?.ItemAt(View.Tab, i) is not { } e || Model == null || !Model.HasLocalPlayer) return null;
        var item = ToItem(e, Model.Tables.Items);
        if (item == null) return null;
        if (item.Data.FunctionName == "ItemExchangeScroll") return null;
        var record = Wearer?.Invoke();
        var equipment = WornEquipment.Of(record, Model.Tables.Items);
        var worn = equipment?.ForCompare(item, Model.Tables.ItemModels, ShiftHeld());
        if (worn != null && worn.Uid == item.Uid) return null;
        Skills.SlotWorld world = null;                                    // built once per hover, on the first count
        var tip = new ItemTooltip(Model, 0x80)
        {
            Owner = record != null ? (record.CharacterId, record.Name) : null,
            Localize = Localize,
            OwnerWearsUid = uid => equipment?.Wears(uid) == true,
            OwnerEquipment = equipment,
            ShiftHeld = ShiftHeld,
            OwnedCount = id => (world ??= SlotWorld?.Invoke())?.CountById(id) ?? 0,
        };
        if (!tip.FillCompare(worn, item)) return null;
        tip.SetEnablePutonBoth("", UseHint(item));
        tip.SetEnablePreviewBoth(null, item);
        return tip.Data;
    }

    /// <summary>
    /// FUN_14140d3e0 (invenslot.cpp:1957) with the hint flag. Its first branches follow the head of DAT_143af2258, the open
    /// dialogs that register there (UIDialog m_164: the shop, trade, item break, item lock and the like). Ported: the item
    /// lock dialog (0x4f08: 10 when CanBeItemLocked, else 0 for the hint and 10 for the click), the shop and the bank (as
    /// the trade dialog below), and the trade dialog (0x4e83): mode 1 when the item can be traded (FUN_140633e40), else 0 for the hint and 1 for
    /// the click (TryAddToTrade then says why). With no ported dialog at the head only the last part runs: 15 for a UGC
    /// item whose look state is 2 (CItem::FUN_140525fc0, CUgcItemLook::FUN_140893520), 6 an equipment item
    /// (CItemData::FUN_14053cbc0), 7 a quick slot item (FUN_14053e460), 12 a gem, else 0.
    /// STILL-OPEN: the other dialogs of the list.
    /// </summary>
    internal int UseMode(Item item, bool hint)
    {
        int head = DialogHead();
        if (head == TradeBook.DialogId)
            return CanTradeItem(item, out _) ? 1 : hint ? 0 : 1;
        if (head == Ms2.Ui.Shop.ShopController.UiId)
            return ItemTradeRules.CanSellToShop(item, GemData, Now) ? 2 : hint ? 0 : 2;
        if (head == StoreDialogController.UiId)
            return BankAllowed(item) ? 3 : hint ? 0 : 3;
        if (head == ItemBreakDialogController.UiId || head == ItemBreakDialogController.DonationUiId)
            return BreakCanBreak(item) ? 8 : hint ? 0 : 8;
        if (head == ItemLockDialogController.UiId)       // 0x4f08: CanBeItemLocked ? 10 : hint ? 0 : 10
            return ItemLockDialogController.CanBeItemLocked(item.Record, item.Data) ? 10 : hint ? 0 : 10;
        // 0x4f3d (invenslot.cpp:2024): 17 for the click and for a hint the dialog accepts; a refused hint falls through
        if (head == ItemMergeRevertDialogController.UiId && (!hint || RevertCheck(item) == ItemMergeRevertDialogController.CheckOk))
            return 17;
        var d = item.Data;
        if (item.Record is { HasUgc: true, Ugc.State: 2 }) return 15;
        if (d.Type == 1) return 6;
        if (d.IsQuickSlotUsable) return 7;
        if (d.IsGemItem) return 12;
        return 0;
    }

    /// <summary>
    /// InvenSlot::vfunc_22 (@0x14140b030): the use mode's right-click hint. Mode 6: with feature 0x8e and a skin item
    /// s_msg_rclick_puton_equip_to_coditab, then, when action 0x4940d has a key (CKeyTable::GetActionKeyName(0x4940d, 0)),
    /// a line s_msg_rclick_puton_equip_to_skintab_with_key with it; else s_msg_rclick_puton_equip (0x751). Mode 7:
    /// s_msg_rclick_puton_qslot (0x75a). Mode 1 (the trade dialog): GetStringCommon(0x74f). The other modes with a hint come only from the dialogs of DAT_143af2258; the rest "".
    /// </summary>
    string UseHint(Item item)
    {
        string Common(string key) => Model.Tables.Common(key) ?? "";
        switch (UseMode(item, true))
        {
            case 1:
                return Model.Tables.Common(0x74f);
            case 2:
                return Model.Tables.Common(0x755);
            case 6:
                if (Feature(0x8e) && item.Data.HasSkinType)
                {
                    string text = Common("s_msg_rclick_puton_equip_to_coditab");
                    var keys = Model.Keys;
                    string key = keys.GetKeyName(keys.FindKeyByActionAndPriority(0x4940d, 0));
                    if (key.Length != 0) text += "\n" + Ms2.Quest.ClientFormat.Format(Common("s_msg_rclick_puton_equip_to_skintab_with_key"), key);
                    return text;
                }
                return Common("s_msg_rclick_puton_equip");
            case 7:
                return Common("s_msg_rclick_puton_qslot");
        }
        return "";
    }

    /// <summary>The flags InvenSlot::vfunc_8 builds its slot with.</summary>
    public const int SlotFlags = 0x1c01f;

    /// <summary>A bag item (the parsed CItem::ReadFromStream record) as the slot's CItem: its data, rarity, count, bag slot,
    /// uid, new flag and expiry; the parts the record parse does not keep yet (enchant, lock) stay 0.</summary>
    public static Item ToItem(EquipItem e, ItemDataTable table)
    {
        var item = Item.Create(table, e.Id, e.Rarity);
        if (item == null) return null;
        item.Rarity = e.Rarity;
        item.Count = (uint)Math.Max(e.Amount, 0);
        item.Slot = e.InventorySlot;
        item.Uid = e.Uid;
        item.IsNew = e.IsNew;
        item.ExpiryTime = e.ExpiryTime;
        item.LockState = e.LockState;
        item.UnlockTime = e.UnlockTime;
        item.EnchantLevel = e.Enchant.Level;
        item.UnlimitedEnchantLevel = e.LimitBreak.Level;
        item.Record = e;
        return item;
    }

    /// <summary>The names of the bag built last.</summary>
    public override IEnumerable<string> GetBagSlotBindings(string bag) =>
        bag == BagName ? Enumerable.Range(0, slotCount).Select(SlotName) : Array.Empty<string>();
}
