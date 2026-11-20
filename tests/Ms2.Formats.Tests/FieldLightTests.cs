using Ms2.Formats.Model;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// FieldLightSet: the point and spot light entities read as the client's NiLights, and which receivers each one lights
/// (ecr LightService's per-receiver relight: range + bound, priority, MaxPointLights / MaxSpotLights).
/// </summary>
public class FieldLightTests
{
    static FieldEntity Light(string model, string[] mixins, params (string, string)[] props)
    {
        var raw = new XEntity { Id = "light", ModelName = model };
        foreach (var (k, v) in props) raw.Props[k] = v;
        var flat = new FlatModel { Name = model };
        foreach (var m in mixins) flat.Mixins.Add(m);
        // the MS2PointLight / MS2SpotLight flat defaults
        flat.Props["ConstantAttenuation"] = "1"; flat.Props["LinearAttenuation"] = "0"; flat.Props["QuadraticAttenuation"] = "0";
        flat.Props["DiffuseColor"] = "1, 1, 1"; flat.Props["AmbientColor"] = "0, 0, 0"; flat.Props["Dimmer"] = "1";
        flat.Props["Range"] = "10000"; flat.Props["LightPriority"] = "50";
        flat.Props["OuterSpotAngle"] = "22.5"; flat.Props["InnerSpotAngle"] = "21.5"; flat.Props["SpotExponent"] = "1";
        return new FieldEntity { Raw = raw, Flat = flat };
    }

    static FieldEntity Point(params (string, string)[] props) => Light("MS2PointLight", new[] { "PointLight", "Light" }, props);
    static FieldEntity Spot(params (string, string)[] props) => Light("MS2SpotLight", new[] { "SpotLight", "PointLight", "Light" }, props);

    static FieldEntity Receiver(string id = "r", int maxPoint = 3, int maxSpot = 2, params string[] mixins)
    {
        var flat = new FlatModel { Name = "mesh" };
        flat.Mixins.Add("Mesh");
        foreach (var m in mixins) flat.Mixins.Add(m);
        flat.Props["MaxPointLights"] = maxPoint.ToString();
        flat.Props["MaxSpotLights"] = maxSpot.ToString();
        return new FieldEntity { Raw = new XEntity { Id = id, ModelName = "mesh" }, Flat = flat };
    }

    static FieldEntity Lightable(string id = "r", int maxPoint = 3, int maxSpot = 2) => Receiver(id, maxPoint, maxSpot, "Lightable");

    [Test]
    public void ReadsTheEntityValues()
    {
        var set = FieldLightSet.Read(new[]
        {
            Point(("Position", "1, 2, 3"), ("Dimmer", "1.8"), ("DiffuseColor", "1, 0.5, 0.25"), ("ConstantAttenuation", "0.1"),
                  ("QuadraticAttenuation", "5E-05"), ("Range", "500")),
            Spot(("OuterSpotAngle", "30"), ("InnerSpotAngle", "20"), ("SpotExponent", "3"), ("Rotation", "0, 90, 0")),
        });
        Assert.That(set.Lights, Has.Count.EqualTo(2));
        var p = set.Lights[0];
        Assert.That(p.Spot, Is.False);
        Assert.That(p.Position, Is.EqualTo(new Vec3(1, 2, 3)));
        Assert.That(p.Dimmer, Is.EqualTo(1.8f));
        Assert.That(p.Diffuse, Is.EqualTo(new[] { 1f, 0.5f, 0.25f }));
        Assert.That((p.Constant, p.Linear, p.Quadratic, p.Range, p.Priority), Is.EqualTo((0.1f, 0f, 5e-5f, 500f, 50f)));
        var s = set.Lights[1];
        Assert.That(s.Spot, Is.True);
        Assert.That(s.OuterCos, Is.EqualTo(MathF.Cos(30 * MathF.PI / 180)).Within(1e-6));
        Assert.That(s.InnerCos, Is.EqualTo(MathF.Cos(20 * MathF.PI / 180)).Within(1e-6));
        Assert.That(s.Exponent, Is.EqualTo(3f));
        // Rotation (0, 90, 0): +X turned 90 degrees about Y points down (-Z), Gamebryo's MakeYRotation(-angle)
        Assert.That(s.Direction.X, Is.EqualTo(0).Within(1e-9));
        Assert.That(s.Direction.Z, Is.EqualTo(-1).Within(1e-9));
    }

