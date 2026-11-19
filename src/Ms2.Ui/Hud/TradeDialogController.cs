using System.Globalization;
using Ms2.Avm2;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The trade window, UITradeDialogEx (client/ms2/uitradedialogex.cpp, UI id 0x4e83, movie UITradeDialogEx), with its two
/// slot bags of TradeSlots (trademanagerex_2.cpp): "TradeDialogEx_my" (mine) and "TradeDialogEx_target", 5 slots each,
/// named "%s.%d". The trade state lives in <see cref="TradeBook"/> (CTradeManagerEx); this controller is what the manager
/// calls through <see cref="ITradeDialog"/>.
/// <para>STILL-OPEN: the partner's fee reduction (CEquipment::GetAbilityRate(0x40)) and PC::IsVip of the local player are
/// <see cref="FeeReduceRate"/> / <see cref="IsVip"/>, 0 and false until the host has them.</para>
/// </summary>
public sealed class TradeDialogController : UiController, ITradeDialog, IDragSlotOwner
{
    public override string Movie => "uitradedialogex";
    protected override string EntityMethod => "initData";
    /// <summary>GetTypeName @0x141b271d0.</summary>
    public override string TypeName => "TradeDialogEx";

    public const int UiId = TradeBook.DialogId;
    public const int SlotCount = 5;
    /// <summary>The ctor's bag ids: GetTypeName + "_my" / "_target".</summary>
    public const string MyBag = "TradeDialogEx_my", TargetBag = "TradeDialogEx_target";
    /// <summary>The flags TradeSlot::vfunc_8 (@0x14185e1b0) builds its slot with.</summary>
    public const int SlotFlags = 0x1c81f;

    public TradeBook Book;
    /// <summary>PC::vfunc_77 of the local player: its name.</summary>
    public Func<string> MyName = () => "";
    /// <summary>FUN_141028be0(the local player's id (FUN_141656a80), its picture (FUN_1416612e0), 1, ""): its profile image.</summary>
    public Func<string> MyProfile = () => "";
    /// <summary>FUN_141028be0(id, picture, 1, ""): a character's profile image path.</summary>
    public Func<long, string, string> ProfilePath = (_, _) => "";
    /// <summary>DAT_143ab2898: the mesos the player owns. Unset: the player state's.</summary>
    public Func<long> OwnedMeso;
    /// <summary>CEquipment::GetAbilityRate(0x40) of the local player (STILL-OPEN: 0).</summary>
    public Func<float> FeeReduceRate = () => 0f;
    /// <summary>PC::IsVip of the local player (STILL-OPEN: false).</summary>
    public Func<bool> IsVip = () => false;
    /// <summary>CInputKeyHandler::IsActionKeyDown(0x49406, 0, 0): the preview key is held (MS2DragSlot::vfunc_19).</summary>
    public Func<int, bool> ActionKeyDown = _ => false;
    /// <summary>GetKeyState(VK_SHIFT) (NotifyDragSlot::SlotUse: a double click with Shift does nothing).</summary>
    public Func<bool> ShiftHeld = () => false;
    /// <summary>ShowMessageBox(text, type, 0, "").</summary>
    public Action<string, int> ShowBox = (t, type) => MessageBoxQueue.ShowMessageBox(t, type, null, "");
    /// <summary>FUN_140d2cfc0: the item linked into the chat input (shift click on a slot).</summary>
    public Action<Item> LinkItem;
    /// <summary>The inventory window (0x4e2d) when it is open (its vfunc_55 is 0), else null.</summary>
    public Func<UiController> OpenInventory = () => null;

    /// <summary>PlaySystemSound(event, ""): "trade_lock", "trade_unlock".</summary>
    public event Action<string> SystemSound;
    /// <summary>TradeSlot::PlayItemSound: the item's material sound (ms2sound/sound/item).</summary>
    public event Action<Item> ItemSound;
    /// <summary>vfunc_66 with the inventory closed: the "openWindowPos" message to the inventory with this id.</summary>
    public event Action<int> InventoryWindowPos;
    /// <summary>vfunc_67 with the inventory open: UIInventoryDialog::FUN_141999e00 (its m_19c = 7).</summary>
    public event Action InventoryTradeClosed;
    /// <summary>A slot path not ported (MS2DragSlot::vfunc_16, the preview): raised by its callback name.</summary>
    public event Action<string> Button;
    /// <summary>The dialog closed (vfunc_67 done).</summary>
    public event Action Closed;

