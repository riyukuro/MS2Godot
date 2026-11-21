using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The Meret, MeretUse and CurrencyToken receives (CSceneStateGame::OnMeret :7025, OnMeretUse :7053, OnCurrencyToken :7094; needs MS2_DATA).</summary>
public class CurrencyFeedTests
{
    static PackSet packs;

    static UiModel NewModel()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
        }
        return new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
    }

    sealed class Seen
    {
        public readonly List<string> Sounds = new();
        public readonly List<(string Text, int Kind)> Lines = new();
        public readonly List<long> Notices = new(), RedNotices = new();
        public readonly List<StatusEvent> Events = new();
        public int TokenChanges;
        public readonly List<string> Centre = new();
    }

    static (CurrencyFeed Feed, UiModel Model, Seen Seen) Make()
    {
        var model = NewModel();
        var seen = new Seen();
        model.Changed += seen.Events.Add;
        var feed = new CurrencyFeed(model)
        {
            Sound = seen.Sounds.Add, Line = (t, k) => seen.Lines.Add((t, k)),
            MeratNotice = seen.Notices.Add, MeratRedNotice = seen.RedNotices.Add, TokenChanged = () => seen.TokenChanges++, Notice = seen.Centre.Add,
        };
        return (feed, model, seen);
    }

    [Test]
    public void MeretGainStoresWalletPlaysSoundAndShowsNotice()
    {
        var (feed, model, seen) = Make();
        feed.OnMeret(new MeretUpdate(1200, 30, 4, 1, 500));
        Assert.That(model.Player.MeratWallet, Is.EqualTo(new long[] { 1200, 30, 4, 1 }));
        Assert.That(model.Player.Merats, Is.EqualTo(1235));
        Assert.That(seen.Sounds, Is.EqualTo(new[] { "Merat_Get" }));
        Assert.That(seen.Notices, Is.EqualTo(new long[] { 500 }));
        Assert.That(seen.Lines, Has.Count.EqualTo(1));
        Assert.That(seen.Lines[0].Kind, Is.EqualTo(TalkKind.Merat));
        Assert.That(seen.Lines[0].Text, Does.Contain("500"));
        Assert.That(seen.Events, Is.EqualTo(new[] { StatusEvent.Merat }));
    }

    [Test]
    public void MeretSpendPlaysUseSoundAndOptionalMessage()
    {
        var (feed, model, seen) = Make();
        feed.OnMeret(new MeretUpdate(700, 0, 0, 0, -1500));
        Assert.That(seen.Sounds, Is.EqualTo(new[] { "Merat_Use" }));
        Assert.That(seen.Notices, Is.Empty);
        Assert.That(seen.Lines, Has.Count.EqualTo(1));
        Assert.That(seen.Lines[0].Text, Does.Contain("1,500"));

        model.Features.Add(Feature.MeratMessage);
        seen.Lines.Clear();
        feed.OnMeret(new MeretUpdate(600, 0, 0, 0, -100));
        Assert.That(seen.Lines, Has.Count.EqualTo(2));
        Assert.That(seen.Lines[1].Kind, Is.EqualTo(TalkKind.Merat));
    }

    [Test]
    public void MeretWithoutDeltaOnlyRefreshes()
    {
        var (feed, _, seen) = Make();
        feed.OnMeret(new MeretUpdate(5, 0, 0, 0, 0));
        Assert.That(seen.Sounds, Is.Empty);
        Assert.That(seen.Lines, Is.Empty);
        Assert.That(seen.Events, Is.EqualTo(new[] { StatusEvent.Merat }));
    }

    [Test]
    public void MeretUseSetsRedBalance()
    {
        var (feed, model, seen) = Make();
        feed.OnMeretUse(new MeretUseUpdate(900, 250));
        Assert.That(model.Player.RedMerat, Is.EqualTo(900));
        Assert.That(seen.Sounds, Is.EqualTo(new[] { "Merat_Get" }));
        Assert.That(seen.RedNotices, Is.EqualTo(new long[] { 250 }));
        Assert.That(seen.Notices, Is.Empty);
        Assert.That(seen.Events, Is.EqualTo(new[] { StatusEvent.RedMerat }));
        feed.OnMeretUse(new MeretUseUpdate(800, -100));
        Assert.That(seen.Sounds[1], Is.EqualTo("Merat_Use"));
    }

    [TestCase(3, 4), TestCase(4, 5), TestCase(5, 6), TestCase(6, 7), TestCase(9, 0xc), TestCase(10, 0xd), TestCase(11, 0xe), TestCase(12, 0xf), TestCase(13, 0x10)]
    public void TokenTypeStoresItsCurrency(int type, int currency)
    {
        var (feed, model, seen) = Make();
        feed.OnCurrencyToken(new CurrencyTokenUpdate((byte)type, 77, 0, 0, 0));
        Assert.That(model.Player.Tokens[currency], Is.EqualTo(77));
        Assert.That(seen.TokenChanges, Is.EqualTo(1));
        Assert.That(seen.Lines, Is.Empty);
    }

    [Test]
    public void TokenReceivePostsNotifyChangedTokenMoneyWithTypeAndDelta()
    {
        var (feed, _, _) = Make();
        var got = new System.Collections.Generic.List<(int, long)>();
        feed.TokenMoneyNotice = (type, delta) => got.Add((type, delta));
        feed.OnCurrencyToken(new CurrencyTokenUpdate(6, 1050, 1000, 0, 0));
        Assert.That(got, Is.EqualTo(new[] { (6, 1000L) }));
    }

    [Test]
    public void TokenGainAddsATakeLineOfKindSeven()
    {
        var (feed, _, seen) = Make();
        feed.OnCurrencyToken(new CurrencyTokenUpdate(6, 1050, 1000, 0, 0));
        Assert.That(seen.Lines, Has.Count.EqualTo(1));
        Assert.That(seen.Lines[0].Kind, Is.EqualTo(TalkKind.Token));
        Assert.That(seen.Lines[0].Text, Does.Contain("1000"));
        feed.OnCurrencyToken(new CurrencyTokenUpdate(6, 50, -1000, 0, 0));
        Assert.That(seen.Lines, Has.Count.EqualTo(1));
    }

    [TestCase(3, 90000006), TestCase(4, 90000017), TestCase(5, 90000013), TestCase(6, 90000014), TestCase(7, 90000018),
     TestCase(8, 90000020), TestCase(9, 90000022), TestCase(10, 90000023), TestCase(11, 90000024), TestCase(12, 90000025),
     TestCase(13, 90000027), TestCase(0, 90000001), TestCase(2, 90000004), TestCase(1, 0), TestCase(14, 0)]
    public void CurrencyItemIdFollowsGetCurrencyItemId(int type, int item) =>
        Assert.That(TokenCurrency.ItemId(type, 99), Is.EqualTo(item));

    [Test]
    public void ColourPicksTheTypeTwoItem()
    {
        Assert.That(TokenCurrency.ItemId(2, 0), Is.EqualTo(90000011));
        Assert.That(TokenCurrency.ItemId(2, 3), Is.EqualTo(90000016));
        Assert.That(TokenCurrency.ItemId(2, 2), Is.EqualTo(90000015));
    }

    [Test]
    public void OverflowShowsCentreNoticeWithTheItemName()
    {
        var (feed, model, seen) = Make();
        feed.OnCurrencyToken(new CurrencyTokenUpdate(6, 4000, 10, 0, 25));
        var name = model.Tables.ItemName(90000014);
        Assert.That(name, Is.Not.Empty);
        Assert.That(seen.Centre, Has.Count.EqualTo(1));
        Assert.That(seen.Centre[0], Does.Contain(name).And.Contain("25"));
        Assert.That(seen.Events, Does.Contain(StatusEvent.TokenMoney));
    }

    [Test]
    public void NoOverflowShowsNoCentreNotice()
    {
        var (feed, _, seen) = Make();
        feed.OnCurrencyToken(new CurrencyTokenUpdate(6, 4000, 10, 0, 0));
        Assert.That(seen.Centre, Is.Empty);
    }
}
