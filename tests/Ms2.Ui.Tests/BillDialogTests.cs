using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIBillDialog (uibeautyshopdialog.cpp:2282, OnLoadComplete @0x1418cd2a0, vfunc_28 @0x1418cd7a0, FUN_1418cda70; needs
/// MS2_DATA): the load modes, the answer of the movie and the total. Texts are not compared.</summary>
public class BillDialogTests
{
    static PackSet packs;
    static MovieSource src;

    sealed class Rig
    {
        public BillDialogController C = new();
        public UiHost Host;
        public List<(int, bool)> Answers = new();
        public List<(int Kind, uint Price)> Sales = new();
        public BillData Data;

        public Rig(string checkText = "", bool sale = false, int feeType = 1, bool load = true)
        {
            if (packs == null)
            {
                var d = Environment.GetEnvironmentVariable("MS2_DATA");
                if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
                packs = new PackSet(d);
                src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            }
            var model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            Data = new BillData
            {
                Answer = (r, c) => Answers.Add((r, c)), Category = "cat", Rows = new() { ("a", "1"), ("b", "2") },
                ShopKind = 1, FeeType = feeType, Price = 1000, CheckText = checkText, Warning = "warn", Checked = true,
            };
            C.Data = Data;
            C.HasPlayer = () => true;
            C.PriceText = (t, i, a, tag) => { Texts.Add((t, i, a, tag)); return "p" + a; };
            C.ShopSale = (k, price, lv) => { Sales.Add((k, price)); return sale ? (price / 2, 3) : null; };
            C.Wallet = () => new long[] { 100, 100, 100, 100 };
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            Host = new UiHost(p, C, model);
            if (!load) return;
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            Errors = rt.Errors.ToList();
            for (int i = 0; i < 10; i++) p.Tick();
        }

        public List<(int, int, uint, string)> Texts = new();
        public List<string> Errors = new();
    }

    [Test]
    public void LoadFillsTheListInTheNormalMode()
    {
        var r = new Rig();
        Assert.That(r.C.IsClosed, Is.False);
        Assert.That(r.Errors, Is.Empty, "the movie took initData and buildList");
    }

    [Test]
    public void SubmitAnswersTheOwnerAndCloses()
    {
        var r = new Rig("coupon");
        Assert.That(r.C.OnCallback("OnSubmit", new object[] { 0.0, true }), Is.True);
        Assert.That(r.Answers, Is.EqualTo(new[] { (0, true) }));
        Assert.That(r.C.Data, Is.Null, "the bill is forgotten with the close");
        Assert.That(r.C.OnCallback("OnSubmit", new object[] { 1.0, false }), Is.False, "a second answer is not taken");
        Assert.That(r.Answers, Has.Count.EqualTo(1));
    }

    [Test]
    public void SubmitWithoutBothArgumentsAnswersNothing()
    {
        var r = new Rig();
        r.C.OnCallback("OnSubmit", new object[] { 0.0 });
        Assert.That(r.Answers, Is.Empty);
        Assert.That(r.C.Data, Is.Not.Null, "not closed");
    }

    [Test]
    public void AClosedDialogIgnoresTheCallbacks()
    {
        var r = new Rig();
        r.C.Close();
        Assert.That(r.C.OnCallback("OnSubmit", new object[] { 0.0, false }), Is.False);
        Assert.That(r.C.OnCallback("OnSetSumPrice", System.Array.Empty<object>()), Is.False);
        Assert.That(r.Answers, Is.Empty);
    }

    [Test]
    public void TheTotalAsksTheSaleOfTheShopKind()
    {
        var r = new Rig(sale: true);
        r.Sales.Clear(); r.Texts.Clear();
        r.C.OnCallback("OnSetSumPrice", System.Array.Empty<object>());
        Assert.That(r.Sales, Is.EqualTo(new[] { (1, 1000u) }));
        Assert.That(r.Texts.Single().Item3, Is.EqualTo(500u), "the price after the sale is shown");
    }

    [Test]
    public void WithoutAPriceTextNothingIsGuessed()
    {
        var r = new Rig();
        r.C.PriceText = null;
        r.Texts.Clear();
        Assert.DoesNotThrow(() => r.C.UpdatePrice(500));
        Assert.That(r.Texts, Is.Empty);
    }

    [Test]
    public void ThePoolFeeBuildsItsTextFromTheWallet()
    {
        var r = new Rig(feeType: 4);
        r.Texts.Clear();
        r.C.UpdatePrice(500);
        Assert.That(r.Texts, Is.Empty, "fee type 4 does not use the price text");
        Assert.That(r.Errors, Is.Empty);
    }
}
