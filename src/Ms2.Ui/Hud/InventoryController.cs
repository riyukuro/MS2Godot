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
public sealed class InventoryController : UiController, IInventoryBinding, IInvenSlotOwner
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
            case "notifyslotuse": return SlotUse(args);
            case "onclickitemoption": case "onclickitemenchanttransform": case "onclickunlimitedenchant":
            case "onclickitemmerge": case "onitemsearch": case "onclickslotexpand":
                Button?.Invoke(name); return true;
            // these two fall through to "return 0" in the client (:945-951, :979-981)
            case "onclickitemremakeoption": case "onclickopenbank":
                Button?.Invoke(name); return false;
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
    /// then by FUN_14140d3e0 (<see cref="UseMode"/>): mode 6 puts it on, FUN_140a621c0(uid, 0, FUN_1414056b0) =
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
        if (UseMode(item) != 6) { Button?.Invoke("notifySlotUse"); return; }
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
    /// <see cref="UseItem"/>. The other kinds (a shop id, a pet, an emotion, an equipment item, FUN_14053c8b0) are not
    /// ported: raised.
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
        if (d.HasCubeTextureMesh || d.IsScoreItem || !d.HasFunction) { Button?.Invoke("notifySlotUse"); return; }
        var record = Wearer?.Invoke();
        bool expired = item.ExpiryTime > 0 && item.ExpiryTime <= View.ServerTime();
        if (expired && !d.IsPetItem && !d.HasMaidId) { ShowCommonBox(ExpiredKey); return; }
        if (record != null && !d.IsLevelAllowed(record.Level)) { ShowCommonBox(UseLevelKey); return; }
        if (record != null && !d.IsJobAllowed(record.JobGroup)) { ShowCommonBox(DisabledJobKey); return; }
        if (record != null && !d.IsGenderAllowed(record.Gender)) { ShowCommonBox(GenderKey); return; }
        UseItem(item, false);
    }

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
    bool UseItem(Item item, bool confirmed)
    {
        if (!CanUseItem(item)) return false;
        var d = item.Data;
        if (!confirmed && NeedsBindConfirm(item))
        {
            ShowBox(BindBox(item, BindOnUseKey, BindOnUseBox));
            return true;
        }
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
        box.SetItemEntry(item, new ItemTooltip(Model, 0).Name(item) ?? "", 1, 0, -1, -1, 1, Model.Tables.Items.TextColor);
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

    /// <summary>The common string ids the equip checks show (commonstringtable.cpp:1841).</summary>
    public const string InvalidCodiSlotKey = "s_item_err_Invalid_slot_codi", InvalidSlotKey = "s_item_err_Invalid_slot";
    public const string LowLevelKey = "s_item_err_puton_low_level", ExpiredKey = "s_item_err_puton_expired", GenderKey = "s_err_gender",
        EquipJobKey = "s_item_err_puton_job", DisabledJobKey = "s_item_err_disable_job";

    /// <summary>
    /// Unk_Inventory::RequestEquipItem(item, slot, layer, skipBinding) (inventory.cpp:1236): no local player, nothing.
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
    /// STILL-OPEN: the guide restriction (CGuideManager::IsRestricted(4), box 2185), the PC bang, VIP
    /// and wedding requirements (their player states are not in the port), the music score part of FUN_140528980, the rest of
    /// CanEquipItem (PC::vfunc_132 / CItem::FUN_140525e50 slot acceptance, the worn two-slot pair boxes 0xd2 / 0xd4 / 0xd6 /
    /// 0x43, s_item_err_codi_slot, the cube texture block), the warning sounds (s_warningLevel,
    /// s_warningGender, s_warningClass) and the item sound. Without the wearer's record the requirement checks are skipped
    /// (the client always has the player).
    /// </summary>
    void RequestEquip(Item item, int slot, int layer, bool skipBinding = false)
    {
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
        var tip = new ItemTooltip(Model, 0x80)
        {
            Owner = record != null ? (record.CharacterId, record.Name) : null,
            Localize = Localize,
            OwnerWearsUid = uid => equipment?.Wears(uid) == true,
            OwnerEquipment = equipment,
            ShiftHeld = ShiftHeld,
        };
        if (!tip.FillCompare(worn, item)) return null;
        tip.SetEnablePutonBoth("", UseHint(item));
        tip.SetEnablePreviewBoth(null, item);
        return tip.Data;
    }

    /// <summary>
    /// FUN_14140d3e0 (invenslot.cpp:1957) with the hint flag. Its first branches follow the head of DAT_143af2258, the open
    /// dialogs that register there (UIDialog m_164: the shop, trade, item break, item lock and the like); none of them is in
    /// this port, so the list is empty and only the last part runs: 15 for a UGC item whose look state is 2
    /// (CItem::FUN_140525fc0, CUgcItemLook::FUN_140893520), 6 an equipment item (CItemData::FUN_14053cbc0), 7 a quick slot
    /// item (FUN_14053e460), 12 a gem, else 0.
    /// </summary>
    static int UseMode(Item item)
    {
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
    /// s_msg_rclick_puton_qslot (0x75a). The other modes with a hint come only from the dialogs of DAT_143af2258; the rest "".
    /// </summary>
    string UseHint(Item item)
    {
        string Common(string key) => Model.Tables.Common(key) ?? "";
        switch (UseMode(item))
        {
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
