using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>A drag source that is an ItemBreakSlot: the uid of the item in the named slot (null for another source).</summary>
public interface IItemBreakSlotOwner
{
    long? BreakSlotUid(string name);
}

/// <summary>
/// The item dismantle window, UIItemBreakDialog (client/ms2/uiinventorydialog.cpp, UI id 0x4e8d, type name "ItemBreakDialog",
/// movie uiitembreakdialog) and the donation variant UIItemBreakDonationDialog (0x4e8e, "ItemBreakDonationDialog"), both on
/// UIItemBreakBase with an ItemBreakSlotBag of ItemBreakSlots (itembreakslot.cpp), named "&lt;TypeName&gt;.%d". The state and
/// every refusal and send are <see cref="ItemBreakManager"/> (CItemBreakManager); this controller shows it: the bag, the
/// type and rank dropdowns, the break button's confirms, the progress calls, the preview (mode 5) and result (mode 3) rows.
/// <para>The texts come from the table the client reads each from: <see cref="Loc"/> (GFxManager::Localize, the Flash table)
/// and <see cref="Common"/> (GetStringByKey / GetStringCommonByKey / FUN_14028dfa0, the common table).</para>
/// <para>STILL-OPEN: the dock beside the inventory (vfunc_43), the movie's reward slot toggles.</para>
/// </summary>
public sealed class ItemBreakDialogController : UiController, IDragSlotOwner, IItemBreakSlotOwner
{
    readonly bool donation;
    public ItemBreakDialogController(bool donation = false)
    {
        this.donation = donation;
        Unloaded += () => InventoryButtons?.Invoke();     // vfunc_26 (@0x1419a1830): the unload, once the close tween is done (IsClosed)
    }

    public override string Movie => donation ? "uiitembreakdonationdialog" : "uiitembreakdialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => donation ? "ItemBreakDonationDialog" : "ItemBreakDialog";

    public const int UiId = 0x4e8d, DonationUiId = 0x4e8e;
    public int Id => donation ? DonationUiId : UiId;
    public string Bag => TypeName;
    /// <summary>ItemBreakSlot::vfunc_8 (@0x141410370): ASSlot(2) built with flags 0x1401f; ITooltip::vfunc_0: ASTooltip flags 0x20.</summary>
    public const int SlotFlags = 0x1401f, TooltipFlags = 0x20;
    /// <summary>BindRewardItem's slot flags (the reward rows' ItemInfoSlot).</summary>
    public const int RewardFlags = 0x401;
    /// <summary>The confirm boxes' types: the rarity 5/6 box with its checkbox, the rarity 4 yes/no.</summary>
    public const int LegendaryBoxType = 0x19, CommonBoxType = 1;
    /// <summary>The notice id of "nothing to break" (BlockFormat 0xe6, box type 0x14).</summary>
    public const int NoItemsNotice = 0xe6, NoItemsBoxType = 0x14;
    public const int TabEquip = 0, TabSkin = 1, TabGem = 6, TabPet = 9;

    public ItemBreakManager Mgr;
    /// <summary>The box is shown (FUN_140d73c60).</summary>
    public Action<MessageBoxData> ShowData = d => MessageBoxQueue.Show(d);
    public event Action Closed;
    /// <summary>An inventory slot needs a redraw (UIInventoryDialog::RefreshItemSlot).</summary>
    public event Action<long> InventorySlotRefresh;
    /// <summary>The s_strExtractProgress / s_strExtractEnd sound of StartBreak / EndBreak.</summary>
    public event Action<string> Sound;
    /// <summary>Inventory UpdateButtonState + FUN_141999e00 (0x4e2d) after the load (OnLoadComplete @0x1419a1770) and the unload
    /// (vfunc_26 @0x1419a1830, uiinventorydialog.cpp:2340-2362).</summary>
    public event Action InventoryButtons;
    /// <summary>The inventory's AddFlags(7) after a reset (onResetBreak).</summary>
    public event Action InventoryReset;
    /// <summary>A callback this port does not handle (the slot preview), raised by name.</summary>
    public event Action<string> Button;
    /// <summary>The key action held (the preview action 0x49406).</summary>
    public Func<int, bool> ActionKeyDown = _ => false;
    /// <summary>Shift held (DragSlot::vfunc_13's check).</summary>
    public Func<bool> ShiftHeld = () => false;
    /// <summary>FUN_140d2cfc0: the item linked into the chat input.</summary>
    public Action<Item> LinkItem;