    readonly Item[] mine = new Item[SlotCount], target = new Item[SlotCount];
    long myMeso;          // m_nMyMeso
    long uidTarget;       // m_uidTarget
    string targetName = "", targetChar = "";
    object myInput, myInputText, targetInput, myFee, targetFee, myMesoText, targetMesoText;
    bool closing;

    long Owned => OwnedMeso?.Invoke() ?? Pc.Mesos;
    long MaxMeso => Model.Tables.TradeMaxMeso;
    long MinMeso => Model.Tables.TradeMinMeso;
    string CommonKey(string key) => Model.Tables.Common(key) ?? "";
    string F(string key, params object[] args) => ClientFormat.Format(CommonKey(key), args);
    static string Group(long n) => NumberText.Group(n.ToString(CultureInfo.InvariantCulture));

    // ---- ITradeDialog ------------------------------------------------------------------------------------------------

    public string Partner => targetName;

    /// <summary>
    /// vfunc_67 (@0x141b27250): the status listener dropped, SendTradeMode7, the manager reset (FUN_14185d7b0), the open
    /// inventory's FUN_141999e00, the slot bags destroyed, vfunc_47(1), UIDialog's close and the inputs dropped.
    /// </summary>
    public override void Close()
    {
        if (closing) return;
        closing = true;
        Book?.SendClose();
        Book?.Reset();
        if (OpenInventory() != null) InventoryTradeClosed?.Invoke();
        DestroySlotBags();
        base.Close();
        myInput = myInputText = targetInput = myFee = targetFee = myMesoText = targetMesoText = null;
        Closed?.Invoke();
    }

    /// <summary>SetTradeSlotItem @0x141b27de0: no movie, nothing; the side's slot (nonzero side = mine) takes the item and is
    /// pushed again (vfunc_10).</summary>
    public void SetTradeSlotItem(bool isMine, int slot, Item item)
    {
        if (Host?.Entity == null || slot < 0 || slot >= SlotCount) return;
        (isMine ? mine : target)[slot] = item;
        Host.Push(SlotName(isMine, slot));
    }

    /// <summary>ClearTradeSlot @0x141b27fa0: the side's slot emptied and pushed again.</summary>
    public void ClearTradeSlot(bool isMine, int slot) => SetTradeSlotItem(isMine, slot, null);

    /// <summary>SetSideMeso @0x141b28130: side 1 my input, else the target's; its text the grouped mesos, vfunc_24(1)
    /// (invalidate), then UpdateFee.</summary>
    public void SetSideMeso(int side, long meso)
    {
        if (Host?.Entity == null) return;
        var input = side == 1 ? myInput : targetInput;
        if (input == null) return;
        Host.Set(input, "text", Group(meso));
        Host.Invoke(input, "invalidate");
        UpdateFee(meso, side == 1);
    }

    /// <summary>FUN_141b28290 @0x141b28290: the movie as an ASDialog; "trade_lock" and latch(side), or "trade_unlock" and
    /// unlatch(side).</summary>
    public void SetLatch(bool isMine, bool on)
    {
        if (Host?.Entity == null) return;
        SystemSound?.Invoke(on ? "trade_lock" : "trade_unlock");
        Host.Invoke(on ? "latch" : "unlatch", isMine ? 1 : 0);
    }

    /// <summary>FUN_141b297e0 @0x141b297e0: results 0xd, 0xe, 0x11, 0x12, 0x13 clear my meso input.</summary>
    public void RequestFailed(int code)
    {
        if (Host?.Entity == null) return;
        if (code is 0xe or 0xd or 0x11 or 0x12 or 0x13) SetMesoInput(0);
    }

    // ---- load --------------------------------------------------------------------------------------------------------

    /// <summary>OnLoadInit @0x141b27a60: CreateSlotBags (both bags emptied), vfunc_47(0).</summary>
    public void OnLoadInit() => DestroySlotBags();

