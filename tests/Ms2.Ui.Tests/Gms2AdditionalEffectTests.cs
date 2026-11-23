using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The GMS2 data's additional effects: one additionaleffect/&lt;id&gt;.xml per effect with "Property"-named children, read as
/// the KMS additional/ rows. The data tests need MS2_GMS2_DATA.</summary>
[NonParallelizable]   // sets process-wide hooks the product reads
public class Gms2AdditionalEffectTests
{
    [Test]
    public void AGms2FileBecomesTheKmsRow()
    {
        var root = XElement.Parse(
            "<ms2>" +
            "<level><beginCondition level=\"1\"/><BasicProperty level=\"2\" buffType=\"2\" durationTick=\"5000\"/>" +
            "<UIProperty icon=\"b.png\" iconOnOff=\"1\"/><InvokeEffectProperty skillID=\"7\" types=\"2,3\" values=\"4.000000,0.000000\" rates=\"0.5\"/></level>" +
            "<level feature=\"F\"><beginCondition level=\"1\"/><BasicProperty level=\"1\" buffType=\"2\" durationTick=\"3000\"/>" +
            "<MotionProperty hide=\"1\" superArmor=\"3\"/><DefensiveProperty invincible=\"1\"/><UIProperty icon=\"a.png\" iconOnOff=\"2\"/></level>" +
            "</ms2>");
        var row = AdditionalTable.FromGms2(42, root);
        Assert.That(((string)row.Attribute("id"), (string)row.Attribute("isDebuff")), Is.EqualTo(("42", "1")));
        var d = AdditionalData.Load(row, _ => true);
        Assert.That((d.Id, d.IsDebuff), Is.EqualTo((42u, true)));
        var l1 = d.GetLevel(1);
        var l2 = d.GetLevel(2);
        Assert.That((l1.Level, l1.DurationTick, l1.Icon, l1.IconOnOff, l1.Hide, l1.SuperArmor, l1.Invincible),
            Is.EqualTo(((ushort)1, 3000u, "a.png", 2, 1, 3u, 1)), "the levels sorted by BasicProperty@level, the Property suffix dropped");
        Assert.That((l2.Level, l2.DurationTick, l2.Icon, l2.InvokeSkillId, l2.InvokeTypes, l2.InvokeValues, l2.InvokeRates),
            Is.EqualTo(((ushort)2, 5000u, "b.png", 7u, new[] { 2, 3 }, new[] { 4, 0 }, new[] { 0.5f })), "float values as whole numbers");
        Assert.That(row.Elements("level").Last().Element("UI"), Is.Not.Null);
        Assert.That((string)AdditionalTable.FromGms2(1, XElement.Parse("<ms2><level><BasicProperty level=\"1\" buffType=\"1\"/></level></ms2>"))
            .Attribute("isDebuff"), Is.EqualTo("0"), "buffType 1: a buff");
    }

    /// <summary>The penalty icon (100000003) and some of the buffs the GMS2 recordings send most (Buff mode 0 ids and levels).</summary>
    [Test]
    public void TheGms2DataHasTheRecordedBuffsAndThePenalty()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        using var packs = new PackSet(d);
        Assert.That(packs.List("Xml").Any(n => n.StartsWith("additional/", StringComparison.OrdinalIgnoreCase)), Is.False, "GMS2 has no additional/");
        var t = GameTables.NewAdditionalTable(packs);
        void Check(uint id, ushort level, bool buff, uint duration, bool icon)
        {
            var l = t.GetLevel(id, level);
            Assert.That(l, Is.Not.Null, $"{id} level {level}");
            Assert.That((l.Level, l.IsBuff, l.DurationTick, l.ShowIcon(true)), Is.EqualTo((level, buff, duration, icon)), $"{id} level {level}");
        }
        Check(100000003, 1, false, 60000000, true);       // the revival penalty, deathPenalty.png
        Check(120020133, 7, false, 10000, true);
        Check(100000055, 11, true, 0, false);
        Check(70000110, 1, true, 1000, false);
        Assert.That(t.GetLevel(100000003, 1).Icon, Does.EndWith("deathPenalty.png"));
        Assert.That(t.GetAdditional(99999999), Is.Null);
        Assert.That(t.GetAdditional(10900074)?.IsDebuff, Is.True);
    }

    /// <summary>The per-install cache of the GMS2 data holds the GMS2 effects.</summary>
    [Test]
    public void TheGms2CacheHasTheEffects()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        var dir = Path.Combine(Path.GetTempPath(), "ms2-gms2-additional-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, AdditionalCache.FileName("test"));
        try
        {
            using var packs = new PackSet(d);
            AdditionalCache.Build(packs, path);
            var cache = AdditionalCache.Open(path);
            Assert.That(cache.Valid, Is.True);
            var xml = GameTables.NewAdditionalTable(packs);
            var cached = GameTables.NewAdditionalTable(packs, cache);
            foreach (var (id, lv) in new (uint, ushort)[] { (100000003, 1), (120020133, 7), (100000055, 11), (70000110, 1), (10900074, 10) })
            {
                var (a, b) = (xml.GetLevel(id, lv), cached.GetLevel(id, lv));
                Assert.That(b, Is.Not.Null, $"{id}");
                Assert.That((b.Level, b.IsBuff, b.DurationTick, b.Icon, b.IconOnOff), Is.EqualTo((a.Level, a.IsBuff, a.DurationTick, a.Icon, a.IconOnOff)), $"{id}");
            }
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
