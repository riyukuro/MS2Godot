using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;
using V = System.Numerics.Vector3;

namespace Ms2.Formats.Tests;

/// <summary>
/// The particle colliders: an NiPSSimulatorCollidersStep's planar and spherical colliders (<see cref="ColliderDef"/>), run
/// after the forces by <see cref="ParticleColliders"/>.
/// </summary>
public class ParticleColliderTests
{
    const string Archive = "Resource/Model/Effect";

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryColliderIsRead(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var count = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Add(string k) => count[k] = count.GetValueOrDefault(k) + 1;
        var bad = new List<string>();
        var seen = new List<string>();
        foreach (var entry in packs.List(Archive).Where(e => e.EndsWith(".nif", StringComparison.Ordinal)).OrderBy(e => e, StringComparer.Ordinal))
        {
            NifFile nif; EffectScene sc;
            try { nif = NifFile.Load(spec, packs.Read(Archive, entry)); sc = EffectScene.Load(nif); }
            catch (Exception) { continue; }
            foreach (var s in sc.Systems)
            {
                if (s.Colliders == 0) continue;
                Add("systems with colliders");
                if (s.ColliderList.Count != s.Colliders) bad.Add($"{entry} {s.Name}: {s.Colliders} colliders, {s.ColliderList.Count} read");
                foreach (var c in s.ColliderList)
                {
                    string k = c.Sphere ? "sphere" : "plane";
                    if (c.ObjectBlock >= 0 && c.Node < 0) bad.Add($"{entry} {s.Name}: {k} object block {c.ObjectBlock} ({nif.Blocks[c.ObjectBlock].Type}) is not a scene node");
                    Add($"{k}{(c.Active ? "" : " inactive")}{(c.Node < 0 ? " no object" : "")}{(c.DieOnCollide ? " die" : "")}{(c.SpawnOnCollide ? " spawn" : "")} bounce {c.Bounce:0.##}");
                    if (seen.Count < 12) seen.Add($"{entry} {s.Name}: {k} node {(c.Node >= 0 ? sc.Nodes[c.Node].Name : "-")} w {c.Width} h {c.Height} r {c.Radius} x {c.XAxis.X},{c.XAxis.Y},{c.XAxis.Z} y {c.YAxis.X},{c.YAxis.Y},{c.YAxis.Z}");
                }
            }
        }
        foreach (var (k, c) in count) TestContext.Out.WriteLine($"{env}:   {k}: {c}");
        foreach (var l in seen) TestContext.Out.WriteLine($"{env}: {l}");
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
    }

    /// <summary>A collider whose object is an NiAVObject that is not a scene node here: the client still takes that object's
    /// world transform, so the load lists it as not read rather than quietly putting the collider at the system's origin.
    /// The lava cave effect's deflector is pointed at the system's simulator block, which is no scene node.</summary>
    [Test]
    public void AColliderObjectThatIsNoSceneNodeIsListed()
    {
        var dir = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(dir);
        var nif = NifFile.Load(new NifSpec(NifEnv.NifXml()), packs.Read(Archive, "bg/cave/eff_ca_lava_emit_01.nif"));
        var sc = EffectScene.Load(nif);
        Assert.That(sc.Unsupported.Where(u => u.Contains(" object ", StringComparison.Ordinal)), Is.Empty);
        var c = sc.Systems.SelectMany(s => s.ColliderList).First();
        Assert.That(c.Node, Is.GreaterThanOrEqualTo(0));
        Assert.That(sc.Nodes[c.Node].Name, Is.EqualTo("Deflector01"));
        int sim = nif.Blocks.FindIndex(b => b.Type == "NiPSSimulator");
        foreach (var b in nif.Blocks.Where(b => b.Type == "NiPSPlanarCollider")) b["Collider Object"] = (long)sim;
        sc = EffectScene.Load(nif);
        Assert.That(sc.Systems.SelectMany(s => s.ColliderList).Select(c => (c.ObjectBlock, c.Node)), Is.All.EqualTo((sim, -1)));
        Assert.That(sc.Unsupported, Does.Contain("NiPSPlanarCollider object NiPSSimulator"));
    }

    static readonly V X = new(1, 0, 0), Y = new(0, 1, 0);
    static ParticleColliders.Collider Floor(float w = 10, float h = 10, float bounce = 1, bool die = false, float z = 0, float scale = 1) =>
        ParticleColliders.Collider.Plane(new V(0, 0, z), X, Y, scale, w, h, bounce, die);

    static bool Run(ParticleColliders.Collider[] cs, ref V pos, ref V vel, out float tLast, float now = 1)
    {
        tLast = 0;
        return ParticleColliders.Step(cs, new int[cs.Length], ref pos, ref vel, ref tLast, now);
    }

