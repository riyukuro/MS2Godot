using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Items;
using NUnit.Framework;
using QuestRecord = Ms2.Quest.QuestRecord;

namespace Ms2.Ui.Tests;

/// <summary>CQuestManager::GetQuestItemLinks (<see cref="ItemTooltip.QuestItemLinks"/>) and the quest data table's item
/// key index (<see cref="QuestTable.QuestIdsByKey"/>) on a made-up table.</summary>
public class QuestItemLinksTests
{
    sealed class Ctx : IQuestContext
    {
        public readonly Dictionary<int, QuestRecord> Records = new();
        public QuestRecord Get(int questId) => Records.TryGetValue(questId, out var r) ? r : null;
        public long ServerTime => 0;
        public long ZoneOffset => 0;
        public long CharacterId => 1;
        public int? Selector => 0;
        public int? Level => 30;
        public int? GearScore => null;
        public int? JobGroup { get; set; } = 10;
        public int? SubJob => null;
        public long? CharacterTime => null;
        public bool? InGuild => null;
        public int? GuildLevel => null;
        public int? GuildNpcLevel => null;
        public long? GuildMemberTime => null;
        public bool? AchievementDone(uint id) => null;
        public bool? AchievementGradeCleared(uint id, int grade) => null;
        public int? FameGrade(int alliance) => null;
        public int? MasteryGrade(int type) => null;
        public int? DungeonRecord(uint dungeon) => null;
        public int? DungeonEnterState(uint dungeon) => null;
        public int? DungeonHelperReward(uint dungeon) => null;
        public int? MentoringRole => null;
        public bool? Vip => null;
        public bool? HasEquipment(uint[] items) => null;
        public readonly Dictionary<string, TermTag> Tags = new();
        public (bool Known, TermTag Tag) TermTag(string name) => Tags.TryGetValue(name, out var t) ? (true, t) : (false, null);
        public bool? HasStamp(int questId) => null;
        public int? FieldId => null;
        public int? ParentField(int field) => null;
        public int? DailyCount(int questId) => 0;
        public int? WeeklyCount(int questId) => 0;
    }

    const int ItemId = 20800001;

    static string Quest(int id, string condition, string basic = "", string require = "") =>
        $@"<quest id=""{id}""><basic {basic} /><require {require} /><condition {condition} value=""1"" /></quest>";

    static QuestTable Table()
    {
        var t = new QuestTable("", null);
        t.AddQuests(XDocument.Parse("<ms2>"
            + Quest(1, $@"type=""item_exist"" code=""{ItemId}""")                              // no record: can start
            + Quest(2, $@"type=""item_exist"" code=""7,{ItemId}""")                            // in progress
            + Quest(3, $@"type=""item_exist"" code=""{ItemId}""")                              // complete, not a repeat: left out
            + Quest(4, $@"type=""item_exist"" code=""{ItemId}""", @"repeatable=""1""")         // complete repeat: can start again
            + Quest(5, $@"type=""item_exist"" code=""{ItemId}""", @"locking=""1""")            // locked
            + Quest(6, $@"type=""item_exist"" code=""{ItemId}""", @"eventTag=""off""")         // a term tag that is not there
            + Quest(7, $@"type=""item_exist"" code=""{ItemId}""", @"eventTag=""on""")          // a term tag that is there
            + Quest(8, $@"type=""item_exist"" code=""{ItemId}""", "", @"job=""20,30""")       // another job
            + Quest(9, $@"type=""item_exist"" code=""{ItemId},{ItemId}""", "", @"job=""10""") // named twice: listed twice
            + Quest(10, $@"type=""equip_exist"" code=""{ItemId}""")                            // an equip quest: kind 1
            + Quest(11, $@"type=""item_pickup"" code=""{ItemId}""")                            // another condition type
            + "</ms2>"));
        t.BuildIndexes();
        return t;
    }

    [Test]
    public void IndexListsQuestsByItemAndKindInIdOrder()
    {
        var t = Table();
        Assert.That(t.QuestIdsByKey(ItemId, 0), Is.EqualTo(new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 9 }));
        Assert.That(t.QuestIdsByKey(ItemId, 1), Is.EqualTo(new uint[] { 10 }));
        Assert.That(t.QuestIdsByKey(7, 0), Is.EqualTo(new uint[] { 2 }));
        Assert.That(t.QuestIdsByKey(ItemId, 2), Is.Empty);
        Assert.That(t.QuestIdsByKey(1, 0), Is.Empty);
    }

    [Test]
    public void LinksFollowTheRecordsTagsJobAndItemFlag()
    {
        var t = Table();
        var c = new Ctx();
        c.Records[2] = new QuestRecord(2, 1, 0, 0, 0, false, false);
        c.Records[3] = new QuestRecord(3, 2, 0, 0, 0, true, false);
        c.Records[4] = new QuestRecord(4, 2, 0, 0, 0, true, false);
        c.Tags["on"] = new TermTag(0, long.MaxValue, false);
        var item = Item.Create(new ItemData { Id = ItemId }, 1);

        var links = ItemTooltip.QuestItemLinks(t, c, item);
        Assert.That(links, Is.EqualTo(new[]
        {
            new QuestItemLink(1, 0, 0), new QuestItemLink(2, 1, 0), new QuestItemLink(4, 0, 0), new QuestItemLink(7, 0, 0),
            new QuestItemLink(9, 0, 0), new QuestItemLink(9, 0, 0), new QuestItemLink(10, 0, 1),
        }));
        Assert.That(ItemTooltip.QuestItemState(links), Is.EqualTo(1));

        item.Record = new EquipItem { HeaderFlagD4 = true };            // the stream flag drops the equip quest only
        Assert.That(ItemTooltip.QuestItemLinks(t, c, item).Select(l => l.QuestId), Is.EqualTo(new[] { 1, 2, 4, 7, 9, 9 }));

        c.JobGroup = null;                                              // an unknown job fails a job list
        Assert.That(ItemTooltip.QuestItemLinks(t, c, item).Select(l => l.QuestId), Is.EqualTo(new[] { 1, 2, 4, 7 }));

        Assert.That(ItemTooltip.QuestItemLinks(t, c, Item.Create(new ItemData { Id = 1 }, 1)), Is.Empty);
        Assert.That(ItemTooltip.QuestItemLinks(null, c, item), Is.Empty);
    }
}
