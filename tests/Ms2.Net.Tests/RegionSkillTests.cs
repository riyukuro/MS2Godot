using System.Numerics;
using System.Xml.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>OnRegionSkill 0x4C (scenestategame.cpp:7718-7749) and CRegionSkillObject (regionskillobject.cpp): the read, Init's
/// spawn gates, PlayLateEffect, RemoveRegionSkillObject's Reset then fade, and the desert healing spot's skill data.</summary>
public class RegionSkillTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    static void WriteAdd(ByteWriter w) {
        w.WriteByte(0); w.WriteInt(77); w.WriteInt(5001); w.WriteInt(123456);
        w.WriteByte(2); w.WriteFloat(-900); w.WriteFloat(0); w.WriteFloat(1200); w.WriteFloat(10); w.WriteFloat(20); w.WriteFloat(30);
        w.WriteInt(70000018); w.WriteShort(1); w.WriteFloat(90); w.WriteFloat(0.5f);
    }

    [Test] public void AddReadsOwnerTickPointsSkillLevelThenRotYAndRotX() {
        var r = R(WriteAdd);
        var m = (RegionSkillMessage.Add) RegionSkillMessage.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That((m.Id, m.OwnerId, m.StartTick, m.SkillId, m.Level), Is.EqualTo((77, 5001, 123456, 70000018, (short) 1)));
        Assert.That(m.Points, Is.EqualTo(new[] { new Vector3(-900, 0, 1200), new Vector3(10, 20, 30) }));
        Assert.That((m.RotY, m.RotX), Is.EqualTo((90f, 0.5f)));
    }

    [Test] public void RemoveAndStopLoopReadTheIdOnlyAndOtherModesNothing() {
        Assert.That(RegionSkillMessage.Read(R(w => { w.WriteByte(1); w.WriteInt(77); })), Is.EqualTo(new RegionSkillMessage.Remove(77)));
        Assert.That(RegionSkillMessage.Read(R(w => { w.WriteByte(2); w.WriteInt(77); })), Is.EqualTo(new RegionSkillMessage.StopLoop(77)));
        Assert.That(RegionSkillMessage.Read(R(w => { w.WriteByte(5); w.WriteInt(77); })), Is.Null);
        Assert.That(() => RegionSkillMessage.Read(R(w => { WriteAdd(w); w.WriteByte(0); })), Throws.Exception);   // ExpectEnd
    }

    [Test] public void FlagsFollowTheClientsPrefixRule() {
        bool F(string v) => SkillTable.Flag(new XElement("motion", new XAttribute("a", v)), "a");
        Assert.That(new[] { "true", "TRUE", "yes", "1", "10" }.All(F));
        Assert.That(new[] { "false", "0", "no", "", "2" }.Any(F), Is.False);
        Assert.That(SkillTable.Flag(new XElement("motion"), "a"), Is.False);
    }

    sealed class Fx : IRegionSkillEffects {
        public readonly List<string> Log = new();
        public readonly List<Xform> At = new();
        public bool FailSpawn;
        public readonly List<int> Kinds = new();
        /// <summary>The points out of view (ShouldSkipByView); none by default.</summary>
        public Func<string, int, Vec3, bool> Skip;
        public int SkipAsks;
        int next = 1;
        public bool Has(string effect) => effect != "missing.xml";
        /// <summary>The owner's object kind (ObjectKind of any id).</summary>
        public int Owner;
        public Fx Of(int kind) { Owner = kind; return this; }
        public int ObjectKind(int objectId) => Owner;
        public int Spawn(string effect, Xform at, bool always, int randomIndex) => Spawn(effect, at, always, randomIndex, 0);
        public void Insert(string effect, Xform at, bool always, int randomIndex) => Insert(effect, at, always, randomIndex, 0);
        public int Spawn(string effect, Xform at, bool always, int randomIndex, int kind) {
            Kinds.Add(kind);
            if (FailSpawn) return 0;
            Log.Add($"spawn {effect}"); At.Add(at); return next++;
        }
        public void Insert(string effect, Xform at, bool always, int randomIndex, int kind) { Kinds.Add(kind); Log.Add($"insert {effect}"); At.Add(at); }
        public bool ShouldSkipByView(string effect, int kind, Vec3 at) { SkipAsks++; return Skip?.Invoke(effect, kind, at) ?? false; }
        public void FadeOut(int handle, float seconds) => Log.Add($"fade {handle} {seconds}");
        public void SetVisible(int handle, bool visible) => Log.Add($"show {handle} {visible}");
    }

    static readonly Vec3[] Two = { new(-900, 0, 1200), new(10, 20, 30) };

    [Test] public void InitSpawnsTheMotionEffectAtEveryPointWithTheRotation() {
        var fx = new Fx();
        var attr = new SkillMotion { MotionEffect = "BG/Common/Eff_Recovery.xml" };
        var o = RegionSkillObject.Init(1, 2, Two, attr, 0, 90, 100, true, 50, fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { "spawn BG/Common/Eff_Recovery.xml", "spawn BG/Common/Eff_Recovery.xml" }));
        Assert.That(o.Effects, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(o.Pending, Is.Empty);
        Assert.That(fx.At[0].T, Is.EqualTo(Two[0]));
        Assert.That(fx.At[1].T, Is.EqualTo(Two[1]));
        // rotate = FromEulerAnglesXYZ(rotX, 0, -rotY * pi / 180): rotY 90 turns Gamebryo +X to +Y
        var x = fx.At[0].Apply(new Vec3(1, 0, 0));
        Assert.That(x.X - Two[0].X, Is.EqualTo(0).Within(1e-5));
        Assert.That(x.Y - Two[0].Y, Is.EqualTo(1).Within(1e-5));
        Assert.That(x.Z - Two[0].Z, Is.EqualTo(0).Within(1e-5));
    }

    [Test] public void InitGatesTheSpawnOnLocalIgnoreHideAndStartDelay() {
        var attr = new SkillMotion { MotionEffect = "a.xml" };
        var fx = new Fx();
        var o = RegionSkillObject.Init(1, 2, Two, attr, 0, 0, 100, false, 50, fx);   // not local, not IgnoreOptimalHide
        Assert.That(fx.Log, Is.Empty);
        Assert.That(o.Pending, Is.EqualTo(new[] { (Two[0], 50u), (Two[1], 50u) }));   // the server tick

        attr.IgnoreOptimalHideCastEffect = true;
        fx = new Fx();
        o = RegionSkillObject.Init(1, 2, Two, attr, 0, 0, 100, false, 50, fx);
        Assert.That(o.Effects, Has.Count.EqualTo(2));

        attr.SplashCastEffectStartDelay = true;
        fx = new Fx();
        o = RegionSkillObject.Init(1, 2, Two, attr, 0, 0, 100, true, 50, fx);
        Assert.That(fx.Log, Is.Empty);
        Assert.That(o.Pending, Is.EqualTo(new[] { (Two[0], 100u), (Two[1], 100u) }));   // the start tick

        fx = new Fx { FailSpawn = true };
        o = RegionSkillObject.Init(1, 2, Two, new SkillMotion { MotionEffect = "a.xml" }, 0, 0, 100, true, 50, fx);
        Assert.That(o.Pending, Has.Count.EqualTo(2));
        Assert.That(RegionSkillObject.Init(1, 2, Two, new SkillMotion { MotionEffect = "missing.xml" }, 0, 0, 100, true, 50, new Fx()).Pending, Is.Empty);
        Assert.That(RegionSkillObject.Init(1, 2, Two, null, 0, 0, 100, true, 50, new Fx()), Is.Null);
    }

    [Test] public void LateEffectPlaysOnceWhenTheStartTickHasCome() {
        var fx = new Fx();
        var o = RegionSkillObject.Init(1, 2, Two, new SkillMotion { SplashDurationCastEffect = "late.xml" }, 0, 0, 100, true, 50, fx);
        Assert.That(fx.Log, Is.Empty);
        o.UpdateEffects(99, fx);
        Assert.That(fx.Log, Is.Empty);
        o.UpdateEffects(100, fx);
        o.UpdateEffects(200, fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { "insert late.xml", "insert late.xml" }));

        fx = new Fx();   // a non-local owner: not played, and done all the same
        o = RegionSkillObject.Init(1, 2, Two, new SkillMotion { SplashDurationCastEffect = "late.xml" }, 0, 0, 100, false, 150, fx);
        o.Local = true;
        o.UpdateEffects(200, fx);
        Assert.That(fx.Log, Is.Empty);
    }

    [Test] public void PendingPointsSpawnOnceTheirTimeHasComeAndTheyAreInView() {
        var attr = new SkillMotion { MotionEffect = "a.xml", SplashCastEffectStartDelay = true };
        var fx = new Fx();
        var o = RegionSkillObject.Init(1, 2, Two, attr, 0, 0, 100, true, 50, fx.Of(3));
        Assert.That(o.OwnerKind, Is.EqualTo(3));
        o.UpdateEffects(99, fx);   // GetServerTick() < nTime: not yet, and the view is not asked
        Assert.That((fx.Log.Count, fx.SkipAsks), Is.EqualTo((0, 0)));
        fx.Skip = (e, k, at) => at == Two[1];   // the second point is out of view
        o.UpdateEffects(100, fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { "spawn a.xml" }));
        Assert.That(fx.At[0].T, Is.EqualTo(Two[0]));
        Assert.That(o.Pending, Is.EqualTo(new[] { (Two[1], 100u) }));
        Assert.That(o.Effects, Is.EqualTo(new[] { 1 }));
        fx.Skip = null;
        o.UpdateEffects(101, fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { "spawn a.xml", "spawn a.xml" }));
        Assert.That(o.Pending, Is.Empty);
        Assert.That(o.Effects, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(fx.Kinds, Is.EqualTo(new[] { 3, 3 }), "the owner's kind goes with every spawn");
    }

    [Test] public void APendingPointRetriesAFailedSpawnAndNeedsALocalOwnerOrIgnoreHide() {
        var fx = new Fx { FailSpawn = true };
        var o = RegionSkillObject.Init(1, 2, Two, new SkillMotion { MotionEffect = "a.xml" }, 0, 0, 100, true, 50, fx);
        Assert.That(o.Pending, Has.Count.EqualTo(2));
        o.UpdateEffects(60, fx);
        Assert.That(o.Pending, Has.Count.EqualTo(2), "a spawn that fails stays pending");
        fx.FailSpawn = false;
        o.UpdateEffects(61, fx);
        Assert.That((o.Pending.Count, o.Effects.Count), Is.EqualTo((0, 2)));

        fx = new Fx();   // not local: pending, never spawned; with IgnoreOptimalHideCastEffect it spawns
        var attr = new SkillMotion { MotionEffect = "a.xml" };
        o = RegionSkillObject.Init(1, 2, Two, attr, 0, 0, 100, false, 50, fx);
        o.UpdateEffects(60, fx);
        Assert.That((fx.Log.Count, o.Pending.Count), Is.EqualTo((0, 2)));
        Assert.That(fx.SkipAsks, Is.EqualTo(2), "ShouldSkipByView runs before the local test");
        attr.IgnoreOptimalHideCastEffect = true;
        o.UpdateEffects(61, fx);
        Assert.That(o.Pending, Is.Empty);
    }

    [Test] public void APendingSpawnTakesTheFirstEntryOfItsPoint() {
        var same = new[] { Two[0], Two[0] };
        var fx = new Fx { FailSpawn = true };
        var o = RegionSkillObject.Init(1, 2, same, new SkillMotion { MotionEffect = "a.xml" }, 0, 0, 100, true, 50, fx);
        fx.FailSpawn = false;
        o.UpdateEffects(60, fx);   // both spawn; each removes the first entry of the point
        Assert.That((o.Pending.Count, o.Effects.Count), Is.EqualTo((0, 2)));
    }

    [Test] public void WaitingLoopPointsSpawnShownWhenInViewWithALocalOwner() {
        var attr = new SkillMotion { LoopEffect = "loop.xml" };
        var fx = new Fx { FailSpawn = true };
        var o = RegionSkillObject.Init(1, 2, Two, attr, 0, 0, 100, true, 50, fx.Of(3));
        Assert.That(o.Waiting, Is.EqualTo(Two));
        fx.FailSpawn = false;
        fx.Skip = (e, k, at) => e == "loop.xml" && k == 3 && at == Two[0];
        o.UpdateEffects(60, fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { "spawn loop.xml", "show 1 True" }));
        Assert.That(o.Waiting, Is.EqualTo(new[] { Two[0] }));
        Assert.That((o.Effects, o.LoopEffects), Is.EqualTo((new List<int> { 1 }, new List<int> { 1 })));
        fx.Skip = null;
        o.UpdateEffects(61, fx);
        Assert.That(o.Waiting, Is.Empty);
        Assert.That(o.LoopEffects, Is.EqualTo(new[] { 1, 2 }));

        fx = new Fx();   // a non-local owner waits for ever, IgnoreOptimalHideCastEffect or not (:370)
        attr.IgnoreOptimalHideCastEffect = true;
        o = RegionSkillObject.Init(1, 2, Two, attr, 0, 0, 100, false, 50, fx);
        o.UpdateEffects(60, fx);
        Assert.That((fx.Log.Count, o.Waiting.Count), Is.EqualTo((0, 2)));
    }

    [Test] public void HitResetAndLateEffectsCarryTheOwnersKind() {
        var fx = new Fx();
        var o = RegionSkillObject.Init(1, 2, new[] { Two[0] }, new SkillMotion { SplashDurationCastEffect = "late.xml", SplashInvokeEffect = "hit.xml",
            SplashEndEffect = "end.xml" }, 0, 0, 0, true, 50, fx.Of(5));
        o.PlayHitEffect(fx);
        o.Reset(fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { "insert late.xml", "insert hit.xml", "insert end.xml" }));
        Assert.That(fx.Kinds, Is.EqualTo(new[] { 5, 5, 5 }));
    }

    [Test] public void AddKeepsTheFirstIdAndRemoveResetsThenFades() {
        var fx = new Fx();
        var attr = new SkillMotion { MotionEffect = "a.xml", SplashEndEffect = "end.xml" };
        var objs = new RegionSkillObjects();
        Assert.That(objs.Add(RegionSkillObject.Init(7, 2, Two, attr, 0, 0, 0, true, 0, fx)), Is.True);
        Assert.That(objs.Add(RegionSkillObject.Init(7, 2, Two, attr, 0, 0, 0, true, 0, fx)), Is.False);
        Assert.That(objs.DroppedDuplicates, Is.EqualTo(1));
        fx.Log.Clear();
        objs.Remove(7, 0.3f, fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { "insert end.xml", "insert end.xml", "fade 1 0.3", "fade 2 0.3" }));
        Assert.That(objs.Objects, Is.Empty);
        fx.Log.Clear();
        objs.Remove(7, 0.3f, fx);
        Assert.That(fx.Log, Is.Empty);
    }

    [Test] public void HitEffectInsertsTheInvokeEffectAtEveryPointWithTheRotation() {
        // PlayHitEffect (regionskillobject.cpp:224-252): the region's damage (0x3D mode 5) plays splashInvokeEffect once per
        // point, turned as Init's spawns, each time a hit comes; nothing kept
        var fx = new Fx();
        var o = RegionSkillObject.Init(3, 2, Two, new SkillMotion { MotionEffect = "a.xml", SplashInvokeEffect = "invoke.xml" }, 0, 90, 0, true, 0, fx);
        fx.Log.Clear(); fx.At.Clear();
        o.PlayHitEffect(fx);
        o.PlayHitEffect(fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { "insert invoke.xml", "insert invoke.xml", "insert invoke.xml", "insert invoke.xml" }));
        Assert.That(fx.At[0].T, Is.EqualTo(Two[0]));
        Assert.That(fx.At[1].T, Is.EqualTo(Two[1]));
        Assert.That(fx.At[0].R, Is.EqualTo(RegionSkillObject.PointTransform(Two[0], 0, 90).R));
        Assert.That(o.Effects, Has.Count.EqualTo(2), "the hit effect is not kept");
    }

    [Test] public void HitEffectNeedsTheEffectAndALocalOwnerOrIgnoreHide() {
        var fx = new Fx();
        RegionSkillObject.Init(3, 2, Two, new SkillMotion(), 0, 0, 0, true, 0, fx).PlayHitEffect(fx);
        Assert.That(fx.Log, Is.Empty, "no splashInvokeEffect");
        RegionSkillObject.Init(3, 2, Two, new SkillMotion { SplashInvokeEffect = "invoke.xml" }, 0, 0, 0, false, 0, fx).PlayHitEffect(fx);
        Assert.That(fx.Log, Is.Empty, "not local");
        RegionSkillObject.Init(3, 2, Two, new SkillMotion { SplashInvokeEffect = "invoke.xml", IgnoreOptimalHideCastEffect = true }, 0, 0, 0, false, 0, fx).PlayHitEffect(fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { "insert invoke.xml", "insert invoke.xml" }), "IgnoreOptimalHideCastEffect");
    }

    /// <summary>The Assassin's Fragmented Star (10800021): its star (attack p0, cubeMagicPathID 108000211) places a region of
    /// 10800022 where it ends, and that region's damage plays the ring of stars, splashInvokeEffect
    /// Eff_Assassin_CircleOfStar_Invoke_01.xml, one nif turned in steps round the point. On both data sets.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void FragmentedStarsRegionPlaysTheStarRingOnItsHit(string env) {
        string data = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) Assert.Ignore(env + " is not set");
        var packs = PackSet.Shared(data);
        var attr = new SkillTable(packs).RegionMotion(10800022, 1);
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.SplashInvokeEffect, Is.EqualTo("CH/Skill/Assassin/Eff_Assassin_CircleOfStar_Invoke_01.xml").IgnoreCase);
        var parts = new EffectTable(packs).Get(attr.SplashInvokeEffect);
        Assert.That(parts, Is.Not.Null);
        var nifs = parts.Where(p => p.Type == "nif" && p.FilePath.EndsWith("Eff_Assassin_CircleOfStar_Invoke_01.nif", StringComparison.OrdinalIgnoreCase)).ToList();
        TestContext.Out.WriteLine($"{env}: {parts.Count} parts, {nifs.Count} star nifs");
        Assert.That(nifs, Has.Count.GreaterThan(1), "the ring: the star nif repeated");

        var fx = new Fx();
        var o = RegionSkillObject.Init(1, 2, new[] { new Vec3(100, 200, 300) }, attr, 0, 45, 0, true, 0, fx);
        fx.Log.Clear();
        o.PlayHitEffect(fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { "insert " + attr.SplashInvokeEffect }));
    }

    [Test] public void LoopEffectSpawnsHiddenThenShownHidesOnAHitAndModeTwoShowsIt() {
        // Init :139-171: the loop effect for a local owner, kept in both lists, hidden, then StopLoopEffects shows it;
        // PlayHitEffect :229-230 hides it; mode 2 (StopLoopEffects) shows it again; the remove fades it with the others
        var fx = new Fx();
        var attr = new SkillMotion { MotionEffect = "a.xml", LoopEffect = "loop.xml", SplashInvokeEffect = "invoke.xml" };
        var o = RegionSkillObject.Init(5, 2, Two, attr, 0, 0, 0, true, 0, fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { "spawn a.xml", "spawn a.xml", "spawn loop.xml", "show 3 False", "spawn loop.xml", "show 4 False",
            "show 3 True", "show 4 True" }));
        Assert.That(o.LoopEffects, Is.EqualTo(new[] { 3, 4 }));
        Assert.That(o.Effects, Is.EqualTo(new[] { 1, 2, 3, 4 }));
        fx.Log.Clear();
        o.PlayHitEffect(fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { "show 3 False", "show 4 False", "insert invoke.xml", "insert invoke.xml" }));
        fx.Log.Clear();
        o.StopLoopEffects(fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { "show 3 True", "show 4 True" }));
        var objs = new RegionSkillObjects();
        objs.Add(o);
        fx.Log.Clear();
        objs.Remove(5, 0.3f, fx);
        Assert.That(fx.Log, Is.EqualTo(new[] { "fade 1 0.3", "fade 2 0.3", "fade 3 0.3", "fade 4 0.3" }));
    }

    [Test] public void LoopEffectNeedsALocalOwnerEvenWithIgnoreHide() {
        // :156 tests m_bLocal only (the target's m_b31 is not set for it): another owner's points wait (m_waiting)
        var fx = new Fx();
        var attr = new SkillMotion { LoopEffect = "loop.xml", IgnoreOptimalHideCastEffect = true };
        var o = RegionSkillObject.Init(5, 2, Two, attr, 0, 0, 0, false, 0, fx);
        Assert.That(fx.Log, Is.Empty);
        Assert.That(o.Waiting, Is.EqualTo(Two));
        fx = new Fx { FailSpawn = true };
        o = RegionSkillObject.Init(5, 2, Two, attr, 0, 0, 0, true, 0, fx);
        Assert.That(o.Waiting, Has.Count.EqualTo(2), "a spawn that failed waits too");
        Assert.That(RegionSkillObject.Init(5, 2, Two, new SkillMotion { LoopEffect = "missing.xml" }, 0, 0, 0, true, 0, new Fx()).Waiting, Is.Empty,
            "no effect data: nothing waits");
    }

    static PackSet Data() {
        string data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set (skill and effect data)");
        return PackSet.Shared(data);
    }

    [Test] public void HealingSpotSkillSpawnsTheRecoveryEffect() {
        var packs = Data();
        var skills = new SkillTable(packs);
        var attr = skills.RegionMotion(70000018, 1);   // 02000347's Skill_HealingSpot_A01
        Assert.That(attr?.MotionEffect, Is.EqualTo("BG/Common/Eff_Recovery.xml"));
        Assert.That(skills.RegionMotion(70000018, 99), Is.Null);   // GetLevelData: the exact level
        var parts = new EffectTable(packs).Get(attr.MotionEffect);
        Assert.That(parts?.Any(p => p.Type == "nif" && p.FilePath.EndsWith("eff_com_recovery.nif", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(RegionSkillObjects.FadeOutDuration(packs), Is.EqualTo(0.3f));   // table/constants.xml
    }
}
