using System.Globalization;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Shop;

namespace Ms2.Ui.Hud;

/// <summary>UIUGCCubeBillDialog::PendingCost (uiugccubebilldialog.h): what the server was asked to bill (kind 1 a cube, 3 a blueprint).</summary>
public class CubeBillPending
{
    public int Kind;
    public long BlueprintUid;
    public int CubeId;

    /// <summary>PendingCost::Reset (@0x140ee9890).</summary>
    public virtual void Reset() { Kind = 0; BlueprintUid = 0; CubeId = 0; }

    /// <summary>PendingCost::Assign (@0x140ee8ba0): the three fields.</summary>
    public void Assign(CubeBillPending o) { Kind = o.Kind; BlueprintUid = o.BlueprintUid; CubeId = o.CubeId; }
}

/// <summary>UIUGCCubeBillDialog::Cost (uiugccubebilldialog.h): the confirmed bill, the pending cost plus the amounts per pay type.</summary>
public sealed class CubeBillCost : CubeBillPending
{
    /// <summary>m_bPreview (+0x20): the bill is a blueprint cost preview (no buy).</summary>
    public bool Preview;
    /// <summary>m_nArg (+0x24): the argument of the description text.</summary>
    public int Arg;
    /// <summary>m_map (+0x28), an ATL CRBMap by pay type: iterated in key order.</summary>
    public readonly SortedDictionary<int, long> Amounts = new();

    /// <summary>Cost::Reset (@0x140ee9850).</summary>
    public override void Reset() { base.Reset(); Preview = false; Arg = 0; Amounts.Clear(); }
}

/// <summary>
/// The bill of a cube or blueprint purchase, the statics and free functions of uiugccubebilldialog.cpp: the ask (SendRequestCubeMode35 @0x140ee8360,
/// :264-273), the server's bills (FUN_140ee8190 @0x140ee8190 for ResponseCube 0x24, FUN_140ee83f0 @0x140ee83f0 for 0x43) and the buy
/// (UIUGCCubeBillDialog::SendRequestCube @0x140ee80a0). The dialog itself is <see cref="CubeBillDialogController"/>.
/// The blueprint ask (SendRequestCubeMode66 @0x140ee86f0, :311-320) and the blueprint open box's answer (FUN_140ee8000 @0x140ee8000 with FUN_140ee8610 @0x140ee8610,
/// :180-205) are here too.
/// STILL-OPEN: their callers. The ask is called by the meret market's UGC product open (FUN_140ec0b50 @0x140ec0b50, uimeratmarketcommon.cpp:1119-1135; needs
/// the product's FUN_140896350 check and its blueprint at +0x418) and no meret market dialog is ported yet; the box that FUN_140ee8000 answers is BlueprintImport's
/// (@0x140d1f6d0, unit_140d1ae30_itemfunctiondispatch.cpp:982-1022), ported for a blueprint with a ugc uid in
/// <see cref="InventoryController.BlueprintImport"/>; its uid 0 paths (FUN_141405440, UIItemBlueprintCreateDialog 0x4f7b) are STILL-OPEN.
/// </summary>
public sealed class CubeBill
{
    /// <summary>s_pending (DAT_143ac8f00).</summary>
    public readonly CubeBillPending Pending = new();
    /// <summary>s_cost (DAT_143ac8f20).</summary>
    public readonly CubeBillCost Cost = new();

    /// <summary>FUN_140710590: the packet goes out.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>RequestCloseDialog(0x4f29) / CloseDialog(0x4f29): the bill dialog closes (its close clears the cost). The client closes at once:
    /// UIDialog::vfunc_68 (@0x141924df0, uidialog.cpp:619-633) calls vfunc_67 itself with the tween off, and UIUGCCubeBillDialog overrides only
    /// vfunc_67 (@0x140ee7730, uiugccubebilldialog.cpp:78-80). So a close never lands after the next bill's cost is stored.</summary>
    public Action CloseBill = () => { };
    /// <summary>OpenDialog(0x4f29).</summary>
    public Action OpenBill = () => { };
    /// <summary>CloseDialog(0x4f27): the save slot dialog closes.</summary>
    public Action CloseSaveSlots = () => { };