    void DestroySlotBags()
    {
        Array.Clear(mine);
        Array.Clear(target);
    }

    /// <summary>OnLoadComplete @0x141b274f0: UIDialog's, then FUN_141b27580 (initData). The status listener is
    /// <see cref="OnStatus"/>.</summary>
    public override void OnLoadComplete()
    {
        closing = false;
        base.OnLoadComplete();
        InitData();
    }

    /// <summary>
    /// vfunc_66 (@0x141b273b0) after UIDialog's: the inventory open, the two side by side (FUN_141275c30); closed, the
    /// "openWindowPos" message with this id.
    /// </summary>
    public void PlaceBesideInventory()
    {
        if (Host?.Entity == null) return;
        var place = OpenPosition();
        if (OpenInventory() is { } inv) PlaceSideBySide(this, place, inv);
        else InventoryWindowPos?.Invoke(UiId);
    }

    /// <summary>
    /// FUN_141b27580: the partner from the manager; no local player, nothing; initData with myName, targetName, myProfile,
    /// targetProfile, slotCount 5 and the two bag ids, then BindControls.
    /// </summary>
    void InitData()
    {
        if (Host?.Entity == null || Book == null) return;
        uidTarget = Book.Partner;
        targetName = Book.PartnerName;
        targetChar = Book.PartnerKey;
        if (!Model.HasLocalPlayer) return;
        var data = Host.Obj(
            ("myName", MyName() ?? ""),
            ("targetName", targetName),
            ("myProfile", MyProfile() ?? ""),
            ("targetProfile", ProfilePath(uidTarget, targetChar) ?? ""),
            ("slotCount", SlotCount),
            ("mySlotBagId", MyBag),
            ("targetSlotBagId", TargetBag));
        Host.Invoke("initData", data);
        BindControls();
    }

    void Listen(object target, string type, Action<object> a)
    {
        Func<object[], object> f = args => { a(args.Length > 0 ? args[0] : null); return null; };
        Host.Invoke(target, "addEventListener", type, f, false, 0, false);
    }

    /// <summary>
    /// BindControls @0x141b284b0: SetMyMeso(owned); my input's change handler, its text field's maxChars (the grouped
    /// TradeMaxMeso's length) and focus-out listener; the target input; the fee texts at 0; the meso number texts at 0;
    /// feature 0xc5 off: both meso texts hidden and each fee text moved to its meso text's y; m_feeNotice the
    /// s_trade_notice_fee text with TradeFeePercent.
    /// </summary>
    void BindControls()
    {
        SetMyMeso(Owned);
        if ((myInput = Host.Child("m_myInputMeso")) != null)
        {
            Listen(myInput, "change", OnMesoInputChanged);
            if ((myInputText = Host.Get(myInput, "textField")) is AsObject)
            {
                Host.Set(myInputText, "maxChars", (double)Group(MaxMeso).Length);
                Listen(myInputText, "focusOut", _ => OnMesoInputFocusLost());
            }
            else myInputText = null;
        }
        targetInput = Host.Child("m_targetInputMeso");
        myFee = Host.Child("m_myTradeFee");
        targetFee = Host.Child("m_targetTradeFee");
        SetFeeText(true, 0);
        SetFeeText(false, 0);
        myMesoText = Host.Child("m_myMesoNumberString");
        targetMesoText = Host.Child("m_targetMesoNumberString");
        SetMesoText(true, 0);
        SetMesoText(false, 0);
        if (!Model.IsFeatureEnabled(0xc5))
        {
            if (myMesoText != null) Host.SetVisible(myMesoText, false);
            if (myFee != null && myMesoText != null) Host.Set(myFee, "y", Host.Get(myMesoText, "y"));
            if (targetMesoText != null) Host.SetVisible(targetMesoText, false);
            if (targetFee != null && targetMesoText != null) Host.Set(targetFee, "y", Host.Get(targetMesoText, "y"));
        }
        if (Host.Child("m_feeNotice") is { } notice) Host.Set(notice, "htmlText", F("s_trade_notice_fee", Model.Tables.TradeFeePercent));
    }

