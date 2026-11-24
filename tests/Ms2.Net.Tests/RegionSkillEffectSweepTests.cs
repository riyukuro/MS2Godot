using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Every region skill's effects, over every skill level in the data (KMS and GMS2): a level whose motion 0 (the motion
/// CRegionSkillObject takes, regionskillobject.cpp:91-99) names a motion effect, a splashDurationCastEffect, a
/// splashInvokeEffect, a splashEndEffect or a loopEffect is one row. Each row goes through the client's order for a local
/// owner: OnRegionSkill mode 0's Init (:105-135, the motion effect spawned at every point unless
/// splashCastEffectStartDelay; then PlayLateEffect :405-433, the duration effect at every point once the start tick has
/// come), OnRegionSkillDamage mode 5's PlayHitEffect (:224-252, the invoke effect at every point, every hit), and
/// RemoveRegionSkillObject (worldservice_1.cpp:4180-4189: Reset :255-285, the end effect at every point, then the motion
/// effects fade). The effect calls must be exactly those. The loop effect is STILL-OPEN (see
/// <see cref="RegionSkillObject"/>): its rows are counted, not played.
/// </summary>
public class RegionSkillEffectSweepTests
{
    sealed class Fx : IRegionSkillEffects
    {
        public readonly List<string> Log = new();
        public Func<string, bool> Exists;
        int next = 1;
        public bool Has(string effect) => Exists(effect);
        public int Spawn(string effect, Xform at, bool always, int randomIndex) { Log.Add("spawn " + effect); return next++; }
        public void Insert(string effect, Xform at, bool always, int randomIndex) => Log.Add("insert " + effect);
        public void FadeOut(int handle, float seconds) => Log.Add("fade " + handle);
    }

    static readonly Vec3[] Points = { new(0, 0, 0), new(150, 0, 0), new(0, 150, 0) };

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryRegionSkillPlaysItsEffectsInTheClientsOrder(string env)
    {
        string data = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) Assert.Ignore(env + " is not set");
        var packs = PackSet.Shared(data);
        var skills = new SkillTable(packs);
        var effects = new EffectTable(packs);
        var known = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool Exists(string e) => known.TryGetValue(e, out var k) ? k : known[e] = effects.Get(e) != null;

        int rows = 0, motion = 0, late = 0, invoke = 0, end = 0, loop = 0, missingInvoke = 0;
        var bad = new List<string>();
        var missing = new List<string>();
        foreach (int id in skills.Ids.Order())
            foreach (var row in skills.Levels(id))
            {
                var a = skills.RegionMotion(id, row.Level);
                if (a == null) continue;
                bool hasMotion = !string.IsNullOrEmpty(a.MotionEffect), hasLate = !string.IsNullOrEmpty(a.SplashDurationCastEffect),
                    hasInvoke = !string.IsNullOrEmpty(a.SplashInvokeEffect), hasEnd = !string.IsNullOrEmpty(a.SplashEndEffect),
                    hasLoop = !string.IsNullOrEmpty(a.LoopEffect);
                if (!hasMotion && !hasLate && !hasInvoke && !hasEnd && !hasLoop) continue;
                rows++;
                if (hasMotion) motion++;
                if (hasLate) late++;
                if (hasInvoke) invoke++;
                if (hasEnd) end++;
                if (hasLoop) loop++;
                if (hasInvoke && !Exists(a.SplashInvokeEffect)) { missingInvoke++; missing.Add($"{id}/{row.Level} {a.SplashInvokeEffect}"); }

                var fx = new Fx { Exists = Exists };
                var expect = new List<string>();
                int spawned = 0;
                if (hasMotion && Exists(a.MotionEffect) && !a.SplashCastEffectStartDelay)
                    foreach (var _ in Points) { expect.Add("spawn " + a.MotionEffect); spawned++; }
                if (hasLate) foreach (var _ in Points) expect.Add("insert " + a.SplashDurationCastEffect);
                var o = RegionSkillObject.Init(id, 1, Points, a, 0, 30, 0, true, 0, fx);
                o.UpdateEffects(1000, fx);   // the late effect only once
                for (int hit = 0; hit < 2; hit++)
                {
                    o.PlayHitEffect(fx);
                    if (hasInvoke) foreach (var _ in Points) expect.Add("insert " + a.SplashInvokeEffect);
                }
                var objs = new RegionSkillObjects();
                objs.Add(o);
                objs.Remove(id, 0.5f, fx);
                if (hasEnd) foreach (var _ in Points) expect.Add("insert " + a.SplashEndEffect);
                for (int h = 1; h <= spawned; h++) expect.Add("fade " + h);
                if (!fx.Log.SequenceEqual(expect)) bad.Add($"{id}/{row.Level}: [{string.Join(", ", fx.Log)}] want [{string.Join(", ", expect)}]");
            }

        TestContext.Out.WriteLine($"{env}: {rows} region skill levels; motion effect {motion}, splashDurationCastEffect {late}, " +
            $"splashInvokeEffect {invoke} ({missingInvoke} naming no effect xml), splashEndEffect {end}, loopEffect {loop} (STILL-OPEN, not played)");
        foreach (var m in missing.Take(20)) TestContext.Out.WriteLine("  no xml: " + m);
        Assert.That(rows, Is.GreaterThan(0));
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
    }
}