    /// <summary>SendRequestCubeMode35 (@0x140ee8360): the bill dialog closes, the pending cost is the cube, then RequestCube 0x23.</summary>
    public void RequestCubeBill(int cubeId)
    {
        CloseBill();
        Pending.Reset();
        Pending.Kind = 1;
        Pending.CubeId = cubeId;
        Send(new HomeDesignBillRequest(cubeId));
    }

    /// <summary>SendRequestCubeMode66 (@0x140ee86f0): the bill dialog closes, the pending cost is cleared (the blueprint uid is not kept in it), then
    /// RequestCube 0x42 with the uid and true.</summary>
    public void RequestBlueprintBill(long blueprintUid)
    {
        CloseBill();
        Pending.Reset();
        Send(new BlueprintBillAskRequest(blueprintUid));
    }

    /// <summary>The bag's FindItemByUid: the item is in the inventory.</summary>
    public Func<long, bool> HasItem = _ => false;
    /// <summary>Unk_Inventory::FUN_140a63880 (@0x140a63880, inventory.cpp:1403): the item's use with the argument; 0 for an item with no function or one
    /// that is not used. A path without the client's use request ported does nothing.</summary>
    public Func<long, string, int> UseItem = (_, _) => 0;

    /// <summary>UIUGCCubeBillDialog::FUN_140ee8000 (@0x140ee8000, :195-205): the blueprint open box's answer; yes (0) looks the item up by the box's uid
    /// (FUN_141a02c20) and goes on with it. Always handles the box.</summary>
    public bool OnBlueprintOpenBox(int answer, long itemUid)
    {
        if (answer == 0 && HasItem(itemUid)) OpenBlueprint(itemUid);
        return true;
    }

    /// <summary>FUN_140ee8610 (@0x140ee8610, :180-193): the item is used with an empty argument; when that went through, the bill dialog closes and the
    /// pending cost is the blueprint (kind 3).</summary>
    public void OpenBlueprint(long itemUid)
    {
        if (UseItem(itemUid, "") == 0) return;
        CloseBill();
        Pending.Reset();
        Pending.BlueprintUid = itemUid;
        Pending.Kind = 3;
    }

    /// <summary>FUN_140ee8190 (ResponseCube 0x24) and FUN_140ee83f0 (0x43): the dialog closes, the cost is rebuilt from the pending cost and the
    /// packet's rows, the save slot dialog closes, and the bill opens with rows, or (a cube with no rows, a blueprint with none and no preview) the buy
    /// goes out at once.</summary>
    public void OnBill(ResponseCubeBill m)
    {
        CloseBill();
        Cost.Reset();
        Cost.Assign(Pending);
        bool blueprint = m.Mode == ResponseCubeBill.ModeBlueprint;
        Cost.Kind = blueprint ? 3 : 1;
        Cost.Amounts.Clear();
        Cost.Arg = m.Arg;
        foreach (var row in m.Amounts) Cost.Amounts[row.Key] = row.Value;
        if (blueprint) Cost.Preview = m.Preview;
        CloseSaveSlots();
        if (m.Count > 0 || (blueprint && m.Preview)) OpenBill();
        else Send(blueprint ? new BlueprintBuyRequest(Cost.BlueprintUid) : new CubeBuyRequest(Cost.CubeId));
    }

    /// <summary>UIUGCCubeBillDialog::SendRequestCube (@0x140ee80a0): a bill that is no preview sends its purchase; the dialog closes either way.</summary>
    public void Buy()
    {
        if (!Cost.Preview)
        {
            if (Cost.Kind == 1) Send(new CubeBuyRequest(Cost.CubeId));
            else if (Cost.Kind == 3) Send(new BlueprintBuyRequest(Cost.BlueprintUid));
        }
        CloseBill();
    }
}

