using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UITokenPocketDialog (uitokenpocketdialog.cpp, UI id 0x4ece) run by its own movie (needs MS2_DATA).</summary>
public class TokenPocketTests
{
    static PackSet packs;
    static MovieSource src;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
        }
        return packs;
    }

    static (UiHost Host, TokenPocketController C, UiModel Model) Load(int[] features, bool starPoint = false)
    {
        var model = new UiModel(new GameTables(Packs(), "en")) { HasLocalPlayer = true };
        foreach (var f in features) model.Features.Add(f);
        model.Player.StarPointEvent = starPoint;
        model.Player.Tokens[6] = 1234; model.Player.Tokens[7] = 5;
        var c = new TokenPocketController();
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k;
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c, model);
    }

    static string Text(UiHost h, object slot) => Rt.ToString(h.Get(h.FindNamed("textField", (AsDisplayObject)slot), "text"));

    [Test]
    public void FeaturelessPocketHasTheLuRowOnly()
    {
        var (h, c, _) = Load(Array.Empty<int>());
        Assert.That(c.Slots.Select(s => s.Currency), Is.EqualTo(new[] { 6 }));
        Assert.That(Text(h, c.Slots[0].Slot), Is.EqualTo("1,234 / 2,000"));
    }

    [Test]
    public void FeaturesAddTheirRowsInTheClientsOrder()
    {
        var (_, c, _) = Load(new[] { Feature.PvPRankingDuelMode, Feature.ItemSocketSystem, Feature.HabiToken, Feature.ReverseRaid01,
            Feature.Mentoring, Feature.MesoMarketToken }, starPoint: true);
        Assert.That(c.Slots.Select(s => s.Currency), Is.EqualTo(new[] { 4, 5, 6, 7, 0xc, 0xd, 0xe, 0xf, 0x10 }));
    }

    [Test]
    public void RowsStackSeventeenPixelsApartFromY50AndTheBackgroundsFollow()
    {
        var (h, c, _) = Load(new[] { Feature.HabiToken });
        double Y(object o) => Rt.ToNumber(h.Get(o, "y"));
        Assert.That(c.Slots.Select(s => Y(s.Slot)), Is.EqualTo(new[] { 50.0, 73.0 }));
        Assert.That(Rt.ToNumber(h.Get(h.Child("m_bgBlack"), "height")), Is.EqualTo(50 + 2 * 0x17 - 0x2c));
        Assert.That(Rt.ToNumber(h.Get(h.Child("m_bgWhite"), "height")), Is.EqualTo(50 + 2 * 0x17 - 0x2c + 0x37));
        Assert.That(Rt.ToNumber(h.Get(c.Slots[0].Slot, "x")), Is.EqualTo(0xe));
    }

    [Test]
    public void TokenMoneyChangeRedrawsTheAmounts()
    {
        var (h, c, model) = Load(Array.Empty<int>());
        model.Player.Tokens[6] = 99;
        model.Notify(StatusEvent.TokenMoney);
        Assert.That(Text(h, c.Slots[0].Slot), Is.EqualTo("99 / 2,000"));
    }

    [Test]
    public void TheIconHtmlNamesTheCurrencyIcon()
    {
        var (h, c, _) = Load(Array.Empty<int>());
        var icon = h.FindNamed("icon", (AsDisplayObject)c.Slots[0].Slot);
        Assert.That(Rt.ToString(h.Get(icon, "htmlText")), Does.Contain("export_common_icon_lu").And.Contain("VSPACE=\"-6\""));
    }

    [Test]
    public void TheCloseButtonCloses()
    {
        var (h, c, _) = Load(Array.Empty<int>());
        var close = h.Child("m_closeButton");
        h.Invoke(close, "dispatchEvent", h.RT.Construct(h.RT.GetClass("flash.events.Event"), new object[] { "buttonClick" }));
        for (int i = 0; i < 60; i++) h.Player.Tick();
        Assert.That(c.IsClosed, Is.True);
    }

    [Test]
    public void UncappedRowHasNoCapSuffix()
    {
        var (_, _, model) = Load(new[] { Feature.MesoMarketToken });
        model.Player.Tokens[0x10] = 12345;
        Assert.That(TokenCurrency.AmountText(model, 0x10), Is.EqualTo("12,345"));
    }
}
