using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The client's dungeon room tables (commonfiles/dungeonroom.cpp, dungeonrewardcoupon.cpp; ctor @0x14031f5d0).</summary>
public class DungeonRoomTableTests
{
    static DungeonRoomTable Make(Dictionary<string, string> files, params (string, string)[] constants) =>
        new(f => files.TryGetValue(f, out var x) ? XElement.Parse("<ms2>" + x + "</ms2>") : null, null, null,
            new ClientConstants(constants.Select(c => new KeyValuePair<string, string>(c.Item1, c.Item2))));

    static DungeonRoomTable Rooms(string rows, params (string, string)[] constants) =>
        Make(new() { ["DungeonRoom.xml"] = rows }, constants);

    static string Room(uint id, int group, uint gear = 0, int level = 0, int sort = 0, string extra = "") =>
        $"<dungeonRoom dungeonRoomID='{id}' groupType='{group}' gearScore='{gear}' limitPlayerLevel='{level}' sortOrder='{sort}' {extra}/>";

    static uint[] Ids(IEnumerable<DungeonRoomData> l) => l.Select(r => r.Id).ToArray();

    [Test]
    public void ARoomReadsItsAttributesAndKeepsTheDefaultsOfTheRest()
    {
        var t = Rooms("<dungeonRoom dungeonRoomID='11003001' dungeonLevel='60' limitEnter='yes' groupType='3' rewardExp='123456789012' rewardExpRate='1.5' " +
                      "rewardLimitedDropBoxIds='5,,6' rewardLimitedDropBoxIds2='7' fieldIDs='2000001,2000002' timerType='Gauge' limitDayOfWeeks='0,6' " +
                      "limitVIP='1' BossRankingType='KillRumble' partyMissions='1,2' userMissions='3' isChangeMaxUser='true' maxUserCount='4'/>");
        var r = t.GetRoom(11003001);
        Assert.That(r.DungeonLevel, Is.EqualTo(60));
        Assert.That(r.LimitEnter && r.LimitVip && r.IsChangeMaxUser);
        Assert.That(r.LimitType, Is.EqualTo(2));
        Assert.That(r.RewardExp, Is.EqualTo(123456789012L));
        Assert.That(r.RewardExpRate, Is.EqualTo(1.5f));
        Assert.That(r.RewardDropBoxIds, Is.EqualTo(new uint[] { 5, 6, 7 }), "list 1 then list 2, empty tokens dropped");
        Assert.That(r.FirstFieldId, Is.EqualTo(2000001u));
        Assert.That(r.TimerType, Is.EqualTo(2));
        Assert.That(r.LimitDayOfWeeks, Is.EqualTo((byte)0b01000001));
        Assert.That(r.BossRankingType, Is.EqualTo(3));
        Assert.That(r.Missions(), Is.EqualTo(new uint[] { 1, 2, 3 }));
        Assert.That(r.HasMissions);
        Assert.That(r.GearScore, Is.EqualTo(0u));
        Assert.That(r.HasEnterRequirement, Is.False);
        Assert.That(r.GetMaxUserCount(1), Is.EqualTo(2u));
        Assert.That(r.GetMaxUserCount(9), Is.EqualTo(4u));
    }

    [Test]
    public void IdHelpersAndTheRankTableByGroup()
    {
        var t = Rooms(Room(11003002, 3, extra: "unionRewardID='11003001'") + Room(21001001, 12) + Room(31001001, 8) + Room(41001001, 9, extra: "rankTableID='4'"));
        var r = t.GetRoom(11003002);
        Assert.That(r.DungeonId, Is.EqualTo(11003u));
        Assert.That(r.RoomIndex, Is.EqualTo(2u));
        Assert.That(r.RewardRoomId, Is.EqualTo(11003001u));
        Assert.That(t.GetRoom(21001001).RankTableId, Is.EqualTo(5u));
        Assert.That(t.GetRoom(31001001).RankTableId, Is.EqualTo(6u));
        Assert.That(t.GetRoom(41001001).RankTableId, Is.EqualTo(4u));
        Assert.That(t.GetRoom(11003002).RankTableId, Is.EqualTo(3u));
    }

