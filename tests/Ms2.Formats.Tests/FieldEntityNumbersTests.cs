using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

public class FieldEntityNumbersTests
{
    static FieldEntity Entity(string pos, string rot, string scale, string flatPos = null)
    {
        var raw = new XEntity { ModelName = "m" };
        if (pos != null) raw.Props["Position"] = pos;
        if (rot != null) raw.Props["Rotation"] = rot;
        if (scale != null) raw.Props["Scale"] = scale;
        var flat = new FlatModel { Name = "m" };
        if (flatPos != null) flat.Props["Position"] = flatPos;
        return new FieldEntity { Raw = raw, Flat = flat };
    }

    [Test]
    public void RepeatedReadsMatchAFreshParse()
    {
        foreach (var (p, r, s) in new[] { ("1.5, -2,3e2", "0,90,45", "2"), ("a,b,c", "1,2", "x"), (",", "", "0.5") })
        {
            var cached = Entity(p, r, s);
            for (int i = 0; i < 3; i++)
            {
                var fresh = Entity(p, r, s);   // a new entity has no cache: the fresh parse
                Assert.That(cached.Numbers("Position"), Is.EqualTo(fresh.Numbers("Position")));
                Assert.That(cached.Position, Is.EqualTo(fresh.Position));
                Assert.That(cached.RotationDeg, Is.EqualTo(fresh.RotationDeg));
                Assert.That(cached.Scale, Is.EqualTo(fresh.Scale));
                Assert.That(cached.Transform.T, Is.EqualTo(fresh.Transform.T));
                Assert.That(cached.Transform.R, Is.EqualTo(fresh.Transform.R));
                Assert.That(cached.Transform.S, Is.EqualTo(fresh.Transform.S));
            }
        }
    }

    [Test]
    public void MissingKeyIsNullAndFlatDefaultIsUsed()
    {
        var e = Entity(null, null, null, "4,5,6");
        Assert.That(e.Numbers("Rotation"), Is.Null);
        Assert.That(e.Numbers("Position"), Is.EqualTo(new double[] { 4, 5, 6 }));
        Assert.That(e.Numbers("Position"), Is.SameAs(e.Numbers("Position")));
    }

    [Test]
    public void EveryEntityOfARealFieldMatchesAFreshParse()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new Ms2.Formats.Pack.PackSet(data);
        var map = FieldMap.Load(packs, new FlatLibrary(packs), "02000001_tw_tria");
        Assert.That(map.Entities.Count, Is.GreaterThan(1000));
        foreach (var e in map.Entities)
        {
            var fresh = new FieldEntity { Raw = e.Raw, Flat = e.Flat };
            for (int i = 0; i < 2; i++)
            {
                Assert.That(e.Position, Is.EqualTo(fresh.Position));
                Assert.That(e.RotationDeg, Is.EqualTo(fresh.RotationDeg));
                Assert.That(e.Scale, Is.EqualTo(fresh.Scale));
                Assert.That(e.Numbers("MaterialColor"), Is.EqualTo(fresh.Numbers("MaterialColor")));
                Assert.That(e.Transform.R, Is.EqualTo(fresh.Transform.R));
            }
        }
    }
}
