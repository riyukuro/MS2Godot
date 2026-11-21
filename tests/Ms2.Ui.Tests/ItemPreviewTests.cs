using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The item preview window (uiitempreviewdialog, UI 0x4ec2; needs MS2_DATA): OpenPreview's refusals and notices, the
/// shown item, the movie's message and initData, the close.</summary>
public class ItemPreviewTests
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
        public ItemPreviewController C = new();
        public List<string> Notices = new();
        public UiModel Model;
        public Item Item;
        public long Now = 1000;

        public Rig(bool player = true)
        {
            Packs();
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = player };
            foreach (int id in new[] { 11300001, 12000001, 20000001, 20000002, 30000001 })
                if ((Item = Item.Create(Model.Tables.Items, id, 1)) != null && !Item.Data.IsGemItem && Item.Data.FunctionName != "ItemChangeBeauty") break;
            if (Item == null) Assert.Ignore("no known item");
            C.Notice = Notices.Add;
            C.Now = () => Now;
        }
    }

    [Test]
    public void ARefusedItemShowsItsNoticeAndOpensNothing()
    {
        var r = new Rig();
        Assert.That(r.C.Open(r.Model, null, 1), Is.False);
        Assert.That(r.C.Open(new Rig(player: false).Model, r.Item, 1), Is.False, "no local player");

        int old = r.Item.Data.DoNotPreview;
        r.Item.Data.DoNotPreview = 1;
        try
        {
            Assert.That(r.C.Open(r.Model, r.Item, 1), Is.False);
            Assert.That(r.Notices, Is.EqualTo(new[] { r.Model.Tables.Common(ItemPreviewController.NotPreviewableText) }));
            Assert.That(r.C.Shown, Is.Null);
        }
        finally { r.Item.Data.DoNotPreview = old; }

        r.Notices.Clear();
        r.Item.ExpiryTime = 500;
        Assert.That(r.C.Open(r.Model, r.Item, 1), Is.False);
        Assert.That(r.Notices, Is.EqualTo(new[] { r.Model.Tables.Common(ItemPreviewController.ExpiredText) }));
        Assert.That(r.C.Shown, Is.Null);
    }

    [Test]
    public void AnOpenedItemIsShownWithItsModeAndNoNotice()
    {
        var r = new Rig();
        r.Item.ExpiryTime = 5000;                       // not expired yet
        Assert.That(r.C.Open(r.Model, r.Item, 1), Is.True);
        Assert.That(r.Notices, Is.Empty);
        Assert.That(r.C.Arg, Is.EqualTo(1));
        Assert.That(r.C.Source, Is.SameAs(r.Item));
        Assert.That(r.C.Data, Is.SameAs(r.Item.Data));
    }

    [Test]
    public void TheMovieLoadsDrivesItsRenderTargetAndTheCloseClearsTheItem()
    {
        var r = new Rig();
        Assert.That(r.C.Open(r.Model, r.Item, 1), Is.True);
        bool? action = null;
        r.C.ShowActionList = _ => (action = true).Value;
        var (rt, p) = src.Open(r.C.Movie, x => x.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var host = new UiHost(p, r.C, r.Model);
        Assert.That(host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        Assert.That(action, Is.True, "initData was sent with the action list rule");
        Assert.That(rt.Errors, Is.Empty);

        int closed = 0;
        r.C.Closed += () => closed++;
        r.C.Close();
        r.C.Close();
        Assert.That(closed, Is.EqualTo(1));
        Assert.That(r.C.Shown, Is.Null);
        Assert.That(r.C.Source, Is.Null);
    }
}
