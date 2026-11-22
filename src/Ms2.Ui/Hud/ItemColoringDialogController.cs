using Ms2.Quest;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui.Hud;

/// <summary>
/// The own-avatar side of the item colour dialog (the client's Customize, m_268 of UIItemColoringDialog): the worn item being
/// dyed and the colour edit. The item controllers behind it (<c>FUN_140f32290</c>'s item rows, the palette apply) are STILL-OPEN, so
/// the host supplies the object that implements this.
/// </summary>
public interface IItemColoringCustomize
{
    /// <summary>Customize::GetEditItem is not null.</summary>
    bool HasEditItem { get; }
    /// <summary>Customize::GetEditItemUid.</summary>
    long EditItemUid { get; }
    /// <summary>Customize::GetEditItemId.</summary>
    int EditItemId { get; }
    /// <summary>Customize::FUN_140cd2a00: anything of the edit item was changed.</summary>
    bool HasChanges { get; }
    bool IsEditItemColorChanged { get; }
    void SelectUid(long uid);
    void RestoreUid(long uid);
    /// <summary>Customize::ApplyPalette(palette, colorSN, 0).</summary>
    void ApplyPalette(uint palette, uint color);
    /// <summary>Customize::SetColors(palette, type, 0).</summary>
    void SetColors(int palette, int color);
    /// <summary>Customize::ResetEdit: the edit becomes the item's new base.</summary>
    void ResetEdit();
    /// <summary>FUN_140f014f0: Customize::RestoreAll.</summary>
    void RestoreAll();
    /// <summary>Customize::GetModifiedAndSelect(uid): the item as dyed, ready to send; null when it is not found.</summary>
    BeautyColorItem GetModifiedAndSelect(long uid);
    /// <summary>The edit item's slot 0 colour (CItemExtraData::GetSlotPaletteId / GetSlotColorId), false without extra data.</summary>
    bool TryGetEditSlotColor(out uint palette, out uint color);
}

/// <summary>A worn, dyeable item of the dialog's list (BuildItems @0x1419ae170: guid, itemid, itemname, iconPath).</summary>
public sealed record ColoringRow(long Uid, int ItemId, string Name, string IconPath);

/// <summary>An item of the basket (m_2a8): its uid and item id.</summary>
public sealed record BasketItem(long Uid, int ItemId);

/// <summary>The bill the dialog hands to UIBillDialog (0x4e7b, OpenBill @0x1419b0ef0): its rows, the total, the coupon line.</summary>
public sealed record ColoringBill(string Category, string PriceText, string Detail, uint Total, bool CouponOffered, string CouponLabel,
    BeautyPayment Fee);

/// <summary>
/// The item colour dialog, UIItemColoringDialog (client/ms2/uiitemcoloringdialog.cpp, UI id 0x4e23, type name "ItemColoringDialog"):
/// the Dye Workshop. The state is the shop packet (inbound 0x31 mode 1: <see cref="BeautyColoringPacket"/>); the dialog lists the worn
/// dyeable items (BuildItems), previews a colour on the Customize, keeps a basket of items to dye (AddCurrentItemToBasket,
/// RemoveBasketItem), shows the fee (SetFee), and sends the basket as Beauty mode 0x16 (@0x1419b0700, <see cref="BeautyColoringRequest"/>).
/// <para>STILL-OPEN: the dye pose and the hair-apply effect (PlayDyePose @0x1419ae790, FUN_1419af750), the pose of PreviewItem, the per-frame
/// tick (vfunc_15), the camera edit dialog 0x4ef2, the item-event listener's registration (the host calls <see cref="OnItemDyed"/>),
/// the tooltips (getTooltipFee, getTooltipColor, the row's tooltipFunction), vfunc_8(1) of the page build, the Customize's
/// FUN_140cd3820 in <see cref="Restart"/>, the "my colour" tooltip text of OnChangeDetailColor, IsPaletteColorLocked, the feature
/// switches and the UIBillDialog itself: each is a host-set hook that does nothing when unset.</para>
/// </summary>
public sealed class ItemColoringDialogController : UiController
{
    public override string Movie => "uiitemcoloringdialog";
    protected override string EntityMethod => "initData";
    public override string TypeName => "ItemColoringDialog";

