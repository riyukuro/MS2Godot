using System.Xml.Linq;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

public class FieldMissionsTests
{
    // synthetic data: two field missions on field 100, one on field 200, one quest of another type on 100
    static FieldMissions Sample(Func<string, bool> featureOn = null)
    {
        var m = new FieldMissions("KR", featureOn ?? (_ => true));
        m.AddQuests(XDocument.Parse(@"<ms2><environment><quest>
            <basic questID=""9002"" questType=""4"" /><progressMap progressMap=""100,300"" />
            <condition type=""run"" value=""1"" /><condition type=""swim"" value=""5"" /></quest></environment></ms2>"));
        m.AddQuests(XDocument.Parse(@"<ms2><environment locale=""CN""><quest>
            <basic questID=""9001"" questType=""4"" /><progressMap progressMap=""999"" /><condition type=""run"" value=""9"" /></quest></environment>
            <environment><quest>
            <basic questID=""9001"" questType=""4"" /><progressMap progressMap=""100"" /><condition type=""fish"" value=""3"" /></quest></environment></ms2>"));
        m.AddQuests(XDocument.Parse(@"<ms2><environment feature=""Gated""><quest>
            <basic questID=""9003"" questType=""4"" /><progressMap progressMap=""200"" /><condition type=""run"" value=""2"" /></quest></environment></ms2>"));
        m.AddQuests(XDocument.Parse(@"<ms2><environment><quest>
            <basic questID=""9004"" questType=""1"" /><progressMap progressMap=""100"" /><condition type=""run"" value=""1"" /></quest></environment></ms2>"));
        m.BuildIndex();
        m.AddDescriptions(XDocument.Parse(@"<ms2>
            <quest questID=""9001"" manual=""Old text"" count=""1"" />
            <quest questID=""9001"" manual=""Catch a $npcName:500$ in $map:100$&#160;"" count=""1"" />
            <quest questID=""9001"" manual=""Other locale"" count=""1"" locale=""NA"" />
            <quest questID=""9002"" manual=""Walk to $npc:501$|Swim||"" count=""1|0"" />
            <quest questID=""9003"" manual=""Gated goal"" count=""1"" /></ms2>"));
        var avail = (Func<XElement, bool>)m.Available;
        m.Words.AddTable(ReservedWords.MapName, XDocument.Parse(@"<ms2><key id=""00000100"" name=""Town"" /></ms2>"), avail);
        m.Words.AddTable(ReservedWords.NpcName, XDocument.Parse(@"<ms2><key id=""500"" name=""Fish"" /><key id=""501"" name=""Bob"" /></ms2>"), avail);
        m.Words.AddTable(ReservedWords.NpcTitle, XDocument.Parse(@"<ms2><key id=""500"" /><key id=""501"" name=""Guard"" /></ms2>"), avail);
        m.CountFormat = "{0} ({1}/{2})";
        return m;
    }

    [Test]
    public void FieldMissionsByFirstProgressMapInIdOrder()
    {
        var m = Sample();
        Assert.That(m.QuestIds(100), Is.EqualTo(new[] { 9001, 9002 }));    // id order; 9004 is not a field mission
        Assert.That(m.QuestIds(300), Is.Empty);                            // only the first progressMap id indexes
        Assert.That(m.QuestIds(999), Is.Empty);                            // the CN environment is skipped
        Assert.That(m.QuestIds(200), Is.EqualTo(new[] { 9003 }));
        Assert.That(Sample(f => f != "Gated").QuestIds(200), Is.Empty);    // a feature that is off drops the quest
    }

    [Test]
    public void ConditionTextsWithCounts()
    {
        var goals = Sample().Goals(100);
        Assert.That(goals.Select(g => g.Text), Is.EqualTo(new[]
        {
            "Catch a Fish in Town (0/3)",        // last kept row, words replaced, trailing space trimmed, goal from value
            "Walk to Guard Bob (0/1)",           // $npc: title and name
            "Swim",                                   // count 0: no suffix; empty manual parts dropped
        }));
        Assert.That(goals.All(g => !g.Satisfied));
        Assert.That(Sample().Goals(300), Is.Empty);
    }

    [Test]
    public void ParentFieldGoals()
    {
        var goals = Sample().Goals(55, f => f == 55 ? 100 : null);
        Assert.That(goals, Has.Count.EqualTo(3));
    }

    [Test]
    public void UnknownWordsStayAndParticlesFollowTheWord()
    {
        var w = new ReservedWords();
        w.AddTable(ReservedWords.NpcName, XDocument.Parse("<ms2><key id=\"1\" name=\"\uac00\" /><key id=\"2\" name=\"\uac01\" /></ms2>"), null);
        Assert.That(w.ReplaceObjectWords("a $nope:1$ b"), Is.EqualTo("a $nope:1$ b"));
        Assert.That(w.ReplaceObjectWords("$npcName:1$$pp:\uc640,\uacfc$ x"), Is.EqualTo("\uac00\uc640 x"));   // no final consonant
        Assert.That(w.ReplaceObjectWords("$npcName:2$$pp:\uc640,\uacfc$ x"), Is.EqualTo("\uac01\uacfc x"));   // final consonant
        Assert.That(ReservedWords.FinalConsonantType('3'), Is.EqualTo(1));
        Assert.That(ReservedWords.FinalConsonantType('A'), Is.EqualTo(2));
    }

    [Test]
    public void ParentFieldsFollowIndoorPortals()
    {
        var meta = FieldMeta.Parse(@"<ms2>
            <fieldPortal field=""1"" indoor=""0""><portal targetField=""10"" portalType=""0"" portalIndoor=""1"" /><portal targetField=""2"" portalType=""0"" portalIndoor=""0"" /></fieldPortal>
            <fieldPortal field=""10"" indoor=""1""><portal targetField=""11"" portalType=""0"" portalIndoor=""1"" /><portal targetField=""1"" portalType=""0"" portalIndoor=""0"" /></fieldPortal>
            <fieldPortal field=""2"" indoor=""0""><portal targetField=""1"" portalType=""0"" portalIndoor=""0"" /></fieldPortal></ms2>");
        Assert.That(meta.ParentFields[10], Is.EqualTo(1));
        Assert.That(meta.ParentFields[11], Is.EqualTo(1));                  // on through the indoor field's own portal
        Assert.That(meta.ParentFields.ContainsKey(2), Is.False);
        Assert.That(meta.ParentFields.ContainsKey(1), Is.False);
    }

    [Test]
    public void AtlMapOrderIsBinThenHeadFirst()
    {
        Assert.That(FieldMeta.AtlMapOrder(new[] { 1, 18, 35, 2, 18 }), Is.EqualTo(new[] { 35, 18, 1, 2 }));
        // 39 keys pass 17 * 2.25: rehash to 53 bins (39 / 0.75 = 52)
        var keys = Enumerable.Range(0, 39).Select(i => i * 17).ToList();
        var order = FieldMeta.AtlMapOrder(keys);
        Assert.That(order, Is.EquivalentTo(keys));
        Assert.That(order.Select(k => k % 53).ToList(), Is.Ordered);
    }

    /// <summary>
    /// A field mission's condition without a value needs one count, as every quest condition (CConditionCommonData's m_nGoal(1),
    /// conditionutil.cpp:287): the panel shows "(0/1)" and the goal is met at a progress of 1, not on an empty record.
    /// </summary>
    [Test]
    public void AMissionConditionWithoutValueNeedsOneCount()
    {
        var m = new FieldMissions("KR", _ => true);
        m.AddQuests(XDocument.Parse(@"<ms2><environment><quest>
            <basic questID=""9101"" questType=""4"" /><progressMap progressMap=""400"" /><condition type=""run"" /><condition type=""swim"" value="""" /></quest></environment></ms2>"));
        m.BuildIndex();
        m.AddDescriptions(XDocument.Parse(@"<ms2><quest questID=""9101"" manual=""Run|Swim"" count=""1|1"" /></ms2>"));
        m.CountFormat = "{0} ({1}/{2})";
        Assert.That(m.ConditionText(9101, 0), Is.EqualTo("Run (0/1)"));
        Assert.That(m.ConditionText(9101, 1), Is.EqualTo("Swim (0/1)"));
        Assert.That(FieldMissions.Goal(null), Is.EqualTo(1));
        Assert.That(FieldMissions.Goal("0"), Is.EqualTo(0), "a written 0 stays 0");
        Assert.That(FieldMissions.Goal("-"), Is.EqualTo(1), "no digits: the read fails");
    }
}