    // ---- the mesos ---------------------------------------------------------------------------------------------------

    /// <summary>SetMyMeso @0x141b27d90: m_nMyMeso = min(mesos, TradeMaxMeso).</summary>
    void SetMyMeso(long meso) => myMeso = Math.Min(meso, MaxMeso);

    /// <summary>vfunc_oa0_11 @0x141b28440 (the meso status): SetMyMeso(the mesos sent last + the owned mesos).</summary>
    public override void OnStatus(StatusEvent e)
    {
        if (e != StatusEvent.Meso || Host?.Entity == null || Book == null) return;
        SetMyMeso(Book.LastMesoSent + Owned);
    }

    /// <summary>SetMesoText @0x141b28d10: feature 0xc5 the meso number text (FUN_141275860), else "".</summary>
    void SetMesoText(bool isMine, long meso)
    {
        var t = isMine ? myMesoText : targetMesoText;
        if (t == null) return;
        Host.Set(t, "htmlText", Model.IsFeatureEnabled(0xc5) ? NumberText.MesoNumberText(meso, CommonKey) : "");
    }

    /// <summary>SetFeeText @0x141b28e30: s_trade_fee formatted with the (ungrouped) fee.</summary>
    void SetFeeText(bool isMine, long fee)
    {
        string text = F("s_trade_fee", fee);
        var t = isMine ? myFee : targetFee;
        if (t == null) return;
        Host.Set(t, "htmlText", text);
    }

    /// <summary>FUN_14087d150 @0x14087d150: 0 for 0; else mesos * TradeFeePercent / 100, times (1 - rate) in float, not
    /// below 0.</summary>
    public static long TradeFee(long meso, float rate, int percent)
    {
        if (meso == 0) return 0;
        long fee = meso * percent / 100;
        fee = (long)(fee * (1.0f - rate));
        return fee < 0 ? 0 : fee;
    }

    /// <summary>FUN_14087d200 @0x14087d200: 0, or within TradeMinMeso..TradeMaxMeso.</summary>
    public static bool MesoInRange(long meso, long min, long max) => meso == 0 || (meso >= min && meso <= max);

    /// <summary>
    /// UpdateFee @0x141b28f40: the side's fee text needed; for the partner's side the local player's fee reduction and VIP
    /// state (no player, nothing); the fee text s_trade_fee with the grouped fee, s_vip_reduce_fee added for a VIP; then
    /// SetMesoText.
    /// </summary>
    void UpdateFee(long meso, bool isMine)
    {
        var t = isMine ? myFee : targetFee;
        if (t == null) return;
        float reduce = 0;
        bool vip = false;
        if (!isMine)
        {
            if (!Model.HasLocalPlayer) return;
            reduce = FeeReduceRate();
            vip = IsVip();
        }
        long fee = TradeFee(meso, reduce, Model.Tables.TradeFeePercent);
        string text = F("s_trade_fee", Group(fee));
        if (vip) text += CommonKey("s_vip_reduce_fee");
        Host.Set(t, "htmlText", text);
        SetMesoText(isMine, meso);
    }

    /// <summary>OnMesoInputChanged @0x141b291a0: the event target's text to digits (FUN_14083cf10) and parsed; above
    /// m_nMyMeso it is clamped and the text set; then UpdateFee(mine).</summary>
    void OnMesoInputChanged(object evt)
    {
        var input = evt != null ? Host.Get(evt, "target") : null;
        if (input == null) return;
        long meso = NumberText.ParseInt64W(NumberText.NumericText(Rt.ToString(Host.Get(input, "text"))));
        if (myMeso < meso)
        {
            meso = myMeso;
            Host.Set(input, "text", Group(meso));
        }
        UpdateFee(meso, true);
    }

    /// <summary>OnMesoInputFocusLost @0x141b29330: my input's text parsed and clamped to 0..m_nMyMeso; UpdateFee when the
    /// clamp changed it; then ValidateMeso.</summary>
    void OnMesoInputFocusLost()
    {
        if (myInput == null) return;
        long parsed = NumberText.ParseInt64W(NumberText.NumericText(Rt.ToString(Host.Get(myInput, "text"))));
        long meso = parsed;
        if (meso < 0) meso = 0;
        if (meso > myMeso) meso = myMeso;
        if (parsed != meso) UpdateFee(meso, true);
        ValidateMeso(meso);
    }

