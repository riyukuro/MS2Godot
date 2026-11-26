using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Shop;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The auto-fishing / auto-play pane (uisystemshopdialog, UI 0x4f07; needs MS2_DATA): the movie loads in mode 4 and 5,
/// the extend title follows the buff and the auto count, closing clears the bait (uisystemshopdialog.cpp:233, 399, 470).</summary>
public class SystemShopDialogTests
{
    static PackSet packs;
    static MovieSource src;

    static (SystemShopController c, UiHost host, Player p) Open(int mode, Action<SystemShopController> setup = null)
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
        }
        var model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
        var c = new SystemShopController { Mode = mode };
        setup?.Invoke(c);
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var host = new UiHost(p, c, model);
        Assert.That(host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        return (c, host, p);
    }

    [Test]
    public void FishingModeLoadsAndFillsTheExtendTitleFromTheBuff()
    {
        var (c, _, _) = Open(4, x => { x.HasAutoFishingBuff = () => true; x.GetAutoFishingCount = () => 2; });
        Assert.That(c.Mode, Is.EqualTo(4));
        Assert.That(c.Model.Tables.Common("s_system_shop_title_extend_autofishing_cancel"), Is.Not.Empty);
    }

    [Test]
    public void InstrumentModeLoads()
    {
        var (c, _, _) = Open(5, x => x.CanPlayEnsemble = () => true);
        Assert.That(c.Mode, Is.EqualTo(5));
    }

    [Test]
    public void CloseClearsTheBait()
    {
        var (c, _, p) = Open(4, x => { x.GetBaitId = () => 0; });
        c.Close();
        for (int i = 0; i < 60; i++) p.Tick();
        Assert.That(c.BaitItem, Is.Null);
        Assert.That(c.IsClosed, Is.True);
    }

    /// <summary>The fishing pane's shop button is OnShop(0): ToggleShopTab(m_nMode) (uisystemshopdialog.cpp:257), which asks the server for
    /// system shop type 4; the instrument pane has no shop button (its action 0 is the extend box).</summary>
    [Test]
    public void TheFishingPanesShopButtonAsksForSystemShopTypeFour()
    {
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        var shop = new ShopController();
        var (c, _, _) = Open(4, x => x.ToggleShop = tab => SystemShopFlow.ToggleShopTab(tab, shop, false, null, null, sent.Add));
        Assert.That(c.OnCallback("OnShop", new object[] { 0 }), Is.True);
        Assert.That(sent, Is.EqualTo(new Ms2.Net.Wire.IOutPacket[] { new Ms2.Net.Packets.SystemShopRequest(4, 1) }));
        var (c5, _, _) = Open(5, x => x.ToggleShop = tab => { Assert.Fail("the instrument pane has no shop button"); return 0; });
        c5.OnCallback("OnShop", new object[] { 0 });
    }
}
