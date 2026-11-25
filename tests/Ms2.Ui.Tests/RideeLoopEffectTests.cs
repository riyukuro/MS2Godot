using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2Godot.Movement;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The ridee's loop effect (Ridee::vfunc_12, ridee.cpp:527-534; ~Ridee, ridee.cpp:402-406): <see cref="RideeLoopEffect"/>
/// and every riding row's loopEffect of a data set.
/// </summary>
public class RideeLoopEffectTests
{
    [Test]
    public void TheLoopEffectIsKeptUntilTheRideeGoes()
    {
        var fx = new object();
        var spawned = new List<string>();
        var stopped = new List<int>();
        int Spawn(string path) { spawned.Add(path); return 7; }
        var loop = new RideeLoopEffect();

        Assert.That(loop.Start(null, fx, Spawn, stopped.Add), Is.False, "a row without a loopEffect");
        Assert.That(loop.Start("", fx, Spawn, stopped.Add), Is.False);
        Assert.That(spawned, Is.Empty);
        Assert.That(loop.Handle, Is.Zero);

        Assert.That(loop.Start("Item/Ride/Eff_Item_Ride_HalloweenStorm_Loop.xml", fx, Spawn, stopped.Add), Is.True);
        Assert.That(spawned, Is.EqualTo(new[] { "Item/Ride/Eff_Item_Ride_HalloweenStorm_Loop.xml" }));
        Assert.That(loop.Handle, Is.EqualTo(7));
        Assert.That(stopped, Is.Empty, "kept while the ridee is in");

        Assert.That(loop.Stop(fx, stopped.Add), Is.True, "~Ridee stops it");
        Assert.That(stopped, Is.EqualTo(new[] { 7 }));
        Assert.That(loop.Handle, Is.Zero);
        Assert.That(loop.Stop(fx, stopped.Add), Is.False, "a second release stops nothing");
        Assert.That(stopped, Has.Count.EqualTo(1));
    }

    [Test]
    public void ANewRideeStopsTheOldOnesEffectFirst()
    {
        var fx = new object();
        var stopped = new List<int>();
        int next = 1;
        var loop = new RideeLoopEffect();
        loop.Start("a.xml", fx, _ => next++, stopped.Add);
        loop.Start("b.xml", fx, _ => next++, stopped.Add);
        Assert.That(stopped, Is.EqualTo(new[] { 1 }));
        Assert.That(loop.Handle, Is.EqualTo(2));
        loop.Start(null, fx, _ => next++, stopped.Add);
        Assert.That(stopped, Is.EqualTo(new[] { 1, 2 }), "a mount without one still releases the last");
        Assert.That(loop.Handle, Is.Zero);
    }

    [Test]
    public void AHandleOfAnotherEffectLayerIsNotStopped()
    {
        var old = new object();
        var stopped = new List<int>();
        var loop = new RideeLoopEffect();
        loop.Start("a.xml", old, _ => 3, stopped.Add);
        Assert.That(loop.Stop(new object(), stopped.Add), Is.False, "the field's effects were replaced");
        Assert.That(stopped, Is.Empty);
        Assert.That(loop.Handle, Is.Zero);

        Assert.That(loop.Start("a.xml", null, _ => 3, stopped.Add), Is.False, "no effect layer, nothing kept");
        Assert.That(loop.Start("a.xml", old, _ => 0, stopped.Add), Is.False, "an effect that did not spawn");
        Assert.That(loop.Stop(old, stopped.Add), Is.False);
        Assert.That(stopped, Is.Empty);
    }

    /// <summary>Every riding row with a loopEffect names an effect xml of the data set that has parts.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryLoopEffectRowNamesAnEffect(string env)
    {
        using var packs = new PackSet(RcGap.Data(env));
        var effects = new EffectTable(packs);
        var ids = packs.List("Xml").Where(e => e.StartsWith("riding/") && e.Count(c => c == '/') == 1 && e.EndsWith(".xml"))
            .Select(e => int.TryParse(e[7..^4], out var id) ? id : -1).Where(id => id > 0).OrderBy(id => id).ToList();
        var rows = new List<string>();
        var missing = new List<string>();
        foreach (var id in ids)
        {
            if (PackXml.Parse(packs.Read("Xml", $"riding/{id:D8}.xml")).Root?.Element("riding")?.Element("basic") == null) continue;
            var d = RideData.Load(packs, id);
            if (d.LoopEffect == null) continue;
            rows.Add($"{id}:{d.LoopEffect}");
            if (effects.Get(d.LoopEffect) is not { Count: > 0 }) missing.Add($"{id}:{d.LoopEffect}");
        }
        TestContext.WriteLine($"{env}: {ids.Count} riding rows, {rows.Count} with a loopEffect, {rows.Select(r => r[(r.IndexOf(':') + 1)..]).Distinct().Count()} effects");
        foreach (var r in rows) TestContext.WriteLine("  " + r);
        Assert.That(rows, Is.Not.Empty, "the Halloween storm mounts (50600146-50600151) have one on both data sets");
        Assert.That(rows, Has.Some.StartsWith("50600146:"));
        Assert.That(missing, Is.Empty, "a loopEffect without its effect xml");
    }
}