    public const int UiId = 0x4e23, BillId = 0x4e7b, ShopCategory = 5, ShopKindColoring = 3;

    // ---- host-set parts -----------------------------------------------------------------------------------------------
    public IItemColoringCustomize Customize;                 // m_268
    public BeautyShopSales Sales = new();
    public Func<int> PlayerLevel = () => 0;                  // FUN_1419b1a50
    public Action<IOutPacket> Send = _ => { };
    public Action<MessageBoxData> ShowData = d => MessageBoxQueue.Show(d);
    /// <summary>The worn items BuildItems lists (FUN_1411e3000(equips, 4, 0, 1) without the real-item, FUN_1411dcda0 and IsCustomizable skips).</summary>
    public Func<IReadOnlyList<ColoringRow>> EquippedRows;
    /// <summary>g_143ab2a90.FUN_140a5f7b0(tag, 0): the bag's count of the items with the coupon tag.</summary>
    public Func<int, int> CouponCountOfTag = _ => 0;
    /// <summary>GetFeePriceText (not recovered): the price text of a payment and an amount.</summary>
    public Func<BeautyPayment, long, string> FeePriceText;
    /// <summary>FUN_141349ef0(paymentType, 0, price): what is left of the payment's money after paying the price.</summary>
    public Func<BeautyPayment, long, long> MoneyLeft;
    /// <summary>FUN_14134a2e0 (not recovered): the "not enough" message of a payment.</summary>
    public Action<BeautyPayment> NotEnough;
    /// <summary>OpenBill's UIBillDialog (not ported): the answer comes back through <see cref="OnBillAnswer"/>.</summary>
    public Action<ColoringBill> OpenBillDialog;
    /// <summary>CloseDialog(0x4e7b) of Restart.</summary>
    public Action CloseBillDialog;
    /// <summary>UIBillDialog::FUN_1418cda70(price): the open bill shows the sale price again.</summary>
    public Action<uint> UpdateBillPrice;
    /// <summary>IsDialogOpen(0x4e7b).</summary>
    public Func<bool> IsBillOpen = () => false;
    /// <summary>The name of an item id (the string table's item name, FUN_141811980(1, id)).</summary>
    public Func<int, string> ItemName = _ => "";
    /// <summary>FUN_1412761e0(m_30, enabled): the movie takes input again (true) or waits for the answer (false).</summary>
    public Action<bool> SetInputEnabled;
    /// <summary>IsPaletteColorLocked(palette, colorSN) (@0x140f02a80, the item data table's palette rows): not ported.</summary>
    public Func<uint, uint, bool> IsPaletteColorLocked;
    /// <summary>FUN_140f32290 / FUN_140f328a0: the movie's data object of the edit item (buildCurrentItem, buildResetItem); null when none.</summary>
    public Func<IDictionary<string, object>> CurrentItemData, ResetItemData;
    /// <summary>IsFeatureEnabled(id) of the client's feature switches (0xb6 option, 0xb7 colour scroll).</summary>
    public Func<int, bool> IsFeatureEnabled = _ => false;
    /// <summary>ShowSystemNotice: a notice line.</summary>
    public event Action<string> NoticeText;
    /// <summary>FUN_1419ae940 tail: the page was left (the field camera and the sub scene 0x620001d).</summary>
    public event Action Left;
    public event Action Closed;

