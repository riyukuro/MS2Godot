using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The vibrate registry (WorldService m_5e8, worldservice_1.cpp:6819-6866) on the data: every map's MS2Vibrate
/// entities against what registers.</summary>
public class FieldVibratesDataTests
{
    static Ms2.Formats.Pack.PackSet Packs()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return new Ms2.Formats.Pack.PackSet(d);
    }

    /// <summary>The Thief intro's vault door (52000128_qd: the safe door at y 1400, in front of the wall cubes at y 1500,
    /// which are not Enabled; the trigger skill 7001 breaks it) registers, and the entries come in key order.</summary>
    [Test]
    public void TheVaultDoorRegistersInKeyOrder()
    {
        var packs = Packs();
        var map = FieldMap.Load(packs, new FlatLibrary(packs), "52000128_qd");
        var v = new FieldVibrates();
        Assert.That(v.AttachField(map.Entities, 0), Is.GreaterThan(0));
        var wall = v.Sorted.Where(e => e.Entity.ModelName == "ry_in_prop_safedoor_A01_").ToList();
        Assert.That(wall.Count, Is.EqualTo(1));
        Assert.That(wall[0].Size.X > 0 && wall[0].Size.Y > 0 && wall[0].Size.Z > 0, Is.True);
        Assert.That(map.Entities.Where(e => e.ModelName == "ry_wall_metal_A01_" && e.Position.Y == 1500).Any(FieldVibrates.Registers), Is.False);
        for (int i = 1; i < v.Sorted.Count; i++)
            Assert.That(string.CompareOrdinal(v.Sorted[i - 1].Name, v.Sorted[i].Name), Is.LessThan(0));
        Assert.That(v.Find(wall[0].Name), Is.SameAs(wall[0]));
    }

    /// <summary>Every map: the MS2Vibrate entities, the ones that register (Enabled and a size), and that each registered
    /// key is its entity id lowercased and found again.</summary>
    [Test]
    public void EveryMapsVibrateObjectsRegister()
    {
        var packs = Packs();
        var flats = new FlatLibrary(packs);
        int maps = 0, failed = 0, withAny = 0, mixin = 0, registered = 0, disabled = 0, noSize = 0, most = 0;
        string mostMap = "";
        foreach (var entry in packs.List(XBlock.Archive).Where(n => n.StartsWith("xblock/") && n.EndsWith(".xblock")).OrderBy(n => n, StringComparer.Ordinal))
        {
            string name = entry["xblock/".Length..^".xblock".Length];
            FieldMap map;
            try { map = FieldMap.Load(packs, flats, name); } catch (Exception) { failed++; continue; }
            maps++;
            var v = new FieldVibrates();
            int n = v.AttachField(map.Entities, 0);
            foreach (var e in map.Entities)
            {
                if (!e.HasMixin(FieldVibrates.Model)) continue;
                mixin++;
                if (FieldVibrates.Registers(e))
                {
                    var found = v.Find(e.Raw.Id.ToLowerInvariant());
                    Assert.That(found?.Entity, Is.SameAs(e), $"{name}: {e.Raw.Id}");
                }
                else if (!e.Bool("Enabled")) disabled++;
                else noSize++;
            }
            registered += n;
            if (n > most) { most = n; mostMap = name; }
            if (n > 0) withAny++;
        }
        TestContext.Out.WriteLine($"{maps} maps ({failed} unreadable), {withAny} with vibrate objects; {mixin} MS2Vibrate entities: {registered} registered, {disabled} not Enabled, {noSize} without a size; most on one map {most} ({mostMap})");
        Assert.That(maps, Is.GreaterThan(0));
        Assert.That(registered, Is.GreaterThan(0));
        Assert.That(registered + disabled + noSize, Is.EqualTo(mixin));
    }
}
