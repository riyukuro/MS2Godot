using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The host side of the saved-styles dialog: it stays shut without a Customize, closes with the scene (FUN_140f08f70 restore),
/// and inbound 0x31 goes to whichever beauty dialog is open (needs no data).</summary>
public class BeautyStyleFlowTests
{
    sealed class Cust : IBeautyCustomize
    {
        public int EditItemId { get; set; }
        public bool HasChanges { get; set; }
        public bool HasStyleChanges { get; set; }
        public bool IsEditItemAdded { get; set; }
        public bool IsEditItemColorChanged { get; set; }
        public bool IsDragging { get; set; }
        public List<string> Calls = new();
        public void UpdateEditItems() => Calls.Add("update");
        public void CommitSlot(int s) => Calls.Add("commit" + s);
        public void RestoreSlot(int s) => Calls.Add("restore" + s);
    }

    static PackSet packs;
    static MovieSource src;

    // a controller on its real movie, as MovieDialog attaches it
    static BeautyStyleDialogController Loaded()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
        }
        var c = new BeautyStyleDialogController();
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var host = new UiHost(p, c, new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true });
        Assert.That(host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        return c;
    }

    static byte[] Removed(long uid) { var b = new List<byte> { 0x12 }; b.AddRange(BitConverter.GetBytes(uid)); return b.ToArray(); }

    static BeautyStyleFlow Flow(Cust cu, List<BeautyStyleDialogController> shown, List<string> logs) => new()
    {
        CreateCustomize = () => cu,
        CreateController = Loaded,
        Show = shown.Add,
        Log = logs.Add,
    };

    [Test]
    public void WithoutACustomizeTheDialogDoesNotOpen()
    {
        var shown = new List<BeautyStyleDialogController>();
        var f = Flow(null, shown, new());
        Assert.That(f.Open(), Is.False);
        Assert.That(f.IsOpen, Is.False);
        Assert.That(shown, Is.Empty);
    }

    [Test]
    public void OpenShowsOnceAndTheSceneTeardownRestoresTheSlot()
    {
        var cu = new Cust();
        var shown = new List<BeautyStyleDialogController>();
        var f = Flow(cu, shown, new());
        Assert.That(f.Open(), Is.True);
        Assert.That(f.Open(), Is.False, "a second open does nothing");
        Assert.That(shown, Has.Count.EqualTo(1));
        Assert.That(shown[0].Customize, Is.SameAs(cu));
        f.Shutdown();
        Assert.That(cu.Calls, Is.Not.Empty.And.All.StartWith("restore"));
        Assert.That(f.IsOpen, Is.False);
        f.Shutdown();
    }

    [Test]
    public void TheBodyGoesToTheStyleDialogWhileItIsOpenAndToTheShopOtherwise()
    {
        var logs = new List<string>();
        var shopLogs = new List<string>();
        var style = Flow(new Cust(), new(), logs);
        var shop = new BeautyShopFlow { Log = shopLogs.Add };
        BeautyPacketRouter.Route(Removed(5), shop, style, logs.Add);
        Assert.That(logs, Has.Some.Contains("beauty shop mode 18"), "no style dialog: the shop reader");
        Assert.That(shopLogs, Is.Not.Empty, "the closed shop flow logs that it arrived closed");
        logs.Clear();
        Assert.That(style.Open(), Is.True);
        BeautyPacketRouter.Route(Removed(5), shop, style, logs.Add);
        Assert.That(logs, Has.Some.Contains("beauty style mode 18"));
    }

    /// <summary>The GMS2 empty shop (mode 0): kind, the shop data, two bytes and the u16 count, 50 bytes; the KMS reader ran past it for the sale context.</summary>
    static byte[] Gms2EmptyShop()
    {
        var b = new List<byte> { 0, 3 };
        void I(int v) => b.AddRange(BitConverter.GetBytes(v));
        void Payment(int amount) { b.Add(4); I(0); I(amount); b.AddRange(new byte[2]); }
        I(501); I(4); I(20300042); b.Add(0); I(0); I(19); b.Add(0);
        Payment(0); Payment(270);
        b.AddRange(new byte[4]);
        return b.ToArray();
    }

    [Test]
    public void TheGms2ShopBodyHasNoSaleContextAndRoutesToTheShop()
    {
        var body = Gms2EmptyShop();
        Assert.That(body, Has.Length.EqualTo(50));
        var logs = new List<string>();
        var shop = new BeautyShopFlow { Log = logs.Add };
        BeautyPacketRouter.Route(body, shop, Flow(new Cust(), new(), logs), logs.Add, null, ProtocolProfile.Gms2);
        Assert.That(logs, Has.Some.Contains("beauty shop mode 0"));
        Assert.Catch(() => BeautyPacketRouter.Route(body, shop, Flow(new Cust(), new(), logs), logs.Add),
            "the KMS reader asks for the sale context the GMS2 body does not have");
    }
}
