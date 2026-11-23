using System.Linq;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>CGuildXmlData::LoadGuildRaid (@0x1404d67e0, guilddef_2.cpp:562-575) with GuildRaid (FUN_1404d53d0, :435-443).</summary>
public class GuildRaidTableTests
{
    static GuildRaidTable Make(string rows, System.Func<XElement, bool> avail = null) =>
        new(XDocument.Parse("<ms2>" + rows + "</ms2>"), avail);

    [Test]
    public void ARowReadsItsAttributes()
    {
        var t = Make("<guildRaid dungeonRoomID='3' clearRaidPoint='7' clearGuildFund='2.5' clearGuildExp='0.5'/>");
        Assert.That(t.Get(3), Is.EqualTo(new GuildRaidTable.Raid(3, 7, 2.5f, 0.5f)));
    }

    [Test]
    public void AMissingFundAndExpStayAtTheDefaultOfOne()
    {
        Assert.That(Make("<guildRaid dungeonRoomID='3'/>").Get(3), Is.EqualTo(new GuildRaidTable.Raid(3, 0, 1f, 1f)));
    }

    [Test]
    public void TheIdsListAscendingAndALaterRowOfTheSameIdReplaces()
    {
        var t = Make("<guildRaid dungeonRoomID='9' clearRaidPoint='1'/><guildRaid dungeonRoomID='2'/><guildRaid dungeonRoomID='9' clearRaidPoint='5'/>");
        Assert.That(t.Ids(), Is.EqualTo(new uint[] { 2, 9 }));
        Assert.That(t.Get(9).ClearRaidPoint, Is.EqualTo(5));
        Assert.That(t.Get(4), Is.Null);
    }

    [Test]
    public void UnavailableRowsAndAWrongRootLoadNothing()
    {
        Assert.That(Make("<guildRaid dungeonRoomID='1'/><guildRaid dungeonRoomID='2'/>", n => (string)n.Attribute("dungeonRoomID") != "1").Ids(), Is.EqualTo(new uint[] { 2 }));
        Assert.That(new GuildRaidTable(XDocument.Parse("<root><guildRaid dungeonRoomID='1'/></root>")).Count, Is.EqualTo(0));
    }

    [Test]
    public void TheDataHasTheTable()
    {
        var data = System.Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(data);
        var t = new GuildRaidTable(packs);
        Assert.That(t.Count, Is.GreaterThan(0));
        Assert.That(t.Ids().All(i => t.Get(i).DungeonRoomId == i));
    }
}
