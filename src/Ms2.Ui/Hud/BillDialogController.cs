using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIBillDialog::TData (uibeautyshopstyledialog.h:130, 0x68 bytes): the bill a shop hands to the bill dialog. The rows are the
/// movie's list (a category and the detail / price text rows); the owner is answered through <see cref="Answer"/> (m_18's OnMessage).
/// </summary>
public sealed class BillData
{
    /// <summary>m_18: the owner's OnMessage of the answer (the result and the check box).</summary>
    public Action<int, bool> Answer;
    /// <summary>m_20: the bill rows; null leaves the movie unfilled (OnLoadComplete @0x1418cd2a0 tests it).</summary>
    public string Category;
    public List<(string Detail, string PriceText)> Rows;
    /// <summary>m_30: the shop whose sale applies to the total (0 item colouring, 1 beauty shop, 2 style shop, 3 wedding hall).</summary>
    public int ShopKind;
    /// <summary>m_34, m_38, m_40: the fee's payment type, item id and icon tag (TShopPaymentAttr).</summary>
    public int FeeType, FeeItemId;
    public string FeeIconTag = "";
    /// <summary>m_48: the price.</summary>
    public uint Price;
    /// <summary>m_50: the check box's text (the coupon line); empty = the "normal" mode without a check box.</summary>
    public string CheckText = "";
    /// <summary>m_58: the warning under the check box.</summary>
    public string Warning = "";
    /// <summary>m_60: the check box's state (enabled and selected).</summary>
    public bool Checked;

    public bool HasRows => Rows != null;
}

/// <summary>
/// UIBillDialog (uibeautyshopdialog.cpp:2282; UI 0x4e7b, movie uibilldialog, GetTypeName "BillDialog"): the bill of the beauty shops
/// the player confirms. OnLoadComplete (@0x1418cd2a0) fills the movie; the movie's "OnSubmit" answers the owner and closes it
/// (vfunc_28 @0x1418cd7a0), its "OnSetSumPrice" asks for the total (<see cref="UpdatePrice"/>, FUN_1418cda70 @0x1418cda70).
/// The dialog's vfunc_13(mode) is ASMovieClip::vfunc_13 @0x1422c2220 (asmovieclip.cpp:111), gotoAndPlay(label); the check box's
/// vfunc_24(true) is invalidate + validateNow @0x1422c80c0 (asuicomponent.h:71). The dialog's vfunc_8(1) is the modal open
/// (UIModalDialog @0x141a55380, uimodaldialog.cpp:32, SetModalDialogId): the host that shows it blocks the shop's input behind it
/// (<see cref="BeautyShopDialogController.BillIsOpen"/>) and closes it with the shop. STILL-OPEN: the wedding hall's price
/// (shop kind 3) needs the hall dialog and is the unchanged price.
/// </summary>
public sealed class BillDialogController : ModalDialogController
{
    public const int UiId = 0x4e7b;
    public override int TypeId => UiId;

    public override string Movie => "uibilldialog";
    protected override string EntityMethod => "buildList";
    public override string TypeName => "BillDialog";

    /// <summary>SetData (@0x1418cda20): m_188.</summary>
    public BillData Data;

    /// <summary>The player's level (the sale tables key on it).</summary>
    public Func<int> PlayerLevel = () => 0;
    /// <summary>The sale of a shop kind (FUN_1418cda70's lookup by m_30): the price after the sale and the sale index. Null when the
    /// shop's dialog is not open, as the client's lookup then leaves the price and a sale index of 0.</summary>
    public Func<int, uint, int, (uint Price, int Sale)?> ShopSale = (_, _, _) => null;
    /// <summary>FUN_141349f30: the price text of a payment type, item id, amount and icon tag.</summary>
    public Func<int, int, uint, string, string> PriceText;
    /// <summary>The wallet by kind for the merat pool rows (<see cref="Pc"/>'s by default).</summary>
    public Func<long[]> Wallet;
    /// <summary>The local player exists (WorldService::GetLocalPlayer).</summary>
    public Func<bool> HasPlayer;
    /// <summary>What UpdatePrice hands the movie's setSumPrice: the total text, the pool label, the price before the sale (empty
    /// without one) and whether no sale applies.</summary>
    public Action<string, string, string, bool> SumShown;

    string Str(int id) => Model.Tables.Common(id) ?? "";

