using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The GMS2 data's additional effects: one additionaleffect/&lt;id&gt;.xml per effect with "Property"-named children, read as
/// the KMS additional/ rows. The data tests need MS2_GMS2_DATA.</summary>
[NonParallelizable]   // sets process-wide hooks the product reads
public class Gms2AdditionalEffectTests
{
    static string Show(Dictionary<int, int> m) => m == null ? "" : string.Join(",", m.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

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

    /// <summary>The GMS2 attribute form of the special abilities becomes the KMS &lt;v type value&gt; list (Gms2SpecialAbilities).</summary>
    [Test]
    public void TheGms2AbilityAttributesBecomeTheKmsList()
    {
        var level = XElement.Parse(
            "<level><BasicProperty level=\"1\"/>" +
            "<StatusProperty statChangeRate=\"0.5\"><Stat mspvalue=\"35\" msprate=\"-0.3\" penrate=\"0.01\" sp_invrate=\"-1.000000\" wapminrate=\"0.01\" " +
            "wapmaxrate=\"0.02\" hpvalue=\"0\" penvalue=\"3\"/><SpecialAbility icedamagerate=\"0.08\" sgi_bossrate=\"0.03\" " +
            "improve_survival_kill_exprate=\"0.1\"/></StatusProperty>" +
            "<OffensiveProperty papDamageR=\"0.13\" mapDamageR=\"0.14\" hitImmuneBreak=\"1\"/>" +
            "<DefensiveProperty papDamageR=\"0.1\" papDamageV=\"-45\" mapDamageV=\"-45\"/></level>");
        var sa = Gms2SpecialAbilities.FromLevel(level);
        Assert.That(sa.Elements("v").Select(v => ((string)v.Attribute("type"), (int)v.Attribute("value"))), Is.EqualTo(new[]
        {
            ("msp", 35), ("mspRate", -300), ("pen", 10), ("spInvRate", -1000), ("wapRate", 10), ("iceDamage", 80), ("sgi_boss", 30),
            ("offensive_physicaldamage", 130), ("offensive_magicaldamage", 140), ("defensive_physicaldamage", 100), ("defensive_damageV", -45),
        }), "per-mille rates, flat values, the min weapon rate; no KMS row for penvalue or improve_survival_kill_exprate");
        Assert.That(Gms2SpecialAbilities.FromLevel(XElement.Parse("<level><StatusProperty><Stat hpvalue=\"0\"/></StatusProperty></level>")), Is.Null);
        // the row FromGms2 makes carries the list, and the stand-in table resolves its names
        var row = AdditionalTable.FromGms2(7, new XElement("ms2", level));
        var types = GameTables.SpecialAbilityTypes(Gms2SpecialAbilities.DefaultTable());
        var d = AdditionalData.Load(row, _ => true, types);
        Assert.That(d.GetLevel(1).SpecialAbilities, Is.EquivalentTo(new Dictionary<int, int>
        {
            [187] = 35, [53] = -300, [199] = 10, [47] = -1000, [6] = 10, [18] = 80, [12] = 30, [177] = 130, [178] = 140, [175] = 100, [171] = -45,
        }));
        Assert.That((Gms2SpecialAbilities.DefaultRows.Length, types("seg"), types("reduce_do_heal_hp"), types("nosuch")), Is.EqualTo((206, 1, 206, 0)));
    }

    /// <summary>The KMS2_DEV data has both forms: per level in both, the GMS2 form's list is the KMS one (the rows picked cover
    /// each rule), and over all of them nearly every level gives the same list (the rest are retuned values).</summary>
    [Test]
    public void TheKmsDevBothFormsGiveTheSameAbilities()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new PackSet(d);
        var gms2 = packs.List("Xml").Where(AdditionalTable.IsGms2File).ToList();
        if (gms2.Count == 0 || !packs.List("Xml").Any(n => n.StartsWith("additional/", StringComparison.OrdinalIgnoreCase)))
            Assert.Ignore("the data does not have both forms");
        var kms = GameTables.NewAdditionalTable(packs);
        var types = GameTables.SpecialAbilityTypes(packs);
        AdditionalData Gms2(uint id) => AdditionalData.Load(AdditionalTable.FromGms2(id, GameStrings.Parse(packs.Read("Xml",
            AdditionalTable.Gms2Folder + id + ".xml")).Root), _ => true, types);
        foreach (var (id, lv) in new (uint, ushort)[] { (10100125, 1), (10100061, 1), (10100111, 1), (10800283, 1), (100000015, 1),
                     (10100202, 1), (10200151, 1), (110001005, 1), (10300061, 1), (10300111, 1) })
        {
            var k = kms.GetLevel(id, lv)?.SpecialAbilities;
            Assert.That(k, Is.Not.Null.And.Not.Empty, $"{id}");
            Assert.That(Show(Gms2(id).GetLevel(lv).SpecialAbilities), Is.EqualTo(Show(k)), $"{id} level {lv}");
        }
        int same = 0, total = 0;
        foreach (var n in gms2)
        {
            if (!uint.TryParse(Path.GetFileNameWithoutExtension(n), out uint id) || kms.GetAdditional(id) is not { } a) continue;
            var g = Gms2(id);
            foreach (var l in a.Levels)
            {
                if (g?.Levels.FirstOrDefault(x => x.Level == l.Level) is not { } gl) continue;
                total++;
                if (Show(gl.SpecialAbilities) == Show(l.SpecialAbilities)) same++;
            }
        }
        TestContext.Out.WriteLine($"same {same} of {total}");
        Assert.That(same, Is.GreaterThan(total * 97 / 100), $"same {same} of {total}");
    }

