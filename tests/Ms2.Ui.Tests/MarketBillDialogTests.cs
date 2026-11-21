using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Shop;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The merat bill dialog (uimarketbilldialog, UI 0x4f17; needs MS2_DATA): the movie loads with the rows, the sums are
/// the rows' prices, onClickBuy calls the buy callback once and closes, onClickGiftOk's refusals.</summary>
public class MarketBillDialogTests
{
    static PackSet packs;
    static MovieSource src;

    static void Packs()
    {
        if (packs != null) return;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = new PackSet(d);
        src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
    }

    sealed class Rig
    {
        public MarketBillDialogController C = new();
        public MarketBillParam Param = new() { Type = 3, DialogId = 0x4e3b, Currency = 3, ShopId = 5, SourceKind = 4 };
        public int Bought;
        public List<string> Notices = new(), Gifts = new();
        public UiHost Host;
        public Player P;
        public void Settle() { for (int i = 0; i < 60; i++) P.Tick(); }

        public Rig(int rows = 2)
        {
            Packs();
            var model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            for (int i = 0; i < rows; i++)
                Param.Rows.Add(new MarketBillRow { ItemId = 11300001, Name = "row" + i, Price = 100 * (i + 1), PayType = 3, SerialId = 7000 + i, Icon = new() });
            Param.Buy = (_, _) => Bought++;
            Param.GiftBuy = (_, _, n, m) => Gifts.Add(n + "|" + m);
            C.Text = k => k;
            C.TextId = id => "{0}";
            C.Notice = Notices.Add;
            C.PlayerName = () => "Me";
            C.SetParam(Param);
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            P = p;
            Host = new UiHost(p, C, model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }
    }

    [Test]
    public void TheMovieLoadsWithTheRowsAndTheSumsAreTheirPrices()
    {
        var r = new Rig();
        Assert.That(r.C.IsClosed, Is.False);
        Assert.That(r.C.Total, Is.EqualTo(300));
        Assert.That(r.C.Prices[3], Is.EqualTo(300));
        Assert.That(r.Host.Child("m_totalFee"), Is.Not.Null);
    }

    [Test]
    public void ClickBuyCallsTheBuyCallbackOnceAndCloses()
    {
        var r = new Rig();
        r.C.OnCallback("onClickBuy", Array.Empty<object>());
        Assert.That(r.Bought, Is.EqualTo(1));
        r.Settle();
        Assert.That(r.C.IsClosed, Is.True);
    }

    [Test]
    public void ABlockedBuyShowsTheNoticeAndStaysOpen()
    {
        var r = new Rig();
        r.C.BuyBlocked = () => "blocked";
        r.C.OnCallback("onClickBuy", Array.Empty<object>());
        Assert.That((r.Bought, r.Notices.Count, r.C.IsClosed), Is.EqualTo((0, 1, false)));
    }

    [Test]
    public void GiftNeedsAnotherPlayersName()
    {
        var r = new Rig();
        r.C.OnCallback("onClickGiftOk", new object[] { "", "hi" });
        r.C.OnCallback("onClickGiftOk", new object[] { "me", "hi" });
        Assert.That((r.Gifts.Count, r.Notices.Count, r.C.IsClosed), Is.EqualTo((0, 2, false)));
        r.C.OnCallback("onClickGiftOk", new object[] { "Friend", "hi" });
        Assert.That(r.Gifts, Is.EqualTo(new[] { "Friend|hi" }));
        r.Settle();
        Assert.That(r.C.IsClosed, Is.True);
    }

    [Test]
    public void ChangingAnOptionMovesTheSums()
    {
        var r = new Rig();
        r.Param.Rows[0].PrevPrice = 100; r.Param.Rows[0].Price = 150;
        r.C.UpdateTotalPrice(0);
        Assert.That(r.C.Prices[3], Is.EqualTo(350));
    }
}
