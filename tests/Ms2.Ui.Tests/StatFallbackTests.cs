using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>A pack without statStringTable.xml (the GMS2 data) takes the stat definitions from the common strings.</summary>
public class StatFallbackTests
{
    static ItemOptionTables Tables()
    {
        var common = new Dictionary<string, string>
        {
            ["s_word_stat_ndd_v"] = "Defense {0}", ["s_word_stat_int_v"] = "Intelligence +{0}", ["s_word_stat_abp_v"] = "Accuracy +{0}%",
            ["s_word_stat_wap_d_v"] = "Min Attack {0}", ["s_word_stat_wap_u_v"] = "Max Attack {0}",
        };
        var names = new Dictionary<string, string>();
        ItemOptionTables.FallbackStatTexts(common, names);
        return new ItemOptionTables(null, names, new());
    }

    [Test]
    public void StatsAreFoundByOptionName()
    {
        var t = Tables();
        Assert.That(t.FindStatByName("ndd"), Is.EqualTo(20));
        Assert.That(t.FindStatByName("INT"), Is.EqualTo(2));
        Assert.That(t.FindStatByName("rmsp"), Is.EqualTo(32));
    }

    [Test]
    public void ValueTextKeepsTheSignOfItsString()
    {
        var t = Tables();
        Assert.That(t.StatValueText(2), Is.EqualTo("Intelligence +{0}"));
        Assert.That(t.StatValueText(20), Is.EqualTo("Defense {0}"));
        Assert.That(t.IsStatRate(21), Is.True);
        Assert.That(t.IsStatRate(20), Is.False);
    }

    [Test]
    public void TheWeaponsMinAndMaxAttackUseTheirOwnKeys()
    {
        var t = Tables();
        Assert.That(t.StatValueText(27), Is.EqualTo("Min Attack {0}"));
        Assert.That(t.StatValueText(28), Is.EqualTo("Max Attack {0}"));
    }
}