    [Test]
    public void TheGroupSetsOrderByGearScoreLevelSortOrderAndId()
    {
        var t = Rooms(Room(5, 1, 100, 20) + Room(4, 1, 100, 10, sort: 2) + Room(3, 1, 100, 10, sort: 1) + Room(2, 1, 50, 99) + Room(9, 2, 10, 1) + Room(8, 0, 1, 1));
        Assert.That(Ids(t.GetGroupRooms(1)), Is.EqualTo(new uint[] { 2, 3, 4, 5 }));
        Assert.That(Ids(t.GetGroupRooms(2)), Is.EqualTo(new uint[] { 9 }));
        Assert.That(t.GetGroupRooms(0), Is.Empty, "group 0 is in no set");
        Assert.That(t.GetGroupRooms(77), Is.Empty);
        Assert.That(t.RoomCount, Is.EqualTo(6));
        Assert.That(Ids(t.RoomList), Is.EqualTo(new uint[] { 2, 3, 4, 5, 8, 9 }), "the room list is by id");
    }

    [Test]
    public void AMatchListTakesGroups1And2AndStopsAtThePickCount()
    {
        string rows = Room(1, 1, 10, 1) + Room(2, 2, 20, 2) + Room(3, 1, 30, 3) + Room(4, 3, 5, 1) + Room(5, 2, 40, 4);
        var t = Rooms(rows, ("DungeonMatchRecommendPickCount", "2"));
        // from the first room at or below (gear 40, level 9) in descending order: 5, 3, 2, 1 ; the limit stops after two
        Assert.That(Ids(t.GetMatchRooms(9, 40)), Is.EqualTo(new uint[] { 5, 3 }));
        Assert.That(Ids(Rooms(rows, ("DungeonMatchRecommendPickCount", "0")).GetMatchRooms(9, 40)), Is.EqualTo(new uint[] { 5, 3, 2, 1 }), "0 means no limit");
        Assert.That(Ids(Rooms(rows).GetMatchRooms(9, 40)).Length, Is.EqualTo(4), "the default is 6, there are 4");
        Assert.That(Ids(t.GetRoomsForLevel(9, 40)), Is.EqualTo(new uint[] { 5, 3, 2, 1, 4 }), "rooms for a level are not limited by the pick count and take group 3 as well");
    }

    [Test]
    public void ARoomAboveTheKeyIsSkippedAndALevelBelowTheRoomsLimitIsDropped()
    {
        var t = Rooms(Room(1, 1, 10, 5) + Room(2, 1, 20, 5) + Room(3, 1, 20, 15) + Room(4, 1, 30, 1));
        // key (gear 20, level 10): descending is 4 (30), 3 (20/15), 2 (20/5), 1 (10/5); 4 and 3 are above, the start is room 2
        Assert.That(Ids(t.GetGroupMatchRooms(1, 10, 20)), Is.EqualTo(new uint[] { 2, 1 }));
        // key (gear 20, level 15): room 3 equals the key and is the start; rooms with a limit above the level 12 are dropped
        Assert.That(Ids(t.GetGroupMatchRooms(1, 15, 20)), Is.EqualTo(new uint[] { 3, 2, 1 }));
        Assert.That(Ids(t.GetGroupMatchRooms(1, 4, 20)), Is.Empty, "the level is below every room's limit");
        Assert.That(Ids(t.GetGroupMatchRooms(2, 99, 99)), Is.Empty);
    }

    [Test]
    public void TheBossRankingRoomsAreInsertedByGearScoreThenId()
    {
        var t = Rooms(Room(1, 5, 30, extra: "BossRanking='1'") + Room(2, 5, 10, extra: "BossRanking='1'") + Room(3, 5, 30, extra: "BossRanking='1'") +
                      Room(4, 5, 10, extra: "BossRanking='1'") + Room(5, 5, 99) + Room(6, 6, 1, extra: "BossRanking='1'"));
        // id order 1(30) 2(10) 3(30) 4(10): [1]; 2 before 1 (gear 30 > 10); 3 (gear 30): meets 2 (10, smaller) then 1 (30 equal, id 1 < 3) so after 1; 4 (gear 10): meets 2 (10 equal, id 2 < 4) so after 2
        Assert.That(Ids(t.GetBossRankingRooms(5)), Is.EqualTo(new uint[] { 2, 4, 1, 3 }));
        Assert.That(t.GetRoom(1).BossRankingType, Is.EqualTo(1), "BossRanking without a type means kill");
    }

