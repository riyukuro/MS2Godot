using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIItemMultiOpenBoxDialog (0x4f88) over the real movie (needs MS2_DATA): the first RequestItemBox falls due 500 ms
/// after the load, the next 100 ms after each opened answer, a failed answer shows its box and closes, OnCancel closes with
/// nothing sent. No language's strings are compared.</summary>
public class ItemMultiOpenBoxTests
{
    sealed class Rig
    {
        public ItemMultiOpenBoxController C = new(3);
        public long Now = 10_000;
        public List<ItemBoxRequest> Sent = new();
        public List<string> Notices = new();
        public int Closes;

        public Rig(bool inBag = true)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            var packs = new PackSet(d);
            var src = TestData.Movies(packs);
            var model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            C.NowMs = () => Now;
            C.InBag = _ => inBag;
            C.Send = Sent.Add;
            C.ShowNotice = Notices.Add;
            C.Closing += () => Closes++;
            C.State.Open(20300327, 1, "k", 5);
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            var host = new UiHost(p, C, model);
            Assert.That(host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }
    }

    [Test]
    public void TheSendsFallDueAndTheAnswersCountThemDown()
    {
        var r = new Rig();
        r.Now += 500; r.C.Tick();
        Assert.That(r.Sent, Is.Empty, "due 500 ms after the load, and strictly after");
        r.Now += 1; r.C.Tick(); r.C.Tick();
        Assert.That(r.Sent, Is.EqualTo(new[] { new ItemBoxRequest(20300327, 1, 3, "k") }), "once, at most the batch size");
        r.C.OnItemBox(new ItemBoxResult(20300327, 3, ItemBoxResult.ResultOpened));
        r.Now += 100; r.C.Tick();
        Assert.That(r.Sent, Has.Count.EqualTo(1), "100 ms after an answer, strictly after");
        r.Now += 1; r.C.Tick();
        Assert.That(r.Sent.Last(), Is.EqualTo(new ItemBoxRequest(20300327, 1, 2, "k")), "what is left");
        r.C.OnItemBox(new ItemBoxResult(20300327, 2, ItemBoxResult.ResultOpened));
        Assert.That((r.Closes, r.C.State.Active), Is.EqualTo((1, false)), "all opened: closed");
    }

    [Test]
    public void ABatchIsAtMostTheMultiOpenMaxCountNotTheLimit()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var tables = new GameTables(new PackSet(d), "en");
        Assert.That(tables.ItemBoxMultiOpenMaxCount, Is.LessThan(tables.ItemBoxMultiOpenLimitCount), "the batch is the smaller constant");
        // the host builds the controller from ItemBoxMultiOpenMaxCount; 300 boxes go out in batches of it
        var state = new ItemMultiOpenBox(tables.ItemBoxMultiOpenMaxCount);
        state.Open(20300327, 1, "k", 300);
        state.Loaded(0, true);
        Assert.That(state.Tick(501), Is.EqualTo(new ItemBoxRequest(20300327, 1, tables.ItemBoxMultiOpenMaxCount, "k")));
    }

    [Test]
    public void AFailedAnswerShowsItsBoxAndCloses()
    {
        var r = new Rig();
        r.C.OnItemBox(new ItemBoxResult(20300327, 0, ItemBoxResult.ResultInventoryFull));
        Assert.That((r.Notices.Count, r.Closes, r.C.State.Active), Is.EqualTo((1, 1, false)));
        var r2 = new Rig();
        r2.C.OnItemBox(new ItemBoxResult(20300327, 0, ItemBoxResult.ResultFailed));
        Assert.That((r2.Notices.Count, r2.Closes), Is.EqualTo((1, 1)));
        Assert.That(r2.Notices[0], Is.Not.EqualTo(r.Notices[0]).Or.Empty, "result 3 and 4 are different boxes");
    }

    [Test]
    public void CancelClosesWithNothingSent()
    {
        var r = new Rig();
        Assert.That(r.C.OnCallback("OnCancel", Array.Empty<object>()), Is.True);
        r.Now += 5000; r.C.Tick();
        Assert.That((r.Sent, r.Closes), Is.EqualTo((new List<ItemBoxRequest>(), 1)));
    }

    [Test]
    public void ABoxNoLongerInTheBagIsNeverSent()
    {
        var r = new Rig(inBag: false);
        r.Now += 5000; r.C.Tick();
        Assert.That(r.Sent, Is.Empty);
    }
}
