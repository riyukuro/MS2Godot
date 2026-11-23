using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// GameStrings.Table reads a table's parts &lt;name&gt;_&lt;n&gt;.xml after &lt;name&gt;.xml: the GMS2 data keeps most skill
/// names in string/&lt;lang&gt;/skillname_&lt;job&gt;.xml, the KMS data keeps them all in skillname.xml.
/// </summary>
public class GameStringsPartsTests
{
    static HashSet<string> Ids(PackSet packs, string entry)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in XmlRows.Elements(packs.Read("Xml", entry), "key"))
            if (r.GetAttribute("id") is { } id) ids.Add(id);
        return ids;
    }

    [Test]
    public void TheKmsSkillNamesHaveNoPartsAndReadAsTheOneFile()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        Assume.That(TablePath.Region(e => packs.Exists("Xml", e)), Is.EqualTo("kr"), "MS2_DATA is KMS data");
        var strings = new GameStrings(packs, "en");
        foreach (var lang in strings.Languages)
            Assert.That(strings.Parts(lang, "skillname"), Is.Empty, $"string/{lang}/skillname_<n>.xml");
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lang in strings.Languages)
            if (packs.Exists("Xml", $"string/{lang}/skillname.xml")) expected.UnionWith(Ids(packs, $"string/{lang}/skillname.xml"));
        Assert.That(strings.Table("skillname", "name").Keys, Is.EquivalentTo(expected));
    }

    [Test]
    public void TheGms2SkillNamesReadTheirJobParts()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var strings = new GameStrings(packs, "en");
        var parts = strings.Parts("en", "skillname");
        Assert.That(parts, Is.Not.Empty);
        Assert.That(parts, Has.All.Match(@"^string/en/skillname_\d+\.xml$"));
        var numbers = parts.Select(p => int.Parse(p["string/en/skillname_".Length..^4])).ToList();
        Assert.That(numbers, Is.Ordered.Ascending, "the parts in number order");

        // 10900001: in skillname_90.xml, not in skillname.xml (stringGuide.xml 10012052 names it as $skill:10900001$)
        var inBase = Ids(packs, "string/en/skillname.xml");
        Assert.That(inBase, Does.Not.Contain("10900001"));
        var table = strings.Table("skillname", "name");
        Assert.That(table.TryGetValue("10900001", out var name) ? name : null, Is.Not.Null.And.Not.Empty);
        Assert.That(table.Keys, Is.SupersetOf(inBase));
        Assert.That(new ReservedWords(strings, null).ReplaceObjectWords("$skill:10900001$"), Is.EqualTo(name));
    }
}