    [Test]
    public void ALaterRoomOfAnIdReplacesAndALinkFollowsTheCouponAndRound()
    {
        var files = new Dictionary<string, string>
        {
            ["DungeonRoom.xml"] = Room(7, 1, 1) + Room(7, 2, 2, extra: "extraRewardTicketTableID='3' extraRewardTicketBaseCount='10' roundID='4'") + Room(8, 1, extra: "unionRewardID='7'") + Room(9, 1, extra: "unionRewardID='99'"),
            ["DungeonRewardCoupon.xml"] = "<dungeonRewardCoupon id='3' ticketTag='x' baseItemID='20' maxExtraCount='3'><baseRate v='1'/><baseRate v='2'/><baseRate v='5'/></dungeonRewardCoupon>" +
                                          "<dungeonRewardCoupon id='3' ticketTag='y' baseItemID='21' maxExtraCount='3' canBuyInMeratMarket='false'><baseRate v='1'/><baseRate v='2'/><baseRate v='5'/></dungeonRewardCoupon>",
            ["DungeonRoundData.xml"] = "<DungeonRoundGroup id='4'><v rewardID='1' gearScore='5'/><v rewardID='2' gearScore='6'/><v rewardID='3' gearScore='7'/></DungeonRoundGroup><DungeonRoundGroup id='0'/>",
        };
        var t = Make(files);
        var r = t.GetRoom(7);
        Assert.That(r.GroupType, Is.EqualTo(2), "the later row wins");
        Assert.That(t.GetGroupRooms(1).Select(x => x.Id), Is.EqualTo(new uint[] { 8, 9 }));
        Assert.That(r.HasExtraRewardTicket);
        Assert.That(r.HasRoundGroup && !r.IsSingleRound);
        Assert.That(t.GetRoom(8).IsSingleRound);
        Assert.That(t.GetRoundGroup(4).GetRewardIds(1, 9), Is.EqualTo(new uint[] { 2, 3 }));
        Assert.That(t.GetRoundGroup(0), Is.Null, "a group of id 0 is dropped");
        Assert.That(t.CouponBaseItemId(r), Is.EqualTo(21u), "a later coupon row of an id replaces");
        Assert.That(t.CanBuyCouponInMeratMarket(r), Is.False);
        Assert.That(t.CouponMaxExtraCount(r), Is.EqualTo(3));
        Assert.That(t.ExtraRewardTicketCost(r, 0), Is.EqualTo(10u));
        Assert.That(t.ExtraRewardTicketCost(r, 2), Is.EqualTo(50u));
        Assert.That(t.ExtraRewardTicketCost(r, 99), Is.EqualTo(50u), "the count is clamped to the last rate");
        Assert.That(t.GetCoupon(t.GetRoom(8)), Is.SameAs(t.GetCoupon(3)), "a room with unionRewardID uses that room's coupon");
        Assert.That(t.GetCoupon(t.GetRoom(9)), Is.Null, "an unknown union room has none");
        Assert.That(t.CouponBaseItemId(t.GetRoom(9)), Is.EqualTo(0u));
        Assert.That(t.GetRoom(8).CouponLink, Is.Null);
    }

    [Test]
    public void ACouponWithoutTheFlagCanBeBoughtInTheMarket()
    {
        var t = Make(new() { ["DungeonRewardCoupon.xml"] = "<dungeonRewardCoupon id='1'/>" });
        Assert.That(t.GetCoupon(1).CanBuyInMeratMarket);
        Assert.That(t.GetCoupon(2), Is.Null);
        Assert.That(t.GetCoupon(0), Is.Null);
    }

