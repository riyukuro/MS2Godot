using Ms2.Avm2;
using Ms2.Quest;

namespace Ms2.Ui.Shop;

/// <summary>One row of the bill (ASBillData / ASMeratMarketBuyBillData): what the movie lists and what the sums add.</summary>
public sealed class MarketBillRow
{
    public int ItemId;                  // vfunc_1
    public string Name = "";            // itemName
    public List<(string, object)> Icon; // iconData: the item slot object
    public long Price, PrevPrice;       // vfunc_2 / vfunc_3 (salePrice and the one before an option change)
    public int PayType = 0x11;          // vfunc_4 (0x11 = none)
    public int PaybackType = 0x11;      // vfunc_8
    public long Payback, PrevPayback;   // vfunc_6 / vfunc_7 (the star point)
    public long RandomPayback;          // vfunc_9
    public string SelectedOption = "";
    public bool EnableGift;
    public string StarPointText = "";
    public int SerialId;                // productOID
    public bool Checked = true;         // IsChecked (basket)
    public bool Hidden;                 // FUN_140eacf20: not counted in the total text
    public Ms2.Net.Packets.PremiumProduct Premium;
    public int Option;
}

/// <summary>TMarketBillDataParam / TMeratMarketBillDataParam (uimarketbilldialog.h): what the dialog shows and the callbacks
/// the buttons call.</summary>
public sealed class MarketBillParam
{
    public int Type = 3;                // m_nType: 1 buy or gift, 3 buy, 4 basket
    public int DialogId;                // m_nDialogId: the opener's (0x4ec1 the market, 0x4ebc the cash shop, 0x4e3b the shop)
    public int Currency = 1;            // m_nCurrency
    public bool Gift;                   // m_b2c: gifting allowed
    public bool Fixed;                  // m_b2d: HasFixedPrice
    public long FixedPrice;             // m_n30
    public int FixedPaybackType;        // m_n38
    public long FixedPayback;           // m_n40
    public int ShopId;                  // m_nShopId
    public int SourceKind;              // m_nSourceKind
    public string Extra = "";           // m_str70: additionalInfoDescText
    public readonly List<MarketBillRow> Rows = new();
    /// <summary>m_dgBuy(param, price map), m_dgGift(param, price map, name, message), m_dgCancel(param).</summary>
    public Action<MarketBillParam, IReadOnlyDictionary<int, long>> Buy;
    public Action<MarketBillParam, IReadOnlyDictionary<int, long>, string, string> GiftBuy;
    public Action<MarketBillParam> Cancel;
}

/// <summary>
/// UIMarketBillDialog (client/ms2/uimarketbilldialog.cpp, UI id 0x4f17, type name "MarketBillDialog", movie
/// uimarketbilldialog): the bill of a merat market or star shop purchase. <see cref="Param"/> is SetParam's; on load
/// InitData hands the rows and texts to the movie ("initData"); onClickBuy, onClickGiftOk, onUpdateTotalPrice and onChangeMode
/// are the movie's callbacks. Money is only summed here: the buy callback queues the packets.
/// <para>STILL-OPEN: the blueprint, UGC map package and locked item warning boxes of OnClickBuy (FUN_140eaad30) and the gift
/// confirm (FUN_140eab330), the buddy block test of a gift, the pool text of pay type 3 and the blueprint rows (FUN_140eabc00,
/// FUN_140eabdc0), the payback manager (FUN_140a8b490), the block notice (FindBlockedAction 0x26), the basket mode's rows,
/// tooltips (m_dgTooltip) and the map order of the sums (the client's CAtlMap order).</para>
/// </summary>
public sealed class MarketBillDialogController : UiController
{
    public override string Movie => "uimarketbilldialog";
    protected override string EntityMethod => "initData";
    public override string TypeName => "MarketBillDialog";
    public const int UiId = 0x4f17;

    /// <summary>GetCommon text by key / id (the strings of the notices and the fee lines).</summary>
    public Func<string, string> Text;
    public Func<int, string> TextId;
    /// <summary>FUN_1418fec00(payType, amount): an amount as the currency's text; GetCurrencyName(payType).</summary>
    public Func<int, long, string> PriceText = (_, a) => a.ToString();
    public Func<int, string> CurrencyName = t => t.ToString();
    /// <summary>ShowSystemNotice(text) for the gift refusals (strings 0xa6d, 0xa6e).</summary>
    public Action<string> Notice = _ => { };
    /// <summary>The local player's name (MyPC::vfunc_77), for the own-name gift refusal.</summary>
    public Func<string> PlayerName = () => "";
    /// <summary>FindBlockedAction(0x26): the block notice text, or null when the buy is not blocked.</summary>
    public Func<string> BuyBlocked = () => null;

    public MarketBillParam Param { get; private set; }
    public int Mode { get; private set; }
    public long Total { get; private set; }
    public readonly Dictionary<int, long> Prices = new(), Paybacks = new(), RandomPaybacks = new();
    public bool Dirty { get; private set; }

    string T(string key) => Text != null ? Text(key) ?? key : Model?.Tables.Common(key) ?? key;

