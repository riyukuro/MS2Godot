using System.Linq;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

/// <summary>GMS2 keeps a file per quest (quest/&lt;id&gt;.xml) in place of questdata.xml, with its own reward elements and an account value of 2.
/// The data tests need MS2_GMS2_DATA.</summary>
public class Gms2QuestTests
{
    static QuestData One(string basic, string body)
    {
        var t = new QuestTable("NA", _ => true);
        t.AddQuests(XDocument.Parse($@"<ms2><quest id=""7""><basic {basic} />{body}</quest></ms2>"));
        return t.Quest(7);
    }

    [Test]
    public void AnAccountValueOfTwoIsAnAccountQuest()
    {
        Assert.That(One(@"account=""2""", "").Account, Is.True);
        Assert.That(One(@"account=""1""", "").Account, Is.True);
        Assert.That(One(@"account=""0""", "").Account, Is.False);
        Assert.That(One("", "").Account, Is.False);
    }

    [Test]
    public void TheEssentialRowsAreTheAcceptAndCompleteRewardAndAZeroCodeIsAnEmptySlot()
    {
        var q = One("", @"<acceptReward><essentialItem code=""20000001"" rank=""1"" count=""3""/><essentialJobItem code=""0"" rank=""0"" count=""0""/></acceptReward>
            <completeReward money=""5""><essentialItem code=""20000002"" rank=""2"" count=""1""/><essentialJobItem code=""10000003"" rank=""3"" count=""1""/></completeReward>");
        Assert.That(q.AcceptRewards, Is.EqualTo(new[] { new QuestRewardItem(20000001, 1, 3) }));
        Assert.That(q.Complete.ChoiceRewards, Is.EqualTo(new[] { new QuestRewardItem(20000002, 2, 1), new QuestRewardItem(10000003, 3, 1) }));
    }

    [Test]
    public void GlobalRowsWithACodeReplaceTheEssentialRowsNotAddToThem()
    {
        var q = One("", @"<completeReward><essentialItem code=""20000002"" rank=""1"" count=""5""/><globalEssentialItem code=""20000002"" rank=""1"" count=""3""/>
            <globalEssentialJobItem code=""0"" rank=""0"" count=""0""/></completeReward>");
        Assert.That(q.Complete.ChoiceRewards, Is.EqualTo(new[] { new QuestRewardItem(20000002, 1, 3) }));
        var onlyEmpty = One("", @"<completeReward><essentialItem code=""20000002"" rank=""1"" count=""5""/><globalEssentialItem code=""0"" rank=""0"" count=""0""/></completeReward>");
        Assert.That(onlyEmpty.Complete.ChoiceRewards, Is.EqualTo(new[] { new QuestRewardItem(20000002, 1, 5) }));
    }

    [Test]
    public void TheKmsRowsStillReadAsTheyDid()
    {
        var q = One("", @"<completeReward><v code=""20000002"" rank=""1"" count=""5""/><v code=""0"" rank=""0"" count=""0""/></completeReward>");
        Assert.That(q.Complete.ChoiceRewards, Has.Count.EqualTo(2));
    }

    [Test]
    public void TheGms2QuestFilesLoadAsQuests()
    {
        var data = System.Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore("MS2_GMS2_DATA is not set");
        var t = QuestTable.Load(new PackSet(data), new[] { "en" }, "NA", _ => true);
        var q = t.Quest(10001172);
        Assert.That(q, Is.Not.Null, "quest/10001172.xml is a quest");
        Assert.That(q.Id, Is.EqualTo(10001172));
        Assert.That(q.Complete.ChoiceRewards, Is.Not.Empty);
        Assert.That(q.Complete.ChoiceRewards.All(r => r.ItemId != 0), Is.True, "no empty slot");
        Assert.That(t.Quest(71000009), Is.Not.Null, "a quest whose locale holds its feature name");
        Assert.That(t.Quest(90000060)?.Account ?? false, Is.True);
    }

    [Test]
    public void EveryGms2QuestHasATypeTheLoginListCanCreate()
    {
        var data = System.Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore("MS2_GMS2_DATA is not set");
        var packs = new PackSet(data);
        var t = QuestTable.Load(packs, new[] { "en" }, "NA", _ => true);
        // the login quest list (OnQuest mode 0x16) skips a record whose quest has no data or a type outside 0-10, and misreads the rest;
        // the tutorial's 70000001 is a type 3 quest of the files
        Assert.That(t.Quest(70000001)?.Type, Is.EqualTo(3));
        // a file whose environments are all for another locale (locale="KR", "CN", ...) has no quest here, as in the client;
        // a file with an environment of no locale and no feature always has one
        var bad = packs.List("Xml").Where(n => n.StartsWith("quest/") && n.EndsWith(".xml"))
            .Where(n => System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", n))).Root.Elements("environment")
                .Any(e => string.IsNullOrEmpty((string)e.Attribute("locale")) && string.IsNullOrEmpty((string)e.Attribute("feature"))))
            .Select(n => int.TryParse(n[6..^4], out int id) ? id : 0).Where(id => id != 0 && t.Quest(id) is not { Type: >= 0 and <= 10 }).ToList();
        Assert.That(bad, Is.Empty, "quest files without a creatable quest: " + string.Join(",", bad.Take(5)));
    }
}