    /// <summary>OnLoadComplete (@0x1418cd2a0): UIModalDialog's, then the movie is initialised and filled from the bill.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        var d = Data;
        if (d == null) return;
        Host.Invoke(Host.Entity, "initData", "");
        if (!d.HasRows) return;
        string mode = d.CheckText.Length > 0 ? "checkbox" : "normal";
        Host.Invoke(Host.Entity, "gotoAndPlay", mode);
        if (d.CheckText.Length > 0)
        {
            if (Host.Child("m_checkbox") is { } check)
            {
                Host.Set(check, "label", d.CheckText);
                Host.Set(check, "enabled", d.Checked);
                Host.Set(check, "selected", d.Checked);
                Host.Invoke(check, "invalidate");
                Host.Invoke(check, "validateNow");
            }
            if (Host.Child("m_warning") is { } warning) Host.Set(warning, "text", d.Warning);
        }
        var list = d.Rows.Select(r => (object)Host.Obj(("detail", r.Detail), ("strPrice", r.PriceText))).ToList();
        Host.Invoke(Host.Entity, "buildList", Host.Obj(("category", d.Category ?? ""), ("list", list)), d.ShopKind, mode);
    }

    /// <summary>vfunc_28 (@0x1418cd7a0): UIDialog's calls first; with the dialog closed or no bill nothing is taken. "OnSubmit" (result,
    /// check box; the client takes any argument count above 0 and an absent check box is false, SetArgsIfAny asdata.cpp:437) answers the owner and closes; "OnSetSumPrice" shows the total. Names compare without case.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (base.OnCallback(name, args)) return true;
        if (IsClosed || Data == null) return false;
        if (string.Equals(name, "OnSubmit", StringComparison.OrdinalIgnoreCase))
        {
            if (args is { Length: >= 1 })
            {
                int result = Ms2.Avm2.Rt.ToInt32(args[0]);
                bool check = args.Length >= 2 && Ms2.Avm2.Rt.ToBoolean(args[1]);
                Data.Answer?.Invoke(result, check);
                Close();
            }
            return true;
        }
        if (string.Equals(name, "OnSetSumPrice", StringComparison.OrdinalIgnoreCase))
        {
            UpdatePrice(Data.Price);
            return true;
        }
        return false;
    }

    /// <summary>vfunc_67 (@0x1418cd250): UIModalDialog's close, then the bill is forgotten.</summary>
    public override void Close()
    {
        base.Close();
        Data = null;
    }

    /// <summary>FUN_1418cda70 (@0x1418cda70): shows the total (and the price before the sale when one applies) through the movie's setSumPrice.</summary>
    public void UpdatePrice(uint price)
    {
        var d = Data;
        if (d == null || Host?.Entity == null) return;
        if (HasPlayer != null && !HasPlayer()) return;
        uint original = price;
        int sale = 0;
        if (ShopSale(d.ShopKind, price, PlayerLevel()) is { } s) { price = s.Price; sale = s.Sale; }
        string originalText = price != original ? Thousands(original) : "";
        string sum, label = "";
        if (d.FeeType == 4)
        {
            var (names, amounts) = PoolTexts(price);
            label = ClientFormat.Format(Str(0x998), names);
            string amount = ClientFormat.Format(Str(0x98f), Thousands(price));
            sum = ClientFormat.Format(Str(0x999), amounts, amount);
        }
        else
        {
            if (PriceText == null) return;   // FUN_141349f30 is the host's: no text is guessed
            sum = PriceText(d.FeeType, d.FeeItemId, price, d.FeeIconTag);
        }
        SumShown?.Invoke(sum, label, originalText, sale == 0);
        Host.Invoke(Host.Entity, "setSumPrice", sum, label, originalText, sale == 0);
    }

    /// <summary>FUN_140eabc00 (uimarketbilldialog.cpp:143) over FUN_140930590's rows of an amount: the pool names and amounts, one per line
    /// (FUN_140eabae0: the name of common string 0x98a + kind, the amount in 0x987; a row with an empty name is left out).</summary>
    (string Names, string Amounts) PoolTexts(uint amount)
    {
        var wallet = Wallet != null ? Wallet() : Pc.MeratWallet;
        var names = new System.Text.StringBuilder();
        var amounts = new System.Text.StringBuilder();
        foreach (var (kind, money) in CubeBillDialogController.PoolRows(wallet, amount))
        {
            string name = kind is >= 0 and <= 3 ? Str(0x98a + kind) : "";
            if (name.Length == 0) continue;
            if (names.Length > 0) names.Append('\n');
            names.Append(name);
            if (amounts.Length > 0) amounts.Append('\n');
            amounts.Append(ClientFormat.Format(Str(0x987), Thousands(money)));
        }
        return (names.ToString(), amounts.ToString());
    }
}
