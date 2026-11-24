using System.Xml.Linq;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>SetItemTable on made-up rows of both layouts: KMS's &lt;set&gt;&lt;part&gt; and GMS2's &lt;set optionID&gt; with the parts
/// in table/setitemoption.xml (set 15123101 of each data cut down).</summary>
public class Gms2SetItemOptionShapeTests
{
    static readonly Dictionary<int, string> Names = new() { [0] = "str", [23] = "pap", [31] = "pen" };

    static SetItemTable Table(string sets, string options) => new(XDocument.Parse(sets), (p, c, k) => p.Elements(c), _ => true,
        k => Names.TryGetValue(k, out var n) ? n : "", _ => "", options == null ? null : XDocument.Parse(options));

    static void AssertParts(SetItemTable t)
    {
        var s = t.Get(15123101);
        Assert.That(s, Is.Not.Null);
        Assert.That(s.ItemIds, Is.EqualTo(new[] { 11300451, 11400397 }));
        Assert.That(s.Parts.Keys, Is.EquivalentTo(new[] { 2, 4, 5 }));
        Assert.That(s.Parts[2].Stats[0], Is.EqualTo(9));
        Assert.That(s.Parts[4].Stats[23], Is.EqualTo(6));
        Assert.That(s.Parts[5].Stats[31], Is.EqualTo(20));
        Assert.That(s.Parts[5].Effects, Is.EqualTo(new[] { (0, (ushort)1) }));
        Assert.That(t.SetsOf(11400397), Has.Count.EqualTo(1));
    }

    [Test]
    public void TheKmsSetReadsItsOwnParts() => AssertParts(Table(
        "<ms2><set id=\"15123101\" itemIDs=\"11300451,11400397\" optionID=\"15123101\">"
        + "<part count=\"2\" str=\"9\"/><part count=\"4\" pap=\"6\"/><part count=\"5\" additionalEffectID=\"0\" additionalEffectLevel=\"1\" pen=\"20\"/></set></ms2>", null));

    [Test]
    public void TheGms2SetReadsTheOptionRowsParts() => AssertParts(Table(
        "<ms2><set id=\"15123101\" itemIDs=\"11300451,11400397\" optionID=\"15123101\"/></ms2>",
        "<ms2><option id=\"15123101\">"
        + "<part count=\"2\" additionalEffectID=\"0\" additionalEffectLevel=\"1\" str_value_base=\"9\"/>"
        + "<part count=\"4\" additionalEffectID=\"0\" additionalEffectLevel=\"1\" pap_value_base=\"6\"/>"
        + "<part count=\"5\" additionalEffectID=\"0\" additionalEffectLevel=\"1\" pen_rate_base=\"0.02\"/></option></ms2>"));

    [Test]
    public void ASetWithItsOwnPartsKeepsThem()
    {
        var t = Table("<ms2><set id=\"1\" itemIDs=\"5\" optionID=\"7\"><part count=\"2\" str=\"3\"/></set></ms2>",
            "<ms2><option id=\"7\"><part count=\"2\" str_value_base=\"9\"/><part count=\"4\" pap_value_base=\"6\"/></option></ms2>");
        Assert.That(t.Get(1).Parts.Keys, Is.EqualTo(new[] { 2 }));
        Assert.That(t.Get(1).Parts[2].Stats[0], Is.EqualTo(3));
    }
}
