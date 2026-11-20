using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

[TestFixture]
public class ForceObjectTests
{
    /// <summary>
    /// The field snow (the weather sky object of fallType 1): its gravity axis is +Z in the space of Wind01, which is turned
    /// 180 degrees about Y, so the flakes fall down; its three drag axes are in the space of Drag01, turned 90 degrees about
    /// X, so the vertical drag is the 0.02 one, not 0.1. Gamebryo turns a force's axis by its object's rotation
    /// (NiPSGravityForce::Update, NiPSDragForce::Update).
    /// </summary>
    [Test]
    public void SnowForcesTakeTheirObjectsRotation()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read("Resource/Model/Effect", "bg/common/eff_cam_snow_soft.nif")));
        var forces = sc.Systems.Single().Forces;
        Vec3 Turn(ForceDef f) { var l = sc.Nodes[f.Node].Local; var p = l.Apply(f.Axis); return new Vec3(p.X - l.T.X, p.Y - l.T.Y, p.Z - l.T.Z); }

        var g = forces.Single(f => f.Kind == ForceKind.Gravity);
        Assert.That(sc.Nodes[g.Node].Name, Is.EqualTo("Wind01"));
        Assert.That(Turn(g).Z, Is.EqualTo(-1).Within(1e-4));

        var drags = forces.Where(f => f.Kind == ForceKind.Drag).ToList();
        Assert.That(drags, Has.Count.EqualTo(3));
        Assert.That(drags.Select(f => sc.Nodes[f.Node].Name), Is.All.EqualTo("Drag01"));
        Assert.That(drags.Select(f => f.Range), Is.All.EqualTo((double)float.MaxValue));
        var vertical = drags.Single(f => Math.Abs(Turn(f).Z) > 0.99);
        Assert.That(vertical.Percentage, Is.EqualTo(0.02).Within(1e-6));
    }
}
