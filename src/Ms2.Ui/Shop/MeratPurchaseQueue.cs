using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Shop;

/// <summary>
/// The merat market's pending purchases: CMeratMarketMan::QueuePurchase (@0x140d59810, meratmarketman.cpp:1482),
/// StartPendingPurchases (@0x140d58650), OnPendingPurchaseResult (@0x140d58810) and FinishPendingPurchases (@0x140d589f0).
/// The bill dialog queues one <see cref="MeretMarketBuy"/> per row and starts them; each is sent after the answer of the one
/// before (the answer's echoed index is the queue index); after the last one the bill dialog closes and one message box lists
/// the bought and the failed products. STILL-OPEN: the basket lines (MarkBasketLineBought / ClearPendingPurchases' basket
/// pass), the shop refresh after a shop buy (GetShopDialogIfShopBuy), the level-limit text of error 0xb4, the errors 0xb6 and
/// 0xbd, and the scene test of the box (the field scene, 0x620000c).
/// </summary>
public sealed class MeratPurchaseQueue
{
    /// <summary>One queued purchase (CPendingBuyBase: m_nState is the answer's error, 0 = bought).</summary>
    public sealed class Pending
    {
        public MeretMarketBuy Buy;
        public string Name = "";
        public int Premium;         // vfunc_5: a premium (shop) product
        public int PayType;         // vfunc_3
        public int State;           // m_nState
    }

    /// <summary>The packet is sent (CMeratMarketBuy::vfunc_4).</summary>
    public Action<MeretMarketBuy> Send = _ => { };
    /// <summary>ShowSystemNotice(text, 1, 1).</summary>
    public Action<string> Notice = _ => { };
    /// <summary>GetStringCommon(id) and GetStringCommonByKey(key).</summary>
    public Func<int, string> Text = _ => "";
    public Func<string, string> TextKey = _ => "";
    /// <summary>FUN_1418fec00(payType, amount, false): the amount as text.</summary>
    public Func<int, long, string> PriceText = (_, _) => "";
    /// <summary>The message box (caption, message, the buy sound) of the finish.</summary>
    public Action<string, string, bool> ShowResult = (_, _, _) => { };
    /// <summary>CloseDialog(0x4f17).</summary>
    public Action CloseBill = () => { };
    /// <summary>A purchase was answered (success, the answer): the caller updates the product's counts.</summary>
    public event Action<MeretMarketPacket> Answered;

    readonly List<Pending> pending = new();   // m_pending
    bool running;                             // m_n2a0
    int buyType;                              // m_n2a8

    public bool Running => running;
    public int Count => pending.Count;
    public IReadOnlyList<Pending> Items => pending;
    /// <summary>m_b2d8: an answer was error 0x88.</summary>
    public bool SawError88 { get; private set; }

    /// <summary>QueuePurchase: while running nothing is added; the first fixes the buy type, a different one is a notice.</summary>
    public void Queue(Pending buy, int kind)
    {
        if (running) return;
        if (buyType == 0 && pending.Count == 0) buyType = kind;
        if (kind != buyType) { Notice(TextKey("s_meratmarket_error_buying_diff_buytype")); return; }
        buy.Buy.Index = pending.Count;
        pending.Add(buy);
    }

    /// <summary>StartPendingPurchases: running is a notice; nothing queued finishes; another buy type is a notice; else the
    /// first is sent.</summary>
    public void Start(int kind)
    {
        if (running) Notice(TextKey("s_meratmarket_error_buying"));
        else if (pending.Count == 0) Finish(false);
        else if (kind != buyType) Notice(TextKey("s_meratmarket_error_buying_diff_buytype"));
        else
        {
            running = true;
            int total = pending.Count;
            for (int i = 0; i < total; i++) pending[i].Buy.Total = total;
            Send(pending[0].Buy);
        }
    }

    /// <summary>The 0x6C answer of a buy (modes 0x1e and 0x1f): OnPendingPurchaseResult(success, error, index).</summary>
    public void OnAnswer(MeretMarketPacket p)
    {
        if (p?.Buy == null) return;
        Answered?.Invoke(p);
        if (!running) return;
        int index = p.Buy.Index;
        if (index < 0 || index >= pending.Count) return;
        pending[index].State = p.Succeeded ? 0 : p.Error;
        if (index + 1 < pending.Count) Send(pending[index + 1].Buy);
        else Finish(false);
    }

