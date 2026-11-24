using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The magic paths (LoadMagicPath, FUN_14145c300) and the own cast's magics' registrations (CMagic::FUN_14145de80).</summary>
public class OwnTagMagicsTests
{
    static readonly MagicPathTable Paths = new(XDocument.Parse(
        "<ms2>" +
        "<type id=\"5\"><move delayTime=\"1\" spawnTime=\"0.2\" lifeTime=\"0\" destroyTime=\"0\"/>" +
        "<move delayTime=\"0.5\" spawnTime=\"0\" lifeTime=\"0.1\" destroyTime=\"0\"/></type>" +
        "<type id=\"6\"><move delayTime=\"9\"/></type>" +
        "<type id=\"6\"><move delayTime=\"0\" spawnTime=\"0.3\"/></type>" +
        "<type id=\"0\"><move delayTime=\"4\"/></type>" +
        "</ms2>"));

    static SkillAttackData Attack(uint magic = 0, int brokenOffence = 1, int method = 0, int shape = 1)
    {
        var a = new SkillAttackData { MagicPathId = magic, BrokenOffence = brokenOffence, AttackMethodType = method };
        a.Range.Shape = shape;
        a.Range.Width = 200; a.Range.Distance = 200; a.Range.Height = 200;
        return a;
    }

    static List<SkillDamageTarget> Tags(params byte[] hits) =>
        hits.Select(h => new SkillDamageTarget(1, 2, h, Array.Empty<SkillDamageLink>())).ToList();

    static MagicOwner Owner(bool local = true, bool dead = false, bool stunned = false) => new(Vector3.Zero, 0, local, dead, stunned);

    [Test]
    public void PathsPickTheRowModuloTheCount()
    {
        Assert.That(Paths.Get(5, 0).DelayTime, Is.EqualTo(1f));
        Assert.That(Paths.Get(5, 3).DelayTime, Is.EqualTo(0.5f), "index 3 of two rows is row 1");
        Assert.That(Paths.Get(6, 0).SpawnTime, Is.EqualTo(0.3f), "a repeated id replaces the earlier list");
        Assert.That(Paths.Get(0, 7), Is.EqualTo(new MagicPathMove(0, 0, 0, 0)), "id 0 is one row of zeros");
        Assert.That(Paths.Get(99, 0), Is.Null);
        Assert.That(MagicPathTable.Empty.Get(0, 0), Is.Not.Null);
        Assert.That(MagicPathTable.Ms(0.2f), Is.EqualTo(200));
    }

    [Test]
    public void NonArrowAttacksWithAPathMakeMagics()
    {
        var m = new OwnTagMagics();
        Assert.That(m.Add(null, Attack(brokenOffence: 0), Tags(0), 0, Paths), Is.EqualTo(1), "brokenOffence 0: the fill only");
        m.Clear();
        Assert.That(m.Add(null, Attack(method: 1), Tags(0), 0, Paths), Is.EqualTo(1), "attackMethodType 1: an arrow magic");
        m.Clear();
        Assert.That(m.Add(new SkillData { MagicControlEnable = 1 }, Attack(method: 1), Tags(0), 0, Paths), Is.EqualTo(0),
            "an arrow with magic control: the controlled flight is not ported");
        Assert.That(m.Add(new SkillData { MagicControlEnable = 2 }, Attack(), Tags(0), 0, Paths), Is.EqualTo(0), "magicControl 2: no fill");
        Assert.That(m.Add(null, Attack(magic: 99), Tags(0, 1), 0, Paths), Is.EqualTo(0), "an unknown id makes no magic");
        Assert.That(m.Add(null, Attack(), Tags(0, 1, 2), 0, Paths), Is.EqualTo(3), "one magic per target record");
        Assert.That(m.Count, Is.EqualTo(3));
    }

    [Test]
    public void ADelayZeroMagicRegistersOnceAtItsFirstTick()
    {
        var m = new OwnTagMagics();
        var pass = new TombTagPass();
        m.Add(null, Attack(), Tags(0), 1000, Paths);
        Assert.That(m.Tick(1000, 0, Owner(), pass), Is.EqualTo(1));
        Assert.That((m.Count, pass.Count), Is.EqualTo((0, 1)));
        Assert.That(m.Tick(1016, 0, Owner(), pass), Is.EqualTo(0));
    }

    [Test]
    public void TheDelayIsDueAtItsEndLessTheLead()
    {
        var m = new OwnTagMagics();
        var pass = new TombTagPass();
        m.Add(null, Attack(magic: 5), Tags(0), 1000, Paths);   // delay 1000, spawn 200
        Assert.That(m.Tick(1899, 0, Owner(), pass), Is.EqualTo(0));
        Assert.That(m.Tick(1900, 100, Owner(), pass), Is.EqualTo(1), "start + delay - lead");

        m.Add(null, Attack(magic: 5), Tags(0), 1000, Paths);
        Assert.That(m.Tick(1999, 0, Owner(), pass), Is.EqualTo(0));
        Assert.That(m.Tick(2000, 0, Owner(), pass), Is.EqualTo(0), "after the delay: state 1 until spawnTime passed");
        Assert.That(m.Tick(2199, 0, Owner(), pass), Is.EqualTo(0));
        Assert.That(m.Tick(2200, 0, Owner(), pass), Is.EqualTo(1));
        Assert.That(m.Count, Is.EqualTo(0));
    }