    [Test]
    public void RankScoresStopAtAScoreNotAboveTheLastAndRanksCountFromOne()
    {
        var t = Make(new()
        {
            ["DungeonConfig.xml"] = "<DungeonConfig><MissionRank><group id='3' maxScore='100'><rank score='10'/><rank score='20'/><rank score='20'/><rank score='30'/></group>" +
                                    "<group id='0' maxScore='5'><rank score='1'/></group><group id='3'><rank score='40'/></group></MissionRank></DungeonConfig>",
            ["DungeonRoom.xml"] = Room(1, 3) + Room(2, 9),
        });
        var r = t.GetRoom(1);
        var rr = t.GetRankRequire(r);
        Assert.That(rr.Scores, Is.EqualTo(new short[] { 10, 20, 40 }), "the repeated 20 ends the first group's list; a second group of the id appends");
        Assert.That(rr.MaxScore, Is.EqualTo(100));
        Assert.That(t.GetMaxScore(r), Is.EqualTo(100));
        Assert.That(t.GetRank(r, 5), Is.EqualTo(0));
        Assert.That(t.GetRank(r, 10), Is.EqualTo(1));
        Assert.That(t.GetRank(r, 25), Is.EqualTo(2));
        Assert.That(t.GetRank(r, 500), Is.EqualTo(3));
        Assert.That(t.GetRank(r, -1), Is.EqualTo(-1));
        Assert.That(rr.GetScore(2), Is.EqualTo(40));
        Assert.That(rr.GetScore(3), Is.EqualTo(0));
        Assert.That(t.GetRank(t.GetRoom(2), 10), Is.EqualTo(-1), "no table, no rank");
        Assert.That(t.GetMaxScore(t.GetRoom(2)), Is.EqualTo(0));
        Assert.That(t.GetRankRequire(t.GetRoom(2)), Is.Null);
    }

    [Test]
    public void ARankRewardOutsideRanks1To5IsDropped()
    {
        var t = Make(new()
        {
            ["DungeonRankReward.xml"] = "<DungeonRankReward rewardID='1'><v rank='1' itemID='11' systemMailID='21'/><v rank='5' itemID='15'/></DungeonRankReward>" +
                                        "<DungeonRankReward rewardID='2'><v rank='0' itemID='1'/></DungeonRankReward><DungeonRankReward rewardID='3'><v rank='6' itemID='1'/></DungeonRankReward>",
        });
        var r = t.GetRankReward(1);
        Assert.That(r.MaxRank, Is.EqualTo(5));
        Assert.That(r.GetItemId(1), Is.EqualTo(11u));
        Assert.That(r.GetSystemMailId(1), Is.EqualTo(21));
        Assert.That(r.GetItemId(5), Is.EqualTo(15u));
        Assert.That(r.GetItemId(2), Is.EqualTo(0u));
        Assert.That(r.GetItemId(6), Is.EqualTo(0u));
        Assert.That(t.GetRankReward(2), Is.Null);
        Assert.That(t.GetRankReward(3), Is.Null);
    }