/// <summary>
/// UIUGCCubeBillDialog (uiugccubebilldialog.cpp; dialog 0x4f29, GetTypeName "UGCCubeBillDialog"): the bill the player confirms before an UGC cube or a
/// blueprint is bought. OnLoadComplete (@0x140ee7760) clears the pending cost and puts the bill into the movie's setData; the movie's "OnBuy" (vfunc_28
/// @0x140ee7f40) buys; the close (vfunc_67 @0x140ee7730) clears the cost.
/// STILL-OPEN: the pool rows of pay type 3 (FUN_140930590 over the wallet at g_143ab2a78) read the wallet by kind as <see cref="PlayerState.MeratWallet"/>
/// (index 0 to 3), taking the client's cash total (FUN_140930250, kinds 0 and 1) as the sum of the first two.
/// </summary>
public sealed class CubeBillDialogController : UiController
{
    public const int UiId = 0x4f29;
    /// <summary>Feature 0x61 (FUN_14036d710), RedMeratUGCDesign.</summary>
    public const int RedMeratUGCDesign = 0x61;

    public override string Movie => "uiugccubebilldialog";
    protected override string EntityMethod => "setData";
    public override string TypeName => "UGCCubeBillDialog";

    readonly CubeBill bill;
    public CubeBillDialogController(CubeBill bill) { this.bill = bill; }

    /// <summary>Whether feature id is on (CFeatureManager::IsFeatureEnabled).</summary>
    public Func<int, bool> FeatureOn;
    /// <summary>FUN_14028dfa0 of a string key: the common string formatted with the arguments.</summary>
    public Func<string, object[], string> Format;
    /// <summary>The movie's text boxes and button by name (m_buy, m_title, m_billDescription): their text, as SetTextW / SetTextHTMLW / FUN_1422cdfb0.</summary>
    public Action<string, string> SetChildText;

    /// <summary>The own wallet by kind (MyPC's merat wallet); the model's by default.</summary>
    public Func<long[]> Wallet;
    /// <summary>A common string by key and by id; the model's tables by default.</summary>
    public Func<string, string> CommonByKey;
    public Func<int, string> CommonById;
    string Str(string key) => CommonByKey != null ? CommonByKey(key) ?? "" : Model.Tables.Common(key) ?? "";

    bool Feature(int id) => FeatureOn != null ? FeatureOn(id) : Model.IsFeatureEnabled(id);
    string Fmt(string key, params object[] args) => Format != null ? Format(key, args) : ClientFormat.Format(Str(key), args);
    void Text(string child, string text)
    {
        if (SetChildText != null) { SetChildText(child, text); return; }
        if (Host.Child(child) is not { } c) return;
        Host.Set(c, child == "m_billDescription" ? "htmlText" : child == "m_buy" ? "label" : "text", text);
    }

    /// <summary>The kind 3 pay type's rows of an amount (FUN_140930590 @0x140930590, accountsessionstate.cpp:654): the wallet's kinds 3 and 2 in that
    /// order cover the amount (kinds 0 and 1 are skipped, FUN_140930200); a remainder, or no row at all, is a row of kind 1 (FUN_1409301f0).</summary>
    public List<(int Kind, long Amount)> PoolRows(long amount) => PoolRows(Wallet != null ? Wallet() : Pc.MeratWallet, amount);

    /// <summary><see cref="PoolRows(long)"/> over a given wallet.</summary>
    public static List<(int Kind, long Amount)> PoolRows(long[] wallet, long amount)
    {
        var rows = new List<(int, long)>();
        long remain = amount;
        if (remain > 0)
        {
            for (int i = 3; i >= 0; i--)
            {
                if (i is 0 or 1) continue;
                long have = wallet != null && i < wallet.Length ? wallet[i] : 0;
                if (have >= remain) { rows.Add((i, remain)); remain = 0; break; }
                if (have > 0) { remain -= have; rows.Add((i, have)); }
            }
        }
        if (remain > 0 || rows.Count == 0) rows.Add((1, remain));
        return rows;
    }

