using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>UGCCubeCategoryTable::LoadInteriors (@0x140886fc0, ugccubecategorytable.cpp:285-311).</summary>
public class CubeInteriorTableTests
{
    static XDocument X(string s) => XDocument.Parse(s);

    [Test]
    public void ARowsRequireLevelIsRead()
    {
        var t = new CubeInteriorTable(X("<ms2><list categoryIndex=\"12\" requireHousingLevel=\"4\"/></ms2>"));
        Assert.That(t.RequireLevel(12), Is.EqualTo((ushort)4));
        Assert.That(t.RequireLevel(13), Is.Null);
    }

    [Test]
    public void MissingAttributesTakeTheDefaults()
    {
        var t = new CubeInteriorTable(X("<ms2><list categoryIndex=\"12\"/><list requireHousingLevel=\"3\"/></ms2>"));
        Assert.That(t.RequireLevel(12), Is.EqualTo((ushort)1), "requireHousingLevel defaults to 1");
        Assert.That(t.RequireLevel(0), Is.EqualTo((ushort)3), "categoryIndex defaults to 0");
    }

    [Test]
    public void TheLastRowOfACategoryWins()
    {
        var t = new CubeInteriorTable(X("<ms2><list categoryIndex=\"12\" requireHousingLevel=\"2\"/><list categoryIndex=\"12\" requireHousingLevel=\"5\"/></ms2>"));
        Assert.That(t.RequireLevel(12), Is.EqualTo((ushort)5));
        Assert.That(t.Count, Is.EqualTo(1));
    }

    [Test]
    public void AFileWithoutTheMs2RootIsSkipped() =>
        Assert.That(new CubeInteriorTable(X("<root><list categoryIndex=\"12\"/></root>")).Count, Is.Zero);

    [Test]
    public void TheDataHasInteriorRows()
    {
        var data = System.Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(data);
        Assert.That(HousingTable.EntryOf(packs, CubeInteriorTable.File), Is.Not.Null);
        Assert.That(new CubeInteriorTable(packs).Count, Is.GreaterThan(0));
    }
}
