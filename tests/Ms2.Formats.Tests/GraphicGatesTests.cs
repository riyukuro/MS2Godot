using Ms2.Formats.Options;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>IsHdrEnabled @0x14125e0b0, IsOuterGlowEnabled @0x14125e160 and the HDR light scale (ScaleLightIntensity
/// @0x1414c0820 / HandleEntityEnterWorld @0x1414c08e0).</summary>
public class GraphicGatesTests
{
    static GameOption.Graphic Graphic(bool hdr, bool glow) => new() { DisplayHdr = hdr, OuterGlow = glow };

    [Test]
    public void HdrFollowsTheOptionUnlessTheFieldTurnsItOff()
    {
        var field = new FieldData();
        Assert.That(field.Hdr.Enabled, Is.True);                                   // fielddatacli.cpp:75
        Assert.That(GraphicGates.IsHdrEnabled(field, Graphic(true, true)), Is.True);
        Assert.That(GraphicGates.IsHdrEnabled(field, Graphic(false, true)), Is.False);
        Assert.That(GraphicGates.IsHdrEnabled(null, Graphic(true, false)), Is.True); // no field loaded: the option
        Assert.That(GraphicGates.IsHdrEnabled(null, Graphic(false, false)), Is.False);
        field.Hdr.Enabled = false;
        Assert.That(GraphicGates.IsHdrEnabled(field, Graphic(true, true)), Is.False);
    }

    [Test]
    public void OuterGlowIsOffInAFieldAndFollowsTheOptionWithout()
    {
        Assert.That(new GameOption.Graphic().OuterGlow, Is.True);                  // m_b1e reset, gameoptionstruct.cpp:1928
        var field = new FieldData();
        Assert.That(field.OuterGlow, Is.False);                                    // +0x1fc, fielddatacli.cpp:72
        Assert.That(GraphicGates.IsOuterGlowEnabled(field, Graphic(false, true)), Is.False);
        Assert.That(GraphicGates.IsOuterGlowEnabled(field, Graphic(false, false)), Is.False);
        Assert.That(GraphicGates.IsOuterGlowEnabled(null, Graphic(false, true)), Is.True);
        Assert.That(GraphicGates.IsOuterGlowEnabled(null, Graphic(false, false)), Is.False);
        field.OuterGlow = true;                                                    // the dev dialog's SetGlowEnabled
        Assert.That(GraphicGates.IsOuterGlowEnabled(field, Graphic(false, true)), Is.True);
        Assert.That(GraphicGates.IsOuterGlowEnabled(field, Graphic(false, false)), Is.False);
    }

    [Test]
    public void LightsAreScaledUpWithHdrOff()
    {
        Assert.That(GraphicGates.LightDimmerScale(true), Is.EqualTo(1.0f));
        Assert.That(GraphicGates.LightDimmerScale(false), Is.EqualTo(1.1f));
    }

    /// <summary>The option file does not carry the outer-glow switch: a load keeps the reset value and a save writes none.</summary>
    [Test]
    public void OuterGlowIsNotInTheOptionFile()
    {
        var d = System.Xml.Linq.XDocument.Parse("<ms2><graphic displayHdr=\"true\" outerGlow=\"false\" /></ms2>");
        var g = new GameOption.Graphic();
        g.Load(d);
        Assert.That(g.DisplayHdr, Is.True);
        Assert.That(g.OuterGlow, Is.True);
    }

    /// <summary>Every field of the table: HDR follows the option (no field turns m_bHdr off), outer glow is off.</summary>
    [Test]
    public void EveryFieldKeepsHdrAndHasNoOuterGlow()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new PackSet(data);
        int n = 0, withHdr = 0;
        foreach (var fd in FieldData.Document(packs).Root.Elements("fieldData"))
        {
            var f = FieldData.FindById(packs, (int)fd.Attribute("id"));
            if (f == null) continue;
            n++;
            if (FieldData.DefaultEnvironment(fd)?.Element("hdr") != null) withHdr++;
            Assert.That(GraphicGates.IsHdrEnabled(f, Graphic(true, true)), Is.True, "field " + f.Id);
            Assert.That(GraphicGates.IsHdrEnabled(f, Graphic(false, true)), Is.False, "field " + f.Id);
            Assert.That(GraphicGates.IsOuterGlowEnabled(f, Graphic(false, true)), Is.False, "field " + f.Id);
        }
        Assert.That(n, Is.GreaterThan(0));
        TestContext.WriteLine($"fields {n}, with <hdr> {withHdr}");
    }
}
