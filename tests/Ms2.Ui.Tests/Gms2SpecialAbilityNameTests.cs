using Ms2.Formats.Pack;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The GMS2 data has no special ability table, so no ability had a name (the character window's rate rows and the
/// other ability texts were blank); the names now come from its s_item_opt_sa_ common strings
/// (<see cref="Gms2SpecialAbilities.LegacyNameKeys"/>).</summary>
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
}
