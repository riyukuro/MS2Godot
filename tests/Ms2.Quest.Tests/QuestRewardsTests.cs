using System.Xml.Linq;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

public class QuestRewardsTests
{
    static bool All(XElement _) => true;

    // synthetic tables: exp table 1 (levels 1 .. 3), the epicQuest type at 2.5 times it; meso table 2 by level, 7 without one;
    // rewards 1003 (table 2) and 1004 (table 7, factor 1.5); guild npc level 2 pays 1.5 times; quest type 4 meso turned off
    static QuestRewards Tables()
    {
        var r = new QuestRewards();
        r.LoadNextExp(XDocument.Parse(@"<ms2><exp level=""1"" value=""100"" /><exp level=""2"" value=""300"" /></ms2>"), All);
        r.ReadExpTable(XDocument.Parse(@"<ms2><table expTableID=""1""><base level=""1"" exp=""4"" /><base level=""2"" exp=""9"" />
            <base level=""3"" exp=""16"" /></table><table expTableID=""1""><base level=""1"" exp=""999"" /></table></ms2>"), All);
        r.LoadCommonExp(XDocument.Parse(@"<ms2><exp expType=""epicQuest"" expTableID=""1"" factor=""2.5"" />
            <exp expType=""quest"" expTableID=""1"" factor=""0.01"" /></ms2>"), All);
        Assert.That(r.ReadMesoTable(XDocument.Parse(@"<ms2><table mesoTableID=""2""><v level=""1"" meso=""100"" /><v level=""2"" meso=""250"" /></table>
            <table mesoTableID=""7""><v meso=""1000"" /></table></ms2>"), All), Is.True);
        r.ReadContents(XDocument.Parse(@"<ms2><v rewardID=""1003"" mesoTableID=""2"" mesoFactor=""1"" />
            <v rewardID=""1004"" mesoTableID=""7"" mesoFactor=""1.5"" /></ms2>"), All);
        r.LoadGuildQuestRewardFactor(XDocument.Parse(@"<ms2><guildNpc level=""1"" guildExpFactor=""1"" userExpFactor=""1"" guildFundFactor=""1"" userMesoFactor=""1"" GuildCoinFactor=""1"" />
            <guildNpc level=""2"" guildExpFactor=""1.2"" userExpFactor=""1.2"" guildFundFactor=""1.5"" userMesoFactor=""1.5"" GuildCoinFactor=""2"" /></ms2>"), All);
        r.LoadMultiControl(XDocument.Parse(@"<ms2><multi type=""questRewardMeso"" key=""4"" value=""0"" />
            <multi type=""questRewardMeso"" key=""4"" value=""3"" /></ms2>"));
        return r;
    }

    static QuestData Quest(string basic, string reward)
    {
        var t = new QuestTable("KR", _ => true);
        t.AddQuests(XDocument.Parse($@"<ms2><quest><basic questID=""1"" {basic} /><completeReward {reward} /></quest></ms2>"));
        return t.Quest(1);
    }

    [Test]
    public void TheLuaFunctionsAsTheClientChunkAnswers()
    {
        // run with the client's own precompiled chunk: alliances up to 5 and the others, ranks S .. D and none
        Assert.That(QuestRewards.LuaFamePoint(0, 3, 2, 0), Is.EqualTo(4000));
        Assert.That(QuestRewards.LuaFamePoint(3, 3, 1, 5), Is.EqualTo(800));
        Assert.That(QuestRewards.LuaFamePoint(4, 3, 2, 6), Is.EqualTo(900));
        Assert.That(QuestRewards.LuaFamePoint(0, 3, 2, 9), Is.EqualTo(2300));
        Assert.That(QuestRewards.LuaFamePoint(5, 3, 2, 0), Is.EqualTo(0));
        Assert.That(QuestRewards.LuaExpFactorRateByRank(8, 2, 1, 6), Is.EqualTo(0.32f));
        Assert.That(QuestRewards.LuaExpFactorRateByRank(9, 0, 1, 0), Is.EqualTo(5f));
        Assert.That(QuestRewards.LuaExpFactorRateByRank(9, 1, 1, 0), Is.EqualTo(4f));
        Assert.That(QuestRewards.LuaExpFactorRateByRank(9, 0, 1, 6), Is.EqualTo(1.4f));
        Assert.That(QuestRewards.LuaExpFactorRateByRank(0, 0, 1, 0), Is.EqualTo(0f));   // the Lua call fails
    }

    [Test]
    public void TheEnumNames()
    {
        Assert.That(QuestRewards.ParseRank("b"), Is.EqualTo(2));
        Assert.That(QuestRewards.ParseRank("E"), Is.EqualTo(5));
        Assert.That(QuestRewards.ParseAlliance("GreenHood_KritiasExped"), Is.EqualTo(8));
        Assert.That(QuestRewards.ParseExpType("epicQuest"), Is.EqualTo(0x2c));
        Assert.That(QuestRewards.ParseExpType("EpicQuest"), Is.EqualTo(-1));          // case sensitive
        Assert.That(QuestRewards.ParseExpType("Exp_Quest"), Is.EqualTo(16));
        Assert.That(QuestRewards.ParseRewardContents("guildquest"), Is.EqualTo(0x3eb));
        var d = Quest(@"questType=""9"" rank=""A"" alliance=""darkwind""", @"relativeExp=""epicQuest"" relativeMeso=""GuildWeeklyQuest"" exp=""5""");
        Assert.That((d.Type, d.Rank, d.Alliance, d.Complete.RelativeExp, d.Complete.RelativeMeso, d.Complete.Exp),
            Is.EqualTo((9, 1, 1, 0x2c, 0x3ec, 5)));
        Assert.That(Quest("", "").Rank, Is.EqualTo(5));
    }

    [Test]
    public void ExpIsTheLargerOfTheFixedAndTheTypeExpScaledByTheRankRate()
    {
        var r = Tables();
        var c = new QuestRewards.Context(2, 0);
        Assert.That(r.GetTypeValue(2, 0x2c), Is.EqualTo(23));                       // 9 * 2.5 + 0.5
        Assert.That(r.GetTypeValue(2, 16), Is.EqualTo(1));                          // at least 1
        Assert.That(r.GetTypeValue(4, 0x2c), Is.EqualTo(1));                        // an unset level: 0 scaled, at least 1
        Assert.That(r.GetTypeValue(100, 0x2c), Is.EqualTo(0));                      // past the 99 levels
        Assert.That(r.GetTypeValue(2, 8), Is.EqualTo(0));
        Assert.That(r.Exp(Quest("", @"relativeExp=""epicQuest"" exp=""5"""), c), Is.EqualTo(23));
        Assert.That(r.Exp(Quest("", @"relativeExp=""epicQuest"" exp=""50"""), c), Is.EqualTo(50));
        // a type 9 rank A quest of an old alliance: 4 times; of another: 1.3f times, truncated
        Assert.That(r.Exp(Quest(@"questType=""9"" rank=""A""", @"exp=""50"""), c), Is.EqualTo(200));
        Assert.That(r.Exp(Quest(@"questType=""9"" rank=""A"" alliance=""georg""", @"exp=""50"""), c), Is.EqualTo(64));   // 50 * (double)1.3f = 64.99999..
        Assert.That(r.Exp(Quest(@"questType=""1"" rank=""A""", @"exp=""50"""), c), Is.EqualTo(50));   // the Lua has no rate
        Assert.That(r.GetNextExp(2), Is.EqualTo(300));
        Assert.That(r.GetNextExp(3), Is.EqualTo(long.MaxValue));
    }

    [Test]
    public void MesoFromTheRewardContentsWithTheGuildBonusAndTheMultiControl()
    {
        var r = Tables();
        Assert.That(r.Meso(Quest("", @"money=""77"""), new(2, 0)), Is.EqualTo(77));
        Assert.That(r.Meso(Quest(@"questType=""4""", @"money=""77"""), new(2, 0)), Is.EqualTo(0));     // the first factor, 0
        var guild = Quest(@"questType=""6""", @"relativeMeso=""GuildQuest"" money=""77"" guildFund=""10"" guildCoin=""3""");
        Assert.That(r.Meso(guild, new(2, 0)), Is.EqualTo(250));                      // no guild: factor 0, no bonus
        Assert.That(r.Meso(guild, new(2, 2)), Is.EqualTo(375));                      // npc level 2: 1.5 times
        Assert.That(r.Meso(guild, new(1, 2)), Is.EqualTo(150));
        Assert.That(r.GuildFund(guild, new(2, 2)), Is.EqualTo(15));
        Assert.That(r.GuildCoin(guild, new(2, 2)), Is.EqualTo(6));
        Assert.That(r.Meso(Quest(@"questType=""1""", @"relativeMeso=""GuildWeeklyQuest"""), new(50, 2)), Is.EqualTo(1500)); // level-less table
    }

    [Test]
    public void FamePointsOnlyForMainFameQuests()
    {
        Assert.That(new QuestRewards().FamePoint(Quest(@"rank=""S"" alliance=""greenhood""", @"useMainFamePoint=""2""")), Is.EqualTo(4000));
        Assert.That(new QuestRewards().FamePoint(Quest(@"rank=""S""", "")), Is.EqualTo(0));
    }
}