    /// <summary>SetParam: a fixed price is the price map, the total and the payback map.</summary>
    public void SetParam(MarketBillParam p)
    {
        Param = p;
        Total = 0;
        Prices.Clear(); RandomPaybacks.Clear();
        if (p != null && p.Fixed)
        {
            Prices[p.Currency] = p.FixedPrice;
            Total = p.FixedPrice;
            Paybacks[p.FixedPaybackType] = p.FixedPayback;
        }
    }

    /// <summary>GetModeName.</summary>
    public static string ModeName(int mode) => mode switch { 1 => "buyGift", 3 => "buy", 4 => "basket", _ => "" };

    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Param == null) { Close(); return; }
        Mode = Param.Type;
        InitData();
    }

    void Add(Dictionary<int, long> map, int type, long amount)
    {
        if (Param != null && Param.Fixed) return;
        map.TryGetValue(type, out long cur);
        map[type] = Math.Max(cur + amount, 0);
    }

    /// <summary>AddPrice (also the total), AddRandomPayback, AddPayback (not the pay type 0x11).</summary>
    public void AddPrice(int type, long amount)
    {
        if (Param != null && Param.Fixed) return;
        Add(Prices, type, amount);
        Total += amount;
    }
    public void AddRandomPayback(int type, long amount) => Add(RandomPaybacks, type, amount);
    public void AddPayback(int type, long amount) { if (type != 0x11) Add(Paybacks, type, amount); }

    /// <summary>InitData: the rows to the movie, the sums, the notice keys; then the fee texts.</summary>
    void InitData()
    {
        var rows = new List<object>();
        var o = new List<(string, object)>();
        if (Mode == 4)
        {
            foreach (var r in Param.Rows) rows.Add(RowObject(r));
            o.Add(("noticeKey", "$s_marketbill_notice_meratmarket_basket"));
        }
        else
        {
            uint count = 0;
            foreach (var r in Param.Rows)
            {
                rows.Add(RowObject(r));
                AddPrice(r.PayType, r.Price);
                AddRandomPayback(r.PayType, r.RandomPayback);
                AddPayback(r.PaybackType, r.Payback);
                if (!r.Hidden) count++;
            }
            o.Add(("totalText", ClientFormat.Format(TextId?.Invoke(0xa40) ?? Model.Tables.Common(0xa40) ?? "", count)));
            string notice = "", noticeGift = "";
            if (Param.DialogId == 0x4ec1) { notice = noticeGift = Param.Gift ? "$s_marketbill_notice_meratmarket" : "$s_marketbill_notice_meratmarket_only_premium"; }
            else if (Param.DialogId == 0x4ebc) { notice = "$s_marketbill_notice_cashshop"; noticeGift = "$s_marketbill_notice_cashshop_gift"; }
            o.Add(("noticeKey", notice)); o.Add(("noticeGiftKey", noticeGift));
            o.Add(("gift", Param.Gift));
            if (Param.Extra.Length > 0) o.Add(("additionalInfoDescText", Param.Extra));
        }
        o.Add(("dp", rows));
        o.Add(("mode", ModeName(Param.Type)));
        Host.Invoke(Host.Entity, "initData", Host.Obj(o.ToArray()));
        FillTexts();
    }

    /// <summary>ASBillData::GetValue / ASMeratMarketBuyBillData::FUN_1409ac6f0: the row's members.</summary>
    object RowObject(MarketBillRow r)
    {
        var o = new List<(string, object)>
        {
            ("iconData", r.Icon != null ? Host.Obj(r.Icon.ToArray()) : null),
            ("itemName", r.Name),
            ("strSalePrice", PriceText(r.PayType, r.Price)), ("salePrice", r.Price),
            ("strRandomPaybackPrice", r.RandomPayback > 0 ? PriceText(r.PayType, r.RandomPayback) : ""),
            ("randomPaybackPrice", r.RandomPayback),
            ("selectedOption", r.SelectedOption), ("enableGift", r.EnableGift),
            ("strStarPoint", r.StarPointText), ("starPoint", r.Payback),
            ("productOID", r.SerialId),
        };
        return Host.Obj(o.ToArray());
    }

    /// <summary>FUN_140ea7900 + FillFeeTexts (@0x140ea7e70): "m_totalFee" / "m_totalFeeText" with a line per pay type, the
    /// gain lines, and "m_totalPayback" for the random payback. Not in the basket mode.</summary>
    void FillTexts()
    {
        if (Mode == 4 || Mode == 0) { Dirty = false; return; }
        var names = new List<string>(); var amounts = new List<string>(); var totals = new List<string>();
        foreach (var (type, value) in Prices)
        {
            names.Add(CurrencyName(type)); amounts.Add(PriceText(type, value)); totals.Add(PriceText(type, value));
        }
        if (totals.Count == 0)
        {
            int cur = Param?.Currency ?? 1;
            names.Add(CurrencyName(cur)); amounts.Add(PriceText(cur, 0)); totals.Add(PriceText(cur, 0));
        }
        string nameText = ClientFormat.Format(TextId?.Invoke(0x998) ?? "", string.Join("\n", names));
        string amountText = ClientFormat.Format(TextId?.Invoke(0x999) ?? "", string.Join("\n", amounts), string.Join("\n", totals));
        if (totals.Count > 1) nameText += new string('\n', totals.Count - 1);
        var giveNames = new List<string>(); var giveAmounts = new List<string>();
        foreach (var (type, value) in Paybacks)
        {
            if (value <= 0) continue;
            giveNames.Add(CurrencyName(type)); giveAmounts.Add(PriceText(type, value));
        }
        if (giveAmounts.Count > 0)
        {
            nameText += "\n" + ClientFormat.Format(T("s_bill_givename_total"), string.Join("\n", giveNames));
            amountText += "\n" + ClientFormat.Format(T("s_bill_givepoint_total"), string.Join("\n", giveAmounts));
        }
        SetText("m_totalFeeText", nameText);
        SetText("m_totalFee", amountText);
        bool any = RandomPaybacks.Values.Any(v => v != 0);
        SetShown("m_totalPayback", any); SetShown("m_totalPaybackText", any);
        if (any)
        {
            var pn = new List<string>(); var pa = new List<string>();
            foreach (var (type, value) in RandomPaybacks)
            {
                pn.Add(ClientFormat.Format(T("s_meratmarket_random_payback"), CurrencyName(type))); pa.Add(PriceText(type, value));
            }
            SetText("m_totalPaybackText", string.Join("\n", pn));
            SetText("m_totalPayback", string.Join("\n", pa));
        }
        Dirty = false;
    }

    /// <summary>FUN_1409b4620(name): the text field member of the dialog's movie; its text and visibility.</summary>
    void SetText(string name, string text) { if (Host.Child(name) is { } t) Host.Set(t, "text", text); }
    void SetShown(string name, bool shown) { if (Host.Child(name) is { } t) Host.Set(t, "visible", shown); }

    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onclickbuy":
                ClickBuy();
                return true;
            case "onclickgiftok":
                ClickGiftOk(args != null && args.Length > 0 ? Str(args[0]) : "", args != null && args.Length > 1 ? Str(args[1]) : "");
                return true;
            case "onupdatetotalprice":
                if (args != null && args.Length >= 2) UpdateTotalPrice((int)Rt.ToNumber(args[1]));
                return true;
            case "onchangemode":
                if (args != null && args.Length > 0) ChangeMode(Rt.ToBoolean(args[0]));
                return true;
        }
        return false;
    }

    static string Str(object v) => v == null || v is Undefined ? "" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "";

    /// <summary>onClickBuy: the basket mode cancels; a blocked buy shows the block notice; else m_dgBuy and the close.</summary>
    public void ClickBuy()
    {
        if (Param == null) return;
        if (Mode == 4) { Cancel(); return; }
        if (BuyBlocked() is { } blocked) { Notice(blocked); return; }
        Param.Buy?.Invoke(Param, Prices);
        Close();
    }

    /// <summary>Cancel (the basket's onClickBuy): m_dgCancel, then the close.</summary>
    public void Cancel()
    {
        if (Param == null) return;
        Param.Cancel?.Invoke(Param);
        Close();
    }

    /// <summary>onClickGiftOk: an empty name is the notice 0xa6d, the own name 0xa6e; else m_dgGift and the close.</summary>
    public void ClickGiftOk(string name, string message)
    {
        if (Param == null) return;
        if (name.Length == 0) { Notice(TextId?.Invoke(0xa6d) ?? ""); return; }
        if (string.Equals(name, PlayerName(), StringComparison.OrdinalIgnoreCase)) { Notice(TextId?.Invoke(0xa6e) ?? ""); return; }
        Param.GiftBuy?.Invoke(Param, Prices, name, message);
        Close();
    }

    /// <summary>onUpdateTotalPrice: a row's option changed; the sums move by (price - previous) and (payback - previous).</summary>
    public void UpdateTotalPrice(int index)
    {
        if (Param == null || index < 0 || index >= Param.Rows.Count) return;
        var r = Param.Rows[index];
        AddPrice(r.PayType, r.Price - r.PrevPrice);
        AddPayback(r.PaybackType, r.Payback - r.PrevPayback);
        Dirty = true;
        FillTexts();
    }

    /// <summary>onChangeMode(on): mode 2 (checked rows only) or 1; the sums rebuilt unless the price is fixed.</summary>
    public void ChangeMode(bool on)
    {
        Mode = on ? 2 : 1;
        if (Param == null || Param.FixedPrice > 0) return;
        Total = 0; Prices.Clear(); Paybacks.Clear(); RandomPaybacks.Clear();
        foreach (var r in Param.Rows)
        {
            if (on && !r.Checked) continue;
            AddPrice(r.PayType, r.Price); AddPayback(r.PaybackType, r.Payback); AddRandomPayback(r.PaybackType, r.RandomPayback);
        }
        Dirty = true;
        FillTexts();
    }
}
