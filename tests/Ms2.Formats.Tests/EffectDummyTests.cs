using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>Effect dummy tables and the npc row fields of the head hp bar, on made-up XML.</summary>
public class EffectDummyTests
{
    const string Defaults = """
        <ms2>
          <dummy name="Top">
            <attachInfo parentNode="Scene Root" applyLocalScale="false" applyWorldTransform="true" />
            <transform translate="0.0, 0.0, 120.0" rotation="0.0, 0.0, 0.0" scale="1.0" />
          </dummy>
          <dummy name="Hit">
            <attachInfo parentNode="Top" applyLocalScale="false" applyWorldTransform="true" />
            <transform translate="0.0, 0.0, 0.0" />
          </dummy>
          <dummy name="Mid">
            <attachInfo parentNode="Spine" />
          </dummy>
        </ms2>
        """;

    static List<EffectDummy> DefaultRows() => EffectDummyTable.ParseDefault(XElement.Parse(Defaults));

    [Test]
    public void ConstructorDefaultsHoldForAbsentAttributes()
    {
        var d = new EffectDummy();
        Assert.That((d.ParentNode, d.ApplyWorldTransform, d.ApplyLocalScale, d.Translate, d.Rotation, d.Scale),
            Is.EqualTo(("", false, true, Vector3.Zero, Vector3.Zero, 1f)));
        var mid = EffectDummyTable.Find(DefaultRows(), "Mid");
        Assert.That((mid.ParentNode, mid.ApplyWorldTransform, mid.ApplyLocalScale, mid.Translate, mid.Scale), Is.EqualTo(("Spine", false, true, Vector3.Zero, 1f)));
    }

    [Test]
    public void DefaultRowsAreReadInFileOrder()
    {
        var rows = DefaultRows();
        Assert.That(rows.Select(r => r.Name), Is.EqualTo(new[] { "Top", "Hit", "Mid" }));
        Assert.That(rows[0].Translate, Is.EqualTo(new Vector3(0, 0, 120)));
        Assert.That((rows[0].ApplyWorldTransform, rows[0].ApplyLocalScale), Is.EqualTo((true, false)));
        Assert.That(rows[1].ParentNode, Is.EqualTo("Top"));
    }

    /// <summary>Booleans take true/yes/1 and false/no/0 in any case; anything else leaves the value.</summary>
    [TestCase("TRUE", true)]
    [TestCase("yes", true)]
    [TestCase("1", true)]
    [TestCase("No", false)]
    [TestCase("0", false)]
    [TestCase("2", true)]          // unreadable: the constructor's true stays
    public void BooleansFollowTheXmlReader(string value, bool expected)
    {
        var d = new EffectDummy();
        d.Parse(XElement.Parse($"<dummy name='x'><attachInfo applyLocalScale='{value}' /></dummy>"));
        Assert.That(d.ApplyLocalScale, Is.EqualTo(expected));
    }

    /// <summary>A point needs three comma tokens (empty ones skipped), each read as atof; with fewer the value stays.</summary>
    [TestCase("1,2,3", 1f, 2f, 3f)]
    [TestCase(" 1.5 ,  -2 ,3e1", 1.5f, -2f, 30f)]
    [TestCase("1,,2,,3", 1f, 2f, 3f)]
    [TestCase("4,5,6,7", 4f, 5f, 6f)]
    [TestCase("1,abc,3", 1f, 0f, 3f)]
    [TestCase("1,2", 9f, 9f, 9f)]
    public void PointsFollowTheXmlReader(string value, float x, float y, float z)
    {
        var d = new EffectDummy { Translate = new Vector3(9, 9, 9) };
        d.Parse(XElement.Parse($"<dummy><transform translate='{value}' /></dummy>"));
        Assert.That(d.Translate, Is.EqualTo(new Vector3(x, y, z)));
    }

    [Test]
    public void UnreadableScaleKeepsTheValue()
    {
        var d = new EffectDummy();
        d.Parse(XElement.Parse("<dummy><transform scale='big' /></dummy>"));
        Assert.That(d.Scale, Is.EqualTo(1f));
        d.Parse(XElement.Parse("<dummy><transform scale=' 2.5x' /></dummy>"));
        Assert.That(d.Scale, Is.EqualTo(2.5f));
    }

