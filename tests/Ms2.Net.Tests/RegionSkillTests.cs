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
        int next = 1;
        public bool Has(string effect) => effect != "missing.xml";
        public int Spawn(string effect, Xform at, bool always, int randomIndex) {
            if (FailSpawn) return 0;
            Log.Add($"spawn {effect}"); At.Add(at); return next++;
        }
        public void Insert(string effect, Xform at, bool always, int randomIndex) { Log.Add($"insert {effect}"); At.Add(at); }
        public void FadeOut(int handle, float seconds) => Log.Add($"fade {handle} {seconds}");
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