    /// <summary>m_1f8: the bundle type; m_1fc: the bundle rank (selected index = rank - 1).</summary>
    public int BundleType, BundleRank = 2;
    /// <summary>m_188: the break is in progress.</summary>
    public bool InProgress { get; private set; }

    readonly List<string> rewardBinds = new();
    readonly Dictionary<int, int> typeIndex = new();
    bool closing;
    object bagChild;

    public string SlotName(int i) => Bag + "." + i.ToString(CultureInfo.InvariantCulture);

    bool ParseSlot(object name, out int index)
    {
        index = -1;
        return name is string n && n.StartsWith(Bag + ".", StringComparison.Ordinal)
            && int.TryParse(n.AsSpan(Bag.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out index)
            && index >= 0 && index < ItemBreakManager.Slots;
    }

    /// <summary>GFxManager::Localize("$" + key): the Flash table's text (the key itself when it has none).</summary>
    string Loc(string key) => Host.Player.Translate?.Invoke("$" + key) ?? key;

    /// <summary>GetStringByKey / GetStringCommonByKey / FUN_14028dfa0's format: the common table's text ("" when it has none).</summary>
    string Common(string key) => Model.Tables.Common(key) ?? "";

    // ---- load / close ------------------------------------------------------------------------------------------------

    /// <summary>OnLoadComplete: the bag bound under the type name, vfunc_85(true, 0), the listener, Mode0 (1 break, 2 donation).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Mgr == null) return;
        Mgr.DialogOpen = true;
        Mgr.SlotChanged += OnSlotChanged;
        Mgr.InventoryRefresh += OnInventoryRefresh;
        Mgr.ResultForwarded += OnResult;
        Mgr.PreviewReceived += OnPreview;
        Mgr.RefreshActive += RefreshActive;
        Mgr.ResetBreak += ResetBreak;
        Mgr.Sound += OnManagerSound;
        BindItemBag();
        RefreshActive();
        if (!donation) { FillDropdowns(); FillTexts(); }
        Mgr.SendMode0((byte)(donation ? 2 : 1));
        UpdateCanSend();
        InventoryButtons?.Invoke();
    }

    /// <summary>Close: the reward (and preview) bindings released, the listener dropped, ResetBreakMode.</summary>
    public override void Close()
    {
        if (closing) return;
        closing = true;
        if (Mgr != null)
        {
            Mgr.SlotChanged -= OnSlotChanged;
            Mgr.InventoryRefresh -= OnInventoryRefresh;
            Mgr.ResultForwarded -= OnResult;
            Mgr.PreviewReceived -= OnPreview;
            Mgr.RefreshActive -= RefreshActive;
            Mgr.ResetBreak -= ResetBreak;
            Mgr.Sound -= OnManagerSound;
            ReleaseBindings();
            Mgr.DialogOpen = false;
            Mgr.BreakInProgress = false;
            Mgr.CanSendOptions = false;
            Mgr.ResetBreakMode();
        }
        base.Close();
        bagChild = null;
        Closed?.Invoke();
    }

    void OnSlotChanged(int i) { if (!closing) Host.Push(SlotName(i)); RefreshActive(); }
    void OnInventoryRefresh(long uid) => InventorySlotRefresh?.Invoke(uid);
    void OnManagerSound(string s) { if (!closing) Sound?.Invoke(s); }

    void UpdateCanSend() { Mgr.CanSendOptions = Mgr.DialogOpen && !InProgress; }

    /// <summary>BindItemBag (FUN_1419a1430): the bag's "bagId" is the type name, its slots are pushed again.</summary>
    void BindItemBag()
    {
        if (Host?.Entity == null || closing) return;
        bagChild ??= Host.Child("m_itemBag");
        if (bagChild != null) Host.Set(bagChild, "bagId", Bag);
        for (int i = 0; i < ItemBreakManager.Slots; i++) Host.Push(SlotName(i));
    }

    /// <summary>vfunc_85(true, 0): m_break is enabled while the list holds an item; the bag scrolls back to the top
    /// rows (max(last / 5 - 2, 0)).</summary>
    void RefreshActive()
    {
        if (Host?.Entity == null || closing || Mgr == null) return;
        if (Host.Child("m_break") is { } b) Host.Set(b, "enabled", Mgr.HasBreakItems);
    }

