using System.Xml.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The fishing book (UIFishingBookDialog, uifishingbookdialog.cpp): CFishData, CFishingTable's fish, the catch
/// records and the dialog run by its movie (the movie tests need MS2_DATA).</summary>
public class FishingBookTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
        }
        return packs;
    }

    static XDocument Doc(string xml) => XDocument.Parse(xml);

    static FishTable Small() => new(Doc(
        "<ms2><fish id='1' fishMastery='0' rank='1' smallSize='10-20' bigSize='40-60' companion='2' habitat='ocean'/>" +
        "<fish id='2' fishMastery='0' rank='1' smallSize='10-20' bigSize='40-60' companion='1' habitat='ocean'/>" +
        "<fish id='3' fishMastery='0' rank='2' smallSize='10-20' bigSize='40-60' fishingBook='0' habitat='ocean'/>" +
        "<fish id='4' fishMastery='0' rank='2' smallSize='bad' bigSize='40-60'/></ms2>"),
        Doc("<ms2><fish id='1' habitat='2000001, 2000002, 2000003, 2000004'/></ms2>"), _ => true);

    [Test]
    public void SizeRangesFollowSmallAndBigSize()
    {
        var f = Small().Get(1);
        Assert.That(f.Sizes, Is.EqualTo(new[] { (10u, 19u), (20u, 39u), (40u, 59u), (60u, 600u) }));
        Assert.That(new[] { 9u, 10u, 19u, 20u, 39u, 40u, 60u, 601u }.Select(f.GetSizeGrade), Is.EqualTo(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }));
    }

    [Test]
    public void AFishWithoutTwoSizesIsNotLoadedAndTheBookListsTheBookFishOnly()
    {
        var t = Small();
        Assert.That(t.Get(4), Is.Null);
        Assert.That(t.Count, Is.EqualTo(3));
        var h = FishingSpots.HabitatId("ocean");
        Assert.That(t.IdsByHabitat(t.Get(1).Habitat, true), Is.EqualTo(new uint[] { 1, 2 }));
        Assert.That(t.IdsByHabitat(t.Get(1).Habitat, false), Is.EqualTo(new uint[] { 1, 2, 3 }));
        Assert.That(t.Get(1).Habitat, Is.EqualTo(h));
    }

    [Test]
    public void MapListKeepsTheFirstThreeExistingMaps()
    {
        Assert.That(Small().MapList(1, m => m != 2000002), Is.EqualTo("$map:2000001$, $map:2000003$, $map:2000004$"));
        Assert.That(Small().MapList(2, _ => true), Is.EqualTo(""));
    }

    [Test]
    public void AListReplacesTheRecordsAndACatchMakesANewOne()
    {
        var d = new FishingBookData();
        Assert.That(d.Apply(new FishingMessage.List(new[] { new FishingMessage.Record(1, 5, 2, 30) }), true), Is.True);
        Assert.That(d.Find(1).IsNew, Is.False);
        Assert.That(d.Apply(new FishingMessage.Result(2, 50, true, false, new FishingMessage.Record(2, 1, 1, 50)), true), Is.True);
        Assert.That((d.Find(2).IsNew, d.Find(2).Size), Is.EqualTo((true, 50u)));
        Assert.That(d.Apply(new FishingMessage.Result(2, 55, true, false, new FishingMessage.Record(2, 2, 2, 55)), true), Is.True);
        Assert.That((d.Find(2).IsNew, d.Find(2).TotalCount), Is.EqualTo((true, 2)));
        Assert.That(d.Apply(new FishingMessage.Result(3, 5, false, false, null), true), Is.False);
        d.ClearNew();
        Assert.That(d.All.Any(f => f.IsNew), Is.False);
        Assert.That(d.Apply(new FishingMessage.List(Array.Empty<FishingMessage.Record>()), false), Is.False, "feature off");
        Assert.That(d.Count, Is.EqualTo(2));
    }

    [Test]
    public void ACompanionsRecordMakesTheFishKnownAndGradesItsSize()
    {
        var t = Small();
        var d = new FishingBookData();
        d.Apply(new FishingMessage.List(new[] { new FishingMessage.Record(2, 1, 0, 45) }), true);
        Assert.That((d.Has(t, 1), d.Has(t, 2), d.Has(t, 3)), Is.EqualTo((true, true, false)));
        Assert.That((d.SizeGrade(t, 1), d.SizeGrade(t, 2)), Is.EqualTo((0, 3)), "grade of the fish's own record only");
    }

    [Test]
    public void TitleTagsFindTheTitleIdOfATag()
    {
        var t = new TitleTags(Doc("<ms2><key id='10' name='n' tag='fisherBeginner'/><key id='11' name='n' tag='fisherPro' feature='Off'/></ms2>"),
            e => (string)e.Attribute("feature") == null);
        Assert.That((t.FindTitleId("fisherBeginner"), t.FindTitleId("fisherPro"), t.FindTitleId("x")), Is.EqualTo((10u, 0u, 0u)));
    }

    // ---- the dialog run by its movie ----

    static (UiHost, FishingBookController) Open(FishingBookData data = null)
    {
        Packs();
        var c = new FishingBookController { Data = data ?? new FishingBookData() };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) h.Player.Tick();
        return (h, c);
    }

    static int BagCount(UiHost h) => (int)Rt.ToNumber(h.Get(h.Get(h.Child("m_bag"), "dataProvider"), "length"));

    [Test]
    public void TheBookFillsTwentyTilesAndTheSortedFirstTab()
    {
        var (h, c) = Open();
        Assert.That(BagCount(h), Is.EqualTo(FishingBookController.PageSize));
        var ids = c.SortedIds(0);
        Assert.That(ids, Is.Not.Empty);
        var t = tables.Fish;
        for (int i = 1; i < ids.Count; i++)
        {
            var a = t.Get(ids[i - 1]); var b = t.Get(ids[i]);
            Assert.That((a.Mastery, a.Rank).CompareTo((b.Mastery, b.Rank)), Is.LessThanOrEqualTo(0), "mastery then rank");
        }
        Assert.That(ids.All(i => t.Get(i).FishingBook && t.Get(i).Habitat == 0), Is.True);
    }

    [Test]
    public void NextAndPreviousPageStayInsideTheTabsPages()
    {
        var (h, c) = Open();
        Assert.That(c.Page, Is.EqualTo(0));
        c.OnCallback("onPrevPage", Array.Empty<object>());
        Assert.That(c.Page, Is.EqualTo(0));
        int pages = (c.SortedIds(0).Count + 19) / 20;
        for (int i = 0; i < pages + 2; i++) c.OnCallback("onNextPage", Array.Empty<object>());
        Assert.That(c.Page, Is.EqualTo(pages - 1));
        c.OnCallback("onTabChange", new object[] { 1 });
        Assert.That((c.Tab, c.Page), Is.EqualTo((1, 0)));
        Assert.That(BagCount(h), Is.EqualTo(FishingBookController.PageSize));
    }

    [Test]
    public void ClosingTheBookClearsTheNewMarks()
    {
        var d = new FishingBookData();
        d.Apply(new FishingMessage.Result(1, 1, true, false, new FishingMessage.Record(1, 1, 0, 1)), true);
        var (_, c) = Open(d);
        Assert.That(d.Find(1).IsNew, Is.True);
        c.Close();
        Assert.That(d.Find(1).IsNew, Is.False);
    }

    // ---- the dialog's own output ----

    static (FishingBookController, UiHost) Opened()
    {
        var (h, c) = Open();
        h.Model.HasLocalPlayer = true;
        return (c, h);
    }

    static object M(UiHost h, AsObject o, string k) => h.Get(o, k);

    [Test]
    public void SetFishSendsAcquireAndMedalZeroForAnIdWithoutData()
    {
        var (c, h) = Opened();
        var o = c.FishObject(0xfffffff0u);
        Assert.That((Rt.ToNumber(M(h, o, "acquire")), Rt.ToNumber(M(h, o, "medal"))), Is.EqualTo((0.0, 0.0)));
        Assert.That(Rt.IsNullish(M(h, o, "fishName")), Is.True, "no other member");
    }

    [Test]
    public void SetFishSendsTheFishDataForAKnownId()
    {
        var d = new FishingBookData();
        var (_, c0) = Open();
        uint id = c0.SortedIds(0)[0];
        d.Apply(new FishingMessage.List(new[] { new FishingMessage.Record(id, 2, 0, 50) }), true);
        var (h, c) = Open(d);
        var o = c.FishObject(id);
        Assert.That(Rt.ToNumber(M(h, o, "acquire")), Is.EqualTo(1));
        Assert.That(M(h, o, "iconPath"), Is.EqualTo(tables.Fish.Get(id).IconPath));
        Assert.That(Rt.ToString(M(h, o, "totalCatchCount")), Is.Not.Empty);
        var unknown = Open().Item2.FishObject(id);
        Assert.That(Rt.ToNumber(h.Get(unknown, "acquire")), Is.EqualTo(0), "not caught: not acquired");
    }

    [Test]
    public void SetFisherSendsTheMasteryInItsGradeAndTheBestTitle()
    {
        var (c, h) = Opened();
        var g = tables.Fishing;
        h.Model.Player.FishingMastery = g.Value(2) + 7;
        var o = c.FisherObject();
        Assert.That((Rt.ToNumber(M(h, o, "curMastery")), Rt.ToNumber(M(h, o, "maxMastery"))),
            Is.EqualTo((7.0, (double)(g.Value(3) - g.Value(2)))));
        Assert.That(Rt.ToString(M(h, o, "fisherTitle")), Is.EqualTo(tables.Common("s_fishing_none_title")), "no title: s_fishing_none_title");
        Assert.That(Rt.ToString(M(h, o, "fisherGradeName")), Is.EqualTo(c.GradeName(2)));
    }

    [Test]
    public void BestFisherTitleIdTakesTheFirstOfTheListThePlayerHas()
    {
        var (c, h) = Opened();
        var tt = tables.TitleTags;
        uint pro = tt.FindTitleId("fisherPro"), beginner = tt.FindTitleId("fisherBeginner"), master = tt.FindTitleId("fisherMaster");
        Assume.That(pro != 0 && beginner != 0 && master != 0);
        c.HasTitleId = id => id == beginner || id == pro;
        Assert.That(c.BestFisherTitleId(), Is.EqualTo(pro), "fisherPro comes before fisherBeginner");
        c.HasTitleId = id => id == beginner;
        Assert.That(c.BestFisherTitleId(), Is.EqualTo(beginner));
        c.HasTitleId = _ => false;
        c.AchieveCompleted = a => a == FishingBookController.FisherTitles[2].AchieveId;
        Assert.That(c.BestFisherTitleId(), Is.EqualTo(master), "a completed trophy counts");
        h.Model.HasLocalPlayer = false;
        Assert.That(c.BestFisherTitleId(), Is.EqualTo(0u));
    }

    static ByteReader Pkt(Action<ByteWriter> f)
    { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    [Test]
    public void AModeSevenListReplacesTheRecordsOnlyWhenTheFeatureIsOn()
    {
        var d = new FishingBookData();
        d.Apply(new FishingMessage.Result(9, 1, true, false, new FishingMessage.Record(9, 1, 0, 1)), true);
        ByteReader List() => Pkt(w => { w.WriteByte(7); w.WriteInt(1); w.WriteInt(4); w.WriteInt(1); w.WriteInt(0); w.WriteInt(10); });
        d.Receive(List(), false, out var ch);
        Assert.That((ch, d.Count, d.Find(9) != null), Is.EqualTo((false, 1, true)), "feature off: nothing read, nothing changed");
        var bad = Pkt(w => w.WriteByte(7));   // truncated: would throw if read
        Assert.DoesNotThrow(() => d.Receive(bad, false, out _));
        d.Receive(List(), true, out ch);
        Assert.That((ch, d.Count, d.Find(9) == null, d.Find(4) != null), Is.EqualTo((true, 1, true, true)));
    }
}
