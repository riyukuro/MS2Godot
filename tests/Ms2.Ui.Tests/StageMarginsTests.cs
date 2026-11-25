using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The HUD's reserved bottom area (the exp bars) in the movies' Common class (CUIService's common callback,
/// uiservice.cpp:3196-3197): the dockable windows (DockingDialog) keep their drag inside the stage less
/// Common.getBottomMarginOfStage (needs MS2_DATA).
/// </summary>
public class StageMarginsTests
{
    static PackSet packs;
    static KeyTable keys;
    static MovieSource src;
    static GameTables tables;

    static UiModel Model(bool adventureLevel)
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            tables = new GameTables(packs, "en");
            keys = new KeyTable(packs, "en");
        }
        var m = new UiModel(tables) { Keys = keys };
        UiFixture.Apply(m);
        m.HasLocalPlayer = true;
        m.Player.Name = "Me";
        m.ServerTime = () => new DateTime(2026, 1, 1, 9, 5, 0);
        m.FeatureEnabledByName = name => adventureLevel || name != "AdventureLevel";
        return m;
    }

    static (UiHost h, Player p) ChatWindow(bool adventureLevel)
    {
        var m = Model(adventureLevel);
        var log = new MessengerBrowser { Model = m };
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true, log);
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        return (h, p);
    }

    /// <summary>Drags the window by its log (MOUSE_DOWN on it is DockingDialog.onStartDrag) down to the stage's bottom edge and lets go
    /// (onMoveWindow, then onStopDrag, which clamps y to stageHeight - Common.getBottomMarginOfStage() - height).</summary>
    static (double Y, double Height) DragToTheBottom(UiHost h, Player p)
    {
        var o = (AsDisplayObject)h.Child("m_textOutput");
        var b = o.GetBounds(o);
        var (gx, gy) = o.LocalToGlobal(b.X + b.W / 2, b.Y + b.H / 2);
        p.MouseMove(gx, gy);
        p.MouseButton(true);
        p.Tick();
        for (int i = 1; i <= 10; i++)
        {
            p.MouseMove(gx, gy + (p.StageH - gy) * i / 10.0);
            p.Tick();
        }
        p.MouseButton(false);
        p.Tick();
        Assert.That(h.RT.Errors, Is.Empty);
        return (Rt.ToNumber(h.Get(h.Entity, "y")), Rt.ToNumber(h.Get(h.Entity, "height")));
    }

    [TestCase(false, 18)]
    [TestCase(true, 19 + 19)]
    public void TheChatWindowStaysAboveTheExpBars(bool adventureLevel, int margin)
    {
        var (h, p) = ChatWindow(adventureLevel);
        var (y, height) = DragToTheBottom(h, p);
        Assert.That(y + height, Is.EqualTo(p.StageH - margin).Within(0.01), "the window's bottom edge sits on the reserved area");
    }

    [Test]
    public void TheMoviesAreToldTheGamePlaySceneMargin()
    {
        var (h, _) = ChatWindow(true);
        var common = h.RT.GetClass("scaleform.ms2.common.Common");
        Assert.That(Rt.ToNumber(h.RT.GetProperty(common, Name.Public("m_bottomMarginOfStage"))), Is.EqualTo(38));
        Assert.That(Rt.ToNumber(h.RT.GetProperty(common, Name.Public("m_bottomMarginOfGamePlayScene"))), Is.EqualTo(38 + 193));
    }
}
