using System.Xml.Linq;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The chaoslight part (<see cref="EffectChaosLightParam"/>, ChaosLightEffectParam::vfunc_1) and its clock
/// (<see cref="ChaosLightRun"/>, ChaosLightEffect on TransitionEffect), worked by hand from the client's code.</summary>
public class ChaosLightTests
{
    static EffectPart Part(string effect) =>
        EffectTable.Parse(XDocument.Parse("<ms2><effects>" + effect + "</effects></ms2>"))[0];

    [Test]
    public void ThePartReadsItsValuesAndKeysFromThePreviousKey()
    {
        var p = Part("<effect type=\"chaoslight\" startDelay=\"1\" duration=\"5\" layer=\"99\" node=\"Bip01\" fresnelBoost=\"10\""
            + " dimmer=\"0.5\" rgb=\"1.0, 0.0, 0.25\" blink=\"true\" blinkBoostStart=\"2\">"
            + "<transition type=\"easeineaseout\" duration=\"2\" fresnelBoost=\"20\"/>"
            + "<transition type=\"FastInEaseOut\" duration=\"3\" rgb=\"0,1,0\" dimmer=\"1\"/>"
            + "<transition duration=\"x\"/></effect>");
        Assert.That(p.TypeId, Is.EqualTo(EffectSkipTable.TypeChaosLight));
        var c = p.ChaosLight;
        Assert.That(c, Is.Not.Null);
        Assert.That((c.Node, c.Layer, c.Blink, c.BlinkBoostStart), Is.EqualTo(("Bip01", 99, true, 2)));
        Assert.That(c.Values, Is.EqualTo(new ChaosLightValues { Boost = 10, Exponent = 5.36f, Dimmer = 0.5f, R = 1, G = 0, B = 0.25f }),
            "fresnelExponent keeps the constructor's 5.36");
        Assert.That(c.Keys.Length, Is.EqualTo(3), "the param's own values are not a key");
        Assert.That((c.Keys[0].Interpolation, c.Keys[0].Start, c.Keys[0].Duration), Is.EqualTo((3, 1f, 2f)), "the times count from the start delay");
        Assert.That(c.Keys[0].Values, Is.EqualTo(new ChaosLightValues { Boost = 20, Exponent = 5.36f, Dimmer = 0.5f, R = 1, G = 0, B = 0.25f }));
        Assert.That((c.Keys[1].Interpolation, c.Keys[1].Start, c.Keys[1].Duration), Is.EqualTo((1, 3f, 3f)));
        Assert.That(c.Keys[1].Values, Is.EqualTo(new ChaosLightValues { Boost = 20, Exponent = 5.36f, Dimmer = 1, R = 0, G = 1, B = 0 }));
        Assert.That((c.Keys[2].Interpolation, c.Keys[2].Start, c.Keys[2].Duration), Is.EqualTo((0, 6f, 0f)), "a bad duration keeps 0");
        Assert.That(c.Keys[2].Values, Is.EqualTo(c.Keys[1].Values));
        Assert.That(c.LoopIndex, Is.EqualTo(-1));

        var d = Part("<effect type=\"chaoslight\"/>").ChaosLight;
        Assert.That(d.Values, Is.EqualTo(new ChaosLightValues { Boost = 26.09f, Exponent = 5.36f, Dimmer = 0, R = 0.95f, G = 0.57f, B = 0.22f }));
        Assert.That((d.Keys.Length, d.LoopIndex, d.Layer, d.Blink), Is.EqualTo((0, -1, 0, false)));
    }

    [Test]
    public void OnlyALoopOfTrueWithAKeyLoopsTheKeys()
    {
        Assert.That(Part("<effect type=\"chaoslight\" loop=\"TRUE\"><transition duration=\"1\"/></effect>").ChaosLight.LoopIndex, Is.EqualTo(0));
        Assert.That(Part("<effect type=\"chaoslight\" loop=\"true\"/>").ChaosLight.LoopIndex, Is.EqualTo(-1), "no key, no loop index");
        var one = Part("<effect type=\"chaoslight\" loop=\"1\"><transition duration=\"1\"/></effect>");
        Assert.That(one.Loop, Is.True, "the part loops (the bool parse)");
        Assert.That(one.ChaosLight.LoopIndex, Is.EqualTo(-1), "but the keys do not: the loop index wants \"true\"");
    }

