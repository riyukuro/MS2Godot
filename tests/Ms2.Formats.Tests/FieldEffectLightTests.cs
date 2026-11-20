using Ms2.Formats.Model;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// Effect lights (CLightEffect "EffectPointLight", lighteffectparam.cpp:361-381): the effect xml's light values, the slot an
/// effect light takes while it runs, and the receivers it reaches (every one, up to the draw's 8 lights).
/// </summary>
public class FieldEffectLightTests
{
    static FieldEntity Light(string id)
    {
        var raw = new XEntity { Id = id, ModelName = "MS2PointLight" };
        var flat = new FlatModel { Name = "MS2PointLight" };
        foreach (var m in new[] { "PointLight", "Light" }) flat.Mixins.Add(m);
        flat.Props["Range"] = "500"; flat.Props["LightPriority"] = "50"; flat.Props["DiffuseColor"] = "1, 1, 1";
        return new FieldEntity { Raw = raw, Flat = flat };
    }

    static FieldEntity Lightable(int maxPoint = 3)
    {
        var flat = new FlatModel { Name = "mesh" };
        flat.Mixins.Add("Mesh"); flat.Mixins.Add("Lightable");
        flat.Props["MaxPointLights"] = maxPoint.ToString(); flat.Props["MaxSpotLights"] = "2";
        return new FieldEntity { Raw = new XEntity { Id = "r", ModelName = "mesh" }, Flat = flat };
    }

    static FieldLightSet Field(int lights) => FieldLightSet.Read(Enumerable.Range(0, lights).Select(i => Light("l" + i)).ToList());

    static readonly EffectLightParam Param = new() { Dimmer = 3, Range = 900, Constant = 1, Linear = 0.0001f, Quadratic = 0.0001f };

    [Test]
    public void ReadsTheKrEffectLight()
    {
        var (packs, _) = NifEnv.Open();
        // buff keepEffect of a skill; the light part is the only part
        var part = new EffectTable(packs).Get("ch/common/eff_innerlight_01.xml").Single(p => p.Type == "light");
        Assert.That(part.Loop, Is.True);
        Assert.That(part.Translate, Is.EqualTo(new[] { 0f, -50f, 140f }));
        var l = part.Light;
        Assert.That((l.Dimmer, l.Range, l.Constant, l.Linear, l.Quadratic), Is.EqualTo((3f, 900f, 1f, 0.0001f, 0.0001f)));
        Assert.That(l.Diffuse, Is.EqualTo(new[] { 1f, 1f, 1f }), "255 / 255");
        var blue = new EffectTable(packs).Get("ch/common/eff_light.xml").Single(p => p.Type == "light").Light;
        Assert.That(blue.Diffuse[0], Is.EqualTo(200f / 255f).Within(1e-6), "200,255,255");
        Assert.That((blue.Constant, blue.Linear), Is.EqualTo((0f, 0.005f)));
    }

    [Test]
    public void AnEffectLightTakesASlotAfterTheFieldLightsAndFreesIt()
    {
        var set = Field(2);
        var a = set.StartEffect(Param, new Vec3(1, 2, 3));
        int v1 = set.EffectVersion;
        var b = set.StartEffect(Param, new Vec3(4, 5, 6));
        Assert.That((a.Index, b.Index), Is.EqualTo((2, 3)));
        Assert.That(set.EffectVersion, Is.EqualTo(v1 + 1));
        Assert.That(set.Lights, Has.Count.EqualTo(2), "the field's own lights are not touched");
        set.EndEffect(a);
        Assert.That(set.Effects[0], Is.Null);
        Assert.That(set.Effects[1], Is.SameAs(b));
        Assert.That(set.StartEffect(Param, new Vec3()).Index, Is.EqualTo(2), "the freed slot is taken again");
        set.EndEffect(a);   // already gone: nothing changes
    }

    [Test]
    public void NoFreeSlotStartsNothing()
    {
        var set = Field(FieldLightSet.MaskBits - 1);
        Assert.That(set.StartEffect(Param, new Vec3()), Is.Not.Null);
        int v = set.EffectVersion;
        Assert.That(set.StartEffect(Param, new Vec3()), Is.Null);
        Assert.That(set.EffectVersion, Is.EqualTo(v));
    }

