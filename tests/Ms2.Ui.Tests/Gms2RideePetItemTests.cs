using Ms2.Formats.Pack;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>GMS2 item files write no param1 on &lt;property&gt;: a ride item names its mount in &lt;ride rideMonster&gt; and a
/// pet in &lt;pet petID&gt;, so GetRideeId / the pet id (param1 of type 9 / 11, itemdata.cpp:1173) were 0 and a mount item did
/// nothing when used. Needs MS2_GMS2_DATA.</summary>
public class Gms2RideePetItemTests
{
    static PackSet packs;

    static PackSet Packs()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        return packs ??= new PackSet(d);
    }

    static IEnumerable<int> Ids(PackSet p, params string[] folders) =>
        p.List("Xml").Where(e => folders.Any(f => e.StartsWith(f, StringComparison.Ordinal)) && e.EndsWith(".xml", StringComparison.Ordinal))
            .Select(e => int.TryParse(e[(e.LastIndexOf('/') + 1)..^4], out int i) ? i : 0).Where(i => i > 0);

    [Test]
    public void ARideItemNamesItsMount()
    {
        var items = new ItemDataTable(Packs());
        Assert.That(items.Get(50600171).RideeId, Is.EqualTo(50600171));
        Assert.That(items.Get(50600212).RideeId, Is.EqualTo(50600212), "GMS2's own row (KMS's param1 is 50600189)");
        Assert.That(items.Get(50620012).RideeId, Is.EqualTo(50600330), "a mount id unlike the item id");
    }

    [Test]
    public void EveryRideItemNamesARidingRow()
    {
        var p = Packs();
        var items = new ItemDataTable(p);
        int n = 0; var bad = new List<string>();
        foreach (int id in Ids(p, "item/5/"))
        {
            if (items.Get(id) is not { Type: 9 } d) continue;
            n++;
            if (d.RideeId == 0 || !p.Exists("Xml", $"riding/{d.RideeId:D8}.xml")) bad.Add($"{id}:{d.RideeId}");
        }
        TestContext.WriteLine($"{n} ride items, {bad.Count} without a riding row");
        Assert.That(n, Is.GreaterThanOrEqualTo(329), "331 files; 2 have only environments of a feature the data turns off");
        Assert.That(bad, Is.Empty);
    }

    [Test]
    public void EveryPetItemNamesItsPet()
    {
        var p = Packs();
        var items = new ItemDataTable(p);
        int n = 0; var bad = new List<int>();
        foreach (int id in Ids(p, "item/6/"))
        {
            if (items.Get(id) is not { Type: 0xb } d) continue;
            n++;
            if (d.PetId == 0) bad.Add(id);
        }
        TestContext.WriteLine($"{n} pet items, {bad.Count} without a pet id");
        Assert.That(n, Is.GreaterThanOrEqualTo(514), "522 files; 8 have only environments of a feature the data turns off");
        Assert.That(bad, Is.Empty);
        Assert.That(items.Get(61000159).PetId, Is.EqualTo(61000159));
    }
}
