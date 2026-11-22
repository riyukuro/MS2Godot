using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>GMS2 keeps a file per map and per npc where the KMS tables hold one row each: the rows built from the files have the KMS row's shape.</summary>
public class PerFileTableTests
{
    // map/02000001.xml of the GMS2 data, cut to what the shape needs
    const string MapFile = @"<ms2>
  <environment feature=""CBT"" locale=""KR""><xblock name=""02000001_TW_Tria_KR"" /></environment>
  <environment feature=""CBT"" locale=""""><xblock name=""02000001_TW_Tria"" /><property mapType=""1"" revivalreturnid=""02000001"" checkFly=""1"" />
    <camera distance=""1100"" hangle=""45"" vangle=""30"" /><fov value=""40"" /></environment>
</ms2>";

    // the KMS table's row of the same field
    const string KmsRow = @"<fieldData id=""2000001""><environment><xblock name=""02000001_TW_Tria"" /><property mapType=""1"" revivalreturnid=""2000001"" checkFly=""1"" />
    <camera distance=""1100"" hangle=""45"" vangle=""30"" /><fov value=""40"" /></environment></fieldData>";

    static XElement PickWithoutLocale(XElement f) => f.Elements("environment").FirstOrDefault(e => (string)e.Attribute("locale") == "");

    [Test]
    public void AFileBecomesARowOfTheKmsShape()
    {
        var doc = PerFileTable.Build(new[] { ("map/02000001.xml", XElement.Parse(MapFile)) }, "fieldData", PickWithoutLocale);
        var row = doc.Root.Element("fieldData");
        var kms = XElement.Parse(KmsRow);
        Assert.That((int)row.Attribute("id"), Is.EqualTo(2000001));
        Assert.That(row.Elements("environment").Count(), Is.EqualTo(1));
        Assert.That(row.Element("environment").Attribute("locale"), Is.Null);
        Assert.That(row.Element("environment").Attribute("feature"), Is.Null);
        Assert.That(row.Element("environment").Elements().Select(e => e.Name.LocalName), Is.EqualTo(kms.Element("environment").Elements().Select(e => e.Name.LocalName)));
        Assert.That(FieldData.DefaultEnvironment(row).Element("xblock").Attribute("name")?.Value,
            Is.EqualTo(FieldData.DefaultEnvironment(kms).Element("xblock").Attribute("name")?.Value));
    }

    [Test]
    public void FilesWithoutAnIdOrAnEnvironmentGiveNoRowAndNoFilesGiveNoTable()
    {
        var doc = PerFileTable.Build(new[] { ("map/readme.xml", XElement.Parse(MapFile)), ("map/00000005.xml", XElement.Parse("<ms2/>")), ("map/00000006.xml", null) },
            "fieldData", PickWithoutLocale);
        Assert.That(doc.Root.Elements("fieldData"), Is.Empty);
        Assert.That(PerFileTable.Build(Array.Empty<(string, XElement)>(), "fieldData", PickWithoutLocale), Is.Null);
    }

    static PackSet Packs()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return new PackSet(d);
    }

    [Test]
    public void TheDataHasAFieldTableOfEitherLayout()
    {
        var packs = Packs();
        Assert.That(FieldData.Exists(packs), Is.True);
        if (!packs.Exists("Xml", "map/02000001.xml")) return;                  // the KMS packs keep the table
        var f = FieldData.FindById(packs, 2000001);
        Assert.That(f?.XBlock, Is.EqualTo("02000001_TW_Tria"));
        Assert.That(f.Id, Is.EqualTo(2000001));
    }

    const string NpcFile = @"<ms2><environment feature=""Season1"" locale=""""><model kfm=""02010001_Stan"" scale=""0.75"" />
      <basic friendly=""2"" level=""20"" /><capsule radius=""70"" height=""210"" ignore=""0"" /></environment></ms2>";

    // the KMS npcdata row shape: <npc id><environment friendly level ...><model/><capsule/></environment></npc>
    [Test]
    public void ANpcFileReadsAsAKmsNpcRow()
    {
        var doc = PerFileTable.Build(new[] { ("npc/11/00/11000001.xml", XElement.Parse(NpcFile)) }, "npc", f => f.Element("environment"));
        var row = NpcTable.ParseRow(doc.Root.Element("npc"), Array.Empty<EffectDummy>());
        Assert.That(row.Id, Is.EqualTo(11000001));
        Assert.That(row.Kfm, Is.EqualTo("02010001_Stan"));
        Assert.That(row.Scale, Is.EqualTo(0.75f));
        Assert.That(row.CapsuleHeight, Is.EqualTo(210f));
    }

    [Test]
    public void TheNpcTableHasRowsInEitherLayout()
    {
        var packs = Packs();
        var t = new NpcTable(packs);
        Assert.That(t.Count, Is.GreaterThan(1000));
    }
}
