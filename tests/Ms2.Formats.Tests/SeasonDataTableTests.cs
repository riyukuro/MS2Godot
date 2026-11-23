using System.Linq;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>CSeasonDataTable::ReadData (@0x1407927d0, seasondatatable.cpp:510-535) and ReadNames (@0x1407931f0) for one category.</summary>
public class SeasonDataTableTests
{
    static SeasonDataTable Make(string rows, string names = "") =>
        new(XDocument.Parse("<ms2>" + rows + "</ms2>"), "GuildPvP", XDocument.Parse("<ms2>" + names + "</ms2>").Root.Elements("season"));

    [Test]
    public void ARowReadsItsIdDatesAndSevenGrades()
    {
        var t = Make("<Season seasonID='3' seasonName='n' eventStart='2022-1-2-3-4-5' eventEnd='2022-2-1-0-0-0' grade1='0' grade2='10' grade3='20' grade4='30' grade5='40' grade6='50' grade7='60'/>");
        var s = t.Get(3);
        Assert.That(s, Is.Not.Null);
        Assert.That(s.SeasonName, Is.EqualTo("n"));
        Assert.That(s.Grades, Is.EqualTo(new[] { 0, 10, 20, 30, 40, 50, 60 }));
        Assert.That(s.LocalStart, Is.EqualTo(new System.DateTimeOffset(2022, 1, 2, 3, 4, 5, System.TimeSpan.Zero).ToUnixTimeSeconds()));
        Assert.That(s.LocalEnd, Is.EqualTo(new System.DateTimeOffset(2022, 2, 1, 0, 0, 0, System.TimeSpan.Zero).ToUnixTimeSeconds()));
    }

    [Test]
    public void ADateOutOfRangeOrMissingIsTimeZeroAndZeroTakesNoZoneOffset()
    {
        Assert.That(new[] { null, "", "1899-1-1-0-0-0", "2022-13-1-0-0-0", "2022-1-0-0-0-0", "2022-1-1-24-0-0", "2022-1-1-0-60-0", "2022-1-1-0-0-60" }.Select(SeasonDataTable.Time),
            Is.All.EqualTo(0L));
        Assert.That(SeasonDataTable.Time("2022-1-1"), Is.EqualTo(new System.DateTimeOffset(2022, 1, 1, 0, 0, 0, System.TimeSpan.Zero).ToUnixTimeSeconds()));
        var s = new SeasonDataTable.Season { LocalStart = 0, LocalEnd = 100 };
        Assert.That((s.Start(-32400), s.End(-32400)), Is.EqualTo((0L, 100L - 32400)));
    }

    [Test]
    public void TheCurrentSeasonIsTheFirstByIdHoldingNowWithTheZoneOffsetApplied()
    {
        var t = Make("<Season seasonID='2' eventStart='2022-3-1-0-0-0' eventEnd='2022-4-1-0-0-0'/><Season seasonID='1' eventStart='2022-1-1-0-0-0' eventEnd='2022-3-1-0-0-0'/>");
        long mar1 = SeasonDataTable.Time("2022-3-1-0-0-0");
        // KST: zone -32400, the local midnight is nine hours before UTC midnight
        Assert.That(t.Current(mar1 - 32400 - 1, -32400)?.Id, Is.EqualTo(1));
        Assert.That(t.Current(mar1 - 32400, -32400)?.Id, Is.EqualTo(2));
        Assert.That(t.Current(mar1, -32400)?.Id, Is.EqualTo(2));
        Assert.That(t.Current(SeasonDataTable.Time("2022-4-1-0-0-0") - 32400, -32400), Is.Null);
    }

    [Test]
    public void ALaterRowOfTheSameIdReplacesAndTheIdsListAscending()
    {
        var t = Make("<Season seasonID='5' seasonName='a'/><Season seasonID='2'/><Season seasonID='5' seasonName='b'/>");
        Assert.That(t.All.Select(s => s.Id), Is.EqualTo(new[] { 2, 5 }));
        Assert.That(t.Get(5).SeasonName, Is.EqualTo("b"));
    }

    [Test]
    public void ANameRowOfThisTypeWithoutContentsGives()
    {
        var t = Make("<Season seasonID='1'/><Season seasonID='2'/>",
            "<season type='guildpvp' id='1' name='first'/><season type='GuildPvP' id='1' name='second'/><season type='PvP' id='2' name='other'/><season type='GuildPvP' id='2' contentsid='4' name='contents'/>");
        Assert.That(t.Get(1).DisplayName, Is.EqualTo("second"));
        Assert.That(t.Get(2).DisplayName, Is.EqualTo(""));
    }

    [Test]
    public void AWrongRootLoadsNothing()
    {
        Assert.That(new SeasonDataTable(XDocument.Parse("<root><Season seasonID='1'/></root>"), "GuildPvP").Count, Is.EqualTo(0));
    }

    [Test]
    public void TheDataHasTheGuildPvpSeasons()
    {
        var data = System.Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(data);
        var t = new SeasonDataTable(packs, "GuildPvP", new GameStrings(packs));
        Assert.That(t.Count, Is.GreaterThan(0));
        Assert.That(t.All.All(s => s.Id > 0 && s.Grades.Length == 7));
    }
}
