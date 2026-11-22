using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The own-avatar side of the beauty shop (the client's Customize, m_1b8 of the dialog): the edit item of the shop's slot and its
/// preview. Step 3 of the beauty shop (the avatar rebuild) fills it; with none set the dialog does the paths the client does
/// without a Customize (GetFee's default payment, no list build, no buy).
/// </summary>
public interface IBeautyCustomize
{
    int EditItemId { get; }
    /// <summary>Customize::FUN_140cd2a00: anything of the slot was changed (a save is worth asking for).</summary>
    bool HasChanges { get; }
    bool IsEditItemAdded { get; }
    bool IsEditItemColorChanged { get; }
    bool IsDragging { get; }
    void UpdateEditItems();
    void CommitSlot(int slot);
    void RestoreSlot(int slot);
}

/// <summary>
/// The beauty shop window, UIBeautyShopDialog (client/ms2/uibeautyshopdialog.cpp, UI id 0x4eee, type name "BeautyShopDialog",
/// movie uibeautyshopdialog, sub scene 0x620001f of UISceneGameBeautyShop). The state is the shop packet (inbound 0x31, mode 0:
/// <see cref="BeautyShopPacket"/>); this controller shows the fee and runs the dialog's paths: the fee row (UpdateFee), the fee by
/// shop page (GetFee), the bill rows (BuildBillRows), the buy (@0x1418c9f20), the close ask (RequestClose) and the packets' modes
/// 0, 8, 9, 0xb, 0xc, 0x10 and 0x11.
/// <para>The price text (GetFeePriceText @0x14134a220), the affordability (CanPayFee @0x14134a1d0: CanPayCurrency, then
/// HasEnoughCurrency) and the "not enough" box (FUN_14134a2e0) belong to the shop host, so <see cref="FeePriceText"/>,
/// <see cref="CanPay"/> and <see cref="NotEnough"/> are set by it and a path that needs one does nothing without it.
/// STILL-OPEN: the list rows (FUN_1418c6870, WriteItemInfo), the page's per-frame
/// state (vfunc_15), the camera dialog 0x4ef2, the style select dialog and the UIBillDialog (0x4e7b) beauty kind are step 3 /
/// not ported: the bill is handed to <see cref="OpenBill"/>.</para>
/// </summary>
public sealed class BeautyShopDialogController : UiController
{
    public override string Movie => "uibeautyshopdialog";
    protected override string EntityMethod => "initData";
    public override string TypeName => "BeautyShopDialog";

    public const int UiId = 0x4eee, SubScene = 0x620001f, ExitSubScene = 0x620001d;
    /// <summary>Shop pages (m_290, the first byte of the mode 0 packet): 1 = items, 2 = special hair (a list of slots), 3 = colours, 4 = skin.</summary>
    public const int PageItems = 1, PageSpecial = 2, PageColors = 3, PageSkin = 4;
    /// <summary>Shop kinds (the shop data's category; GetShopKind): 1 hair, 2 makeup, 3, 4 skin, 5.</summary>
    public const int KindHair = 1;

    // ---- host-set parts -----------------------------------------------------------------------------------------------
    public IBeautyCustomize Customize;
    public BeautyShopSales Sales = new();
    public Func<int> PlayerLevel = () => 0;
    public Action<IOutPacket> Send = _ => { };
    public Action<MessageBoxData> ShowData = d => MessageBoxQueue.Show(d);
    /// <summary>g_143ab2a90.HasItemWithTag: the bag holds an item with the tag (IsCouponSaleActive).</summary>
    public Func<int, bool> HasItemWithTag = _ => false;
    /// <summary>GetFeePriceText @0x14134a220: the price text of a payment and an amount (set by the host).</summary>
    public Func<BeautyPayment, long, string> FeePriceText;
    /// <summary>CanPayFee @0x14134a1d0 (unit_141347b10.cpp:614: CanPayCurrency, then HasEnoughCurrency), set by the host.</summary>
    public Func<BeautyPayment, long, bool> CanPay;
    /// <summary>FUN_14134a2e0 (@0x14134a2e0, unit_141347b10.cpp:630): ShowLackCurrency(kind, item, 0), the "not enough" box of a payment.</summary>
    public Action<BeautyPayment> NotEnough;
    /// <summary>OnClickSave's UIBillDialog (0x4e7b: FUN_140f0a350's dialog, SetData, OpenDialog; <see cref="BillDialogController"/>): the bill
    /// is handed over and its answer comes back through <see cref="OnBillAnswer"/>. Unset = the dialog does not exist, nothing opens.</summary>
    public Action<BillData> OpenBill;
    /// <summary>The bill dialog is open. It is modal (UIModalDialog vfunc_8 @0x141a55380, uimodaldialog.cpp:32, SetModalDialogId): while it
    /// is, the shop takes no movie callback. Unset = no bill is open.</summary>
    public Func<bool> BillIsOpen;
    /// <summary>Closes the open bill without an answer; the shop's close calls it, so the bill never outlives the shop.</summary>
    public Action CloseBill;
    /// <summary>The open bill's total is shown again (OnGameEventChanged @0x1418c9780: IsDialogOpen(0x4e7b), FUN_1418cda70).</summary>
    public Action<uint> UpdateBill;
    /// <summary>The notice of an item id of mode 9 (FUN_141811980(1, id): the item's name in the common string 0x996).</summary>
    public Func<int, string> ItemNoticeText;
    /// <summary>GetMaxSlotCount / FUN_140f01220 (mode 0x11's slot count): not recovered, set by the host.</summary>
    public int MaxSlotCount = int.MaxValue, ExtendBy;
    /// <summary>The name of an item id (the string table's item name, FUN_141811980(1, id)).</summary>
    public Func<int, string> ItemName = _ => "";
    /// <summary>ShowSystemNotice: a notice line.</summary>
    public event Action<string> NoticeText;
    /// <summary>FUN_14134a2e0-like exit: the page was left (FUN_1418c9cd0: the field camera and the sub scene 0x620001d).</summary>
    public event Action Left;
    public event Action Closed;

    // ---- state (the mode 0 packet) -------------------------------------------------------------------------------------
    public int Page;                                  // m_290
    public BeautyShopData Shop;                       // m_1d0
    public int SavedStyles, StyleCount;               // m_294, m_298
    public long Context;                              // m_2a0
    public readonly List<BeautyShopItem> Items = new();   // m_198
    public bool Loaded;                               // m_2a8
    /// <summary>m_2d8: a buy / preview answer is awaited.</summary>
    public bool Waiting;
    /// <summary>The shop root's input is off (FUN_1412761e0(m_30, 0) at uibeautyshopdialog.cpp:1860, after the send): the shop takes no
    /// callback and the host blocks its view until the answer turns it on (FUN_1418ca380 :1885, on a result other than 0) or an item event does (<see cref="OnItemEvent"/>).
    /// STILL-OPEN: the host feeds only vfunc_24; the own-equip (vfunc_21) and skin-colour (vfunc_25) events have no LiveSession source yet.</summary>
    public bool InputOff;
    /// <summary>m_2b8: the id of the item worn in the shop's slot, kept before the mode-7 send (uibeautyshopdialog.cpp:1840, FUN_141655e10
    /// of FUN_140f01070(kind)); 0 = none. <see cref="WornItemId"/> reads it, the host sets it.</summary>
    public int OldItemId;
    /// <summary>m_2c8: the item the server put on (the item event listener vfunc_21 @0x1418c57b0, kept on the special page only); 0 = none.</summary>
    public int NewItemId;
    /// <summary>FUN_141655e10(FUN_140f01070(kind)): the id of the item worn in a slot; null or 0 = none.</summary>
    public Func<int, int> WornItemId;
    /// <summary>OpenStyleSelectDialog (@0x1418cb000) of the old and the new item id, raised by mode 0xb when both match the kept ones.
    /// STILL-OPEN: the dialog itself (its item rows with WriteItemInfo, the random select and the selected-index callbacks) is not
    /// ported and the host leaves this unset, so the answer opens nothing.</summary>
    public event Action<int, int> StyleSelectRequested;
    /// <summary>The Customize's commit sends (SendColors, SendAdded, SendModified) behind CommitSlot on the items, colour and skin pages are
    /// ported. STILL-OPEN: they are not, so without this the buy on those pages does nothing (no local commit, no send).</summary>
    public bool CommitSendsPorted;
    /// <summary>The answer of the special hair buy (mode 0xb: m_2b8 kept before the send, OpenStyleSelectDialog @0x1418cb000) is ported.
    /// STILL-OPEN: it is not, so without this the mode-7 send is not made.</summary>
    public bool StyleSelectPorted;
    /// <summary>m_2b5: the style select dialog is open.</summary>
    public bool StyleSelectOpen;
    /// <summary>m_2ac: "preview with cap".</summary>
    public bool PreviewWithCap;
    /// <summary>vfunc_55: the dialog is closed (every packet mode but 0 and 9 is dropped).</summary>
    public bool Closing;
    /// <summary>The initData page was built.</summary>
    public bool PageBuilt;

    public int SaleEventId => Shop?.ShopId ?? 0;
    public int ShopKind => Shop?.Category ?? 0;

    // ---- load / close ------------------------------------------------------------------------------------------------

    /// <summary>OnLoadComplete: m_2a8 = 1, and the page is built when the shop list already arrived.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        Loaded = true;
        Closing = false;
        if (Shop != null) BuildPage();
    }

    public override void Close()
    {
        if (Closing) return;
        Closing = true;
        Loaded = false; PageBuilt = false; Waiting = false; InputOff = false; StyleSelectOpen = false; Items.Clear();
        CloseBill?.Invoke();
        base.Close();
        Closed?.Invoke();
    }

    // ---- packet 0x31 -------------------------------------------------------------------------------------------------

    /// <summary>OnBeautyShopPacket (@0x1418c80b0): modes 0 and 9 act first; the others act only while the dialog is open and reset m_2d8.</summary>
    public void OnPacket(BeautyShopPacket p)
    {
        if (p.Mode == 0)
        {
            Items.Clear();
            Page = p.Kind;
            Shop = p.Shop;
            SavedStyles = p.SlotCount;
            StyleCount = p.SavedStyles;
            Context = p.Context;
            Items.AddRange(p.Items);
            if (Loaded) BuildPage();
        }
        else if (p.Mode == 9 && p.ShowsItemNotice)
        {
            var text = ItemNoticeText?.Invoke(p.ItemId);
            if (text != null) NoticeText?.Invoke(text);
        }
        // the gated modes read the string tables and show boxes: they act only once the movie is attached
        if (Closing || Host == null) return;
        Waiting = false;
        switch (p.Mode)
        {
            case 0xb:
                // :1541-1548: the two ids of the packet must be the kept ones (m_2b8, m_2c8), as the items are not null
                if (OldItemId != 0 && OldItemId == p.OldItemId && NewItemId != 0 && NewItemId == p.NewItemId)
                    StyleSelectRequested?.Invoke(OldItemId, NewItemId);
                break;
            case 0xc:
                NoticeText?.Invoke(CouponNotice(p.CouponId, p.Mailed));
                break;
            case 0x10:
                StyleCount++;
                NoticeText?.Invoke(Model?.Tables.Common("s_beauty_msg_complete_save_hair_style"));
                break;
            case 0x11:
                StyleCount++;
                SavedStyles = Math.Min(MaxSlotCount, SavedStyles + ExtendBy);
                NoticeText?.Invoke(Model?.Tables.Common("s_beauty_msg_complete_save_hair_style"));
                break;
            case 8:
                OnBuyResult(p.Result);
                break;
        }
    }


    string CouponNotice(int coupon, bool mailed)
    {
        if (coupon == 0) return Model?.Tables.Common("s_beauty_msg_complete_apply_special_hair");
        string key = mailed ? "s_beauty_msg_complete_apply_return_coupon_to_mail" : "s_beauty_msg_complete_apply_return_coupon";
        return ClientFormat.Format(Model?.Tables.Common(key), ItemName(coupon));
    }

    /// <summary>Mode 8: the buy result. Not enough: the payment's box; colour error, slot max: a box; 0x14, 0x12: nothing here;
    /// 7: common string 0x273 whose ok leaves the page; any other: common string 0x272 with the result. A result other than 0
    /// unlocks the page (m_2d8 = 0, UI input back on).</summary>
    void OnBuyResult(int result)
    {
        var fee = GetFee(out _);
        switch (result)
        {
            case BeautyResult.NotEnough: NotEnough?.Invoke(fee); break;
            case BeautyResult.ColorError: ShowData(MessageBoxQueue.CreateWithCallback(Model.Tables.Common("s_beauty_msg_error_color"), 0, (_, _) => true)); break;
            case BeautyResult.SlotMax: ShowData(MessageBoxQueue.CreateWithCallback(Model.Tables.Common("s_beauty_msg_error_style_slot_max"), 0, (_, _) => true)); break;
            case BeautyResult.Unavailable: /* FUN_14134a120(4, 0, 0): a purchase shortcut, not ported */ break;
            case BeautyResult.Silent: break;
            case BeautyResult.MessageBoxOk:
                ShowData(MessageBoxQueue.CreateWithCallback(Model.Tables.Common(0x273), 0, LeaveOnOk));
                break;
            default:
                ShowData(MessageBoxQueue.CreateWithCallback(ClientFormat.Format(Model.Tables.Common(0x272), result), 0, LeaveOnOk));
                break;
        }
        if (result != 0) { Waiting = false; InputOff = false; }   // FUN_1418ca380 (:1885): input back on
    }

    bool LeaveOnOk(int button, MessageBoxData _)
    {
        if (button == 0) Leave();
        return true;
    }

    // ---- fee ---------------------------------------------------------------------------------------------------------

    /// <summary>GetFee (@0x1418c74e0): the payment of the page and its amount. Without a Customize, or for a page that charges
    /// nothing, the shop's own payment and 0.</summary>
    public BeautyPayment GetFee(out uint price)
    {
        price = 0;
        if (Shop == null) return null;
        var c = Customize;
        if (c == null) return Shop.Payment;
        BeautyPayment fee = null;
        switch (Page)
        {
            case PageItems:
                if (c.IsEditItemAdded) fee = RowOf(c.EditItemId)?.Payment ?? Shop.Payment;
                else if (c.IsEditItemColorChanged) fee = Shop.RecolorPayment;
                break;
            case PageColors:
                if (c.IsEditItemColorChanged) fee = Shop.RecolorPayment;
                break;
            case PageSpecial:
                fee = Shop.Payment;
                break;
        }
        if (fee != null) price = (uint)fee.Amount;
        return fee ?? Shop.Payment;
    }

    /// <summary>The row the page picked for an item (m_198 by item id; the client keeps the row of the last OnChangeItem, m_278).</summary>
    BeautyShopItem RowOf(int itemId) => picked is { } r && r.ItemId == itemId ? r : Items.FirstOrDefault(i => i.ItemId == itemId);
    BeautyShopItem picked;

    /// <summary>UpdateFee (@0x1418c6d40): the page's fee row: "setFee"(text, text, sale, discounted).</summary>
    public void UpdateFee(BeautyPayment fee, uint price)
    {
        if (Host?.Entity == null) return;
        int level = PlayerLevel();
        bool sale = Sales.SaleIndex(SaleEventId, level) == 0;
        if (fee == null)
        {
            string d = Model.Tables.Common("s_beauty_fee_default");
            Host.Invoke(Host.Entity, "setFee", d, d, sale, false);
            return;
        }
        if (FeePriceText == null) return;   // GetFeePriceText is not recovered: nothing is shown rather than a guessed text
        uint cut = Sales.DiscountedPrice(SaleEventId, price, level);
        if (cut != price) Host.Invoke(Host.Entity, "setFee", Thousands(price), FeePriceText(fee, cut), sale, true);
        else Host.Invoke(Host.Entity, "setFee", FeePriceText(fee, price), cut.ToString(System.Globalization.CultureInfo.InvariantCulture), sale, false);
    }

    // ---- page ---------------------------------------------------------------------------------------------------------

    /// <summary>@0x1418c8ee0: the page's initData (type, category, isEquipCap, isPcBang, useDetailColor) and the empty fee row.
    /// The guide movie, the detail colour feature and the Customize build are step 3 (STILL-OPEN).</summary>
    void BuildPage()
    {
        if (SaleEventId == 0 || Host?.Entity == null) return;
        UpdateFee(null, 0);
        bool sale = Sales.SaleIndex(SaleEventId, PlayerLevel()) == 0;
        Host.Invoke(Host.Entity, "initData", Host.Obj(
            ("type", Page), ("category", ShopKind), ("isEquipCap", IsEquipCap?.Invoke() ?? false), ("isPcBang", sale),
            ("useDetailColor", false)));
        PageBuilt = true;
        RefreshButtons();
    }

    /// <summary>FUN_1418cbc70: the hair shop and the avatar wears a cap (the equip set is the host's).</summary>
    public Func<bool> IsEquipCap;

    /// <summary>@0x1418c7ab0: the save / reset buttons and the fee row of the page.</summary>
    public void RefreshButtons()
    {
        var c = Customize;
        if (c == null) return;
        bool state = c.HasChanges, save = state, reset = state;
        if (Page == PageSpecial) save = reset = state = true;
        if (Host?.Entity != null)
        {
            if (Host.Child("m_save") is { } s) Host.Set(s, "enabled", save);
            if (Host.Child("m_reset") is { } r) Host.Set(r, "enabled", reset);
        }
        var fee = GetFee(out uint price);
        if (state || price > 0) UpdateFee(fee, price); else UpdateFee(null, 0);
    }

    /// <summary>NotifyGameEventChanged "SaleBeautyShop" (@0x1418c9780): the sale price is shown again.</summary>
    public void OnSaleEvent()
    {
        var fee = GetFee(out uint price);
        UpdateFee(fee, price);
        if (Host?.Entity != null && Host.Child("m_pcBang") is { } pc)
            Host.Set(pc, "visible", Sales.SaleIndex(SaleEventId, PlayerLevel()) == 0);
        UpdateBill?.Invoke(price);
    }

    // ---- close / leave --------------------------------------------------------------------------------------------------

    /// <summary>FUN_1418c9cd0: the worn slot is restored and the page is left.</summary>
    public void Leave()
    {
        Customize?.RestoreSlot(WornSlot());
        Left?.Invoke();
    }

    /// <summary>FUN_140f01070 of the shop kind: the equip slot the shop edits (1 -> 1, 2 -> 3, 3 -> 2, else 0; recovered at
    /// uibeautyshopstyledialog.cpp:381, <see cref="BeautyStyleDialogController.SlotOfKind"/>).</summary>
    public Func<int, int> SlotOfKind = BeautyStyleDialogController.SlotOfKind;
    int WornSlot() => SlotOfKind(ShopKind);

    /// <summary>RequestClose (@0x1418c9d80): leave at once for the special page or with nothing changed; else ask (common string 0x271).</summary>
    public void RequestClose()
    {
        if (Page == PageSpecial || Customize == null || !Customize.HasChanges) { Leave(); return; }
        ShowData(MessageBoxQueue.CreateWithCallback(Model.Tables.Common(0x271), 1, (button, _) => { if (button == 0) Leave(); return true; }));
    }

    // ---- bill -----------------------------------------------------------------------------------------------------------

    /// <summary>BuildBillRows (@0x1418ca510): the bill's category and rows, and the total price. Null without a Customize or a page.</summary>
    public BeautyBill BuildBill()
    {
        var c = Customize;
        if (c == null || Page == 0 || FeePriceText == null) return null;
        var fee = GetFee(out uint price);
        string row = Model.Tables.Common("s_bill_list_row");
        string detail, cost, category;
        if (Page == PageSpecial)
        {
            category = Host.Player.Translate?.Invoke("$s_beauty_special_hair") ?? "";
            detail = ClientFormat.Format(row, Host.Player.Translate?.Invoke("$s_beauty_change_style") ?? "");
            cost = FeePriceText(fee, price);
        }
        else
        {
            category = ItemName(c.EditItemId);
            if (c.IsEditItemAdded)
            {
                detail = ClientFormat.Format(row, Host.Player.Translate?.Invoke("$s_beauty_change_style") ?? "");
                cost = FeePriceText(fee, price);
            }
            else if (c.IsEditItemColorChanged)
            {
                detail = ClientFormat.Format(row, Host.Player.Translate?.Invoke("$s_beauty_change_color") ?? "");
                cost = FeePriceText(fee, price);
            }
            else
            {
                // nothing changed: the "none" row (the crop row needs Customize::HasSelectedChangeFlag2, step 3)
                price = 0;
                detail = Host.Player.Translate?.Invoke("$s_beauty_none") ?? "";
                cost = Model.Tables.Common("s_beauty_fee_default");
            }
        }
        return new BeautyBill(category, detail, cost, price);
    }

    /// <summary>The bill title's coupon line (@0x1418cb920): the shop's coupon item, else the common string of the shop kind.</summary>
    public string BillCouponLine(int kind, int mode)
    {
        if (Shop != null && Shop.DisplayCouponId != 0)
            return ClientFormat.Format(Model.Tables.Common("s_bill_coupon_beauty_coupon"), ItemName(Shop.DisplayCouponId));
        return kind switch
        {
            1 => mode == 2 ? Model.Tables.Common("s_bill_coupon_special") : Model.Tables.Common(0x991),
            3 => Model.Tables.Common(0x992),
            2 => Model.Tables.Common(0x993),
            4 => Model.Tables.Common(0x994),
            5 => Model.Tables.Common(0x995),
            _ => "",
        };
    }

    /// <summary>@0x1418cbb40: the bill's warning (the special hair page only).</summary>
    public string BillWarning(int mode) => mode == 2 ? Model.Tables.Common("s_beauty_msg_bill_warning_special_hair") : "";

    /// <summary>IsCouponSaleActive (@0x1418cbc00): <paramref name="coupon"/> true and the bag holds the shop's sale coupon item.</summary>
    public bool IsCouponSaleActive(bool coupon) => coupon && Shop is { SaleCouponTag: not 0 } s && HasItemWithTag(s.SaleCouponTag);

    /// <summary>The bill dialog's answer (OnMessage): result 0 buys.</summary>
    public void OnBillAnswer(int result, bool useCoupon)
    {
        couponChecked = useCoupon;
        if (result == 0) Buy();
    }
    bool couponChecked;

    /// <summary>OnClickSave (@0x1418c8700, uibeautyshopdialog.cpp:771): with the bill dialog there, the rows are built (none: nothing opens), the
    /// coupon is offered when the sale coupon is active and the fee after the sale is not 0, and the bill (shop kind 1, the fee's type, item and
    /// icon tag, the price) goes to the dialog. The check box's line, warning and state are set only for a shop with a display coupon (m_1d0.m_20).</summary>
    public void ClickSave()
    {
        if (OpenBill == null) return;
        var bill = BuildBill();
        if (bill == null) return;
        int kind = ShopKind;
        string title = BillCouponLine(kind, Page);
        bool coupon = IsCouponSaleActive(true);
        var fee = GetFee(out uint nFee);
        if (Sales.DiscountedPrice(SaleEventId, nFee, PlayerLevel()) == 0) coupon = false;
        var data = new BillData
        {
            Answer = OnBillAnswer,
            Category = bill.Category,
            Rows = new() { (bill.Detail, bill.PriceText) },
            ShopKind = 1,
            FeeType = fee?.Type ?? 0,
            FeeItemId = fee?.ItemId ?? 0,
            FeeIconTag = fee?.IconTag ?? "",
            Price = bill.Total,
        };
        if (Shop is { DisplayCouponId: not 0 })
        {
            data.Checked = coupon;
            data.CheckText = title;
            data.Warning = BillWarning(Page);
        }
        OpenBill(data);
    }

    /// <summary>The buy (@0x1418c9f20): the preview cap off, the price after the sale; a fee that can't be paid (and no coupon) shows
    /// the "not enough" box and buys nothing. The special hair page sends mode 7 after restoring the slot; the other pages commit the
    /// slot. The page then waits for the answer (m_2d8).</summary>
    public bool Buy()
    {
        var c = Customize;
        if (c == null || CanPay == null) return false;
        // an unported path does nothing: not the commit without its send, not the send without its answer
        if (!(Page == PageSpecial ? StyleSelectPorted : CommitSendsPorted)) return false;
        if (PreviewWithCap) PreviewWithCap = false;
        int ev = SaleEventId;
        var fee = GetFee(out uint price);
        if (fee == null) return false;
        uint pay = Sales.DiscountedPrice(ev, price, PlayerLevel());
        bool coupon = IsCouponSaleActive(couponChecked);
        if (!CanPay(fee, pay) && !coupon) { NotEnough?.Invoke(fee); return false; }
        int slot = WornSlot();
        if (Page == PageSpecial)
        {
            c.RestoreSlot(slot);
            OldItemId = WornItemId?.Invoke(slot) ?? 0;   // m_2b8 (:1840), after the restore
            Send(new BeautyBuyRequest(ev, couponChecked, Context));
        }
        else c.CommitSlot(slot);
        InputOff = true;   // FUN_1412761e0(m_30, 0) (:1860)
        Waiting = true;
        return true;
    }

    /// <summary>The item event listeners vfunc_21 (@0x1418c57b0, the own equip's new item; <paramref name="newItemId"/> is its id),
    /// vfunc_24 (@0x1418c5850, OnItemUpdate) and vfunc_25 (@0x1418c58a0, the skin colour) of the dialog (uibeautyshopdialog.cpp:896-925):
    /// with the movie attached (m_30) each runs FUN_1418ca240 (:1868): the wait ends, the slot is restored and the shop takes input again,
    /// which is how a successful buy unlocks; vfunc_21 also keeps the item on the special page (m_2c8). STILL-OPEN: FUN_1418c7ce0 (the
    /// refresh), FUN_1418ca400 (the hair apply effect), vfunc_84 (the list rebuild) and, on page 1, FUN_1418cb6a0.</summary>
    public void OnItemEvent(int newItemId = 0, bool ownEquip = false)
    {
        if (Host?.Entity == null) return;   // m_30 == 0
        Waiting = false;
        if (Customize != null)               // m_1b8 == 0 (:1871): the restore, the input and the rest do not run
        {
            Customize.RestoreSlot(WornSlot());
            InputOff = false;               // FUN_1412761e0(m_30, 1)
        }
        if (ownEquip && Page == PageSpecial) NewItemId = newItemId;   // vfunc_21 stores m_2c8 after the call
    }

    // ---- movie callbacks (vfunc_28) -----------------------------------------------------------------------------------

    public override bool OnCallback(string name, object[] args)
    {
        if (BillIsOpen?.Invoke() == true || InputOff) return false;   // behind the modal bill, and after a buy until its answer, the shop takes no input
        switch (name.ToLowerInvariant())
        {
            case "onclickclose": RequestClose(); return true;
            case "onclicksave": ClickSave(); return true;
            case "onclickreset":
                Customize?.RestoreSlot(WornSlot());
                RefreshButtons();
                return true;
        }
        return false;
    }
}

/// <summary>The beauty bill (BuildBillRows): the category, the one row (detail, price text) and the total.</summary>
public sealed record BeautyBill(string Category, string Detail, string PriceText, uint Total);
