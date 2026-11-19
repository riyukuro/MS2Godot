using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>Emitter directions follow NiPSEmitter::ComputeDirection: declination 0 is +Z, and the pre-RPI and RPI methods
/// differ.</summary>
public class EffectEmitterTests
{
    static void Near(Vec3 v, double x, double y, double z, string what)
    {
        Assert.That(v.X, Is.EqualTo(x).Within(1e-6), what + " x");
        Assert.That(v.Y, Is.EqualTo(y).Within(1e-6), what + " y");
        Assert.That(v.Z, Is.EqualTo(z).Within(1e-6), what + " z");
    }

    [Test]
    public void DirectionMethods()
    {
        foreach (var pre in new[] { true, false })
        {
            Near(EmitterDef.Direction(0, 1.2, pre), 0, 0, 1, $"dec 0 pre {pre}");
        }
        Near(EmitterDef.Direction(Math.PI, 0, true), 0, 0, -1, "pre-RPI dec pi");
        Near(EmitterDef.Direction(Math.PI / 2, 0, true), 1, 0, 0, "pre-RPI dec pi/2");
        Near(EmitterDef.Direction(Math.PI / 2, Math.PI / 2, true), 0, 1, 0, "pre-RPI dec pi/2 plan pi/2");
        Near(EmitterDef.Direction(Math.PI / 2, 0, false), 0, -1, 0, "RPI dec pi/2");
        Near(EmitterDef.Direction(0.3, Math.PI / 2, false), Math.Cos(0.3), -Math.Sin(0.3), 0, "RPI dec 0.3 plan pi/2");
    }

    /// <summary>The GrandHall portal's star system (bg/common/eff_com_portal_b.nif, Blizzard01) is pre-RPI with declination
    /// pi, so its particles leave the box emitter along the box's -Z, the normal of the width x height plane.</summary>
    [Test]
    public void PortalStarsLeaveAlongBoxNormal()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read("Resource/Model/Effect", "bg/common/eff_com_portal_b.nif")));
        var ps = sc.Systems.Single(s => s.Name == "Blizzard01");
        Assert.That(ps.PreRpi, Is.True);
        var e = ps.Emitters.Single();
        Assert.That(e.Kind, Is.EqualTo(EmitterKind.Box));
        Assert.That(e.Depth, Is.EqualTo(0).Within(1e-6));
        Assert.That(e.Width * e.Height, Is.GreaterThan(1000));
        Near(EmitterDef.Direction(e.Declination, e.PlanarAngle, ps.PreRpi), 0, 0, -1, "Blizzard01");
    }
}