    /// <summary>SetMesoInput @0x141b294f0: my input's text the grouped mesos, vfunc_24(1), UpdateFee(mine).</summary>
    void SetMesoInput(long meso)
    {
        if (myInput == null) return;
        Host.Set(myInput, "text", Group(meso));
        Host.Invoke(myInput, "invalidate");
        UpdateFee(meso, true);
    }

    /// <summary>
    /// ValidateMeso @0x141b295a0: out of TradeMinMeso..TradeMaxMeso (FUN_14087d200), the input reset to 0 and the box
    /// s_trade_error_invalid_meso_range (type 0); more than m_nMyMeso, the input reset and the box
    /// s_trade_error_lack_meso_2; then always SendTradeMode10 of the mesos (0 after a reset).
    /// </summary>
    bool ValidateMeso(long meso)
    {
        bool valid = true;
        if (!MesoInRange(meso, MinMeso, MaxMeso))
        {
            SetMesoInput(0);
            meso = 0;
            ShowBox(F("s_trade_error_invalid_meso_range", Group(MinMeso), Group(MaxMeso)), 0);
            valid = false;
        }
        if (myMeso < meso)
        {
            SetMesoInput(0);
            meso = 0;
            ShowBox(CommonKey("s_trade_error_lack_meso_2"), 0);
            valid = false;
        }
        Book?.SendMeso(meso);
        return valid;
    }

