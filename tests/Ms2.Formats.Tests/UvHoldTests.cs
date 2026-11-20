using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

[TestFixture]
public class UvHoldTests
{
    /// <summary>
    /// The beaker's base map has an active CLAMP translate-V controller, keys 0:-1.2 and 1.333:1.1, on each of its two meshes. A field model's app
    /// time is past its stop on field entry, so it is a hold at the last key (UvHolds), not a running animation (UvAnims).
    /// </summary>
    [Test]
    public void BeakerClampHoldsLastKey()
    {
        var (packs, spec) = NifEnv.Open();
        var md = ModelData.Load(NifFile.Load(spec, packs.Read("Resource/Model/Map", "ellinia/indoor/el_in_prop_beaker_a01.nif")));
        // two Editable Poly meshes, each with the controller on its base map
        var holds = md.Meshes.SelectMany(m => m.UvHolds).ToList();
        Assert.That(holds, Has.Count.EqualTo(2));
        foreach (var h in holds)
        {
            Assert.That(h.Slot, Is.EqualTo("base"));
            Assert.That(h.Operation, Is.EqualTo(1));
            Assert.That(h.Values[^1], Is.EqualTo(1.1f).Within(1e-5));
        }
        // the map's own transform keeps the file value (EffectScene reads it as the pre-start value)
        Assert.That(md.Meshes.Where(m => m.UvHolds.Count > 0).Select(m => m.Maps["base"].Transform.TV), Is.All.EqualTo(-1.2f).Within(1e-5));
        Assert.That(md.Meshes.SelectMany(m => m.UvAnims), Is.Empty);
    }
}
