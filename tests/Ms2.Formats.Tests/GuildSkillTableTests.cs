using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

public class GuildSkillTableTests
{
    static GuildSkillTable Make(string rows) => new(XDocument.Parse("<ms2>" + rows + "</ms2>"));

    [Test]
    public void RowsAreFiledByIdAndLevelAndTimesAreInSeconds()
    {
        var t = Make("<guildBuff id='2' level='1' duration='3' cooltime='2' upgradeCost='50' cost='7' functionName='f'/>" +
                     "<guildBuff id='2' level='2' upgradeCost='90'/><guildBuff id='1' level='1'/>");
        var s = t.Get(2, 1);
        Assert.That((s.Duration, s.Cooltime, s.UpgradeCost, s.Cost, s.FunctionName), Is.EqualTo((180, 120, 50, 7, "f")));
        Assert.That(t.Get(2, 2).UpgradeCost, Is.EqualTo(90));
        Assert.That(t.Get(2, 3), Is.Null);
        Assert.That(t.Get(9, 1), Is.Null);
        Assert.That(t.GuildSkillIds(), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void KindsAreByIdRange()
    {
        var t = Make("<guildBuff id='1000' level='1'/><guildBuff id='1001' level='1'/><guildBuff id='10000' level='1'/><guildBuff id='10001' level='1'/>");
        Assert.That(t.Get(1000, 1).IsEvent, Is.False);
        Assert.That(t.Get(1001, 1).IsEvent, Is.True);
        Assert.That(t.Get(10000, 1).IsEvent, Is.True);
        Assert.That(t.Get(10001, 1).IsPersonal && !t.Get(10001, 1).IsGuild, Is.True);
        Assert.That(t.GuildSkillIds(), Is.EqualTo(new[] { 1000, 1001, 10000 }));
        Assert.That(t.PersonalSkillIds(), Is.EqualTo(new[] { 10001 }));
    }

    [Test]
    public void UnavailableRowsAndAWrongRootLoadNothing()
    {
        var doc = XDocument.Parse("<ms2><guildBuff id='1' level='1'/><guildBuff id='2' level='1'/></ms2>");
        Assert.That(new GuildSkillTable(doc, e => (string)e.Attribute("id") == "2").Get(1, 1), Is.Null);
        Assert.That(new GuildSkillTable(XDocument.Parse("<x><guildBuff id='1' level='1'/></x>")).Count, Is.EqualTo(0));
    }

    [Test]
    public void TheRealTableHasGuildAndPersonalSkillsWithLevelOne()
    {
        var dir = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(dir);
        var t = new GuildSkillTable(packs);
        Assert.That(t.Count, Is.GreaterThan(0));
        foreach (int id in t.GuildSkillIds()) Assert.That(t.Get(id, 1), Is.Not.Null, "level 1 of " + id);
    }
}
