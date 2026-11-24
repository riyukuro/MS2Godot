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
/// splashCastEffectStartDelay, which UpdateEffects spawns once the start tick has come, :311-354; then PlayLateEffect :405-433, the duration effect at every point once the start tick has
/// come), OnRegionSkillDamage mode 5's PlayHitEffect (:224-252, the invoke effect at every point, every hit), and
/// RemoveRegionSkillObject (worldservice_1.cpp:4180-4189: Reset :255-285, the end effect at every point, then the motion
/// effects fade). The loop effect (:139-171) spawns after the motion effects for a local owner, hidden, then StopLoopEffects
/// shows it; each hit hides it first (:229-230); OnRegionSkill mode 2 shows it again (StopLoopEffects); the remove fades it
/// with the motion effects. The effect calls must be exactly those.
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
        public void SetVisible(int handle, bool visible) => Log.Add($"show {handle} {visible}");
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

        int rows = 0, motion = 0, late = 0, invoke = 0, end = 0, loop = 0, missingInvoke = 0, missingLoop = 0, delayed = 0;
        var bad = new List<string>();
        var missing = new List<string>();
        var jobLoops = new List<string>();   // the job skills' (ids below 20000000) loop effects, level 1, for the log
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
                if (hasLoop && !Exists(a.LoopEffect)) { missingLoop++; missing.Add($"{id}/{row.Level} {a.LoopEffect}"); }
                if (hasLoop && id < 20000000 && row.Level == 1) jobLoops.Add($"{id} {a.LoopEffect}");

                var fx = new Fx { Exists = Exists };
                var expect = new List<string>();
                int spawned = 0;
                if (hasMotion && Exists(a.MotionEffect) && !a.SplashCastEffectStartDelay)
                    foreach (var _ in Points) { expect.Add("spawn " + a.MotionEffect); spawned++; }
                var loops = new List<int>();
                if (hasLoop && Exists(a.LoopEffect))
                    foreach (var _ in Points) { expect.Add("spawn " + a.LoopEffect); loops.Add(++spawned); expect.Add($"show {spawned} False"); }
                foreach (int h in loops) expect.Add($"show {h} True");
                if (hasLate) foreach (var _ in Points) expect.Add("insert " + a.SplashDurationCastEffect);
                var o = RegionSkillObject.Init(id, 1, Points, a, 0, 30, 0, true, 0, fx);
                // UpdateEffects (:311-354): the start delay's pending points spawn once the start tick has come; the late
                // effect only once
                if (hasMotion && Exists(a.MotionEffect) && a.SplashCastEffectStartDelay)
                {
                    foreach (var _ in Points) { expect.Add("spawn " + a.MotionEffect); spawned++; }
                    delayed++;
                }
                o.UpdateEffects(1000, fx);
                for (int hit = 0; hit < 2; hit++)
                {
                    o.PlayHitEffect(fx);
                    foreach (int h in loops) expect.Add($"show {h} False");
                    if (hasInvoke) foreach (var _ in Points) expect.Add("insert " + a.SplashInvokeEffect);
                }
                o.StopLoopEffects(fx);   // mode 2
                foreach (int h in loops) expect.Add($"show {h} True");
                var objs = new RegionSkillObjects();
                objs.Add(o);
                objs.Remove(id, 0.5f, fx);
                if (hasEnd) foreach (var _ in Points) expect.Add("insert " + a.SplashEndEffect);
                for (int h = 1; h <= spawned; h++) expect.Add("fade " + h);
                if (!fx.Log.SequenceEqual(expect)) bad.Add($"{id}/{row.Level}: [{string.Join(", ", fx.Log)}] want [{string.Join(", ", expect)}]");
            }

        TestContext.Out.WriteLine($"{env}: {rows} region skill levels; motion effect {motion}, splashDurationCastEffect {late}, " +
            $"splashInvokeEffect {invoke} ({missingInvoke} naming no effect xml), splashEndEffect {end}, loopEffect {loop} ({missingLoop} naming no effect xml); " +
            $"motion effects spawned at the start tick (splashCastEffectStartDelay) {delayed}");
        foreach (var m in missing.Take(20)) TestContext.Out.WriteLine("  no xml: " + m);
        TestContext.Out.WriteLine($"  job skills with a loopEffect: {string.Join("; ", jobLoops.Distinct())}");
        Assert.That(rows, Is.GreaterThan(0));
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
    }

    /// <summary>The view tests with the real effect xmls and Table/effectOption.xml: a spawn that makes no part (every top level
    /// nif or soundEvent part out of view, <see cref="EffectSkipTable.PartOutOfView"/>) fails, and ShouldSkipByView
    /// (<see cref="EffectSkipTable.ShouldSkipByView"/>) holds the retries back.</summary>
    sealed class ViewFx : IRegionSkillEffects
    {
        public EffectTable Effects;
        public EffectSkipTable Table;
        public System.Numerics.Vector3? Eye;
        public readonly List<string> Log = new();
        public readonly Dictionary<int, bool> Shown = new();
        int next = 1;
        public bool Has(string effect) => Effects.Get(effect) != null;
        static System.Numerics.Vector3 N(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
        public bool Refused(string effect, Xform at, int kind)
        {
            var parts = Effects.Get(effect);
            if (parts == null || kind == 0) return false;
            int top = 0, refused = 0;
            foreach (var p in parts)
            {
                if (!p.TopLevel) continue;
                top++;
                if (Table.PartOutOfView(p, kind, N(at.Apply(new Vec3(p.Translate[0], p.Translate[1], p.Translate[2]))), Eye)) refused++;
            }
            return top > 0 && refused == top;
        }
        /// <summary>The owner's object kind (ObjectKind of any id).</summary>
        public int Owner;
        public ViewFx Of(int kind) { Owner = kind; return this; }
        public int ObjectKind(int objectId) => Owner;
        public int Spawn(string effect, Xform at, bool always, int randomIndex) => Spawn(effect, at, always, randomIndex, 0);
        public void Insert(string effect, Xform at, bool always, int randomIndex) => Insert(effect, at, always, randomIndex, 0);
        public int Spawn(string effect, Xform at, bool always, int randomIndex, int kind)
        {
            if (Refused(effect, at, kind)) { Log.Add("refused " + effect); return 0; }
            Log.Add("spawn " + effect);
            return next++;
        }
        public void Insert(string effect, Xform at, bool always, int randomIndex, int kind) => Log.Add("insert " + effect);
        public void FadeOut(int handle, float seconds) => Log.Add("fade " + handle);
        public void SetVisible(int handle, bool visible) => Shown[handle] = visible;
        public bool ShouldSkipByView(string effect, int kind, Vec3 at) => kind != 0 && Table.ShouldSkipByView(Effects.Get(effect), kind, N(at), Eye);
    }

    /// <summary>
    /// Every region skill level with a motion effect or a loop effect, for every graphic quality's view distances. Another PC's
    /// region (kind 3) with the camera's actor far away: Init spawns a point's effect unless every part is out of view, and
    /// the rest wait (motion: pending, loop: waiting). A tick at the start tick, still far away, spawns a pending point only
    /// when ShouldSkipByView passes and the spawn makes a part (so a start delay effect with one part out of view waits too),
    /// and spawns nothing else. With the actor at the points, the next tick spawns every waiting point: each loop effect is
    /// shown, nothing is left waiting, and each point has its effects. The own PC's region (kind 1) never waits for view.
    /// </summary>
    static readonly Vec3[] One = { new(0, 0, 0) };

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryRegionSkillsPointsWaitOutOfViewAndSpawnInView(string env)
    {
        string data = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) Assert.Ignore(env + " is not set");
        var packs = PackSet.Shared(data);
        var skills = new SkillTable(packs);
        var effects = new EffectTable(packs);
        string option = System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/effectoption.xml"));
        var bad = new List<string>();
        var eyeFar = new System.Numerics.Vector3(2500, 0, 0);   // past quality 0-3's 1500 / 2000, nearer than quality 4's 3000
        for (int quality = 0; quality <= 4; quality++)
        {
            var table = EffectSkipTable.Parse(option, quality);
            int rows = 0, waitMotion = 0, waitLoop = 0, delayHeld = 0, ownWaits = 0;
            foreach (int id in skills.Ids.Order())
                foreach (var row in skills.Levels(id))
                {
                    var a = skills.RegionMotion(id, row.Level);
                    if (a == null) continue;
                    bool hasMotion = !string.IsNullOrEmpty(a.MotionEffect) && effects.Get(a.MotionEffect) != null;
                    bool hasLoop = !string.IsNullOrEmpty(a.LoopEffect) && effects.Get(a.LoopEffect) != null;
                    if (!hasMotion && !hasLoop) continue;
                    rows++;
                    string where = $"q{quality} {id}/{row.Level}";
                    var probe = new ViewFx { Effects = effects, Table = table, Eye = eyeFar };
                    var at = RegionSkillObject.PointTransform(One[0], 0, 30);
                    bool motionRefused = hasMotion && probe.Refused(a.MotionEffect, at, 3);
                    bool loopRefused = hasLoop && probe.Refused(a.LoopEffect, at, 3);
                    bool motionSkip = hasMotion && probe.ShouldSkipByView(a.MotionEffect, 3, One[0]);

                    var fx = new ViewFx { Effects = effects, Table = table, Eye = eyeFar };
                    var o = RegionSkillObject.Init(id, 1, One, a, 0, 30, 500, true, 0, fx.Of(3));
                    int wantPending = !hasMotion ? 0 : a.SplashCastEffectStartDelay || motionRefused ? One.Length : 0;
                    int wantWaiting = hasLoop && loopRefused ? One.Length : 0;
                    if (o.Pending.Count != wantPending || o.Waiting.Count != wantWaiting)
                        bad.Add($"{where} init: pending {o.Pending.Count} want {wantPending}, waiting {o.Waiting.Count} want {wantWaiting}");
                    if (hasMotion && motionRefused) waitMotion++;
                    if (wantWaiting > 0) waitLoop++;

                    o.UpdateEffects(500, fx);   // far away still
                    int stillPending = wantPending > 0 && motionSkip ? One.Length : 0;   // a refused spawn is out of view too
                    if (a.SplashCastEffectStartDelay && hasMotion && motionSkip && !motionRefused) delayHeld++;
                    if (o.Pending.Count != stillPending || o.Waiting.Count != wantWaiting)
                        bad.Add($"{where} far tick: pending {o.Pending.Count} want {stillPending}, waiting {o.Waiting.Count} want {wantWaiting}");

                    fx.Eye = new System.Numerics.Vector3((float)One[0].X, (float)One[0].Y, (float)One[0].Z);
                    // the actor at the point: distance 0 is in view of every distance but 0, which never skips (a vibrate part's
                    // PC distance is 1, so a second point 150 away would stay out of view)
                    o.UpdateEffects(501, fx);
                    int want = (hasMotion ? One.Length : 0) + (hasLoop ? One.Length : 0);
                    if (o.Pending.Count != 0 || o.Waiting.Count != 0 || o.Effects.Count != want || o.LoopEffects.Count != (hasLoop ? One.Length : 0))
                        bad.Add($"{where} near tick: pending {o.Pending.Count}, waiting {o.Waiting.Count}, effects {o.Effects.Count} want {want}, loops {o.LoopEffects.Count}");
                    foreach (int h in o.LoopEffects)
                        if (!fx.Shown.TryGetValue(h, out bool v) || !v) bad.Add($"{where}: loop effect {h} not shown");

                    var own = new ViewFx { Effects = effects, Table = table, Eye = eyeFar };
                    var mine = RegionSkillObject.Init(id, 1, One, a, 0, 30, 500, true, 0, own.Of(1));
                    mine.UpdateEffects(500, own);
                    if (mine.Pending.Count != 0 || mine.Waiting.Count != 0) { ownWaits++; bad.Add($"{where}: the own region waits"); }
                }
            TestContext.Out.WriteLine($"{env} quality {quality}: {rows} region skill levels with a motion or loop effect; another PC's far away: " +
                $"motion effect waits {waitMotion}, loop effect waits {waitLoop}, start delay held by view {delayHeld}; own waits {ownWaits}");
            Assert.That(rows, Is.GreaterThan(0));
        }
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
    }
}
