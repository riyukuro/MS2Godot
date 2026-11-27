using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

[TestFixture]
public class UvHoldTests
{
    /// <summary>
    /// The beaker's two held-UV meshes ("Editable Poly", each base map with an active CLAMP translate-V controller, keys
    /// 0:-1.2 and 1.333:1.1) are the master particles of its mesh-particle systems PCloud02 and PCloud03: they hang under no
    /// node in the file and are drawn only as each live particle's clone (NiPSMeshParticleSystem::InitializeParticle,
    /// NiPSMeshParticleSystem.cpp:229-300). ModelData leaves them out (ModelData.MasterParticleBlocks); EffectScene, which
    /// the field runs a model's particle systems through (FieldBuilder.ParticlesOf), keeps them, marked Master, with the
    /// controller on the base map.
    /// </summary>
    [Test]
    public void BeakerHeldMeshesAreMeshParticleMasters()
    {
        var (packs, spec) = NifEnv.Open();
        var nif = NifFile.Load(spec, packs.Read("Resource/Model/Map", "ellinia/indoor/el_in_prop_beaker_a01.nif"));
        var masters = ModelData.MasterParticleBlocks(nif);
        Assert.That(nif.Blocks.Where(b => b.Type == "NiPSMeshParticleSystem").Select(b => b.Ints("Master Particles").Count()), Is.EqualTo(new[] { 1, 1 }));

        // the static model: no master mesh, so no hold
        var md = ModelData.Load(nif);
        Assert.That(md.Meshes, Is.Not.Empty);
        Assert.That(md.Meshes.Select(m => m.Block), Has.None.Matches<int>(masters.Contains));
        Assert.That(md.Meshes.SelectMany(m => m.UvHolds), Is.Empty);
        Assert.That(md.Meshes.SelectMany(m => m.UvAnims), Is.Empty);

        // the masters, as the clones draw them
        var sc = EffectScene.Load(nif);
        var held = sc.Meshes.Where(em => em.Mesh.UvHolds.Count > 0).ToList();
        Assert.That(held, Has.Count.EqualTo(2));
        foreach (var em in held)
        {
            Assert.That(em.Master, Is.True);
            Assert.That(masters, Does.Contain(em.Mesh.Block));
            var h = em.Mesh.UvHolds.Single();
            Assert.That(h.Slot, Is.EqualTo("base"));
            Assert.That(h.Operation, Is.EqualTo(1));
            Assert.That(h.Values[^1], Is.EqualTo(1.1f).Within(1e-5));
            Assert.That(em.Mesh.Maps["base"].Transform.TV, Is.EqualTo(-1.2f).Within(1e-5), "the file value");
            // held on the transform a clone's base map draws with once the controller is past its stop
            var drawn = em.Mesh.DrawnTransform("base", false);
            Assert.That(drawn.TV, Is.EqualTo(1.1f).Within(1e-5));
            Assert.That(drawn.TU, Is.EqualTo(em.Mesh.Maps["base"].Transform.TU), "only translate-V is held");
            // a clone runs the controller at its particle's age: started at 0 (StartAnimations(clone, 0),
            // NiPSMeshParticleSystem.cpp:268), updated at the age (UpdateDownwardPass, :430); EffectInstance.AnimateUv
            // sets op.Curve(op.Ctl.Local(age)) on the base map
            var op = em.Uv.Single();
            Assert.That((op.Slot, op.Operation, op.Ctl.Cycle), Is.EqualTo(("base", 1, 2)));
            Assert.That(op.Curve(op.Ctl.Local(0)), Is.EqualTo(-1.2).Within(1e-5), "a new clone starts at the first key");
            Assert.That(op.Curve(op.Ctl.Local(op.Ctl.Stop)), Is.EqualTo(1.1).Within(1e-5));
            Assert.That(op.Ctl.Stop, Is.EqualTo(1.333).Within(1e-3));
            foreach (double age in new[] { 2.0, 10.0, 100.0 })
                Assert.That(op.Curve(op.Ctl.Local(age)), Is.EqualTo(drawn.TV).Within(1e-5), "past its stop a clamp controller holds its last key");
        }
        Assert.That(sc.Systems.Where(d => d.MeshParticles).SelectMany(d => d.MasterMeshes.SelectMany(g => g)).Select(i => sc.Meshes[i]),
            Is.EquivalentTo(held));
    }

    /// <summary>
    /// A field model's own (non-master) held map: each of the Kritias picture decal's meshes has an active CLAMP
    /// translate-U controller on its detail map, last key 0.69. A field model's app time is past its stop on field entry, so
    /// it is a hold at the last key (UvHolds), not a running animation (UvAnims), and the field shader is given that key
    /// (ModelBuilder.SetUvTransforms) while the map's own transform keeps the file value.
    /// </summary>
    [Test]
    public void FieldModelClampHoldsLastKey()
    {
        var (packs, spec) = NifEnv.Open();
        var md = ModelData.Load(NifFile.Load(spec, packs.Read("Resource/Model/Map", "kritias/indoor/ks_in_decal_picture_b01.nif")));
        var held = md.Meshes.Where(m => m.UvHolds.Count > 0).ToList();
        Assert.That(held, Has.Count.EqualTo(11));
        Assert.That(md.Meshes.SelectMany(m => m.UvAnims), Is.Empty);
        foreach (var m in held)
        {
            var h = m.UvHolds.Single();
            Assert.That((h.Slot, h.Operation), Is.EqualTo(("detail", 0)));
            Assert.That(h.Values[^1], Is.EqualTo(0.69f).Within(1e-5));
            var file = m.Maps["detail"].Transform?.TU;
            Assert.That(m.DrawnTransform("detail", false).TU, Is.EqualTo(0.69f).Within(1e-5));
            Assert.That(m.Maps["detail"].Transform?.TU, Is.EqualTo(file), "the map's own transform is not changed");
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