    /// <summary>The player table groups by gender, and every row is parsed over the previous one, so a left-out attribute carries over.</summary>
    [Test]
    public void PcRowsGroupByGenderAndCarryOver()
    {
        var pc = EffectDummyTable.ParsePc(XElement.Parse("""
            <ms2><effectdummy>
              <dummy name="Top" gender="0"><attachInfo parentNode="Scene Root" applyWorldTransform="true" applyLocalScale="false" /><transform translate="0,0,100" /></dummy>
              <dummy name="Low" gender="1"><transform translate="0,0,50" /></dummy>
              <dummy name="Side"><attachInfo parentNode="Hand" /></dummy>
            </effectdummy></ms2>
            """));
        var t = new EffectDummyTable(null, pc);
        Assert.That(t.Pc(0).Select(r => r.Name), Is.EqualTo(new[] { "Top", "Side" }));
        Assert.That(t.Pc(1).Select(r => r.Name), Is.EqualTo(new[] { "Low" }));
        Assert.That(t.Pc(2), Is.Empty);
        var low = t.Pc(1)[0];
        Assert.That((low.ParentNode, low.ApplyWorldTransform, low.ApplyLocalScale, low.Translate), Is.EqualTo(("Scene Root", true, false, new Vector3(0, 0, 50))));
        var side = t.Pc(0)[1];
        Assert.That((side.ParentNode, side.Translate), Is.EqualTo(("Hand", new Vector3(0, 0, 50))));
        Assert.That(t.Pc(0)[0].Translate, Is.EqualTo(new Vector3(0, 0, 100)));
    }

    /// <summary>An npc dummy with a default's name is parsed over a copy of that row; any other is added as a fresh row.</summary>
    [Test]
    public void NpcDummiesOverrideOrAdd()
    {
        var defaults = DefaultRows();
        var env = XElement.Parse("""
            <environment><effectdummy>
              <dummy name="Top"><transform translate="0,0,210" /></dummy>
              <dummy name="top"><attachInfo parentNode="Scene Root" /></dummy>
              <dummy name="Extra"><transform translate="1,2,3" /></dummy>
            </effectdummy></environment>
            """);
        var rows = EffectDummyTable.MergeNpc(defaults, env);
        Assert.That(rows.Select(r => r.Name), Is.EqualTo(new[] { "Top", "Hit", "Mid", "top", "Extra" }));
        var top = rows[0];
        Assert.That((top.ParentNode, top.ApplyWorldTransform, top.ApplyLocalScale, top.Translate), Is.EqualTo(("Scene Root", true, false, new Vector3(0, 0, 210))));
        Assert.That(defaults[0].Translate, Is.EqualTo(new Vector3(0, 0, 120)), "the shared default row is not changed");
        Assert.That(rows[4].ApplyLocalScale, Is.True, "an added row starts from the constructor");
        Assert.That(EffectDummyTable.MergeNpc(defaults, XElement.Parse("<environment />")), Is.SameAs(defaults));
    }

    [Test]
    public void NpcRowReadsHpBarTagsAndDummies()
    {
        var npc = XElement.Parse("""
            <npc id="7"><environment hpBar="1" propertyTags="Big_Hp_Bar, always_display_hp_bar">
              <effectdummy><dummy name="Top"><transform translate="0,0,99" /></dummy></effectdummy>
            </environment></npc>
            """);
        var row = NpcTable.ParseRow(npc, DefaultRows());
        Assert.That((row.Id, row.HpBar, row.BigHpBar, row.AlwaysDisplayHpBar, row.HasDamagedHpBarTag), Is.EqualTo((7, true, true, true, false)));
        Assert.That(row.Dummy("Top").Translate.Z, Is.EqualTo(99f));
        Assert.That(row.Dummy("Hit").ParentNode, Is.EqualTo("Top"));

        var plain = NpcTable.ParseRow(XElement.Parse("<npc id='8'><environment /></npc>"), DefaultRows());
        Assert.That((plain.HpBar, plain.BigHpBar, plain.AlwaysDisplayHpBar), Is.EqualTo((false, false, false)));
        Assert.That(plain.Dummy("Top").Translate.Z, Is.EqualTo(120f));
        Assert.That(NpcTable.ParseRow(XElement.Parse("<npc id='9' />"), DefaultRows()), Is.Null);
    }

