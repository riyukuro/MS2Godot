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
    /// <summary>gemSystem (fielddata.cpp:706) follows the property's attribute and is true when absent (:557).</summary>
    [Test]
    public void GemSystemFollowsTheProperty()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new PackSet(data);
        int checkedOff = 0, checkedOn = 0;
        foreach (var fd in FieldData.Document(packs).Root.Elements("fieldData"))
        {
            var env = fd.Elements("environment").FirstOrDefault(e => e.Attribute("locale") == null);
            if (env?.Element("xblock")?.Attribute("name") == null) continue;
            var prop = env.Element("property") ?? fd.Element("property");
            var a = (string)prop?.Attribute("gemSystem");
            bool want = a == null || a is not ("0" or "false" or "FALSE" or "False" or "no");
            var f = FieldData.FindById(packs, (int)fd.Attribute("id"));
            if (f == null || f.Id != (int)fd.Attribute("id")) continue;
            Assert.That(f.GemSystem, Is.EqualTo(want), "field " + f.Id);
            if (want) checkedOn++; else checkedOff++;
        }
        Assert.That(checkedOn, Is.GreaterThan(0));
        TestContext.WriteLine($"gemSystem on {checkedOn}, off {checkedOff}");
    }

    /// <summary>The index gives what a scan of the table gives: by id the last entry of an id that has a default xblock
    /// (CFieldData::LoadFieldData, fielddata.cpp:591 SetAt), by xblock the first entry of a name.</summary>
    [Test]
    public void IndexAgreesWithTheTable()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new PackSet(data);
        var byId = new Dictionary<int, string>();
        var byXb = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var fd in FieldData.Document(packs).Root.Elements("fieldData"))
        {
            var env = fd.Elements("environment").FirstOrDefault(e => e.Attribute("locale") == null);
            var xb = (string)env?.Element("xblock")?.Attribute("name");
            if (xb == null || (int?)fd.Attribute("id") is not int id) continue;
            byId[id] = xb;
            byXb.TryAdd(xb, id);
        }
        Assert.That(byId.Count, Is.GreaterThan(100));
        foreach (var (id, xb) in byId)
        {
            Assert.That(FieldData.XBlockOf(packs, id), Is.EqualTo(xb), "id " + id);
            Assert.That(FieldData.FindById(packs, id)?.Id, Is.EqualTo(id), "id " + id);
        }
        foreach (var (xb, id) in byXb)
            Assert.That(FieldData.FindByXBlock(packs, xb.ToUpperInvariant())?.Id, Is.EqualTo(id), xb);
        Assert.That(FieldData.FindById(packs, int.MinValue), Is.Null);
        Assert.That(FieldData.XBlockOf(packs, int.MinValue), Is.Null);
        Assert.That(FieldData.FindByXBlock(packs, "no such xblock"), Is.Null);
    }

    /// <summary>A lookup of the table's last entry costs one entry, not a walk of the whole table (the old scan made an
    /// environment enumerator per entry on the way, ~110 KB per lookup of the last entry).</summary>
    [Test]
    public void LookupOfTheLastEntryAllocatesLittle()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new PackSet(data);
        string last = null;
        foreach (var fd in FieldData.Document(packs).Root.Elements("fieldData"))
            if ((string)fd.Elements("environment").FirstOrDefault(e => e.Attribute("locale") == null)?.Element("xblock")?.Attribute("name") is { } xb)
                last = xb;
        Assert.That(FieldData.FindByXBlock(packs, last), Is.Not.Null);      // warm: the index is built once
        long before = GC.GetAllocatedBytesForCurrentThread();
        var f = FieldData.FindByXBlock(packs, last);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.WriteLine($"FindByXBlock({last}) allocated {bytes} B");
        Assert.That(f, Is.Not.Null);
        Assert.That(bytes, Is.LessThan(16 * 1024));
    }
}
