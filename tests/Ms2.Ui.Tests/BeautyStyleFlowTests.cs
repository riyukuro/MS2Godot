using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
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
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
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
}