    /// <summary>The bag scroll of vfunc_85: <paramref name="reset"/> goes to max(last / 5 - 2, 0); else the row of
    /// <paramref name="index"/> (clamped to the last row) is scrolled into the 3 rows the bag shows.</summary>
    public static int ScrollTarget(bool reset, int index, int lastIndex, int top)
    {
        if (reset) return Math.Max(lastIndex / 5 - 2, 0);
        int lastRow = Math.Max(lastIndex / 5, 0);
        int row = Math.Clamp(index / 5, 0, lastRow);
        if (row < top) return row;
        if (top + 2 < row) return row - 2;
        return top;
    }

    // ---- the type and rank dropdowns, the texts (UIItemBreakDialog::vfunc_46) ----------------------------------------

    void FillDropdowns()
    {
        typeIndex.Clear();
        if (Host.Child("m_bundleTypeDropdown") is { } type)
        {
            var dp = Host.Get(type, "dataProvider");
            if (!Rt.IsNullish(dp))
            {
                Host.Invoke(dp, "cleanUp");
                int n = 0;
                void Row(int t, string name)
                {
                    Host.Invoke(dp, "push", new Dictionary<string, object> { ["type"] = t, ["label"] = Loc("s_inventory_tab_" + name) });
                    typeIndex[t] = n++;
                }
                Row(TabEquip, "equip");
                if (Model.IsFeatureEnabled(0x3a)) Row(TabSkin, "skin");
                if (Model.IsFeatureEnabled(0x2e)) Row(TabGem, "gem");
                Row(TabPet, "pet");
                Host.Set(type, "menuRowCount", (uint)Math.Max(n, 1));
                Host.Set(type, "selectedIndex", typeIndex.TryGetValue(BundleType, out int sel) ? sel : 0);
                Host.Invoke(type, "invalidate");
                Host.Invoke(type, "validateNow");
            }
        }
        if (Host.Child("m_bundleRankDropdown") is { } rank)
        {
            var dp = Host.Get(rank, "dataProvider");
            if (!Rt.IsNullish(dp))
            {
                Host.Invoke(dp, "cleanUp");
                string below = Common("s_itembreak_rank_or_below");     // GetStringByKey (uiinventorydialog.cpp:2609)
                for (int r = 1; r < 7; r++)
                    Host.Invoke(dp, "push", new Dictionary<string, object> { ["rank"] = r, ["label"] = RankName(r) + " " + below });
                Host.Set(rank, "menuRowCount", 6u);
                Host.Set(rank, "selectedIndex", BundleRank - 1);
                Host.Invoke(rank, "invalidate");
                Host.Invoke(rank, "validateNow");
            }
        }
    }

    string RankName(int rank) => Model.Tables.Common("s_word_rank_" + rank.ToString("00", CultureInfo.InvariantCulture));

    void FillTexts()
    {
        bool skin = Model.IsFeatureEnabled(0x3a), socket = Model.IsFeatureEnabled(0x2e);
        if (Host.Child("m_desc") is { } desc)
        {
            string text = Rt.IsNullish(Host.Get(desc, "text")) ? "" : Rt.ToString(Host.Get(desc, "text"));
            if (skin) text = text + ", " + Loc("s_itembreak_desc_skin");
            if (socket) text = text + ", " + Loc("s_itembreak_desc_socket");
            Host.Set(desc, "text", text);
        }
        if (Host.Child("m_info") is { } info)
        {
            string tip = Loc("s_itembreak_tooltip_desc");
            if (skin) tip += "\n" + Loc("s_itembreak_tooltip_skin");
            if (socket) tip += "\n" + Loc("s_itembreak_tooltip_socket");
            Host.Set(info, "tooltipText", tip);
        }
    }

    // ---- progress ----------------------------------------------------------------------------------------------------

    /// <summary>StartBreak: Invoke "startBreak", m_188 = 1, the s_strExtractProgress sound.</summary>
    public void StartBreak()
    {
        InProgress = true;
        Mgr.BreakInProgress = true;
        UpdateCanSend();
        if (Host.Entity != null) Host.Invoke(Host.Entity, "startBreak");
        Sound?.Invoke("Extract_Progress");                     // s_strExtractProgress (uiinventorydialog.cpp:476, :2514)
    }

