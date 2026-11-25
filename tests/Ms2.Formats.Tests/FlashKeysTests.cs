using System.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>korFlash.xml rows as NiGFxLocalizeManager::ParseStringXml picks them: by locale, later rows win, features ignored.</summary>
public class FlashKeysTests
{
    const string Xml = "<ms2>"
        + "<key id=\"$s_a\" value=\"first\" />"
        + "<key id=\"$s_a\" value=\"na only\" locale=\"NA\" />"
        + "<key id=\"$s_b\" value=\"x\\ny\" feature=\"SomeFeature\" />"
        + "<key id=\"$s_c\" value=\"kr only\" locale=\"KR\" />"
        + "</ms2>";

    [Test]
    public void ARowForAnotherLocaleIsSkipped()
    {
        var kr = GameStrings.FlashKeys(Encoding.UTF8.GetBytes(Xml), "KR");
        Assert.That(kr["$s_a"], Is.EqualTo("first"), "the NA row does not replace the general one");
        Assert.That(kr["$s_c"], Is.EqualTo("kr only"));
        var na = GameStrings.FlashKeys(Encoding.UTF8.GetBytes(Xml), "na");
        Assert.That(na["$s_a"], Is.EqualTo("na only"), "the later row of the client's locale wins, case-insensitive");
        Assert.That(na.ContainsKey("$s_c"), Is.False);
    }

    [Test]
    public void TheFeatureAttributeIsNotLookedAtAndEscapesBecomeLineBreaks()
    {
        Assert.That(GameStrings.FlashKeys(Encoding.UTF8.GetBytes(Xml), "KR")["$s_b"], Is.EqualTo("x\ny"));
    }

    [Test]
    public void AnEmptyLocaleAppliesAndOnlyTheWholeNameMatches()
    {
        var m = GameStrings.FlashKeys(Encoding.UTF8.GetBytes(
            "<ms2><key id=\"a\" value=\"all\" locale=\"\" /><key id=\"b\" value=\"na2\" locale=\"NA2\" /><key id=\"c\" value=\"na\" locale=\"NA\" /></ms2>"), "NA");
        Assert.That(m["a"], Is.EqualTo("all"), "an empty locale attribute is no filter");
        Assert.That(m.ContainsKey("b"), Is.False, "NA2 is not NA");
        Assert.That(m["c"], Is.EqualTo("na"));
        Assert.That(GameStrings.FlashKeys(Encoding.UTF8.GetBytes("<ms2><key id=\"b\" value=\"na2\" locale=\"NA2\" /></ms2>"), "NA2")["b"], Is.EqualTo("na2"));
    }

    [Test]
    public void ARowWithoutValueIsItsOwnKeyAndAnEmptyIdIsAdded()
    {
        var m = GameStrings.FlashKeys(Encoding.UTF8.GetBytes("<ms2><key id=\"$s_x\" /><key id=\"\" value=\"blank\" /><key value=\"again\" /></ms2>"), "KR");
        Assert.That(m["$s_x"], Is.EqualTo("$s_x"));
        Assert.That(m[""], Is.EqualTo("again"), "no id keeps the previous row's key, the later text replaces");
    }

    [Test]
    public void ThePathOfTheSubtitleTablesReadsTheNameAttribute()
    {
        // usmSubtitleText.xml and stringMovie.xml carry their text in "name" (LoadStringTables parses them with "name")
        const string movie = "<ms2><key id=\"s_Cut_Blackstar_Crash_01\" name=\"a\\nb\" npc=\"x\" /><key id=\"s_v\" value=\"only value\" /></ms2>";
        var m = GameStrings.FlashKeys(Encoding.UTF8.GetBytes(movie), "KR", "name");
        Assert.That(m["s_Cut_Blackstar_Crash_01"], Is.EqualTo("a\nb"));
        Assert.That(m["s_v"], Is.EqualTo("s_v"), "a row without the attribute is its own key");
    }

    [Test]
    public void ALocalisationMarkerTakesTheKrFoldersText()
    {
        var en = GameStrings.FlashKeys(Encoding.UTF8.GetBytes("<ms2>"
            + "<key id=\"$s_inventory_unlimited_enchant\" value=\"KORFLASH_$S_INVENTORY_UNLIMITED_ENCHANT_VALUE:[F]UnlimitedEnchant\" feature=\"UnlimitedEnchant\" />"
            + "<key id=\"$s_both\" value=\"KORFLASH_$S_BOTH_VALUE\" />"
            + "<key id=\"$s_kept\" value=\"English\" />"
            + "<key id=\"$s_only_here\" value=\"KORFLASH_$S_ONLY_HERE_VALUE\" />"
            + "</ms2>"), "KR");
        var kr = GameStrings.FlashKeys(Encoding.UTF8.GetBytes("<ms2>"
            + "<key id=\"$s_inventory_unlimited_enchant\" value=\"Limit Break\" />"
            + "<key id=\"$s_both\" value=\"KORFLASH_$S_BOTH_VALUE\" />"
            + "<key id=\"$s_kept\" value=\"Korean\" />"
            + "</ms2>"), "KR");
        GameStrings.FillUntranslated(en, kr);
        Assert.That(en["$s_inventory_unlimited_enchant"], Is.EqualTo("Limit Break"), "the marker is not shown");
        Assert.That(en["$s_kept"], Is.EqualTo("English"), "a real text of the language stays");
        Assert.That(en["$s_both"], Does.StartWith("KORFLASH_"), "a marker in both stays as the language has it");
        Assert.That(en["$s_only_here"], Does.StartWith("KORFLASH_"), "a key the kr folder lacks stays as the language has it");
    }

    /// <summary>The real data: no language's UI table shows the marker for a key the kr folder has real text for.</summary>
    [Test]
    public void NoLanguageOfTheDataShowsAMarkerWhereKrHasText()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new Ms2.Formats.Pack.PackSet(d);
        string locale = Ms2.Ui.LobbyFeatures.LocaleOf(packs);
        var kr = GameStrings.FlashTable(packs, "kr", locale);
        int checkedLanguages = 0;
        foreach (var lang in new[] { "jp", "en", "cn", "fr", "de", "pt", "es" })
        {
            if (!packs.Exists("Xml", $"string/{lang}/korflash.xml")) continue;
            checkedLanguages++;
            var table = GameStrings.FlashTable(packs, lang, locale);
            var shown = table.Where(kv => GameStrings.IsUntranslated(kv.Value) && kr.TryGetValue(kv.Key, out var t) && !GameStrings.IsUntranslated(t))
                .Select(kv => kv.Key).ToList();
            Assert.That(shown, Is.Empty, $"{lang}: markers shown for keys kr has text for");
        }
        if (checkedLanguages == 0) Assert.Ignore("the data has no other language folder with korflash.xml");
    }
}
