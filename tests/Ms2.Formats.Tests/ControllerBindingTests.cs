using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// How a particle controller finds what it drives: by name, the first match only.
/// - Client: NiPSEmitterCtlr binds GetEmitterByName, the first emitter with its name (NiPSEmitterCtlr::PostLinkObject,
///   NiPSEmitterCtlr.cpp:119-125; NiPSParticleSystem.cpp:285-299), and an emit controller with none does nothing
///   (NiPSEmitParticlesCtlr::Update, NiPSEmitParticlesCtlr.cpp:81). NiPSForceCtlr binds GetForceByName, the first force
///   with its name (NiPSForceCtlr.cpp:100-118; NiPSSimulatorForcesStep.inl:29-43); a gravity strength controller takes
///   it only when that force is a gravity force (NiPSGravityStrengthCtlr.cpp:59-62).
/// - Here: EffectScene binds the controllers the same way.
/// - The shipped effects name every emitter and force once per system, so these cases come from an effect edited after
///   reading: a second emitter or force given the name, or a name that matches none.
/// </summary>
public class ControllerBindingTests
{
    const string Archive = "Resource/Model/Effect";

    static NifFile Read(string entry)
    {
        var (packs, spec) = NifEnv.Open();
        return NifFile.Load(spec, packs.Read(Archive, entry));
    }

    static int First(NifFile n, string type)
    {
        for (int i = 0; i < n.Blocks.Count; i++) if (n.Blocks[i].Type == type) return i;
        Assert.Fail($"no {type}");
        return -1;
    }

    static ParticleSystemDef System(EffectScene sc, string name)
    {
        var all = sc.Systems.Where(s => s.Name == name).ToList();
        Assert.That(all, Has.Count.EqualTo(1), name);
        return all[0];
    }

    /// <summary>The forces of the system a controller targets, in the forces step's order.</summary>
    static long[] ForceRefs(NifFile n, int ctl)
    {
        var ps = n.Blocks[(int)n.Blocks[ctl].Int("Target")];
        foreach (var st in n.Blocks[(int)ps.Int("Simulator")].Ints("Simulation Steps"))
            if (n.Blocks[(int)st].Type == "NiPSSimulatorForcesStep") return n.Blocks[(int)st].Ints("Forces");
        Assert.Fail("no forces step");
        return null;
    }

    [Test]
    public void AnEmitControllerWithNoEmitterOfItsNameEmitsNothing()
    {
        var n = Read("additional/burn/eff_additional_burn_invoke_01.nif");
        int c = First(n, "NiPSEmitParticlesCtlr");
        string sys = n.Name((int)n.Blocks[c].Int("Target"));
        var before = System(EffectScene.Load(n), sys);
        Assert.That(before.Emitters, Has.Count.EqualTo(1));
        Assert.That(before.Emitters[0].Rate, Is.Not.Null);
        // the controller now names the system, which no emitter is called
        n.Blocks[c]["Emitter Name"] = (long)n.Strings.IndexOf(sys);
        Assert.That(n.Str(n.Blocks[c].Int("Emitter Name")), Is.EqualTo(sys).And.Not.EqualTo(before.Emitters[0].Name));
        var after = System(EffectScene.Load(n), sys);
        Assert.That(after.Emitters[0].Rate, Is.Null);
        Assert.That(after.Emitters[0].On, Is.Null);
    }

    [Test]
    public void AForceActiveControllerDrivesTheFirstForceOfItsName()
    {
        var n = Read("bg/common/eff_co_torchlight_big_01.nif");
        int c = First(n, "NiPSForceActiveCtlr");
        string sys = n.Name((int)n.Blocks[c].Int("Target"));
        var refs = ForceRefs(n, c);
        Assert.That(refs, Has.Length.GreaterThanOrEqualTo(2));
        var before = System(EffectScene.Load(n), sys);
        string fn = n.Str(n.Blocks[c].Int("Force Name"));
        int k = before.Forces.FindIndex(f => f.Name == fn);
        // another force, one no controller drives, given the controlled force's name after it
        int j = before.Forces.FindIndex(k + 1, f => f.OnCtl == null);
        Assert.That(k, Is.GreaterThanOrEqualTo(0));
        Assert.That(j, Is.GreaterThan(k));
        n.Blocks[(int)refs[j]]["Name"] = n.Blocks[c]["Force Name"];
        var after = System(EffectScene.Load(n), sys);
        Assert.That(after.Forces[k].OnCtl, Is.Not.Null);
        Assert.That(after.Forces[j].Name, Is.EqualTo(fn));
        Assert.That(after.Forces[j].OnCtl, Is.Null);
    }

    [Test]
    public void AGravityStrengthControllerWhoseFirstForceOfTheNameIsNoGravityDrivesNothing()
    {
        var n = Read("bg/perform/eff_perform_halloweenbase_a01_play.nif");
        int c = First(n, "NiPSGravityStrengthCtlr");
        string sys = n.Name((int)n.Blocks[c].Int("Target"));
        var refs = ForceRefs(n, c);
        var before = System(EffectScene.Load(n), sys);
        string fn = n.Str(n.Blocks[c].Int("Force Name"));
        int k = before.Forces.FindIndex(f => f.Name == fn);
        int j = before.Forces.FindIndex(f => f.Kind != ForceKind.Gravity);
        Assert.That(k, Is.GreaterThanOrEqualTo(0));
        Assert.That(before.Forces[k].StrengthAnim, Is.Not.Null);
        Assert.That(j, Is.GreaterThanOrEqualTo(0));
        // a force that is no gravity force, put first in the step and given the gravity force's name
        (refs[0], refs[j]) = (refs[j], refs[0]);
        n.Blocks[(int)refs[0]]["Name"] = n.Blocks[c]["Force Name"];
        var after = System(EffectScene.Load(n), sys);
        Assert.That(after.Forces[0].Kind, Is.Not.EqualTo(ForceKind.Gravity));
        Assert.That(after.Forces[0].Name, Is.EqualTo(fn));
        Assert.That(after.Forces.Where(f => f.Name == fn && f.Kind == ForceKind.Gravity), Is.Not.Empty);
        Assert.That(after.Forces.Select(f => f.StrengthAnim), Is.All.Null);
    }
}