    /// <summary>The target frame's row fields: level (1 when absent), class (1 when absent), race, the hidden level tag and the stat hp.</summary>
    [Test]
    public void NpcRowReadsLevelClassAndRace()
    {
        var row = NpcTable.ParseRow(XElement.Parse("""
            <npc id="5"><environment level="12" class="3" raceString="Plant" propertyTags="Hide_HpBar_Level"><stat hp="1500" asp="100" /></environment></npc>
            """), DefaultRows());
        Assert.That((row.Level, row.Class, row.Race, row.HideHpBarLevel, row.HpBarLevel), Is.EqualTo((12, 3, 2, true, 0)));
        var plain = NpcTable.ParseRow(XElement.Parse("<npc id='6'><environment raceString='robot' /></npc>"), DefaultRows());
        Assert.That((plain.Level, plain.Class, plain.Race, plain.HpBarLevel), Is.EqualTo((1, 1, 0, 1)));
        Assert.That((row.Hp, plain.Hp), Is.EqualTo((1500L, 0L)));                 // stat hp, 0 without a stat block
        var names = new Dictionary<string, string> { ["plant"] = "Leafy", ["animal"] = "Furry" };
        Assert.That(NpcTable.RaceName(names, row.Race), Is.EqualTo("Leafy"));
        Assert.That(NpcTable.RaceName(names, NpcTable.ParseRace("ANIMAL")), Is.EqualTo("Furry"));
        Assert.That(NpcTable.RaceName(names, NpcTable.ParseRace("bug")), Is.Null, "a race the table lacks");
        Assert.That(NpcTable.RaceName(names, 0), Is.Null);
        Assert.That(NpcTable.ParseRace("creature"), Is.EqualTo(12));
    }

    /// <summary>The name tag colour key: friendly rows, then the class, and the hostile / dead pick.</summary>
    [Test]
    public void NpcRowPicksTagColour()
    {
        NpcRow Row(string env) => NpcTable.ParseRow(XElement.Parse($"<npc id='4'><environment {env} /></npc>"), DefaultRows());
        Assert.That(Row("friendly='2' class='5'").ClassColorKey, Is.EqualTo("NameTagColorFriendly"));
        Assert.That(Row("").ClassColorKey, Is.EqualTo("NameTagColorMob"), "class 1 when absent");
        Assert.That((Row("class='3'").ClassColorKey, Row("class='7'").ClassColorKey), Is.EqualTo(("NameTagColorNamed", "NameTagColorNamed")));
        Assert.That((Row("class='4'").ClassColorKey, Row("class='6'").ClassColorKey), Is.EqualTo(("NameTagColorBoss", "NameTagColorBoss")));
        Assert.That((Row("class='2'").ClassColorKey, Row("class='5'").ClassColorKey), Is.EqualTo(((string)null, (string)null)));
        var mob = Row("class='1'");
        Assert.That(mob.TagColorKey(hostile: true, dead: false), Is.EqualTo("NameTagColorMob"));
        Assert.That(mob.TagColorKey(hostile: true, dead: true), Is.EqualTo("NameTagColorDead"));
        Assert.That(mob.TagColorKey(hostile: false, dead: true), Is.EqualTo("NameTagColorFriendly"));
    }

    /// <summary>The player's data, when present: the UI dummy of the default and player tables.</summary>
    [Test]
    public void GameTablesHaveAUiDummy()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var t = EffectDummyTable.Load(new PackSet(data));
        var ui = EffectDummyTable.Find(t.Default, "Eff_UI");
        Assert.That(ui, Is.Not.Null);
        Assert.That((ui.ParentNode, ui.ApplyWorldTransform, ui.ApplyLocalScale), Is.EqualTo(("Scene Root", true, false)));
        Assert.That(EffectDummyTable.Find(t.Default, "Eff_Damage")?.ParentNode, Is.EqualTo("Eff_UI"));
        foreach (int g in new[] { 0, 1 })
            Assert.That(EffectDummyTable.Find(t.Pc(g), "Eff_UI"), Is.Not.Null, $"gender {g}");
    }
}