    [Test]
    public void APlaneHitMovesToTheHitAndBouncesTheNormalPart()
    {
        var pos = new V(1, 2, 1); var vel = new V(1, 0, -2);
        Assert.That(Run(new[] { Floor(bounce: 0.5f) }, ref pos, ref vel, out var t), Is.False);
        Assert.That(t, Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(pos.X, Is.EqualTo(1.5f).Within(1e-6)); Assert.That(pos.Z, Is.EqualTo(0f).Within(1e-5));
        // v - 2(v.n)n, times the bounce
        Assert.That(vel, Is.EqualTo(new V(0.5f, 0, 1)));
    }

    [Test]
    public void APlaneIsHitOnlyInsideItsScaledWidthAndHeight()
    {
        var pos = new V(6, 0, 1); var vel = new V(0, 0, -2);
        Assert.That(Run(new[] { Floor() }, ref pos, ref vel, out var t), Is.False);
        Assert.That(t, Is.EqualTo(0f)); Assert.That(vel, Is.EqualTo(new V(0, 0, -2)));
        pos = new V(0, 4.9f, 1);
        Run(new[] { Floor() }, ref pos, ref vel, out t);
        Assert.That(t, Is.EqualTo(0.5f).Within(1e-6));
        // half width and height are scaled by the object's scale
        pos = new V(3, 0, 1); vel = new V(0, 0, -2);
        Run(new[] { Floor(scale: 0.5f) }, ref pos, ref vel, out t);
        Assert.That(t, Is.EqualTo(0f));
    }

    [Test]
    public void AParticleMovingAwayOrNotReachingThePlaneIsNotHit()
    {
        var pos = new V(0, 0, 1); var vel = new V(0, 0, 2);
        Run(new[] { Floor() }, ref pos, ref vel, out var t);
        Assert.That(t, Is.EqualTo(0f));
        vel = new V(0, 0, -0.5f);
        Run(new[] { Floor() }, ref pos, ref vel, out t);
        Assert.That(t, Is.EqualTo(0f)); Assert.That(pos, Is.EqualTo(new V(0, 0, 1)));
    }

    [Test]
    public void TheFirstHitEndsTheStepAsTheHitTimeIsNotReset()
    {
        // after bouncing off the floor at 0.25 the particle would reach the ceiling at 0.75, but the kernel keeps the
        // hit time of 0.25, so no later hit is found in the step
        var pos = new V(0, 0, 1); var vel = new V(0, 0, -4);
        Run(new[] { Floor(), Floor(z: 2) }, ref pos, ref vel, out var t);
        Assert.That(t, Is.EqualTo(0.25f).Within(1e-6));
        Assert.That(vel, Is.EqualTo(new V(0, 0, 4)));
        // the closer of two colliders is the one hit, whatever the order
        pos = new V(0, 0, 1); vel = new V(0, 0, -4);
        Run(new[] { Floor(z: -0.5f, bounce: 0), Floor(z: 0.5f) }, ref pos, ref vel, out t);
        Assert.That(t, Is.EqualTo(0.125f).Within(1e-6)); Assert.That(vel, Is.EqualTo(new V(0, 0, 4)));
    }

    [Test]
    public void ADieColliderKillsTheParticle()
    {
        var pos = new V(0, 0, 1); var vel = new V(0, 0, -4);
        Assert.That(Run(new[] { Floor(bounce: 0, die: true) }, ref pos, ref vel, out _), Is.True);
        pos = new V(8, 0, 1); vel = new V(0, 0, -4);
        Assert.That(Run(new[] { Floor(bounce: 0, die: true) }, ref pos, ref vel, out _), Is.False);
    }

    [Test]
    public void AParticleOnThePlaneAtTheStepsEndOnlyLosesItsNormalSpeed()
    {
        // the hit at now with a speed along the normal below the epsilon: the normal part is removed, no bounce
        var c = Floor(bounce: 0.5f);
        var v = c.Bounced(new V(3, 0, 0.00005f), new V(0, 0, 0), 1, 1);
        Assert.That(v.X, Is.EqualTo(3f)); Assert.That(v.Z, Is.EqualTo(0f).Within(1e-9));
        v = c.Bounced(new V(3, 0, -1), new V(0, 0, 0), 0.5f, 1);
        Assert.That(v, Is.EqualTo(new V(1.5f, 0, 0.5f)));
    }

    [Test]
    public void ASphereIsHitFromOutsideWhereTheLineEntersAndFromInsideWhereItLeaves()
    {
        var ball = ParticleColliders.Collider.Ball(new V(0, 0, 0), 2, 0.5f, 1, false);
        var pos = new V(0, 0, 3); var vel = new V(0, 0, -4);
        Run(new[] { ball }, ref pos, ref vel, out var t);
        Assert.That(t, Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(pos.Z, Is.EqualTo(1f).Within(1e-6)); Assert.That(vel.Z, Is.EqualTo(4f).Within(1e-6));
        pos = new V(0, 0, 0); vel = new V(0, 0, 2);
        Run(new[] { ball }, ref pos, ref vel, out t);
        Assert.That(t, Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(pos.Z, Is.EqualTo(1f).Within(1e-6)); Assert.That(vel.Z, Is.EqualTo(-2f).Within(1e-6));
        // passing by outside the radius
        pos = new V(1.5f, 0, 3); vel = new V(0, 0, -4);
        Run(new[] { ball }, ref pos, ref vel, out t);
        Assert.That(t, Is.EqualTo(0f));
    }

    [Test]
    public void TheStepAllocatesNothing()
    {
        var cs = new[] { Floor(), Floor(z: 2), ParticleColliders.Collider.Ball(new V(5, 5, 5), 1, 1, 0.5f, false) };
        var tried = new int[cs.Length];
        void One() { var p = new V(0, 0, 1); var v = new V(0, 0, -4); float t = 0; ParticleColliders.Step(cs, tried, ref p, ref v, ref t, 1); }
        // Warm up as long as the measured run, so the tier-up of Step and this loop happens before it (in Release
        // that tier-up allocates once, 24 B, which is not a per-step cost).
        long Run() { long b = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 1000; i++) One(); return GC.GetAllocatedBytesForCurrentThread() - b; }
        Run();
        Run();
        Assert.That(Run(), Is.EqualTo(0));
    }
}