    /// <summary>EndBreak: Invoke "endBreak", m_188 = 0, the s_strExtractEnd sound ("Extract_End").</summary>
    public void EndBreak()
    {
        InProgress = false;
        Mgr.BreakInProgress = false;
        UpdateCanSend();
        if (Host.Entity != null) Host.Invoke(Host.Entity, "endBreak");
        Sound?.Invoke("Extract_End");                          // s_strExtractEnd (:475, :2526)
    }

    /// <summary>ResetBreak: m_188 = 0, Invoke "resetBreak".</summary>
    public void ResetBreak()
    {
        InProgress = false;
        Mgr.BreakInProgress = false;
        UpdateCanSend();
        if (Host.Entity != null) Host.Invoke(Host.Entity, "resetBreak");
    }

    // ---- result and preview rows -------------------------------------------------------------------------------------

    void ReleaseBindings()
    {
        foreach (var b in rewardBinds) Model.ItemSlots.Remove(b);
        rewardBinds.Clear();
    }

    /// <summary>BindRewardItem: the item made at its own rank with the amount, bound as "&lt;type&gt;_&lt;id&gt;" (flags 0x401).</summary>
    string BindReward(string suffix, int itemId, uint amount)
    {
        string bind = TypeName + "_" + suffix + itemId.ToString(CultureInfo.InvariantCulture);
        var item = Item.Create(Model.Tables.Items, itemId, Model.Tables.Items.Get(itemId)?.Rank ?? 0);
        if (item != null)
        {
            item.SetCountClamped((int)amount);
            Model.ItemSlots[bind] = new ItemInfoSlot(bind, item, 0, 0x415, RewardFlags);
            rewardBinds.Add(bind);
        }
        return bind;
    }

    string ItemDisplayName(int itemId) => Model.Tables.ItemName(itemId);

    /// <summary>vfunc_84 (mode 3 rewards): the old bindings released; a row per reward with an amount, then "setResult"
    /// {dp} and EndBreak. The donation dialog only releases and ends.</summary>
    void OnResult(ItemDismantlePacket p)
    {
        if (Host?.Entity == null || closing) return;
        ReleaseBindings();
        if (donation) { EndBreak(); return; }
        var rows = new List<object>();
        foreach (var r in p.Rewards ?? Array.Empty<DismantleReward>())
        {
            if (r.Amount == 0) continue;
            string text = RowText(r.ItemId, (uint)r.Amount, (uint)r.Amount);
            string bind = BindReward("", r.ItemId, (uint)r.Amount);
            rows.Add(Host.Obj(("slotBinding", bind), ("toggle", false), ("text", text)));
        }
        Host.Invoke(Host.Entity, "setResult", Host.Obj(("dp", DataProvider(rows))));
        EndBreak();
    }

    /// <summary>vfunc_(mode 5 preview): by item id (the last duplicate wins), nothing for a 0..0 range, a fixed text when low
    /// equals high, else the random one; "setPreview" {dp}.</summary>
    void OnPreview(ItemDismantlePacket p)
    {
        if (Host?.Entity == null || closing || donation) return;
        ReleaseBindings();
        var byId = new SortedDictionary<int, DismantlePreview>();
        foreach (var v in p.Previews ?? Array.Empty<DismantlePreview>()) byId[v.ItemId] = v;
        var rows = new List<object>();
        foreach (var (id, v) in byId)
        {
            if (v.Low == 0 && v.High == 0) continue;
            string text = RowText(id, (uint)v.Low, (uint)v.High);
            string bind = BindReward("preview_", id, (uint)v.Low);
            rows.Add(Host.Obj(("slotBinding", bind), ("toggle", false), ("text", text)));
        }
        Host.Invoke(Host.Entity, "setPreview", Host.Obj(("dp", DataProvider(rows))));
    }

    /// <summary>A result or preview row's text: FUN_14028dfa0 of "s_itembreak_preview_static" (the item's name, the amount) when
    /// low equals high, else of "s_itembreak_preview_random" (the name, low, high) (uiinventorydialog.cpp:2736, :2878-2880).</summary>
    internal string RowText(int itemId, uint low, uint high) => low == high
        ? ClientFormat.Format(Common("s_itembreak_preview_static"), ItemDisplayName(itemId), low)
        : ClientFormat.Format(Common("s_itembreak_preview_random"), ItemDisplayName(itemId), low, high);

    object DataProvider(IEnumerable<object> values)
    {
        var dp = Host.RT.Construct(Host.RT.GetClass("scaleform.clik.data.DataProvider"), Array.Empty<object>());
        foreach (var v in values) Host.Invoke(dp, "push", v);
        return dp;
    }