    [Test]
    public void ARangeThatMissesTheBoundLightsNothing()
    {
        var set = FieldLightSet.Read(new[] { Point(("Position", "0, 0, 0"), ("Range", "500")) });
        var r = Lightable();
        Assert.That(set.Unlit(r, new Vec3(600, 0, 0), 99), Is.EqualTo(1u));     // 600 >= 500 + 99
        Assert.That(set.Unlit(r, new Vec3(600, 0, 0), 101), Is.EqualTo(0u));    // the bound reaches into the range
    }

    /// <summary>02000077_in's first light has LightPriority 14.615386: (p / 100 * 8 - 4) = -2.83, so it lights only the
    /// receivers whose influence (intensity + share in range + share affected) is above 2.83, even inside its range.</summary>
    [Test]
    public void LowPriorityLightSkipsAWeakReceiverInRange()
    {
        (string, string)[] medium = { ("Range", "1500"), ("ConstantAttenuation", "0.05"), ("LinearAttenuation", "1E-05"), ("QuadraticAttenuation", "6E-06") };
        var low = Point(medium.Append(("LightPriority", "14.615386")).Append(("Position", "0, 0, 0")).ToArray());
        var normal = Point(medium.Append(("Position", "0, 0, 0")).ToArray());
        var set = FieldLightSet.Read(new[] { low, normal });
        var r = Lightable();
        Assert.That(set.Unlit(r, new Vec3(300, 0, 0), 200), Is.EqualTo(0u), "near: both");
        Assert.That(set.Unlit(r, new Vec3(1000, 0, 0), 200), Is.EqualTo(1u), "in range but weak: only the default-priority light");
        Assert.That(FieldLightSet.PriorityFor(set.Lights[0], "r", new Vec3(1000, 0, 0), 200), Is.LessThan(0f));
    }

    /// <summary>The intensity term of the priority uses the linear attenuation twice (LightService.cpp:1166-1169): a light
    /// with only a quadratic term ranks as if it had none.</summary>
    [Test]
    public void PriorityIgnoresTheQuadraticTerm()
    {
        var quad = FieldLightSet.Read(new[] { Point(("Position", "0, 0, 0"), ("ConstantAttenuation", "1"), ("QuadraticAttenuation", "1")) }).Lights[0];
        float p = FieldLightSet.PriorityFor(quad, "r", new Vec3(5000, 0, 0), 10);
        // intensity 1 (1 / 1), in range 1 - 4990/10000, affected (10000 + 10 - 5000) / 20 -> 1: 0 + 1 + 0.501 + 1
        Assert.That(p, Is.EqualTo(2.501f).Within(1e-3));
    }

    /// <summary>Equal priorities come out in std::priority_queue's heap order, not the push order: pushing four equal
    /// entries into an MSVC max-heap pops 0, 2, 1, 3 (hand-traced push_heap / pop_heap).</summary>
    [Test]
    public void EqualPrioritiesPopInHeapOrder()
    {
        var eq = Enumerable.Range(0, 4).Select(i => (1f, i)).ToList();
        Assert.That(FieldLightSet.PopOrder(eq).Select(e => e.index), Is.EqualTo(new[] { 0, 2, 1, 3 }));
        var mixed = new List<(float, int)> { (1f, 0), (3f, 1), (2f, 2), (3f, 3), (1f, 4) };
        var order = FieldLightSet.PopOrder(mixed).Select(e => e.priority).ToArray();
        Assert.That(order, Is.EqualTo(new[] { 3f, 3f, 2f, 1f, 1f }));
        var set = FieldLightSet.Read(Enumerable.Range(0, 4).Select(i => Point(("Position", "0, 0, 0"))).ToArray());
        // one point light allowed, four equal: the heap's first pop is light 0
        Assert.That(set.Unlit(Lightable(maxPoint: 1), new Vec3(0, 0, 0), 10), Is.EqualTo(0b1110u));
        Assert.That(set.Unlit(Lightable(maxPoint: 3), new Vec3(0, 0, 0), 10), Is.EqualTo(0b1000u), "pops 0, 2, 1: light 3 is left out");
    }

