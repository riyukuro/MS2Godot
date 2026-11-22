using System.Linq;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>CGuildXmlData::LoadGuildHouse (@0x1404d6210, guilddef_2.cpp:532-543) with GuildHouse (FUN_1404d5070, :402-417).</summary>
public class GuildHouseTableTests
{
    static GuildHouseTable Make(string rows, System.Func<XElement, bool> avail = null) =>
        new(XDocument.Parse("<ms2>" + rows + "</ms2>"), avail);

    [Test]
    public void ARowReadsEveryAttributeAndTheFacilityList()
    {
        var t = Make("<guildHouse level='2' theme='3' fieldID='9' areaSize='40' facility='11,,12,13' stringKey='5' upgradeCost='700' upgradeReqGuildLevel='4' rethemeCost='60' imagePath='i.png' thumbnailPath='t.png'/>");
        var h = t.Get(2, 3);
        Assert.That(h, Is.Not.Null);
        Assert.That((h.Level, h.Theme, h.FieldId, h.AreaSize, h.StringKey), Is.EqualTo((2, 3, 9u, 40, 5)));
        Assert.That((h.UpgradeCost, h.UpgradeReqGuildLevel, h.RethemeCost), Is.EqualTo((700, 4, 60)));
        Assert.That(h.Facilities, Is.EqualTo(new[] { 11, 12, 13 }));
        Assert.That((h.ImagePath, h.ThumbnailPath), Is.EqualTo(("i.png", "t.png")));
    }

    [Test]
    public void TheKeyIsLevelAndThemeAndTheKeysListAscending()
    {
        var t = Make("<guildHouse level='2' theme='1'/><guildHouse level='1' theme='2'/><guildHouse level='1' theme='1'/>");
        Assert.That(t.Keys(), Is.EqualTo(new[] { (1, 1), (1, 2), (2, 1) }));
        Assert.That(t.Get(1, 2), Is.Not.Null);
        Assert.That(t.Get(2, 2), Is.Null);
    }

    [Test]
    public void ALaterRowOfTheSameKeyReplacesTheEarlierOne()
    {
        var t = Make("<guildHouse level='1' theme='1' areaSize='1'/><guildHouse level='1' theme='1' areaSize='2'/>");
        Assert.That(t.Count, Is.EqualTo(1));
        Assert.That(t.Get(1, 1).AreaSize, Is.EqualTo(2));
    }

    [Test]
    public void UnavailableRowsAndAWrongRootLoadNothing()
    {
        Assert.That(Make("<guildHouse level='1' theme='1'/><guildHouse level='2' theme='1'/>", n => (string)n.Attribute("level") != "1").Keys(), Is.EqualTo(new[] { (2, 1) }));
        Assert.That(new GuildHouseTable(XDocument.Parse("<root><guildHouse level='1' theme='1'/></root>")).Count, Is.EqualTo(0));
    }

    [Test]
    public void TheDataHasTheTable()
    {
        var data = System.Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(data);
        var t = new GuildHouseTable(packs);
        Assert.That(t.Count, Is.GreaterThan(0));
        Assert.That(t.Keys().All(k => t.Get(k.Level, k.Theme).Level == k.Level));
    }
}

/// <summary>CGuildXmlData::LoadGuildNpc (@0x1404d64c0, guilddef_2.cpp:545-559) with GuildNpc (FUN_1404d5270, :421-430).</summary>
public class GuildNpcTableTests
{
    static GuildNpcTable Make(string rows, System.Func<XElement, bool> avail = null) =>
        new(XDocument.Parse("<ms2>" + rows + "</ms2>"), avail);

    [Test]
    public void ARowReadsItsAttributesAndTheTypeByName()
    {
        var t = Make("<npc type='Goods' level='2' upgradeCost='5000000000' requireGuildLevel='3' requireHouseLevel='4' stringKey='8'/>");
        var n = t.Get(2, 2);
        Assert.That(n, Is.EqualTo(new GuildNpcTable.Npc(2, 2, 5000000000L, 3, 4, 8)));
    }

    [Test]
    public void TheTypeNamesMapToTheClientsKindsCaseInsensitively()
    {
        Assert.That(new[] { "equip", "GOODS", "Gemstone", "itemMerge", "music", "quest", "nothing", null }.Select(GuildNpcTable.KindOf),
            Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 0, 0 }));
    }

    [Test]
    public void KindsListOnceAscendingAndLevelsAreKeyedWithThem()
    {
        var t = Make("<npc type='quest' level='1'/><npc type='equip' level='2'/><npc type='equip' level='1'/>");
        Assert.That(t.Kinds(), Is.EqualTo(new[] { 1, 6 }));
        Assert.That(t.Get(1, 2), Is.Not.Null);
        Assert.That(t.Get(1, 3), Is.Null);
    }

    [Test]
    public void UnavailableRowsAndAWrongRootLoadNothing()
    {
        Assert.That(Make("<npc type='equip' level='1'/><npc type='equip' level='2'/>", n => (string)n.Attribute("level") != "1").Count, Is.EqualTo(1));
        Assert.That(new GuildNpcTable(XDocument.Parse("<root><npc type='equip' level='1'/></root>")).Count, Is.EqualTo(0));
    }

    [Test]
    public void TheDataHasTheTable()
    {
        var data = System.Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(data);
        var t = new GuildNpcTable(packs);
        Assert.That(t.Count, Is.GreaterThan(0));
        Assert.That(t.Kinds().All(k => k != 0));
    }
}
