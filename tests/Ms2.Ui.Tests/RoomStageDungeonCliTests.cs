using System.Xml.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Formats.World;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>RoomStageDungeonCli (roomstagedungeoncli.cpp) and DungeonMissionCli (dungeonmissioncli.cpp): the packet modes and the stage's questions. No test needs data and none asserts a language-specific string.</summary>
public class RoomStageDungeonCliTests
{
    static DungeonMissionTable Missions() => new(XElement.Parse(
        "<ms2><DungeonMission id='1' type='PlayTime' maxScore='30'/><DungeonMission id='2' type='DeathCount' maxScore='20' applyCount='2'/>" +
        "<DungeonMission id='3' type='LimitUserCount' maxScore='10'/><DungeonMission id='4' type='NoSuchClass' maxScore='5'/>" +
        "<DungeonMission id='5' type='Trigger' maxScore='15' applyCount='3' isPenaltyType='true'/></ms2>"), null);

    static DungeonRoomTable Rooms() => new(f => f switch
    {
        "DungeonConfig.xml" => XElement.Parse("<ms2><DungeonConfig><MissionRank><group id='3' maxScore='100'><rank score='10'/><rank score='20'/><rank score='30'/></group></MissionRank></DungeonConfig></ms2>"),
        "DungeonRoom.xml" => XElement.Parse("<ms2><dungeonRoom dungeonRoomID='21001001' groupType='3'/></ms2>"),
        _ => null,
    }, null, null, null);

    static RoomStageDungeonCli Stage(int stage = 0, bool flag = true) => new(21001001, stage, flag, Rooms(), Missions());

    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    static void Init(RoomStageDungeonCli s, params (int id, short score)[] m)
    {
        s.OnPacket(R(w =>
        {
            w.WriteByte(0); w.WriteInt(m.Length);
            foreach (var (id, score) in m)
            {
                w.WriteInt(id); w.WriteShort(score);
                if (id == 1) { w.WriteLong(111); w.WriteLong(222); }
                else if (id == 2 || id == 5) w.WriteShort(1);
            }
        }));
    }

    [Test]
    public void TheStageKnowsItsRoomStageAndFlag()
    {
        var s = Stage(0, true);
        Assert.That((s.Room?.Id, s.Stage, s.Flag, s.IsStageFlagSet), Is.EqualTo((21001001u, 0, true, true)));
        Assert.That(Stage(1, true).IsStageFlagSet, Is.False, "only stage 0");
        Assert.That(Stage(0, false).IsStageFlagSet, Is.False);
        Assert.That(new RoomStageDungeonCli(7, 0, true, Rooms(), Missions()).Room, Is.Null, "a room the table lacks");
    }

    [Test]
    public void ModeZeroCreatesTheMissionsAndReadsEachBodyByItsClass()
    {
        var s = Stage();
        Init(s, (1, 5), (2, 7), (3, 4));
        Assert.That(s.Missions.Select(m => m.Id), Is.EqualTo(new uint[] { 1, 2, 3 }));
        var play = s.FindMission(1);
        Assert.That((play.Score, play.Time1, play.Time2), Is.EqualTo(((short)5, 111UL, 222UL)));
        Assert.That((s.FindMission(2).Score, s.FindMission(2).Count), Is.EqualTo(((short)7, (short)1)));
        Assert.That((s.FindMission(3).Score, s.FindMission(3).Count), Is.EqualTo(((short)4, (short)0)), "LimitUserCount reads the score only");
        Assert.That((s.Score, s.MaxScore), Is.EqualTo((16, 60)));
    }

    [Test]
    public void ModeZeroDropsTheOldMissionsAndStopsAtAMissionTheClientCannotCreate()
    {
        var s = Stage();
        Init(s, (1, 5));
        s.OnPacket(R(w => { w.WriteByte(0); w.WriteInt(3); w.WriteInt(3); w.WriteShort(2); w.WriteInt(4); w.WriteShort(9); w.WriteInt(9); w.WriteShort(1); }));
        Assert.That(s.Missions.Select(m => m.Id), Is.EqualTo(new uint[] { 3 }), "id 4 has no class: the list ends there; id 1 is gone");
        s.OnPacket(R(w => { w.WriteByte(0); w.WriteInt(1); w.WriteInt(99); }));
        Assert.That(s.Missions, Is.Empty, "an unknown id ends the list too");
    }