    [Test]
    public void TheMaskStaysWithinTheFloatMantissa()
    {
        // 30 lights far from the receiver: all unlit, but only the first 24 bits (a float holds them exactly)
        var set = FieldLightSet.Read(Enumerable.Range(0, 30).Select(i => Point(("Position", "0, 0, 0"), ("Range", "10"))).ToArray());
        uint mask = set.Unlit(Lightable(), new Vec3(5000, 0, 0), 1);
        Assert.That(mask, Is.EqualTo(0xFFFFFFu));
        Assert.That((uint)(float)mask, Is.EqualTo(mask));
    }

    [Test]
    public void LimitsPerTypeByPriority()
    {
        var spots = Enumerable.Range(0, 3).Select(i => Spot(("Position", $"{i * 1000}, 0, 0"))).ToArray();
        var points = Enumerable.Range(0, 4).Select(i => Point(("Position", $"{i * 1000}, 0, 0"))).ToArray();
        var set = FieldLightSet.Read(spots.Concat(points));
        // at x = 0: the nearer lights rank higher; the farthest spot (bit 2) and point (bit 6) are left out
        Assert.That(set.Unlit(Lightable(), new Vec3(0, 0, 0), 10), Is.EqualTo((1u << 2) | (1u << 6)));
        Assert.That(set.Unlit(Lightable(maxPoint: 0, maxSpot: 1), new Vec3(0, 0, 0), 10), Is.EqualTo(0b1111110u));
    }

    [Test]
    public void OnlyLightableReceiversAreRelit()
    {
        var set = FieldLightSet.Read(new[] { Point(("Position", "0, 0, 0"), ("Range", "10")) });
        var far = new Vec3(100000, 0, 0);
        Assert.That(set.Unlit(Lightable(), far, 1), Is.EqualTo(1u));
        // not Lightable, or ExceptLightService: not a receiver, keeps the light CreateQueuedLights attached
        Assert.That(set.Unlit(Receiver(), far, 1), Is.EqualTo(0u));
        Assert.That(set.Unlit(Receiver("r", 3, 2, "Lightable", "ExceptLightService"), far, 1), Is.EqualTo(0u));
        // a receiver without MaxPointLights takes none
        var none = Lightable();
        none.Flat.Props.Remove("MaxPointLights");
        Assert.That(set.Unlit(none, new Vec3(0, 0, 0), 1), Is.EqualTo(1u));
    }

    [Test]
    public void PriorityOverrides()
    {
        var always = Point(("Position", "0, 0, 0"), ("Range", "10"));
        always.Raw.Sets["AlwaysAffectedByLight"] = new() { ("1", "urn:llid:0000abcd-0000-0000-0000-000000000000") };
        var never = Point(("Position", "0, 0, 0"));
        never.Raw.Sets["NeverAffectedByLight"] = new() { ("1", "0000abcd000000000000000000000000") };
        var set = FieldLightSet.Read(new[] { always, never });
        var r = Lightable("0000ABCD000000000000000000000000");
        Assert.That(set.Unlit(r, new Vec3(100000, 0, 0), 1), Is.EqualTo(2u), "always: lit out of range");
        Assert.That(set.Unlit(r, new Vec3(5, 0, 0), 1), Is.EqualTo(2u), "never: in range, but priority -4 + influence <= 0");
        Assert.That(set.Unlit(Lightable("other"), new Vec3(5, 0, 0), 1), Is.EqualTo(0u));
    }

