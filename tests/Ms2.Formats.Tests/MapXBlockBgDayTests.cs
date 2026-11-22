using System.Xml.Linq;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The backdrop a field's mapxblock names (CUIService::LoadBackgroundMovie, uiservice.cpp:504-530).</summary>
public class MapXBlockBgDayTests
{
    static XDocument Block(string attrs) => XDocument.Parse($"<ms2><clientProperty {attrs} /></ms2>");

    [Test]
    public void ABlockWithOneNameGivesIt() =>
        Assert.That(MapXBlock.BgDay(Block("bgDay=\"BG_login_2020_spring.swf\""), n => 0), Is.EqualTo("BG_login_2020_spring.swf"));

    [Test]
    public void ACommaListIsPickedFromByTheCallersIndex()
    {
        var b = Block("bgDay=\"BG_a.swf, BG_b.swf,BG_c.swf\"");
        int count = 0;
        Assert.That(MapXBlock.BgDay(b, n => { count = n; return 0; }), Is.EqualTo("BG_a.swf"));
        Assert.That(count, Is.EqualTo(3));
        Assert.That(MapXBlock.BgDay(b, n => 1), Is.EqualTo("BG_b.swf"));
        Assert.That(MapXBlock.BgDay(b, n => 9), Is.EqualTo("BG_c.swf"));
    }

    [Test]
    public void ABlockWithoutANameGivesNull()
    {
        Assert.That(MapXBlock.BgDay(Block("bgDay=\"\""), n => 0), Is.Null);
        Assert.That(MapXBlock.BgDay(XDocument.Parse("<ms2/>"), n => 0), Is.Null);
        Assert.That(MapXBlock.BgDay(null, n => 0), Is.Null);
    }
}