    [Test]
    public void AReceiverInRangeIsLitAndSoIsOneOutOfRange()
    {
        var set = Field(2);
        var fx = set.StartEffect(Param, new Vec3(0, 0, 0));
        var r = Lightable();
        uint effectBit = 1u << fx.Index;
        // inside the effect light's range
        Assert.That(FieldLightSet.AffectsReceiver(fx, new Vec3(100, 0, 0), 10), Is.True);
        Assert.That(set.Unlit(r, new Vec3(100, 0, 0), 10) & effectBit, Is.EqualTo(0u));
        // far out of range: the client attaches an effect light to every scene graph, with no range test
        Assert.That(FieldLightSet.AffectsReceiver(fx, new Vec3(5000, 0, 0), 10), Is.False);
        Assert.That(set.Unlit(r, new Vec3(5000, 0, 0), 10) & effectBit, Is.EqualTo(0u));
        // a receiver that is not Lightable takes it as it takes every light
        var plain = new FieldEntity { Raw = new XEntity { Id = "p", ModelName = "m" }, Flat = new FlatModel { Name = "m" } };
        Assert.That(set.Unlit(plain, new Vec3(5000, 0, 0), 10), Is.EqualTo(0u));
    }

    [Test]
    public void ADrawWithEightFieldLightsDropsTheEffectLight()
    {
        // 9 field lights all reaching a receiver that takes 8: it holds 8, the effect light (attached last) is cut
        var set = Field(9);
        var fx = set.StartEffect(Param, new Vec3());
        var r = Lightable(8);
        uint off = set.Unlit(r, new Vec3(0, 0, 0), 10);
        Assert.That(off & (1u << fx.Index), Is.Not.EqualTo(0u));
        // the same receiver with room (7 of the field's lights) keeps it
        var r7 = Lightable(7);
        Assert.That(set.Unlit(r7, new Vec3(0, 0, 0), 10) & (1u << fx.Index), Is.EqualTo(0u));
        // not Lightable: all 9 field lights attached, so the effect light is cut there too
        var plain = new FieldEntity { Raw = new XEntity { Id = "p", ModelName = "m" }, Flat = new FlatModel { Name = "m" } };
        Assert.That(set.Unlit(plain, new Vec3(), 10) & (1u << fx.Index), Is.Not.EqualTo(0u));
    }
    [Test]
    public void AnEffectLightStartedWithHdrOffHasTheDimmerScaleAndATogglesRescalesIt()
    {
        // lighteffectparam.cpp:363-377: the effect light is an EffectPointLight entity, so HandleEntityEnterWorld gives it
        // Dimmer x1.1 with HDR off and ScaleLightIntensity (ms2lightservice.cpp:395-403) rescales it with the others
        var p = new EffectLightParam { Dimmer = 3, Range = 900, Constant = 1, Diffuse = new[] { 0.5f, 0.25f, 1f }, Specular = new[] { 1f, 1f, 1f } };
        var set = Field(2);
        var l = set.StartEffect(p, new Vec3());
        var data = new float[24 * 6 * 4];
        int o = (1 * 24 + l.Index) * 4;
        FieldLightSet.PutColourRows(data, 24, l.Index, l, Options.GraphicGates.LightDimmerScale(false));
        Assert.That(data[o], Is.EqualTo(0.5f * 3 * 1.1f).Within(1e-6f), "diffuse red, HDR off");
        Assert.That(data[o + 2], Is.EqualTo(1f * 3 * 1.1f).Within(1e-6f));
        Assert.That(data[o + 3], Is.EqualTo(1f), "the constant attenuation is not scaled");
        FieldLightSet.PutColourRows(data, 24, l.Index, l, Options.GraphicGates.LightDimmerScale(true));
        Assert.That(data[o], Is.EqualTo(0.5f * 3).Within(1e-6f), "diffuse red after the switch to HDR on");
        Assert.That(l.Dimmer, Is.EqualTo(3f), "the stored Dimmer is kept; only the upload is scaled");
    }
}
