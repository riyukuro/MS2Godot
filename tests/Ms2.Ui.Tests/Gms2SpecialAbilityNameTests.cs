using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The GMS2 data has no special ability table, so no ability had a name (the character window's rate rows and the
/// other ability texts were blank); the names now come from its s_item_opt_sa_ common strings
/// (<see cref="Gms2SpecialAbilities.LegacyNameKeys"/>). The item option tables had no rows either, so no ability was a rate
/// (a 2.6% gem showed "26") and no item option special was found; they now take the stand-in rows with the _r / _v value
/// texts.</summary>
public class Gms2SpecialAbilityNameTests
{
    [Test]
    public void ALegacyKeyGivesTheAbilitysName()
    {
        var common = new Dictionary<string, string>
        {
            ["s_item_opt_sa_improve_piercing_par"] = "pierce",
            ["s_item_opt_sa_reduce_elements_ice"] = "",
            ["s_item_opt_sa_improve_piercing_par_r"] = "pierce {0}",
        };
        var names = Gms2SpecialAbilities.LegacyNames(common);
        Assert.That(names, Is.EqualTo(new Dictionary<int, string> { [0x1a] = "pierce" }), "an empty or missing text is left out");
    }

    [Test]
    public void TheKeysAreOneEachAndInTheTablesRange()
    {
        var ids = Gms2SpecialAbilities.LegacyNameKeys.Select(k => k.Id).ToList();
        Assert.That(ids, Is.Unique);
        Assert.That(Gms2SpecialAbilities.LegacyNameKeys.Select(k => k.Stem), Is.Unique);
        Assert.That(ids, Has.All.InRange(1, Gms2SpecialAbilities.DefaultRows.Length));
    }

    /// <summary>On the GMS2 data: the character window's rate rows (the piercing, range and element damage ones) and every
    /// keyed ability have the text of their common string; before, every name was "".</summary>
    [Test]
    public void TheGms2DataNamesItsAbilities()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        using var packs = new PackSet(d);
        Assume.That(packs.Exists("Xml", "table/specialabilitystringtable.xml"), Is.False, "a pack with the table names them from it");
        var t = new GameTables(packs, "en");
        var stems = Gms2SpecialAbilities.LegacyNameKeys.ToDictionary(k => k.Id, k => k.Stem);
        foreach (int id in new[] { 0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x20, 0x21, 0x25, 0x26 })
            Assert.That(t.SpecialAbilityName(id), Is.Not.Empty.And.EqualTo(t.Common("s_item_opt_sa_" + stems[id])), $"ability {id:x}");
        Assert.That(Gms2SpecialAbilities.LegacyNameKeys.Where(k => t.SpecialAbilityName(k.Id).Length == 0).Select(k => k.Stem), Is.Empty);
        Assert.That(t.SpecialAbilityName(0xcd), Is.Empty, "an ability without a key stays without a name");
    }

    [Test]
    public void AValueTextFollowsTheAbilitysKind()
    {
        var common = new Dictionary<string, string>
        {
            ["s_item_opt_sa_improve_piercing_par_v"] = "flat {0:d}",          // 0x1a parpen: a rate
            ["s_item_opt_sa_improve_piercing_par_r"] = "rate {0:0.1f}%",
            ["s_item_opt_sa_improve_speed_dash_v"] = "flat {0:d}",            // 4 dashDistance: flat
            ["s_item_opt_sa_improve_speed_dash_r"] = "rate {0:0.1f}%",
            ["s_item_opt_sa_improve_damage_final_r"] = "",                    // 7: an empty text is left out
        };
        Assume.That(Gms2SpecialAbilities.DefaultRows[0x1a - 1], Is.EqualTo(("parpen", 2)));
        Assume.That(Gms2SpecialAbilities.DefaultRows[4 - 1], Is.EqualTo(("dashDistance", 1)));
        var texts = new Dictionary<string, string> { ["1004"] = "kept" };
        Gms2SpecialAbilities.LegacyValueTexts(common, texts);
        Assert.That(texts, Is.EqualTo(new Dictionary<string, string> { ["1026"] = "rate {0:0.1f}%", ["1004"] = "kept" }));
        texts.Remove("1004");
        Gms2SpecialAbilities.LegacyValueTexts(common, texts);
        Assert.That(texts["1004"], Is.EqualTo("flat {0:d}"));
    }

    /// <summary>On the GMS2 data: every stand-in row is a special ability of the item option tables with its kind, a keyed one
    /// with its _r or _v text, and every item option attribute that names a special has that kind's suffix
    /// (&lt;name&gt;_rate_base for a rate); before, no special was a rate and none was found by name.</summary>
    [Test]
    public void TheGms2DataGivesEveryAbilityItsKindAndValueText()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        using var packs = new PackSet(d);
        Assume.That(packs.Exists("Xml", "table/specialabilitystringtable.xml"), Is.False, "a pack with the table reads its rows");
        var t = new GameTables(packs, "en");
        var o = t.ItemOptions;
        Assert.That(o.IsSpecialRate(7), Is.True, "finalAdditionalDamage, the gem's total damage");
        Assert.That(o.FindSpecialByName("finalAdditionalDamage"), Is.EqualTo(7));
        for (int i = 0; i < Gms2SpecialAbilities.DefaultRows.Length; i++)
        {
            var (option, type) = Gms2SpecialAbilities.DefaultRows[i];
            Assert.That((o.SpecialOption(i + 1), o.IsSpecialRate(i + 1)), Is.EqualTo((option, type == 2)), $"ability {i + 1}");
        }
        int texts = 0;
        foreach (var (id, stem) in Gms2SpecialAbilities.LegacyNameKeys)
        {
            string want = t.Common("s_item_opt_sa_" + stem + (o.IsSpecialRate(id) ? "_r" : "_v")) ?? "";
            Assert.That(o.SpecialValueText(id), Is.EqualTo(want), $"ability {id}");
            if (want.Length > 0) texts++;
        }
        Assert.That(texts, Is.GreaterThan(100));

        // the GMS2 option rows name each value's kind: a special's suffix is its row's
        int seen = 0;
        var wrong = new List<string>();
        foreach (var n in packs.List("Xml").Where(n => n.Replace('\\', '/').StartsWith("itemoption/", StringComparison.OrdinalIgnoreCase)))
            foreach (var e in GameStrings.Parse(packs.Read("Xml", n)).Root?.Descendants() ?? Enumerable.Empty<System.Xml.Linq.XElement>())
                foreach (var a in e.Attributes())
                {
                    string an = a.Name.LocalName;
                    bool rate = an.EndsWith("_rate_base", StringComparison.Ordinal);
                    if (!rate && !an.EndsWith("_value_base", StringComparison.Ordinal)) continue;
                    if (!double.TryParse(a.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x) || x == 0) continue;
                    string name = an[..^(rate ? "_rate_base".Length : "_value_base".Length)];
                    if (o.FindStatByName(name) != -1) continue;
                    int sp = o.FindSpecialByName(name);
                    if (sp == 0) continue;
                    seen++;
                    if (o.IsSpecialRate(sp) != rate) wrong.Add(an);
                }
        TestContext.Out.WriteLine($"{seen} special values in the option rows");
        Assert.That(seen, Is.GreaterThan(1000));
        Assert.That(wrong.Distinct(), Is.Empty);
    }
}
