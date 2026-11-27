using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The client's fog-off walk on effect nifs: PrepareLoadedNif @0x1415672e0 runs FUN_141567b60 on every loaded nif, effects
/// too (CNifLoadingService::CreateCachedNif, nifeffectparam.cpp:2338), and the walk tests a particle system as any object
/// (unit_141567000.cpp:349-424). A particle system's material (ParticleSystemDef.Material.Mesh.NoFog) carries the result,
/// as an effect mesh's ModelMesh.NoFog does.
/// </summary>
public class EffectFogOffTests
{
    const string Archive = "Resource/Model/Effect";

    static EffectScene Load(string file)
    {
        var (packs, spec) = NifEnv.Open();
        var entry = packs.List(Archive).Single(e => e.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase));
        return EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, entry)));
    }

    static bool NoFog(EffectScene sc, string system) => sc.Systems.Single(s => s.Name == system).Material.Mesh.NoFog;

    /// <summary>The recovery pillar: the client draws all four systems without the depth fog (capture 02000110, draws
    /// 99-102), the additive bling ones and the blended PArray01 and leaf (dest INVSRCALPHA, untagged, no 0x400 flag in the
    /// file) alike, as NiPSParticleSystem::LoadBinary sets flag 0x400 on every particle system (NiPSParticleSystem.cpp:1282);
    /// its meshes keep the fog.</summary>
    [Test]
    public void EveryParticleSystemIsFogOff()
    {
        var sc = Load("eff_com_recovery.nif");
        Assert.That(NoFog(sc, "bling"), Is.True, "bling, dest ONE");
        Assert.That(NoFog(sc, "bling01"), Is.True, "bling01, dest ONE");
        Assert.That(NoFog(sc, "PArray01"), Is.True, "PArray01, dest INVSRCALPHA");
        Assert.That(NoFog(sc, "leaf"), Is.True, "leaf, dest INVSRCALPHA");
        Assert.That(sc.Meshes.Any(m => !m.Mesh.NoFog), Is.True, "the ring and column meshes keep the fog");
    }

    /// <summary>The darkness keep effect: its PArray01 carries NiAVObject flag 0x400 in the file too (FUN_14098dfc0).</summary>
    [Test]
    public void Flag0x400ParticleSystemIsFogOff() =>
        Assert.That(NoFog(Load("eff_additional_darkness_keep.nif"), "PArray01"), Is.True);
}
