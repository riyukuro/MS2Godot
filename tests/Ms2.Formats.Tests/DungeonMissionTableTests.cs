using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>DungeonMissionDataTable (commonfiles/dungeonmissiondatatable.cpp, ReadFromXml @0x140318d40) and DungeonMissionData (@0x140314960, GetScore @0x140314c20).</summary>
public class DungeonMissionTableTests
{
    static DungeonMissionTable Make(string rows, Func<XElement, bool> available = null) => new(XElement.Parse("<ms2>" + rows + "</ms2>"), available);

    [Test]
    public void ARowKeepsItsBaseFieldsAndTheDefaultsOfTheRest()
    {
        var t = Make("<DungeonMission id='7' type='PlayTime' maxScore='30' applyCount='3' isPenaltyType='true'/><DungeonMission id='8' type='DeathCount'/>");
        var m = t.GetMission(7);
        Assert.That((m.Id, m.Type, m.MaxScore, m.ApplyCount, m.IsPenaltyType), Is.EqualTo((7u, "PlayTime", (short)30, (short)3, true)));
        var d = t.GetMission(8);
        Assert.That((d.MaxScore, d.ApplyCount, d.IsPenaltyType), Is.EqualTo(((short)0, (short)1, false)));
        Assert.That(t.GetMission(9), Is.Null);
    }

    [Test]
    public void ARowWithoutATypeOrWithIdZeroIsDroppedAndTheFirstRowOfAnIdStays()
    {
        var t = Make("<DungeonMission id='1' maxScore='5'/><DungeonMission id='0' type='PlayTime'/><DungeonMission id='2' type='A' maxScore='5'/><DungeonMission id='2' type='B' maxScore='9'/>");
        Assert.That(t.Count, Is.EqualTo(1));
        Assert.That(t.GetMission(2).Type, Is.EqualTo("A"));
    }

    [Test]
    public void ARowTheAvailabilityRefusesIsNotKept()
    {
        var t = Make("<DungeonMission id='1' type='A'/><DungeonMission id='2' type='A' feature='x'/>", e => e.Attribute("feature") == null);
        Assert.That(t.GetMission(1), Is.Not.Null);
        Assert.That(t.GetMission(2), Is.Null);
    }

    [Test]
    public void NoFileOrAForeignRootGivesNoRows()
    {
        Assert.That(new DungeonMissionTable(null, null).Count, Is.Zero);
        Assert.That(new DungeonMissionTable(XElement.Parse("<other><DungeonMission id='1' type='A'/></other>"), null).Count, Is.Zero);
    }

    [TestCase(false, 0, 0)]
    [TestCase(false, 1, 10)]
    [TestCase(false, 3, 30)]
    [TestCase(false, 9, 30)]
    [TestCase(false, -2, 0)]
    [TestCase(true, 0, 30)]
    [TestCase(true, 1, 20)]
    [TestCase(true, 3, 0)]
    [TestCase(true, 9, 0)]
    public void TheScoreIsTheStepsAppliedClampedToTheMaxScore(bool penalty, int count, int expected)
    {
        var t = Make($"<DungeonMission id='1' type='A' maxScore='30' applyCount='3' isPenaltyType='{penalty}'/>");
        Assert.That(t.GetMission(1).GetScore((short)count), Is.EqualTo((short)expected));
    }

    [Test]
    public void OneStepGivesTheWholeScore()
    {
        var m = Make("<DungeonMission id='1' type='A' maxScore='12'/>").GetMission(1);
        Assert.That((m.GetScore(0), m.GetScore(1), m.GetScore(5)), Is.EqualTo(((short)0, (short)12, (short)12)));
    }

    [Test]
    public void TheRowsOfTheDataSetLoadWithAScoreAndAType()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var t = new DungeonMissionTable(new PackSet(d));
        Assert.That(t.Count, Is.GreaterThan(0));
        foreach (var m in t.All)
        {
            Assert.That(m.Id, Is.Not.Zero);
            Assert.That(m.Type, Is.Not.Empty, "mission " + m.Id);
            Assert.That(m.GetScore(m.ApplyCount), Is.EqualTo(m.IsPenaltyType ? (short)0 : m.MaxScore), "mission " + m.Id);
        }
    }

    [Test]
    public void EveryMissionOfEveryRoomResolvesInTheTableWithAType()
    {
        // The whole list: DungeonRoomData.Missions() (the party missions, then the user missions) of every room names rows the table has.
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new PackSet(d);
        var missions = new DungeonMissionTable(packs);
        var rooms = new DungeonRoomTable(packs, new ClientConstants(Array.Empty<KeyValuePair<string, string>>()));
        var used = new SortedSet<uint>();
        foreach (var room in rooms.RoomList)
            foreach (uint id in room.Missions()) used.Add(id);
        Assert.That(used, Is.Not.Empty, "a data set whose rooms name missions");
        var missing = used.Where(id => missions.GetMission(id) == null).ToList();
        var typeless = used.Where(id => missions.GetMission(id) is { Type: "" }).ToList();
        Assert.That(missing, Is.Empty, "mission ids a room names that the table lacks");
        Assert.That(typeless, Is.Empty, "mission rows without a type");
    }
}
