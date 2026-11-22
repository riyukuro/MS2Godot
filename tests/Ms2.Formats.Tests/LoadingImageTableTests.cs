using System.Linq;
using System.Xml.Linq;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The field-move loading picture choice (UILoadingProgress::GetLoadingImagePath / CLoadingImageTable), on made-up rows.</summary>
public class LoadingImageTableTests
{
    const string Always = "startTime=\"0-0-0-0-0-0\" endTime=\"0-0-0-0-0-0\"";

    static LoadingImageTable Table(params string[] rows) =>
        new(XDocument.Parse("<ms2>" + string.Concat(rows) + "</ms2>"));

    static string Row(int code, string bg, string extra = "") => $"<image code=\"{code:D8}\" locale=\"\" {Always} bgLoading=\"{bg}\" {extra} />";

    [Test]
    public void AnNaPackKeepsItsOwnRowsAndDropsKr()
    {
        string Loc(int code, string bg, string loc) => $"<image code=\"{code:D8}\" locale=\"{loc}\" {Always} bgLoading=\"{bg}\" />";
        var doc = XDocument.Parse("<ms2>" + Loc(100, "Na_01", "NA") + Loc(100, "Kr_01", "KR") + "</ms2>");
        Assert.That(new LoadingImageTable(doc, "NA").Candidates(100, DateTime.Now).Single(), Does.Contain("na_01"));
        Assert.That(new LoadingImageTable(doc).Candidates(100, DateTime.Now).Single(), Does.Contain("kr_01"));
    }

    [Test]
    public void SameOwnPicturesGiveTheScreenshot()
    {
        // two towns listing the same pictures: the client keeps the field on screen
        var t = Table(Row(100, "Town_01"), Row(100, "Town_02"), Row(200, "Town_02"), Row(200, "Town_01"));
        Assert.That(t.SameImages(200, 100, DateTime.Now), Is.True);
        Assert.That(t.PickForMove(200, 100, new Random(1)), Is.Null);
    }

    [Test]
    public void OtherOwnPicturesGiveATargetPicture()
    {
        var t = Table(Row(100, "Town_01"), Row(300, "Forest_01"), Row(300, "Forest_02"));
        var pick = t.PickForMove(300, 100, new Random(1));
        Assert.That(pick, Is.AnyOf("loadingimage/forest_01.dds", "loadingimage/forest_02.dds"));
    }

    [Test]
    public void CommonRowsCountForHasImageButNotForSameImages()
    {
        // neither map has pictures of its own: IsSameImages compares two empty sets, so the screenshot, even with a common row
        var common = "<image code=\"0\" locale=\"\" " + Always + " bgLoading=\"Common_01\" exceptMap=\"\" />";
        var t = Table(common);
        Assert.That(t.Candidates(400, DateTime.Now), Is.EqualTo(new[] { "loadingimage/common_01.dds" }));
        Assert.That(t.PickForMove(400, 500, new Random(1)), Is.Null);
        // the target has its own row, the map left has none: a picture, chosen among the common and own rows
        var t2 = Table(common, Row(400, "Own_01"));
        Assert.That(t2.PickForMove(400, 500, new Random(1)), Is.AnyOf("loadingimage/common_01.dds", "loadingimage/own_01.dds"));
    }

    [Test]
    public void NoPictureForTheTargetGivesTheScreenshot()
    {
        var t = Table(Row(100, "Town_01"));
        Assert.That(t.PickForMove(600, 100, new Random(1)), Is.Null);
    }

    [Test]
    public void ATransitionMovieOfTheMapLeftGivesTheScreenshot()
    {
        var t = Table(Row(100, "Town_01"), Row(300, "Forest_01"),
            $"<image code=\"{100:D8}\" locale=\"\" {Always} bgLoading=\"\" playOneTime=\"0\" movieFileName=\"Common/Leave.usm\" />");
        Assert.That(t.HasMovie(100, 100), Is.True);
        Assert.That(t.PickForMove(300, 100, new Random(1)), Is.Null);
    }

    [Test]
    public void ExpiredOwnRowsDoNotCount()
    {
        var old = $"<image code=\"{300:D8}\" locale=\"\" startTime=\"2001-1-1-0-0-0\" endTime=\"2001-2-1-0-0-0\" bgLoading=\"Event_01\" />";
        var t = Table(Row(100, "Town_01"), Row(300, "Town_01"), old);
        Assert.That(t.PickForMove(300, 100, new Random(1)), Is.Null);
    }
}