    [Test]
    public void TheEasesClampAndCurve()
    {
        Assert.That(ChaosLightRun.Ease(0, -1f), Is.EqualTo(0f));
        Assert.That(ChaosLightRun.Ease(0, 0.3f), Is.EqualTo(0.3f));
        Assert.That(ChaosLightRun.Ease(0, 2f), Is.EqualTo(1f));
        Assert.That(ChaosLightRun.Ease(1, 0.5f), Is.EqualTo(0.8660254f).Within(1e-6f), "sqrt(1 - 0.25)");
        Assert.That(ChaosLightRun.Ease(2, 0.5f), Is.EqualTo(0.1339746f).Within(1e-6f), "1 - sqrt(1 - 0.25)");
        Assert.That(ChaosLightRun.Ease(3, 0.25f), Is.EqualTo(0.1464466f).Within(1e-6f), "sin(-pi/4)/2 + 1/2");
        Assert.That(ChaosLightRun.Ease(2, float.NaN), Is.EqualTo(0f));
        Assert.That(ChaosLightRun.Ease(3, 1.5f), Is.EqualTo(1f));
    }

    static EffectChaosLightParam Param(string attrs, string keys = "") =>
        Part("<effect type=\"chaoslight\" " + attrs + ">" + keys + "</effect>").ChaosLight;

    [Test]
    public void WithoutKeysTheLayersHoldThePartsValuesUntilTheEnd()
    {
        var c = Param("fresnelBoost=\"10\"");
        var r = new ChaosLightRun();
        Assert.That(r.Begin(c, 0f, 2f, false, 100), Is.True, "no delay: the layers go on at once");
        Assert.That(r.Layer, Is.EqualTo(c.Values));
        Assert.That(r.Tick(101, out bool now), Is.True);
        Assert.That(now, Is.False);
        Assert.That(r.Layer, Is.EqualTo(c.Values));
        Assert.That(r.Tick(102, out _), Is.True, "alive at the end");
        Assert.That(r.Tick(102.01, out _), Is.False, "over past it");
        var l = new ChaosLightRun();
        l.Begin(c, 0f, 2f, true, 100);
        Assert.That(l.Tick(500, out _), Is.True, "a looping part never ends by itself");
    }

    [Test]
    public void TheKeysCountTheStartDelayTwiceAndEaseByTheFromKey()
    {
        // delay 1: the start is 11; the keys start at 1 (2 s) and 3 (2 s) from there
        var c = Param("startDelay=\"1\" fresnelBoost=\"10\"",
            "<transition type=\"EaseInEaseOut\" duration=\"2\" fresnelBoost=\"20\"/><transition duration=\"2\" fresnelBoost=\"40\"/>");
        var r = new ChaosLightRun();
        Assert.That(r.Begin(c, 1f, 6f, false, 10), Is.False);
        Assert.That(r.Tick(10.5, out bool on), Is.True);
        Assert.That((on, r.Started), Is.EqualTo((false, false)));
        Assert.That(r.Current.Boost, Is.EqualTo(20f), "before the start t < 0: the first key");
        Assert.That(r.Layer.Boost, Is.EqualTo(10f), "no layer yet");
        Assert.That(r.Tick(11, out on), Is.True);
        Assert.That((on, r.Layer.Boost), Is.EqualTo((true, 20f)), "on at the start; elapsed 0 is before the first key at 1");
        r.Tick(12.5, out _);
        Assert.That(r.Layer.Boost, Is.EqualTo(20f + 0.1464466f * 20f).Within(1e-4f), "elapsed 1.5: t 0.25, the first key's sine");
        r.Tick(14, out _);
        Assert.That(r.Layer.Boost, Is.EqualTo(40f), "elapsed 3: the last key, held");
        r.Tick(15, out _);
        Assert.That(r.Layer.Boost, Is.EqualTo(40f));
        Assert.That(r.Tick(17.01, out _), Is.False, "over at 11 + 6");
    }

    [Test]
    public void ALoopWrapsFromTheLoopKeysStartAgainstUnshiftedKeyTimes()
    {
        string keys = "<transition duration=\"1\" fresnelBoost=\"20\"/><transition duration=\"1\" fresnelBoost=\"40\"/>";
        var c = Param("loop=\"true\" fresnelBoost=\"10\"", keys);
        var r = new ChaosLightRun();
        r.Begin(c, 0f, 1f, true, 0);
        r.Tick(0.5, out _);
        Assert.That(r.Layer.Boost, Is.EqualTo(30f), "the first key toward the second");
        r.Tick(1.5, out _);
        Assert.That(r.Layer.Boost, Is.EqualTo(30f), "the last key toward the loop key");
        r.Tick(2.25, out _);
        Assert.That(r.Layer.Boost, Is.EqualTo(25f), "2.25 wraps to 0.25");
        r.Tick(5.75, out _);
        Assert.That(r.Layer.Boost, Is.EqualTo(25f), "5.75 wraps to 1.75: t 0.75 from the last key (40) back to the loop key (20)");

        // delay 1: keys at 1..2 and 2..3, start 1; elapsed 3.5 wraps to 2.5, less the loop start 1 is 0.5, before the first key
        var d = Param("loop=\"true\" startDelay=\"1\" fresnelBoost=\"10\"", keys);
        var s = new ChaosLightRun();
        s.Begin(d, 1f, 1f, true, 0);
        s.Tick(1 + 3.5, out _);
        Assert.That(s.Layer.Boost, Is.EqualTo(20f));

        // one key looping: from and to are the same key
        var e = Param("loop=\"true\" fresnelBoost=\"10\"", "<transition duration=\"1\" fresnelBoost=\"20\"/>");
        var u = new ChaosLightRun();
        u.Begin(e, 0f, 1f, true, 0);
        u.Tick(7.3, out _);
        Assert.That(u.Layer.Boost, Is.EqualTo(20f));

        // a loop of length 0: t 1 from the last key to the loop key
        var z = Param("loop=\"true\"", "<transition duration=\"0\" fresnelBoost=\"20\"/><transition duration=\"0\" fresnelBoost=\"40\"/>");
        var w = new ChaosLightRun();
        w.Begin(z, 0f, 0f, true, 0);
        w.Tick(3, out _);
        Assert.That(w.Layer.Boost, Is.EqualTo(20f));
    }

