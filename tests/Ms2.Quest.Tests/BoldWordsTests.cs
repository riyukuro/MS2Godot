using System.Xml.Linq;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

public class BoldWordsTests
{
    static ReservedWords Words()
    {
        var w = new ReservedWords { QuestName = id => "q" + id };
        w.AddTable(ReservedWords.FieldEnterance, XDocument.Parse("<ms2><key id='7' name='dungeon seven'/></ms2>"), null);
        w.AddTable(ReservedWords.PetCollectGroupName, XDocument.Parse("<ms2><key id='3' name='group three'/></ms2>"), null);
        w.AddTable(ReservedWords.NpcName, XDocument.Parse("<ms2><key id='5' name='npc five'/></ms2>"), null);
        return w;
    }

    [Test]
    public void LinkWordsWrapTheObjectText()
    {
        var w = Words();
        Assert.That(w.ReplaceBoldWords("a $dungeonTitle:7$ b"), Is.EqualTo("a <u><a href='event:dungeon,7'><FONT color='#5c2b00'>dungeon seven</FONT></a></u> b"));
        Assert.That(w.ReplaceBoldWords("$quest:9$"), Is.EqualTo("<u><a href='event:quest,9'><FONT color='#5c2b00'>q9</FONT></a></u>"));
        Assert.That(w.ReplaceBoldWords("$petCollectGroup:3$"), Is.EqualTo("<u><a href='event:petCollectGroup,3'><FONT color='#5c2b00'>group three</FONT></a></u>"));
    }

    [Test]
    public void ZeroIdsAndUnknownWords()
    {
        var w = Words();
        Assert.That(w.ReplaceBoldWords("$dungeonTitle:0$$quest:0$$map:0$$item:0$"), Is.Empty);
        Assert.That(w.ReplaceBoldWords("x $nothing:1$ y"), Is.EqualTo("x $nothing:1$ y"));
        Assert.That(w.ReplaceBoldWords("one $ dollar"), Is.EqualTo("one $ dollar"));
    }

    [Test]
    public void NpcsAreTextWithoutTheDictionaryFeature()
    {
        var w = Words();
        Assert.That(w.FeatureEnabled(ReservedWords.GameDictionaryFeature), Is.False);
        Assert.That(w.ReplaceBoldWords("$npcName:5$"), Is.EqualTo(w.ReplaceObjectWords("$npcName:5$")));
    }

    [Test]
    public void RealQuestDescriptionHasItsWordsReplaced()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new PackSet(data);
        var t = QuestTable.Load(packs, new[] { "en" }, "KR", _ => true);
        string desc = t.QuestDesc(40002710);
        TestContext.Out.WriteLine(desc);
        Assert.That(desc, Is.Not.Empty);
        Assert.That(desc, Does.Not.Contain("$npcName"));
        Assert.That(desc, Does.Not.Contain("$map:"));
        Assert.That(t.QuestDesc(1), Is.Empty, "no row");
    }
}
