using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.World;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The shadow expedition boss bar (UIShadowExpeditionBossBar, uishadowexpeditionbossbar.cpp) and its listener FieldGameScoreBoardCli.
/// The listener tests need no data; the movie tests need MS2_DATA. No test asserts a language-specific string.
/// </summary>
public class ShadowExpeditionBossBarTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static GameTables Tables()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs ??= new PackSet(d);
        src ??= new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        return tables ??= new GameTables(packs, "en");
    }

    static ShadowExpeditionBossBarController Open(int cur, int max, string title, out UiHost host)
    {
        var t = Tables();
        var c = new ShadowExpeditionBossBarController();
        c.SetCurMax(cur, max);
        c.SetTitle(title);
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        host = new UiHost(p, c, new UiModel(t));
        Assert.That(host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors) + " missing: " + string.Join("; ", rt.Missing.Keys));
        for (int i = 0; i < 5; i++) p.Tick();
        return c;
    }

    static string Text(UiHost h, object gauge, string name)
    {
        var n = h.Child(name, gauge);
        Assert.That(n, Is.Not.Null, name);
        var f = h.Has(n, "htmlText") ? n : h.Child("textField", n);
        return Rt.ToString(h.Get(f, "text"));
    }

    // ---- the listener (no data) ----

    static (FieldGameScoreBoard board, List<ShadowExpeditionBossBarController> opened, Func<ShadowExpeditionBossBarController> bar) Board(bool exists = true)
    {
        var board = new FieldGameScoreBoard();
        var opened = new List<ShadowExpeditionBossBarController>();
        var bar = new ShadowExpeditionBossBarController();
        board.FindBossBar = () => exists ? bar : null;
        board.Open = c => opened.Add(c);
        return (board, opened, () => bar);
    }

    [Test]
    public void TheClientsTypeNameUiIdAndOffsetAreTheDialogs()
    {
        Assert.That(new ShadowExpeditionBossBarController().TypeName, Is.EqualTo("ShadowExpeditionBossBar"));
        Assert.That(ShadowExpeditionBossBarController.UiId, Is.EqualTo(0x4f0e));
        Assert.That(ShadowExpeditionBossBarController.TopOffset, Is.EqualTo(0x8c));
    }

    [Test]
    public void OpenModeOneGivesTheBarThePointsAndTheTranslatedTitleAndOpensIt()
    {
        var (board, opened, bar) = Board();
        board.ServerString = s => "<" + s + ">";
        board.OnPacket(new FieldGameScoreBoardPacket(FieldGameScoreBoardPacket.Open, 1, "t", 30, 200));
        Assert.That((bar().Cur, bar().Max, bar().Title), Is.EqualTo((30, 200, "<t>")));
        Assert.That(board.Mode, Is.EqualTo(1));
        Assert.That(opened, Is.EqualTo(new[] { bar() }));
    }

    [Test]
    public void OpenWithoutAFoundDialogDoesNothing()
    {
        var (board, opened, _) = Board(exists: false);
        board.OnPacket(new FieldGameScoreBoardPacket(FieldGameScoreBoardPacket.Open, 1, "t", 30, 200));
        Assert.That(opened, Is.Empty);
        Assert.That(board.Dialog, Is.Null);
    }

    [Test]
    public void TheDarkStreamModesDriveNoBossBar()
    {
        foreach (byte mode in new byte[] { 2, 3 })
        {
            var (board, opened, bar) = Board();
            board.OnPacket(new FieldGameScoreBoardPacket(FieldGameScoreBoardPacket.Open, mode, "t", 30, 200));
            Assert.That(opened, Is.Empty, "mode " + mode);
            Assert.That(bar().Max, Is.EqualTo(0));
            Assert.That(board.Mode, Is.EqualTo(mode));
        }
    }

    [Test]
    public void TheUpdateSetsOnlyTheCurrentPoints()
    {
        var (board, _, bar) = Board();
        board.OnPacket(new FieldGameScoreBoardPacket(FieldGameScoreBoardPacket.Open, 1, "t", 30, 200));
        board.OnPacket(new FieldGameScoreBoardPacket(FieldGameScoreBoardPacket.SetCurrent, 0, "", 90, 0));
        Assert.That((bar().Cur, bar().Max), Is.EqualTo((90, 200)));
    }

    [Test]
    public void TheUpdateBeforeAnyOpenChangesNothing()
    {
        var (board, opened, bar) = Board();
        board.OnPacket(new FieldGameScoreBoardPacket(FieldGameScoreBoardPacket.SetCurrent, 0, "", 90, 0));
        Assert.That(bar().Cur, Is.EqualTo(0));
        Assert.That(opened, Is.Empty);
    }

    [Test]
    public void ACloseBeforeAnyOpenChangesNothing()
    {
        var (board, _, _) = Board();
        Assert.DoesNotThrow(() => board.OnPacket(new FieldGameScoreBoardPacket(FieldGameScoreBoardPacket.Close, 0, "", 0, 0)));
    }

    [Test]
    public void AnotherTypeDoesNothing()
    {
        var (board, opened, _) = Board();
        board.OnPacket(new FieldGameScoreBoardPacket(9, 1, "t", 30, 200));
        Assert.That(opened, Is.Empty);
    }

    // ---- the movie ----

    [Test]
    public void ATitleSelectsTheGaugeWithATitleAndTheGaugeIsOnTheRoot()
    {
        var c = Open(30, 200, "x", out var h);
        Assert.That(c.Gauge, Is.Not.Null);
        Assert.That(h.Child("m_gauge", c.Gauge), Is.Not.Null);
        Assert.That(Text(h, c.Gauge, "m_title"), Is.EqualTo("x"));
    }

    [Test]
    public void NoTitleSelectsTheGaugeWithoutATitle()
    {
        var withTitle = Open(30, 200, "x", out _);
        var without = Open(30, 200, "", out var h);
        Assert.That(without.Gauge, Is.Not.Null);
        Assert.That(h.Child("m_gauge", without.Gauge), Is.Not.Null);
        Assert.That(without.Gauge, Is.Not.SameAs(withTitle.Gauge));
    }

    [Test]
    public void TheProgressTextCarriesThePercentOfTheClientsIntegerDivision()
    {
        var c = Open(30, 200, "x", out var h);
        Assert.That(Text(h, c.Gauge, "m_progressText"), Does.Contain("15"));
        c.SetCur(100); c.Refresh();
        Assert.That(Text(h, c.Gauge, "m_progressText"), Does.Contain("50"));
        c.SetCur(500); c.Refresh();
        Assert.That(Text(h, c.Gauge, "m_progressText"), Does.Contain("100"));
    }

    [Test]
    public void AMostOfZeroLeavesTheGauge()
    {
        var c = Open(0, 0, "x", out var h);
        Assert.That(Text(h, c.Gauge, "m_progressText"), Does.Not.Contain("NaN"));
        c.SetCur(5); Assert.DoesNotThrow(c.Refresh);
    }

    [Test]
    public void UnloadClearsTheGaugeAndTheTitleButNotThePoints()
    {
        var c = Open(30, 200, "x", out var h);
        c.Close();                                  // the movie's OnEndClose raises Unloaded (vfunc_26 @0x140f9d0f0)
        for (int i = 0; i < 120 && c.Gauge != null; i++) h.Player.Tick();
        Assert.That(c.Gauge, Is.Null);
        Assert.That(c.Title, Is.Empty);
        Assert.That((c.Cur, c.Max), Is.EqualTo((30, 200)));
    }
}