    [Test]
    public void ALifeTimeThatRanOutRegistersNothing()
    {
        var m = new OwnTagMagics();
        var pass = new TombTagPass();
        m.Add(null, Attack(magic: 5), Tags(1), 1000, Paths);   // row 1: delay 500, spawn 0, life 100
        Assert.That(m.Tick(1600, 0, Owner(), pass), Is.EqualTo(0), "state 3: no fill, no registration");
        Assert.That((m.Count, pass.Count), Is.EqualTo((0, 0)));

        m.Add(null, Attack(magic: 5), Tags(1), 1000, Paths);
        Assert.That(m.Tick(1599, 0, Owner(), pass), Is.EqualTo(1));
    }

    [Test]
    public void TheFillNeedsALiveLocalOwnerAndACollision()
    {
        var pass = new TombTagPass();
        foreach (var o in new[] { Owner(local: false), Owner(dead: true), Owner(stunned: true) })
        {
            var m = new OwnTagMagics();
            m.Add(null, Attack(), Tags(0), 1000, Paths);
            Assert.That(m.Tick(1000, 0, o, pass), Is.EqualTo(0));
            Assert.That(m.Count, Is.EqualTo(0), "one attempt, then the end");
        }
        var none = new OwnTagMagics();
        none.Add(null, Attack(shape: 0), Tags(0), 0, Paths);
        Assert.That(none.Tick(0, 0, Owner(), pass), Is.EqualTo(0), "no shape 1-4: no collision kept");
        Assert.That(pass.Count, Is.EqualTo(0));
    }

    // CMagicModel::CanCancel / CMagicService::RemoveCasterModels: 7 = delay 0 spawn 0.3; 8 = the same with
    // ignoreCancelAtSpawnTime; 5 row 0 = delay 1
    static readonly MagicPathTable CancelPaths = new(XDocument.Parse(
        "<ms2><type id=\"7\"><move delayTime=\"0\" spawnTime=\"0.3\"/></type>" +
        "<type id=\"8\"><move delayTime=\"0.0000001\" spawnTime=\"0.3\" ignoreCancelAtSpawnTime=\"1\"/></type>" +
        "<type id=\"5\"><move delayTime=\"1\"/></type></ms2>"));

    static int AddCast(OwnTagMagics m, long castId, uint magic, int startMs, int bounce = 0)
    {
        var a = Attack(magic);
        a.Arrow.BounceType = bounce;
        return m.Add(null, a, new CastRecord { CastId = castId }, Tags(0), startMs, CancelPaths, null);
    }

    [Test]
    public void IgnoreCancelAtSpawnTimeLoads()
    {
        Assert.That(CancelPaths.Get(8, 0).IgnoreCancelAtSpawnTime, Is.True);
        Assert.That(CancelPaths.Get(7, 0).IgnoreCancelAtSpawnTime, Is.False);
    }

    [Test]
    public void TheStateExitDropsTheCastsDelayZeroMagicsInTheirSpawnWait()
    {
        var m = new OwnTagMagics();
        var pass = new TombTagPass();
        AddCast(m, 70, 7, 1000);
        AddCast(m, 71, 7, 1000);
        Assert.That(m.Tick(1100, 0, Owner(), pass), Is.EqualTo(0), "the spawn wait");
        Assert.That(m.CancelCaster(70, 1100), Is.EqualTo(1));
        Assert.That(m.Count, Is.EqualTo(1), "another cast's magic stays");
        Assert.That(m.Tick(1300, 0, Owner(), pass), Is.EqualTo(1), "only cast 71's magic fills");
    }

    [Test]
    public void TheCancelKeepsDelayedBouncingAndIgnoringMagics()
    {
        var m = new OwnTagMagics();
        AddCast(m, 70, 5, 1000);                   // delay 1 s
        AddCast(m, 70, 7, 1000, bounce: 2);        // the arrow bounceType 2
        AddCast(m, 70, 8, 1000);                   // delay nearly 0, ignoreCancelAtSpawnTime
        Assert.That(m.CancelCaster(70, 1100), Is.EqualTo(0));
        Assert.That(m.Count, Is.EqualTo(3));
        var early = new OwnTagMagics();
        AddCast(early, 70, 8, 1000);
        Assert.That(early.CancelCaster(70, 999), Is.EqualTo(1), "state 0: ignoreCancelAtSpawnTime only keeps state 1");
    }

    [Test]
    public void TheClientsMagicPathsRead()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var paths = new GameTables(new PackSet(d), "en").MagicPaths;
        Assert.That(paths.Get(0, 3), Is.EqualTo(new MagicPathMove(0, 0, 0, 0)), "id 0 is set to zeros after the load");
        var row = paths.Get(5015, 0);
        Assert.That(row, Is.Not.Null);
        Assert.That((row.DelayTime, row.SpawnTime), Is.EqualTo((1f, 0.2f)));
    }

    [Test]
    public void TheTagCarriesTheAttacksBrokenOffenceAndHit()
    {
        var m = new OwnTagMagics();
        var pass = new TombTagPass();
        int hits = 0;
        m.Add(null, Attack(brokenOffence: 3), Tags(0), 0, Paths, () => hits++);
        m.Tick(0, 0, Owner(), pass);
        var tomb = new Tombstone("Regen_A", "Damg_A", "Idle_A", "Dead_A", n => 0);
        tomb.Breakable(true, new Vector3(100, 100, 150), true, 4);
        var found = new List<TombHit>();
        Assert.That(pass.Run(new[] { new TombTarget(77, false, tomb, new Vector3(0, -100, 0), Vector3.Zero) }, found), Is.EqualTo(1));
        Assert.That(hits, Is.EqualTo(1));
    }
}