    [Test]
    public void ModeOneUpdatesTheNamedMissionsAndStopsAtAnUnknownId()
    {
        var s = Stage();
        Init(s, (2, 1), (3, 1));
        s.OnPacket(R(w => { w.WriteByte(1); w.WriteInt(3); w.WriteInt(2); w.WriteShort(8); w.WriteShort(2); w.WriteInt(99); w.WriteInt(3); w.WriteShort(9); }));
        Assert.That((s.FindMission(2).Score, s.FindMission(2).Count), Is.EqualTo(((short)8, (short)2)));
        Assert.That(s.FindMission(3).Score, Is.EqualTo(1), "the packet stopped at the unknown id (the count said 3)");
    }

    [Test]
    public void ModeTwoSyncsAllInOrderAndStopsAtADifferentCountOrId()
    {
        var s = Stage();
        Init(s, (2, 1), (3, 1));
        s.OnPacket(R(w => { w.WriteByte(2); w.WriteInt(2); w.WriteInt(2); w.WriteShort(5); w.WriteShort(1); w.WriteInt(3); w.WriteShort(6); }));
        Assert.That((s.FindMission(2).Score, s.FindMission(3).Score), Is.EqualTo(((short)5, (short)6)));
        s.OnPacket(R(w => { w.WriteByte(2); w.WriteInt(3); w.WriteInt(2); w.WriteShort(9); w.WriteShort(1); }));
        Assert.That(s.FindMission(2).Score, Is.EqualTo(5), "a different count: nothing read");
        s.OnPacket(R(w => { w.WriteByte(2); w.WriteInt(2); w.WriteInt(2); w.WriteShort(7); w.WriteShort(1); w.WriteInt(9); w.WriteShort(1); }));
        Assert.That((s.FindMission(2).Score, s.FindMission(3).Score), Is.EqualTo(((short)7, (short)6)), "the first mission was updated before the id mismatch");
    }

    [Test]
    public void ModesThreeAndFourSetTheStageAndTheFlagAndModeFiveGivesUp()
    {
        var s = Stage(0, false);
        s.OnPacket(R(w => { w.WriteByte(3); w.WriteByte(2); }));
        s.OnPacket(R(w => { w.WriteByte(4); w.WriteBool(true); }));
        Assert.That((s.Stage, s.Flag), Is.EqualTo((2, true)));
        int gaveUp = 0; s.GaveUp += () => gaveUp++;
        s.OnPacket(R(w => w.WriteByte(5)));
        s.OnPacket(R(w => w.WriteByte(6)));
        Assert.That(gaveUp, Is.EqualTo(1), "mode 6 and above do nothing");
    }

    [Test]
    public void TheRankIsTheRoomsRankOfTheScoreAndZeroWithoutTheRoom()
    {
        var s = Stage();
        Init(s, (1, 25), (3, 5));
        Assert.That(s.Score, Is.EqualTo(30));
        Assert.That(s.Rank, Is.EqualTo(3), "DungeonRoomTable.GetRank of 30");
        Assert.That(new RoomStageDungeonCli(7, 0, true, Rooms(), Missions()).Rank, Is.Zero);
    }

    [Test]
    public void TheMissionAnswersMaxScoreAndPenaltyClearedByTheClientsRules()
    {
        var s = Stage();
        Init(s, (3, 10), (5, 15), (2, 20));
        Assert.That(s.FindMission(3).IsMaxScore, Is.True);
        Assert.That(s.FindMission(5).IsMaxScore, Is.False, "never for a penalty mission");
        Assert.That(s.FindMission(2).IsMaxScore, Is.True);
        Assert.That(s.FindMission(2).IsPenaltyCleared, Is.False, "the client's test needs a step count below 0");
    }
}