    /// <summary>FUN_1418fe5d0 (uicommon.cpp:762): the pool kind's name (common string 0x98a + kind).</summary>
    string PoolName(int kind) => kind is >= 0 and <= 3 ? (CommonById != null ? CommonById(0x98a + kind) : Model.Tables.Common(0x98a + kind)) ?? "" : "";

    /// <summary>FUN_1418fe870 (uicommon.cpp:830) for the pay types the bill holds, then FUN_1418fec90 (:912): s_common_payment with the icon.</summary>
    string MoneyText(int payType, long amount, bool alt)
    {
        int kind = payType;
        if (payType == 3 && alt && Feature(Ms2.Ui.Feature.RedMerat) && Feature(RedMeratUGCDesign)) kind = 10;     // FUN_1418fec00 (:926): FUN_14036d710(0x61)
        string icon; int vspace = -4;
        switch (kind)
        {
            case 9: icon = Feature(Ms2.Ui.Feature.RedMerat) ? "export_common_icon_redmerat" : "export_common_icon_merat"; break;
            case 10: icon = Feature(Ms2.Ui.Feature.RedMerat) ? "export_common_icon_rbmerat" : "export_common_icon_merat"; break;
            default: (icon, vspace) = ShopController.CurrencyIcon(kind, 0); break;
        }
        return icon.Length > 0 ? Fmt("s_common_payment", amount.ToString("N0", CultureInfo.InvariantCulture), icon, vspace) : "";
    }

    /// <summary>OnLoadComplete (@0x140ee7760): UIDialog's, the pending cost cleared, the preview's button and title, the description, setData.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        bill.Pending.Reset();
        SetData();
    }

    /// <summary>The body of OnLoadComplete after UIDialog's: the texts and the bill object.</summary>
    public void SetData()
    {
        var cost = bill.Cost;
        string desc = "";
        if (cost.Preview)
        {
            Text("m_buy", Str("s_common_confirm"));
            Text("m_title", Str("s_ugc_cube_bill_title_calculate"));
            if (cost.Kind == 3) desc = Fmt("s_home_bill_description_blueprint_preview", (uint) cost.Arg);
        }
        if (desc.Length == 0) desc = Fmt("s_home_bill_description", (uint) cost.Arg);
        Text("m_billDescription", desc);
        Host.Invoke(Host.Entity, "setData", Host.Obj(Members()));
    }

    /// <summary>The members of the setData object in the client's order, one per pay type of the cost: 1 meso; 3 bluemerat, and merats (the
    /// pool's rows) when more than one row or a row of a kind other than 0 and 1; 9 redmerat.</summary>
    public (string Key, object Value)[] Members()
    {
        var members = new List<(string, object)>();
        foreach (var (payType, amount) in bill.Cost.Amounts)
        {
            string text = MoneyText(payType, amount, true);
            if (payType == 1) members.Add(("meso", text));
            else if (payType == 3)
            {
                members.Add(("bluemerat", text));
                if (Feature(Ms2.Ui.Feature.RedMerat) && Feature(RedMeratUGCDesign)) continue;     // !FUN_14036d710(0x61) || !bAlt, bAlt is true
                var rows = PoolRows(amount);
                bool list = rows.Count > 1 || (rows.Count == 1 && rows[0].Kind is not (0 or 1));
                if (!list) continue;
                var merats = new List<object>();
                foreach (var (kind, money) in rows)
                    merats.Add(new Dictionary<string, object> { ["name"] = PoolName(kind), ["money"] = MoneyText(3, money, true) });
                members.Add(("merats", merats));
            }
            else if (payType == 9) members.Add(("redmerat", text));
        }
        return members.ToArray();
    }

    /// <summary>vfunc_28 (@0x140ee7f40): UIDialog's calls first, then "OnBuy" (compared without case).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (base.OnCallback(name, args)) return true;
        if (!string.Equals(name, "OnBuy", StringComparison.OrdinalIgnoreCase)) return false;
        bill.Buy();
        return true;
    }

    /// <summary>vfunc_67 (@0x140ee7730): UIDialog's close, then the cost is forgotten.</summary>
    public override void Close()
    {
        base.Close();
        bill.Cost.Reset();
    }
}