    [Test]
    public void BlinkTogglesTheColourAndSpeedsUpPastItsBoostStart()
    {
        var c = Param("blink=\"true\" blinkBoostStart=\"1\" fresnelBoost=\"10\" rgb=\"1,0.5,0.25\"");
        var r = new ChaosLightRun();
        r.Begin(c, 0f, 10f, false, 100);
        r.Tick(100, out _);
        Assert.That((r.Layer.R, r.Layer.G, r.Layer.B), Is.EqualTo((1f, 0.5f, 0.25f)), "the last tick starts at 0: the first turns it on");
        r.Tick(100.2, out _);
        Assert.That(r.Layer.R, Is.EqualTo(1f), "phase 0.6");
        r.Tick(100.4, out _);
        Assert.That((r.Layer.R, r.Layer.G, r.Layer.B, r.Layer.Boost), Is.EqualTo((0f, 0f, 0f, 10f)), "phase 1.2: off, the rest kept");
        r.Tick(101, out _);
        Assert.That(r.Layer.R, Is.EqualTo(1f), "phase 1.8 (1 s is not past the boost start of 1): on");
        r.Tick(101.2, out _);
        Assert.That(r.Layer.R, Is.EqualTo(1f), "phase 0.2 * 3.2");
        r.Tick(101.32, out _);
        Assert.That(r.Layer.R, Is.EqualTo(0f), "0.64 + 0.12 * 3.32 > 1 (0.96 without the boost)");

        // with keys the keys' push comes after the blink's, so the colour stays
        var k = Param("blink=\"true\" rgb=\"1,1,1\"", "<transition duration=\"10\"/>");
        var s = new ChaosLightRun();
        s.Begin(k, 0f, 10f, false, 100);
        for (int i = 0; i < 20; i++) { s.Tick(100 + i * 0.2, out _); Assert.That(s.Layer.R, Is.EqualTo(1f)); }
    }

    [Test]
    public void TickingAllocatesNothing()
    {
        var c = Param("loop=\"true\" blink=\"true\" blinkBoostStart=\"1\"",
            "<transition type=\"EaseInFastOut\" duration=\"0.5\" fresnelBoost=\"20\"/><transition duration=\"0.7\" rgb=\"0,0,1\"/>");
        var r = new ChaosLightRun();
        r.Begin(c, 0.2f, 1f, true, 0);
        r.Tick(0.01, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) r.Tick(i / 60.0, out _);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }
    /// <summary>ChaosLightEffect::AddLayers skips a mesh whose glow map an NiTextureTransformController drives: the one such
    /// character mesh in the data (a fishing rod) reads as driven; the other glowing character meshes beside it do not.</summary>
    [Test]
    public void GlowMapDrivenMarksOnlyTheMeshWithAGlowController()
    {
        var (packs, spec) = NifEnv.Open();
        const string arch = "Resource/Model/Item";
        var rod = Ms2.Formats.Model.ModelData.Load(Ms2.Formats.Nif.NifFile.Load(spec, packs.Read(arch, "1/90/19000025_fishingrod025.nif")));
        var driven = rod.Meshes.Single(m => m.Name == "19000025_fishingRod01");
        Assert.That(driven.GlowMapDriven, Is.True, "the glow controller is read");
        Assert.That(driven.Textures.ContainsKey("glow"), Is.True);
        int others = 0;
        foreach (var entry in packs.List(arch).Where(e => e.StartsWith("1/90/") && e.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)).Take(80))
        {
            var md = entry == "1/90/19000025_fishingrod025.nif" ? rod : Ms2.Formats.Model.ModelData.Load(Ms2.Formats.Nif.NifFile.Load(spec, packs.Read(arch, entry)));
            foreach (var m in md.Meshes)
            {
                if (m == driven || !m.Textures.ContainsKey("glow")) continue;
                others++;
                Assert.That(m.GlowMapDriven, Is.False, $"{entry} {m.Name}");
            }
        }
        Assert.That(others, Is.GreaterThan(0), "other glowing meshes are seen");
    }
}
