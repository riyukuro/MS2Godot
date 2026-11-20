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
        // what the field shader is given (ModelBuilder.SetUvTransforms): the held last key, not the file value
        foreach (var m in md.Meshes.Where(m => m.UvHolds.Count > 0))
        {
            var drawn = m.DrawnTransform("base", false);
            Assert.That(drawn.TV, Is.EqualTo(1.1f).Within(1e-5));
            Assert.That(m.Maps["base"].Transform.TV, Is.EqualTo(-1.2f).Within(1e-5), "the map's own transform is not changed");
        }
    }

    /// <summary>A hold on a map without its own transform sets the key on the controller default one; a hold on a slot
    /// the mesh has no map in is dropped.</summary>
    [Test]
    public void HoldWithoutTransformUsesControllerDefault()
    {
        var m = new ModelMesh();
        m.Maps["base"] = new TexMap();
        m.UvHolds.Add(new UvAnim { Slot = "base", Operation = 0, Values = new[] { 0f, 0.25f } });
        m.UvHolds.Add(new UvAnim { Slot = "glow", Operation = 0, Values = new[] { 0.5f } });
        var t = m.DrawnTransform("base", false);
        Assert.That(t, Is.Not.Null);
        Assert.That(t.TU, Is.EqualTo(0.25f));
        Assert.That(t.SU, Is.EqualTo(TexTransform.ControllerDefault().SU));
        Assert.That(m.DrawnTransform("glow", false), Is.Null);
        Assert.That(m.DrawnTransform("dark", false), Is.Null);
    }
}