    // ---- callbacks ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// vfunc_28 @0x141b27aa0 after UIDialog's and the slot callbacks (FUN_1418fc760): OnClose / OnCancel close; OnLock
    /// parses its text (_atoi64, FUN_1407182f0) and, when ValidateMeso passes, SendTradeMode11; OnConfirm SendTradeMode13.
    /// </summary>
    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "notifyslotuse": return SlotUse(args);
            case "notifyslotshiftclick": return SlotShiftClick(args);
            case "onclose":
            case "oncancel":
                Close();
                return true;
            case "onlock":
                if (args.Length > 0)
                {
                    long meso = NumberText.ParseInt64W(Rt.ToString(args[0]));
                    if (ValidateMeso(meso)) Book?.SendLock();
                }
                return true;
            case "onconfirm":
                Book?.SendConfirm();
                return true;
        }
        return false;
    }

    // ---- the slots (TradeSlot) -----------------------------------------------------------------------------------------

    public static string SlotName(bool isMine, int i) => (isMine ? MyBag : TargetBag) + "." + i.ToString(CultureInfo.InvariantCulture);

    /// <summary>A slot name's side and index; false for another name.</summary>
    static bool ParseSlot(object name, out bool isMine, out int index)
    {
        isMine = false;
        index = -1;
        if (name is not string n) return false;
        foreach (var (bag, side) in new[] { (MyBag, true), (TargetBag, false) })
            if (n.StartsWith(bag + ".", StringComparison.Ordinal) && int.TryParse(n.AsSpan(bag.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int i)
                && i >= 0 && i < SlotCount)
            {
                isMine = side;
                index = i;
                return true;
            }
        return false;
    }

    Item SlotItem(bool isMine, int i) => (isMine ? mine : target)[i];

    public override IEnumerable<string> GetBagSlotBindings(string bag) =>
        bag == MyBag ? Enumerable.Range(0, SlotCount).Select(i => SlotName(true, i))
        : bag == TargetBag ? Enumerable.Range(0, SlotCount).Select(i => SlotName(false, i))
        : Array.Empty<string>();

    /// <summary>TradeSlot::vfunc_8 @0x14185e1b0: none without an item; ASSlot(2) built with flags 0x1c81f; the partner's
    /// slots DisableDrag.</summary>
    public override object GetBinding(string name)
    {
        if (!ParseSlot(name, out bool isMine, out int i)) return base.GetBinding(name);
        var item = SlotItem(isMine, i);
        if (item == null || Model == null) return null;
        var slot = new List<(string, object)>();
        if (!ItemSlot.Build(slot, item, Model.HasLocalPlayer, SlotFlags, Model, Model.ItemSlotSources)) return null;
        if (!isMine) slot.Add(("disableDrag", 1));
        return Host.Obj(slot.ToArray());
    }

    /// <summary>
    /// TradeSlot::vfunc_0 @0x14185df40: ASTooltip(the local player, 0x80 mine / 0x1000 the partner's, 2) filled with the
    /// item (FUN_1409e0d40); enablePuton Common 0x750 for mine, "" for the partner's; enableUseDbClick ""; the preview (the
    /// slot is enabled). An empty object when it gives none.
    /// </summary>
    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (!ParseSlot(name, out bool isMine, out int i)) return base.GetSlotTooltip(name);
        var item = SlotItem(isMine, i);
        var tip = new ItemTooltip(Model, isMine ? 0x80 : 0x1000) { Localize = k => Host.Player.Translate?.Invoke(k) ?? k };
        if (item == null || !tip.Fill(item)) return new AsTooltip();
        tip.SetEnablePuton(isMine ? Model.Tables.Common(0x750) : "");
        tip.SetEnableUseDbClick("");
        tip.SetEnablePreview(item);
        return tip.Data;
    }

    /// <summary>
    /// NotifyDragSlot::SlotUse for a trade slot: button 0 without Shift, DragSlot::vfunc_13 (pushed again); button 1, the
    /// preview key held (MS2DragSlot::vfunc_19) the preview (not ported: raised), else TradeSlot::vfunc_14: on my side the
    /// item sound and SendTradeMode9(uid, index).
    /// </summary>
    bool SlotUse(object[] args)
    {
        if (args.Length < 1 || !ParseSlot(args[0], out bool isMine, out int i)) return false;
        int button = args.Length > 1 ? InventoryController.ArgInt(args[1]) : 0;
        if (button == 0)
        {
            if (!ShiftHeld()) Host.Push(SlotName(isMine, i));
        }
        else if (button == 1)
        {
            if (ActionKeyDown(0x49406)) Button?.Invoke("notifySlotUse");
            else if (isMine)
            {
                var item = mine[i];
                if (item != null) ItemSound?.Invoke(item);
                Book?.SendRemoveItem(item?.Uid ?? 0, i);
            }
        }
        return true;
    }

    /// <summary>NotifyDragSlot::SlotShiftClick, TradeSlot::vfunc_15: the item linked (FUN_140d2cfc0), then pushed again.</summary>
    bool SlotShiftClick(object[] args)
    {
        if (args.Length < 1 || !ParseSlot(args[0], out bool isMine, out int i)) return false;
        if (SlotItem(isMine, i) is { } item) LinkItem?.Invoke(item);
        Host.Push(SlotName(isMine, i));
        return true;
    }

    // ---- IDragSlotOwner ----------------------------------------------------------------------------------------------

    public bool IsDragSlot(string name) => ParseSlot(name, out _, out _);

    public void RefreshSlot(string name)
    {
        if (ParseSlot(name, out _, out _)) Host.Push(name);
    }

    /// <summary>TradeSlot::vfunc_20 @0x14185e2c0: DragSlot's, then the item sound.</summary>
    public void SlotDragStarted(string name)
    {
        if (ParseSlot(name, out bool isMine, out int i) && SlotItem(isMine, i) is { } item) ItemSound?.Invoke(item);
    }

    /// <summary>
    /// TradeSlot::vfunc_18 @0x14185de60: an inventory slot dropped here; on my side its item (FindItemByUid) goes to
    /// InvenSlot::TryAddToTrade; then the inventory slot's vfunc_10. Another source: its vfunc_10.
    /// </summary>
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner)
    {
        if (!ParseSlot(name, out bool isMine, out _)) return;
        if (sourceOwner is InventoryController inv && inv.InvenSlotUid(source) is long uid)
        {
            if (isMine && inv.FindItem(uid) is { } item) inv.TryAddToTrade(item);
            inv.RefreshSlot(source);
            return;
        }
        sourceOwner?.RefreshSlot(source);
    }

    public void SlotDropUnresolved(string name) => RefreshSlot(name);
}
