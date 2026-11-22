using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>CGuildXmlData::LoadGuildEvent (@0x1404d5fa0, guilddef_2.cpp:519-529) and GuildEvent (FUN_1404d4f10, :389-396).</summary>
public class GuildEventTableTests
{
    static GuildEventTable Make(string rows, System.Func<XElement, bool> avail = null) =>
        new(XDocument.Parse("<ms2>" + rows + "</ms2>"), avail);

    [Test]
    public void RowsAreFiledByIdAndListedAscending()
    {
        var t = Make("<guildEvent id='3' mapCode='20' imagePath='c.png'/><guildEvent id='1' mapCode='10' imagePath='a.png'/>");
        Assert.That(t.Ids(), Is.EqualTo(new[] { 1, 3 }));
        Assert.That(t.Get(3), Is.EqualTo(new GuildEventTable.Event(3, 20, "c.png")));
        Assert.That(t.Get(2), Is.Null);
    }

    [Test]
    public void ALaterRowOfTheSameIdReplacesTheEarlierOne()
    {
        var t = Make("<guildEvent id='1' mapCode='10'/><guildEvent id='1' mapCode='11'/>");
        Assert.That(t.Count, Is.EqualTo(1));
        Assert.That(t.Get(1).MapCode, Is.EqualTo(11));
    }

    [Test]
    public void UnavailableRowsAndAWrongRootLoadNothing()
    {
        Assert.That(Make("<guildEvent id='1'/><guildEvent id='2'/>", n => (string)n.Attribute("id") != "1").Ids(), Is.EqualTo(new[] { 2 }));
        Assert.That(new GuildEventTable(XDocument.Parse("<root><guildEvent id='1'/></root>")).Count, Is.EqualTo(0));
    }

    [Test]
    public void TheDataHasTheTable()
    {
        var data = System.Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(data);
        var t = new GuildEventTable(packs);
        Assert.That(t.Count, Is.GreaterThan(0));
        Assert.That(t.Ids().All(id => t.Get(id).Id == id));
    }
}