    [Test]
    public void SeasonsAreFoundByPeriodAndIdAndRewardsByScore()
    {
        var t = Make(new()
        {
            ["DungeonSeason.xml"] =
                "<DungeonSeason seasonID='1' maxSeasonScore='500' startTime='2020-01-01-00-00-00' endTime='2020-02-01-00-00-00'>" +
                "<Reward itemID='9' grade='1' count='2' requireScore='300'/><Reward itemID='8' grade='1' count='1' requireScore='200'/><Reward itemID='7' grade='1' count='1' requireScore='100'/></DungeonSeason>" +
                "<DungeonSeason seasonID='2' startTime='2020-02-01-00-00-00' endTime='2020-03-01-00-00-00'/>" +
                "<DungeonSeason seasonID='3' startTime='2020-03-01-00-00-00' endTime='2020-04-01-00-00-00'><Reward itemID='1' requireScore='100'/><Reward itemID='2' requireScore='200'/><Reward itemID='3' requireScore='300'/></DungeonSeason>",
        });
        Assert.That(t.Seasons.Select(s => s.SeasonId), Is.EqualTo(new short[] { 1, 2 }), "a season whose rewards are not falling is skipped");
        long jan15 = Ms2.Formats.World.SeasonDataTable.Time("2020-01-15-00-00-00"), feb1 = Ms2.Formats.World.SeasonDataTable.Time("2020-02-01-00-00-00");
        Assert.That(t.GetSeasonId(jan15, 0), Is.EqualTo((short)1));
        Assert.That(t.GetSeasonId(feb1, 0), Is.EqualTo((short)2), "the end is exclusive");
        Assert.That(t.GetSeasonId(feb1 + 100 * 86400, 0), Is.EqualTo((short)0));
        Assert.That(t.GetCurrentSeason(jan15 + 3600, 3600).SeasonId, Is.EqualTo((short)1), "the zone offset moves the period");
        Assert.That(t.GetCurrentSeason(feb1 + 3600, 3600).SeasonId, Is.EqualTo((short)2));
        var s1 = t.GetSeason(1);
        Assert.That(s1.GetReward(250).ItemId, Is.EqualTo(8u), "the first reward in order the score reaches");
        Assert.That(s1.GetReward(300).ItemId, Is.EqualTo(9u));
        Assert.That(s1.GetReward(50), Is.Null);
        Assert.That(t.GetSeason(9), Is.Null);
    }

    [Test]
    public void TheReverseRaidRoomRotatesByThePeriodDays()
    {
        var t = Make(new()
        {
            ["DungeonConfig.xml"] = "<ReverseRaidConfig startDate='2020-01-01-00-00-00' periodDays='7'><v dungeonID='1003'/><v dungeonID='0'/><v dungeonID='1004'/><v dungeonID='1007'/></ReverseRaidConfig>",
        });
        long s = SeasonDataTable.Time("2020-01-01-00-00-00");
        Assert.That(t.GetReverseRaidRoomId(s - 1, 0), Is.EqualTo(0u), "before the start");
        Assert.That(t.GetReverseRaidRoomId(s, 0), Is.EqualTo(1003u));
        Assert.That(t.GetReverseRaidRoomId(s + 6 * 86400 + 86399, 0), Is.EqualTo(1003u));
        Assert.That(t.GetReverseRaidRoomId(s + 7 * 86400, 0), Is.EqualTo(1004u));
        Assert.That(t.GetReverseRaidRoomId(s + 14 * 86400, 0), Is.EqualTo(1007u));
        Assert.That(t.GetReverseRaidRoomId(s + 21 * 86400, 0), Is.EqualTo(1003u), "it wraps");
        Assert.That(Make(new()).GetReverseRaidRoomId(s, 0), Is.EqualTo(0u));
    }

    [Test]
    public void TheUnitedWeeklyRewardsKeepEveryRow()
    {
        var t = Make(new()
        {
            ["DungeonConfig.xml"] = "<UnitedWeeklyReward><v rewardCount='3' rewardID='30' uiPosX='5' uiShowBg='true'/><v rewardCount='1' rewardID='10'/><v rewardCount='3' rewardID='31'/></UnitedWeeklyReward>",
        });
        Assert.That(t.UnitedWeeklyRewardCount, Is.EqualTo(3));
        Assert.That(t.GetUnitedWeeklyReward(0).UiShowBg);
        Assert.That(t.GetUnitedWeeklyReward(0).UiPosX, Is.EqualTo(5));
        Assert.That(t.GetUnitedWeeklyReward(3), Is.Null);
        Assert.That(t.FindUnitedWeeklyReward(3).RewardId, Is.EqualTo(30u), "the first of a count");
        Assert.That(t.FindUnitedWeeklyReward(2), Is.Null);
    }

    [Test]
    public void TheSeasonRankRoomNeedsGroup3AndTheMinimumLevel()
    {
        var t = Rooms(Room(1, 3, level: 60) + Room(2, 3, level: 59) + Room(3, 1, level: 80));
        Assert.That(t.IsSeasonRankRoom(t.GetRoom(1)));
        Assert.That(t.IsSeasonRankRoom(t.GetRoom(2)), Is.False);
        Assert.That(t.IsSeasonRankRoom(t.GetRoom(3)), Is.False);
        var t2 = Rooms(Room(2, 3, level: 59), ("DungeonSeasonRankMinLevel", "50"));
        Assert.That(t2.IsSeasonRankRoom(t2.GetRoom(2)));
    }

