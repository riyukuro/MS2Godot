using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The bill of a cube or blueprint (uiugccubebilldialog.cpp): the ask, the server's bill and the buy.</summary>
[TestFixture]
public class CubeBillTests
{
    static (CubeBill b, List<IOutPacket> sent, List<string> log) Make()
    {
        var sent = new List<IOutPacket>(); var log = new List<string>();
        var b = new CubeBill
        {
            Send = sent.Add, CloseBill = () => log.Add("closeBill"), OpenBill = () => log.Add("openBill"), CloseSaveSlots = () => log.Add("closeSlots"),
        };
        return (b, sent, log);
    }

    static ResponseCubeBill Bill(byte mode, bool preview, params (int, long)[] rows)
        => new(mode, 7, rows.Select(r => new KeyValuePair<int, long>(r.Item1, r.Item2)).ToArray(), preview) { Count = rows.Length };

    [Test] public void TheCloseClearsTheCostAtOnceSoTheNextBillKeepsItsCost()
    {
        var (b, _, _) = Make();
        b.CloseBill = () => b.Cost.Reset();   // the dialog's close: UIDialog's, then s_cost.Reset
        b.OnBill(Bill(ResponseCubeBill.ModeCube, false, (3, 40)));
        b.OnBill(Bill(ResponseCubeBill.ModeCube, false, (1, 5)));
        Assert.That(b.Cost.Amounts.Select(x => (x.Key, x.Value)), Is.EqualTo(new[] { (1, 5L) }));
    }

