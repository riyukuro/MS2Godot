using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

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
public sealed class InventoryController : UiController, IInventoryBinding
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
    /// <summary>DAT_143af62d0, initData's second argument (the dev mode: the auto pick-up option button enabled).</summary>
    public bool DevMode;

    /// <summary>CUIService::ToggleDialog(id) of a button: item break 0x4e8d, item lock 0x4f08, socket and gem 0x4f0a, enchant 0x4e91.</summary>
    public event Action<int> ToggleDialog;
    /// <summary>A button whose client path sends a request or opens a part not here (by its callback name): OnClickSort,
    /// onClickSlotExpand, onClickOpenBank, onClickItemOption, onClickItemMerge, onClickItemEnchantTransform,
    /// onClickUnlimitedEnchant, onClickItemRemakeOption, OnItemSearch, notifySlotUse.</summary>
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
        Vfunc46();
    }

    /// <summary>vfunc_46 @0x1419994e0: the item break button's tooltip, the buttons hidden by their features (socket and gem
    /// 0x2e, merge 0x6f, open bank 0x75, enchant transform 0xa6, unlimited enchant 0x10d), then updateButtonPos.</summary>
    void Vfunc46()
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

    /// <summary>
    /// UpdateButtonState @0x141999f60. The item lock / break / donation / extraction dialogs are not here, so they count as
    /// closed (m_1a4 0: break and lock on). bAutoPickup: level &gt;= 1. bOpenBank: PC bang or VIP (FUN_141658f30 || IsVip);
    /// neither state is here, so 0.
    /// </summary>
    void UpdateButtonState()
    {
        bool bank = false;
        Host.Invoke("updateButtonState", Host.Obj(
            ("bAutoPickup", Pc.Level >= 1 ? 1 : 0),
            ("bItemEchant", true),
            ("bItemBreak", 1),
            ("itemBreakToolTip", GetItemBreakTooltip()),
            ("bItemLock", 1),
            ("bRemakeOption", true),
            ("bUnlimitedEnchant", true),
            ("bGem", true),
            ("slotExpandToolTip", GetExpandTooltip()),
            ("bOpenBank", bank ? 1 : 0),
            ("openBankTooltip", GetOpenBankTooltip(bank))));
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
            case "onclickitemoption": case "onclickitemenchanttransform": case "onclickunlimitedenchant":
            case "onclickitemmerge": case "onitemsearch": case "onclickslotexpand": case "notifyslotuse":
                Button?.Invoke(name); return true;
            // these two fall through to "return 0" in the client (:945-951, :979-981)
            case "onclickitemremakeoption": case "onclickopenbank":
                Button?.Invoke(name); return false;
        }
        return false;
    }

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
        return item;
    }

    /// <summary>The names of the bag built last.</summary>
    public override IEnumerable<string> GetBagSlotBindings(string bag) =>
        bag == BagName ? Enumerable.Range(0, slotCount).Select(SlotName) : Array.Empty<string>();
}
