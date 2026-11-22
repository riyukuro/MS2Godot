using System.Linq;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>UgcHomeOptionData::Load (@0x140b67fa0, ugchomebgdata.cpp:99-250).</summary>
public class UgcHomeOptionDataTests
{
    static XDocument X(string s) => XDocument.Parse(s);

    [Test]
    public void RowsKeepDocumentOrderWithNamesById()
    {
        var d = new UgcHomeOptionData(
            X("<ms2><list id=\"2\" path=\"b.dds\"/><list id=\"1\" path=\"a.dds\"/></ms2>"),
            X("<ms2><list id=\"101\" ambientcolor=\"255, 51, 0\"/></ms2>"),
            X("<ms2><list id=\"201\" offset=\"-0.5, 1.0, 2\" upAxis=\"0.0, 0.0, 1.0\"/></ms2>"),
            id => "n" + id);
        Assert.That(d.Backgrounds.Select(b => (b.Id, b.Path, b.Name)), Is.EqualTo(new[] { (2, "b.dds", "n2"), (1, "a.dds", "n1") }));
        Assert.That(d.Lights[0].Color, Is.EqualTo((1f, 0.2f, 0f)));
        Assert.That(d.Cameras[0].Offset, Is.EqualTo((-0.5f, 1f, 2f)));
        Assert.That(d.Cameras[0].UpAxis, Is.EqualTo((0f, 0f, 1f)));
        Assert.That(d.Lights[0].Name, Is.EqualTo("n101"));
    }

    [Test]
    public void ABackgroundRowTheCollectorDropsLeavesNoGapInTheKeys()
    {
        var bg = X("<ms2><list id=\"1\" path=\"a\"/><list id=\"2\" path=\"b\" feature=\"Off\"/><list id=\"3\" path=\"c\"/></ms2>");
        var d = new UgcHomeOptionData(bg, null, null, id => "",
            root => root.Elements("list").Where(e => (string)e.Attribute("feature") != "Off").ToList());
        Assert.That(d.Backgrounds.Select(b => b.Id), Is.EqualTo(new[] { 1, 3 }));
        Assert.That(new UgcHomeOptionData(bg, null, null, id => "").Backgrounds.Count, Is.EqualTo(3));
    }

    [Test]
    public void AFileWithoutTheMs2RootGivesNoRows()
    {
        var d = new UgcHomeOptionData(X("<root><list id=\"1\" path=\"a\"/></root>"), null, X("<ms2/>"), _ => "");
        Assert.That(d.Backgrounds, Is.Empty);
        Assert.That(d.Lights, Is.Empty);
        Assert.That(d.Cameras, Is.Empty);
    }

    [Test]
    public void TheDataHasAllThreeLists()
    {
        var data = System.Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(data);
        var d = new UgcHomeOptionData(packs, _ => "");
        Assert.That(d.Backgrounds, Is.Not.Empty);
        Assert.That(d.Lights, Is.Not.Empty);
        Assert.That(d.Cameras, Is.Not.Empty);
        Assert.That(d.Backgrounds.All(b => b.Id > 0 && b.Path.Length > 0));
        Assert.That(d.Lights.All(l => l.Color.R <= 1f && l.Color.G <= 1f && l.Color.B <= 1f));
        Assert.That(d.Backgrounds.Select(b => b.Id).Distinct().Count(), Is.EqualTo(d.Backgrounds.Count));
    }
}
