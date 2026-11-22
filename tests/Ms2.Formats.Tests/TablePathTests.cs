using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>GetXmlPath("Table", file) as the client opens it: the locale folder in front of the file name first, then the plain path.</summary>
public class TablePathTests
{
    static Func<string, bool> Has(params string[] names) { var s = new HashSet<string>(names); return s.Contains; }

    [Test]
    public void TheNaFolderWinsWhenThePackHoldsOnlyThat()
    {
        Assert.That(TablePath.Resolve(Has("table/na/multicontrol.xml", "table/other.xml"), "multicontrol.xml"), Is.EqualTo("table/na/multicontrol.xml"));
    }

    [Test]
    public void TheNaFolderBeatsThePlainFile()
    {
        Assert.That(TablePath.Resolve(Has("table/na/smartpush.xml", "table/smartpush.xml"), "smartpush.xml"), Is.EqualTo("table/na/smartpush.xml"));
    }

    [Test]
    public void KmsDataKeepsItsKrFolderThenThePlainPath()
    {
        Assert.That(TablePath.Resolve(Has("table/kr/multicontrol.xml", "table/multicontrol.xml"), "MultiControl.xml"), Is.EqualTo("table/kr/multicontrol.xml"));
        Assert.That(TablePath.Resolve(Has("table/itemdata.xml"), "itemdata.xml"), Is.EqualTo("table/itemdata.xml"));
        Assert.That(TablePath.Resolve(Has("table/kr/a.xml"), "b.xml"), Is.Null);
    }
}