    /// <summary>MS2SpotLight carries the PointLight mixin, so UpdateRelightingMode counts spots as point lights: 3 spots do
    /// not turn automatic relighting on, 4 do.</summary>
    [Test]
    public void SpotsCountAsPointLightsForRelighting()
    {
        Assert.That(FieldLightSet.Read(Enumerable.Range(0, 3).Select(_ => Spot())).AutomaticRelighting, Is.False);
        Assert.That(FieldLightSet.Read(Enumerable.Range(0, 4).Select(_ => Spot())).AutomaticRelighting, Is.True);
    }

    static FieldMap Map(string xblock)
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new Ms2.Formats.Pack.PackSet(data);
        return FieldMap.Load(packs, new FlatLibrary(packs), xblock);
    }

    /// <summary>02000012_in: a point light and two spots, with their xblock values over the MS2PointLight / MS2SpotLight flat
    /// defaults.</summary>
    [Test]
    public void KnownIndoorMap()
    {
        var set = FieldLightSet.Read(Map("02000012_in").Entities);
        Assert.That(set.Lights.Select(l => l.Spot), Is.EqualTo(new[] { false, true, true }));
        Assert.That(set.AutomaticRelighting, Is.False);
        var p = set.Lights[0];
        Assert.That(p.Position.X, Is.EqualTo(-352.160034).Within(1e-4));
        Assert.That(p.Position.Z, Is.EqualTo(1747.97009).Within(1e-4));
        Assert.That((p.Range, p.Dimmer, p.Constant, p.Linear, p.Quadratic, p.Priority), Is.EqualTo((500f, 1.8f, 0.1f, 0f, 5e-5f, 50f)));
        Assert.That(p.Diffuse, Is.EqualTo(new[] { 1f, 0.9215686f, 0.427451f }));
        Assert.That(p.Ambient, Is.EqualTo(new[] { 0f, 0f, 0f }));

        var s1 = set.Lights[1];
        Assert.That((s1.Range, s1.Dimmer, s1.Exponent, s1.Constant, s1.Quadratic), Is.EqualTo((2000f, 1.8f, 3f, 0.1f, 1e-6f)));
        Assert.That(s1.OuterCos, Is.EqualTo(MathF.Cos(30 * MathF.PI / 180)).Within(1e-6));
        Assert.That(s1.InnerCos, Is.EqualTo(MathF.Cos(20 * MathF.PI / 180)).Within(1e-6));
        // Rotation (323.380859, 0, 90): Rx Ry Rz, column 0 = (0, cos 36.62, -sin 36.62): down and towards +Y
        Assert.That(s1.Direction.X, Is.EqualTo(0).Within(1e-6));
        Assert.That(s1.Direction.Y, Is.EqualTo(0.8026).Within(1e-4));
        Assert.That(s1.Direction.Z, Is.EqualTo(-0.5965).Within(1e-4));
        var s2 = set.Lights[2];
        Assert.That(s2.OuterCos, Is.EqualTo(MathF.Cos(35 * MathF.PI / 180)).Within(1e-6));
        Assert.That(s2.Direction.X, Is.EqualTo(0.7201).Within(1e-4));
        Assert.That(s2.Direction.Z, Is.EqualTo(-0.6938).Within(1e-4));
    }

    /// <summary>02000498_bf: three spots. They do not turn automatic relighting on (counted as point lights), and a receiver
    /// that all three reach takes two (MaxSpotLights 2 of the mesh flats), leaving out the one with the lowest priority.</summary>
    [Test]
    public void ThreeSpotsTwoPerReceiver()
    {
        var map = Map("02000498_bf");
        var set = FieldLightSet.Read(map.Entities);
        Assert.That(set.Lights.Count(l => l.Spot), Is.EqualTo(3));
        Assert.That(set.AutomaticRelighting, Is.False);
        var mesh = map.Entities.First(e => FieldLightSet.Receives(e) && e.HasMixin("Mesh") && e.Number("MaxSpotLights") == 2);
        var at = new Vec3(-900, -3000, 11000);
        Assert.That(set.Lights.All(l => FieldLightSet.AffectsReceiver(l, at, 3000)), Is.True);
        Assert.That(set.Unlit(mesh, at, 3000), Is.EqualTo(1u));   // the spot at y -9675, farthest from the bound
    }
}