    [Test] public void BlueprintAskClosesTheBillClearsThePendingAndSends0x42()
    {
        var (b, sent, log) = Make();
        b.Pending.Kind = 3; b.Pending.BlueprintUid = 9; b.Pending.CubeId = 4;
        b.RequestBlueprintBill(77);
        Assert.That(log, Is.EqualTo(new[] { "closeBill" }));
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new BlueprintBillAskRequest(77) }));
        Assert.That((b.Pending.Kind, b.Pending.BlueprintUid, b.Pending.CubeId), Is.EqualTo((0, 0L, 0)));
    }

    [Test] public void BlueprintOpenBoxAnswersOnlyYesForAnItemInTheBag()
    {
        var (b, sent, log) = Make();
        var used = new List<(long, string)>();
        b.HasItem = uid => uid == 5;
        b.UseItem = (uid, arg) => { used.Add((uid, arg)); return 1; };
        Assert.That(b.OnBlueprintOpenBox(1, 5), Is.True);
        Assert.That(b.OnBlueprintOpenBox(0, 6), Is.True);
        Assert.That(used, Is.Empty);
        Assert.That(b.OnBlueprintOpenBox(0, 5), Is.True);
        Assert.That(used, Is.EqualTo(new[] { (5L, "") }));
        Assert.That(log, Is.EqualTo(new[] { "closeBill" }));
        Assert.That((b.Pending.Kind, b.Pending.BlueprintUid), Is.EqualTo((3, 5L)));
        Assert.That(sent, Is.Empty);
    }

    [Test] public void BlueprintOpenBoxKeepsThePendingWhenTheUseDoesNotGoThrough()
    {
        var (b, _, log) = Make();
        b.Pending.Kind = 1; b.Pending.CubeId = 4;
        b.HasItem = _ => true;   // UseItem stays the default: 0
        b.OnBlueprintOpenBox(0, 5);
        Assert.That(log, Is.Empty);
        Assert.That((b.Pending.Kind, b.Pending.CubeId), Is.EqualTo((1, 4)));
    }

    [Test] public void BlueprintPreviewBillOpensWithNoBuy()
    {
        var (b, sent, log) = Make();
        b.RequestBlueprintBill(77);
        b.OnBill(Bill(ResponseCubeBill.ModeBlueprint, true));
        b.Buy();
        Assert.That(log, Does.Contain("openBill"));
        Assert.That(sent.OfType<BlueprintBuyRequest>(), Is.Empty);
    }

    [Test] public void AskClosesTheBillSetsThePendingCubeAndSends0x23()
    {
        var (b, sent, log) = Make();
        b.Pending.Kind = 3; b.Pending.BlueprintUid = 9;
        b.RequestCubeBill(5);
        Assert.That(log, Is.EqualTo(new[] { "closeBill" }));
        Assert.That((b.Pending.Kind, b.Pending.CubeId, b.Pending.BlueprintUid), Is.EqualTo((1, 5, 0L)));
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new HomeDesignBillRequest(5) }));
    }

    [Test] public void CubeBillWithRowsOpensTheDialogAndClosesTheSlots()
    {
        var (b, sent, log) = Make();
        b.RequestCubeBill(5); sent.Clear(); log.Clear();
        b.OnBill(Bill(ResponseCubeBill.ModeCube, false, (3, 40), (1, 100)));
        Assert.That(log, Is.EqualTo(new[] { "closeBill", "closeSlots", "openBill" }));
        Assert.That(sent, Is.Empty);
        Assert.That((b.Cost.Kind, b.Cost.CubeId, b.Cost.Arg), Is.EqualTo((1, 5, 7)));
        Assert.That(b.Cost.Amounts.Keys, Is.EqualTo(new[] { 1, 3 }), "ascending pay type");
    }

    [Test] public void CubeBillWithoutRowsBuysAtOnce()
    {
        var (b, sent, log) = Make();
        b.RequestCubeBill(5); sent.Clear(); log.Clear();
        b.OnBill(Bill(ResponseCubeBill.ModeCube, false));
        Assert.That(log, Is.EqualTo(new[] { "closeBill", "closeSlots" }));
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new CubeBuyRequest(5) }));
    }

    [Test] public void BlueprintBillWithoutRowsBuysUnlessItIsAPreview()
    {
        var (b, sent, log) = Make();
        b.Pending.Kind = 3; b.Pending.BlueprintUid = 77;
        b.OnBill(Bill(ResponseCubeBill.ModeBlueprint, false));
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new BlueprintBuyRequest(77) }));
        sent.Clear(); log.Clear();
        b.Pending.Kind = 3; b.Pending.BlueprintUid = 77;
        b.OnBill(Bill(ResponseCubeBill.ModeBlueprint, true));
        Assert.That(sent, Is.Empty);
        Assert.That(log, Does.Contain("openBill"));
        Assert.That((b.Cost.Kind, b.Cost.Preview), Is.EqualTo((3, true)));
    }

    [Test] public void BuyOfAPreviewSendsNothingAndCloses()
    {
        var (b, sent, log) = Make();
        b.Cost.Kind = 3; b.Cost.BlueprintUid = 1; b.Cost.Preview = true;
        b.Buy();
        Assert.That(sent, Is.Empty);
        Assert.That(log, Is.EqualTo(new[] { "closeBill" }));
    }

    [Test] public void BuySendsByKind()
    {
        var (b, sent, _) = Make();
        b.Cost.Kind = 1; b.Cost.CubeId = 4; b.Buy();
        b.Cost.Kind = 3; b.Cost.BlueprintUid = 8; b.Buy();
        b.Cost.Kind = 0; b.Buy();
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new CubeBuyRequest(4), new BlueprintBuyRequest(8) }));
    }

    [Test] public void CostResetClearsEverything()
    {
        var (b, _, _) = Make();
        b.OnBill(Bill(ResponseCubeBill.ModeBlueprint, true, (1, 5)));
        b.Cost.Reset();
        Assert.That((b.Cost.Kind, b.Cost.Preview, b.Cost.Arg, b.Cost.Amounts.Count), Is.EqualTo((0, false, 0, 0)));
    }

    static CubeBillDialogController Dialog(CubeBill b, long[] wallet, bool red = false)
        => new(b)
        {
            FeatureOn = f => red, Wallet = () => wallet, CommonByKey = k => k, CommonById = id => "n" + id,
            Format = (k, a) => k == "s_common_payment" ? $"{a[0]}|{a[1]}|{a[2]}" : k,
        };

    [Test] public void SetDataMembersFollowThePayTypes()
    {
        var (b, _, _) = Make();
        b.Cost.Amounts[1] = 1000; b.Cost.Amounts[3] = 30; b.Cost.Amounts[9] = 2;
        var m = Dialog(b, new long[4]).Members();
        Assert.That(m.Select(x => x.Key), Is.EqualTo(new[] { "meso", "bluemerat", "redmerat" }), "one row of kind 1: no list");
        Assert.That(m[0].Value, Is.EqualTo("1,000|export_common_icon_meso_h21|-6"));
    }

    [Test] public void MeratListWhenTheWalletNeedsSeveralKindsOrKindOther()
    {
        var (b, _, _) = Make();
        b.Cost.Amounts[3] = 30;
        var m = Dialog(b, new long[] { 0, 0, 10, 15 }).Members();
        Assert.That(m.Select(x => x.Key), Is.EqualTo(new[] { "bluemerat", "merats" }));
        var rows = (List<object>) m[1].Value;
        Assert.That(rows.Count, Is.EqualTo(3), "kind 3 (15), kind 2 (10), the rest of 5 as kind 1");
    }

    [Test] public void PoolRowsCoverTheAmountThenTheRemainderIsKind1()
    {
        var (b, _, _) = Make();
        var d = Dialog(b, new long[] { 99, 99, 10, 15 });
        Assert.That(d.PoolRows(12), Is.EqualTo(new[] { (3, 12L) }));
        Assert.That(d.PoolRows(30), Is.EqualTo(new[] { (3, 15L), (2, 10L), (1, 5L) }));
        Assert.That(Dialog(b, new long[4]).PoolRows(7), Is.EqualTo(new[] { (1, 7L) }));
    }

    [Test] public void OnBuyBuysAndAnyOtherCallIsLeftAlone()
    {
        var (b, sent, log) = Make();
        b.Cost.Kind = 1; b.Cost.CubeId = 3;
        var d = Dialog(b, new long[4]);
        Assert.That(d.OnCallback("onbuy", Array.Empty<object>()), Is.True);
        Assert.That(sent.Count, Is.EqualTo(1));
        Assert.That(d.OnCallback("OnOther", Array.Empty<object>()), Is.False);
    }

    /// <summary>A preview's buy button and title are the "$" keys set as they are (uiugccubebilldialog.cpp:94, :97): the text field's
    /// translator shows the Flash table's text, not the common table's.</summary>
    [Test] public void PreviewButtonAndTitleAreTheFlashTablesTexts()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new Ms2.Formats.Pack.PackSet(d);
        var src = new Ms2.Avm2.Flash.MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        var flash = Ms2.Formats.World.GameStrings.FlashTable(packs, "en", LobbyFeatures.LocaleOf(packs));
        Assert.That(flash.TryGetValue("$s_common_confirm", out var confirm), Is.True, "the data's Flash row");
        Assert.That(flash.TryGetValue("$s_ugc_cube_bill_title_calculate", out var title), Is.True, "the data's Flash row");
        var (b, _, _) = Make();
        b.OnBill(Bill(ResponseCubeBill.ModeBlueprint, true, (1, 5)));
        var model = new UiModel(new GameTables(packs, "en"));
        var c = new CubeBillDialogController(b) { FeatureOn = _ => false, Wallet = () => new long[4] };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => flash.TryGetValue(k, out var t) ? t : k;
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();      // the button's label goes to its text field on the next draw
        Assert.That(Ms2.Avm2.Rt.ToString(h.Get(h.Get(h.Child("m_buy"), "textField"), "text")), Is.EqualTo(confirm));
        Assert.That(Ms2.Avm2.Rt.ToString(h.Get(h.Child("m_title"), "text")), Is.EqualTo(title));
    }
}
