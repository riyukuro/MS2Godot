using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The motion's tag effects (SkillMotionData::m_mapTagEffects, skillleveldata.cpp:1180-1188) and StateSkill::TextKeyEvent's
/// test before it plays one (stateskill.cpp:792-834).
/// </summary>
public class SkillTagEffectsTests {
    [Test] public void TheTagListSplitsLikeTheClient() {
        // CString::Tokenize on "," then "=", empty tokens skipped; only a two-part token maps; a later key replaces an earlier one
        var m = Ms2.Formats.World.SkillMotion.ParseTagEffects("e0=a.xml,,bad,e1=b.xml=c,e0=d.xml,e2==f.xml");
        Assert.That(m, Is.EqualTo(new Dictionary<string, string> { ["e0"] = "d.xml", ["e2"] = "f.xml" }));
        Assert.That(Ms2.Formats.World.SkillMotion.ParseTagEffects(""), Is.Null);
        Assert.That(Ms2.Formats.World.SkillMotion.ParseTagEffects("e0"), Is.Null);
    }

    [Test] public void InsideTheLoopOnlyTheLoopsKeysPlay() {
        // m_b117: before "loopstart", after "loopend" or with the record queued (m_n9c) nothing plays
        Assert.That(SkillTagEffects.Plays(0.3f, true, false, false, 0.2f, 0.7f, -1), Is.True);
        Assert.That(SkillTagEffects.Plays(0.1f, true, false, false, 0.2f, 0.7f, -1), Is.False);
        Assert.That(SkillTagEffects.Plays(0.8f, true, false, false, 0.2f, 0.7f, -1), Is.False);
        Assert.That(SkillTagEffects.Plays(0.3f, true, true, false, 0.2f, 0.7f, -1), Is.False);
        Assert.That(SkillTagEffects.Plays(0.3f, true, false, false, 0.2f, -1, -1), Is.False);   // no "loopend": fKey > -FLT_MAX
    }

    [Test] public void AfterTheChargingLoopKeysBeforeTheAttackDoNotPlay() {
        Assert.That(SkillTagEffects.Plays(0.1f, false, false, true, -1, -1, 0.5f), Is.False);
        Assert.That(SkillTagEffects.Plays(0.6f, false, false, true, -1, -1, 0.5f), Is.True);
        Assert.That(SkillTagEffects.Plays(0.1f, false, false, false, -1, -1, 0.5f), Is.True);
        Assert.That(SkillTagEffects.Plays(0.1f, false, true, false, -1, -1, 0.5f), Is.True);   // m_n9c is read in the loop only
        Assert.That(SkillTagEffects.Plays(-1f, true, true, true, 0.2f, 0.7f, 0.5f), Is.True);   // fKey == -FLT_MAX
    }

    [Test] public void TheKeyWalkTakesTheNamedKeysInTimeOrder() {
        var keys = new SkillTagEffects.Keys();
        keys.Set(new List<(double, string)> { (0.1, "e0"), (0.2, "p0"), (0.4, "e1"), (0.6, "e0") },
            new Dictionary<string, string> { ["e0"] = "a.xml", ["e1"] = "b.xml" });
        Assert.That(keys.Count, Is.EqualTo(3));
        Assert.That(keys.Next(-1, 0.05), Is.EqualTo(-1));
        Assert.That(keys.Next(-1, 0.5), Is.EqualTo(0.1));
        Assert.That(keys.Next(0.1, 0.5), Is.EqualTo(0.4));
        Assert.That(keys.Next(0.4, 0.6), Is.EqualTo(0.6));
        Assert.That(keys[2], Is.EqualTo((0.6, "e0", "a.xml")));
        keys.Set(null, null);
        Assert.That(keys.Count, Is.Zero);
    }

    [Test] public void TheKeyWalkAllocatesNothing() {
        var keys = new SkillTagEffects.Keys();
        keys.Set(new List<(double, string)> { (0.1, "e0"), (0.4, "e1") }, new Dictionary<string, string> { ["e0"] = "a", ["e1"] = "b" });
        keys.Next(0, 1);
        double sum = 0;
        long bytes = Alloc.Least(() => { for (int i = 0; i < 1000; i++) sum += keys.Next(i * 0.001, 1) + (SkillTagEffects.Plays(0.3f, true, false, false, 0.2f, 0.7f, 0) ? 1 : 0); });
        Assert.That(bytes, Is.Zero);
        Assert.That(sum, Is.GreaterThan(0));
    }

    [Test] public void TheGms2AndKmsBasicAttackNamesItsCastEffect() {
        // skill 1 (the bare-hand attack) motion 0: strTagEffects "e0=CH/Weapon/Hand/Eff_Hand_Attack01_Cast_01.xml"
        string data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set (skill data)");
        var m = new SkillTable(PackSet.Shared(data)).Motion(1, 1, 0);
        Assert.That(m?.TagEffects, Is.EqualTo(new Dictionary<string, string> { ["e0"] = "CH/Weapon/Hand/Eff_Hand_Attack01_Cast_01.xml" }));
    }
    [Test] public void TheHandleMapReplacesAKeysIdAndEndsSoftOrHard() {
        // m_map138: SetAt replaces (the old effect left running); vfunc_5: StopRecordEffects then the release of what is left
        var h = new SkillTagEffects.Handles();
        h.Set("e0", 5); h.Set("e1", 0); h.Set("e0", 7); h.Set("e2", 9);
        Assert.That((h.Count, h["e0"], h["e1"], h["x"]), Is.EqualTo((3, 7, 0, 0)));
        var stopped = new List<int>(); var released = new List<int>();
        h.End(false, stopped.Add, released.Add);
        Assert.That((stopped, released, h.Count), Is.EqualTo((new List<int>(), new List<int> { 7, 9 }, 0)));
        h.Set("e0", 3); h.Set("e1", 4);
        h.End(true, stopped.Add, released.Add);
        Assert.That(stopped, Is.EqualTo(new[] { 3, 4 }));
        Assert.That(released, Is.EqualTo(new[] { 7, 9 }), "a hard end releases nothing more: the ids are zeroed");
    }
}