    // ---- callbacks ---------------------------------------------------------------------------------------------------

    /// <summary>vfunc_28: onClickBreak (the rarity confirms), onSendBreak, and the break dialog's onResetBreak,
    /// OnBundleType, OnBundleRank, OnBundleRegister. The slots' NotifyDragSlot callbacks: <see cref="SlotUse"/> and
    /// <see cref="SlotShiftClick"/>.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (Mgr == null) return false;
        switch (name.ToLowerInvariant())
        {
            case "onclickbreak": ClickBreak(); return true;
            case "onsendbreak": Mgr.SendMode3(); return true;
            case "notifyslotuse": return SlotUse(args);
            case "notifyslotshiftclick": return SlotShiftClick(args);
        }
        if (donation) return false;
        switch (name.ToLowerInvariant())
        {
            case "onresetbreak": InventoryReset?.Invoke(); return true;
            case "onbundletype": BundleType = args.Length > 0 ? Rt.ToInt32(args[0]) : 0; return true;
            case "onbundlerank": BundleRank = args.Length > 0 ? Rt.ToInt32(args[0]) : 1; return true;
            case "onbundleregister": Mgr.SendMode6((byte)BundleType, (byte)BundleRank); return true;
        }
        return false;
    }

    /// <summary>The break button: nothing listed is the 0xe6 notice box; rarity 5 and 6 ask with a must-check checkbox, rarity 4 a
    /// plain yes/no (0x8f9, donation 0x8fa), otherwise the break starts. Yes (result 0) starts it.</summary>
    public void ClickBreak()
    {
        if (!Mgr.HasBreakItems)
        {
            var none = MessageBoxQueue.Create(NoItemsBoxType, Model.Tables.Common(NoItemsNotice), null, "");
            ShowData(none);
            return;
        }
        int rarity = Mgr.MaxItemRarity;
        if (rarity is 5 or 6)
        {
            string key = rarity == 6 ? "s_itembreak_artifact_item" : "s_itembreak_legendary_item";
            var box = MessageBoxQueue.Create(LegendaryBoxType, Model.Tables.Common(key), null, "");
            box.SetButtonPreset(1);
            box.MustCheck = true;
            box.EnterAllowed = false;
            box.SetCheckbox0(Model.Tables.Common(key + "_checkbox"), false);
            box.Callback = ConfirmStart;
            ShowData(box);
        }
        else if (rarity == 4)
        {
            var box = MessageBoxQueue.Create(CommonBoxType, Model.Tables.Common(donation ? 0x8fa : 0x8f9), null, "");
            box.Callback = ConfirmStart;
            ShowData(box);
        }
        else StartBreak();
    }

    bool ConfirmStart(int result, MessageBoxData box)
    {
        if (result == 0 && !closing) StartBreak();
        return true;
    }

    // ---- the slots (ItemBreakSlot) -----------------------------------------------------------------------------------

    /// <summary>
    /// NotifyDragSlot::SlotUse for a break slot: button 0 without Shift, DragSlot::vfunc_13 (pushed again); button 1, the
    /// preview key held (MS2DragSlot::vfunc_19, the slot enabled by its ctor) the preview (not ported: raised), else
    /// ItemBreakSlot::vfunc_14 (itembreakslot.cpp:95): <see cref="RemoveFromSlot"/>.
    /// </summary>
    bool SlotUse(object[] args)
    {
        if (args.Length < 1 || !ParseSlot(args[0], out int i)) return false;
        int button = args.Length > 1 ? InventoryController.ArgInt(args[1]) : 0;
        if (button == 0) { if (!ShiftHeld()) Host.Push(SlotName(i)); }
        else if (button == 1)
        {
            if (ActionKeyDown(0x49406)) Button?.Invoke("notifySlotUse");
            else RemoveFromSlot(i);
        }
        return true;
    }

    /// <summary>NotifyDragSlot::SlotShiftClick, ItemBreakSlot::vfunc_15 (itembreakslot.cpp:105): the item linked
    /// (FUN_140d2cfc0), then DragSlot::vfunc_15 (pushed again).</summary>
    bool SlotShiftClick(object[] args)
    {
        if (args.Length < 1 || !ParseSlot(args[0], out int i)) return false;
        if (ItemOf(i) is { } item) LinkItem?.Invoke(item);
        Host.Push(SlotName(i));
        return true;
    }

    public override IEnumerable<string> GetBagSlotBindings(string bag)
    {
        if (bag != Bag) return Array.Empty<string>();
        return Enumerable.Range(0, ItemBreakManager.Slots).Select(SlotName);
    }

    public override object GetBinding(string name)
    {
        if (!ParseSlot(name, out int i)) return base.GetBinding(name);
        var item = ItemOf(i);
        if (item == null || Model == null || !Model.HasLocalPlayer) return null;      // vfunc_8: no local player, none
        var slot = new List<(string, object)>();
        return ItemSlot.Build(slot, item, Model.HasLocalPlayer, SlotFlags, Model, Model.ItemSlotSources) ? Host.Obj(slot.ToArray()) : null;
    }

    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (!ParseSlot(name, out int i)) return base.GetSlotTooltip(name);
        var item = ItemOf(i);
        if (Model == null || !Model.HasLocalPlayer) return new AsTooltip();           // vfunc_0: no local player, none
        var tip = new ItemTooltip(Model, TooltipFlags) { Localize = k => Host.Player.Translate?.Invoke(k) ?? k };
        if (item == null || !tip.Fill(item)) return new AsTooltip();
        tip.SetEnablePreview(item);                                                    // the slot is enabled (its ctor)
        return tip.Data;
    }

    /// <summary>ItemBreakSlot::vfunc_25: GetSlotItemClone(index), the listed item (an empty slot has none).</summary>
    Item ItemOf(int slot) => Mgr.Uid(slot) is long uid and not 0 ? Mgr.Find(uid) : null;

    public long? BreakSlotUid(string name) => ParseSlot(name, out int i) && Mgr != null ? Mgr.Uid(i) : null;
    public bool IsBreakSlot(string name) => ParseSlot(name, out _);

    // ---- IDragSlotOwner ----------------------------------------------------------------------------------------------

    public bool IsDragSlot(string name) => ParseSlot(name, out _);
    public void RefreshSlot(string name) { if (ParseSlot(name, out _)) Host.Push(name); }
    public void SlotDragStarted(string name) { }
    public void SlotDropUnresolved(string name) => RefreshSlot(name);

    /// <summary>ItemBreakSlot::vfunc_18 (itembreakslot.cpp:89): an inventory item dropped on a slot is TryAddItem(item, slot
    /// index); the source slot is refreshed either way.</summary>
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner)
    {
        if (ParseSlot(name, out int index) && Mgr != null && sourceOwner is IInvenSlotOwner inv && inv.InvenSlotUid(source) is long uid
            && inv.FindItem(uid) is { } item)
            Mgr.TryAddItem(item, index);
        sourceOwner?.RefreshSlot(source);
    }

    /// <summary>ItemBreakSlot::vfunc_14 (right-click): RequestRemoveItem for the slot's item.</summary>
    public void RemoveFromSlot(int slot)
    {
        if (Mgr != null && ItemOf(slot) is { } item) Mgr.RequestRemoveItem(item);
    }

    /// <summary>InvenSlot's use mode 8/9 (invenslot.cpp:1000): with the dialog open and idle, TryAddItem(item, -1).</summary>
    public void AddFromInventory(Item item)
    {
        if (Mgr != null && Mgr.DialogOpen && !InProgress && item != null) Mgr.TryAddItem(item, -1);
    }

    /// <summary>InvenSlot::vfunc_18's ItemBreakSlot branch (invenslot.cpp:1241-1246): a listed item dragged onto an inventory
    /// slot: when it can be broken, the slot's item replaces it if that can be broken too (TryAddItem at the old slot), else the
    /// listed item is removed.</summary>
    public void DroppedOnInventory(long oldUid, Item mine)
    {
        if (Mgr == null || Mgr.Find(oldUid) is not { } old || !Mgr.CanBreakItem(old)) return;
        if (mine != null && Mgr.CanBreakItem(mine)) Mgr.TryAddItem(mine, old.Slot);
        else Mgr.RequestRemoveItem(old);
    }

    /// <summary>vfunc_15 per frame: not hidden, so the items the inventory no longer has are removed.</summary>
    public void Tick()
    {
        if (!closing && !IsClosed && Mgr != null && Mgr.DialogOpen) Mgr.RemoveMissingItems();   // vfunc_15: vfunc_55 (closed) returns first
    }
}