    // ---- state ------------------------------------------------------------------------------------------------------
    public int Kind;                                          // m_1a0
    public BeautyShopData Shop;                               // m_1a8
    /// <summary>m_25c: the movie is attached (OnLoadComplete).</summary>
    public bool Loaded;
    public bool Closing;
    public bool PageBuilt;
    public readonly List<BasketItem> Basket = new();         // m_2a8
    public readonly List<long> Listed = new();               // m_278 (the uids of the items the list was built with)
    public int SelectedIndex = -1;                            // m_250
    public (uint Palette, uint Color) PaletteColor;           // m_254
    /// <summary>m_264 / m_260: dye requests sent and not answered; <see cref="Dyeing"/> while there is one.</summary>
    public int Pending;
    public bool Dyeing;
    /// <summary>m_10 of the customize listener (the bill's coupon check) and m_14 (free tries, -1 unlimited), m_08 (its shop id).</summary>
    public bool UseCoupon;
    public int FreeTries = -1;
    public int ListenerShopId;

    public int SaleEventId => Shop?.ShopId ?? 0;           // GetSaleEventId: m_1c0

    // ---- load / close ------------------------------------------------------------------------------------------------

    /// <summary>OnLoadComplete @0x1419ac680: the page is built from the shop data that arrived, and m_25c is set.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        Loaded = true;
        Closing = false;
        BuildPage();
    }

    public override void Close()
    {
        if (Closing) return;
        Closing = true;
        Loaded = false; PageBuilt = false;
        // vfunc_67 (@0x1419ac220): the dialog's state is cleared
        Basket.Clear(); Listed.Clear(); SelectedIndex = -1; PaletteColor = default; Pending = 0; Dyeing = false;
        base.Close();
        Closed?.Invoke();
    }

    // ---- packet 0x31 -------------------------------------------------------------------------------------------------

    /// <summary>OnBeautyShopPacket (@0x1419b00b0, uiitemcoloringdialog.cpp:937). Mode 8 acts once the dialog is open: result 2
    /// shows the fee's "not enough" message, any other result a box (common string 0x272 with the result, tag "errorcoloring"), and
    /// a non-zero one calls <see cref="OnDyeFailed"/>. Mode 1 stores the shop data and builds the page when the movie is attached.</summary>
    public void OnPacket(BeautyColoringPacket p)
    {
        if (p.Mode == 8)
        {
            if (Closing) return;
            if (p.Result == BeautyResult.NotEnough) NotEnough?.Invoke(GetFee());
            else
                ShowData(MessageBoxQueue.CreateWithCallback(ClientFormat.Format(Model.Tables.Common(0x272), p.Result), 0,
                    (button, _) => { if (button == 0) Leave(); return true; }));
            if (p.Result != 0) OnDyeFailed();
        }
        else if (p.Mode == 1)
        {
            Kind = p.Kind;
            Shop = p.Shop;
            if (Loaded) BuildPage();
        }
    }

    // ---- page ---------------------------------------------------------------------------------------------------------

    /// <summary>FUN_1419afe30 (@0x1419afe30): needs the movie, a shop with an id, category 5 and kind 3; calls initData(default fee text,
    /// free-price flag, option, object) and BuildItems. STILL-OPEN: vfunc_8(1).</summary>
    public bool BuildPage()
    {
        if (Host?.Entity == null || Shop == null) return false;
        if (Shop.ShopId == 0 || Shop.Category != ShopCategory || Kind != ShopKindColoring) return false;
        bool free = Sales.SaleIndex(SaleEventId, PlayerLevel()) == 0;
        bool option = IsFeatureEnabled(0xb6);
        var obj = new Dictionary<string, object>();
        bool extended = IsFeatureEnabled(0xb7);           // FUN_140f01540
        obj["colorScrollVisible"] = extended;
        if (extended) obj["fixRow"] = option ? 4 : 5;
        Host.Invoke(Host.Entity, "initData", Model.Tables.Common("s_beauty_fee_default"), free, option, obj);
        BuildItems();
        PageBuilt = true;
        return true;
    }

    /// <summary>BuildItems (@0x1419ae170): the list of the worn dyeable items, then buildItems(items, -1).</summary>
    public void BuildItems()
    {
        if (Host?.Entity == null) return;
        Listed.Clear();
        var items = new List<object>();
        foreach (var row in EquippedRows?.Invoke() ?? Array.Empty<ColoringRow>())
        {
            items.Add(new Dictionary<string, object>
            {
                ["guid"] = row.Uid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["itemid"] = row.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["itemname"] = row.Name,
                ["iconPath"] = row.IconPath,
            });
            Listed.Add(row.Uid);
        }
        Host.Invoke(Host.Entity, "buildItems", items, -1);
    }

    // ---- fee ---------------------------------------------------------------------------------------------------------

    /// <summary>GetPricePerItem (@0x1419b1a90): the shop's recolor payment amount.</summary>
    public uint GetPricePerItem() => (uint)(Shop?.RecolorPayment.Amount ?? 0);

    /// <summary>GetFee (@0x1419b1ac0).</summary>
    public BeautyPayment GetFee() => Shop?.RecolorPayment;

    /// <summary>GetTotalPrice (@0x1419b1710): the basket's count times the price.</summary>
    public long GetTotalPrice() => Basket.Count * (long)GetPricePerItem();

    /// <summary>GetSalePrice (@0x1419b1af0).</summary>
    public uint GetSalePrice(uint price) => Sales.DiscountedPrice(SaleEventId, price, PlayerLevel());

    /// <summary>SetFee (@0x1419af2f0): the fee row; the default text with an empty basket. The price text is the host's
    /// (GetFeePriceText): without it only the default row is shown.</summary>
    public void SetFee(uint price)
    {
        if (Host?.Entity == null) return;
        bool sale = Sales.SaleIndex(SaleEventId, PlayerLevel()) == 0;
        if (Basket.Count == 0)
        {
            string d = Model.Tables.Common("s_beauty_fee_default");
            Host.Invoke(Host.Entity, "setFee", d, d, sale, false);
            return;
        }
        if (FeePriceText == null) return;
        uint cut = GetSalePrice(price);
        var fee = GetFee();
        if (cut != price) Host.Invoke(Host.Entity, "setFee", Thousands(price), FeePriceText(fee, cut), sale, true);
        else Host.Invoke(Host.Entity, "setFee", FeePriceText(fee, price), Thousands(cut), sale, false);
    }

    static string Thousands(uint v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>NotifyGameEventChanged "SaleBeautyShop" (@0x1419adbe0): the fee, the pc-bang mark and an open bill's price.</summary>
    public void OnSaleEvent()
    {
        SetFee((uint)GetTotalPrice());
        if (Host?.Entity != null && Host.Child("m_pcBang") is { } pc)
            Host.Set(pc, "visible", Sales.SaleIndex(SaleEventId, PlayerLevel()) == 0);
        if (IsBillOpen()) UpdateBillPrice?.Invoke(GetSalePrice((uint)GetTotalPrice()));
    }

    // ---- coupons -----------------------------------------------------------------------------------------------------

    /// <summary>HasCoupon (@0x1419b1900), GetCouponCount (@0x1419b1970).</summary>
    public int GetCouponCount(bool coupon) => coupon && Shop is { SaleCouponTag: not 0 } s ? CouponCountOfTag(s.SaleCouponTag) : 0;
    public bool HasCoupon(bool coupon) => GetCouponCount(coupon) > 0;

    /// <summary>GetCouponName (@0x1419b19c0): the shop's display coupon, else common string 0x995.</summary>
    public string GetCouponName() => Shop is { DisplayCouponId: not 0 } s ? ItemName(s.DisplayCouponId) : Model.Tables.Common(0x995);

    /// <summary>FUN_1419b1760 (@0x1419b1760): the basket can be paid, counting the coupons first; the coupons it uses.</summary>
    public bool CanPayBasket(out int coupons)
    {
        coupons = 0;
        if (MoneyLeft == null) return false;
        int have = GetCouponCount(UseCoupon);
        var fee = GetFee();
        long left = MoneyLeft(fee, GetSalePrice((uint)GetTotalPrice()));
        foreach (var _ in Basket)
        {
            uint price = GetSalePrice(GetPricePerItem());
            if (UseCoupon && have > 0) { coupons++; have--; }
            else left -= price;
        }
        return left >= 0;
    }

    // ---- basket ------------------------------------------------------------------------------------------------------

    /// <summary>AddCurrentItemToBasket (@0x1419b1440): the edit item, when its id is the one asked for, becomes the new base and joins the basket.</summary>
    public BasketItem AddCurrentItemToBasket(int itemId)
    {
        var c = Customize;
        if (c == null || !c.HasEditItem || c.EditItemId != itemId) return null;
        var item = new BasketItem(c.EditItemUid, itemId);
        c.ResetEdit();
        Basket.Add(item);
        return item;
    }

    /// <summary>RemoveBasketItem (@0x1419b1590): the first basket item with the id.</summary>
    public BasketItem RemoveBasketItem(int itemId)
    {
        int i = Basket.FindIndex(b => b.ItemId == itemId);
        if (i < 0) return null;
        var item = Basket[i];
        Basket.RemoveAt(i);
        return item;
    }

    /// <summary>ClearBasket (@0x1419b1db0).</summary>
    public void ClearBasket()
    {
        if (Host?.Entity == null || Basket.Count == 0) return;
        Basket.Clear();
        Host.Invoke(Host.Entity, "forceRemoveBasketItemAll");
    }

    /// <summary>RefreshBasketState (@0x1419aeb50): the save button follows the basket, and the fee.</summary>
    public void RefreshBasketState()
    {
        SetButtonEnabled("m_save", Basket.Count > 0);
        SetFee((uint)GetTotalPrice());
    }

    // ---- buttons ----------------------------------------------------------------------------------------------------

    void SetButtonEnabled(string name, bool enabled)
    {
        if (Host?.Entity != null && Host.Child(name) is { } b) Host.Set(b, "enabled", enabled);
    }

    /// <summary>IsSaveEnabled (@0x1419aef80).</summary>
    public bool IsSaveEnabled()
    {
        if (Host?.Entity == null || Host.Child("m_save") is not { } b) return false;
        return Rt.ToBoolean(Host.Get(b, "enabled"));
    }

    /// <summary>RefreshButtons (@0x1419b0620): reset follows the edit's changes; add-to-basket is off for a locked palette colour, on for a changed colour.</summary>
    public void RefreshButtons()
    {
        var c = Customize;
        if (c == null) return;
        if (c.HasChanges)
        {
            SetButtonEnabled("m_reset", true);
            if (IsPaletteColorLocked?.Invoke(PaletteColor.Palette, PaletteColor.Color) == true) SetButtonEnabled("m_addBasket", false);
            else if (c.IsEditItemColorChanged) SetButtonEnabled("m_addBasket", true);
        }
        else
        {
            SetButtonEnabled("m_reset", false);
            SetButtonEnabled("m_addBasket", false);
        }
    }

    void ClearSelected()
    {
        if (Host?.Entity != null) Host.Invoke(Host.Entity, "forceClearSelected");
    }

    // ---- selecting and previewing ---------------------------------------------------------------------------------------

    /// <summary>SelectItem (@0x1419aebc0): a locked colour on the edit item is dropped without asking; otherwise an unsaved colour is asked about first.</summary>
    public void SelectItem(long uid, int index)
    {
        var c = Customize;
        if (c == null) return;
        bool ask = true;
        if (c.HasEditItem && IsPaletteColorLocked?.Invoke(PaletteColor.Palette, PaletteColor.Color) == true)
        {
            ask = false;
            c.RestoreUid(c.EditItemUid);
            if (index < 0) ClearSelected();
        }
        if (ask)
        {
            if (AskChangeItem(uid, index, "")) return;
            c.RestoreUid(c.EditItemUid);
        }
        PreviewItem(uid, index);
    }

    /// <summary>FUN_1419b1fd0 (@0x1419b1fd0): with an unsaved colour on the edit item the box of common string 0x278 asks; its answer
    /// is <see cref="OnChangeItemAnswer"/>. True when it asked.</summary>
    public bool AskChangeItem(long uid, int index, string next)
    {
        var c = Customize;
        if (c == null || !c.IsEditItemColorChanged) return false;
        string name = ItemName(c.EditItemId);
        ShowData(MessageBoxQueue.CreateWithCallback(ClientFormat.Format(Model.Tables.Common(0x278), name), 1,
            (button, _) => { OnChangeItemAnswer(button, uid, index, next); return true; }));
        return true;
    }

    /// <summary>The "changeitem" answer of OnMessage (@0x1419aca80): the edit item's colour is kept in the basket (0) or put back (other);
    /// then the asked item is previewed and a "clickSave" next opens the bill.</summary>
    public void OnChangeItemAnswer(int button, long uid, int index, string next)
    {
        var c = Customize;
        if (c != null)
        {
            long prev = c.EditItemUid;
            if (prev > 0)
            {
                if (button == 0)
                {
                    if (Host?.Entity != null) Host.Invoke(Host.Entity, "forceAddBasket", SelectedIndex, index == -1);
                }
                else
                {
                    c.RestoreUid(prev);
                    if (index < 0) ClearSelected();
                }
            }
        }
        if (uid > 0) PreviewItem(uid, index);
        if (next == "clickSave") OpenBill();
    }

    /// <summary>PreviewItem (@0x1419b02c0): the item is selected on the Customize and the movie builds it. STILL-OPEN: the dye pose.</summary>
    public void PreviewItem(long uid, int index)
    {
        var c = Customize;
        if (c == null) return;
        c.SelectUid(uid);
        if (!c.HasEditItem) return;
        var data = CurrentItemData?.Invoke();
        if (data != null)
        {
            if (c.TryGetEditSlotColor(out uint pal, out uint col)) PaletteColor = (pal, col);
            Host?.Invoke(Host.Entity, "buildCurrentItem", data);
        }
        RefreshButtons();
        SelectedIndex = index;
    }

    /// <summary>PreviewPaletteColor (@0x1419aeea0), PreviewDetailColor (@0x1419aef10).</summary>
    public void PreviewPaletteColor(uint palette, uint color) => Customize?.ApplyPalette(palette, color);
    public void PreviewDetailColor(int palette, int color) => Customize?.SetColors(palette, color);

    // ---- bill ---------------------------------------------------------------------------------------------------------

    /// <summary>BuildBillRows (@0x1419b0b10): one row, the category "$s_beauty_change_color", the price of the basket and the item names. Null for an empty basket.</summary>
    public ColoringBill BuildBillRows()
    {
        if (Basket.Count == 0 || FeePriceText == null) return null;
        string row = Model.Tables.Common("s_bill_list_row");
        var detail = new System.Text.StringBuilder();
        foreach (var b in Basket) detail.Append(ClientFormat.Format(row, ItemName(b.ItemId)));
        uint sum = (uint)(Basket.Count * (long)GetPricePerItem());
        var fee = GetFee();
        return new ColoringBill("$s_beauty_change_color", FeePriceText(fee, sum), detail.ToString(), sum, false, "", fee);
    }

    /// <summary>OpenBill (@0x1419b0ef0): the rows, the coupon offer (the bag holds coupons and the sale price is not 0), the coupon label
    /// ("s_beauty_coupon_with_count" with the count when offered).</summary>
    public bool OpenBill()
    {
        if (OpenBillDialog == null) return false;
        var rows = BuildBillRows();
        if (rows == null) return false;
        int coupons = GetCouponCount(true);
        bool offered = coupons != 0 && GetSalePrice((uint)GetTotalPrice()) != 0;
        string name = GetCouponName();
        string label = offered ? ClientFormat.Format(Model.Tables.Common("s_beauty_coupon_with_count"), name, coupons) : name;
        OpenBillDialog(rows with { CouponOffered = offered, CouponLabel = label });
        return true;
    }

    /// <summary>The bill dialog's answer (OnMessage @0x1419aca80): m_10 is the coupon check; result 0 clears the selection and pays.</summary>
    public void OnBillAnswer(int result, bool useCoupon)
    {
        UseCoupon = useCoupon;
        if (result == 0)
        {
            ClearSelected();
            TryPay();
        }
    }

    // ---- paying ------------------------------------------------------------------------------------------------------

    /// <summary>FUN_1419af980 (@0x1419af980): an empty basket is fine; a basket that cannot be paid shows the fee's "not enough" message; with
    /// the coupon checked and fewer coupons than items the rest is asked first (common string "s_beauty_msg_ask_coupon_and_merat_confirm");
    /// else the basket is sent.</summary>
    public bool TryPay()
    {
        if (Customize == null) return false;
        if (Basket.Count == 0) return true;
        var fee = GetFee();
        if (!CanPayBasket(out int coupons)) { NotEnough?.Invoke(fee); return false; }
        FreeTries = coupons;
        int count = Basket.Count;
        if (UseCoupon && coupons < count)
        {
            uint price = (uint)((count - coupons) * (long)GetSalePrice(GetPricePerItem()));
            string text = ClientFormat.Format(Model.Tables.Common("s_beauty_msg_ask_coupon_and_merat_confirm"), count, GetCouponName(), coupons, FeePriceText?.Invoke(fee, price) ?? "");
            ShowData(MessageBoxQueue.CreateWithCallback(text, 1, (button, _) => { if (button == 0) SendBasket(); return true; }));
            return true;
        }
        SendBasket();
        return true;
    }

    /// <summary>UseFreeTry (IItemCustomizeEventListener, @0x140cd5950): -1 is unlimited (then the coupon check); else one try is used.</summary>
    int UseFreeTry()
    {
        if (FreeTries == -1) return UseCoupon ? 1 : 0;
        int old = FreeTries--;
        FreeTries = Math.Max(0, FreeTries);
        return old > 0 ? 1 : 0;
    }

    /// <summary>FUN_1419b0700 (@0x1419b0700): Beauty mode 0x16 with the basket's dyed items; the request count is pending and the movie waits.</summary>
    public void SendBasket()
    {
        var c = Customize;
        if (c == null) return;
        var items = new List<BeautyColorItem>();
        foreach (var b in Basket)
        {
            var it = c.GetModifiedAndSelect(b.Uid);
            if (it == null) continue;
            items.Add(it with { ShopFlag = ListenerShopId != 0, FreeTry = UseFreeTry() > 0 });
        }
        Send(new BeautyColoringRequest(items.ToArray()));
        AddPendingRequests(items.Count);
        SetInputEnabled?.Invoke(false);
    }

    /// <summary>AddPendingRequests (@0x1419b1f50).</summary>
    public void AddPendingRequests(int delta)
    {
        Pending += delta;
        if (Pending < 0) Pending = 0;
        Dyeing = Pending > 0;
    }

    /// <summary>OnDyeFailed (@0x1419af8d0): one request less; the edit is put back and the movie takes input again.</summary>
    public void OnDyeFailed()
    {
        AddPendingRequests(-1);
        var c = Customize;
        if (c == null) return;
        c.RestoreAll();
        SetInputEnabled?.Invoke(true);
    }

    /// <summary>OnDyeApplied (@0x1419af840): the fee is refreshed and the movie takes input again. STILL-OPEN: the hair-apply effect (FUN_1419af750).</summary>
    public bool OnDyeApplied()
    {
        Dyeing = false;
        RefreshBasketState();
        SetInputEnabled?.Invoke(true);
        return true;
    }

    /// <summary>vfunc_24 (@0x1419adf80), the item-event listener: a dyed item leaves the basket; the last one ends the dye.</summary>
    public void OnItemDyed(int itemId)
    {
        if (Host?.Entity == null || Closing) return;
        if (RemoveBasketItem(itemId) != null) AddPendingRequests(-1);
        Host.Invoke(Host.Entity, "removeBasketItem", itemId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Basket.Count == 0) OnDyeApplied();
    }

    // ---- leaving ------------------------------------------------------------------------------------------------------

    /// <summary>FUN_1419ae940 (@0x1419ae940): the edit is put back and the page is left (the field camera and the sub scene 0x620001d are the host's).</summary>
    public bool Leave()
    {
        if (Customize == null) return false;
        Customize.RestoreAll();
        Left?.Invoke();
        return true;
    }

    /// <summary>RequestClose (@0x1419ae9f0): with the save button on, ask (common string 0x271); else leave.</summary>
    public void RequestClose()
    {
        if (IsSaveEnabled())
            ShowData(MessageBoxQueue.CreateWithCallback(Model.Tables.Common(0x271), 1, (button, _) => { if (button == 0) Leave(); return true; }));
        else Leave();
    }

    /// <summary>Restart (@0x1419b1b50): while a dye is pending a notice; else the basket's and the list's items are put back and the page rebuilt.
    /// STILL-OPEN: Customize::FUN_140cd3820.</summary>
    public bool Restart()
    {
        var c = Customize;
        if (Host?.Entity == null || c == null) return true;
        if (Dyeing) { NoticeText?.Invoke(Model.Tables.Common("s_beauty_msg_wait_by_expired_codi")); return false; }
        CloseBillDialog?.Invoke();
        foreach (var b in Basket) c.RestoreUid(b.Uid);
        foreach (var uid in Listed) c.RestoreUid(uid);
        c.RestoreUid(c.EditItemUid);
        c.SelectUid(0);
        c.RestoreAll();
        ClearBasket();
        RefreshBasketState();
        BuildItems();
        return true;
    }

    // ---- movie callbacks (vfunc_28 @0x1419acfb0) -----------------------------------------------------------------------

    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onchangeitem":
                // uiitemcoloringdialog.cpp:434-440: the first string argument is skipped (FUN_1422b8d30); uid is args[1], index args[2].
                if (args.Length >= 3)
                {
                    long.TryParse(Rt.ToString(args[1]), out long uid);
                    SelectItem(uid, (int)Rt.ToNumber(args[2]));
                    return true;
                }
                break;
            case "onchangeinvaliditemindex":
                if (args.Length >= 1) SelectedIndex = -1;
                break;
            case "onchangecolor":
                if (args.Length >= 3)
                {
                    uint color = (uint)Rt.ToNumber(Host.Get(args[2], "colorSN"));
                    uint palette = (uint)Rt.ToNumber(Host.Get(args[2], "paletteID"));
                    PaletteColor = (palette, color);
                    PreviewPaletteColor(palette, color);
                    RefreshButtons();
                }
                return true;
            case "onchangedetailcolor":
                if (args.Length >= 6)
                {
                    PaletteColor = default;
                    int palette = (int)(uint)Rt.ToNumber(args[0]);
                    int type = (uint)Rt.ToNumber(args[1]) == 0 ? 0 : 2;       // FUN_140f024e0
                    PreviewDetailColor(palette, type);
                    RefreshButtons();
                }
                return true;
            case "onclickaddbasket":
                if (args.Length >= 1 && Customize != null && int.TryParse(Rt.ToString(args[0]), out int add))
                    if (AddCurrentItemToBasket(add) != null) RefreshBasketState();
                return true;
            case "onclicksubbasket":
                if (args.Length >= 1 && Customize != null && int.TryParse(Rt.ToString(args[0]), out int sub))
                    if (RemoveBasketItem(sub) != null) RefreshBasketState();
                return true;
            case "onclicksave":
                if (!AskChangeItem(0, -1, "clickSave")) OpenBill();
                return true;
            case "onclickreset":
                if (Customize is { } c)
                {
                    c.RestoreUid(c.EditItemUid);
                    var data = ResetItemData?.Invoke();
                    if (data != null) Host.Invoke(Host.Entity, "buildResetItem", data);
                }
                return true;
            case "onclickclose": RequestClose(); return true;
        }
        return false;
    }
}