    [Test]
    public void SoloPlayIsDisabledOnHighLevelNormalRoomsByTheConstant()
    {
        string rows = Room(1, 1, level: 50) + Room(2, 1, level: 49) + Room(3, 2, level: 90);
        Assert.That(Rooms(rows).GetRoom(1).IsPartyOnly, Is.False);
        var t = Rooms(rows, ("DisableSoloPlayHighLevelDungeon", "1"));
        Assert.That(t.GetRoom(1).IsPartyOnly);
        Assert.That(t.GetRoom(2).IsPartyOnly, Is.False);
        Assert.That(t.GetRoom(3).IsPartyOnly, Is.False);
    }

    [Test]
    public void TheLevelRangeIsFiveLevelsOrAtOrAbove50UpToTheDungeonsLevel()
    {
        Assert.That(DungeonRoomData.IsDungeonLevelInRange(55, 60));
        Assert.That(DungeonRoomData.IsDungeonLevelInRange(65, 60));
        Assert.That(DungeonRoomData.IsDungeonLevelInRange(54, 60), Is.True, "both 50 or more and the level is not above");
        Assert.That(DungeonRoomData.IsDungeonLevelInRange(66, 60), Is.False);
        Assert.That(DungeonRoomData.IsDungeonLevelInRange(30, 40), Is.False);
        Assert.That(DungeonRoomData.IsDungeonLevelInRange(35, 40));
    }

    [Test]
    public void TheAttributeReadersFollowTinyXml()
    {
        var t = Rooms("<dungeonRoom dungeonRoomID='1' limitEnter='maybe' limitVIP='' isPartyOnly='NO' rewardCount='12abc' rewardMeso='xyz' dungeonLevel='-1' isTimeUp='7'/>");
        var r = t.GetRoom(1);
        Assert.That(r.LimitEnter, Is.False, "an unreadable bool keeps the default");
        Assert.That(r.LimitVip, Is.False);
        Assert.That(r.RewardCount, Is.EqualTo(12u), "sscanf takes the leading number");
        Assert.That(r.RewardMeso, Is.EqualTo(0L));
        Assert.That(r.DungeonLevel, Is.EqualTo(65535));
        Assert.That(r.IsTimeUp, Is.EqualTo(7));
    }

    [Test]
    public void AMissingFileOrWrongRootLoadsNothing()
    {
        var t = new DungeonRoomTable(f => f == "DungeonRoom.xml" ? XElement.Parse("<other><dungeonRoom dungeonRoomID='1'/></other>") : null, null, null, null);
        Assert.That(t.RoomCount, Is.EqualTo(0));
        Assert.That(t.GetRoom(1), Is.Null);
    }

    [Test]
    public void TheDataHasTheTables()
    {
        var data = System.Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(data);
        var t = new DungeonRoomTable(packs, new ClientConstants(System.Array.Empty<KeyValuePair<string, string>>()));
        Assert.That(t.RoomCount, Is.GreaterThan(100));
        Assert.That(t.RoomList.All(r => r.Id != 0));
        Assert.That(t.RoomList.Select(r => r.Id), Is.Ordered);
        Assert.That(t.GetGroupRooms(1), Is.Ordered.Using(DungeonRoomData.Order));
        // the GMS2 table is of an older layout without groupType (every room is in no group); the groups are asserted where a room has one
        bool grouped = t.RoomList.Any(r => r.GroupType != 0);
        Assert.That(t.GetGroupRooms(1).Count > 0, Is.EqualTo(t.RoomList.Any(r => r.GroupType == 1)));
        if (grouped) Assert.That(t.GetMatchRooms(200, uint.MaxValue), Is.Not.Empty);
        Assert.That(t.RoomList.Where(r => r.GroupType != 0).Count(), Is.EqualTo(Enumerable.Range(0, 20).Sum(g => t.GetGroupRooms(g).Count)));
    }
}