    /// <summary>OnDialogClosed (0x4ec1, or the shop 0x4e3b while a shop buy runs): FinishPendingPurchases(1).</summary>
    public void Cancel() => Finish(true);

    /// <summary>FinishPendingPurchases: the running flag off, the bill dialog closed; a cancel forgets the queue, else the box.</summary>
    void Finish(bool cancel)
    {
        running = false;
        CloseBill();
        if (cancel) { Clear(); return; }
        if (pending.Count == 0) return;
        bool premium = pending[0].Premium != 0;
        string okFormat = Text(premium ? 0x9fc : 0x9f6), failFormat = Text(premium ? 0x9fd : 0x9f7);
        var ok = new System.Text.StringBuilder();
        var fail = new System.Text.StringBuilder();
        int failed = 0;
        SawError88 = false;
        foreach (var b in pending)
        {
            string price = PriceText(b.PayType, b.Buy.Price);
            if (b.State != 0)
            {
                if (b.State == 0x88) SawError88 = true;
                if (fail.Length > 0) fail.Append('\n');
                fail.Append(ClientFormat.Format(failFormat, b.Name, price, ErrorText(b.State)));
                failed++;
            }
            else
            {
                if (ok.Length > 0) ok.Append('\n');
                ok.Append(ClientFormat.Format(okFormat, b.Name, price));
            }
        }
        string title = Text(0x9f4), caption = Text(premium ? 0x9fb : 0x9f5);
        if (failed == 0) ShowResult(caption, ClientFormat.Format(title, Text(premium ? 0x9fe : 0x9f8), ok.ToString()), true);
        else if (failed == pending.Count) ShowResult(caption, ClientFormat.Format(title, Text(premium ? 0xa00 : 0x9fa), fail.ToString()), false);
        else ShowResult(caption, ClientFormat.Format(title, Text(premium ? 0x9ff : 0x9f9), ok + "\n" + fail), true);
        Clear();
    }

    /// <summary>ClearPendingPurchases (without the basket pass).</summary>
    void Clear()
    {
        buyType = 0;
        pending.Clear();
    }

    /// <summary>GetPurchaseErrorText (@0x140d5a330): the text of an error; 0 for the ones not ported (empty).</summary>
    public string ErrorText(int error) => error switch
    {
        0x88 => Text(0x9e8), 0x89 => Text(0x9e9), 0x90 => Text(0x9ea), 0x92 => Text(0x9eb), 0x94 => Text(0x9ec),
        0x95 => Text(0x9ee), 0x97 => TextKey("s_meratmarket_error_result_buy_change_star_point"), 0x80 => Text(0x9ef),
        0x6a => Text(0x9f0), 0x9d => Text(0x9ed), 0xac => Text(0xa0f),
        0x91 => TextKey("s_meratmarket_error_result_gift_own_product"),
        0x9f => TextKey("s_meratmarket_error_result_buy_sold_out_limitedbundle"),
        0xa1 => TextKey("s_meratmarket_error_result_buy_sold_out_changeable"),
        0xab => TextKey("s_meratmarket_error_result_buy_ing"),
        0xb1 => TextKey("s_meratmarket_error_result_buy_achieve_complete"),
        0xb2 => Text(0x9f0), 0xb3 => Text(0xa6e), 0xa6 => Text(0xa47), 0xa8 or 0xa9 => Text(0xa48),
        0xa4 or 0xa5 => TextKey("s_meratmarket_error_result_gift_cannot"),
        0xb4 => TextKey("s_meratmarket_error_buy_limit_level"),
        0xb5 => TextKey("s_meratmarket_error_buy_limit_job"),
        0xb7 => TextKey("s_meratmarket_error_buy_not_pcbang"),
        0xb8 => TextKey("s_meratmarket_error_buy_low_pcbang_level"),
        0xbc => TextKey("s_meratmarket_error_buy_shutdown"),
        0x87 => TextKey("s_meratmarket_error_buy_invalid_price_type"),
        _ => "",
    };
}
