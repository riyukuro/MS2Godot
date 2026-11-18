using System.Xml.Linq;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

public class QuestTextsTests
{
    sealed class State : IQuestState
    {
        public readonly Dictionary<int, QuestRecord> ById = new();
        public IReadOnlyList<QuestRecord> Records => ById.Values.ToList();
        public QuestRecord Get(int questId) => ById.TryGetValue(questId, out var r) ? r : null;
        public bool CanStart(int questId) => false;
        public int PlayerLevel => 1;
        public long CharacterId => 0;
        public bool NpcFieldOpen(int npcId, int fieldId) => false;
    }

    static readonly Dictionary<string, string> CommonStrings = new()
    {
        ["s_quest_replace_npc"] = "N[{0}]",
        ["s_quest_replace_talk"] = "T[{0}$x, y$]",
        ["s_quest_replace_field"] = "F[{0}$x, y$]",
        ["s_quest_replace_item"] = "I[{0}]",
        ["s_quest_replace_item_move"] = "M[{0}$x, y$ {1}$p, q$]",
        ["s_quest_replace_satisfied"] = "S[{0}]",
        ["s_quest_desc_with_count"] = "{0} ({1}/{2})",
    };

    // quest 1: one condition of each formatted type and a manual one; quest 2 has a manual whose count is 0
    static QuestTable Table()
    {
        var t = new QuestTable("KR", _ => true) { Common = k => CommonStrings.TryGetValue(k, out var v) ? v : "" };
        t.AddQuests(XDocument.Parse(@"<ms2>
            <quest id=""1""><basic /><complete npc=""7"" />
              <condition type=""npc"" code=""7"" value=""1"" />
              <condition type=""Dialogue"" code=""7, 8"" value=""1"" />
              <condition type=""interact_object"" code=""5"" value=""2"" />
              <condition type=""map"" code=""3"" value=""1"" />
              <condition type=""item_exist"" code=""40-49"" value=""10"" />
              <condition type=""item_move"" code=""40"" target=""9"" value=""4"" />
              <condition type=""no_such_type"" code=""1"" />
              <condition type=""npc"" code=""0"" value=""3"" />
              <condition type=""npc"" code=""7"" value=""5"" />
            </quest>
            <quest id=""2""><basic /><condition type=""npc"" code=""7"" value=""5"" /></quest></ms2>"));
        t.BuildIndexes();
        t.Words.AddTable(ReservedWords.NpcName, XDocument.Parse(@"<ms2><key id=""7"" name=""Npc&#xAC01;"" /></ms2>"), null);
        t.Words.AddTable(ReservedWords.NpcTitle, XDocument.Parse(@"<ms2><key id=""7"" name=""Ttl"" /></ms2>"), null);
        t.Words.AddTable(ReservedWords.MapName, XDocument.Parse(@"<ms2><key id=""3"" name=""Map&#xAC00;"" /></ms2>"), null);
        t.Words.AddTable(ReservedWords.ItemName, XDocument.Parse(@"<ms2><key id=""40"" name=""Item&#xAC00;"" /></ms2>"), null);
        t.Words.AddTable(ReservedWords.InteractObjectAction, XDocument.Parse(@"<ms2><key id=""5"" name=""Act"" /></ms2>"), null);
        t.Words.AddTable(ReservedWords.LiftableTargetBox, XDocument.Parse(@"<ms2><key id=""9"" name=""Box&#xAC01;"" /></ms2>"), null);
        t.AddDescriptions(XDocument.Parse(@"<ms2>
            <quest questID=""1"" name=""Q"" manual="" | | | | | | |Get $item:40$  "" count=""0|0|0|0|0|0|0|1"" />
            <quest questID=""2"" name=""R"" manual=""Plain"" count=""0"" complete=""Done\nnow"" /></ms2>"));
        return t;
    }

    [Test]
    public void ConditionsLoadAsTheClientParsesThem()
    {
        var d = Table().Quest(1);
        Assert.That(d.Conditions.Select(c => c.Type), Is.EqualTo(new[] { 12, 26, 29, 37, 3, 156, 12, 12 }));   // the unknown type dropped
        Assert.That((d.Conditions[1].Code.IdCount, d.Conditions[1].Code.FirstValue), Is.EqualTo((2, 7L)));
        Assert.That((d.Conditions[4].Code.IdCount, d.Conditions[4].Code.FirstValue), Is.EqualTo((0, 40L)));     // a range: no ids
        Assert.That(d.Conditions[6].Code.IdCount, Is.EqualTo(0));                                               // code "0" not parsed
        var p = ConditionParam.Parse("3:9, 1-2, 5");
        Assert.That((p.Min, p.Max, p.Ranges.Count, p.Ids.Single(), p.Flags), Is.EqualTo((3L, 9L, 1, 5L, 7)));
        Assert.That(ConditionParam.Parse("dict:{a:1}").Flags, Is.EqualTo(8));
        Assert.That(d.CompleteNpc, Is.EqualTo(7));
    }

    [Test]
    public void TheJosaPicksByTheFinalConsonant()
    {
        Assert.That(QuestTable.Josa("a$x, y$b", "\uac00"), Is.EqualTo("axb"));
        Assert.That(QuestTable.Josa("a$x, y$b", "\uac01"), Is.EqualTo("ayb"));
        Assert.That(QuestTable.Josa("a$x, y$b", "z"), Is.EqualTo("axb"));       // not Hangul: type 2, the first
        Assert.That(QuestTable.Josa("plain", ""), Is.EqualTo("plain"));
        Assert.That(QuestTable.Josa("a$x, y", "\uac00"), Is.EqualTo(""));
        Assert.That(QuestTable.Josa("a$x$", "\uac00"), Is.EqualTo(""));
        Assert.That(QuestTable.Josa("a$x, y$", ""), Is.EqualTo(""));
    }

    [Test]
    public void TheFormattersWithoutALocalPlayer()
    {
        var t = Table();
        Assert.That(Enumerable.Range(0, 8).Select(i => t.ConditionText(1, i, null)), Is.EqualTo(new[]
        {
            "N[Ttl Npc\uac01]", "T[Ttl Npc\uac01y]", "Act", "F[Map\uac00x]", "", "M[Item\uac00x Box\uac01q]", "", "Get Item\uac00",
        }));
        Assert.That(t.ConditionText(1, 8, null), Is.EqualTo(""));
        Assert.That(t.ConditionText(3, 0, null), Is.EqualTo(""));
        Assert.That(t.CompleteText(1), Is.EqualTo("S[Ttl Npc\uac01]"));
        Assert.That(t.CompleteText(2), Is.EqualTo("Done\nnow"));
    }

    [Test]
    public void TheCountSuffixWithTheProgress()
    {
        var t = Table();
        var s = new State();
        // no record: 0 of the goal; the map condition and a manual with count 0 have none
        Assert.That(t.ConditionText(1, 0, s), Is.EqualTo("N[Ttl Npc\uac01] (0/1)"));
        Assert.That(t.ConditionText(1, 3, s), Is.EqualTo("F[Map\uac00x]"));
        Assert.That(t.ConditionText(2, 0, s), Is.EqualTo("Plain"));
        Assert.That(t.ConditionText(1, 7, s), Is.EqualTo("Get Item\uac00 (0/5)"));
        s.ById[1] = new QuestRecord(1, 1, 0, 0, 0, false, false, new long[] { 0, 0, 7, 0, 99, -3 });
        Assert.That(t.ConditionText(1, 2, s), Is.EqualTo("Act (2/2)"));          // clamped to the goal
        Assert.That(t.ConditionText(1, 4, s), Is.EqualTo(" (10/10)"));
        Assert.That(t.ConditionText(1, 5, s), Is.EqualTo("M[Item\uac00x Box\uac01q] (0/4)"));
        Assert.That(t.ConditionText(1, 7, s), Is.EqualTo("Get Item\uac00 (0/5)"));   // past the progress list
        s.ById[1] = new QuestRecord(1, 2, 0, 0, 0, true, false);
        Assert.That(t.ConditionText(1, 7, s), Is.EqualTo("Get Item\uac00 (5/5)"));   // rewarded: the goal
    }
}