    /// <summary>The GMS2 recordings' common buffs with abilities: their lists by the stand-in ids.</summary>
    [Test]
    public void TheGms2RecordedBuffsHaveTheirAbilities()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        using var packs = new PackSet(d);
        Assert.That(packs.Exists("Xml", "table/specialabilitystringtable.xml"), Is.False, "GMS2 has no special ability table");
        var t = GameTables.NewAdditionalTable(packs);
        var types = GameTables.SpecialAbilityTypes(packs);
        Dictionary<int, int> Of(params (string, int)[] v) => v.ToDictionary(p => types(p.Item1), p => p.Item2);
        Assert.That(t.GetLevel(10900155, 1).SpecialAbilities, Is.EquivalentTo(Of(("mspRate", -300), ("jmpRate", -300), ("rmspRate", -300))));
        Assert.That(t.GetLevel(120020133, 7).SpecialAbilities, Is.EquivalentTo(Of(("papRate", -30), ("mapRate", -30))));
        Assert.That(t.GetLevel(100000055, 11).SpecialAbilities, Is.EquivalentTo(Of(("reduce_gameitem_socket_unlock_fee", 200))));
        Assert.That(t.GetLevel(100000035, 8).SpecialAbilities, Is.EquivalentTo(Of(("smd", 80))));
        Assert.That(t.GetLevel(100000003, 1).SpecialAbilities, Is.Null, "the revival penalty has none");
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
            foreach (var (id, lv) in new (uint, ushort)[] { (100000003, 1), (120020133, 7), (100000055, 11), (70000110, 1), (10900074, 10), (10900155, 1) })
            {
                var (a, b) = (xml.GetLevel(id, lv), cached.GetLevel(id, lv));
                Assert.That(b, Is.Not.Null, $"{id}");
                Assert.That((b.Level, b.IsBuff, b.DurationTick, b.Icon, b.IconOnOff), Is.EqualTo((a.Level, a.IsBuff, a.DurationTick, a.Icon, a.IconOnOff)), $"{id}");
                Assert.That(Show(b.SpecialAbilities), Is.EqualTo(Show(a.SpecialAbilities)), $"{id} abilities");
            }
            // a stun effect's Motion stun (the own pc's StateStun) from the cache as from the XML
            var (sx, sc) = (xml.GetLevel(40353001, 1), cached.GetLevel(40353001, 1));
            Assert.That(sc, Is.Not.Null, "40353001");
            Assert.That((sx.Stun, sx.StunAnimation, sx.MaxBuffCount), Is.EqualTo((2u, "", 1u)), "40353001 stun in the XML");
            Assert.That((sc.Stun, sc.StunAnimation, sc.MaxBuffCount), Is.EqualTo((sx.Stun, sx.StunAnimation, sx.MaxBuffCount)), "40353001 stun in the cache");
            // the names and tooltips from the split koradditionaldescription files, the same from the cache
            var keep = GameTables.AdditionalCacheOf;
            try
            {
                var fromXml = new GameTables(packs, "en");
                GameTables.AdditionalCacheOf = _ => path;
                var fromCache = new GameTables(packs, "en");
                foreach (var (id, lv) in new (uint, ushort)[] { (100000003, 1), (120020133, 7), (100000001, 1) })
                    foreach (var tag in new[] { "name", "tooltipDescription" })
                        Assert.That(fromCache.AdditionalDescription(id, lv, tag), Is.Not.Empty.And.EqualTo(fromXml.AdditionalDescription(id, lv, tag)), $"{id} {tag}");
            }
            finally { GameTables.AdditionalCacheOf = keep; }
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
