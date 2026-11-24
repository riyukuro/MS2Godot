using System.Numerics;
using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The end of an own magic whose record is not an arrow one (CMagic::FUN_14145de80's end check FUN_141463fb0 and lifetime,
/// then FUN_14145f570): the cube splash (Skill 1/2) and the end effects, as an arrow magic's end has them.
/// </summary>
public class OwnTagMagicEndTests
{
    // 0: the default row LoadMagicPath sets (magicdata.cpp:370-375); 20: delay 0.1, spawn 0.2, hold 300 ms;
    // 21: life 0.1 after delay 0.5, explosionByDestroy 1; 22: the same with explosionByDestroy 0; 23: a moving magic
    static readonly MagicPathTable Paths = new(XDocument.Parse(
        "<ms2><type id=\"0\"><move delayTime=\"4\" explosionByDestroy=\"1\"/></type>" +
        "<type id=\"20\"><move delayTime=\"0.1\" spawnTime=\"0.2\" moveEndHoldDuration=\"300\"/></type>" +
        "<type id=\"21\"><move delayTime=\"0.5\" lifeTime=\"0.1\"/></type>" +
        "<type id=\"22\"><move delayTime=\"0.5\" lifeTime=\"0.1\" explosionByDestroy=\"0\"/></type>" +
        "<type id=\"23\"><move vel=\"1000\" distance=\"500\" direction=\"0,1,0\" explosionByDestroy=\"0\"/></type>" +
        "</ms2>"));

    static SkillAttackData Attack(uint magic = 0, int cube = 103000111)
    {
        var a = new SkillAttackData { MagicPathId = magic, CubeMagicPathId = cube, BrokenOffence = 0 };
        a.Range.Shape = 1;
        a.Range.Width = 200; a.Range.Distance = 200; a.Range.Height = 200;
        return a;
    }

    static readonly CastRecord Cast = new() { CastId = 9001, SendCount = 3, AttackIndex = 1, Position = new Vector3(10, 20, 30) };

    static List<SkillDamageTarget> Tags(params byte[] hits) =>
        hits.Select(h => new SkillDamageTarget(1, 2, h, Array.Empty<SkillDamageLink>())).ToList();

    static MagicOwner Owner(bool local = true, bool dead = false) => new(Vector3.Zero, 0, local, dead, false);

    static int Tick(OwnTagMagics m, int now, List<SkillSplashRequest> splashes, List<MagicEndEffect> fx = null, bool local = true,
        int lead = 0, bool dead = false) =>
        m.Tick(now, lead, Owner(local, dead), default, Cast, null, new List<SkillHitRequest>(), null, null, false, null, splashes, fx);

    [Test]
    public void AStillTagMagicSendsTheCubeSplashInItsFillTick()
    {
        // FUN_141464d00 sets m_n1CC (the explosion) for the local owner; FUN_141463fb0 ends it in the same tick (no flight, no hold)
        var m = new OwnTagMagics();
        var s = new List<SkillSplashRequest>();
        Assert.That(m.Add(null, Attack(), Cast, Tags(0), 1000, Paths, null), Is.EqualTo(1));
        Tick(m, 1000, s);
        Assert.That(m.Count, Is.Zero);
        Assert.That(s, Has.Count.EqualTo(1));
        Assert.That((s[0].CastId, s[0].Attack, s[0].SendCount, s[0].Hit), Is.EqualTo((9001L, (byte)1, 3, 0)));
        Assert.That(s[0].Position, Is.EqualTo(Cast.Position), "the start point: no fire node here, the record's position");
    }

    [Test]
    public void NoCubePathNoSplash()
    {
        var m = new OwnTagMagics();
        var s = new List<SkillSplashRequest>();
        m.Add(null, Attack(cube: 0), Cast, Tags(0), 1000, Paths, null);
        Tick(m, 1000, s);
        Assert.That((m.Count, s.Count), Is.EqualTo((0, 0)), "it ends without a splash");
    }

    [Test]
    public void AnotherOwnersMagicEndsWithoutTheSplash()
    {
        // FUN_141464410 0: no fill (m_n1CC stays 0), the default row has no explosionByDestroy, and the send needs it anyway
        var m = new OwnTagMagics();
        var s = new List<SkillSplashRequest>();
        m.Add(null, Attack(), Cast, Tags(0), 1000, Paths, null);
        Tick(m, 1000, s, local: false);
        Assert.That((m.Count, s.Count), Is.EqualTo((0, 0)));
    }

    [Test]
    public void ADeadOwnersFillStillExplodes()
    {
        // m_n1CC is set before the dead test (magicdata.cpp:1758-1762)
        var m = new OwnTagMagics();
        var s = new List<SkillSplashRequest>();
        m.Add(null, Attack(), Cast, Tags(0), 1000, Paths, null);
        Tick(m, 1000, s, dead: true);
        Assert.That(s, Has.Count.EqualTo(1));
    }

    [Test]
    public void TheLeadFillWaitsForTheSpawnAndTheHoldBeforeTheEnd()
    {
        var m = new OwnTagMagics();
        var s = new List<SkillSplashRequest>();
        m.Add(null, Attack(20), Cast, Tags(0), 1000, Paths, null);   // delay 100, spawn 200, hold 300
        Tick(m, 1050, s, lead: 50);
        Assert.That((m.Count, s.Count), Is.EqualTo((1, 0)), "filled before the delay's end, not ended");
        Tick(m, 1100, s);
        Tick(m, 1299, s);
        Assert.That(s, Is.Empty, "state 1 until spawnTime passed");
        Tick(m, 1300, s);
        Assert.That((m.Count, s.Count), Is.EqualTo((1, 0)), "state 2: the hold starts (m_nD0 = now + 300)");
        Tick(m, 1600, s);
        Assert.That(s, Is.Empty, "m_nD0 < now: not yet at 1600");
        Tick(m, 1601, s);
        Assert.That((m.Count, s.Count), Is.EqualTo((0, 1)));
    }

    [Test]
    public void ALifeTimeEndExplodesOnlyByExplosionByDestroy()
    {
        foreach (var (magic, splash) in new[] { (21u, 1), (22u, 0) })
        {
            var m = new OwnTagMagics();
            var s = new List<SkillSplashRequest>();
            m.Add(null, Attack(magic), Cast, Tags(0), 1000, Paths, null);
            Tick(m, 1600, s);   // state 2 at 1500, life 100 ran out: m_n52C 1, no fill
            Assert.That((m.Count, s.Count), Is.EqualTo((0, splash)), $"magic {magic}");
        }
    }

    [Test]
    public void AMovingTagMagicEndsWhereItsFlightEnds()
    {
        var m = new OwnTagMagics();
        var s = new List<SkillSplashRequest>();
        m.Add(null, Attack(23), Cast, Tags(0), 1000, Paths, null);   // 500 at 1000/s
        Tick(m, 1000, s);
        Assert.That((m.Count, s.Count), Is.EqualTo((1, 0)), "filled, still flying");
        for (int t = 1016; t < 3000 && m.Count != 0; t += 16) Tick(m, t, s);
        Assert.That(s, Has.Count.EqualTo(1));
        Assert.That(Vector3.Distance(s[0].Position, Cast.Position), Is.EqualTo(500f).Within(0.5f), "the end point, not the start");
    }

    [Test]
    public void TheEndPlaysTheArrowsRemainEffect()
    {
        // FUN_14145f570 at the end check: effectRemain, kind 0, at the current point
        var a = Attack(cube: 0);
        a.Arrow.EffectRemain = "Eff_Remain.xml";
        var m = new OwnTagMagics();
        var fx = new List<MagicEndEffect>();
        m.Add(null, a, Cast, Tags(0), 1000, Paths, null);
        Tick(m, 1000, new List<SkillSplashRequest>(), fx);
        Assert.That(fx.Select(f => (f.File, f.Along, f.At)).ToList(), Is.EqualTo(new List<(string, bool, Vector3)> { ("Eff_Remain.xml", false, Cast.Position) }));
    }

    [Test]
    public void EveryTagAttackWithACubePathSendsOneSplash()
    {
        // whole list over the data: every attack that is not an arrow record, of a skill without magicControl 2, with a
        // cubeMagicPathID and a magic path row, ends with exactly one splash for the local owner; none without a cubeMagicPathID
        string data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) Assert.Ignore("MS2_DATA is not set");
        var tables = new GameTables(Ms2.Formats.Pack.PackSet.Shared(data), "en");
        var paths = tables.MagicPaths;
        int withCube = 0, without = 0, skills = 0, noRow = 0, withFx = 0;
        var fx = new List<MagicEndEffect>();
        var fxFiles = new HashSet<string>();
        var bad = new List<string>();
        var s = new List<SkillSplashRequest>();
        foreach (var skill in tables.Skills.Skills.Values)
        {
            if (skill.MagicControlEnable == 2) continue;
            bool counted = false;
            foreach (var level in skill.Levels)
                foreach (var motion in level.Motions)
                    foreach (var attack in motion.Attacks)
                    {
                        if (attack.ArrowRecord) continue;
                        if (paths.Get(attack.MagicPathId, 0) == null) { noRow++; continue; }
                        var m = new OwnTagMagics();
                        s.Clear();
                        fx.Clear();
                        m.Add(skill, attack, Cast, Tags(0), 1000, paths, null);
                        // the row's delay and spawn (49161041: delayTime 1500 s), then up to 60 s of flight and hold
                        var row = paths.Get(attack.MagicPathId, 0);
                        int until = 1000 + MagicPathTable.Ms(row.DelayTime) + MagicPathTable.Ms(row.SpawnTime) + row.MoveEndHoldDuration + 60000;
                        Tick(m, 1000, s, fx);
                        int t = 1000 + Math.Max(0, MagicPathTable.Ms(row.DelayTime) + MagicPathTable.Ms(row.SpawnTime) - 16);
                        for (; t < until && m.Count != 0; t += 16) Tick(m, t, s, fx);
                        if (fx.Count != 0) withFx++;
                        foreach (var f in fx) fxFiles.Add(f.File);
                        int want = attack.CubeMagicPathId != 0 ? 1 : 0;
                        if (m.Count != 0 || s.Count != want) bad.Add($"{skill.Id} L{level.Level} cube {attack.CubeMagicPathId} path {attack.MagicPathId} {paths.Get(attack.MagicPathId, 0)}: {s.Count} splashes, {m.Count} left");
                        if (want == 1) { withCube++; if (!counted) { skills++; counted = true; } }
                        else without++;
                    }
        }
        TestContext.Progress.WriteLine($"tag attacks: {withCube} with a cube path ({skills} skills), {without} without, {noRow} with no path row; " +
            $"{withFx} play end effects ({fxFiles.Count} files: {string.Join(", ", fxFiles.Take(8))})");
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
        Assert.That(withCube, Is.GreaterThan(0));
    }
}
