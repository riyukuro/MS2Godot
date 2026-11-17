using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

public class FieldDataTests
{
    /// <summary>The table is parsed once per pack set; every lookup still returns a fresh, equal FieldData.</summary>
    [Test]
    public void RepeatedLookupsAreEqualAndIndependent()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new PackSet(data);
        var a = FieldData.FindByXBlock(packs, "02000001_tw_tria");
        Assert.That(a, Is.Not.Null);
        a.Camera.Distance += 123f; a.CheckClimb = !a.CheckClimb;
        var b = FieldData.FindByXBlock(packs, "02000001_TW_TRIA");
        var c = FieldData.FindByXBlock(packs, "02000001_tw_tria");
        Assert.That(b, Is.Not.SameAs(a));
        Assert.That(b.Camera.Distance, Is.EqualTo(c.Camera.Distance));
        Assert.That(b.Camera.Distance, Is.Not.EqualTo(a.Camera.Distance));
        Assert.That(b.CheckClimb, Is.EqualTo(c.CheckClimb));
        Assert.That(b.Id, Is.EqualTo(c.Id));
        Assert.That(FieldData.XBlockOf(packs, b.Id), Is.EqualTo(b.XBlock));
    }
}
